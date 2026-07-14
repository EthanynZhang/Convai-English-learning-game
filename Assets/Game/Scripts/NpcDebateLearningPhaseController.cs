using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System;
using Convai.Scripts.Runtime.Core;
using Convai.Scripts.Runtime.UI;
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
        private const string TargetSceneNameSuffix = "Level_NPCVsNPCDebate";
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
        [SerializeField] private bool narrateEveryLearningStage = true;
        [SerializeField] private bool useMiniMaxStageNarration = true;

        [Header("World Panel")]
        [SerializeField] private Transform panelWorldAnchor;
        [SerializeField] private Vector2 panelSize = new(1450f, 1150f);
        [SerializeField] private float panelWorldScale = 0.0025085f;
        [SerializeField] private bool useFixedPanelTransform = true;
        [SerializeField] private Vector3 fixedPanelPosition = new(-0.31524f, 1.45f, 3.7437f);
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
        private TMP_Text _voicePracticeTranscriptText;
        private TMP_Text _voicePracticeStatusText;
        private TMP_Text _voicePracticeCompletedText;
        private LayoutElement _bodyLayout;
        private LayoutElement _transcriptLayout;
        private LayoutElement _voicePracticeTranscriptLayout;
        private LayoutElement _voicePracticeStatusLayout;
        private LayoutElement _voicePracticeCompletedLayout;
        private GameObject _choiceButtonsRoot;
        private GameObject _rationaleButtonsRoot;
        private GameObject _shortTextInputRoot;
        private TMP_InputField _shortTextInput;
        private LayoutElement _choiceButtonsLayout;
        private LayoutElement _rationaleButtonsLayout;
        private LayoutElement _navigationLayout;
        private Button _replayButton;

        private int _stageIndex;
        private float _stageElapsed;
        private bool _completed;
        private bool _started;
        private Coroutine _demoRoutine;
        private Coroutine _stageNarrationRoutine;
        private DemoDialogueLine[] _currentDialogueLines = System.Array.Empty<DemoDialogueLine>();
        private int _currentDialogueLineIndex = -1;
        private ConvaiNPC _currentAudioNpc;
        private AudioSource _currentDemoAudioSource;
        private AudioClip _currentGeneratedAudioClip;
        private MiniMaxTtsClient _miniMaxTtsClient;
        private int _narrationGeneration;
        private bool _stageNarrationInProgress;
        private bool _loggedMiniMaxNarrationFallback;

        private readonly string[] _voicePracticeTranscripts = new string[DebateLearningContent.CreeiVoicePracticePrompts.Length];
        private readonly bool[] _voicePracticeConfirmed = new bool[DebateLearningContent.CreeiVoicePracticePrompts.Length];
        private readonly int[] _voicePracticeRerecordCounts = new int[DebateLearningContent.CreeiVoicePracticePrompts.Length];
        private int _voicePracticeStepIndex;
        private bool _voicePracticeRecording;
        private bool _voicePracticeAwaitingTranscript;
        private bool _voicePracticeInitialized;
        private float _voicePracticeAwaitingSince;
        private string _voicePracticeRetryStatus = string.Empty;
        private const float VoicePracticeTranscriptTimeoutSeconds = 30f;
#if CROSSTALES_RTVOICE
        private bool _loggedRtVoiceSelection;
        private float _lastRtVoiceProviderReloadTime = -999f;
        private Speaker _rtVoiceSpeaker;
        private Voice _annaRtVoice;
        private Voice _mikeRtVoice;
#endif

        private void Awake()
        {
            if (!IsTargetSceneName(SceneManager.GetActiveScene().name))
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

        internal static bool IsTargetSceneName(string sceneName)
        {
            return !string.IsNullOrWhiteSpace(sceneName) &&
                   sceneName.EndsWith(TargetSceneNameSuffix, StringComparison.OrdinalIgnoreCase);
        }

        private void Start()
        {
            if (_started || !enabled)
            {
                return;
            }

            _started = true;
            RegisterTutorialInputIsolation();
            EnterStage(0);
        }

        private void Update()
        {
            if (_completed || !_started)
            {
                return;
            }

            _stageElapsed += Time.deltaTime;
            UpdateVoicePracticeTimeout();
            HandleKeyboardInput();
            UpdateControls(CurrentStage);
        }

        private void OnDisable()
        {
            StopDemoPlayback();
            UnregisterTutorialInputIsolation();
        }

        private void OnDestroy()
        {
            UnregisterTutorialInputIsolation();
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

            _miniMaxTtsClient = GetComponent<MiniMaxTtsClient>();
            if (_miniMaxTtsClient == null)
            {
                _miniMaxTtsClient = gameObject.AddComponent<MiniMaxTtsClient>();
            }

            EnsureAudioLipSync(primaryDemoNPC);
            EnsureAudioLipSync(secondaryDemoNPC);
        }

        private static void EnsureAudioLipSync(ConvaiNPC npc)
        {
            if (npc == null)
            {
                return;
            }

            AudioSource source = npc.GetComponent<AudioSource>();
            if (source == null)
            {
                source = npc.gameObject.AddComponent<AudioSource>();
            }

            AudioDrivenNpcLipSync lipSync = npc.GetComponent<AudioDrivenNpcLipSync>();
            if (lipSync == null)
            {
                lipSync = npc.gameObject.AddComponent<AudioDrivenNpcLipSync>();
            }

            lipSync.Configure(source);
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
            _voicePracticeTranscriptText = CreateText(panel.transform, string.Empty, 32, FontStyles.Normal, 190f, new Color(0.08f, 0.10f, 0.12f));
            _voicePracticeStatusText = CreateText(panel.transform, string.Empty, 30, FontStyles.Bold, 54f, new Color(0.20f, 0.25f, 0.25f));
            _voicePracticeCompletedText = CreateText(panel.transform, string.Empty, 27, FontStyles.Normal, 54f, new Color(0.18f, 0.24f, 0.26f));
            _countdownText = CreateText(panel.transform, string.Empty, 1, FontStyles.Normal, 1f, Color.clear);
            _bodyLayout = _bodyText.GetComponent<LayoutElement>();
            _transcriptLayout = _transcriptText.GetComponent<LayoutElement>();
            _voicePracticeTranscriptLayout = _voicePracticeTranscriptText.GetComponent<LayoutElement>();
            _voicePracticeStatusLayout = _voicePracticeStatusText.GetComponent<LayoutElement>();
            _voicePracticeCompletedLayout = _voicePracticeCompletedText.GetComponent<LayoutElement>();

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

            _replayButton = CreateButton(navigation.transform, "Replay Demo", ReplayCurrentDemo, new Color(0.45f, 0.33f, 0.14f));
            ConfigureButtonVisual(_replayButton, 42f, 24f);

            CreateKeyboardNavigationHint(panel.transform);
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
            _voicePracticeTranscriptText.text = string.Empty;
            _voicePracticeStatusText.text = string.Empty;
            _voicePracticeCompletedText.text = string.Empty;
            _countdownText.text = string.Empty;
            SetChoiceButtons(System.Array.Empty<string>(), null);
            SetRationaleButtons(System.Array.Empty<string>(), null);
            SetShortTextInputVisible(false);
            ConfigureStageLayout(stage.Key);

            if (IsDemoStage(stage.Key))
            {
                _currentDialogueLines = DebateLearningContent.GetDialogueLines(stage.Key);
                SetTranscriptText(_currentDialogueLines, -1);
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
            BeginStageNarration(stage);
        }

        private void ConfigureMicroPracticeStage(DebateLearningStageKey key)
        {
            SetChoiceButtons(System.Array.Empty<string>(), null);
            SetRationaleButtons(System.Array.Empty<string>(), null);
            SetShortTextInputVisible(false);
            _shortTextInput.text = string.Empty;
            _transcriptText.text = string.Empty;

            if (IsVoicePracticeStage(key))
            {
                ConfigureVoicePracticeStage();
                return;
            }

            _voicePracticeTranscriptText.text = string.Empty;
            _voicePracticeStatusText.text = string.Empty;
            _voicePracticeCompletedText.text = string.Empty;
        }

        private void ConfigureVoicePracticeStage()
        {
            if (!_voicePracticeInitialized)
            {
                _voicePracticeInitialized = true;
                _voicePracticeStepIndex = 0;
            }

            _voicePracticeStepIndex = FindFirstUnfinishedVoicePracticeStep();
            RenderVoicePractice();
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

        private void BeginStageNarration(DebateLearningStageSpec stage)
        {
            if (!narrateEveryLearningStage || stage.IsTerminal || primaryDemoNPC == null)
            {
                if (IsDemoStage(stage.Key))
                {
                    BeginDemoStage(stage);
                }

                return;
            }

            _stageNarrationInProgress = true;
            int generation = ++_narrationGeneration;
            if (IsDemoStage(stage.Key))
            {
                _currentSpeakerText.text = "Current Speaker: Anna Reed (stage guide)";
                _demoStatusText.text = "Anna is introducing this stage...";
            }

            if (IsVoicePracticeStage(stage.Key))
            {
                _voicePracticeStatusText.text = "Anna is introducing this stage. Please wait before pressing T.";
            }

            _stageNarrationRoutine = StartCoroutine(PlayStageNarrationThenContinue(stage, generation));
        }

        private IEnumerator PlayStageNarrationThenContinue(DebateLearningStageSpec stage, int generation)
        {
            string narrationText = BuildStageNarrationText(stage);
            AudioClip generatedClip = null;
            string miniMaxError = string.Empty;

            if (useMiniMaxStageNarration && _miniMaxTtsClient != null)
            {
                yield return _miniMaxTtsClient.RequestClip(
                    narrationText,
                    generation,
                    IsNarrationGenerationCurrent,
                    clip => generatedClip = clip,
                    error => miniMaxError = error);
            }

            if (!IsNarrationGenerationCurrent(generation))
            {
                yield break;
            }

            float speechSeconds = 0f;
            if (generatedClip != null)
            {
                speechSeconds = PlayAudioClipOnNpc(generatedClip, primaryDemoNPC, true);
            }

#if CROSSTALES_RTVOICE
            if (speechSeconds <= 0f && useRtVoiceTts)
            {
                DemoDialogueLine narrationLine = new(AnnaSpeakerId, "Anna Reed", narrationText);
                TryPlayRtVoiceDemoTts(narrationLine, primaryDemoNPC, out speechSeconds);
            }
#endif

            if (speechSeconds <= 0f && !_loggedMiniMaxNarrationFallback)
            {
                _loggedMiniMaxNarrationFallback = true;
                string reason = string.IsNullOrWhiteSpace(miniMaxError)
                    ? "No MiniMax or RT-Voice speech provider was available."
                    : miniMaxError;
                Debug.LogWarning("Anna stage narration could not play. The tutorial will continue without blocking. " + reason);
            }

            if (speechSeconds > 0f)
            {
                yield return new WaitForSeconds(speechSeconds + 0.15f);
            }

            if (!IsNarrationGenerationCurrent(generation))
            {
                yield break;
            }

            StopCurrentDemoAudio();
            _stageNarrationInProgress = false;
            _stageNarrationRoutine = null;

            if (IsDemoStage(stage.Key))
            {
                BeginDemoStage(stage);
            }
            else if (IsVoicePracticeStage(stage.Key))
            {
                RenderVoicePractice();
            }
        }

        private bool IsNarrationGenerationCurrent(int generation)
        {
            return enabled && !_completed && generation == _narrationGeneration;
        }

        private static string BuildStageNarrationText(DebateLearningStageSpec stage)
        {
            string body = (stage.Body ?? string.Empty)
                .Replace("\r", " ")
                .Replace("\n", " ")
                .Replace("_", string.Empty);
            return string.IsNullOrWhiteSpace(body)
                ? stage.Title
                : stage.Title + ". " + body;
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
                float voiceSeconds = 0f;
                if (string.Equals(line.SpeakerId, AnnaSpeakerId, StringComparison.OrdinalIgnoreCase))
                {
                    yield return PlayAnnaDialogueLineWithStageVoice(
                        line,
                        speaker,
                        seconds => voiceSeconds = seconds);
                }
                else
                {
                    voiceSeconds = PlayDialogueLine(CurrentStage.Key, i, line, speaker);
                }

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

        private IEnumerator PlayAnnaDialogueLineWithStageVoice(
            DemoDialogueLine line,
            ConvaiNPC speaker,
            Action<float> onCompleted)
        {
            if (!playNpcVoice || speaker == null || string.IsNullOrWhiteSpace(line.Text))
            {
                onCompleted?.Invoke(0f);
                yield break;
            }

            int generation = _narrationGeneration;
            AudioClip generatedClip = null;
            string miniMaxError = string.Empty;
            if (useMiniMaxStageNarration && _miniMaxTtsClient != null)
            {
                yield return _miniMaxTtsClient.RequestClip(
                    line.Text,
                    generation,
                    IsNarrationGenerationCurrent,
                    clip => generatedClip = clip,
                    error => miniMaxError = error);
            }

            if (!IsNarrationGenerationCurrent(generation))
            {
                yield break;
            }

            float speechSeconds = 0f;
            if (generatedClip != null)
            {
                speechSeconds = PlayAudioClipOnNpc(generatedClip, speaker, true);
            }

#if CROSSTALES_RTVOICE
            if (speechSeconds <= 0f && useRtVoiceTts)
            {
                TryPlayRtVoiceDemoTts(line, speaker, out speechSeconds);
            }
#endif

            if (speechSeconds <= 0f)
            {
                string reason = string.IsNullOrWhiteSpace(miniMaxError)
                    ? "No MiniMax or RT-Voice speech provider was available."
                    : miniMaxError;
                Debug.LogWarning("Anna dialogue demo voice could not play. " + reason);
            }

            onCompleted?.Invoke(speechSeconds);
        }

        private void ReplayCurrentDemo()
        {
            if (!IsDemoStage(CurrentStage.Key) || _stageNarrationInProgress)
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
            if (IsVoicePracticeStage(CurrentStage.Key) && Input.GetKeyDown(KeyCode.T))
            {
                if (_stageNarrationInProgress)
                {
                    return;
                }

                ToggleVoicePracticeRecording();
                return;
            }

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
                    AdvanceStage();
                    break;
                case KeyCode.LeftArrow:
                    PreviousStage();
                    break;
                case KeyCode.DownArrow:
                    if (IsDemoStage(CurrentStage.Key))
                    {
                        ReplayCurrentDemo();
                    }
                    break;
                case KeyCode.UpArrow:
                    if (IsDemoStage(CurrentStage.Key))
                    {
                        ReplayCurrentDemo();
                    }
                    break;
            }
        }

        private void PreviousStage()
        {
            if (IsVoicePracticeStage(CurrentStage.Key) && IsVoicePracticeInputBlocked())
            {
                return;
            }

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
            if (IsVoicePracticeStage(CurrentStage.Key))
            {
                ConfirmVoicePracticeStep();
                return;
            }

            AdvanceToNextStage();
        }

        private void AdvanceToNextStage()
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
            UnregisterTutorialInputIsolation();
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
            _narrationGeneration++;
            _stageNarrationInProgress = false;
            if (_stageNarrationRoutine != null)
            {
                StopCoroutine(_stageNarrationRoutine);
                _stageNarrationRoutine = null;
            }

            if (_demoRoutine != null)
            {
                StopCoroutine(_demoRoutine);
                _demoRoutine = null;
            }

            StopCurrentDemoAudio();
        }

        private float PlayAudioClipOnNpc(AudioClip clip, ConvaiNPC speaker, bool destroyWhenStopped)
        {
            if (clip == null || speaker == null)
            {
                return 0f;
            }

            AudioSource audioSource = speaker.GetComponent<AudioSource>();
            if (audioSource == null)
            {
                audioSource = speaker.gameObject.AddComponent<AudioSource>();
                EnsureAudioLipSync(speaker);
            }

            StopCurrentDemoAudio();
            speaker.StopAllAudioPlayback();
            audioSource.clip = clip;
            audioSource.Play();

            _currentAudioNpc = speaker;
            _currentDemoAudioSource = audioSource;
            _currentGeneratedAudioClip = destroyWhenStopped ? clip : null;
            SetDemoTalkingState(speaker, true);
            return clip.length;
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

            clipSeconds = PlayAudioClipOnNpc(clip, speaker, false);
            return clipSeconds > 0f;
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
                if (_currentGeneratedAudioClip != null)
                {
                    Destroy(_currentGeneratedAudioClip);
                    _currentGeneratedAudioClip = null;
                }

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
            if (_currentGeneratedAudioClip != null)
            {
                Destroy(_currentGeneratedAudioClip);
                _currentGeneratedAudioClip = null;
            }
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
            SetTextObjectActive(_progressText, true);
            SetTextObjectActive(_titleText, true);

            if (IsVoicePracticeStage(stage.Key))
            {
                UpdateVoicePracticeControls();
                return;
            }

            bool isDemo = IsDemoStage(stage.Key);
            bool isMicroChoice = stage.Key == DebateLearningStageKey.MicroChoice;
            bool isMicroPractice = IsMicroPracticeStage(stage.Key);
            bool showsReflectionText = isMicroChoice || isMicroPractice;

            if (_replayButton != null)
            {
                _replayButton.gameObject.SetActive(isDemo);
                _replayButton.interactable = isDemo && !_stageNarrationInProgress;
            }

            SetTextObjectActive(_strategyLabelText, !string.IsNullOrWhiteSpace(_strategyLabelText != null ? _strategyLabelText.text : string.Empty));
            SetTextObjectActive(_currentSpeakerText, isDemo);
            SetTextObjectActive(_demoStatusText, isDemo);
            SetTextObjectActive(_transcriptText, isDemo || showsReflectionText);
            SetTextObjectActive(_voicePracticeTranscriptText, false);
            SetTextObjectActive(_voicePracticeStatusText, false);
            SetTextObjectActive(_voicePracticeCompletedText, false);

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

        private void UpdateVoicePracticeControls()
        {
            SetTextObjectActive(_progressText, true);
            SetTextObjectActive(_titleText, true);

            if (_replayButton != null)
            {
                _replayButton.gameObject.SetActive(false);
                _replayButton.interactable = false;
            }

            SetTextObjectActive(_strategyLabelText, false);
            SetTextObjectActive(_currentSpeakerText, false);
            SetTextObjectActive(_demoStatusText, false);
            SetTextObjectActive(_transcriptText, false);
            SetTextObjectActive(_voicePracticeTranscriptText, true);
            SetTextObjectActive(_voicePracticeStatusText, true);
            SetTextObjectActive(_voicePracticeCompletedText, true);

            if (_choiceButtonsRoot != null)
            {
                _choiceButtonsRoot.SetActive(false);
            }

            if (_rationaleButtonsRoot != null)
            {
                _rationaleButtonsRoot.SetActive(false);
            }
        }

        private void ConfigureStageLayout(DebateLearningStageKey key)
        {
            if (IsDemoStage(key))
            {
                SetLayoutHeight(_bodyLayout, 110f);
                SetLayoutHeight(_transcriptLayout, string.IsNullOrWhiteSpace(DebateLearningContent.GetStrategyForStage(key)) ? 610f : 560f);
                SetLayoutHeight(_voicePracticeTranscriptLayout, 0f);
                SetLayoutHeight(_voicePracticeStatusLayout, 0f);
                SetLayoutHeight(_voicePracticeCompletedLayout, 0f);
                SetLayoutHeight(_choiceButtonsLayout, 0f);
                SetLayoutHeight(_rationaleButtonsLayout, 0f);
                SetLayoutHeight(_navigationLayout, 46f);
                return;
            }

            if (IsMicroPracticeStage(key))
            {
                SetLayoutHeight(_bodyLayout, IsVoicePracticeStage(key) ? 360f : 780f);
                SetLayoutHeight(_transcriptLayout, 0f);
                SetLayoutHeight(_voicePracticeTranscriptLayout, IsVoicePracticeStage(key) ? 190f : 0f);
                SetLayoutHeight(_voicePracticeStatusLayout, IsVoicePracticeStage(key) ? 54f : 0f);
                SetLayoutHeight(_voicePracticeCompletedLayout, IsVoicePracticeStage(key) ? 54f : 0f);
                SetLayoutHeight(_choiceButtonsLayout, 0f);
                SetLayoutHeight(_rationaleButtonsLayout, 0f);
                SetLayoutHeight(_navigationLayout, 0f);
                return;
            }

            if (key == DebateLearningStageKey.MicroChoice)
            {
                SetLayoutHeight(_bodyLayout, 780f);
                SetLayoutHeight(_transcriptLayout, 0f);
                SetLayoutHeight(_choiceButtonsLayout, 0f);
                SetLayoutHeight(_rationaleButtonsLayout, 0f);
                SetLayoutHeight(_navigationLayout, 0f);
                return;
            }

            SetLayoutHeight(_bodyLayout, key == DebateLearningStageKey.WarmUp ? 650f : 780f);
            SetLayoutHeight(_transcriptLayout, 0f);
            SetLayoutHeight(_voicePracticeTranscriptLayout, 0f);
            SetLayoutHeight(_voicePracticeStatusLayout, 0f);
            SetLayoutHeight(_voicePracticeCompletedLayout, 0f);
            SetLayoutHeight(_choiceButtonsLayout, 0f);
            SetLayoutHeight(_rationaleButtonsLayout, 0f);
            SetLayoutHeight(_navigationLayout, 0f);
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

        private static bool IsVoicePracticeStage(DebateLearningStageKey key)
        {
            return key == DebateLearningStageKey.MicroPracticeOneSentence;
        }

        private bool IsVoicePracticeInputBlocked()
        {
            return _voicePracticeRecording || _voicePracticeAwaitingTranscript;
        }

        private int FindFirstUnfinishedVoicePracticeStep()
        {
            for (int i = 0; i < _voicePracticeConfirmed.Length; i++)
            {
                if (!_voicePracticeConfirmed[i])
                {
                    return i;
                }
            }

            return _voicePracticeConfirmed.Length - 1;
        }

        private void ToggleVoicePracticeRecording()
        {
            if (IsVoicePracticeInputBlocked() && !_voicePracticeRecording)
            {
                return;
            }

            if (_voicePracticeRecording)
            {
                StopVoicePracticeRecording();
            }
            else
            {
                StartVoicePracticeRecording();
            }
        }

        private void StartVoicePracticeRecording()
        {
            if (_voicePracticeStepIndex < 0 || _voicePracticeStepIndex >= _voicePracticeTranscripts.Length ||
                _voicePracticeConfirmed[_voicePracticeStepIndex])
            {
                return;
            }

            if (primaryDemoNPC == null)
            {
                SetVoicePracticeRetryStatus("Practice NPC is unavailable. Press T to retry.");
                return;
            }

            if (MicrophoneManager.Instance == null || !MicrophoneManager.Instance.HasAnyMicrophoneDevices())
            {
                SetVoicePracticeRetryStatus("No microphone is available. Press T to retry.");
                return;
            }

            try
            {
                PrepareVoicePracticeRerecord();
                SilenceConvaiAgentResponse(primaryDemoNPC);
                _voicePracticeRetryStatus = string.Empty;
                _voicePracticeRecording = true;
                _voicePracticeAwaitingTranscript = false;
                ConvaiNPCManager.Instance?.SetActiveConvaiNPC(primaryDemoNPC);
                primaryDemoNPC.StartListening();
                RenderVoicePractice();
            }
            catch (Exception exception)
            {
                Debug.LogWarning("CREEI voice practice recording could not start: " + exception.Message);
                SetVoicePracticeRetryStatus("Recording could not start. Press T to retry.");
            }
        }

        private void StopVoicePracticeRecording()
        {
            if (!_voicePracticeRecording)
            {
                return;
            }

            _voicePracticeRecording = false;
            _voicePracticeAwaitingTranscript = true;
            _voicePracticeAwaitingSince = Time.unscaledTime;
            try
            {
                primaryDemoNPC?.StopListening();
                SilenceConvaiAgentResponse(primaryDemoNPC);
                _voicePracticeRetryStatus = string.Empty;
                RenderVoicePractice();
            }
            catch (Exception exception)
            {
                Debug.LogWarning("CREEI voice practice recording stream could not stop: " + exception.Message);
                SetVoicePracticeRetryStatus("The recording stream failed. Press T to retry.");
            }
        }

        private void PrepareVoicePracticeRerecord()
        {
            if (!string.IsNullOrWhiteSpace(_voicePracticeTranscripts[_voicePracticeStepIndex]))
            {
                _voicePracticeRerecordCounts[_voicePracticeStepIndex]++;
                _metrics.RecordMicroPractice2Rerecord();
                _voicePracticeTranscripts[_voicePracticeStepIndex] = string.Empty;
            }
        }

        private bool TryHandleVoicePracticeTranscript(string transcript)
        {
            if (!IsVoicePracticeStage(CurrentStage.Key) || !_voicePracticeAwaitingTranscript)
            {
                return false;
            }

            string safeTranscript = transcript?.Trim() ?? string.Empty;
            _voicePracticeAwaitingTranscript = false;
            _voicePracticeRecording = false;
            if (string.IsNullOrWhiteSpace(safeTranscript))
            {
                SetVoicePracticeRetryStatus("No speech was detected. Press T to retry.");
                return true;
            }

            _voicePracticeRetryStatus = string.Empty;
            _voicePracticeTranscripts[_voicePracticeStepIndex] = safeTranscript;
            SilenceConvaiAgentResponse(primaryDemoNPC);
            RenderVoicePractice();
            UpdateVoicePracticeControls();
            return true;
        }

        private void UpdateVoicePracticeTimeout()
        {
            if (!IsVoicePracticeStage(CurrentStage.Key) || !_voicePracticeAwaitingTranscript)
            {
                return;
            }

            if (Time.unscaledTime - _voicePracticeAwaitingSince >= VoicePracticeTranscriptTimeoutSeconds)
            {
                SetVoicePracticeRetryStatus("No final transcript arrived. Press T to retry.");
            }
        }

        private void SetVoicePracticeRetryStatus(string status)
        {
            _voicePracticeRecording = false;
            _voicePracticeAwaitingTranscript = false;
            _voicePracticeRetryStatus = status ?? string.Empty;
            RenderVoicePractice();
            UpdateVoicePracticeControls();
        }

        private void ConfirmVoicePracticeStep()
        {
            if (IsVoicePracticeInputBlocked())
            {
                return;
            }

            string transcript = _voicePracticeTranscripts[_voicePracticeStepIndex];
            if (string.IsNullOrWhiteSpace(transcript))
            {
                return;
            }

            CreeiPartKey part = DebateLearningContent.CreeiVoicePracticePrompts[_voicePracticeStepIndex].Part;
            _metrics.RecordMicroPractice2ConfirmedPart(part, transcript);
            _voicePracticeConfirmed[_voicePracticeStepIndex] = true;

            if (_voicePracticeConfirmed.All(confirmed => confirmed))
            {
                AdvanceToNextStage();
                return;
            }

            _voicePracticeStepIndex = FindFirstUnfinishedVoicePracticeStep();
            RenderVoicePractice();
            UpdateVoicePracticeControls();
        }

        private void RenderVoicePractice()
        {
            if (_voicePracticeTranscriptText == null || !IsVoicePracticeStage(CurrentStage.Key))
            {
                return;
            }

            CreeiVoicePracticePrompt prompt = DebateLearningContent.CreeiVoicePracticePrompts[_voicePracticeStepIndex];
            _progressText.text = $"CREEI Step {_voicePracticeStepIndex + 1} / {DebateLearningContent.CreeiVoicePracticePrompts.Length}";
            _titleText.text = $"Micro Practice 2: {prompt.Title}";
            _bodyText.text = "Debate topic\n" + DebateLearningContent.CreeiVoicePracticeTopic + "\n\n" + prompt.Prompt;

            string transcript = _voicePracticeTranscripts[_voicePracticeStepIndex];
            _voicePracticeTranscriptText.text = string.IsNullOrWhiteSpace(transcript)
                ? "Transcript (read-only):\n—"
                : "Transcript (read-only):\n" + transcript;

            if (_voicePracticeRecording)
            {
                _voicePracticeStatusText.text = "Listening... Press T to stop.";
            }
            else if (_voicePracticeAwaitingTranscript)
            {
                _voicePracticeStatusText.text = "Transcribing...";
            }
            else if (!string.IsNullOrWhiteSpace(_voicePracticeRetryStatus))
            {
                _voicePracticeStatusText.text = _voicePracticeRetryStatus;
            }
            else if (string.IsNullOrWhiteSpace(transcript))
            {
                _voicePracticeStatusText.text = "Press T to start speaking.";
            }
            else
            {
                _voicePracticeStatusText.text = "Transcript ready. Press Right Arrow to continue.";
            }

            List<string> completed = new();
            for (int i = 0; i < _voicePracticeConfirmed.Length; i++)
            {
                if (_voicePracticeConfirmed[i])
                {
                    completed.Add(DebateLearningContent.CreeiVoicePracticePrompts[i].Title);
                }
            }

            _voicePracticeCompletedText.text = completed.Count == 0
                ? "Completed: none"
                : "Completed: " + string.Join(", ", completed);
        }

        private void RegisterTutorialInputIsolation()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            ConvaiGRPCAPI.TryHandleUserVoiceTranscript = TryHandleTutorialVoiceTranscript;
            ConvaiGRPCAPI.ShouldSuppressVoiceResponse = ShouldSuppressTutorialVoiceResponse;
            ConvaiInputManager.ShouldSuppressTalkInput = ShouldSuppressTutorialTalkInput;
            ConvaiPlayerInteractionManager.TryHandleTextSubmission = TryHandleTutorialTextSubmission;
            ConvaiPlayerInteractionManager.ShouldSuppressChatToggle = ShouldSuppressTutorialTalkInput;
            ConvaiPlayerInteractionManager.ShouldSuppressTalkInput = ShouldSuppressTutorialTalkInput;
            ConvaiPlayerInteractionManager.ShouldSuppressNpcInteraction = ShouldSuppressTutorialTalkInput;
            ConvaiNPCManager.ShouldSuppressAutoActiveNPCUpdate = ShouldSuppressVoicePracticeAutoActiveNpcUpdate;
        }

        private void UnregisterTutorialInputIsolation()
        {
            if (ConvaiGRPCAPI.TryHandleUserVoiceTranscript == (Func<string, bool>)TryHandleTutorialVoiceTranscript)
            {
                ConvaiGRPCAPI.TryHandleUserVoiceTranscript = null;
            }

            if (ConvaiGRPCAPI.ShouldSuppressVoiceResponse == (Func<bool>)ShouldSuppressTutorialVoiceResponse)
            {
                ConvaiGRPCAPI.ShouldSuppressVoiceResponse = null;
                ConvaiGRPCAPI.Instance?.ResetVoiceResponseSuppression();
            }

            if (ConvaiInputManager.ShouldSuppressTalkInput == (Func<bool>)ShouldSuppressTutorialTalkInput)
            {
                ConvaiInputManager.ShouldSuppressTalkInput = null;
            }

            if (ConvaiPlayerInteractionManager.TryHandleTextSubmission == (Func<string, bool>)TryHandleTutorialTextSubmission)
            {
                ConvaiPlayerInteractionManager.TryHandleTextSubmission = null;
            }

            if (ConvaiPlayerInteractionManager.ShouldSuppressChatToggle == (Func<bool>)ShouldSuppressTutorialTalkInput)
            {
                ConvaiPlayerInteractionManager.ShouldSuppressChatToggle = null;
            }

            if (ConvaiPlayerInteractionManager.ShouldSuppressTalkInput == (Func<bool>)ShouldSuppressTutorialTalkInput)
            {
                ConvaiPlayerInteractionManager.ShouldSuppressTalkInput = null;
            }

            if (ConvaiPlayerInteractionManager.ShouldSuppressNpcInteraction == (Func<bool>)ShouldSuppressTutorialTalkInput)
            {
                ConvaiPlayerInteractionManager.ShouldSuppressNpcInteraction = null;
            }

            if (ConvaiNPCManager.ShouldSuppressAutoActiveNPCUpdate == (Func<bool>)ShouldSuppressVoicePracticeAutoActiveNpcUpdate)
            {
                ConvaiNPCManager.ShouldSuppressAutoActiveNPCUpdate = null;
            }
        }

        private bool TryHandleTutorialVoiceTranscript(string transcript)
        {
            if (!IsTutorialActive())
            {
                return false;
            }

            if (IsVoicePracticeStage(CurrentStage.Key) && _voicePracticeAwaitingTranscript)
            {
                TryHandleVoicePracticeTranscript(transcript);
            }

            return true;
        }

        private bool TryHandleTutorialTextSubmission(string text)
        {
            return IsTutorialActive();
        }

        private bool ShouldSuppressTutorialTalkInput()
        {
            return IsTutorialActive();
        }

        private bool ShouldSuppressTutorialVoiceResponse()
        {
            return IsTutorialActive();
        }

        private static void SilenceConvaiAgentResponse(ConvaiNPC npc)
        {
            if (npc == null)
            {
                return;
            }

            npc.StopAllAudioPlayback();
            npc.ClearResponseQueue();
            npc.StopLipSync();
            npc.ResetCharacterAnimation();
        }

        private bool IsTutorialActive()
        {
            return enabled && _started && !_completed;
        }

        private bool ShouldSuppressVoicePracticeAutoActiveNpcUpdate()
        {
            return IsTutorialActive() && IsVoicePracticeStage(CurrentStage.Key);
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

        private static void CreateKeyboardNavigationHint(Transform parent)
        {
            GameObject hintRoot = CreateRect("Learning Keyboard Hint", parent);
            Image hintBackground = hintRoot.AddComponent<Image>();
            hintBackground.color = new Color(0.86f, 0.89f, 0.89f, 0.96f);
            hintBackground.raycastTarget = false;

            HorizontalLayoutGroup hintLayout = hintRoot.AddComponent<HorizontalLayoutGroup>();
            hintLayout.padding = new RectOffset(18, 18, 8, 8);
            hintLayout.spacing = 42f;
            hintLayout.childAlignment = TextAnchor.MiddleCenter;
            hintLayout.childControlHeight = true;
            hintLayout.childControlWidth = true;
            hintLayout.childForceExpandHeight = true;
            hintLayout.childForceExpandWidth = true;

            LayoutElement rootLayout = hintRoot.AddComponent<LayoutElement>();
            rootLayout.preferredHeight = 96f;
            rootLayout.minHeight = 96f;

            CreateKeyboardHintItem(hintRoot.transform, "\u2190", "Previous");
            CreateKeyboardHintItem(hintRoot.transform, "\u2192", "Next / Confirm");
        }

        private static void CreateKeyboardHintItem(Transform parent, string keySymbol, string label)
        {
            GameObject item = CreateRect(label + " Hint", parent);
            HorizontalLayoutGroup itemLayout = item.AddComponent<HorizontalLayoutGroup>();
            itemLayout.spacing = 14f;
            itemLayout.childAlignment = TextAnchor.MiddleCenter;
            itemLayout.childControlHeight = true;
            itemLayout.childControlWidth = true;
            itemLayout.childForceExpandHeight = false;
            itemLayout.childForceExpandWidth = false;

            LayoutElement itemSize = item.AddComponent<LayoutElement>();
            itemSize.flexibleWidth = 1f;
            itemSize.preferredHeight = 76f;

            GameObject keycap = CreateRect(keySymbol + " Keycap", item.transform);
            Image keycapImage = keycap.AddComponent<Image>();
            keycapImage.color = new Color(0.12f, 0.17f, 0.19f, 1f);
            keycapImage.raycastTarget = false;

            LayoutElement keycapSize = keycap.AddComponent<LayoutElement>();
            keycapSize.preferredWidth = 88f;
            keycapSize.minWidth = 88f;
            keycapSize.preferredHeight = 70f;
            keycapSize.minHeight = 70f;

            GameObject glyph = CreateRect("Key Glyph", keycap.transform);
            RectTransform glyphRect = glyph.GetComponent<RectTransform>();
            glyphRect.anchorMin = Vector2.zero;
            glyphRect.anchorMax = Vector2.one;
            glyphRect.offsetMin = Vector2.zero;
            glyphRect.offsetMax = Vector2.zero;

            TMP_Text keyText = glyph.AddComponent<TextMeshProUGUI>();
            keyText.text = keySymbol;
            keyText.fontSize = 42f;
            keyText.fontStyle = FontStyles.Bold;
            keyText.color = Color.white;
            keyText.alignment = TextAlignmentOptions.Center;
            keyText.raycastTarget = false;

            TMP_Text labelText = CreateText(
                item.transform,
                label,
                36,
                FontStyles.Bold,
                70f,
                new Color(0.10f, 0.16f, 0.18f));
            labelText.alignment = TextAlignmentOptions.MidlineLeft;
            LayoutElement labelSize = labelText.GetComponent<LayoutElement>();
            labelSize.preferredWidth = 320f;
            labelSize.minWidth = 280f;
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
            if (!NpcDebateLearningPhaseController.IsTargetSceneName(scene.name))
            {
                return;
            }

            if (UnityEngine.Object.FindAnyObjectByType<NpcDebateLearningPhaseController>() != null)
            {
                return;
            }

            NpcDebateRoundManager roundManager = UnityEngine.Object.FindAnyObjectByType<NpcDebateRoundManager>();
            GameObject watcherObject = new("NPC Debate Learning Phase Runtime Watcher");
            NpcDebateLearningPhaseController controller = watcherObject.AddComponent<NpcDebateLearningPhaseController>();
            controller.Configure(roundManager);
        }
    }
}
