using System.Collections;
using System.Collections.Generic;
using Convai.Scripts.Runtime.Addons;
using Convai.Scripts.Runtime.Core;
using Convai.Scripts.Runtime.Features;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
#endif

namespace Game.Debate
{
    public class InteractiveNpcDebateController : MonoBehaviour
    {
        public const bool DefaultUseConvaiVoiceCommandInput = true;
        public const bool DefaultShowTypedCommandPanel = false;
        public const bool DefaultDisableTalkDurationLimit = true;
        public static readonly Vector2 CommandPanelAnchorMin = Vector2.zero;
        public static readonly Vector2 CommandPanelAnchorMax = new(0.42f, 0.4f);

        private sealed class SavedAudio
        {
            public AudioClip Clip;
            public string Transcript;
        }

        [Header("Conversation")]
        [SerializeField] private NPC2NPCConversationManager conversationManager;
        [SerializeField] private ConvaiGroupNPCController firstNPC;
        [SerializeField] private ConvaiGroupNPCController secondNPC;
        [SerializeField]
        private string debateTopic =
            "Reading and speaking, which is more important in learning English?";

        [Header("UI")]
        [SerializeField] private TMP_Text statusText;
        [SerializeField] private InteractiveDebateTranscriptBridge transcriptBridge;
        [SerializeField] private string waitingText = "Waiting for an NPC response...";
        [SerializeField] private bool unlockCursorWhilePaused = true;

        [Header("Natural Language Commands")]
        [SerializeField] private bool enableNaturalLanguageCommands = true;
        [SerializeField] private bool useConvaiVoiceCommandInput = DefaultUseConvaiVoiceCommandInput;
        [SerializeField] private bool showTypedCommandPanel = DefaultShowTypedCommandPanel;
        [SerializeField] private bool disableTalkDurationLimit = DefaultDisableTalkDurationLimit;
        [SerializeField] private string participantId = "PILOT";
        [SerializeField] private string condition = "interactive_npc";
        [SerializeField] private string topicId = "reading_vs_speaking";
        [SerializeField] private string commandStage = "interactive_rewrite";
        [SerializeField] private string openAIModel = "gpt-4o-mini";
        [SerializeField] private string openAIBaseUrl = "https://api.meding.site";
        [SerializeField] private string openAIApiKeyOverride = "";
        [SerializeField] private float commandParserTimeoutSeconds = 10f;

        private readonly List<SavedAudio> _firstAudioBuffer = new();
        private readonly List<SavedAudio> _secondAudioBuffer = new();
        private readonly List<SavedAudio> _pendingAudio = new();

        private ConvaiGroupNPCController _pendingSpeaker;
        private string _pendingTranscript;
        private ConvaiNPC _activeNpcBeforeCommandMode;
        private GameObject _commandCanvasRoot;
        private GameObject _commandPanelRoot;
        private TMP_Text _commandPanelStatusText;
        private TMP_InputField _commandInputField;
        private DebateCommandParser _commandParser;
        private DebateCommandLogger _commandLogger;
        private bool _isReplaying;
        private bool _isRequestingVariant;
        private bool _isParsingCommand;
        private bool _firstNpcSubscribed;
        private bool _secondNpcSubscribed;
        private bool _pendingTranscriptIsRevised;
        private int _modificationCount;
        private int _practiceTurnId;

        public bool IsPaused { get; private set; }

        private void OnEnable()
        {
            RegisterCommandInputInterceptors();
        }

        private IEnumerator Start()
        {
            _commandParser = new DebateCommandParser(
                openAIModel,
                commandParserTimeoutSeconds,
                openAIBaseUrl,
                openAIApiKeyOverride);
            _commandLogger = new DebateCommandLogger();
            transcriptBridge = transcriptBridge != null
                ? transcriptBridge
                : GetComponent<InteractiveDebateTranscriptBridge>();
            EnsureCommandInputPanel();
            RegisterCommandInputInterceptors();

            if (conversationManager != null)
            {
                conversationManager.RelayInterceptor = InterceptRelay;
            }

            SetStatus(waitingText);
            SetCommandPanelStatus("Wait for an NPC response. After pause, hold T and speak a strategy command.");

            while (!_firstNpcSubscribed || !_secondNpcSubscribed)
            {
                _firstNpcSubscribed = TrySubscribeToAudio(
                    firstNPC,
                    CaptureFirstNpcAudio,
                    HandleFirstNpcTalkingChanged,
                    _firstNpcSubscribed);
                _secondNpcSubscribed = TrySubscribeToAudio(
                    secondNPC,
                    CaptureSecondNpcAudio,
                    HandleSecondNpcTalkingChanged,
                    _secondNpcSubscribed);

                if (!_firstNpcSubscribed || !_secondNpcSubscribed)
                {
                    yield return null;
                }
            }
        }

        private void OnDestroy()
        {
            if (conversationManager != null && conversationManager.RelayInterceptor == InterceptRelay)
            {
                conversationManager.RelayInterceptor = null;
            }

            UnsubscribeFromAudio(
                firstNPC,
                CaptureFirstNpcAudio,
                HandleFirstNpcTalkingChanged,
                _firstNpcSubscribed);
            UnsubscribeFromAudio(
                secondNPC,
                CaptureSecondNpcAudio,
                HandleSecondNpcTalkingChanged,
                _secondNpcSubscribed);

            UnregisterCommandInputInterceptors();
            if (_commandCanvasRoot != null)
            {
                Destroy(_commandCanvasRoot);
                _commandCanvasRoot = null;
            }
        }

        private void Update()
        {
            if (!enableNaturalLanguageCommands)
            {
                return;
            }

            if (!useConvaiVoiceCommandInput && WasCommandFocusPressed())
            {
                TryFocusCommandInput();
            }

            if (WasCommandCancelPressed())
            {
                CancelCommandInput();
            }
        }

        public void ReplayLastResponse()
        {
            if (!CanUsePendingResponse() || _isReplaying)
            {
                SetStatus("No completed response is available to replay.");
                return;
            }

            TryFinalizePendingAudio(_pendingSpeaker);
            if (_pendingAudio.Count == 0)
            {
                string status = _pendingSpeaker.ConvaiNPC.IsCharacterTalking
                    ? "Wait for the response audio to finish before replaying it."
                    : "No completed response audio is available to replay.";
                SetStatus(status);
                return;
            }

            StartCoroutine(ReplayPendingAudio());
        }

        public void MakeMorePolite()
        {
            RequestVariant(
                "Rewrite the response below as a more polite and less direct disagreement. " +
                "Preserve the original meaning, keep it concise, and output only the revised response.");
        }

        public void ShowBetterArgument()
        {
            RequestVariant(
                "Rewrite the response below as a stronger and clearer debate argument. " +
                "Add one specific reason or example, keep it concise, and output only the improved response.");
        }

        public void ShowBadExample()
        {
            RequestVariant(
                "Create a clearly inappropriate classroom debate example based on the response below. " +
                "Make it unnecessarily rude and too direct, but do not use profanity, slurs, threats, or hateful language. " +
                "Output only the bad example.");
        }

        public void UseLogosAppeal()
        {
            RequestVariant(
                "Rewrite the response below using Logos, a logical appeal. " +
                "Prioritize clear reasons, evidence, cause-and-effect relationships, tradeoffs, and practical solutions. " +
                "Keep the tone rational, specific, and well structured. Avoid vague emotional claims. " +
                "Output only the revised response.");
        }

        public void UseEthosAppeal()
        {
            RequestVariant(
                "Rewrite the response below using Ethos, a credibility and ethical appeal. " +
                "Emphasize fairness, responsibility, honesty, professional credibility, and a balanced position. " +
                "Acknowledge reasonable concerns from different stakeholders. Keep the tone principled, trustworthy, and steady. " +
                "Output only the revised response.");
        }

        public void UsePathosAppeal()
        {
            RequestVariant(
                "Rewrite the response below using Pathos, an emotional appeal. " +
                "Emphasize empathy, real pressure, a sense of fairness, and concern about future consequences. " +
                "Make the response more humane and emotionally resonant, but do not exaggerate or manipulate emotions. " +
                "Output only the revised response.");
        }

        public void ContinueDebate()
        {
            if (!CanUsePendingResponse() || conversationManager == null)
            {
                SetStatus("There is no paused response to continue from.");
                return;
            }

            ConvaiGroupNPCController speaker = _pendingSpeaker;
            string transcript = _pendingTranscript;
            bool wasRevised = _pendingTranscriptIsRevised;

            IsPaused = false;
            RestoreActiveNpcAfterCommandMode();
            _pendingSpeaker = null;
            _pendingTranscript = string.Empty;
            _pendingTranscriptIsRevised = false;
            _pendingAudio.Clear();
            SetActionCursor(false);
            SetStatus(wasRevised ? "Debate continuing with the revised response..." : "Debate continuing...");

            conversationManager.RelayMessageWithoutInterception(transcript, speaker);
        }

        private bool InterceptRelay(string message, ConvaiGroupNPCController sender)
        {
            bool wasRequestingVariant = _isRequestingVariant;
            if (!wasRequestingVariant)
            {
                _practiceTurnId++;
            }

            _pendingSpeaker = sender;
            _pendingTranscript = message?.Trim() ?? string.Empty;
            _pendingTranscriptIsRevised = wasRequestingVariant;
            IsPaused = true;
            _isParsingCommand = false;
            SetActionCursor(false);
            ClearCommandInput();
            transcriptBridge?.PublishNpcLine(sender, _pendingTranscript, wasRequestingVariant);

            _pendingAudio.Clear();

            string actionResult = wasRequestingVariant ? "Revised response ready" : "Debate paused";
            _isRequestingVariant = false;
            if (!TryFinalizePendingAudio(sender))
            {
                SetStatus(
                    $"{actionResult} after {GetSpeakerName(sender)}. " +
                    "Waiting for the response audio to finish...");
            }

            return true;
        }

        private void RequestVariant(string instruction, bool appendDebateContext = true)
        {
            if (!CanUsePendingResponse() || _isRequestingVariant)
            {
                SetStatus("Wait for the current response to finish.");
                return;
            }

            _isRequestingVariant = true;
            RestoreActiveNpcAfterCommandMode();
            SetActionCursor(false);
            _pendingAudio.Clear();
            GetAudioBuffer(_pendingSpeaker).Clear();

            string prompt = appendDebateContext
                ? $"{instruction}\n\nDebate topic: {debateTopic}\nOriginal response: \"{_pendingTranscript}\""
                : instruction;

            SetStatus($"{GetSpeakerName(_pendingSpeaker)} is preparing a revised response...");
            _pendingSpeaker.SendTextDataNPC2NPC(prompt);
        }

        private IEnumerator ProcessNaturalLanguageCommand(string commandText)
        {
            if (!CanUsePendingResponse())
            {
                yield break;
            }

            _isParsingCommand = true;
            SetActionCursor(false);
            SetCommandPanelStatus("Parsing command...");
            SetStatus("Parsing your strategy command...");

            DebateCommandParseResult parsed = null;
            DebateCommandParser parser = _commandParser ?? new DebateCommandParser(
                openAIModel,
                commandParserTimeoutSeconds,
                openAIBaseUrl,
                openAIApiKeyOverride);
            yield return parser.ParseCommand(commandText, debateTopic, result => parsed = result);

            if (!CanUsePendingResponse())
            {
                _isParsingCommand = false;
                SetCommandPanelStatus("Paused response changed. Try again after the next pause.");
                SetStatus("The paused response changed before the command finished parsing.");
                yield break;
            }

            parsed ??= DebateCommandParser.ParseWithLocalRules(commandText);
            string originalResponse = _pendingTranscript;
            string speakerName = GetSpeakerName(_pendingSpeaker);
            string prompt = DebateCommandPromptBuilder.BuildPrompt(parsed, debateTopic, originalResponse);
            transcriptBridge?.PublishLearnerCommand(commandText);

            _modificationCount++;
            (_commandLogger ??= new DebateCommandLogger()).LogCommand(
                participantId,
                condition,
                topicId,
                commandStage,
                commandText,
                parsed,
                _modificationCount,
                speakerName,
                _practiceTurnId,
                originalResponse);

            _isParsingCommand = false;
            SetCommandPanelStatus(
                $"Parsed: {parsed.StrategyDimensionLabel} / {parsed.OperationLabel} / {parsed.TargetMoveLabel}");
            SetStatus(
                $"Parsed: {parsed.StrategyDimensionLabel} / {parsed.OperationLabel} / {parsed.TargetMoveLabel}. " +
                $"{speakerName} is preparing a revised response...");
            RequestVariant(prompt, false);
        }

        private bool TryHandleCommandSubmission(string input)
        {
            if (!IsCommandModeAvailable())
            {
                return false;
            }

            if (_isParsingCommand || _isRequestingVariant)
            {
                SetCommandPanelStatus("A revised response is already being prepared.");
                SetStatus("A revised response is already being prepared.");
                return true;
            }

            string commandText = input?.Trim();
            if (string.IsNullOrWhiteSpace(commandText))
            {
                SetCommandPanelStatus("Type a strategy command, then press Enter.");
                SetStatus("Type a strategy command, then press Enter.");
                return true;
            }

            StartCoroutine(ProcessNaturalLanguageCommand(commandText));
            return true;
        }

        private bool TryFocusCommandInput()
        {
            if (!IsCommandModeAvailable())
            {
                EnsureCommandInputPanel();
                SetCommandPanelStatus("Wait until the NPC response is paused, then press T.");
                return false;
            }

            if (_isParsingCommand || _isRequestingVariant)
            {
                SetStatus("Wait for the current revised response to finish.");
                return true;
            }

            EnsureCommandInputPanel();
            _commandInputField = FindConvaiInputField();
            if (_commandInputField == null)
            {
                SetStatus("Could not find the Convai text input field in this scene.");
                return true;
            }

            _commandInputField.text = string.Empty;
            _commandInputField.interactable = true;
            _commandInputField.Select();
            _commandInputField.ActivateInputField();
            SetActionCursor(true);
            SetCommandPanelStatus("Type a strategy command, then press Enter.");
            SetStatus("Type a strategy command, then press Enter. Press Esc to cancel.");
            StartCoroutine(ClearCommandInputNextFrame());
            return true;
        }

        private void SubmitCommandFromVisibleInput(string input)
        {
            if (!enableNaturalLanguageCommands)
            {
                return;
            }

            if (TryHandleCommandSubmission(input))
            {
                ClearCommandInput();
            }
        }

        private bool TryHandleVoiceCommandTranscript(string transcript)
        {
            if (!useConvaiVoiceCommandInput || !IsCommandModeAvailable())
            {
                return false;
            }

            string commandText = transcript?.Trim();
            if (string.IsNullOrWhiteSpace(commandText))
            {
                return true;
            }

            SetCommandPanelStatus("Voice strategy command: " + commandText);
            return TryHandleCommandSubmission(commandText);
        }

        private void CancelCommandInput()
        {
            if (_commandInputField == null || !_commandInputField.isFocused || !IsCommandModeAvailable())
            {
                return;
            }

            ClearCommandInput();
            SetActionCursor(true);
            SetCommandPanelStatus("Command canceled. Press T to type again.");
            SetStatus("Command input canceled. Choose an action or press T to type a strategy command.");
        }

        private void ClearCommandInput()
        {
            if (_commandInputField == null)
            {
                return;
            }

            _commandInputField.text = string.Empty;
            _commandInputField.DeactivateInputField();
        }

        private TMP_InputField FindConvaiInputField()
        {
            if (_commandInputField != null)
            {
                return _commandInputField;
            }

            TMP_InputField inputField = TryFindInputField(firstNPC);
            if (inputField != null)
            {
                return inputField;
            }

            inputField = TryFindInputField(secondNPC);
            if (inputField != null)
            {
                return inputField;
            }

            TMP_InputField[] inputFields = FindObjectsOfType<TMP_InputField>(true);
            for (int i = 0; i < inputFields.Length; i++)
            {
                if (inputFields[i] != null && inputFields[i].interactable)
                {
                    return inputFields[i];
                }
            }

            return null;
        }

        private void EnsureCommandInputPanel()
        {
            if (!enableNaturalLanguageCommands || !showTypedCommandPanel || _commandPanelRoot != null)
            {
                return;
            }

            EnsureEventSystem();

            GameObject canvasObject = new("Interactive Debate Command Canvas");
            _commandCanvasRoot = canvasObject;
            Canvas canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 1000;
            canvasObject.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            canvasObject.AddComponent<GraphicRaycaster>();

            GameObject panelObject = new("Command Input Panel");
            panelObject.transform.SetParent(canvasObject.transform, false);
            _commandPanelRoot = panelObject;

            RectTransform panelRect = panelObject.AddComponent<RectTransform>();
            panelRect.anchorMin = CommandPanelAnchorMin;
            panelRect.anchorMax = CommandPanelAnchorMax;
            panelRect.offsetMin = new Vector2(18f, 18f);
            panelRect.offsetMax = new Vector2(-18f, -18f);

            Image panelImage = panelObject.AddComponent<Image>();
            panelImage.color = new Color(0.05f, 0.06f, 0.07f, 0.82f);

            VerticalLayoutGroup layout = panelObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(12, 12, 10, 12);
            layout.spacing = 8f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            TMP_Text titleText = CreatePanelText(panelObject.transform, "Command Title", "Strategy command", 18, FontStyles.Bold);
            titleText.color = Color.white;

            _commandPanelStatusText = CreatePanelText(
                panelObject.transform,
                "Command Status",
                "Wait for an NPC response. Press T after the debate pauses.",
                14,
                FontStyles.Normal);
            _commandPanelStatusText.color = new Color(0.82f, 0.88f, 0.95f, 1f);

            _commandInputField = CreateCommandInputField(panelObject.transform);
            LayoutElement inputLayout = _commandInputField.gameObject.AddComponent<LayoutElement>();
            inputLayout.minHeight = 48f;
            inputLayout.preferredHeight = 58f;
        }

        private static TMP_Text CreatePanelText(
            Transform parent,
            string objectName,
            string text,
            int fontSize,
            FontStyles fontStyle)
        {
            GameObject textObject = new(objectName);
            textObject.transform.SetParent(parent, false);
            TMP_Text tmpText = textObject.AddComponent<TextMeshProUGUI>();
            tmpText.text = text;
            tmpText.fontSize = fontSize;
            tmpText.fontStyle = fontStyle;
            tmpText.enableWordWrapping = true;
            tmpText.overflowMode = TextOverflowModes.Ellipsis;
            return tmpText;
        }

        private TMP_InputField CreateCommandInputField(Transform parent)
        {
            GameObject inputObject = new("Command Input Field");
            inputObject.transform.SetParent(parent, false);

            Image background = inputObject.AddComponent<Image>();
            background.color = new Color(1f, 1f, 1f, 0.94f);

            TMP_InputField inputField = inputObject.AddComponent<TMP_InputField>();
            inputField.lineType = TMP_InputField.LineType.SingleLine;
            inputField.onSubmit.AddListener(SubmitCommandFromVisibleInput);

            RectTransform inputRect = inputObject.GetComponent<RectTransform>();
            inputRect.sizeDelta = new Vector2(0f, 58f);

            TMP_Text placeholder = CreateInputChildText(
                inputObject.transform,
                "Placeholder",
                "e.g., add stronger evidence",
                new Color(0.38f, 0.42f, 0.48f, 0.82f),
                FontStyles.Italic);
            TMP_Text text = CreateInputChildText(
                inputObject.transform,
                "Text",
                string.Empty,
                new Color(0.05f, 0.06f, 0.07f, 1f),
                FontStyles.Normal);

            inputField.placeholder = placeholder;
            inputField.textComponent = text;
            inputField.targetGraphic = background;
            return inputField;
        }

        private static TMP_Text CreateInputChildText(
            Transform parent,
            string objectName,
            string text,
            Color color,
            FontStyles fontStyle)
        {
            GameObject textObject = new(objectName);
            textObject.transform.SetParent(parent, false);
            RectTransform rect = textObject.AddComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(12f, 6f);
            rect.offsetMax = new Vector2(-12f, -6f);

            TMP_Text tmpText = textObject.AddComponent<TextMeshProUGUI>();
            tmpText.text = text;
            tmpText.color = color;
            tmpText.fontSize = 16f;
            tmpText.fontStyle = fontStyle;
            tmpText.alignment = TextAlignmentOptions.MidlineLeft;
            tmpText.enableWordWrapping = false;
            tmpText.overflowMode = TextOverflowModes.Ellipsis;
            return tmpText;
        }

        private static void EnsureEventSystem()
        {
            if (FindObjectOfType<EventSystem>() != null)
            {
                return;
            }

            GameObject eventSystemObject = new("EventSystem");
            eventSystemObject.AddComponent<EventSystem>();
#if ENABLE_INPUT_SYSTEM
            eventSystemObject.AddComponent<InputSystemUIInputModule>();
#else
            eventSystemObject.AddComponent<StandaloneInputModule>();
#endif
        }

        private IEnumerator ClearCommandInputNextFrame()
        {
            yield return null;
            if (_commandInputField != null && _commandInputField.isFocused)
            {
                _commandInputField.text = string.Empty;
            }
        }

        private void SetCommandPanelStatus(string message)
        {
            if (_commandPanelStatusText != null)
            {
                _commandPanelStatusText.text = message;
            }
        }

        private TMP_InputField TryFindInputField(ConvaiGroupNPCController controller)
        {
            if (controller == null || controller.ConvaiNPC == null || controller.ConvaiNPC.playerInteractionManager == null)
            {
                return null;
            }

            return controller.ConvaiNPC.playerInteractionManager.FindActiveInputField();
        }

        private void RegisterCommandInputInterceptors()
        {
            if (!Application.isPlaying || !enableNaturalLanguageCommands)
            {
                return;
            }

            ConvaiPlayerInteractionManager.TryHandleTextSubmission = TryHandleCommandSubmission;
            ConvaiPlayerInteractionManager.ShouldSuppressChatToggle = ShouldSuppressConvaiChatToggle;
            ConvaiPlayerInteractionManager.ShouldSuppressTalkInput = ShouldSuppressConvaiTalkInput;
            ConvaiPlayerInteractionManager.ShouldSuppressNpcInteraction = ShouldSuppressConvaiNpcInteraction;
            ConvaiInputManager.ShouldSuppressTalkInput = ShouldSuppressConvaiTalkInput;
            ConvaiGRPCAPI.TryHandleUserVoiceTranscript = TryHandleVoiceCommandTranscript;
            TalkButtonDurationChecker.ShouldDisableTalkDurationLimit = ShouldDisableTalkDurationLimit;
        }

        private void UnregisterCommandInputInterceptors()
        {
            if (ConvaiPlayerInteractionManager.TryHandleTextSubmission == (System.Func<string, bool>)TryHandleCommandSubmission)
            {
                ConvaiPlayerInteractionManager.TryHandleTextSubmission = null;
            }

            if (ConvaiPlayerInteractionManager.ShouldSuppressChatToggle == (System.Func<bool>)ShouldSuppressConvaiChatToggle)
            {
                ConvaiPlayerInteractionManager.ShouldSuppressChatToggle = null;
            }

            if (ConvaiPlayerInteractionManager.ShouldSuppressTalkInput == (System.Func<bool>)ShouldSuppressConvaiTalkInput)
            {
                ConvaiPlayerInteractionManager.ShouldSuppressTalkInput = null;
            }

            if (ConvaiPlayerInteractionManager.ShouldSuppressNpcInteraction == (System.Func<bool>)ShouldSuppressConvaiNpcInteraction)
            {
                ConvaiPlayerInteractionManager.ShouldSuppressNpcInteraction = null;
            }

            if (ConvaiInputManager.ShouldSuppressTalkInput == (System.Func<bool>)ShouldSuppressConvaiTalkInput)
            {
                ConvaiInputManager.ShouldSuppressTalkInput = null;
            }

            if (ConvaiGRPCAPI.TryHandleUserVoiceTranscript == (System.Func<string, bool>)TryHandleVoiceCommandTranscript)
            {
                ConvaiGRPCAPI.TryHandleUserVoiceTranscript = null;
            }

            if (TalkButtonDurationChecker.ShouldDisableTalkDurationLimit == (System.Func<bool>)ShouldDisableTalkDurationLimit)
            {
                TalkButtonDurationChecker.ShouldDisableTalkDurationLimit = null;
            }
        }

        private bool ShouldSuppressConvaiChatToggle()
        {
            return IsCommandModeAvailable();
        }

        private bool ShouldSuppressConvaiTalkInput()
        {
            return IsCommandModeAvailable() && !useConvaiVoiceCommandInput;
        }

        private bool ShouldSuppressConvaiNpcInteraction()
        {
            return IsCommandModeAvailable();
        }

        private bool ShouldDisableTalkDurationLimit()
        {
            return disableTalkDurationLimit;
        }

        private bool IsCommandModeAvailable()
        {
            return enableNaturalLanguageCommands && CanUsePendingResponse();
        }

        private void ActivatePendingSpeakerForVoiceCommand(ConvaiGroupNPCController speaker)
        {
            if (!useConvaiVoiceCommandInput ||
                speaker == null ||
                speaker.ConvaiNPC == null ||
                ConvaiNPCManager.Instance == null)
            {
                return;
            }

            _activeNpcBeforeCommandMode ??= ConvaiNPCManager.Instance.activeConvaiNPC;
            ConvaiNPCManager.Instance.SetActiveConvaiNPC(speaker.ConvaiNPC, false);
        }

        private void RestoreActiveNpcAfterCommandMode()
        {
            if (!useConvaiVoiceCommandInput || ConvaiNPCManager.Instance == null)
            {
                return;
            }

            if (ConvaiNPCManager.Instance.activeConvaiNPC == _pendingSpeaker?.ConvaiNPC)
            {
                ConvaiNPCManager.Instance.SetActiveConvaiNPC(_activeNpcBeforeCommandMode, false);
            }

            _activeNpcBeforeCommandMode = null;
        }

        private static bool WasCommandFocusPressed()
        {
#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current != null && Keyboard.current.tKey.wasPressedThisFrame)
            {
                return true;
            }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetKeyDown(KeyCode.T);
#else
            return false;
#endif
        }

        private static bool WasCommandCancelPressed()
        {
#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                return true;
            }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetKeyDown(KeyCode.Escape);
#else
            return false;
#endif
        }

        private IEnumerator ReplayPendingAudio()
        {
            _isReplaying = true;
            SetStatus($"Replaying {GetSpeakerName(_pendingSpeaker)}'s last response...");

            ConvaiNPCAudioManager audioManager = _pendingSpeaker.ConvaiNPC.AudioManager;
            float duration = 0f;

            // Cached audio has no new streamed lip-sync frames, so release Convai's
            // post-response lip-sync wait before putting replay clips on its queue.
            audioManager.SetWaitForCharacterLipSync(false);

            foreach (SavedAudio saved in _pendingAudio)
            {
                if (saved.Clip == null)
                {
                    continue;
                }

                duration += saved.Clip.length;
                audioManager.AddResponseAudio(new ConvaiNPCAudioManager.ResponseAudio
                {
                    AudioClip = saved.Clip,
                    AudioTranscript = saved.Transcript,
                    IsFinal = false
                });
            }

            audioManager.AddResponseAudio(new ConvaiNPCAudioManager.ResponseAudio { IsFinal = true });

            float startTimeout = 3f;
            while (!_pendingSpeaker.ConvaiNPC.IsCharacterTalking && startTimeout > 0f)
            {
                startTimeout -= Time.deltaTime;
                yield return null;
            }

            if (!_pendingSpeaker.ConvaiNPC.IsCharacterTalking)
            {
                _isReplaying = false;
                SetStatus("Replay could not start. Try Replay Last Response again.");
                yield break;
            }

            float finishTimeout = duration + 3f;
            while (_pendingSpeaker.ConvaiNPC.IsCharacterTalking && finishTimeout > 0f)
            {
                finishTimeout -= Time.deltaTime;
                yield return null;
            }

            _isReplaying = false;
            SetActionCursor(true);
            SetStatus("Replay finished. Choose another action or continue.");
        }

        private void CaptureFirstNpcAudio(ConvaiNPCAudioManager.ResponseAudio response)
        {
            CaptureAudio(_firstAudioBuffer, response);
        }

        private void CaptureSecondNpcAudio(ConvaiNPCAudioManager.ResponseAudio response)
        {
            CaptureAudio(_secondAudioBuffer, response);
        }

        private void HandleFirstNpcTalkingChanged(bool isTalking)
        {
            HandleNpcTalkingChanged(firstNPC, isTalking);
        }

        private void HandleSecondNpcTalkingChanged(bool isTalking)
        {
            HandleNpcTalkingChanged(secondNPC, isTalking);
        }

        private void HandleNpcTalkingChanged(ConvaiGroupNPCController speaker, bool isTalking)
        {
            if (!isTalking && !_isReplaying && IsPaused && _pendingSpeaker == speaker)
            {
                TryFinalizePendingAudio(speaker);
            }
        }

        private void CaptureAudio(List<SavedAudio> buffer, ConvaiNPCAudioManager.ResponseAudio response)
        {
            if (_isReplaying || response == null || response.IsFinal || response.AudioClip == null)
            {
                return;
            }

            buffer.Add(new SavedAudio
            {
                Clip = response.AudioClip,
                Transcript = response.AudioTranscript
            });
        }

        private bool TryFinalizePendingAudio(ConvaiGroupNPCController speaker)
        {
            if (speaker == null || speaker.ConvaiNPC == null || speaker.ConvaiNPC.IsCharacterTalking)
            {
                return false;
            }

            List<SavedAudio> source = GetAudioBuffer(speaker);
            if (source.Count == 0)
            {
                return false;
            }

            _pendingAudio.Clear();
            _pendingAudio.AddRange(source);
            source.Clear();
            SetActionCursor(true);
            if (_pendingTranscriptIsRevised)
            {
                SetCommandPanelStatus("Revised response ready. Click Continue to send it to the next NPC.");
                SetStatus(
                    $"Revised response ready after {GetSpeakerName(speaker)}. " +
                    "Click Continue to continue the debate.");
            }
            else
            {
                ActivatePendingSpeakerForVoiceCommand(speaker);
                SetCommandPanelStatus(useConvaiVoiceCommandInput
                    ? "Hold T and speak a strategy command. Wait for the revised response, then click Continue."
                    : "Press T, type a strategy command, then press Enter. Wait for the revised response, then click Continue.");
                SetStatus(
                    $"Debate paused after {GetSpeakerName(speaker)}. " +
                    "Say a strategy command, wait for the revised response, then click Continue.");
            }
            return true;
        }

        private void SetActionCursor(bool canUseUi)
        {
            if (!unlockCursorWhilePaused)
            {
                return;
            }

            Cursor.lockState = canUseUi ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = canUseUi;
        }

        private bool TrySubscribeToAudio(
            ConvaiGroupNPCController controller,
            System.Action<ConvaiNPCAudioManager.ResponseAudio> audioHandler,
            System.Action<bool> talkingHandler,
            bool alreadySubscribed)
        {
            if (alreadySubscribed)
            {
                return true;
            }

            if (controller == null || controller.ConvaiNPC == null || controller.ConvaiNPC.AudioManager == null)
            {
                return false;
            }

            controller.ConvaiNPC.AudioManager.OnResponseAudioStarted += audioHandler;
            controller.ConvaiNPC.AudioManager.OnCharacterTalkingChanged += talkingHandler;
            return true;
        }

        private void UnsubscribeFromAudio(
            ConvaiGroupNPCController controller,
            System.Action<ConvaiNPCAudioManager.ResponseAudio> audioHandler,
            System.Action<bool> talkingHandler,
            bool wasSubscribed)
        {
            if (wasSubscribed &&
                controller != null &&
                controller.ConvaiNPC != null &&
                controller.ConvaiNPC.AudioManager != null)
            {
                controller.ConvaiNPC.AudioManager.OnResponseAudioStarted -= audioHandler;
                controller.ConvaiNPC.AudioManager.OnCharacterTalkingChanged -= talkingHandler;
            }
        }

        private List<SavedAudio> GetAudioBuffer(ConvaiGroupNPCController speaker)
        {
            return speaker == firstNPC ? _firstAudioBuffer : _secondAudioBuffer;
        }

        private bool CanUsePendingResponse()
        {
            return IsPaused && _pendingSpeaker != null && !string.IsNullOrWhiteSpace(_pendingTranscript);
        }

        private string GetSpeakerName(ConvaiGroupNPCController speaker)
        {
            if (speaker == null)
            {
                return "NPC";
            }

            return !string.IsNullOrWhiteSpace(speaker.CharacterName)
                ? speaker.CharacterName
                : speaker.gameObject.name;
        }

        private void SetStatus(string message)
        {
            if (statusText != null)
            {
                statusText.text = message;
            }
        }
    }
}
