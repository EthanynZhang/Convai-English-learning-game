using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Convai.Scripts.Runtime.Core;
#if CROSSTALES_RTVOICE
using Crosstales.RTVoice;
using Crosstales.RTVoice.Model;
using Crosstales.RTVoice.Model.Enum;
#endif
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.Serialization;
using UnityEngine.UI;

namespace Game.Debate
{
    [DefaultExecutionOrder(-500)]
    public sealed class NpcDebateLearningPhaseController : MonoBehaviour
    {
        private const string TargetSceneName = "Level_NPCVsNPCDebate";
        private const string AnnaSpeakerId = "Anna";
        private const string MikeSpeakerId = "Mike";

        [Header("Research Logging")]
        [SerializeField] private string participantId = "PILOT";
        [SerializeField] private string condition = "npc_vs_npc";

        [Header("Debate Start")]
        [SerializeField] private NpcDebateRoundManager roundManager;
        [SerializeField] private bool unlockCursorDuringLearning = true;

        [Header("NPC Presentation")]
        [FormerlySerializedAs("demonstrationNPC")]
        [SerializeField] private ConvaiNPC primaryDemoNPC;
        [SerializeField] private ConvaiNPC secondaryDemoNPC;
        [SerializeField] private bool playNpcVoice = true;
        [SerializeField] private bool useRtVoiceTts = true;
        [SerializeField] private string annaRtVoiceNameHints = "Jenny;Aria;Zira;Samantha;Susan";
        [SerializeField] private string mikeRtVoiceNameHints = "David;Guy;Mark;George;Daniel";
        [SerializeField] private string rtVoiceCulture = "en";
        [SerializeField, Range(0.01f, 3f)] private float annaRtVoiceRate = 0.92f;
        [SerializeField, Range(0f, 2f)] private float annaRtVoicePitch = 1.02f;
        [SerializeField, Range(0.01f, 3f)] private float mikeRtVoiceRate = 0.9f;
        [SerializeField, Range(0f, 2f)] private float mikeRtVoicePitch = 0.95f;
        [SerializeField, Range(0.01f, 1f)] private float rtVoiceVolume = 0.9f;
        [SerializeField] private bool logRtVoiceSelection = true;
        [SerializeField] private bool reloadRtVoiceProviderIfVoiceMissing = true;
        [SerializeField] private bool useLocalCachedTts;
        [SerializeField] private bool fallbackToConvaiVoiceIfClipMissing;
        [SerializeField] private float demoLineSeconds = 6f;

        [Header("World Panel")]
        [SerializeField] private Transform panelWorldAnchor;
        [SerializeField] private Vector2 panelSize = new(1450f, 1150f);
        [SerializeField] private float panelWorldScale = 0.0025085f;
        [SerializeField] private bool useFixedPanelTransform = true;
        [SerializeField] private Vector3 fixedPanelPosition = new(-0.31524f, 1.0433f, 3.7437f);
        [SerializeField] private Vector3 fixedPanelEulerAngles;
        [SerializeField] private float defaultForwardOffset = 1.2f;
        [SerializeField] private float defaultDownOffset = 0.75f;

        private readonly DebateLearningMetrics _metrics = new();
        private readonly List<string> _strategyVersionsViewed = new();
        private readonly List<Button> _choiceButtons = new();
        private readonly List<Button> _rationaleButtons = new();

        private DebateLearningLogger _logger;
        private GameObject _root;
        private TMP_Text _titleText;
        private TMP_Text _bodyText;
        private TMP_Text _progressText;
        private TMP_Text _countdownText;
        private TMP_Text _strategyLabelText;
        private TMP_Text _currentSpeakerText;
        private TMP_Text _transcriptText;
        private TMP_Text _demoStatusText;
        private LayoutElement _bodyLayout;
        private LayoutElement _transcriptLayout;
        private GameObject _choiceButtonsRoot;
        private GameObject _rationaleButtonsRoot;
        private GameObject _shortTextInputRoot;
        private TMP_InputField _shortTextInput;
        private LayoutElement _choiceButtonsLayout;
        private LayoutElement _rationaleButtonsLayout;
        private LayoutElement _navigationLayout;
        private Button _previousButton;
        private Button _replayButton;
        private Button _nextButton;

        private int _stageIndex;
        private float _stageElapsed;
        private bool _completed;
        private bool _started;
        private Coroutine _demoRoutine;
        private DemoDialogueLine[] _currentDialogueLines = System.Array.Empty<DemoDialogueLine>();
        private int _currentDialogueLineIndex = -1;
        private ConvaiNPC _currentAudioNpc;
        private AudioSource _currentDemoAudioSource;
#if CROSSTALES_RTVOICE
        private bool _loggedRtVoiceSelection;
        private float _lastRtVoiceProviderReloadTime = -999f;
        private Speaker _rtVoiceSpeaker;
        private Voice _annaRtVoice;
        private Voice _mikeRtVoice;
#endif

        private void Awake()
        {
            if (SceneManager.GetActiveScene().name != TargetSceneName)
            {
                enabled = false;
                return;
            }

            _logger = new DebateLearningLogger();
            ResolveReferences();
            roundManager?.SetWaitForExternalStart(true);
            EnsureEventSystem();
            BuildUi();
            SetLearningCursor(true);
        }

        private void Start()
        {
            if (_started || !enabled)
            {
                return;
            }

            _started = true;
            EnterStage(0);
        }

        private void Update()
        {
            if (_completed || !_started)
            {
                return;
            }

            _stageElapsed += Time.deltaTime;
            HandleKeyboardInput();
            UpdateControls(CurrentStage);
        }

        public void Configure(NpcDebateRoundManager manager)
        {
            roundManager = manager;
            roundManager?.SetWaitForExternalStart(true);
        }

        private DebateLearningStageSpec CurrentStage => DebateLearningContent.StageSequence[_stageIndex];

        private void ResolveReferences()
        {
            if (roundManager == null)
            {
                roundManager = FindAnyObjectByType<NpcDebateRoundManager>();
            }

            ConvaiNPC[] npcs = FindObjectsByType<ConvaiNPC>(FindObjectsInactive.Exclude);
            if (primaryDemoNPC == null)
            {
                primaryDemoNPC = FindNpcByName(npcs, "Anna") ?? npcs.FirstOrDefault();
            }

            if (secondaryDemoNPC == null)
            {
                secondaryDemoNPC = FindNpcByName(npcs, "Mike") ?? npcs.FirstOrDefault(npc => npc != primaryDemoNPC);
            }
        }

        private static ConvaiNPC FindNpcByName(IEnumerable<ConvaiNPC> npcs, string namePart)
        {
            return npcs.FirstOrDefault(npc =>
                npc != null &&
                (npc.name.Contains(namePart) ||
                 (!string.IsNullOrWhiteSpace(npc.characterName) && npc.characterName.Contains(namePart))));
        }

        private static void EnsureEventSystem()
        {
            EventSystem[] systems = FindObjectsByType<EventSystem>(FindObjectsInactive.Exclude);
            if (systems.Length > 0)
            {
                return;
            }

            new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
        }

        private void BuildUi()
        {
            _root = new GameObject("NPC Debate Learning World Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            Canvas canvas = _root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 100;

            RectTransform rootRect = _root.GetComponent<RectTransform>();
            rootRect.sizeDelta = panelSize;
            rootRect.localScale = Vector3.one * panelWorldScale;
            PositionWorldPanel(_root.transform);

            CanvasScaler scaler = _root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;

            GameObject panel = CreateRect("Learning Panel", _root.transform);
            RectTransform panelRect = panel.GetComponent<RectTransform>();
            panelRect.anchorMin = Vector2.zero;
            panelRect.anchorMax = Vector2.one;
            panelRect.offsetMin = Vector2.zero;
            panelRect.offsetMax = Vector2.zero;

            Image panelImage = panel.AddComponent<Image>();
            panelImage.color = new Color(0.93f, 0.95f, 0.94f, 0.98f);

            VerticalLayoutGroup layout = panel.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(38, 38, 32, 32);
            layout.spacing = 8f;
            layout.childControlWidth = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            _progressText = CreateText(panel.transform, string.Empty, 30, FontStyles.Bold, 42f, new Color(0.18f, 0.24f, 0.26f));
            _titleText = CreateText(panel.transform, string.Empty, 44, FontStyles.Bold, 74f, new Color(0.05f, 0.12f, 0.16f));
            _strategyLabelText = CreateText(panel.transform, string.Empty, 35, FontStyles.Bold, 48f, new Color(0.12f, 0.30f, 0.48f));
            _bodyText = CreateText(panel.transform, string.Empty, 35, FontStyles.Normal, 260f, new Color(0.08f, 0.10f, 0.12f));
            _currentSpeakerText = CreateText(panel.transform, string.Empty, 35, FontStyles.Bold, 46f, new Color(0.12f, 0.22f, 0.28f));
            _transcriptText = CreateText(panel.transform, string.Empty, 35, FontStyles.Normal, 520f, new Color(0.08f, 0.10f, 0.12f));
            _demoStatusText = CreateText(panel.transform, string.Empty, 32, FontStyles.Bold, 42f, new Color(0.20f, 0.25f, 0.25f));
            _countdownText = CreateText(panel.transform, string.Empty, 1, FontStyles.Normal, 1f, Color.clear);
            _bodyLayout = _bodyText.GetComponent<LayoutElement>();
            _transcriptLayout = _transcriptText.GetComponent<LayoutElement>();

            GameObject choices = CreateRect("Learning Choice Buttons", panel.transform);
            _choiceButtonsRoot = choices;
            VerticalLayoutGroup choiceLayout = choices.AddComponent<VerticalLayoutGroup>();
            choiceLayout.spacing = 8f;
            choiceLayout.childControlHeight = true;
            choiceLayout.childControlWidth = true;
            choiceLayout.childForceExpandHeight = false;
            choiceLayout.childForceExpandWidth = true;
            LayoutElement choiceLayoutElement = choices.AddComponent<LayoutElement>();
            choiceLayoutElement.preferredHeight = 210f;
            choiceLayoutElement.minHeight = 140f;
            _choiceButtonsLayout = choiceLayoutElement;

            for (int i = 0; i < 3; i++)
            {
                Button button = CreateButton(choices.transform, string.Empty, null, new Color(0.14f, 0.42f, 0.56f));
                ConfigureButtonVisual(button, 62f, 28f);
                _choiceButtons.Add(button);
            }

            _shortTextInput = CreateInputField(panel.transform, "Optional: Because...");
            _shortTextInputRoot = _shortTextInput.gameObject;

            GameObject rationales = CreateRect("Micro Choice Rationale Buttons", panel.transform);
            _rationaleButtonsRoot = rationales;
            VerticalLayoutGroup rationaleLayout = rationales.AddComponent<VerticalLayoutGroup>();
            rationaleLayout.spacing = 6f;
            rationaleLayout.childControlHeight = true;
            rationaleLayout.childControlWidth = true;
            rationaleLayout.childForceExpandHeight = false;
            rationaleLayout.childForceExpandWidth = true;
            LayoutElement rationaleLayoutElement = rationales.AddComponent<LayoutElement>();
            rationaleLayoutElement.preferredHeight = 236f;
            rationaleLayoutElement.minHeight = 190f;
            _rationaleButtonsLayout = rationaleLayoutElement;

            for (int i = 0; i < DebateLearningContent.MicroChoiceRationaleOptions.Length; i++)
            {
                Button button = CreateButton(rationales.transform, string.Empty, null, new Color(0.34f, 0.39f, 0.43f));
                ConfigureButtonVisual(button, 54f, 25f);
                _rationaleButtons.Add(button);
            }

            GameObject navigation = CreateRect("Learning Navigation Buttons", panel.transform);
            HorizontalLayoutGroup navigationLayout = navigation.AddComponent<HorizontalLayoutGroup>();
            navigationLayout.spacing = 12f;
            navigationLayout.childControlHeight = true;
            navigationLayout.childControlWidth = true;
            navigationLayout.childForceExpandHeight = true;
            navigationLayout.childForceExpandWidth = true;
            LayoutElement navigationLayoutElement = navigation.AddComponent<LayoutElement>();
            navigationLayoutElement.preferredHeight = 46f;
            navigationLayoutElement.minHeight = 46f;
            _navigationLayout = navigationLayoutElement;

            _previousButton = CreateButton(navigation.transform, "Previous", PreviousStage, new Color(0.34f, 0.39f, 0.43f));
            _replayButton = CreateButton(navigation.transform, "Replay Demo", ReplayCurrentDemo, new Color(0.45f, 0.33f, 0.14f));
            _nextButton = CreateButton(navigation.transform, "Next", AdvanceStage, new Color(0.13f, 0.47f, 0.33f));
            ConfigureButtonVisual(_previousButton, 42f, 24f);
            ConfigureButtonVisual(_replayButton, 42f, 24f);
            ConfigureButtonVisual(_nextButton, 42f, 24f);
        }

        private void PositionWorldPanel(Transform panel)
        {
            if (panelWorldAnchor != null)
            {
                panel.SetPositionAndRotation(panelWorldAnchor.position, panelWorldAnchor.rotation);
                return;
            }

            if (useFixedPanelTransform)
            {
                panel.SetPositionAndRotation(fixedPanelPosition, Quaternion.Euler(fixedPanelEulerAngles));
                return;
            }

            Transform source = Camera.main != null ? Camera.main.transform : transform;
            Vector3 forward = Vector3.ProjectOnPlane(source.forward, Vector3.up).normalized;
            if (forward.sqrMagnitude < 0.001f)
            {
                forward = source.forward.normalized;
            }

            panel.position = source.position + forward * defaultForwardOffset + Vector3.down * defaultDownOffset;
            panel.rotation = Quaternion.LookRotation(forward, Vector3.up);
        }

        private void EnterStage(int index)
        {
            StopDemoPlayback();
            _stageIndex = Mathf.Clamp(index, 0, DebateLearningContent.StageSequence.Length - 1);
            _stageElapsed = 0f;
            _currentDialogueLines = System.Array.Empty<DemoDialogueLine>();
            _currentDialogueLineIndex = -1;

            DebateLearningStageSpec stage = CurrentStage;
            _progressText.text = $"Stage {_stageIndex + 1} / {DebateLearningContent.StageSequence.Length - 1}";
            _titleText.text = stage.Title;
            _bodyText.text = stage.Body;
            _strategyLabelText.text = GetStrategyLabel(stage.Key);
            _currentSpeakerText.text = string.Empty;
            _transcriptText.text = string.Empty;
            _demoStatusText.text = string.Empty;
            _countdownText.text = string.Empty;
            SetChoiceButtons(System.Array.Empty<string>(), null);
            SetRationaleButtons(System.Array.Empty<string>(), null);
            SetShortTextInputVisible(false);
            ConfigureStageLayout(stage.Key);

            if (IsDemoStage(stage.Key))
            {
                BeginDemoStage(stage);
            }
            else if (IsMicroPracticeStage(stage.Key))
            {
                ConfigureMicroPracticeStage(stage.Key);
            }
            else if (stage.Key == DebateLearningStageKey.MicroChoice)
            {
                ConfigureMicroChoiceStage();
            }

            UpdateControls(stage);
        }

        private void ConfigureMicroPracticeStage(DebateLearningStageKey key)
        {
            SetChoiceButtons(System.Array.Empty<string>(), null);
            SetRationaleButtons(System.Array.Empty<string>(), null);
            SetShortTextInputVisible(false);
            _shortTextInput.text = string.Empty;
            _transcriptText.text = string.Empty;
        }

        private void ConfigureMicroChoiceStage()
        {
            SetChoiceButtons(System.Array.Empty<string>(), null);
            SetRationaleButtons(System.Array.Empty<string>(), null);
            SetShortTextInputVisible(false);
            _shortTextInput.text = string.Empty;
            _transcriptText.text = string.Empty;
        }

        private void BeginDemoStage(DebateLearningStageSpec stage)
        {
            _currentDialogueLines = DebateLearningContent.GetDialogueLines(stage.Key);
            string strategy = DebateLearningContent.GetStrategyForStage(stage.Key);
            if (!string.IsNullOrWhiteSpace(strategy) && !_strategyVersionsViewed.Contains(strategy))
            {
                _strategyVersionsViewed.Add(strategy);
            }

            SetTranscriptText(_currentDialogueLines, -1);
            _demoStatusText.text = "Demo playing...";
            _demoRoutine = StartCoroutine(PlayDemoLines(_currentDialogueLines));
        }

        private IEnumerator PlayDemoLines(DemoDialogueLine[] lines)
        {
            for (int i = 0; i < lines.Length; i++)
            {
                _currentDialogueLineIndex = i;
                DemoDialogueLine line = lines[i];
                _currentSpeakerText.text = $"Current Speaker: {line.SpeakerName}";
                SetTranscriptText(lines, i);
                ConvaiNPC speaker = GetNpcForLine(line);
                float voiceSeconds = PlayDialogueLine(CurrentStage.Key, i, line, speaker);
                float waitSeconds = voiceSeconds > 0f ? voiceSeconds + 0.2f : demoLineSeconds;
                yield return new WaitForSeconds(Mathf.Max(0.1f, waitSeconds));
                if (_currentAudioNpc == speaker)
                {
                    StopCurrentDemoAudio();
                }
            }

            _currentDialogueLineIndex = -1;
            _currentSpeakerText.text = "Current Speaker: Demo finished";
            _demoStatusText.text = "Demo finished. Review the transcript, replay, go back, or continue.";
            _demoRoutine = null;
        }

        private void ReplayCurrentDemo()
        {
            if (!IsDemoStage(CurrentStage.Key))
            {
                return;
            }

            _metrics.RecordRewatch();
            StopDemoPlayback();
            SetTranscriptText(_currentDialogueLines, -1);
            _currentSpeakerText.text = string.Empty;
            _demoStatusText.text = "Demo replaying...";
            _demoRoutine = StartCoroutine(PlayDemoLines(_currentDialogueLines));
        }

        private void HandleKeyboardInput()
        {
            if (Input.GetKeyDown(KeyCode.RightArrow) || Input.GetKeyDown(KeyCode.Keypad6))
            {
                HandleKeyboardShortcut(KeyCode.RightArrow);
            }
            else if (Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.Keypad4))
            {
                HandleKeyboardShortcut(KeyCode.LeftArrow);
            }
            else if (Input.GetKeyDown(KeyCode.DownArrow) || Input.GetKeyDown(KeyCode.Keypad2))
            {
                HandleKeyboardShortcut(KeyCode.DownArrow);
            }
            else if (Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.Keypad8))
            {
                HandleKeyboardShortcut(KeyCode.UpArrow);
            }
        }

        private void HandleKeyboardShortcut(KeyCode key)
        {
            if (_completed || !_started)
            {
                return;
            }

            switch (key)
            {
                case KeyCode.RightArrow:
                    if (CanUseButton(_nextButton))
                    {
                        AdvanceStage();
                    }
                    break;
                case KeyCode.LeftArrow:
                    if (CanUseButton(_previousButton))
                    {
                        PreviousStage();
                    }
                    break;
                case KeyCode.DownArrow:
                    if (CanUseButton(_replayButton))
                    {
                        ReplayCurrentDemo();
                    }
                    break;
                case KeyCode.UpArrow:
                    if (CanUseButton(_replayButton))
                    {
                        ReplayCurrentDemo();
                    }
                    break;
            }
        }

        private static bool CanUseButton(Button button)
        {
            return button != null && button.gameObject.activeInHierarchy && button.interactable;
        }

        private void PreviousStage()
        {
            if (_stageIndex <= 0)
            {
                return;
            }

            CaptureCurrentStageTextInput();
            StopDemoPlayback();
            LogCurrentStage();
            EnterStage(_stageIndex - 1);
        }

        private void AdvanceStage()
        {
            CaptureCurrentStageTextInput();
            StopDemoPlayback();
            int nextIndex = _stageIndex + 1;
            if (nextIndex >= DebateLearningContent.StageSequence.Length)
            {
                CompleteLearning(string.Empty);
                return;
            }

            if (DebateLearningContent.StageSequence[nextIndex].IsTerminal)
            {
                CompleteLearning(string.Empty);
                return;
            }

            LogCurrentStage();
            EnterStage(nextIndex);
        }

        private void CompleteLearning(string strategy)
        {
            if (_completed)
            {
                return;
            }

            StopDemoPlayback();
            CaptureCurrentStageTextInput();
            if (!string.IsNullOrWhiteSpace(strategy))
            {
                _metrics.RecordMicroChoice(strategy);
            }

            LogCurrentStage();
            _logger.LogFinalSummary(participantId, condition, _metrics);

            _completed = true;
            _root.SetActive(false);
            SetLearningCursor(false);
            roundManager?.BeginRound();
        }

        private void LogCurrentStage()
        {
            DebateLearningStageSpec stage = CurrentStage;
            string strategyVersion = DebateLearningContent.GetStrategyForStage(stage.Key);
            _metrics.RecordStage(stage.Title, _stageElapsed, stage.ViewKind, strategyVersion);
            _logger.LogStage(participantId, condition, stage.Title, _metrics);
        }

        private void CaptureCurrentStageTextInput()
        {
        }

        private void StopDemoPlayback()
        {
            if (_demoRoutine != null)
            {
                StopCoroutine(_demoRoutine);
                _demoRoutine = null;
            }

            StopCurrentDemoAudio();
        }

        private float PlayDialogueLine(DebateLearningStageKey stageKey, int lineIndex, DemoDialogueLine line, ConvaiNPC speaker)
        {
            if (!playNpcVoice || speaker == null || string.IsNullOrWhiteSpace(line.Text))
            {
                return 0f;
            }

            if (useLocalCachedTts && TryPlayCachedDemoTts(stageKey, lineIndex, line, speaker, out float cachedClipSeconds))
            {
                return cachedClipSeconds;
            }

            if (useRtVoiceTts)
            {
#if CROSSTALES_RTVOICE
                if (TryPlayRtVoiceDemoTts(line, speaker, out float rtVoiceSeconds))
                {
                    return rtVoiceSeconds;
                }

                Debug.LogWarning($"RT-Voice demo TTS did not play for {line.SpeakerName}. Local WAV and Convai fallback are disabled while RT-Voice mode is enabled.");
#else
                Debug.LogWarning("RT-Voice demo TTS is enabled, but the project was compiled without CROSSTALES_RTVOICE. Use local cached TTS clips or install RT-Voice Pro and define CROSSTALES_RTVOICE.");
#endif
                return 0f;
            }

            if (useLocalCachedTts && !fallbackToConvaiVoiceIfClipMissing)
            {
                string resourcePath = DebateLearningContent.GetDialogueClipResourcePath(stageKey, lineIndex, line);
                Debug.LogWarning($"Missing cached debate demo TTS clip at Resources/{resourcePath}.wav. Skipping Convai fallback for fixed-script demo.");
                return 0f;
            }

            ConvaiNPCManager.Instance?.SetActiveConvaiNPC(speaker);
            speaker.SendTextDataAsync(DebateLearningContent.GetRepeatExactlyPrompt(line.Text));
            return demoLineSeconds;
        }

#if CROSSTALES_RTVOICE
        private bool TryPlayRtVoiceDemoTts(DemoDialogueLine line, ConvaiNPC speaker, out float speechSeconds)
        {
            speechSeconds = 0f;
            if (!useRtVoiceTts || string.IsNullOrWhiteSpace(line.Text))
            {
                return false;
            }

            Speaker rtVoice = GetRtVoiceSpeaker();
            if (rtVoice == null || !rtVoice.isSpeakSupported)
            {
                return false;
            }

            AudioSource audioSource = speaker.GetComponent<AudioSource>();
            if (audioSource == null)
            {
                audioSource = speaker.gameObject.AddComponent<AudioSource>();
            }

            Voice voice = GetRtVoiceForLine(line);
            if (voice == null)
            {
                Debug.LogWarning($"No RT-Voice voice matched {line.SpeakerName} ({line.SpeakerId}). Install an English {(IsMikeLine(line) ? "male" : "female")} system voice or update the voice hints on NpcDebateLearningPhaseController.");
                return false;
            }

            StopCurrentDemoAudio();
            rtVoice.Silence();
            speaker.StopAllAudioPlayback();

            float rate = GetRtVoiceRateForLine(line);
            float pitch = GetRtVoicePitchForLine(line);
            string uid = rtVoice.Speak(line.Text, audioSource, voice, true, rate, pitch, rtVoiceVolume, string.Empty, false);
            if (string.IsNullOrWhiteSpace(uid) || uid == "disabled")
            {
                return false;
            }

            _currentAudioNpc = speaker;
            _currentDemoAudioSource = audioSource;
            SetDemoTalkingState(speaker, true);
            LogRtVoiceSelectionOnce();
            speechSeconds = Mathf.Max(0.5f, rtVoice.ApproximateSpeechLength(line.Text, rate));
            return true;
        }

        private Speaker GetRtVoiceSpeaker()
        {
            if (_rtVoiceSpeaker != null)
            {
                return _rtVoiceSpeaker;
            }

            _rtVoiceSpeaker = Speaker.Instance;
            if (_rtVoiceSpeaker == null)
            {
                return null;
            }

            ResolveRtVoiceSelections();
            return _rtVoiceSpeaker;
        }

        private void ResolveRtVoiceSelections()
        {
            if (_rtVoiceSpeaker == null)
            {
                return;
            }

            _annaRtVoice = PickRtVoice(_rtVoiceSpeaker.Voices, Gender.FEMALE, rtVoiceCulture, annaRtVoiceNameHints);
            _mikeRtVoice = PickRtVoice(_rtVoiceSpeaker.Voices, Gender.MALE, rtVoiceCulture, mikeRtVoiceNameHints);
        }

        private Voice GetRtVoiceForLine(DemoDialogueLine line)
        {
            if (_rtVoiceSpeaker == null)
            {
                return null;
            }

            if (IsMikeLine(line))
            {
                if (_mikeRtVoice == null)
                {
                    ResolveRtVoiceSelections();
                    TryReloadRtVoiceProviderForMissingVoice();
                }

                return _mikeRtVoice;
            }

            if (_annaRtVoice == null)
            {
                ResolveRtVoiceSelections();
            }

            return _annaRtVoice;
        }

        private void TryReloadRtVoiceProviderForMissingVoice()
        {
            if (!reloadRtVoiceProviderIfVoiceMissing || _rtVoiceSpeaker == null || _mikeRtVoice != null)
            {
                return;
            }

            if (Time.realtimeSinceStartup - _lastRtVoiceProviderReloadTime < 5f)
            {
                return;
            }

            _lastRtVoiceProviderReloadTime = Time.realtimeSinceStartup;
            _rtVoiceSpeaker.ReloadProvider();
            ResolveRtVoiceSelections();
        }

        private float GetRtVoiceRateForLine(DemoDialogueLine line)
        {
            return IsMikeLine(line)
                ? mikeRtVoiceRate
                : annaRtVoiceRate;
        }

        private float GetRtVoicePitchForLine(DemoDialogueLine line)
        {
            return IsMikeLine(line)
                ? mikeRtVoicePitch
                : annaRtVoicePitch;
        }

        private static bool IsMikeLine(DemoDialogueLine line)
        {
            return string.Equals(line.SpeakerId, MikeSpeakerId, System.StringComparison.OrdinalIgnoreCase);
        }

        private void LogRtVoiceSelectionOnce()
        {
            if (!logRtVoiceSelection || _loggedRtVoiceSelection)
            {
                return;
            }

            _loggedRtVoiceSelection = true;
            Debug.Log($"RT-Voice demo voices selected. Anna: {FormatRtVoiceName(_annaRtVoice)}; Mike: {FormatRtVoiceName(_mikeRtVoice)}");
        }

        private static string FormatRtVoiceName(Voice voice)
        {
            return voice == null ? "default system voice" : voice.ToString();
        }

        public static Voice PickRtVoice(IEnumerable<Voice> voices, Gender gender, string culture, string nameHints)
        {
            if (voices == null)
            {
                return null;
            }

            List<Voice> available = voices.Where(voice => voice != null).ToList();
            if (available.Count == 0)
            {
                return null;
            }

            List<Voice> genderCultureMatches = available
                .Where(voice => voice.Gender == gender && MatchesCulture(voice, culture))
                .OrderByDescending(voice => voice.isNeural)
                .ThenBy(voice => voice.Name)
                .ToList();

            List<Voice> genderMatches = available
                .Where(voice => voice.Gender == gender)
                .OrderByDescending(voice => voice.isNeural)
                .ThenBy(voice => voice.Name)
                .ToList();

            foreach (string hint in SplitVoiceHints(nameHints))
            {
                Voice hintedVoice = genderCultureMatches.FirstOrDefault(voice => MatchesVoiceHint(voice, hint))
                    ?? genderMatches.FirstOrDefault(voice => MatchesVoiceHint(voice, hint));
                if (hintedVoice != null)
                {
                    return hintedVoice;
                }
            }

            return genderCultureMatches.FirstOrDefault() ?? genderMatches.FirstOrDefault();
        }

        private static IEnumerable<string> SplitVoiceHints(string nameHints)
        {
            return string.IsNullOrWhiteSpace(nameHints)
                ? System.Array.Empty<string>()
                : nameHints
                    .Split(new[] { ';', ',', '|' }, System.StringSplitOptions.RemoveEmptyEntries)
                    .Select(hint => hint.Trim())
                    .Where(hint => hint.Length > 0);
        }

        private static bool MatchesVoiceHint(Voice voice, string hint)
        {
            return ContainsIgnoreCase(voice.Name, hint) ||
                   ContainsIgnoreCase(voice.Description, hint) ||
                   ContainsIgnoreCase(voice.Identifier, hint);
        }

        private static bool MatchesCulture(Voice voice, string culture)
        {
            if (string.IsNullOrWhiteSpace(culture))
            {
                return true;
            }

            string voiceCulture = (voice.Culture ?? string.Empty).Replace("-", string.Empty).Replace("_", string.Empty);
            string expectedCulture = culture.Trim().Replace("-", string.Empty).Replace("_", string.Empty);
            return voiceCulture.StartsWith(expectedCulture, System.StringComparison.OrdinalIgnoreCase);
        }

        private static bool ContainsIgnoreCase(string value, string part)
        {
            return !string.IsNullOrWhiteSpace(value) &&
                   !string.IsNullOrWhiteSpace(part) &&
                   value.IndexOf(part, System.StringComparison.OrdinalIgnoreCase) >= 0;
        }
#endif

        private bool TryPlayCachedDemoTts(
            DebateLearningStageKey stageKey,
            int lineIndex,
            DemoDialogueLine line,
            ConvaiNPC speaker,
            out float clipSeconds)
        {
            clipSeconds = 0f;
            string resourcePath = DebateLearningContent.GetDialogueClipResourcePath(stageKey, lineIndex, line);
            AudioClip clip = Resources.Load<AudioClip>(resourcePath);
            if (clip == null || speaker.AudioManager == null)
            {
                return false;
            }

            AudioSource audioSource = speaker.GetComponent<AudioSource>();
            if (audioSource == null)
            {
                return false;
            }

            StopCurrentDemoAudio();
            speaker.StopAllAudioPlayback();
            audioSource.clip = clip;
            audioSource.Play();

            _currentAudioNpc = speaker;
            _currentDemoAudioSource = audioSource;
            SetDemoTalkingState(speaker, true);
            clipSeconds = clip.length;
            return true;
        }

        private void StopCurrentDemoAudio()
        {
#if CROSSTALES_RTVOICE
            if (_rtVoiceSpeaker != null)
            {
                _rtVoiceSpeaker.Silence();
            }
#endif

            if (_currentAudioNpc == null)
            {
                return;
            }

            if (_currentDemoAudioSource != null)
            {
                _currentDemoAudioSource.Stop();
                _currentDemoAudioSource.clip = null;
            }

            _currentAudioNpc.StopAllAudioPlayback();
            _currentAudioNpc.SetCharacterTalking(false);
            SetDemoTalkingState(_currentAudioNpc, false);
            _currentAudioNpc.ResetCharacterAnimation();
            _currentAudioNpc = null;
            _currentDemoAudioSource = null;
        }

        private static void SetDemoTalkingState(ConvaiNPC npc, bool isTalking)
        {
            npc.SetCharacterTalking(isTalking);
            Animator animator = npc.GetComponent<Animator>();
            if (animator != null)
            {
                animator.SetBool("Talk", isTalking);
            }
        }

        private ConvaiNPC GetNpcForLine(DemoDialogueLine line)
        {
            if (string.Equals(line.SpeakerId, MikeSpeakerId, System.StringComparison.OrdinalIgnoreCase))
            {
                return secondaryDemoNPC != null ? secondaryDemoNPC : primaryDemoNPC;
            }

            if (string.Equals(line.SpeakerId, AnnaSpeakerId, System.StringComparison.OrdinalIgnoreCase))
            {
                return primaryDemoNPC != null ? primaryDemoNPC : secondaryDemoNPC;
            }

            return primaryDemoNPC != null ? primaryDemoNPC : secondaryDemoNPC;
        }

        private void SetTranscriptText(DemoDialogueLine[] lines, int activeIndex)
        {
            if (_transcriptText == null)
            {
                return;
            }

            if (lines == null || lines.Length == 0)
            {
                _transcriptText.text = string.Empty;
                return;
            }

            StringBuilder builder = new();
            for (int i = 0; i < lines.Length; i++)
            {
                DemoDialogueLine line = lines[i];
                string prefix = string.IsNullOrWhiteSpace(line.CreeiPart)
                    ? line.SpeakerName
                    : $"{line.SpeakerName} ({line.CreeiPart})";
                string row = $"{prefix}: {line.Text}";
                if (i == activeIndex)
                {
                    builder.Append("<mark=#FFE8A3AA><b>");
                    builder.Append(row);
                    builder.Append("</b></mark>");
                }
                else
                {
                    builder.Append(row);
                }

                if (i < lines.Length - 1)
                {
                    builder.AppendLine();
                }
            }

            _transcriptText.text = builder.ToString();
        }

        private void UpdateControls(DebateLearningStageSpec stage)
        {
            bool isDemo = IsDemoStage(stage.Key);
            bool isMicroChoice = stage.Key == DebateLearningStageKey.MicroChoice;
            bool isMicroPractice = IsMicroPracticeStage(stage.Key);
            bool showsReflectionText = isMicroChoice || isMicroPractice;

            if (_previousButton != null)
            {
                _previousButton.gameObject.SetActive(true);
                _previousButton.interactable = _stageIndex > 0;
            }

            if (_replayButton != null)
            {
                _replayButton.gameObject.SetActive(isDemo);
                _replayButton.interactable = isDemo;
            }

            if (_nextButton != null)
            {
                _nextButton.gameObject.SetActive(true);
                _nextButton.interactable = true;
            }

            SetTextObjectActive(_strategyLabelText, !string.IsNullOrWhiteSpace(_strategyLabelText != null ? _strategyLabelText.text : string.Empty));
            SetTextObjectActive(_currentSpeakerText, isDemo);
            SetTextObjectActive(_demoStatusText, isDemo);
            SetTextObjectActive(_transcriptText, isDemo || showsReflectionText);

            if (_choiceButtonsRoot != null)
            {
                _choiceButtonsRoot.SetActive(false);
            }

            if (_rationaleButtonsRoot != null)
            {
                _rationaleButtonsRoot.SetActive(false);
            }

            foreach (Button button in _choiceButtons)
            {
                button.interactable = false;
            }

            foreach (Button button in _rationaleButtons)
            {
                button.interactable = false;
            }
        }

        private void ConfigureStageLayout(DebateLearningStageKey key)
        {
            if (IsDemoStage(key))
            {
                SetLayoutHeight(_bodyLayout, 110f);
                SetLayoutHeight(_transcriptLayout, string.IsNullOrWhiteSpace(DebateLearningContent.GetStrategyForStage(key)) ? 610f : 560f);
                SetLayoutHeight(_choiceButtonsLayout, 0f);
                SetLayoutHeight(_rationaleButtonsLayout, 0f);
                SetLayoutHeight(_navigationLayout, 46f);
                return;
            }

            if (IsMicroPracticeStage(key))
            {
                SetLayoutHeight(_bodyLayout, 780f);
                SetLayoutHeight(_transcriptLayout, 0f);
                SetLayoutHeight(_choiceButtonsLayout, 0f);
                SetLayoutHeight(_rationaleButtonsLayout, 0f);
                SetLayoutHeight(_navigationLayout, 46f);
                return;
            }

            if (key == DebateLearningStageKey.MicroChoice)
            {
                SetLayoutHeight(_bodyLayout, 780f);
                SetLayoutHeight(_transcriptLayout, 0f);
                SetLayoutHeight(_choiceButtonsLayout, 0f);
                SetLayoutHeight(_rationaleButtonsLayout, 0f);
                SetLayoutHeight(_navigationLayout, 46f);
                return;
            }

            SetLayoutHeight(_bodyLayout, key == DebateLearningStageKey.WarmUp ? 650f : 780f);
            SetLayoutHeight(_transcriptLayout, 0f);
            SetLayoutHeight(_choiceButtonsLayout, 0f);
            SetLayoutHeight(_rationaleButtonsLayout, 0f);
            SetLayoutHeight(_navigationLayout, 46f);
        }

        private static bool IsDemoStage(DebateLearningStageKey key)
        {
            return DebateLearningContent.GetDialogueLines(key).Length > 0;
        }

        private static bool IsMicroPracticeStage(DebateLearningStageKey key)
        {
            return key is DebateLearningStageKey.MicroPracticeSpotMissing
                or DebateLearningStageKey.MicroPracticeOneSentence
                or DebateLearningStageKey.MicroPracticeStrategyTry;
        }

        private void SetChoiceButtons(string[] options, System.Action<string> onSelected)
        {
            SetDynamicButtons(_choiceButtons, options, onSelected);
            if (_choiceButtonsRoot != null)
            {
                _choiceButtonsRoot.SetActive(options.Length > 0);
            }
        }

        private void SetRationaleButtons(string[] options, System.Action<string> onSelected)
        {
            SetDynamicButtons(_rationaleButtons, options, onSelected);
            if (_rationaleButtonsRoot != null)
            {
                _rationaleButtonsRoot.SetActive(options.Length > 0);
            }
        }

        private static void SetDynamicButtons(IReadOnlyList<Button> buttons, string[] options, System.Action<string> onSelected)
        {
            for (int i = 0; i < buttons.Count; i++)
            {
                Button button = buttons[i];
                bool active = i < options.Length;
                button.gameObject.SetActive(active);
                button.onClick.RemoveAllListeners();
                if (!active)
                {
                    continue;
                }

                string option = options[i];
                SetButtonLabel(button, option);
                if (onSelected != null)
                {
                    button.onClick.AddListener(() => onSelected(option));
                }
            }
        }

        private static void SetButtonLabel(Button button, string label)
        {
            TMP_Text text = button.GetComponentInChildren<TMP_Text>(true);
            if (text != null)
            {
                text.text = label;
            }
        }

        private void SetShortTextInputVisible(bool visible)
        {
            if (_shortTextInputRoot != null)
            {
                _shortTextInputRoot.SetActive(visible);
            }
        }

        private static void SetTextObjectActive(TMP_Text text, bool active)
        {
            if (text != null)
            {
                text.gameObject.SetActive(active);
            }
        }

        private static void SetLayoutHeight(LayoutElement layout, float height)
        {
            if (layout == null)
            {
                return;
            }

            layout.preferredHeight = height;
            layout.minHeight = height;
        }

        private static string GetStrategyLabel(DebateLearningStageKey key)
        {
            string strategy = DebateLearningContent.GetStrategyForStage(key);
            return string.IsNullOrWhiteSpace(strategy)
                ? string.Empty
                : $"Current Strategy: {strategy}";
        }

        private void SetLearningCursor(bool visible)
        {
            if (!unlockCursorDuringLearning)
            {
                return;
            }

            Cursor.lockState = visible ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = visible;
        }

        private static GameObject CreateRect(string name, Transform parent)
        {
            GameObject gameObject = new(name, typeof(RectTransform));
            gameObject.transform.SetParent(parent, false);
            return gameObject;
        }

        private static TMP_Text CreateText(
            Transform parent,
            string text,
            int fontSize,
            FontStyles style,
            float height,
            Color color)
        {
            GameObject textObject = CreateRect("Text", parent);
            TMP_Text textComponent = textObject.AddComponent<TextMeshProUGUI>();
            textComponent.text = text;
            textComponent.fontSize = fontSize;
            textComponent.fontStyle = style;
            textComponent.color = color;
            textComponent.alignment = TextAlignmentOptions.Left;
            textComponent.textWrappingMode = TextWrappingModes.Normal;
            textComponent.overflowMode = TextOverflowModes.Overflow;
            textComponent.raycastTarget = false;
            textComponent.richText = true;

            LayoutElement layout = textObject.AddComponent<LayoutElement>();
            layout.preferredHeight = height;
            layout.minHeight = height;
            return textComponent;
        }

        private static Button CreateButton(Transform parent, string label, UnityEngine.Events.UnityAction action, Color color)
        {
            return CreateButton(parent, label, action, color, 68f, 30f);
        }

        private static Button CreateButton(
            Transform parent,
            string label,
            UnityEngine.Events.UnityAction action,
            Color color,
            float height,
            float fontSize)
        {
            GameObject buttonObject = CreateRect(label, parent);
            Image image = buttonObject.AddComponent<Image>();
            image.color = color;

            Button button = buttonObject.AddComponent<Button>();
            button.targetGraphic = image;
            if (action != null)
            {
                button.onClick.AddListener(action);
            }

            LayoutElement layout = buttonObject.AddComponent<LayoutElement>();
            layout.preferredHeight = height;
            layout.minHeight = height;

            GameObject labelObject = CreateRect("Label", buttonObject.transform);
            RectTransform labelRect = labelObject.GetComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = new Vector2(12f, 4f);
            labelRect.offsetMax = new Vector2(-12f, -4f);

            TMP_Text text = labelObject.AddComponent<TextMeshProUGUI>();
            text.text = label;
            text.fontSize = fontSize;
            text.enableAutoSizing = true;
            text.fontSizeMin = 18;
            text.fontSizeMax = fontSize;
            text.fontStyle = FontStyles.Bold;
            text.color = Color.white;
            text.alignment = TextAlignmentOptions.Center;
            text.textWrappingMode = TextWrappingModes.Normal;
            text.overflowMode = TextOverflowModes.Overflow;
            text.raycastTarget = false;
            return button;
        }

        private static void ConfigureButtonVisual(Button button, float height, float fontSize)
        {
            if (button == null)
            {
                return;
            }

            LayoutElement layout = button.GetComponent<LayoutElement>();
            if (layout != null)
            {
                layout.preferredHeight = height;
                layout.minHeight = height;
            }

            TMP_Text text = button.GetComponentInChildren<TMP_Text>(true);
            if (text != null)
            {
                text.fontSize = fontSize;
                text.fontSizeMax = fontSize;
            }
        }

        private static TMP_InputField CreateInputField(Transform parent, string placeholder)
        {
            GameObject inputObject = CreateRect("Optional Short Text Input", parent);
            Image background = inputObject.AddComponent<Image>();
            background.color = new Color(1f, 1f, 1f, 0.92f);

            TMP_InputField input = inputObject.AddComponent<TMP_InputField>();
            LayoutElement layout = inputObject.AddComponent<LayoutElement>();
            layout.preferredHeight = 72f;
            layout.minHeight = 72f;

            GameObject textObject = CreateRect("Text", inputObject.transform);
            RectTransform textRect = textObject.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(18f, 8f);
            textRect.offsetMax = new Vector2(-18f, -8f);

            TMP_Text text = textObject.AddComponent<TextMeshProUGUI>();
            text.fontSize = 30;
            text.color = new Color(0.08f, 0.10f, 0.12f);
            text.alignment = TextAlignmentOptions.Left;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Ellipsis;

            GameObject placeholderObject = CreateRect("Placeholder", inputObject.transform);
            RectTransform placeholderRect = placeholderObject.GetComponent<RectTransform>();
            placeholderRect.anchorMin = Vector2.zero;
            placeholderRect.anchorMax = Vector2.one;
            placeholderRect.offsetMin = new Vector2(18f, 8f);
            placeholderRect.offsetMax = new Vector2(-18f, -8f);

            TMP_Text placeholderText = placeholderObject.AddComponent<TextMeshProUGUI>();
            placeholderText.text = placeholder;
            placeholderText.fontSize = 30;
            placeholderText.color = new Color(0.42f, 0.46f, 0.48f);
            placeholderText.alignment = TextAlignmentOptions.Left;
            placeholderText.textWrappingMode = TextWrappingModes.NoWrap;
            placeholderText.overflowMode = TextOverflowModes.Ellipsis;

            input.textComponent = text;
            input.placeholder = placeholderText;
            input.characterLimit = 140;
            input.lineType = TMP_InputField.LineType.SingleLine;
            input.targetGraphic = background;
            input.textViewport = textRect;
            return input;
        }

    }

    internal static class NpcDebateLearningRuntimeWatcher
    {
        private const string TargetSceneName = "Level_NPCVsNPCDebate";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Initialize()
        {
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            SceneManager.sceneLoaded += HandleSceneLoaded;
            AttachIfNeeded(SceneManager.GetActiveScene());
        }

        private static void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            AttachIfNeeded(scene);
        }

        private static void AttachIfNeeded(Scene scene)
        {
            if (scene.name != TargetSceneName)
            {
                return;
            }

            if (Object.FindAnyObjectByType<NpcDebateLearningPhaseController>() != null)
            {
                return;
            }

            NpcDebateRoundManager roundManager = Object.FindAnyObjectByType<NpcDebateRoundManager>();
            GameObject watcherObject = new("NPC Debate Learning Phase Runtime Watcher");
            NpcDebateLearningPhaseController controller = watcherObject.AddComponent<NpcDebateLearningPhaseController>();
            controller.Configure(roundManager);
        }
    }
}
