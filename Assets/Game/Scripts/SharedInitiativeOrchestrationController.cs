using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Convai.Scripts.Runtime.Core;
using Convai.Scripts.Runtime.Features;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Debate
{
    public enum OrchestrationPhase
    {
        Intro,
        OpponentSpeaking,
        WaitingForPlayer,
        CoachGenerating,
        CoachSuggestionReady,
        Complete
    }

    [Serializable]
    public sealed class ConditionCSessionRecord
    {
        public string topic;
        public string learnerStance;
        public string taskGoal;
        public int completedTurns;
        public string latestLearnerNote;
        public string latestOpponentUtterance;
        public string latestCoachSuggestion;
    }

    public sealed class SharedInitiativeOrchestrationController : MonoBehaviour
    {
        public const bool DefaultAutomaticCoachAfterPlayerVoice = true;
        public const bool DefaultShowManualPauseButton = false;

        [Header("Agents")]
        [SerializeField] private NPC2NPCConversationManager conversationManager;
        [SerializeField] private ConvaiNPC conversationNPC;
        [SerializeField] private ConvaiNPC coachNPC;

        [Header("Task")]
        [SerializeField] private string participantId = "PILOT";
        [SerializeField] private string condition = "Condition C";
        [SerializeField] private string topicId = "reading_vs_speaking";
        [SerializeField]
        private string debateTopic =
            "Reading and speaking, which is more important in learning English?";
        [SerializeField] private string learnerStance = "Speaking is more important for learning English.";
        [SerializeField] private string taskGoal = "Practice debate with post-turn strategy coaching.";
        [SerializeField] private int maxConversationTurns = 3;
        [SerializeField] private string selectedStrategy = "Logos";
        [SerializeField] private string[] previousCommands = Array.Empty<string>();
        [SerializeField] private string[] previousNpcVersionsViewed = Array.Empty<string>();

        [TextArea(2, 6)]
        [SerializeField]
        private string npcOpeningPrompt =
            "The debate topic is: {0} You are the opposing side. Argue that reading is more important in learning English. Present one short opening argument, then wait for the learner to respond.";
        [TextArea(2, 6)]
        [SerializeField]
        private string npcFollowUpPrompt =
            "The learner has responded. Ask one concise follow-up question or rebuttal that challenges the claim that speaking is more important. Keep it under 20 seconds, then wait.";
        [TextArea(2, 4)]
        [SerializeField]
        private string closingText =
            "Good work. The guided practice debate is complete.";

        [Header("Coach")]
        [SerializeField] private bool automaticCoachAfterPlayerVoice = DefaultAutomaticCoachAfterPlayerVoice;
        [SerializeField] private bool showManualPauseButton = DefaultShowManualPauseButton;
        [SerializeField] private bool speakCoachFeedback;
        [SerializeField] private string openAIModel = "gpt-4o-mini";
        [SerializeField] private string openAIBaseUrl = "https://api.meding.site";
        [SerializeField] private string openAIApiKeyOverride = "";
        [SerializeField] private float coachResponseTimeoutSeconds = 10f;

        [Header("UI")]
        [SerializeField] private Canvas uiCanvas;
        [SerializeField] private GameObject legacyInteractiveControls;
        [SerializeField] private GameObject legacyStartButton;
        [SerializeField] private GameObject legacyRoundTimer;
        [SerializeField] private InteractiveDebateTranscriptBridge transcriptBridge;
        [SerializeField] private bool unlockCursorForControlUi = true;

        public ConditionCSessionRecord SessionRecord = new();
        public OrchestrationPhase Phase { get; private set; } = OrchestrationPhase.Intro;

        private readonly StringBuilder _opponentTranscript = new();
        private readonly List<CoachFeedbackLogRow> _pendingFeedbackRows = new();

        private DebateCoachFeedbackGenerator _coachGenerator;
        private DebateCoachLogger _coachLogger;
        private GameObject _root;
        private TMP_Text _titleText;
        private TMP_Text _taskText;
        private TMP_Text _statusText;
        private TMP_Text _playerText;
        private TMP_Text _coachText;
        private TMP_InputField _learnerNoteInput;
        private Button _startButton;
        private Button _pauseForCoachButton;
        private Button _continueButton;
        private Button _exampleButton;
        private Button _endButton;
        private Coroutine _coachRoutine;
        private int _turnIndex;
        private bool _exampleRequestedForCurrentTurn;
        private string _latestPlayerUtterance = string.Empty;
        private string _latestFeedbackText = string.Empty;

        private void Awake()
        {
            BuildUi();
            HideLegacyUi();
        }

        private void OnEnable()
        {
            RegisterVoiceInterceptor();
        }

        private void OnDisable()
        {
            UnregisterVoiceInterceptor();
        }

        private IEnumerator Start()
        {
            _coachGenerator = new DebateCoachFeedbackGenerator(
                openAIModel,
                coachResponseTimeoutSeconds,
                openAIBaseUrl,
                openAIApiKeyOverride);
            _coachLogger = new DebateCoachLogger();
            transcriptBridge = transcriptBridge != null
                ? transcriptBridge
                : GetComponent<InteractiveDebateTranscriptBridge>();

            SessionRecord.topic = debateTopic;
            SessionRecord.learnerStance = learnerStance;
            SessionRecord.taskGoal = taskGoal;

            if (conversationManager != null)
            {
                conversationManager.RelayInterceptor = null;
            }

            SubscribeToConversationAudio();
            RegisterVoiceInterceptor();

            yield return null;
            EnterIntro();
        }

        private void OnDestroy()
        {
            if (_coachRoutine != null)
            {
                StopCoroutine(_coachRoutine);
                _coachRoutine = null;
            }

            UnregisterVoiceInterceptor();
            UnsubscribeFromConversationAudio();
            SetControlCursor(false);
        }

        public void BeginConditionC()
        {
            if (Phase != OrchestrationPhase.Intro && Phase != OrchestrationPhase.Complete)
            {
                return;
            }

            if (conversationNPC == null)
            {
                SetStatus("Assign a conversation NPC before starting.");
                return;
            }

            _turnIndex = 0;
            _pendingFeedbackRows.Clear();
            _latestPlayerUtterance = string.Empty;
            _latestFeedbackText = string.Empty;
            SessionRecord.completedTurns = 0;
            SessionRecord.latestLearnerNote = string.Empty;
            SessionRecord.latestOpponentUtterance = string.Empty;
            SessionRecord.latestCoachSuggestion = string.Empty;
            _learnerNoteInput.text = string.Empty;
            _playerText.text = "Your response transcript will appear here after you speak.";
            _coachText.text = "Coach feedback will appear after your response.";

            StartNpcTurn(FormatTaskText(npcOpeningPrompt));
        }

        public void PauseForCoach()
        {
            if (Phase != OrchestrationPhase.WaitingForPlayer && Phase != OrchestrationPhase.OpponentSpeaking)
            {
                return;
            }

            string typedNote = _learnerNoteInput.text.Trim();
            if (string.IsNullOrWhiteSpace(typedNote))
            {
                SetStatus("Speak with T first, or type a note before manually requesting Coach feedback.");
                return;
            }

            HandlePlayerUtterance(typedNote, true);
        }

        public void ContinueConversation()
        {
            if (Phase != OrchestrationPhase.CoachSuggestionReady)
            {
                return;
            }

            FlushPendingFeedbackRows(DateTime.UtcNow.ToString("o"));

            if (_turnIndex >= maxConversationTurns)
            {
                StartCoroutine(RequestCoachFeedback(CoachFeedbackLevel.Summary, false));
                return;
            }

            _learnerNoteInput.text = string.Empty;
            string prompt = BuildFollowUpPrompt();
            StartNpcTurn(prompt);
        }

        public void RequestExample()
        {
            if (Phase != OrchestrationPhase.CoachSuggestionReady || _exampleRequestedForCurrentTurn)
            {
                return;
            }

            _exampleRequestedForCurrentTurn = true;
            StartCoroutine(RequestCoachFeedback(CoachFeedbackLevel.Level3, true));
        }

        public void AskCoachAgain()
        {
            RequestExample();
        }

        public void EndConditionC()
        {
            FlushPendingFeedbackRows(string.Empty);
            Phase = OrchestrationPhase.Complete;
            ConvaiNPCManager.Instance?.SetActiveConvaiNPC(coachNPC != null ? coachNPC : conversationNPC);
            SetControlCursor(true);
            SetStatus(closingText);
            _coachText.text = "Session complete.";
            SetButtonsForPhase();
        }

        public void AdvanceFormalDebate()
        {
            ContinueConversation();
        }

        private void StartNpcTurn(string prompt)
        {
            Phase = OrchestrationPhase.OpponentSpeaking;
            _turnIndex++;
            _opponentTranscript.Clear();
            _latestPlayerUtterance = string.Empty;
            _latestFeedbackText = string.Empty;
            _exampleRequestedForCurrentTurn = false;
            ConvaiNPCManager.Instance?.SetActiveConvaiNPC(conversationNPC);
            SetControlCursor(false);
            SetStatus($"Turn {_turnIndex}: listen to the opponent, then hold T and answer by voice.");
            SetButtonsForPhase();
            conversationNPC.SendTextDataAsync(prompt);
        }

        private bool TryHandlePlayerVoiceTranscript(string transcript)
        {
            if (!automaticCoachAfterPlayerVoice || !CanHandlePlayerVoiceTranscript())
            {
                return false;
            }

            string safeTranscript = transcript?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(safeTranscript))
            {
                return true;
            }

            FinalizeOpponentTurnForPlayerVoice();
            HandlePlayerUtterance(safeTranscript, false);
            return true;
        }

        private bool CanHandlePlayerVoiceTranscript()
        {
            return Phase == OrchestrationPhase.WaitingForPlayer ||
                   Phase == OrchestrationPhase.OpponentSpeaking;
        }

        private void FinalizeOpponentTurnForPlayerVoice()
        {
            if (Phase != OrchestrationPhase.OpponentSpeaking)
            {
                return;
            }

            SessionRecord.latestOpponentUtterance = _opponentTranscript.ToString().Trim();
            Phase = OrchestrationPhase.WaitingForPlayer;
            Debug.Log("SharedInitiativeOrchestrationController accepted player voice while the opponent-speaking phase was still active.");
        }

        private void HandlePlayerUtterance(string transcript, bool fromTypedFallback)
        {
            _latestPlayerUtterance = transcript.Trim();
            SessionRecord.completedTurns = Mathf.Max(SessionRecord.completedTurns, _turnIndex);
            SessionRecord.latestLearnerNote = _latestPlayerUtterance;
            _learnerNoteInput.text = _latestPlayerUtterance;
            _playerText.text = "You: " + _latestPlayerUtterance;
            transcriptBridge?.PublishPlayerUtterance(_latestPlayerUtterance);
            SetStatus(fromTypedFallback ? "Coach is reading your typed note..." : "Voice captured. Coach is preparing feedback...");
            StartCoroutine(RequestCoachFeedback(CoachFeedbackLevel.Level2, false));
        }

        private IEnumerator RequestCoachFeedback(CoachFeedbackLevel level, bool exampleRequested)
        {
            Phase = OrchestrationPhase.CoachGenerating;
            SetControlCursor(true);
            SetButtonsForPhase();
            ConvaiNPCManager.Instance?.SetActiveConvaiNPC(coachNPC);

            CoachFeedbackRequest request = BuildCoachRequest(level);
            _coachText.text = level == CoachFeedbackLevel.Summary
                ? "Coach is preparing your practice summary..."
                : level == CoachFeedbackLevel.Level3
                    ? "Coach is preparing a short example frame..."
                    : "Coach is thinking...";

            CoachFeedbackResult result = null;
            yield return (_coachGenerator ??= new DebateCoachFeedbackGenerator(
                    openAIModel,
                    coachResponseTimeoutSeconds,
                    openAIBaseUrl,
                    openAIApiKeyOverride))
                .GenerateFeedback(request, feedback => result = feedback);

            result ??= DebateCoachFeedbackGenerator.BuildLocalFallback(request);
            ApplyCoachFeedback(request, result, exampleRequested);
        }

        private void ApplyCoachFeedback(CoachFeedbackRequest request, CoachFeedbackResult result, bool exampleRequested)
        {
            string feedback = result.FeedbackText?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(feedback))
            {
                result = DebateCoachFeedbackGenerator.BuildLocalFallback(request);
                feedback = result.FeedbackText;
            }

            SessionRecord.latestCoachSuggestion = feedback;
            _latestFeedbackText = feedback;
            _coachText.text = feedback;
            transcriptBridge?.PublishCoachLine(feedback);

            CoachFeedbackLogRow row = BuildLogRow(request, result, exampleRequested);
            if (result.FeedbackLevel == CoachFeedbackLevel.Summary)
            {
                (_coachLogger ??= new DebateCoachLogger()).LogFeedback(row);
                Phase = OrchestrationPhase.Complete;
                SetStatus("Practice debate complete. Coach summary is ready.");
            }
            else
            {
                _pendingFeedbackRows.Add(row);
                Phase = OrchestrationPhase.CoachSuggestionReady;
                SetStatus(result.FeedbackLevel == CoachFeedbackLevel.Level3
                    ? "Example frame ready. Press Continue when you are ready for the next opponent turn."
                    : "Coach feedback is ready. You can request an example or press Continue.");
            }

            if (speakCoachFeedback && coachNPC != null)
            {
                coachNPC.SendTextDataAsync(DebateLearningContent.GetRepeatExactlyPrompt(feedback));
            }

            SetButtonsForPhase();
        }

        private CoachFeedbackRequest BuildCoachRequest(CoachFeedbackLevel level)
        {
            return new CoachFeedbackRequest
            {
                Condition = condition,
                Stage = "Practice Debate",
                TopicId = topicId,
                Topic = debateTopic,
                PlayerSide = learnerStance,
                TurnId = _turnIndex,
                OpponentUtteranceText = SessionRecord.latestOpponentUtterance,
                PlayerUtteranceText = _latestPlayerUtterance,
                SelectedStrategy = selectedStrategy,
                PreviousCommands = previousCommands ?? Array.Empty<string>(),
                PreviousNpcVersionsViewed = previousNpcVersionsViewed ?? Array.Empty<string>(),
                FeedbackLevel = level
            };
        }

        private CoachFeedbackLogRow BuildLogRow(
            CoachFeedbackRequest request,
            CoachFeedbackResult result,
            bool exampleRequested)
        {
            return new CoachFeedbackLogRow
            {
                ParticipantId = participantId,
                Condition = request.Condition,
                Stage = request.Stage,
                TopicId = request.TopicId,
                TurnId = request.TurnId,
                PlayerSide = request.PlayerSide,
                OpponentUtteranceText = request.OpponentUtteranceText,
                PlayerUtteranceText = request.PlayerUtteranceText,
                SelectedStrategy = request.SelectedStrategy,
                PreviousCommandCount = request.PreviousCommands?.Length ?? 0,
                PreviousCommandTypes = string.Join(";", request.PreviousCommands ?? Array.Empty<string>()),
                CoachTriggered = true,
                CoachFeedbackLevel = result.FeedbackLevel.ToString(),
                CoachFeedbackType = result.FeedbackType,
                CoachStrongComponent = result.StrongComponent,
                CoachWeakComponent = result.WeakComponent,
                CoachDominantStrategy = result.DominantStrategy,
                CoachRecommendedStrategy = result.RecommendedStrategy,
                CoachNextAction = result.NextAction,
                CoachFeedbackText = result.FeedbackText,
                ExampleRequested = exampleRequested,
                TimestampFeedbackShown = DateTime.UtcNow.ToString("o")
            };
        }

        private void FlushPendingFeedbackRows(string timestampNextPlayerTurnStarted)
        {
            if (_pendingFeedbackRows.Count == 0)
            {
                return;
            }

            DebateCoachLogger logger = _coachLogger ??= new DebateCoachLogger();
            for (int i = 0; i < _pendingFeedbackRows.Count; i++)
            {
                _pendingFeedbackRows[i].TimestampNextPlayerTurnStarted = timestampNextPlayerTurnStarted;
                logger.LogFeedback(_pendingFeedbackRows[i]);
            }

            _pendingFeedbackRows.Clear();
        }

        private string BuildFollowUpPrompt()
        {
            return FormatTaskText(npcFollowUpPrompt) + "\n\n" +
                   "Learner's latest response: \"" + _latestPlayerUtterance + "\"\n" +
                   "Coach's latest feedback to the learner: \"" + _latestFeedbackText + "\"\n" +
                   "Now continue as the opponent. Ask one concise challenge or follow-up, then wait.";
        }

        private void CaptureOpponentAudio(ConvaiNPCAudioManager.ResponseAudio response)
        {
            if (response == null || response.IsFinal || string.IsNullOrWhiteSpace(response.AudioTranscript))
            {
                return;
            }

            if (_opponentTranscript.Length > 0)
            {
                _opponentTranscript.Append(' ');
            }

            _opponentTranscript.Append(response.AudioTranscript.Trim());
            SessionRecord.latestOpponentUtterance = _opponentTranscript.ToString();
        }

        private void HandleOpponentTalkingChanged(bool isTalking)
        {
            if (isTalking || Phase != OrchestrationPhase.OpponentSpeaking)
            {
                return;
            }

            SessionRecord.latestOpponentUtterance = _opponentTranscript.ToString().Trim();
            Phase = OrchestrationPhase.WaitingForPlayer;
            SetControlCursor(false);
            SetStatus($"Turn {_turnIndex}: hold T and answer the opponent. Coach feedback will appear after you finish.");
            SetButtonsForPhase();
        }

        private void SubscribeToConversationAudio()
        {
            if (conversationNPC != null && conversationNPC.AudioManager != null)
            {
                conversationNPC.AudioManager.OnResponseAudioStarted += CaptureOpponentAudio;
                conversationNPC.AudioManager.OnCharacterTalkingChanged += HandleOpponentTalkingChanged;
            }
        }

        private void UnsubscribeFromConversationAudio()
        {
            if (conversationNPC != null && conversationNPC.AudioManager != null)
            {
                conversationNPC.AudioManager.OnResponseAudioStarted -= CaptureOpponentAudio;
                conversationNPC.AudioManager.OnCharacterTalkingChanged -= HandleOpponentTalkingChanged;
            }
        }

        private void RegisterVoiceInterceptor()
        {
            if (!Application.isPlaying || !automaticCoachAfterPlayerVoice)
            {
                return;
            }

            ConvaiGRPCAPI.TryHandleUserVoiceTranscript = TryHandlePlayerVoiceTranscript;
        }

        private void UnregisterVoiceInterceptor()
        {
            if (ConvaiGRPCAPI.TryHandleUserVoiceTranscript == (Func<string, bool>)TryHandlePlayerVoiceTranscript)
            {
                ConvaiGRPCAPI.TryHandleUserVoiceTranscript = null;
            }
        }

        private void EnterIntro()
        {
            Phase = OrchestrationPhase.Intro;
            SetControlCursor(true);
            SetStatus("Press Start Conversation when you are ready.");
            _playerText.text = "Your response transcript will appear here after you speak.";
            _coachText.text = "Coach feedback will appear after your response.";
            SetButtonsForPhase();
        }

        private void BuildUi()
        {
            if (uiCanvas == null)
            {
                uiCanvas = FindObjectsByType<Canvas>(FindObjectsInactive.Exclude)
                    .FirstOrDefault(canvas => canvas.name == "Debate Round UI");
            }

            if (uiCanvas == null)
            {
                SetStatus("Condition C UI needs a canvas.");
                return;
            }

            _root = CreateRect("Shared Initiative Coach Controls", uiCanvas.transform);
            RectTransform rootRect = _root.GetComponent<RectTransform>();
            rootRect.anchorMin = new Vector2(0f, 1f);
            rootRect.anchorMax = new Vector2(0f, 1f);
            rootRect.pivot = new Vector2(0f, 1f);
            rootRect.anchoredPosition = new Vector2(24f, -24f);
            rootRect.sizeDelta = new Vector2(540f, 680f);

            Image background = _root.AddComponent<Image>();
            background.color = new Color(0.05f, 0.07f, 0.09f, 0.76f);

            VerticalLayoutGroup layout = _root.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(18, 18, 16, 16);
            layout.spacing = 10f;
            layout.childControlWidth = true;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            _titleText = CreateText(_root.transform, "Condition C: Coach Agent", 24, FontStyles.Bold, 34f);
            _taskText = CreateText(_root.transform, string.Empty, 15, FontStyles.Normal, 92f);
            _statusText = CreateText(_root.transform, string.Empty, 15, FontStyles.Bold, 54f);
            _playerText = CreateText(_root.transform, string.Empty, 15, FontStyles.Normal, 82f);
            _coachText = CreateText(_root.transform, string.Empty, 16, FontStyles.Normal, 122f);
            _learnerNoteInput = CreateInput(_root.transform, "Optional typed fallback if voice transcription is unavailable.", 70f);

            _startButton = CreateButton(_root.transform, "Start Conversation", BeginConditionC, new Color(0.20f, 0.48f, 0.36f));
            _pauseForCoachButton = CreateButton(_root.transform, "Pause For Coach", PauseForCoach, new Color(0.72f, 0.50f, 0.18f));
            _exampleButton = CreateButton(_root.transform, "Need an example?", RequestExample, new Color(0.42f, 0.36f, 0.64f));
            _continueButton = CreateButton(_root.transform, "Continue", ContinueConversation, new Color(0.24f, 0.43f, 0.70f));
            _endButton = CreateButton(_root.transform, "End Session", EndConditionC, new Color(0.62f, 0.22f, 0.20f));

            _taskText.text =
                $"Topic: {debateTopic}\n" +
                $"Your stance: {learnerStance}\n" +
                $"Selected strategy: {selectedStrategy}";
        }

        private void HideLegacyUi()
        {
            if (legacyInteractiveControls == null)
            {
                legacyInteractiveControls = GameObject.Find("Interactive Controls");
            }

            if (legacyStartButton == null)
            {
                legacyStartButton = GameObject.Find("Start Debate Button");
            }

            if (legacyRoundTimer == null)
            {
                legacyRoundTimer = GameObject.Find("Round Timer");
            }

            legacyInteractiveControls?.SetActive(false);
            legacyStartButton?.SetActive(false);
            legacyRoundTimer?.SetActive(false);
        }

        private void SetButtonsForPhase()
        {
            bool intro = Phase == OrchestrationPhase.Intro || Phase == OrchestrationPhase.Complete;
            bool waitingForPlayer = Phase == OrchestrationPhase.WaitingForPlayer;
            bool coachReady = Phase == OrchestrationPhase.CoachSuggestionReady;
            bool activeSession = Phase != OrchestrationPhase.Intro && Phase != OrchestrationPhase.Complete;

            SetActive(_startButton, intro);
            SetActive(_pauseForCoachButton, showManualPauseButton && waitingForPlayer);
            SetActive(_exampleButton, coachReady && !_exampleRequestedForCurrentTurn);
            SetActive(_continueButton, coachReady);
            SetActive(_endButton, activeSession);

            SetButtonInteractable(_continueButton, coachReady);
            SetButtonInteractable(_exampleButton, coachReady && !_exampleRequestedForCurrentTurn);

            if (_continueButton != null)
            {
                TMP_Text label = _continueButton.GetComponentInChildren<TMP_Text>();
                if (label != null)
                {
                    label.text = _turnIndex >= maxConversationTurns ? "Finish & Show Summary" : "Continue";
                }
            }
        }

        private void SetStatus(string text)
        {
            if (_statusText != null)
            {
                _statusText.text = text;
            }
        }

        private void SetControlCursor(bool visible)
        {
            if (!unlockCursorForControlUi)
            {
                return;
            }

            Cursor.lockState = visible ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = visible;
        }

        private string FormatTaskText(string template)
        {
            if (string.IsNullOrWhiteSpace(template))
            {
                return string.Empty;
            }

            return template.Contains("{0}", StringComparison.Ordinal)
                ? string.Format(template, debateTopic)
                : template;
        }

        private static GameObject CreateRect(string name, Transform parent)
        {
            GameObject gameObject = new(name, typeof(RectTransform));
            gameObject.transform.SetParent(parent, false);
            return gameObject;
        }

        private static TMP_Text CreateText(Transform parent, string text, int fontSize, FontStyles style, float height)
        {
            GameObject textObject = CreateRect("Text", parent);
            TMP_Text textComponent = textObject.AddComponent<TextMeshProUGUI>();
            textComponent.text = text;
            textComponent.fontSize = fontSize;
            textComponent.fontStyle = style;
            textComponent.color = Color.white;
            textComponent.alignment = TextAlignmentOptions.Left;
            textComponent.textWrappingMode = TextWrappingModes.Normal;
            textComponent.raycastTarget = false;

            LayoutElement layout = textObject.AddComponent<LayoutElement>();
            layout.preferredHeight = height;
            layout.minHeight = height;
            return textComponent;
        }

        private static Button CreateButton(Transform parent, string label, UnityEngine.Events.UnityAction action, Color color)
        {
            GameObject buttonObject = CreateRect(label, parent);
            Image image = buttonObject.AddComponent<Image>();
            image.color = color;

            Button button = buttonObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(action);

            LayoutElement layout = buttonObject.AddComponent<LayoutElement>();
            layout.preferredHeight = 42f;
            layout.minHeight = 42f;

            GameObject labelObject = CreateRect("Label", buttonObject.transform);
            RectTransform labelRect = labelObject.GetComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;

            TMP_Text text = labelObject.AddComponent<TextMeshProUGUI>();
            text.text = label;
            text.fontSize = 16;
            text.fontStyle = FontStyles.Bold;
            text.color = Color.white;
            text.alignment = TextAlignmentOptions.Center;
            text.raycastTarget = false;
            return button;
        }

        private static TMP_InputField CreateInput(Transform parent, string placeholder, float height)
        {
            GameObject inputObject = CreateRect("Learner Note Input", parent);
            Image image = inputObject.AddComponent<Image>();
            image.color = new Color(1f, 1f, 1f, 0.92f);

            LayoutElement layout = inputObject.AddComponent<LayoutElement>();
            layout.preferredHeight = height;
            layout.minHeight = height;

            TMP_InputField inputField = inputObject.AddComponent<TMP_InputField>();
            inputField.lineType = TMP_InputField.LineType.MultiLineNewline;

            GameObject textObject = CreateRect("Text", inputObject.transform);
            RectTransform textRect = textObject.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(10f, 8f);
            textRect.offsetMax = new Vector2(-10f, -8f);

            TMP_Text textComponent = textObject.AddComponent<TextMeshProUGUI>();
            textComponent.fontSize = 14;
            textComponent.color = Color.black;
            textComponent.textWrappingMode = TextWrappingModes.Normal;

            GameObject placeholderObject = CreateRect("Placeholder", inputObject.transform);
            RectTransform placeholderRect = placeholderObject.GetComponent<RectTransform>();
            placeholderRect.anchorMin = Vector2.zero;
            placeholderRect.anchorMax = Vector2.one;
            placeholderRect.offsetMin = new Vector2(10f, 8f);
            placeholderRect.offsetMax = new Vector2(-10f, -8f);

            TMP_Text placeholderText = placeholderObject.AddComponent<TextMeshProUGUI>();
            placeholderText.text = placeholder;
            placeholderText.fontSize = 14;
            placeholderText.color = new Color(0f, 0f, 0f, 0.48f);
            placeholderText.textWrappingMode = TextWrappingModes.Normal;

            inputField.textComponent = textComponent;
            inputField.placeholder = placeholderText;
            return inputField;
        }

        private static void SetActive(Selectable selectable, bool active)
        {
            if (selectable != null)
            {
                selectable.gameObject.SetActive(active);
            }
        }

        private static void SetButtonInteractable(Button button, bool interactable)
        {
            if (button != null)
            {
                button.interactable = interactable;
            }
        }
    }
}
