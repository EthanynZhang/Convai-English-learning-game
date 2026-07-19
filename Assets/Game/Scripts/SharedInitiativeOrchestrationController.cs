using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using Convai.Scripts.Runtime.Core;
using Convai.Scripts.Runtime.Addons;
using Convai.Scripts.Runtime.Features;
using Convai.Scripts.Runtime.UI;
using Convai.Scripts.Runtime.Utils;
using Service;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Game.Debate
{
    public enum OrchestrationPhase
    {
        Intro,
        OpponentGenerating,
        OpponentSpeaking,
        WaitingForPlayer,
        PlayerResponseReady,
        CoachGenerating,
        CoachSuggestionReady,
        MockDebateOpponentSpeaking,
        MockDebateReady,
        MockDebateSpeaking,
        MockDebateAwaitingTranscript,
        MockDebateEvaluating,
        PracticeCycleReady,
        PracticeCycleSpeaking,
        PracticeCycleAwaitingTranscript,
        PracticeCycleConfirmingTranscript,
        PracticeCycleDiagnosing,
        PracticeCycleCoach,
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
        public const int PracticeCycleCount = 3;

        private enum RealtimeCapturePurpose
        {
            None,
            PracticeStage,
            PracticeCycle,
            MockDebate
        }

        public const bool DefaultAutomaticCoachAfterPlayerVoice = true;
        public const bool DefaultShowManualPauseButton = false;
        private const float MockDebateMinimumResponseTimeoutSeconds = 90f;

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
        [SerializeField] private string opponentStance = "Reading is more important for learning English.";
        [SerializeField] private string taskGoal = "Practice debate with post-turn strategy coaching.";
        [SerializeField] private string selectedStrategy = "Logos";
        [SerializeField] private string[] previousCommands = Array.Empty<string>();
        [SerializeField] private string[] previousNpcVersionsViewed = Array.Empty<string>();

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
        [SerializeField, Min(30f)] private float mockDebateTargetSeconds = 180f;
        [SerializeField] private bool useWindowsTtsFallbackWhenConvaiSilent = false;
        [SerializeField] private float convaiSpeechFallbackDelaySeconds = 5f;
        [SerializeField] [Range(0.75f, 1.05f)] private float coachVoicePitch = 0.92f;
        [SerializeField]
        private string coachSpeechStyleInstruction =
            "Use a warm, gentle, supportive adult female coaching voice. Speak calmly and softly.";

        [Header("UI")]
        [SerializeField] private Canvas uiCanvas;
        [SerializeField] private GameObject legacyInteractiveControls;
        [SerializeField] private GameObject legacyStartButton;
        [SerializeField] private GameObject legacyRoundTimer;
        [SerializeField] private InteractiveDebateTranscriptBridge transcriptBridge;

        [Header("Realtime Transcription")]
        [SerializeField] private XfyunRealtimeTranscriber realtimeTranscriber;
        [SerializeField] private CoachExperimentView experimentView;
        [SerializeField] private CoachEpisodeController episodeController;

        public ConditionCSessionRecord SessionRecord = new();
        public OrchestrationPhase Phase { get; private set; } = OrchestrationPhase.Intro;

        private readonly StringBuilder _opponentTranscript = new();
        private readonly StringBuilder _livePlayerVoiceTranscript = new();
        private readonly StringBuilder _completeMockDebateTranscript = new();
        private readonly StringBuilder _coachSpeechTranscript = new();
        private readonly List<CoachFeedbackLogRow> _pendingFeedbackRows = new();
        private readonly string[] _opponentCreeiStatements = new string[5];
        private readonly string[] _learnerCreeiResponses = new string[5];

        private DebateCoachFeedbackGenerator _coachGenerator;
        private CreeiOpponentStatementGenerator _opponentGenerator;
        private MockDebateEvaluationGenerator _mockDebateGenerator;
        private DebateCoachLogger _coachLogger;
        private GameObject _root;
        private GameObject _debugRoot;
        private GameObject _opponentHeadCaptionRoot;
        private GameObject _coachHeadCaptionRoot;
        private TMP_Text _titleText;
        private TMP_Text _taskText;
        private TMP_Text _stageText;
        private TMP_Text _mockDebateTimerText;
        private TMP_Text _statusText;
        private TMP_Text _playerText;
        private TMP_Text _coachText;
        private TMP_Text _debugStatusText;
        private TMP_Text _opponentHeadCaptionText;
        private TMP_Text _coachHeadCaptionText;
        private TMP_Text _coachBoardTitleText;
        private TMP_Text _coachBoardStageText;
        private TMP_Text _coachBoardFeedbackText;
        private ScrollRect _opponentHeadCaptionScrollRect;
        private ScrollRect _coachHeadCaptionScrollRect;
        private ScrollRect _coachBoardScrollRect;
        private TMP_InputField _learnerNoteInput;
        private TMP_InputField _debugPromptInput;
        private TMP_InputField _debugResultInput;
        private Toggle _detailToggle;
        private Button _startButton;
        private Button _askCoachButton;
        private Button _continueButton;
        private Button _exampleButton;
        private Button _endButton;
        private GameObject _stageSelectorRoot;
        private GameObject _coachBoardRoot;
        private readonly Button[] _stageSelectionButtons = new Button[6];
        private int _debugLayoutScreenWidth = -1;
        private int _debugLayoutScreenHeight = -1;
        private Coroutine _coachRoutine;
        private Coroutine _opponentRoutine;
        private Coroutine _mockDebateRoutine;
        private Coroutine _mockDebateTranscriptFallbackRoutine;
        private Coroutine _coachSpeechGuardRoutine;
        private Coroutine _coachSpeechRetryRoutine;
        private Coroutine _opponentCaptionHideRoutine;
        private Coroutine _coachCaptionHideRoutine;
        private bool _isUiMode;
        private bool _legacySpeechBubblesDisabled;
        private int _turnIndex;
        private int _creeiStageIndex;
        private int _coachRequestVersion;
        private int _opponentRequestVersion;
        private int _mockDebateRequestVersion;
        private bool _exampleRequestedForCurrentTurn;
        private bool _mockDebateFeedbackReady;
        private bool _coachFeedbackSpeechActive;
        private bool _coachFeedbackSentToAnna;
        private bool _coachAudioObservedThisAttempt;
        private int _coachSpeechRetryCount;
        private string _pendingCoachSpeechText = string.Empty;
        private CoachFeedbackRequest _latestCoachRequest;
        private CoachFeedbackResult _latestCoachResult;
        private string _latestPlayerUtterance = string.Empty;
        private string _latestFeedbackText = string.Empty;
        private string _mockDebateTranscript = string.Empty;
        private RealtimeCapturePurpose _realtimeCapturePurpose;
        private bool _practiceVoiceRecording;
        private bool _practiceVoiceAwaitingTranscript;
        private float _mockDebateStartedAt;
        private float _mockDebateDurationSeconds;
        private bool _mockDebateRecording;
        private bool _mockDebateAutoStopRequested;
        private bool _mockDebateLeoTimerRunning;
        private bool _mockDebateLeoHasStartedTalking;
        private float _mockDebateLeoStartedAt;
        private float _mockDebateLeoDurationSeconds;
        private string[] _mockDebateLeoSpeechSegments = Array.Empty<string>();
        private int _mockDebateLeoSegmentIndex;
        private readonly CoachPolicyConfig _coachPolicyConfig = CoachPolicyConfig.CreateDefault();
        private ICoachDiagnosisEngine _diagnosisEngine;
        private CoachResearchLogger _researchLogger;
        private Coroutine _diagnosisRoutine;
        private Coroutine _experimentFeedbackRoutine;
        private Coroutine _practiceCycleTranscriptTimeoutRoutine;
        private CoachDiagnosisResult _practiceDiagnosis;
        private CoachOrchestrationMode _orchestrationMode = CoachOrchestrationMode.Disabled;
        private int _practiceCycleIndex;
        private float _practiceCycleStartedAt;
        private float _practiceCycleDurationSeconds;
        private bool _practiceCycleRecording;
        private bool _practiceCycleAutoStopRequested;
        private bool _practiceCycleTechnicalFallback;
        private bool _studyStarted;
        private string _practiceCycleTranscript = string.Empty;
        private string _confirmedPracticeTranscript = string.Empty;
        private string _lastExperimentFeedback = string.Empty;
        private CoachFeedbackLevel _lastExperimentFeedbackLevel = CoachFeedbackLevel.Level2;
        private ConvaiPlayerMovement[] _playerMovementComponents = Array.Empty<ConvaiPlayerMovement>();
        private bool[] _playerMovementOriginalStates = Array.Empty<bool>();
        private bool _playerMovementFrozen;
        private bool _isShuttingDown;
        private CursorLockMode _originalCursorLockMode;
        private bool _originalCursorVisible;

        private static readonly string[] MockDebateLeoCreeiSegments =
        {
            "Reading is more important than speaking when students are building a strong foundation in English. Speaking practice certainly matters, but learners need words, sentence patterns, ideas, and examples before they can express themselves clearly. Regular reading supplies that language in a rich and organized form. For this reason, reading should receive greater priority during the early and intermediate stages of English learning. This priority does not silence students; it prepares them to speak with more substance and control.",
            "The main reason is that reading gives learners repeated contact with accurate English while allowing enough time to notice how the language works. A reader can pause, reread a difficult sentence, infer a word from context, and compare several ways of expressing the same idea. Speaking usually happens quickly, so learners often reuse the limited language they already know instead of discovering more precise vocabulary and structures. Reading also exposes them to collocations and transitions that are difficult to invent during spontaneous talk.",
            "For example, imagine two students preparing to discuss environmental problems. One student only practices spontaneous conversation and repeatedly uses simple words such as good, bad, and important. The other student first reads short articles about pollution, recycling, and renewable energy. During the discussion, the second student can describe specific causes, compare solutions, and support claims with relevant examples because the reading supplied both knowledge and useful language. The difference is not personality or fluency practice alone; it is the quality of language input available.",
            "This example shows why reading strengthens later speaking rather than competing with it. The second student speaks more effectively because the articles provided vocabulary, sentence models, evidence, and a clearer understanding of the topic. Reading turns unfamiliar language into material that learners can recognize, organize, and eventually use for themselves. Speaking then becomes an opportunity to retrieve and apply that richer store of language in real communication. In other words, reading expands the resources that speaking practice can activate.",
            "The long-term impact is greater independence and confidence across many learning situations. Students who read regularly can continue learning outside the classroom, understand academic instructions, research unfamiliar topics, and prepare stronger arguments before discussions or presentations. Their spoken English also becomes more varied and accurate over time. Prioritizing reading therefore supports examination performance, informed participation, lifelong learning, and ultimately more meaningful spoken communication. Schools can still include discussion, but those discussions become stronger when they grow from purposeful reading."
        };

        private static readonly CreeiStage[] CreeiStages =
        {
            CreeiStage.Claim,
            CreeiStage.Reason,
            CreeiStage.Evidence,
            CreeiStage.Explanation,
            CreeiStage.Impact
        };

        private CreeiStage CurrentCreeiStage => CreeiStages[Mathf.Clamp(_creeiStageIndex, 0, CreeiStages.Length - 1)];

        private void Awake()
        {
            _originalCursorLockMode = Cursor.lockState;
            _originalCursorVisible = Cursor.visible;
            BuildUi();
            BuildCoachFeedbackBoard();
            BuildWorldSpeechCaptions();
            HideLegacyUi();
            experimentView = experimentView != null ? experimentView : GetComponent<CoachExperimentView>();
            experimentView ??= gameObject.AddComponent<CoachExperimentView>();
            experimentView.Build(uiCanvas);
            episodeController = episodeController != null ? episodeController : GetComponent<CoachEpisodeController>();
            episodeController ??= gameObject.AddComponent<CoachEpisodeController>();
            episodeController.AutomaticTick = false;
            SubscribeToExperimentView();
        }

        private void OnEnable()
        {
            _isShuttingDown = false;
            RegisterVoiceInterceptor();
            RegisterConvaiInputSuppressors();
            if (Application.isPlaying)
            {
                FreezePlayerMovement();
            }
        }

        private void OnDisable()
        {
            _isShuttingDown = true;
            UnregisterVoiceInterceptor();
            UnregisterConvaiInputSuppressors();
            UnsubscribeFromCoachAudio();
            CancelExperimentActivity();
            RestorePlayerControlState();
        }

        private void Update()
        {
            realtimeTranscriber?.Tick();

            if (_practiceCycleRecording)
            {
                _practiceCycleDurationSeconds = Mathf.Min(
                    _coachPolicyConfig.LearnerSpeechMaximumSeconds,
                    Mathf.Max(0f, Time.realtimeSinceStartup - _practiceCycleStartedAt));
                experimentView?.ShowPractice(
                    _practiceCycleIndex,
                    _practiceCycleDurationSeconds < _coachPolicyConfig.LearnerSpeechMinimumSeconds
                        ? "Keep speaking. You can submit after 01:00."
                        : "Press T to submit, or continue until automatic stop at 01:30.",
                    _practiceCycleDurationSeconds,
                    _practiceCycleTranscript,
                    false,
                    false);
                if (CoachSpeechWindow.ShouldAutoStop(_practiceCycleDurationSeconds, _coachPolicyConfig))
                {
                    StopPracticeCycleRecording(true);
                }
            }

            if (Phase == OrchestrationPhase.PracticeCycleCoach && episodeController != null)
            {
                episodeController.Advance(Time.unscaledDeltaTime, _coachFeedbackSpeechActive);
            }

            if (!_legacySpeechBubblesDisabled)
            {
                DisableLegacyNpcSpeechBubbles();
            }

            if (Input.GetMouseButtonDown(1))
            {
                SetControlCursor(!_isUiMode);
            }

            if (_debugRoot != null &&
                (Screen.width != _debugLayoutScreenWidth || Screen.height != _debugLayoutScreenHeight))
            {
                ApplyDebugPanelLayout();
            }

            if (_mockDebateRecording)
            {
                _mockDebateDurationSeconds = Mathf.Max(0f, Time.realtimeSinceStartup - _mockDebateStartedAt);
                UpdateMockDebateStageUi();
                if (_mockDebateDurationSeconds >= mockDebateTargetSeconds)
                {
                    StopMockDebateRecordingAtTimeLimit();
                }
            }

            if (_mockDebateLeoTimerRunning)
            {
                _mockDebateLeoDurationSeconds = Mathf.Min(
                    mockDebateTargetSeconds,
                    Mathf.Max(0f, Time.realtimeSinceStartup - _mockDebateLeoStartedAt));
                UpdateMockDebateStageUi();
                if (_mockDebateLeoDurationSeconds >= mockDebateTargetSeconds)
                {
                    _mockDebateLeoTimerRunning = false;
                    conversationNPC?.InterruptCharacterSpeech();
                    StartMockDebatePlayerTurn("Leo reached the three-minute limit. Your turn is ready.");
                }
            }
        }

        private void LateUpdate()
        {
            ApplyControlCursorState();
        }

        private IEnumerator Start()
        {
            _coachGenerator = new DebateCoachFeedbackGenerator(
                openAIModel,
                coachResponseTimeoutSeconds,
                openAIBaseUrl,
                openAIApiKeyOverride);
            _opponentGenerator = new CreeiOpponentStatementGenerator(
                openAIModel,
                coachResponseTimeoutSeconds,
                openAIBaseUrl,
                openAIApiKeyOverride);
            _mockDebateGenerator = new MockDebateEvaluationGenerator(
                openAIModel,
                GetMockDebateResponseTimeoutSeconds(),
                openAIBaseUrl,
                openAIApiKeyOverride);
            _coachLogger = new DebateCoachLogger();
            _diagnosisEngine = new CoachDiagnosisEngine(
                openAIModel,
                coachResponseTimeoutSeconds,
                openAIBaseUrl,
                openAIApiKeyOverride);
            _researchLogger = new CoachResearchLogger();
            transcriptBridge = transcriptBridge != null
                ? transcriptBridge
                : GetComponent<InteractiveDebateTranscriptBridge>();
            if (realtimeTranscriber == null)
            {
                realtimeTranscriber = GetComponent<XfyunRealtimeTranscriber>();
            }

            if (realtimeTranscriber == null)
            {
                realtimeTranscriber = gameObject.AddComponent<XfyunRealtimeTranscriber>();
            }

            SubscribeToRealtimeTranscriber();

            SessionRecord.topic = debateTopic;
            SessionRecord.learnerStance = learnerStance;
            SessionRecord.taskGoal = taskGoal;

            if (conversationManager != null)
            {
                conversationManager.RelayInterceptor = null;
            }

            SubscribeToConversationAudio();
            SubscribeToCoachAudio();
            SubscribeToPlayerVoiceTranscript();
            RegisterVoiceInterceptor();
            RegisterConvaiInputSuppressors();

            yield return null;
            transcriptBridge?.DisableConvaiTranscriptUi();
            DisableLegacyNpcSpeechBubbles();
            EnterIntro();
        }

        private void OnDestroy()
        {
            _isShuttingDown = true;
            if (_coachRoutine != null)
            {
                StopCoroutine(_coachRoutine);
                _coachRoutine = null;
            }

            if (_opponentRoutine != null)
            {
                StopCoroutine(_opponentRoutine);
                _opponentRoutine = null;
            }

            if (_mockDebateRoutine != null)
            {
                StopCoroutine(_mockDebateRoutine);
                _mockDebateRoutine = null;
            }

            if (_mockDebateTranscriptFallbackRoutine != null)
            {
                StopCoroutine(_mockDebateTranscriptFallbackRoutine);
                _mockDebateTranscriptFallbackRoutine = null;
            }

            StopCoachFeedbackSpeech(false);
            UnregisterVoiceInterceptor();
            UnregisterConvaiInputSuppressors();
            UnsubscribeFromConversationAudio();
            UnsubscribeFromCoachAudio();
            UnsubscribeFromPlayerVoiceTranscript();
            UnsubscribeFromRealtimeTranscriber();
            realtimeTranscriber?.CancelSession();
            CancelExperimentActivity();
            UnsubscribeFromExperimentView();
            RestorePlayerControlState();
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
            _creeiStageIndex = 0;
            Array.Clear(_opponentCreeiStatements, 0, _opponentCreeiStatements.Length);
            Array.Clear(_learnerCreeiResponses, 0, _learnerCreeiResponses.Length);
            _pendingFeedbackRows.Clear();
            ClearPreparedCoachFeedback();
            _latestPlayerUtterance = string.Empty;
            ResetMockDebateState();
            SessionRecord.completedTurns = 0;
            SessionRecord.latestLearnerNote = string.Empty;
            SessionRecord.latestOpponentUtterance = string.Empty;
            SessionRecord.latestCoachSuggestion = string.Empty;
            _learnerNoteInput.text = string.Empty;
            _playerText.text = "Your response transcript will appear here after you speak.";
            ShowCoachLineInLeftUi("Coach feedback will appear after your response.");

            StartCurrentCreeiStage();
        }

        private void SubscribeToExperimentView()
        {
            if (experimentView == null)
            {
                return;
            }

            experimentView.SessionStartRequested -= HandleStudySessionStart;
            experimentView.ConfirmTranscriptRequested -= ConfirmPracticeCycleTranscript;
            experimentView.RerecordRequested -= RerecordPracticeCycle;
            experimentView.RetryDiagnosisRequested -= RetryPracticeCycleDiagnosis;
            experimentView.SkipCycleRequested -= SkipPracticeCycle;
            experimentView.LearnerActionRequested -= HandleCoachLearnerAction;
            experimentView.SessionStartRequested += HandleStudySessionStart;
            experimentView.ConfirmTranscriptRequested += ConfirmPracticeCycleTranscript;
            experimentView.RerecordRequested += RerecordPracticeCycle;
            experimentView.RetryDiagnosisRequested += RetryPracticeCycleDiagnosis;
            experimentView.SkipCycleRequested += SkipPracticeCycle;
            experimentView.LearnerActionRequested += HandleCoachLearnerAction;

            if (episodeController != null)
            {
                episodeController.DecisionMade -= HandleCoachPolicyDecision;
                episodeController.EpisodeEnded -= HandleCoachEpisodeEnded;
                episodeController.DecisionMade += HandleCoachPolicyDecision;
                episodeController.EpisodeEnded += HandleCoachEpisodeEnded;
            }
        }

        private void UnsubscribeFromExperimentView()
        {
            if (experimentView != null)
            {
                experimentView.SessionStartRequested -= HandleStudySessionStart;
                experimentView.ConfirmTranscriptRequested -= ConfirmPracticeCycleTranscript;
                experimentView.RerecordRequested -= RerecordPracticeCycle;
                experimentView.RetryDiagnosisRequested -= RetryPracticeCycleDiagnosis;
                experimentView.SkipCycleRequested -= SkipPracticeCycle;
                experimentView.LearnerActionRequested -= HandleCoachLearnerAction;
            }

            if (episodeController != null)
            {
                episodeController.DecisionMade -= HandleCoachPolicyDecision;
                episodeController.EpisodeEnded -= HandleCoachEpisodeEnded;
            }
        }

        private void HandleStudySessionStart(string anonymousParticipantId, CoachOrchestrationMode mode)
        {
            if (_studyStarted)
            {
                return;
            }

            CoachStudySessionSnapshot snapshot = CoachStudySessionContext.Initialize(
                anonymousParticipantId,
                mode,
                debateTopic,
                debateTopic);
            participantId = snapshot.ParticipantId;
            _orchestrationMode = snapshot.Mode;
            _studyStarted = true;
            condition = "Disabled";
            taskGoal = "Complete the common five-stage CREEI prerequisite practice.";
            if (_titleText != null) _titleText.text = "CREEI Prerequisite Practice";
            if (_stageSelectorRoot != null) _stageSelectorRoot.SetActive(false);
            if (_coachBoardRoot != null) _coachBoardRoot.SetActive(false);
            if (_coachHeadCaptionRoot != null) _coachHeadCaptionRoot.SetActive(false);
            FreezePlayerMovement();
            experimentView?.HideAll();
            if (_root != null) _root.SetActive(true);
            BeginConditionC();
        }

        public void SelectStage(int stageIndex)
        {
            if (stageIndex < 0 || stageIndex > CreeiStages.Length)
            {
                return;
            }

            CancelCurrentStageActivity();
            if (stageIndex == CreeiStages.Length)
            {
                EnterMockDebate();
                return;
            }

            _creeiStageIndex = stageIndex;
            _turnIndex = stageIndex + 1;
            for (int i = stageIndex; i < CreeiStages.Length; i++)
            {
                _opponentCreeiStatements[i] = string.Empty;
                _learnerCreeiResponses[i] = string.Empty;
            }

            SessionRecord.completedTurns = Mathf.Min(SessionRecord.completedTurns, stageIndex);
            SessionRecord.latestLearnerNote = string.Empty;
            SessionRecord.latestOpponentUtterance = string.Empty;
            SessionRecord.latestCoachSuggestion = string.Empty;
            StartCurrentCreeiStage();
        }

        private void CancelCurrentStageActivity()
        {
            Phase = OrchestrationPhase.Intro;
            _coachRequestVersion++;
            _opponentRequestVersion++;

            if (_coachRoutine != null)
            {
                StopCoroutine(_coachRoutine);
                _coachRoutine = null;
            }

            if (_opponentRoutine != null)
            {
                StopCoroutine(_opponentRoutine);
                _opponentRoutine = null;
            }

            conversationNPC?.StopListening();
            conversationNPC?.InterruptCharacterSpeech();
            StopCoachFeedbackSpeech(true);
            ResetMockDebateState();
            ClearPreparedCoachFeedback();
            ClearLivePlayerVoiceTranscript();
            _pendingFeedbackRows.Clear();
            _latestPlayerUtterance = string.Empty;
            _exampleRequestedForCurrentTurn = false;
            HideWorldCaption(_opponentHeadCaptionRoot, ref _opponentCaptionHideRoutine);
            HideWorldCaption(_coachHeadCaptionRoot, ref _coachCaptionHideRoutine);
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

        public void RequestCoachFeedbackForCurrentResponse()
        {
            AskAnnaForReadyFeedback();
        }

        public void AskAnnaForReadyFeedback()
        {
            if (Phase == OrchestrationPhase.WaitingForPlayer || Phase == OrchestrationPhase.OpponentSpeaking)
            {
                string typedNote = _learnerNoteInput.text.Trim();
                if (string.IsNullOrWhiteSpace(typedNote))
                {
                    SetStatus("Speak with T first, or type a note. Anna unlocks after Coach feedback is ready.");
                    return;
                }

                FinalizeOpponentTurnForPlayerVoice();
                HandlePlayerUtterance(typedNote, true);
                return;
            }

            if (Phase == OrchestrationPhase.CoachGenerating || Phase == OrchestrationPhase.PlayerResponseReady)
            {
                SetStatus("Coach is still preparing feedback. Ask Anna will unlock when it is ready.");
                return;
            }

            if (Phase != OrchestrationPhase.CoachSuggestionReady || string.IsNullOrWhiteSpace(_latestFeedbackText))
            {
                return;
            }

            _coachFeedbackSentToAnna = true;
            SetStatus("Anna is speaking the prepared Coach feedback...");
            StartCoachFeedbackSpeech(_latestFeedbackText);
            SetButtonsForPhase();
        }

        public void ContinueConversation()
        {
            if (Phase != OrchestrationPhase.CoachSuggestionReady &&
                Phase != OrchestrationPhase.PlayerResponseReady)
            {
                return;
            }

            StopCoachFeedbackSpeech(true);
            FlushPendingFeedbackRows(DateTime.UtcNow.ToString("o"));

            if (_mockDebateFeedbackReady)
            {
                Phase = OrchestrationPhase.Complete;
                SetMockDebateTimerVisible(false);
                SetControlCursor(true);
                SetStatus("Mock Debate complete. Final CREEI feedback has been prepared.");
                SetButtonsForPhase();
                return;
            }

            if (_creeiStageIndex >= CreeiStages.Length - 1)
            {
                EnterMockDebate();
                return;
            }

            _learnerNoteInput.text = string.Empty;
            _creeiStageIndex++;
            StartCurrentCreeiStage();
        }

        public void RequestExample()
        {
            if (Phase != OrchestrationPhase.CoachSuggestionReady ||
                _exampleRequestedForCurrentTurn ||
                _mockDebateFeedbackReady)
            {
                return;
            }

            StopCoachFeedbackSpeech(true);
            _exampleRequestedForCurrentTurn = true;
            StartCoachFeedbackRequest(CoachFeedbackLevel.Level3, true, true);
        }

        public void AskCoachAgain()
        {
            RequestExample();
        }

        public void EndConditionC()
        {
            if (_mockDebateFeedbackReady)
            {
                StopCoachFeedbackSpeech(false);
                Phase = OrchestrationPhase.Complete;
                SetMockDebateTimerVisible(false);
                SetControlCursor(true);
                SetStatus("Mock Debate complete. Final CREEI feedback has been prepared.");
                SetButtonsForPhase();
                return;
            }

            StopCoachFeedbackSpeech(false);
            FlushPendingFeedbackRows(string.Empty);
            ConvaiNPCManager.Instance?.SetActiveConvaiNPC(coachNPC);
            SetControlCursor(true);
            SetStatus("Coach is preparing final feedback...");
            ShowCoachLineInLeftUi("Coach is preparing final feedback...");
            StartCoachFeedbackRequest(CoachFeedbackLevel.Summary, false, true);
        }

        public void AdvanceFormalDebate()
        {
            ContinueConversation();
        }

        private void StartCurrentCreeiStage()
        {
            _practiceVoiceRecording = false;
            _practiceVoiceAwaitingTranscript = false;
            _realtimeCapturePurpose = RealtimeCapturePurpose.None;
            realtimeTranscriber?.CancelSession();
            _opponentRequestVersion++;
            if (_opponentRoutine != null)
            {
                StopCoroutine(_opponentRoutine);
                _opponentRoutine = null;
            }

            Phase = OrchestrationPhase.OpponentGenerating;
            SetMockDebateTimerVisible(false);
            _turnIndex = _creeiStageIndex + 1;
            _opponentTranscript.Clear();
            _livePlayerVoiceTranscript.Clear();
            HideWorldCaption(_opponentHeadCaptionRoot, ref _opponentCaptionHideRoutine);
            HideWorldCaption(_coachHeadCaptionRoot, ref _coachCaptionHideRoutine);
            ClearPreparedCoachFeedback();
            _latestPlayerUtterance = string.Empty;
            _exampleRequestedForCurrentTurn = false;
            ConvaiNPCManager.Instance?.SetActiveConvaiNPC(conversationNPC);
            transcriptBridge?.DisableConvaiTranscriptUi();
            SetControlCursor(false);
            UpdateCreeiStageUi();
            CreeiOpponentStatementRequest request = BuildOpponentStatementRequest();
            UpdateOpponentDebugPrompt(request);
            SetStatus($"{CurrentCreeiStage}: GPT is preparing Leo's stage statement...");
            SetButtonsForPhase();
            _opponentRoutine = StartCoroutine(RequestOpponentStatement(request, _opponentRequestVersion));
        }

        private IEnumerator RequestOpponentStatement(
            CreeiOpponentStatementRequest request,
            int requestVersion)
        {
            CreeiOpponentStatementResult result = null;
            yield return (_opponentGenerator ??= new CreeiOpponentStatementGenerator(
                    openAIModel,
                    coachResponseTimeoutSeconds,
                    openAIBaseUrl,
                    openAIApiKeyOverride))
                .GenerateStatement(request, response => result = response);

            if (requestVersion != _opponentRequestVersion)
            {
                yield break;
            }

            _opponentRoutine = null;
            if (result == null || !result.IsValid || string.IsNullOrWhiteSpace(result.SpeechText))
            {
                UpdateOpponentDebugResult(result);
                Phase = OrchestrationPhase.Intro;
                SetControlCursor(true);
                SetStatus($"{CurrentCreeiStage}: Leo GPT did not return a valid statement. Press Start Conversation to retry.");
                SetButtonsForPhase();
                yield break;
            }

            UpdateOpponentDebugResult(result);
            _opponentCreeiStatements[_creeiStageIndex] = result.SpeechText.Trim();
            Phase = OrchestrationPhase.OpponentSpeaking;
            string convaiPrompt = BuildOpponentConvaiPrompt(result.SpeechText);
            SetStatus($"{CurrentCreeiStage}: listen to Leo, then press T once to start recording your {CurrentCreeiStage.ToString().ToLowerInvariant()}.");
            SetButtonsForPhase();
            conversationNPC.SendTextDataAsync(convaiPrompt);
        }

        private CreeiOpponentStatementRequest BuildOpponentStatementRequest()
        {
            return new CreeiOpponentStatementRequest
            {
                Topic = debateTopic,
                OpponentStance = opponentStance,
                Stage = CurrentCreeiStage,
                PreviousOpponentCreeiStages = BuildCreeiContext(_opponentCreeiStatements, _creeiStageIndex)
            };
        }

        private string BuildOpponentConvaiPrompt(string statement)
        {
            return
                "You are Leo. Speak the prepared English debate statement below aloud. " +
                "Keep the same meaning, do not answer the learner, do not ask a question, and do not add another argument.\n" +
                "Current CREEI stage: " + CurrentCreeiStage + "\n" +
                "Prepared statement: [" + statement.Trim() + "]";
        }

        private bool TryHandlePlayerVoiceTranscript(string transcript)
        {
            if (!automaticCoachAfterPlayerVoice || !CanHandlePlayerVoiceTranscript())
            {
                return false;
            }

            if (_realtimeCapturePurpose != RealtimeCapturePurpose.None || IsMockDebateInputPhase())
            {
                return true;
            }

            string safeTranscript = transcript?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(safeTranscript))
            {
                RunOnMainThread(ClearLivePlayerVoiceTranscript);
                return true;
            }

            RunOnMainThread(() =>
            {
                if (!CanHandlePlayerVoiceTranscript())
                {
                    return;
                }

                ClearLivePlayerVoiceTranscript();
                FinalizeOpponentTurnForPlayerVoice();
                HandlePlayerUtterance(safeTranscript, false);
            });
            return true;
        }

        private bool CanHandlePlayerVoiceTranscript()
        {
            return Phase == OrchestrationPhase.WaitingForPlayer ||
                   Phase == OrchestrationPhase.OpponentSpeaking ||
                   Phase == OrchestrationPhase.PlayerResponseReady ||
                   Phase == OrchestrationPhase.CoachGenerating ||
                   Phase == OrchestrationPhase.CoachSuggestionReady ||
                   IsMockDebateInputPhase();
        }

        private void HandlePlayerSpeakingChanged(bool isSpeaking)
        {
            if (IsMockDebatePhase())
            {
                return;
            }

            if (!isSpeaking || !CanHandlePlayerVoiceTranscript())
            {
                return;
            }

            RunOnMainThread(BeginPlayerRetake);
        }

        private void BeginPlayerRetake()
        {
            if (!CanHandlePlayerVoiceTranscript())
            {
                return;
            }

            _coachRequestVersion++;
            if (_coachRoutine != null)
            {
                StopCoroutine(_coachRoutine);
                _coachRoutine = null;
            }

            if (_opponentRoutine != null)
            {
                StopCoroutine(_opponentRoutine);
                _opponentRoutine = null;
            }

            if (_coachFeedbackSpeechActive || (coachNPC != null && coachNPC.IsCharacterTalking))
            {
                StopCoachFeedbackSpeech(true);
            }
            ClearPreparedCoachFeedback();
            _pendingFeedbackRows.RemoveAll(row => row != null && row.TurnId == _turnIndex);
            _latestPlayerUtterance = string.Empty;
            _learnerCreeiResponses[_creeiStageIndex] = string.Empty;
            _livePlayerVoiceTranscript.Clear();
            _exampleRequestedForCurrentTurn = false;
            SessionRecord.latestLearnerNote = string.Empty;
            SessionRecord.latestCoachSuggestion = string.Empty;
            Phase = OrchestrationPhase.WaitingForPlayer;

            if (_learnerNoteInput != null)
            {
                _learnerNoteInput.SetTextWithoutNotify(string.Empty);
            }

            if (_playerText != null)
            {
                _playerText.text = "You (listening): start speaking...";
            }

            ShowCoachLineInLeftUi("Previous response and Coach feedback cleared. Listening to your new response...");
            UpdateCoachDebugIdle();
            SetStatus("Ready to record your new response. Press T again when you finish speaking.");
            SetButtonsForPhase();
        }

        private void FinalizeOpponentTurnForPlayerVoice()
        {
            if (Phase != OrchestrationPhase.OpponentSpeaking)
            {
                return;
            }

            SessionRecord.latestOpponentUtterance = _opponentTranscript.ToString().Trim();
            Phase = OrchestrationPhase.WaitingForPlayer;
            UnityEngine.Debug.Log("SharedInitiativeOrchestrationController accepted player voice while the opponent-speaking phase was still active.");
        }

        private void HandlePlayerUtterance(string transcript, bool fromTypedFallback)
        {
            _latestPlayerUtterance = transcript.Trim();
            _learnerCreeiResponses[_creeiStageIndex] = _latestPlayerUtterance;
            SessionRecord.completedTurns = Mathf.Max(SessionRecord.completedTurns, _turnIndex);
            SessionRecord.latestLearnerNote = _latestPlayerUtterance;
            _learnerNoteInput.text = _latestPlayerUtterance;
            _playerText.text = "You: " + BuildRollingTranscriptText(_latestPlayerUtterance, string.Empty);
            Phase = OrchestrationPhase.PlayerResponseReady;
            SetControlCursor(true);
            ClearPreparedCoachFeedback();
            if (_studyStarted)
            {
                ShowCoachLineInLeftUi("Coach is disabled during the common CREEI prerequisite practice.");
                SetStatus($"{CurrentCreeiStage}: response saved. Moving to the next prerequisite stage...");
                SetButtonsForPhase();
                StartCoroutine(AdvanceDisabledCreeiStage());
                return;
            }

            ShowCoachLineInLeftUi("Coach is preparing feedback. Ask Anna will unlock when it is ready.");
            SetStatus(fromTypedFallback
                ? $"{CurrentCreeiStage}: typed response saved. Stage-specific Coach feedback request started."
                : $"{CurrentCreeiStage}: voice captured. Stage-specific Coach feedback request started.");
            SetButtonsForPhase();
            StartCoachFeedbackRequest(CoachFeedbackLevel.Level2, false);
        }

        private IEnumerator AdvanceDisabledCreeiStage()
        {
            yield return new WaitForSecondsRealtime(0.35f);
            if (!_studyStarted || Phase != OrchestrationPhase.PlayerResponseReady)
            {
                yield break;
            }

            if (_creeiStageIndex >= CreeiStages.Length - 1)
            {
                BeginPracticeCycles();
                yield break;
            }

            _learnerNoteInput.text = string.Empty;
            _creeiStageIndex++;
            StartCurrentCreeiStage();
        }

        private bool IsMockDebateInputPhase()
        {
            return Phase == OrchestrationPhase.MockDebateReady ||
                   Phase == OrchestrationPhase.MockDebateSpeaking ||
                   Phase == OrchestrationPhase.MockDebateAwaitingTranscript ||
                   (_mockDebateFeedbackReady && Phase == OrchestrationPhase.CoachSuggestionReady);
        }

        private bool IsMockDebatePhase()
        {
            return Phase == OrchestrationPhase.MockDebateOpponentSpeaking ||
                   IsMockDebateInputPhase() ||
                   Phase == OrchestrationPhase.MockDebateEvaluating;
        }

        private void ResetMockDebateState()
        {
            _mockDebateRequestVersion++;
            if (_mockDebateRoutine != null)
            {
                StopCoroutine(_mockDebateRoutine);
                _mockDebateRoutine = null;
            }

            if (_mockDebateTranscriptFallbackRoutine != null)
            {
                StopCoroutine(_mockDebateTranscriptFallbackRoutine);
                _mockDebateTranscriptFallbackRoutine = null;
            }

            _mockDebateRecording = false;
            _mockDebateAutoStopRequested = false;
            _practiceVoiceRecording = false;
            _practiceVoiceAwaitingTranscript = false;
            _realtimeCapturePurpose = RealtimeCapturePurpose.None;
            realtimeTranscriber?.CancelSession();
            _mockDebateStartedAt = 0f;
            _mockDebateDurationSeconds = 0f;
            _mockDebateLeoTimerRunning = false;
            _mockDebateLeoHasStartedTalking = false;
            _mockDebateLeoStartedAt = 0f;
            _mockDebateLeoDurationSeconds = 0f;
            _mockDebateLeoSpeechSegments = Array.Empty<string>();
            _mockDebateLeoSegmentIndex = 0;
            _completeMockDebateTranscript.Clear();
            _mockDebateTranscript = string.Empty;
            _mockDebateFeedbackReady = false;
        }

        private void EnterMockDebate()
        {
            StopCoachFeedbackSpeech(true);
            ResetMockDebateState();
            ClearPreparedCoachFeedback();
            ClearLivePlayerVoiceTranscript();
            _latestPlayerUtterance = string.Empty;
            ConvaiNPCManager.Instance?.SetActiveConvaiNPC(conversationNPC);
            SetControlCursor(false);

            if (_learnerNoteInput != null)
            {
                _learnerNoteInput.SetTextWithoutNotify(string.Empty);
            }

            if (_playerText != null)
            {
                _playerText.text = "You: wait for Leo to finish, then press T once for your three-minute argument.";
            }

            UpdateMockDebateDebugIdle();
            StartMockDebateLeoSpeech();
        }

        private void StartMockDebateLeoSpeech()
        {
            Phase = OrchestrationPhase.MockDebateOpponentSpeaking;
            _opponentTranscript.Clear();
            _mockDebateLeoSpeechSegments = (string[])MockDebateLeoCreeiSegments.Clone();
            _mockDebateLeoSegmentIndex = 0;
            _mockDebateLeoTimerRunning = true;
            _mockDebateLeoHasStartedTalking = false;
            _mockDebateLeoStartedAt = Time.realtimeSinceStartup;
            _mockDebateLeoDurationSeconds = 0f;
            ShowCoachLineInLeftUi("Mock Debate Round 1: listen to Leo's complete CREEI argument.");
            SetStatus("Leo is delivering his three-minute CREEI argument first.");
            UpdateMockDebateStageUi();
            SetButtonsForPhase();
            SendNextMockDebateLeoSegment();
        }

        private void SendNextMockDebateLeoSegment()
        {
            if (conversationNPC == null ||
                _mockDebateLeoSegmentIndex >= _mockDebateLeoSpeechSegments.Length)
            {
                StartMockDebatePlayerTurn("Leo finished his CREEI argument. Your turn is ready.");
                return;
            }

            CreeiStage stage = (CreeiStage)Mathf.Clamp(_mockDebateLeoSegmentIndex, 0, CreeiStages.Length - 1);
            string segment = _mockDebateLeoSpeechSegments[_mockDebateLeoSegmentIndex].Trim();
            _mockDebateLeoSegmentIndex++;
            string prompt =
                "You are Leo. Speak only in English. Read the prepared debate speech below aloud once. " +
                "Do not answer the learner, add a new argument, ask a question, or discuss these instructions. " +
                "Speak calmly at a clear debate pace. This is the " + stage + " part. Prepared text: [" + segment + "]";
            conversationNPC.SendTextDataAsync(prompt);
        }

        private void StartMockDebatePlayerTurn(string transitionMessage)
        {
            if (Phase == OrchestrationPhase.MockDebateReady ||
                Phase == OrchestrationPhase.MockDebateSpeaking ||
                Phase == OrchestrationPhase.MockDebateAwaitingTranscript ||
                Phase == OrchestrationPhase.MockDebateEvaluating)
            {
                return;
            }

            _mockDebateLeoTimerRunning = false;
            _mockDebateRecording = false;
            _mockDebateAutoStopRequested = false;
            _mockDebateDurationSeconds = 0f;
            realtimeTranscriber?.CancelSession();
            _completeMockDebateTranscript.Clear();
            ClearLivePlayerVoiceTranscript();
            Phase = OrchestrationPhase.MockDebateReady;
            ConvaiNPCManager.Instance?.SetActiveConvaiNPC(conversationNPC);
            if (_playerText != null)
            {
                _playerText.text = "You: press T once when you are ready.";
            }

            ShowCoachLineInLeftUi("Mock Debate Round 2: deliver your complete Claim, Reason, Evidence, Explanation, and Impact.");
            SetStatus(transitionMessage + " Press T once; recording stops automatically at 03:00.");
            UpdateMockDebateStageUi();
            SetButtonsForPhase();
        }

        private void BeginMockDebateRecording()
        {
            if (!IsMockDebateInputPhase())
            {
                return;
            }

            _mockDebateRequestVersion++;
            if (_mockDebateRoutine != null)
            {
                StopCoroutine(_mockDebateRoutine);
                _mockDebateRoutine = null;
            }

            if (_coachFeedbackSpeechActive ||
                (coachNPC != null && (coachNPC.IsCharacterTalking || coachNPC.GetAudioResponseCount() > 0)))
            {
                StopCoachFeedbackSpeech(true);
            }
            ClearPreparedCoachFeedback();
            _mockDebateFeedbackReady = false;
            ClearLivePlayerVoiceTranscript();
            _completeMockDebateTranscript.Clear();
            _mockDebateTranscript = string.Empty;
            _mockDebateDurationSeconds = 0f;
            _mockDebateStartedAt = Time.realtimeSinceStartup;
            _mockDebateRecording = true;
            _mockDebateAutoStopRequested = false;
            Phase = OrchestrationPhase.MockDebateSpeaking;

            if (_learnerNoteInput != null)
            {
                _learnerNoteInput.SetTextWithoutNotify(string.Empty);
            }

            if (_playerText != null)
            {
                _playerText.text = "You (listening): start your complete debate speech...";
            }

            ShowCoachLineInLeftUi("Listening to your Mock Debate. A new T recording replaces the previous attempt.");
            SetStatus("Mock Debate recording... keep speaking. Recording stops automatically at 03:00.");
            UpdateMockDebateStageUi();
            SetButtonsForPhase();
        }

        private void HandleOrchestrationTapToTalk()
        {
            RunOnMainThread(() =>
            {
                if (IsPracticeCyclePhase())
                {
                    TogglePracticeCycleRecording();
                    return;
                }

                if (IsMockDebateInputPhase())
                {
                    StartMockDebateTapRecording();
                    return;
                }

                TogglePracticeStageRecording();
            });
        }

        private bool IsPracticeCyclePhase()
        {
            return Phase is OrchestrationPhase.PracticeCycleReady or
                OrchestrationPhase.PracticeCycleSpeaking or
                OrchestrationPhase.PracticeCycleAwaitingTranscript or
                OrchestrationPhase.PracticeCycleConfirmingTranscript or
                OrchestrationPhase.PracticeCycleDiagnosing or
                OrchestrationPhase.PracticeCycleCoach;
        }

        private void BeginPracticeCycles()
        {
            StopCoachFeedbackSpeech(true);
            _realtimeCapturePurpose = RealtimeCapturePurpose.None;
            realtimeTranscriber?.CancelSession();
            _practiceCycleIndex = 1;
            if (_root != null) _root.SetActive(false);
            BeginPracticeCycle();
        }

        private void BeginPracticeCycle()
        {
            CancelExperimentNetworkRequests();
            StopPracticeCycleTranscriptTimeout();
            _practiceDiagnosis = null;
            _practiceCycleTranscript = string.Empty;
            _confirmedPracticeTranscript = string.Empty;
            _lastExperimentFeedback = string.Empty;
            _practiceCycleDurationSeconds = 0f;
            _practiceCycleRecording = false;
            _practiceCycleAutoStopRequested = false;
            _practiceCycleTechnicalFallback = false;
            Phase = OrchestrationPhase.PracticeCycleReady;
            SetControlCursor(true);
            experimentView?.SetTranscriptEditable(false);
            experimentView?.ShowPractice(
                _practiceCycleIndex,
                "Press T to begin one complete 60–90 second argument. There is no opponent turn.",
                0f,
                string.Empty,
                false,
                false);
        }

        private void TogglePracticeCycleRecording()
        {
            if (_coachFeedbackSpeechActive)
            {
                experimentView?.ShowPractice(_practiceCycleIndex,
                    "Coach speech must finish before the microphone can start.",
                    _practiceCycleDurationSeconds, _practiceCycleTranscript, false, false);
                return;
            }

            if (_practiceCycleRecording)
            {
                if (!CoachSpeechWindow.CanSubmit(_practiceCycleDurationSeconds, _coachPolicyConfig))
                {
                    experimentView?.ShowPractice(_practiceCycleIndex,
                        "You cannot submit before 01:00. Keep speaking.",
                        _practiceCycleDurationSeconds, _practiceCycleTranscript, false, false);
                    return;
                }

                StopPracticeCycleRecording(false);
                return;
            }

            if (Phase != OrchestrationPhase.PracticeCycleReady)
            {
                return;
            }

            if (realtimeTranscriber == null)
            {
                _practiceCycleTechnicalFallback = true;
                _practiceCycleRecording = true;
                _practiceCycleStartedAt = Time.realtimeSinceStartup;
                _practiceCycleDurationSeconds = 0f;
                Phase = OrchestrationPhase.PracticeCycleSpeaking;
                experimentView?.SetTranscriptEditable(false);
                experimentView?.ShowPractice(_practiceCycleIndex,
                    "Transcription is unavailable. Keep speaking for 01:00–01:30; a researcher can enter the fallback transcript after the speech.",
                    0f, string.Empty, false, false);
                return;
            }

            if (realtimeTranscriber.IsConnecting)
            {
                experimentView?.ShowPractice(_practiceCycleIndex,
                    "Realtime transcription is still connecting...", 0f, string.Empty, false, false);
                return;
            }

            PrepareForPlayerVoiceInput();
            _practiceCycleTranscript = string.Empty;
            _realtimeCapturePurpose = RealtimeCapturePurpose.PracticeCycle;
            experimentView?.ShowPractice(_practiceCycleIndex,
                "Connecting to English realtime transcription...", 0f, string.Empty, false, false);
            string microphoneDevice = MicrophoneManager.Instance?.SelectedMicrophoneName ?? string.Empty;
            realtimeTranscriber.StartSession(microphoneDevice);
        }

        private void StopPracticeCycleRecording(bool automatic)
        {
            if (!_practiceCycleRecording || _practiceCycleAutoStopRequested)
            {
                return;
            }

            _practiceCycleAutoStopRequested = true;
            _practiceCycleRecording = false;
            if (automatic)
            {
                _practiceCycleDurationSeconds = _coachPolicyConfig.LearnerSpeechMaximumSeconds;
            }
            if (_practiceCycleTechnicalFallback)
            {
                _practiceCycleAutoStopRequested = false;
                Phase = OrchestrationPhase.PracticeCycleConfirmingTranscript;
                SetControlCursor(true);
                experimentView?.SetTranscriptEditable(true);
                experimentView?.ShowPractice(
                    _practiceCycleIndex,
                    "Speech duration requirement met. A researcher may enter the fallback transcript, or re-record.",
                    _practiceCycleDurationSeconds,
                    _practiceCycleTranscript,
                    true,
                    true);
                return;
            }
            Phase = OrchestrationPhase.PracticeCycleAwaitingTranscript;
            experimentView?.ShowPractice(_practiceCycleIndex,
                automatic
                    ? "01:30 reached. Finalizing transcript..."
                    : "Recording stopped. Finalizing transcript...",
                _practiceCycleDurationSeconds, _practiceCycleTranscript, false, false);
            realtimeTranscriber?.StopSession();
            StopPracticeCycleTranscriptTimeout();
            _practiceCycleTranscriptTimeoutRoutine = StartCoroutine(
                CompletePracticeCycleAfterTranscriptTimeout());
        }

        private IEnumerator CompletePracticeCycleAfterTranscriptTimeout()
        {
            yield return new WaitForSecondsRealtime(12f);
            _practiceCycleTranscriptTimeoutRoutine = null;
            if (Phase != OrchestrationPhase.PracticeCycleAwaitingTranscript)
            {
                yield break;
            }

            _realtimeCapturePurpose = RealtimeCapturePurpose.None;
            realtimeTranscriber?.CancelSession();
            Phase = OrchestrationPhase.PracticeCycleConfirmingTranscript;
            SetControlCursor(true);
            bool needsResearcherFallback = string.IsNullOrWhiteSpace(_practiceCycleTranscript);
            experimentView?.SetTranscriptEditable(needsResearcherFallback);
            experimentView?.ShowPractice(
                _practiceCycleIndex,
                needsResearcherFallback
                    ? "Final transcript timed out. A researcher may enter a fallback transcript, or re-record."
                    : "Final transcript timed out. Review the partial transcript, then confirm or re-record.",
                _practiceCycleDurationSeconds,
                _practiceCycleTranscript,
                true,
                true);
        }

        private void StopPracticeCycleTranscriptTimeout()
        {
            if (_practiceCycleTranscriptTimeoutRoutine == null)
            {
                return;
            }

            StopCoroutine(_practiceCycleTranscriptTimeoutRoutine);
            _practiceCycleTranscriptTimeoutRoutine = null;
        }

        private void ConfirmPracticeCycleTranscript()
        {
            if (Phase != OrchestrationPhase.PracticeCycleConfirmingTranscript)
            {
                return;
            }

            string transcript = experimentView?.TranscriptText?.Trim() ?? _practiceCycleTranscript.Trim();
            if (string.IsNullOrWhiteSpace(transcript))
            {
                experimentView?.ShowPractice(_practiceCycleIndex,
                    "A non-empty transcript is required before diagnosis.",
                    _practiceCycleDurationSeconds, transcript, false, true);
                return;
            }

            _confirmedPracticeTranscript = transcript;
            _practiceCycleTranscript = transcript;
            BeginPracticeCycleDiagnosis();
        }

        private void RerecordPracticeCycle()
        {
            if (Phase != OrchestrationPhase.PracticeCycleConfirmingTranscript)
            {
                return;
            }

            _practiceCycleTranscript = string.Empty;
            _confirmedPracticeTranscript = string.Empty;
            BeginPracticeCycle();
        }

        private void RetryPracticeCycleDiagnosis()
        {
            if (Phase == OrchestrationPhase.PracticeCycleDiagnosing &&
                !string.IsNullOrWhiteSpace(_confirmedPracticeTranscript))
            {
                LogExperimentEvent(
                    "DiagnosisRetryRequested",
                    CoachTerminationReason.None,
                    CoachLearnerAction.RetryDiagnosis);
                BeginPracticeCycleDiagnosis();
            }
        }

        private void BeginPracticeCycleDiagnosis()
        {
            CancelExperimentNetworkRequests();
            Phase = OrchestrationPhase.PracticeCycleDiagnosing;
            experimentView?.SetTranscriptEditable(false);
            experimentView?.ShowPractice(_practiceCycleIndex,
                "Running condition-blind diagnosis...",
                _practiceCycleDurationSeconds,
                _confirmedPracticeTranscript,
                false,
                false);
            CoachDiagnosisRequest request = new()
            {
                ParticipantId = participantId,
                Stage = "PracticeDebate",
                TopicId = topicId,
                PracticeCycleId = _practiceCycleIndex,
                TurnId = _practiceCycleIndex,
                Topic = debateTopic,
                LearnerSide = learnerStance,
                PlayerUtteranceText = _confirmedPracticeTranscript,
                SelectedStrategy = selectedStrategy
            };
            _diagnosisRoutine = StartCoroutine((_diagnosisEngine ??= new CoachDiagnosisEngine(
                    openAIModel, coachResponseTimeoutSeconds, openAIBaseUrl, openAIApiKeyOverride))
                .Diagnose(request, CompletePracticeCycleDiagnosis));
        }

        private void CompletePracticeCycleDiagnosis(CoachDiagnosisResult diagnosis)
        {
            _diagnosisRoutine = null;
            if (Phase != OrchestrationPhase.PracticeCycleDiagnosing)
            {
                return;
            }

            if (diagnosis == null || !diagnosis.Success)
            {
                string error = diagnosis?.Error ?? "Diagnosis failed for an unknown technical reason.";
                LogExperimentEvent("DiagnosisTechnicalFailure");
                experimentView?.ShowPractice(_practiceCycleIndex,
                    error + " Retry diagnosis or skip this cycle.",
                    _practiceCycleDurationSeconds,
                    _confirmedPracticeTranscript,
                    false,
                    false,
                    true);
                return;
            }

            _practiceDiagnosis = diagnosis;
            Phase = OrchestrationPhase.PracticeCycleCoach;
            experimentView?.SetSelectedFocus(diagnosis.RecommendedFocus);
            episodeController.Configure(
                _orchestrationMode,
                _practiceCycleIndex,
                _coachPolicyConfig,
                _researchLogger);
            episodeController.AutomaticTick = false;
            episodeController.BeginOpportunity(
                diagnosis,
                _confirmedPracticeTranscript,
                topicId,
                _practiceCycleIndex);
        }

        private void SkipPracticeCycle()
        {
            if (Phase != OrchestrationPhase.PracticeCycleDiagnosing)
            {
                return;
            }

            CoachStudySessionSnapshot session = CoachStudySessionContext.Current;
            _researchLogger?.LogEvent(new CoachEventRecord
            {
                ParticipantId = session?.ParticipantId ?? participantId,
                SessionId = session?.SessionId ?? string.Empty,
                OrchestrationMode = _orchestrationMode,
                Stage = "PracticeDebate",
                TopicId = topicId,
                PracticeCycleId = _practiceCycleIndex,
                TurnId = _practiceCycleIndex,
                EventType = "DiagnosisTechnicalFailureSkipped",
                LearnerControlAction = CoachLearnerAction.SkipCycle.ToString(),
                ConfirmedLearnerText = _confirmedPracticeTranscript,
                TerminationReason = CoachTerminationReason.CycleSkipped,
                TerminationOwner = ControlOwner.Learner,
                DiagnosisModelVersion = _coachPolicyConfig.DiagnosisModelVersion,
                FeedbackModelVersion = _coachPolicyConfig.FeedbackModelVersion,
                PolicyVersion = _coachPolicyConfig.PolicyVersion
            });
            AdvanceToNextPracticeCycle();
        }

        private void HandleCoachLearnerAction(CoachLearnerAction action, string focus)
        {
            if (Phase != OrchestrationPhase.PracticeCycleCoach || episodeController == null)
            {
                return;
            }

            if (action == CoachLearnerAction.RequestCoach)
            {
                LogExperimentEvent(
                    "CoachRequested",
                    CoachTerminationReason.None,
                    CoachLearnerAction.RequestCoach);
                if (_orchestrationMode == CoachOrchestrationMode.SharedControl)
                {
                    experimentView?.SetSelectedFocus(_practiceDiagnosis?.RecommendedFocus);
                    experimentView?.ShowCoach(
                        "AI suggested " + (_practiceDiagnosis?.RecommendedFocus ?? "a focus") +
                        ". Keep it or choose another CREEI/rhetorical focus, then confirm.",
                        string.Empty,
                        true,
                        CoachLearnerAction.ConfirmFocus,
                        CoachLearnerAction.ExitCoaching);
                }
                else
                {
                    experimentView?.ShowCoach(
                        "Choose the CREEI or rhetorical focus you want Coach to address, then confirm.",
                        string.Empty,
                        true,
                        CoachLearnerAction.ConfirmFocus,
                        CoachLearnerAction.ExitCoaching);
                }
                return;
            }

            if (action == CoachLearnerAction.Replay)
            {
                episodeController.SubmitLearnerAction(action, focus);
                if (!string.IsNullOrWhiteSpace(_lastExperimentFeedback))
                {
                    StartCoachFeedbackSpeech(_lastExperimentFeedback);
                }
                return;
            }

            if (action == CoachLearnerAction.RetryFeedback)
            {
                LogExperimentEvent(
                    "FeedbackRetryRequested",
                    CoachTerminationReason.None,
                    CoachLearnerAction.RetryFeedback);
                StartExperimentFeedback(_lastExperimentFeedbackLevel);
                return;
            }

            if (action == CoachLearnerAction.ExitCoaching)
            {
                StopCoachFeedbackSpeech(true);
            }

            string selectedFocus = action is CoachLearnerAction.ConfirmFocus or
                CoachLearnerAction.ChangeFocus or CoachLearnerAction.Override
                ? focus
                : string.Empty;
            episodeController.SubmitLearnerAction(action, selectedFocus);
        }

        private void HandleCoachPolicyDecision(CoachPolicyDecision decision)
        {
            if (decision == null || Phase != OrchestrationPhase.PracticeCycleCoach)
            {
                return;
            }

            switch (decision.Action)
            {
                case CoachPolicyAction.MakeAvailable:
                {
                    bool sharedSuggestion = _orchestrationMode == CoachOrchestrationMode.SharedControl &&
                                            _practiceDiagnosis != null;
                    bool noEligibleIssue = _practiceDiagnosis != null && !_practiceDiagnosis.HasEligibleIssue;
                    experimentView?.ShowCoach(
                        noEligibleIssue
                            ? "No priority coaching issue was identified in this cycle."
                            : sharedSuggestion
                                ? "Diagnosis is ready. Suggested focus: " + _practiceDiagnosis.RecommendedFocus +
                                  ". Coach remains silent until you ask."
                                : "Diagnosis is ready. Coach remains silent until you ask.",
                        noEligibleIssue ? "Your confirmed argument can move to the next practice cycle without Coach feedback." : string.Empty,
                        sharedSuggestion && !noEligibleIssue,
                        noEligibleIssue
                            ? new[] { CoachLearnerAction.ApplyNextCycle, CoachLearnerAction.ExitCoaching }
                            : new[]
                            {
                                CoachLearnerAction.RequestCoach,
                                CoachLearnerAction.ApplyNextCycle,
                                CoachLearnerAction.ExitCoaching
                            });
                    break;
                }

                case CoachPolicyAction.Invite:
                    experimentView?.ShowCoach(
                        "Coach invitation: " + BuildDiagnosisCategoryText() +
                        ". Suggested focus: " + decision.ProposedFocus + ".",
                        string.Empty,
                        true,
                        CoachLearnerAction.Accept,
                        CoachLearnerAction.ChangeFocus,
                        CoachLearnerAction.Decline,
                        CoachLearnerAction.ExitCoaching);
                    break;

                case CoachPolicyAction.AutoStart:
                case CoachPolicyAction.Start:
                    StartExperimentFeedback(CoachFeedbackLevel.Level2);
                    break;

                case CoachPolicyAction.Continue:
                    StartExperimentFeedback(
                        decision.PolicyReason == CoachLearnerAction.NeedExample.ToString()
                            ? CoachFeedbackLevel.Level3
                            : CoachFeedbackLevel.Level2);
                    break;

                case CoachPolicyAction.Replay:
                    break;

                case CoachPolicyAction.End:
                    StopCoachFeedbackSpeech(true);
                    break;
            }
        }

        private string BuildDiagnosisCategoryText()
        {
            if (_practiceDiagnosis == null)
            {
                return "argument structure";
            }

            string component = string.IsNullOrWhiteSpace(_practiceDiagnosis.WeakComponent)
                ? "argument structure"
                : _practiceDiagnosis.WeakComponent;
            string strategy = string.IsNullOrWhiteSpace(_practiceDiagnosis.RecommendedStrategy)
                ? string.Empty
                : " / " + _practiceDiagnosis.RecommendedStrategy;
            return component + strategy;
        }

        private void StartExperimentFeedback(CoachFeedbackLevel level)
        {
            _lastExperimentFeedbackLevel = level;
            if (_experimentFeedbackRoutine != null)
            {
                StopCoroutine(_experimentFeedbackRoutine);
            }

            _experimentFeedbackRoutine = StartCoroutine(GenerateExperimentFeedback(level));
        }

        private IEnumerator GenerateExperimentFeedback(CoachFeedbackLevel level)
        {
            experimentView?.ShowCoach(
                level == CoachFeedbackLevel.Level3
                    ? "Generating the learner-requested example..."
                    : "Generating focused Coach feedback...",
                string.Empty,
                false,
                CoachLearnerAction.ExitCoaching);

            CoachFeedbackRequest request = new()
            {
                Stage = "PracticeDebate",
                TopicId = topicId,
                Topic = debateTopic,
                PlayerSide = learnerStance,
                CurrentCreeiStage = NormalizeCreeiFocus(episodeController.ConfirmedFocus),
                TurnId = _practiceCycleIndex,
                PlayerUtteranceText = _confirmedPracticeTranscript,
                PreviousCoachFeedbackText = _lastExperimentFeedback,
                SelectedStrategy = selectedStrategy,
                ConfirmedFocus = episodeController.ConfirmedFocus,
                DiagnosisIssueCode = _practiceDiagnosis?.DiagnosisIssueCode ?? string.Empty,
                RecommendedStrategy = _practiceDiagnosis?.RecommendedStrategy ?? string.Empty,
                TargetSuccessCriterion = _practiceDiagnosis?.RecommendedNextAction ?? string.Empty,
                FeedbackLevel = level,
                DetailedJson = true
            };

            CoachFeedbackResult result = null;
            yield return (_coachGenerator ??= new DebateCoachFeedbackGenerator(
                    openAIModel, coachResponseTimeoutSeconds, openAIBaseUrl, openAIApiKeyOverride))
                .GenerateFeedback(request, value => result = value);
            _experimentFeedbackRoutine = null;

            if (Phase != OrchestrationPhase.PracticeCycleCoach ||
                result == null || result.Source != CoachFeedbackSource.OpenAI ||
                string.IsNullOrWhiteSpace(result.FeedbackText))
            {
                LogExperimentEvent("FeedbackTechnicalFailure");
                experimentView?.ShowCoach(
                    "Coach feedback could not be generated. Retry the same request or end coaching safely.",
                    _lastExperimentFeedback,
                    false,
                    CoachLearnerAction.RetryFeedback,
                    CoachLearnerAction.ApplyNextCycle,
                    CoachLearnerAction.ExitCoaching);
                yield break;
            }

            _lastExperimentFeedback = result.FeedbackText.Trim();
            episodeController.NotifyFeedbackPresented(
                level.ToString(),
                result.FeedbackType,
                _lastExperimentFeedback);
            bool aiLed = _orchestrationMode == CoachOrchestrationMode.AiLed;
            experimentView?.ShowCoach(
                aiLed
                    ? $"Coach feedback. Action window: {episodeController.ActionWindowRemaining:0} seconds."
                    : "Coach feedback is ready. Choose the next action.",
                _lastExperimentFeedback,
                true,
                aiLed
                    ? new[]
                    {
                        CoachLearnerAction.NeedExample,
                        CoachLearnerAction.Override,
                        CoachLearnerAction.Replay,
                        CoachLearnerAction.ExitCoaching
                    }
                    : new[]
                    {
                        CoachLearnerAction.NeedExample,
                        CoachLearnerAction.ChangeFocus,
                        CoachLearnerAction.Replay,
                        CoachLearnerAction.ApplyNextCycle,
                        CoachLearnerAction.ExitCoaching
                    });

            if (speakCoachFeedback && coachNPC != null)
            {
                StartCoachFeedbackSpeech(_lastExperimentFeedback);
            }
        }

        private static string NormalizeCreeiFocus(string focus)
        {
            return Enum.TryParse(focus, true, out CreeiStage stage)
                ? stage.ToString()
                : CreeiStage.Explanation.ToString();
        }

        private void HandleCoachEpisodeEnded(CoachTerminationReason reason)
        {
            if (Phase != OrchestrationPhase.PracticeCycleCoach)
            {
                return;
            }

            StopCoachFeedbackSpeech(true);
            if (_isShuttingDown)
            {
                return;
            }
            AdvanceToNextPracticeCycle();
        }

        private void AdvanceToNextPracticeCycle()
        {
            CancelExperimentNetworkRequests();
            if (_practiceCycleIndex >= PracticeCycleCount)
            {
                CompleteCoachStudyAndLoadTransfer();
                return;
            }

            _practiceCycleIndex++;
            BeginPracticeCycle();
        }

        private void CompleteCoachStudyAndLoadTransfer()
        {
            Phase = OrchestrationPhase.Complete;
            _researchLogger?.Flush();
            CancelExperimentActivity();
            RestorePlayerControlState();
            experimentView?.HideAll();
            SceneManager.LoadScene("05Level_PlayerVsNPCDebate 1");
        }

        private void CancelExperimentNetworkRequests()
        {
            if (_diagnosisRoutine != null)
            {
                StopCoroutine(_diagnosisRoutine);
                _diagnosisRoutine = null;
            }
            _diagnosisEngine?.Cancel();
            if (_experimentFeedbackRoutine != null)
            {
                StopCoroutine(_experimentFeedbackRoutine);
                _experimentFeedbackRoutine = null;
            }
            _coachGenerator?.Cancel();
        }

        private void CancelExperimentActivity()
        {
            if (episodeController != null && episodeController.HasOpenOpportunity)
            {
                episodeController.Complete(
                    CoachTerminationReason.SafetyOverride,
                    ControlOwner.SystemSafety);
            }
            CancelExperimentNetworkRequests();
            StopPracticeCycleTranscriptTimeout();
            _practiceCycleRecording = false;
            _practiceCycleAutoStopRequested = false;
            _practiceCycleTechnicalFallback = false;
            _practiceVoiceRecording = false;
            _practiceVoiceAwaitingTranscript = false;
            _mockDebateRecording = false;
            _realtimeCapturePurpose = RealtimeCapturePurpose.None;
            realtimeTranscriber?.CancelSession();
            StopCoachFeedbackSpeech(true);
            _researchLogger?.Flush();
        }

        private void LogExperimentEvent(
            string eventType,
            CoachTerminationReason terminationReason = CoachTerminationReason.None,
            CoachLearnerAction learnerAction = CoachLearnerAction.None)
        {
            CoachStudySessionSnapshot session = CoachStudySessionContext.Current;
            CoachEpisodeState state = episodeController?.State ?? CoachEpisodeState.Inactive;
            ControlOwner startOwner = _orchestrationMode switch
            {
                CoachOrchestrationMode.LearnerLed => ControlOwner.Learner,
                CoachOrchestrationMode.SharedControl => ControlOwner.Shared,
                CoachOrchestrationMode.AiLed => ControlOwner.Coach,
                _ => ControlOwner.SystemSafety
            };
            _researchLogger?.LogEvent(new CoachEventRecord
            {
                ParticipantId = session?.ParticipantId ?? participantId,
                SessionId = session?.SessionId ?? string.Empty,
                OrchestrationMode = _orchestrationMode,
                Stage = "PracticeDebate",
                TopicId = topicId,
                PracticeCycleId = _practiceCycleIndex,
                TurnId = _practiceCycleIndex,
                CoachEpisodeId = episodeController?.CoachEpisodeId ?? string.Empty,
                EpisodeStateBefore = state,
                EpisodeStateAfter = state,
                EventType = eventType ?? string.Empty,
                DiagnosisIssueCode = _practiceDiagnosis?.DiagnosisIssueCode ?? string.Empty,
                DiagnosisSeverity = _practiceDiagnosis?.Severity ?? 0,
                DiagnosisConfidence = _practiceDiagnosis?.Confidence ?? 0f,
                ConfirmedFocus = episodeController?.ConfirmedFocus ?? string.Empty,
                StartAuthority = startOwner,
                AgendaOwner = startOwner,
                PacingOwner = startOwner,
                LearnerControlAction = learnerAction == CoachLearnerAction.None
                    ? string.Empty
                    : learnerAction.ToString(),
                ConfirmedLearnerText = _confirmedPracticeTranscript,
                TerminationReason = terminationReason,
                TerminationOwner = terminationReason == CoachTerminationReason.TechnicalFailure
                    ? ControlOwner.SystemSafety
                    : _orchestrationMode == CoachOrchestrationMode.AiLed
                        ? ControlOwner.Coach
                        : ControlOwner.Learner,
                DiagnosisModelVersion = _coachPolicyConfig.DiagnosisModelVersion,
                FeedbackModelVersion = _coachPolicyConfig.FeedbackModelVersion,
                PolicyVersion = _coachPolicyConfig.PolicyVersion
            });
        }

        private void FreezePlayerMovement()
        {
            if (_playerMovementFrozen)
            {
                return;
            }

            _playerMovementComponents = FindObjectsByType<ConvaiPlayerMovement>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
            _playerMovementOriginalStates = new bool[_playerMovementComponents.Length];
            for (int index = 0; index < _playerMovementComponents.Length; index++)
            {
                ConvaiPlayerMovement movement = _playerMovementComponents[index];
                _playerMovementOriginalStates[index] = movement != null && movement.enabled;
                if (movement != null) movement.enabled = false;
            }

            _playerMovementFrozen = true;
        }

        private void RestorePlayerControlState()
        {
            if (_playerMovementFrozen)
            {
                for (int index = 0; index < _playerMovementComponents.Length; index++)
                {
                    ConvaiPlayerMovement movement = _playerMovementComponents[index];
                    if (movement != null && index < _playerMovementOriginalStates.Length)
                    {
                        movement.enabled = _playerMovementOriginalStates[index];
                    }
                }

                _playerMovementFrozen = false;
            }

            _isUiMode = _originalCursorVisible || _originalCursorLockMode != CursorLockMode.Locked;
            Cursor.lockState = _originalCursorLockMode;
            Cursor.visible = _originalCursorVisible;
        }

        private bool IsPracticeStageVoicePhase()
        {
            return Phase == OrchestrationPhase.WaitingForPlayer ||
                   Phase == OrchestrationPhase.PlayerResponseReady ||
                   Phase == OrchestrationPhase.CoachGenerating ||
                   (Phase == OrchestrationPhase.CoachSuggestionReady && !_mockDebateFeedbackReady);
        }

        private void TogglePracticeStageRecording()
        {
            if (_practiceVoiceRecording)
            {
                _practiceVoiceRecording = false;
                _practiceVoiceAwaitingTranscript = true;
                SetStatus("Recording stopped. Finalizing the English transcript before Coach analysis...");
                realtimeTranscriber?.StopSession();
                return;
            }

            if (_practiceVoiceAwaitingTranscript)
            {
                SetStatus("The recording has stopped. Wait for the final transcript and Coach analysis.");
                return;
            }

            if (Phase == OrchestrationPhase.OpponentSpeaking)
            {
                SetStatus($"Wait for Leo to finish his {CurrentCreeiStage} statement, then press T once to record.");
                return;
            }

            if (!IsPracticeStageVoicePhase())
            {
                ShowTalkInputBlockedStatus();
                return;
            }

            if (realtimeTranscriber == null)
            {
                SetStatus("The English realtime transcription component is not assigned.");
                return;
            }

            if (realtimeTranscriber.IsConnecting)
            {
                SetStatus("English realtime transcription is still connecting...");
                return;
            }

            PrepareForPlayerVoiceInput();
            BeginPlayerRetake();
            _realtimeCapturePurpose = RealtimeCapturePurpose.PracticeStage;
            _practiceVoiceRecording = false;
            _practiceVoiceAwaitingTranscript = false;
            SetStatus("Connecting to English realtime transcription...");
            string microphoneDevice = MicrophoneManager.Instance?.SelectedMicrophoneName ?? string.Empty;
            realtimeTranscriber.StartSession(microphoneDevice);
        }

        private void StartMockDebateTapRecording()
        {
            if (!IsMockDebateInputPhase())
            {
                return;
            }

            if (_mockDebateRecording)
            {
                SetStatus("Mock Debate is already recording. It will stop automatically at 03:00.");
                return;
            }

            if (realtimeTranscriber != null && realtimeTranscriber.IsConnecting)
            {
                SetStatus("English realtime transcription is still connecting...");
                return;
            }

            PrepareForPlayerVoiceInput();
            if (realtimeTranscriber == null)
            {
                Phase = OrchestrationPhase.MockDebateReady;
                SetStatus("The English realtime transcription component is not assigned.");
                return;
            }

            SetStatus("Connecting to English realtime transcription...");
            _realtimeCapturePurpose = RealtimeCapturePurpose.MockDebate;
            string microphoneDevice = MicrophoneManager.Instance?.SelectedMicrophoneName ?? string.Empty;
            realtimeTranscriber.StartSession(microphoneDevice);
        }

        private void StopMockDebateRecordingAtTimeLimit()
        {
            if (!_mockDebateRecording || _mockDebateAutoStopRequested)
            {
                return;
            }

            _mockDebateAutoStopRequested = true;
            _mockDebateRecording = false;
            _mockDebateDurationSeconds = mockDebateTargetSeconds;
            Phase = OrchestrationPhase.MockDebateAwaitingTranscript;
            UpdateMockDebateStageUi();
            SetStatus("03:00 reached. Recording stopped; waiting for the final English transcript...");
            realtimeTranscriber?.StopSession();
            if (_mockDebateTranscriptFallbackRoutine != null)
            {
                StopCoroutine(_mockDebateTranscriptFallbackRoutine);
            }

            _mockDebateTranscriptFallbackRoutine = StartCoroutine(CompleteMockDebateAfterTranscriptTimeout());
        }

        private void SubscribeToRealtimeTranscriber()
        {
            if (realtimeTranscriber == null)
            {
                return;
            }

            realtimeTranscriber.SessionStarted -= HandleRealtimeTranscriptionStarted;
            realtimeTranscriber.TranscriptUpdated -= HandleRealtimeTranscriptUpdated;
            realtimeTranscriber.SessionCompleted -= HandleRealtimeTranscriptionCompleted;
            realtimeTranscriber.SessionFailed -= HandleRealtimeTranscriptionFailed;
            realtimeTranscriber.SessionStarted += HandleRealtimeTranscriptionStarted;
            realtimeTranscriber.TranscriptUpdated += HandleRealtimeTranscriptUpdated;
            realtimeTranscriber.SessionCompleted += HandleRealtimeTranscriptionCompleted;
            realtimeTranscriber.SessionFailed += HandleRealtimeTranscriptionFailed;
        }

        private void UnsubscribeFromRealtimeTranscriber()
        {
            if (realtimeTranscriber == null)
            {
                return;
            }

            realtimeTranscriber.SessionStarted -= HandleRealtimeTranscriptionStarted;
            realtimeTranscriber.TranscriptUpdated -= HandleRealtimeTranscriptUpdated;
            realtimeTranscriber.SessionCompleted -= HandleRealtimeTranscriptionCompleted;
            realtimeTranscriber.SessionFailed -= HandleRealtimeTranscriptionFailed;
        }

        private void HandleRealtimeTranscriptionStarted()
        {
            if (_realtimeCapturePurpose == RealtimeCapturePurpose.PracticeCycle)
            {
                if (Phase != OrchestrationPhase.PracticeCycleReady)
                {
                    _realtimeCapturePurpose = RealtimeCapturePurpose.None;
                    realtimeTranscriber?.CancelSession();
                    return;
                }

                _practiceCycleRecording = true;
                _practiceCycleAutoStopRequested = false;
                _practiceCycleStartedAt = Time.realtimeSinceStartup;
                _practiceCycleDurationSeconds = 0f;
                Phase = OrchestrationPhase.PracticeCycleSpeaking;
                experimentView?.SetTranscriptEditable(false);
                experimentView?.ShowPractice(_practiceCycleIndex,
                    "Recording. Keep speaking for at least 01:00.", 0f, string.Empty, false, false);
                return;
            }

            if (_realtimeCapturePurpose == RealtimeCapturePurpose.PracticeStage)
            {
                if (!IsPracticeStageVoicePhase())
                {
                    _realtimeCapturePurpose = RealtimeCapturePurpose.None;
                    realtimeTranscriber?.CancelSession();
                    return;
                }

                _practiceVoiceRecording = true;
                _practiceVoiceAwaitingTranscript = false;
                SetStatus($"Recording your {CurrentCreeiStage}. Press T again when you finish.");
                if (_playerText != null)
                {
                    _playerText.text = "You (recording): start speaking...";
                }

                return;
            }

            if (_realtimeCapturePurpose != RealtimeCapturePurpose.MockDebate ||
                Phase != OrchestrationPhase.MockDebateReady)
            {
                _realtimeCapturePurpose = RealtimeCapturePurpose.None;
                realtimeTranscriber?.CancelSession();
                return;
            }

            BeginMockDebateRecording();
            SetStatus("English realtime transcription is active. Recording stops automatically at 03:00.");
        }

        private void HandleRealtimeTranscriptUpdated(string transcript)
        {
            if (_realtimeCapturePurpose == RealtimeCapturePurpose.PracticeCycle)
            {
                if (Phase != OrchestrationPhase.PracticeCycleSpeaking &&
                    Phase != OrchestrationPhase.PracticeCycleAwaitingTranscript)
                {
                    return;
                }

                _practiceCycleTranscript = transcript?.Trim() ?? string.Empty;
                experimentView?.ShowPractice(_practiceCycleIndex,
                    Phase == OrchestrationPhase.PracticeCycleAwaitingTranscript
                        ? "Finalizing transcript..."
                        : "Recording your complete argument...",
                    _practiceCycleDurationSeconds,
                    _practiceCycleTranscript,
                    false,
                    false);
                return;
            }

            if (_realtimeCapturePurpose == RealtimeCapturePurpose.PracticeStage)
            {
                string practiceTranscript = transcript?.Trim() ?? string.Empty;
                SetLivePlayerVoiceTranscript(practiceTranscript);
                if (_learnerNoteInput != null)
                {
                    _learnerNoteInput.SetTextWithoutNotify(practiceTranscript);
                }

                if (_playerText != null)
                {
                    _playerText.text = string.IsNullOrWhiteSpace(practiceTranscript)
                        ? "You (recording):"
                        : "You (recording): " + BuildRollingTranscriptText(practiceTranscript, string.Empty);
                }

                return;
            }

            if (_realtimeCapturePurpose != RealtimeCapturePurpose.MockDebate)
            {
                return;
            }

            if (Phase != OrchestrationPhase.MockDebateSpeaking &&
                Phase != OrchestrationPhase.MockDebateAwaitingTranscript)
            {
                return;
            }

            string safeTranscript = transcript?.Trim() ?? string.Empty;
            _completeMockDebateTranscript.Clear();
            _completeMockDebateTranscript.Append(safeTranscript);
            ClearLivePlayerVoiceTranscript();
            if (_learnerNoteInput != null)
            {
                _learnerNoteInput.SetTextWithoutNotify(safeTranscript);
            }

            if (_playerText != null)
            {
                _playerText.text = string.IsNullOrWhiteSpace(safeTranscript)
                    ? "You (listening):"
                    : "You (listening): " + BuildRollingTranscriptText(safeTranscript, string.Empty);
            }
        }

        private void HandleRealtimeTranscriptionCompleted(string transcript)
        {
            if (_realtimeCapturePurpose == RealtimeCapturePurpose.PracticeCycle)
            {
                StopPracticeCycleTranscriptTimeout();
                _practiceCycleRecording = false;
                _practiceCycleAutoStopRequested = false;
                _realtimeCapturePurpose = RealtimeCapturePurpose.None;
                string finalTranscript = transcript?.Trim() ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(finalTranscript))
                {
                    _practiceCycleTranscript = finalTranscript;
                }

                Phase = OrchestrationPhase.PracticeCycleConfirmingTranscript;
                SetControlCursor(true);
                experimentView?.SetTranscriptEditable(false);
                experimentView?.ShowPractice(
                    _practiceCycleIndex,
                    string.IsNullOrWhiteSpace(_practiceCycleTranscript)
                        ? "No speech was transcribed. Re-record this cycle."
                        : "Review the read-only transcript. Confirm it or re-record the whole argument.",
                    _practiceCycleDurationSeconds,
                    _practiceCycleTranscript,
                    !string.IsNullOrWhiteSpace(_practiceCycleTranscript),
                    true);
                return;
            }

            if (_realtimeCapturePurpose == RealtimeCapturePurpose.PracticeStage)
            {
                _practiceVoiceRecording = false;
                _practiceVoiceAwaitingTranscript = false;
                _realtimeCapturePurpose = RealtimeCapturePurpose.None;
                string practiceTranscript = transcript?.Trim() ?? string.Empty;
                ClearLivePlayerVoiceTranscript();
                if (string.IsNullOrWhiteSpace(practiceTranscript))
                {
                    Phase = OrchestrationPhase.WaitingForPlayer;
                    SetStatus($"No English speech was transcribed. Press T once to record your {CurrentCreeiStage} again.");
                    if (_playerText != null)
                    {
                        _playerText.text = "You: (No speech was transcribed.)";
                    }

                    SetButtonsForPhase();
                    return;
                }

                FinalizeOpponentTurnForPlayerVoice();
                HandlePlayerUtterance(practiceTranscript, false);
                return;
            }

            if (_realtimeCapturePurpose != RealtimeCapturePurpose.MockDebate)
            {
                return;
            }

            if (Phase != OrchestrationPhase.MockDebateAwaitingTranscript)
            {
                return;
            }

            _realtimeCapturePurpose = RealtimeCapturePurpose.None;
            string safeTranscript = transcript?.Trim() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(safeTranscript))
            {
                _completeMockDebateTranscript.Clear();
                _completeMockDebateTranscript.Append(safeTranscript);
            }

            FinalizeMockDebateTranscriptAndEvaluate();
        }

        private void HandleRealtimeTranscriptionFailed(string error)
        {
            string safeError = string.IsNullOrWhiteSpace(error)
                ? "English realtime transcription failed for an unknown reason."
                : error.Trim();

            if (_realtimeCapturePurpose == RealtimeCapturePurpose.PracticeCycle)
            {
                StopPracticeCycleTranscriptTimeout();
                _practiceCycleAutoStopRequested = false;
                _realtimeCapturePurpose = RealtimeCapturePurpose.None;
                if (!CoachSpeechWindow.CanSubmit(_practiceCycleDurationSeconds, _coachPolicyConfig))
                {
                    _practiceCycleTechnicalFallback = true;
                    _practiceCycleRecording = true;
                    if (_practiceCycleDurationSeconds <= 0f)
                    {
                        _practiceCycleStartedAt = Time.realtimeSinceStartup;
                    }
                    Phase = OrchestrationPhase.PracticeCycleSpeaking;
                    experimentView?.SetTranscriptEditable(false);
                    experimentView?.ShowPractice(
                        _practiceCycleIndex,
                        safeError + " Keep speaking until at least 01:00; a researcher can enter a fallback transcript afterward.",
                        _practiceCycleDurationSeconds,
                        _practiceCycleTranscript,
                        false,
                        false);
                    return;
                }

                _practiceCycleRecording = false;
                _practiceCycleTechnicalFallback = true;
                Phase = OrchestrationPhase.PracticeCycleConfirmingTranscript;
                SetControlCursor(true);
                experimentView?.SetTranscriptEditable(true);
                experimentView?.ShowPractice(
                    _practiceCycleIndex,
                    safeError + " A researcher may enter a fallback transcript, or re-record.",
                    _practiceCycleDurationSeconds,
                    _practiceCycleTranscript,
                    true,
                    true);
                return;
            }

            if (_realtimeCapturePurpose == RealtimeCapturePurpose.PracticeStage)
            {
                _practiceVoiceRecording = false;
                _practiceVoiceAwaitingTranscript = false;
                _realtimeCapturePurpose = RealtimeCapturePurpose.None;
                Phase = OrchestrationPhase.WaitingForPlayer;
                SetStatus(safeError + " Press T once to try recording again.");
                ShowCoachLineInLeftUi("Realtime English transcription failed. Check the network, microphone, and iFlytek credentials.");
                SetButtonsForPhase();
                return;
            }

            if (_realtimeCapturePurpose != RealtimeCapturePurpose.MockDebate ||
                (!IsMockDebateInputPhase() && !_mockDebateRecording))
            {
                return;
            }

            bool wasWaitingForFinal = Phase == OrchestrationPhase.MockDebateAwaitingTranscript;
            _mockDebateRecording = false;
            _mockDebateAutoStopRequested = false;
            if (_mockDebateTranscriptFallbackRoutine != null)
            {
                StopCoroutine(_mockDebateTranscriptFallbackRoutine);
                _mockDebateTranscriptFallbackRoutine = null;
            }

            _realtimeCapturePurpose = RealtimeCapturePurpose.None;
            if (wasWaitingForFinal && _completeMockDebateTranscript.Length > 0)
            {
                SetStatus(safeError + " The transcript received so far will be evaluated.");
                FinalizeMockDebateTranscriptAndEvaluate();
                return;
            }

            Phase = OrchestrationPhase.MockDebateReady;
            SetStatus(safeError + " Press T to try again.");
            ShowCoachLineInLeftUi("Realtime English transcription could not start. Check the network, microphone, and iFlytek credentials.");
            UpdateMockDebateStageUi();
            SetButtonsForPhase();
        }

        private IEnumerator CompleteMockDebateAfterTranscriptTimeout()
        {
            yield return new WaitForSecondsRealtime(12f);
            _mockDebateTranscriptFallbackRoutine = null;
            if (Phase == OrchestrationPhase.MockDebateAwaitingTranscript)
            {
                FinalizeMockDebateTranscriptAndEvaluate();
            }
        }

        private void FinalizeMockDebateTranscriptAndEvaluate()
        {
            if (_mockDebateTranscriptFallbackRoutine != null)
            {
                StopCoroutine(_mockDebateTranscriptFallbackRoutine);
                _mockDebateTranscriptFallbackRoutine = null;
            }

            _mockDebateTranscript = _completeMockDebateTranscript.ToString().Trim();
            _latestPlayerUtterance = _mockDebateTranscript;
            SessionRecord.latestLearnerNote = _mockDebateTranscript;
            if (_learnerNoteInput != null)
            {
                _learnerNoteInput.SetTextWithoutNotify(_mockDebateTranscript);
            }

            if (_playerText != null)
            {
                _playerText.text = string.IsNullOrWhiteSpace(_mockDebateTranscript)
                    ? "You: (No speech was transcribed.)"
                    : "You: " + BuildRollingTranscriptText(_mockDebateTranscript, string.Empty);
            }

            StartMockDebateEvaluation();
        }

        private void StartMockDebateEvaluation()
        {
            _mockDebateRequestVersion++;
            if (_mockDebateRoutine != null)
            {
                StopCoroutine(_mockDebateRoutine);
            }

            MockDebateEvaluationRequest request = new()
            {
                Topic = debateTopic,
                LearnerStance = learnerStance,
                Transcript = _mockDebateTranscript,
                DurationSeconds = _mockDebateDurationSeconds,
                TargetDurationSeconds = mockDebateTargetSeconds
            };
            int requestVersion = _mockDebateRequestVersion;
            Phase = OrchestrationPhase.MockDebateEvaluating;
            SetControlCursor(true);
            SetStatus("Coach is evaluating the complete Mock Debate...");
            ShowCoachLineInLeftUi("Coach is checking the full speech against CREEI and preparing final feedback.");
            UpdateMockDebateStageUi();
            UpdateMockDebateDebugPrompt(request);
            SetButtonsForPhase();
            _mockDebateRoutine = StartCoroutine(RequestMockDebateEvaluation(request, requestVersion));
        }

        private IEnumerator RequestMockDebateEvaluation(MockDebateEvaluationRequest request, int requestVersion)
        {
            MockDebateEvaluationResult result = null;
            yield return (_mockDebateGenerator ??= new MockDebateEvaluationGenerator(
                    openAIModel,
                    GetMockDebateResponseTimeoutSeconds(),
                    openAIBaseUrl,
                    openAIApiKeyOverride))
                .Evaluate(request, response => result = response);

            if (requestVersion != _mockDebateRequestVersion)
            {
                yield break;
            }

            _mockDebateRoutine = null;
            UpdateMockDebateDebugResult(request, result);

            if (result == null || !result.IsValid)
            {
                Phase = OrchestrationPhase.MockDebateReady;
                SetControlCursor(false);
                ShowCoachLineInLeftUi("GPT did not return valid Mock Debate feedback. Hold T to try again.");
                SetStatus("Mock Debate evaluation failed. Check Coach Debug, then record again.");
                SetButtonsForPhase();
                yield break;
            }

            ApplyMockDebateFeedback(request, result);
        }

        private float GetMockDebateResponseTimeoutSeconds()
        {
            return Mathf.Max(MockDebateMinimumResponseTimeoutSeconds, coachResponseTimeoutSeconds);
        }

        private void ApplyMockDebateFeedback(
            MockDebateEvaluationRequest request,
            MockDebateEvaluationResult result)
        {
            string feedback = result.FeedbackText.Trim();
            SessionRecord.latestCoachSuggestion = feedback;
            _latestFeedbackText = feedback;
            ShowCoachFeedbackOnBoard("Mock Debate - Complete CREEI", feedback);
            _latestCoachRequest = new CoachFeedbackRequest
            {
                Stage = "Mock Debate",
                TopicId = topicId,
                Topic = debateTopic,
                PlayerSide = learnerStance,
                CurrentCreeiStage = "Complete CREEI",
                TurnId = CreeiStages.Length + 1,
                PlayerUtteranceText = request.Transcript,
                FeedbackLevel = CoachFeedbackLevel.Summary
            };
            _latestCoachResult = new CoachFeedbackResult
            {
                Source = CoachFeedbackSource.OpenAI,
                FeedbackLevel = CoachFeedbackLevel.Summary,
                FeedbackType = "Mock Debate Summary",
                FeedbackText = feedback,
                DebugInfo = result.DebugInfo,
                RawJson = result.RawJson
            };
            _coachFeedbackSentToAnna = false;
            _mockDebateFeedbackReady = true;
            Phase = OrchestrationPhase.CoachSuggestionReady;
            SetControlCursor(true);
            ShowCoachLineInLeftUi("Full CREEI summary is ready. Press Ask Anna when you want to hear it.");
            SetStatus("Mock Debate analysis is ready. Press Ask Anna to hear the complete CREEI summary and advice.");

            (_coachLogger ??= new DebateCoachLogger()).LogFeedback(BuildLogRow(_latestCoachRequest, _latestCoachResult, false));
            SetButtonsForPhase();
        }

        private void HandleConvaiResultReceived(GetResponseResponse result)
        {
            if (IsMockDebatePhase() || _realtimeCapturePurpose != RealtimeCapturePurpose.None)
            {
                return;
            }

            if (result?.UserQuery == null || !automaticCoachAfterPlayerVoice || !CanHandlePlayerVoiceTranscript())
            {
                return;
            }

            string chunk = result.UserQuery.TextData ?? string.Empty;
            bool isFinal = result.UserQuery.IsFinal;
            bool isEndOfResponse = result.UserQuery.EndOfResponse;

            RunOnMainThread(() => HandlePlayerVoiceTranscriptChunk(chunk, isFinal, isEndOfResponse));
        }

        private void HandlePlayerVoiceTranscriptChunk(string chunk, bool isFinal, bool isEndOfResponse)
        {
            if (!CanHandlePlayerVoiceTranscript())
            {
                return;
            }

            if (IsMockDebateInputPhase())
            {
                string preview = BuildRollingTranscriptText(
                    _completeMockDebateTranscript.ToString(),
                    isEndOfResponse ? string.Empty : chunk);
                if (!string.IsNullOrWhiteSpace(preview) && _playerText != null)
                {
                    _playerText.text = isEndOfResponse
                        ? "You: " + preview
                        : "You (listening): " + preview;
                }

                return;
            }

            string previewTranscript = CombineTranscript(_livePlayerVoiceTranscript.ToString(), chunk);
            if (!string.IsNullOrWhiteSpace(previewTranscript))
            {
                ShowPlayerVoiceTranscript(previewTranscript, isEndOfResponse);
            }

            if (isFinal)
            {
                SetLivePlayerVoiceTranscript(CombineTranscript(_livePlayerVoiceTranscript.ToString(), chunk));
            }

            if (isEndOfResponse)
            {
                ClearLivePlayerVoiceTranscript();
            }
        }

        private void SetLivePlayerVoiceTranscript(string transcript)
        {
            _livePlayerVoiceTranscript.Clear();
            _livePlayerVoiceTranscript.Append(transcript?.Trim() ?? string.Empty);
        }

        private void ClearLivePlayerVoiceTranscript()
        {
            _livePlayerVoiceTranscript.Clear();
        }

        private void ShowPlayerVoiceTranscript(string transcript, bool isFinal)
        {
            string safeTranscript = transcript?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(safeTranscript))
            {
                return;
            }

            if (_learnerNoteInput != null)
            {
                _learnerNoteInput.SetTextWithoutNotify(safeTranscript);
            }

            if (_playerText != null)
            {
                _playerText.text = isFinal
                    ? "You: " + safeTranscript
                    : "You (listening): " + safeTranscript;
            }

            if (!isFinal)
            {
                SetStatus("Recording... press T again when you finish speaking.");
            }
        }

        private static void RunOnMainThread(Action action)
        {
            if (MainThreadDispatcher.Instance != null)
            {
                MainThreadDispatcher.Instance.RunOnMainThread(action);
                return;
            }

            action?.Invoke();
        }

        private static string CombineTranscript(string existingText, string nextChunk)
        {
            string existing = existingText?.Trim() ?? string.Empty;
            string next = nextChunk?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(existing))
            {
                return next;
            }

            if (string.IsNullOrWhiteSpace(next))
            {
                return existing;
            }

            return existing.EndsWith(" ", StringComparison.Ordinal) || next.StartsWith(" ", StringComparison.Ordinal)
                ? existing + next
                : existing + " " + next;
        }

        private static string BuildRollingTranscriptText(string confirmedText, string livePreview)
        {
            string confirmed = confirmedText?.Trim() ?? string.Empty;
            string preview = livePreview?.Trim() ?? string.Empty;
            string combined;
            if (string.IsNullOrWhiteSpace(confirmed))
            {
                combined = preview;
            }
            else if (string.IsNullOrWhiteSpace(preview) || confirmed.EndsWith(preview, StringComparison.Ordinal))
            {
                combined = confirmed;
            }
            else
            {
                combined = confirmed + " " + preview;
            }

            const int maxVisibleCharacters = 180;
            if (combined.Length <= maxVisibleCharacters)
            {
                return combined;
            }

            return "..." + combined.Substring(combined.Length - maxVisibleCharacters).TrimStart();
        }

        private void StartCoachFeedbackRequest(CoachFeedbackLevel level, bool exampleRequested, bool speakWhenReady = false)
        {
            _coachRequestVersion++;
            if (_coachRoutine != null)
            {
                StopCoroutine(_coachRoutine);
                _coachRoutine = null;
            }

            _coachRoutine = StartCoroutine(RequestCoachFeedback(level, exampleRequested, speakWhenReady, _coachRequestVersion));
        }

        private IEnumerator RequestCoachFeedback(CoachFeedbackLevel level, bool exampleRequested, bool speakWhenReady, int requestVersion)
        {
            Phase = OrchestrationPhase.CoachGenerating;
            SetControlCursor(true);
            SetButtonsForPhase();
            if (speakWhenReady)
            {
                ConvaiNPCManager.Instance?.SetActiveConvaiNPC(coachNPC);
            }

            CoachFeedbackRequest request = BuildCoachRequest(level);
            UpdateCoachDebugPrompt(request);
            string pendingCoachText = level == CoachFeedbackLevel.Summary
                ? "Coach is preparing your practice summary..."
                : level == CoachFeedbackLevel.Level3
                    ? $"Coach is preparing a concrete {CurrentCreeiStage} example based on your response and Anna's advice..."
                    : $"Coach is evaluating only the {CurrentCreeiStage} stage of your response...";
            ShowCoachLineInLeftUi(pendingCoachText);

            CoachFeedbackResult result = null;
            yield return (_coachGenerator ??= new DebateCoachFeedbackGenerator(
                    openAIModel,
                    coachResponseTimeoutSeconds,
                    openAIBaseUrl,
                    openAIApiKeyOverride))
                .GenerateFeedback(request, feedback => result = feedback);

            if (requestVersion != _coachRequestVersion)
            {
                yield break;
            }

            result ??= DebateCoachFeedbackGenerator.BuildLocalFallback(request);
            if (string.IsNullOrWhiteSpace(result.DebugInfo))
            {
                result.DebugInfo = "Feedback came from the built-in local fallback rules.";
            }

            ApplyCoachFeedback(request, result, exampleRequested, speakWhenReady);
            _coachRoutine = null;
        }

        private void ApplyCoachFeedback(CoachFeedbackRequest request, CoachFeedbackResult result, bool exampleRequested, bool speakWhenReady)
        {
            if (!IsUsableCoachFeedback(result))
            {
                ApplyUnavailableCoachFeedback(request, result, speakWhenReady);
                return;
            }

            string feedback = result.FeedbackText.Trim();
            SessionRecord.latestCoachSuggestion = feedback;
            _latestCoachRequest = request;
            _latestCoachResult = result;
            _latestFeedbackText = feedback;
            _coachFeedbackSentToAnna = false;
            string boardHeading = result.FeedbackLevel == CoachFeedbackLevel.Level3
                ? request.CurrentCreeiStage + " Example"
                : result.FeedbackLevel == CoachFeedbackLevel.Summary
                    ? "Final Coach Summary"
                    : request.CurrentCreeiStage + " Feedback";
            ShowCoachFeedbackOnBoard(boardHeading, feedback);
            ShowCoachLineInLeftUi(result.FeedbackLevel == CoachFeedbackLevel.Level3
                ? "Concrete example ready. Anna will say it now."
                : "Feedback ready. Press Ask Anna to hear it.");
            UpdateCoachDebugResult(request, result, feedback);

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
                    ? "Concrete example ready. Press Ask Anna to hear it, or Continue when you are ready."
                    : "Coach feedback is ready. Press Ask Anna to hear it, request an example, or Continue.");
            }

            if (speakWhenReady && speakCoachFeedback && coachNPC != null)
            {
                _coachFeedbackSentToAnna = true;
                StartCoachFeedbackSpeech(feedback);
            }

            SetButtonsForPhase();
        }

        private void ApplyUnavailableCoachFeedback(CoachFeedbackRequest request, CoachFeedbackResult result, bool speakWhenReady)
        {
            result ??= DebateCoachFeedbackGenerator.BuildLocalFallback(request);
            if (string.IsNullOrWhiteSpace(result.DebugInfo))
            {
                result.DebugInfo = result.Source == CoachFeedbackSource.OpenAI
                    ? "GPT returned an empty Coach feedback message."
                    : "GPT did not return valid Coach feedback. Local default feedback is disabled for this scene.";
            }

            if (request.FeedbackLevel == CoachFeedbackLevel.Level3)
            {
                UpdateCoachDebugResult(request, result, string.Empty);
                _exampleRequestedForCurrentTurn = false;
                Phase = OrchestrationPhase.CoachSuggestionReady;
                ShowCoachLineInLeftUi("GPT did not return a concrete example. Your previous Coach advice is still available, and you can request the example again.");
                SetStatus("The Example result was rejected because it looked like advice or was invalid. Press Need an example? to retry.");
                SetButtonsForPhase();
                return;
            }

            SessionRecord.latestCoachSuggestion = string.Empty;
            _latestCoachRequest = request;
            _latestCoachResult = result;
            _latestFeedbackText = string.Empty;
            _coachFeedbackSentToAnna = false;

            UpdateCoachDebugResult(request, result, string.Empty);
            ShowCoachLineInLeftUi("GPT did not return valid Coach feedback. Ask Anna is disabled for this turn.");

            if (result.FeedbackLevel == CoachFeedbackLevel.Summary)
            {
                Phase = OrchestrationPhase.Complete;
                SetStatus("GPT did not return a valid final Coach summary. Check the debug panel.");
            }
            else
            {
                Phase = OrchestrationPhase.CoachSuggestionReady;
                SetStatus(BuildCoachFailureSummary(result) + " Check the debug panel, then Continue or End Session.");
            }

            if (speakWhenReady)
            {
                _coachFeedbackSentToAnna = false;
            }

            SetButtonsForPhase();
        }

        private void ClearPreparedCoachFeedback()
        {
            _latestCoachRequest = null;
            _latestCoachResult = null;
            _latestFeedbackText = string.Empty;
            _coachFeedbackSentToAnna = false;
            ClearCoachFeedbackBoard();
        }

        private bool HasPreparedCoachFeedback()
        {
            return _latestCoachRequest != null &&
                   _latestCoachResult != null &&
                   IsUsableCoachFeedback(_latestCoachResult) &&
                   !string.IsNullOrWhiteSpace(_latestFeedbackText);
        }

        private static bool IsUsableCoachFeedback(CoachFeedbackResult result)
        {
            return result != null &&
                   result.Source == CoachFeedbackSource.OpenAI &&
                   !string.IsNullOrWhiteSpace(result.FeedbackText);
        }

        private void StartCoachFeedbackSpeech(string feedback)
        {
            if (coachNPC == null || string.IsNullOrWhiteSpace(feedback))
            {
                _coachFeedbackSentToAnna = false;
                SetStatus("Anna is not assigned, so the prepared feedback cannot be spoken.");
                SetButtonsForPhase();
                return;
            }

            StopCoachFeedbackSpeech(false);
            SetCoachFeedbackBoardTextVisible(false);
            _coachFeedbackSpeechActive = true;
            _coachAudioObservedThisAttempt = false;
            _coachSpeechRetryCount = 0;
            _pendingCoachSpeechText = feedback.Trim();
            _coachSpeechTranscript.Clear();
            HideWorldCaption(_opponentHeadCaptionRoot, ref _opponentCaptionHideRoutine);
            HideWorldCaption(_coachHeadCaptionRoot, ref _coachCaptionHideRoutine);
            ConvaiNPCManager.Instance?.SetActiveConvaiNPC(coachNPC);
            ApplyCoachVoiceSettings();
            SendCoachSpeechRequest(_pendingCoachSpeechText);

            _coachSpeechGuardRoutine = StartCoroutine(GuardCoachFeedbackSpeech(_pendingCoachSpeechText));
        }

        private void SendCoachSpeechRequest(string feedback)
        {
            string speechPrompt = BuildCoachSpeechPrompt(feedback);
            UpdateCoachDebugConvaiPrompt(speechPrompt);
            AppendCoachDebugLine("Convai delivery: request dispatched to Anna; waiting for English audio.");
            coachNPC.SendTextDataAsync(speechPrompt);
        }

        private void HandleConvaiTextSendFailed(ConvaiNPC sendingNpc, string failure)
        {
            if (sendingNpc != coachNPC)
            {
                return;
            }

            RunOnMainThread(() => HandleCoachTextSendFailedOnMainThread(failure));
        }

        private void HandleCoachTextSendFailedOnMainThread(string failure)
        {
            if (!_coachFeedbackSpeechActive)
            {
                return;
            }

            AppendCoachDebugLine("Convai delivery failure: " + (failure ?? "unknown connection error"));
            if (_coachSpeechRetryCount < 1)
            {
                _coachSpeechRetryCount++;
                SetStatus("Convai connection failed. Retrying Anna automatically...");
                if (_coachSpeechRetryRoutine != null)
                {
                    StopCoroutine(_coachSpeechRetryRoutine);
                }

                _coachSpeechRetryRoutine = StartCoroutine(RetryCoachSpeechAfterDelay(2f));
                return;
            }

            if (_coachSpeechGuardRoutine != null)
            {
                StopCoroutine(_coachSpeechGuardRoutine);
                _coachSpeechGuardRoutine = null;
            }

            _coachFeedbackSpeechActive = false;
            _coachFeedbackSentToAnna = false;
            SetStatus("Convai is unavailable. Press Ask Anna to retry the prepared Coach feedback.");
            ShowCoachLineInLeftUi("Coach feedback is ready, but Convai could not connect. Press Ask Anna to retry.");
            SetButtonsForPhase();
        }

        private IEnumerator RetryCoachSpeechAfterDelay(float delaySeconds)
        {
            yield return new WaitForSecondsRealtime(delaySeconds);
            _coachSpeechRetryRoutine = null;
            if (!_coachFeedbackSpeechActive || coachNPC == null || string.IsNullOrWhiteSpace(_pendingCoachSpeechText))
            {
                yield break;
            }

            ConvaiNPCManager.Instance?.SetActiveConvaiNPC(coachNPC);
            AppendCoachDebugLine("Convai delivery: automatic retry dispatched to Anna.");
            SendCoachSpeechRequest(_pendingCoachSpeechText);
        }

        private void ApplyCoachVoiceSettings()
        {
            if (coachNPC != null && coachNPC.TryGetComponent(out AudioSource coachAudioSource))
            {
                coachAudioSource.pitch = Mathf.Clamp(coachVoicePitch, 0.75f, 1.05f);
            }
        }

        private string BuildCoachSpeechPrompt(string feedback)
        {
            string safeFeedback = NormalizeCoachSpeechText(feedback);
            string style = string.IsNullOrWhiteSpace(coachSpeechStyleInstruction)
                ? "Use a warm, gentle coaching voice."
                : coachSpeechStyleInstruction.Trim();

            if (_latestCoachResult?.FeedbackLevel == CoachFeedbackLevel.Level3)
            {
                return style + " " +
                       "Speak only in English. Read the model sentence below verbatim exactly once. " +
                       "This is the learner's example sentence, not advice. Do not introduce it, explain it, evaluate it, paraphrase it, or add any words. " +
                       "Model sentence: " + safeFeedback;
            }

            return style + " " +
                   "Speak only in English. Do not translate the feedback into Chinese or any other language. " +
                   "Please say this prepared Coach feedback aloud to the learner exactly once. " +
                   "Do not add new advice. " +
                   "Feedback text: " + safeFeedback;
        }

        private static string NormalizeCoachSpeechText(string feedback)
        {
            if (string.IsNullOrWhiteSpace(feedback))
            {
                return string.Empty;
            }

            string withoutBrackets = feedback.Replace("[", string.Empty).Replace("]", string.Empty);
            return string.Join(
                " ",
                withoutBrackets.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries));
        }

        private void ShowCoachLineInLeftUi(string text)
        {
            string safeText = text?.Trim() ?? string.Empty;
            if (_coachText != null)
            {
                _coachText.text = string.IsNullOrWhiteSpace(safeText)
                    ? string.Empty
                    : "Coach: " + safeText;
            }
        }

        private IEnumerator GuardCoachFeedbackSpeech(string feedback)
        {
            float startTime = Time.realtimeSinceStartup;
            float timeoutSeconds = Mathf.Max(8f, coachResponseTimeoutSeconds + 8f);
            bool sawAudioOrTalking = false;
            bool launchedFallback = false;

            while (coachNPC != null && Time.realtimeSinceStartup - startTime < timeoutSeconds)
            {
                int queuedAudio = coachNPC.GetAudioResponseCount();
                bool isTalking = coachNPC.IsCharacterTalking;
                sawAudioOrTalking |= queuedAudio > 0 || isTalking;

                if (sawAudioOrTalking && !_coachAudioObservedThisAttempt)
                {
                    _coachAudioObservedThisAttempt = true;
                    SetCoachFeedbackBoardTextVisible(true);
                    AppendCoachDebugLine("Convai delivery: Anna audio or talking state was observed.");
                }

                if (!sawAudioOrTalking &&
                    !launchedFallback &&
                    useWindowsTtsFallbackWhenConvaiSilent &&
                    Time.realtimeSinceStartup - startTime >= Mathf.Max(1f, convaiSpeechFallbackDelaySeconds))
                {
                    launchedFallback = true;
                    SpeakWithWindowsTts(feedback);
                }

                if (sawAudioOrTalking && !isTalking && queuedAudio == 0 && Time.realtimeSinceStartup - startTime > 0.5f)
                {
                    break;
                }

                yield return new WaitForSecondsRealtime(0.1f);
            }

            if (!sawAudioOrTalking)
            {
                UnityEngine.Debug.LogWarning("Coach feedback text was generated, but no Anna/Convai audio was observed before timeout.");
                AppendCoachDebugLine(
                    "Convai delivery failure: no Anna audio was observed before timeout. " +
                    "Likely causes are a Convai/network delay or cancellation during an active-NPC change. Press Ask Anna to retry.");
                _coachFeedbackSentToAnna = false;
                SetStatus("Anna did not produce audio. Press Ask Anna to try again.");
            }
            else
            {
                ScheduleWorldCaptionHide(_coachHeadCaptionRoot, ref _coachCaptionHideRoutine, 4f);
            }

            _coachFeedbackSpeechActive = false;
            _coachSpeechGuardRoutine = null;

            if (Phase == OrchestrationPhase.CoachSuggestionReady ||
                Phase == OrchestrationPhase.CoachGenerating)
            {
                ConvaiNPCManager.Instance?.SetActiveConvaiNPC(conversationNPC);
            }

            SetButtonsForPhase();
        }

        private void SpeakWithWindowsTts(string text)
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            try
            {
                string escapedText = text.Replace("'", "''");
                string command =
                    "Add-Type -AssemblyName System.Speech; " +
                    "$s = New-Object System.Speech.Synthesis.SpeechSynthesizer; " +
                    "$s.Rate = 0; $s.Volume = 100; " +
                    "$s.Speak('" + escapedText + "');";
                string encodedCommand = Convert.ToBase64String(Encoding.Unicode.GetBytes(command));
                ProcessStartInfo startInfo = new()
                {
                    FileName = "powershell.exe",
                    Arguments = "-NoProfile -NonInteractive -WindowStyle Hidden -EncodedCommand " + encodedCommand,
                    CreateNoWindow = true,
                    UseShellExecute = false
                };
                Process.Start(startInfo);
                SetCoachFeedbackBoardTextVisible(true);
                UnityEngine.Debug.Log("Started Windows TTS fallback for Coach feedback.");
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning("Windows TTS fallback failed: " + ex.Message);
            }
#else
            _ = text;
#endif
        }

        private void StopCoachFeedbackSpeech(bool restoreConversationNpc)
        {
            bool shouldInterruptCoach = _coachFeedbackSpeechActive ||
                                        (coachNPC != null &&
                                         (coachNPC.IsCharacterTalking || coachNPC.GetAudioResponseCount() > 0));
            if (_coachSpeechGuardRoutine != null)
            {
                StopCoroutine(_coachSpeechGuardRoutine);
                _coachSpeechGuardRoutine = null;
            }

            if (_coachSpeechRetryRoutine != null)
            {
                StopCoroutine(_coachSpeechRetryRoutine);
                _coachSpeechRetryRoutine = null;
            }

            _coachFeedbackSpeechActive = false;
            _pendingCoachSpeechText = string.Empty;

            if (shouldInterruptCoach && coachNPC != null)
            {
                coachNPC.InterruptCharacterSpeech();
            }

            if (restoreConversationNpc)
            {
                ConvaiNPCManager manager = ConvaiNPCManager.Instance;
                if (manager != null && conversationNPC != null && manager.activeConvaiNPC != conversationNPC)
                {
                    manager.SetActiveConvaiNPC(conversationNPC);
                }
            }
        }

        private CoachFeedbackRequest BuildCoachRequest(CoachFeedbackLevel level)
        {
            return new CoachFeedbackRequest
            {
                Stage = "Practice Debate - " + CurrentCreeiStage,
                TopicId = topicId,
                Topic = debateTopic,
                PlayerSide = learnerStance,
                CurrentCreeiStage = CurrentCreeiStage.ToString(),
                PreviousLearnerCreeiStages = BuildCreeiContext(
                    _learnerCreeiResponses,
                    level == CoachFeedbackLevel.Summary ? CreeiStages.Length : _creeiStageIndex),
                TurnId = _turnIndex,
                OpponentUtteranceText = SessionRecord.latestOpponentUtterance,
                PlayerUtteranceText = _latestPlayerUtterance,
                PreviousCoachFeedbackText = _latestFeedbackText,
                SelectedStrategy = selectedStrategy,
                PreviousCommands = previousCommands ?? Array.Empty<string>(),
                PreviousNpcVersionsViewed = previousNpcVersionsViewed ?? Array.Empty<string>(),
                FeedbackLevel = level,
                DetailedJson = _detailToggle != null && _detailToggle.isOn
            };
        }

        private static string BuildCreeiContext(string[] values, int countExclusive)
        {
            if (values == null || values.Length == 0 || countExclusive <= 0)
            {
                return "(none)";
            }

            StringBuilder builder = new();
            int count = Mathf.Min(countExclusive, Mathf.Min(values.Length, CreeiStages.Length));
            for (int i = 0; i < count; i++)
            {
                if (string.IsNullOrWhiteSpace(values[i]))
                {
                    continue;
                }

                if (builder.Length > 0)
                {
                    builder.Append('\n');
                }

                builder.Append(CreeiStages[i]).Append(": ").Append(values[i].Trim());
            }

            return builder.Length == 0 ? "(none)" : builder.ToString();
        }

        private CoachFeedbackLogRow BuildLogRow(
            CoachFeedbackRequest request,
            CoachFeedbackResult result,
            bool exampleRequested)
        {
            return new CoachFeedbackLogRow
            {
                ParticipantId = participantId,
                Condition = condition,
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
                DetailedJson = request.DetailedJson,
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
            ShowWorldCaption(
                _opponentHeadCaptionRoot,
                _opponentHeadCaptionText,
                _opponentHeadCaptionScrollRect,
                _opponentTranscript.ToString());
        }

        private void HandleOpponentTalkingChanged(bool isTalking)
        {
            if (Phase == OrchestrationPhase.MockDebateOpponentSpeaking)
            {
                if (isTalking)
                {
                    _mockDebateLeoHasStartedTalking = true;
                    return;
                }

                if (_mockDebateLeoTimerRunning && _mockDebateLeoHasStartedTalking)
                {
                    _mockDebateLeoHasStartedTalking = false;
                    _mockDebateLeoDurationSeconds = Mathf.Min(
                        mockDebateTargetSeconds,
                        Time.realtimeSinceStartup - _mockDebateLeoStartedAt);
                    UpdateMockDebateStageUi();
                    if (_mockDebateLeoSegmentIndex < _mockDebateLeoSpeechSegments.Length)
                    {
                        SetStatus("Leo is continuing to the next CREEI part...");
                        SendNextMockDebateLeoSegment();
                    }
                    else
                    {
                        _mockDebateLeoTimerRunning = false;
                        StartMockDebatePlayerTurn("Leo finished his CREEI argument. Your turn is ready.");
                    }
                }

                return;
            }

            if (isTalking || Phase != OrchestrationPhase.OpponentSpeaking)
            {
                return;
            }

            SessionRecord.latestOpponentUtterance = _opponentTranscript.ToString().Trim();
            ScheduleWorldCaptionHide(_opponentHeadCaptionRoot, ref _opponentCaptionHideRoutine, 4f);
            Phase = OrchestrationPhase.WaitingForPlayer;
            SetControlCursor(false);
            SetStatus($"{CurrentCreeiStage}: press T once to start recording and press T again to stop. Coach will evaluate this stage only.");
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

        private void SubscribeToCoachAudio()
        {
            if (coachNPC != null && coachNPC.AudioManager != null)
            {
                coachNPC.AudioManager.OnResponseAudioStarted -= CaptureCoachAudio;
                coachNPC.AudioManager.OnResponseAudioStarted += CaptureCoachAudio;
            }

            if (ConvaiGRPCAPI.Instance != null)
            {
                ConvaiGRPCAPI.Instance.OnTextSendFailed -= HandleConvaiTextSendFailed;
                ConvaiGRPCAPI.Instance.OnTextSendFailed += HandleConvaiTextSendFailed;
            }
        }

        private void UnsubscribeFromCoachAudio()
        {
            if (coachNPC != null && coachNPC.AudioManager != null)
            {
                coachNPC.AudioManager.OnResponseAudioStarted -= CaptureCoachAudio;
            }

            if (ConvaiGRPCAPI.Instance != null)
            {
                ConvaiGRPCAPI.Instance.OnTextSendFailed -= HandleConvaiTextSendFailed;
            }
        }

        private void CaptureCoachAudio(ConvaiNPCAudioManager.ResponseAudio response)
        {
            if (response == null || response.IsFinal || string.IsNullOrWhiteSpace(response.AudioTranscript))
            {
                return;
            }

            string spokenChunk = response.AudioTranscript.Trim();
            if (!_coachAudioObservedThisAttempt)
            {
                _coachAudioObservedThisAttempt = true;
                SetCoachFeedbackBoardTextVisible(true);
                AppendCoachDebugLine("Convai delivery: Anna audio response started.");
            }

            if (_coachSpeechTranscript.Length > 0)
            {
                _coachSpeechTranscript.Append(' ');
            }

            _coachSpeechTranscript.Append(spokenChunk);
            ShowWorldCaption(
                _coachHeadCaptionRoot,
                _coachHeadCaptionText,
                _coachHeadCaptionScrollRect,
                _coachSpeechTranscript.ToString());
        }

        private void SubscribeToPlayerVoiceTranscript()
        {
            if (ConvaiGRPCAPI.Instance != null)
            {
                ConvaiGRPCAPI.Instance.OnResultReceived -= HandleConvaiResultReceived;
                ConvaiGRPCAPI.Instance.OnResultReceived += HandleConvaiResultReceived;
                ConvaiGRPCAPI.Instance.OnPlayerSpeakingChanged -= HandlePlayerSpeakingChanged;
                ConvaiGRPCAPI.Instance.OnPlayerSpeakingChanged += HandlePlayerSpeakingChanged;
            }
        }

        private void UnsubscribeFromPlayerVoiceTranscript()
        {
            if (ConvaiGRPCAPI.Instance != null)
            {
                ConvaiGRPCAPI.Instance.OnResultReceived -= HandleConvaiResultReceived;
                ConvaiGRPCAPI.Instance.OnPlayerSpeakingChanged -= HandlePlayerSpeakingChanged;
            }
        }

        private void RegisterVoiceInterceptor()
        {
            if (!Application.isPlaying || !automaticCoachAfterPlayerVoice)
            {
                return;
            }

            ConvaiGRPCAPI.TryHandleUserVoiceTranscript = TryHandlePlayerVoiceTranscript;
            ConvaiGRPCAPI.ShouldSuppressVoiceResponse = ShouldSuppressMockDebateNpcResponse;
        }

        private void RegisterConvaiInputSuppressors()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            ConvaiInputManager.ShouldSuppressTalkInput = ShouldSuppressCoachTalkInput;
            ConvaiInputManager.ShouldUseTapToTalk = ShouldUseOrchestrationTapToTalk;
            ConvaiInputManager.TapToTalkRequested = HandleOrchestrationTapToTalk;
            ConvaiPlayerInteractionManager.ShouldSuppressTalkInput = ShouldSuppressCoachTalkInput;
            ConvaiPlayerInteractionManager.ShouldSuppressNpcInteraction = ShouldSuppressCoachTalkInput;
            ConvaiNPCManager.ShouldSuppressAutoActiveNPCUpdate = ShouldSuppressAutoActiveNpcUpdate;
        }

        private void UnregisterVoiceInterceptor()
        {
            if (ConvaiGRPCAPI.TryHandleUserVoiceTranscript == (Func<string, bool>)TryHandlePlayerVoiceTranscript)
            {
                ConvaiGRPCAPI.TryHandleUserVoiceTranscript = null;
            }

            if (ConvaiGRPCAPI.ShouldSuppressVoiceResponse == (Func<bool>)ShouldSuppressMockDebateNpcResponse)
            {
                ConvaiGRPCAPI.ShouldSuppressVoiceResponse = null;
            }
        }

        private bool ShouldSuppressMockDebateNpcResponse()
        {
            return _mockDebateRecording ||
                   Phase == OrchestrationPhase.MockDebateSpeaking ||
                   Phase == OrchestrationPhase.MockDebateAwaitingTranscript ||
                   Phase == OrchestrationPhase.MockDebateEvaluating;
        }

        private void UnregisterConvaiInputSuppressors()
        {
            if (ConvaiInputManager.ShouldSuppressTalkInput == (Func<bool>)ShouldSuppressCoachTalkInput)
            {
                ConvaiInputManager.ShouldSuppressTalkInput = null;
            }

            if (ConvaiInputManager.ShouldUseTapToTalk == (Func<bool>)ShouldUseOrchestrationTapToTalk)
            {
                ConvaiInputManager.ShouldUseTapToTalk = null;
            }

            if (ConvaiInputManager.TapToTalkRequested == (Action)HandleOrchestrationTapToTalk)
            {
                ConvaiInputManager.TapToTalkRequested = null;
            }

            if (ConvaiPlayerInteractionManager.ShouldSuppressTalkInput == (Func<bool>)ShouldSuppressCoachTalkInput)
            {
                ConvaiPlayerInteractionManager.ShouldSuppressTalkInput = null;
            }

            if (ConvaiPlayerInteractionManager.ShouldSuppressNpcInteraction == (Func<bool>)ShouldSuppressCoachTalkInput)
            {
                ConvaiPlayerInteractionManager.ShouldSuppressNpcInteraction = null;
            }

            if (ConvaiNPCManager.ShouldSuppressAutoActiveNPCUpdate == (Func<bool>)ShouldSuppressAutoActiveNpcUpdate)
            {
                ConvaiNPCManager.ShouldSuppressAutoActiveNPCUpdate = null;
            }
        }

        private bool ShouldSuppressCoachTalkInput()
        {
            bool practiceCycleBlocked = IsPracticeCyclePhase() &&
                                        Phase is not OrchestrationPhase.PracticeCycleReady and
                                            not OrchestrationPhase.PracticeCycleSpeaking;
            bool shouldSuppress = Phase == OrchestrationPhase.Intro ||
                                  Phase == OrchestrationPhase.OpponentGenerating ||
                                  Phase == OrchestrationPhase.MockDebateOpponentSpeaking ||
                                  Phase == OrchestrationPhase.MockDebateAwaitingTranscript ||
                                  Phase == OrchestrationPhase.MockDebateEvaluating ||
                                  Phase == OrchestrationPhase.Complete ||
                                  practiceCycleBlocked ||
                                  _coachFeedbackSpeechActive;
            if (!shouldSuppress)
            {
                PrepareForPlayerVoiceInput();
            }
            else
            {
                ShowTalkInputBlockedStatus();
            }

            return shouldSuppress;
        }

        private bool ShouldUseOrchestrationTapToTalk()
        {
            return IsPracticeCyclePhase() ||
                   ((IsPracticeStageVoicePhase() ||
                    Phase == OrchestrationPhase.OpponentSpeaking ||
                    _practiceVoiceRecording ||
                    _practiceVoiceAwaitingTranscript ||
                    Phase == OrchestrationPhase.MockDebateReady ||
                    Phase == OrchestrationPhase.MockDebateSpeaking ||
                    (_mockDebateFeedbackReady && Phase == OrchestrationPhase.CoachSuggestionReady)) &&
                   !_coachFeedbackSpeechActive);
        }

        private void ShowTalkInputBlockedStatus()
        {
            if (_coachFeedbackSpeechActive)
            {
                SetStatus("Anna is still speaking. Hold T after she finishes.");
                return;
            }

            SetStatus(Phase switch
            {
                OrchestrationPhase.Intro => "Press Start Conversation before using T.",
                OrchestrationPhase.OpponentGenerating => $"Wait for GPT to prepare Leo's {CurrentCreeiStage} statement, then use T.",
                OrchestrationPhase.MockDebateOpponentSpeaking => "Wait for Leo to finish his complete CREEI argument before using T.",
                OrchestrationPhase.MockDebateAwaitingTranscript => "Recording has ended. Wait for the final transcript segment and Coach analysis.",
                OrchestrationPhase.MockDebateEvaluating => "Wait for Coach to finish evaluating your Mock Debate before using T again.",
                OrchestrationPhase.PracticeCycleAwaitingTranscript => "Recording has stopped. Wait for the final transcript.",
                OrchestrationPhase.PracticeCycleConfirmingTranscript => "Confirm or re-record the transcript before using T again.",
                OrchestrationPhase.PracticeCycleDiagnosing => "Diagnosis is in progress. Voice input is disabled.",
                OrchestrationPhase.PracticeCycleCoach => "Coach interaction is active. Voice input is disabled for this episode.",
                OrchestrationPhase.Complete => "This CREEI session is complete. Start a new session before using T.",
                _ => "Voice input is temporarily unavailable. Please try T again in a moment."
            });
        }

        private void PrepareForPlayerVoiceInput()
        {
            DeactivateInputField(_learnerNoteInput);
            DeactivateInputField(_debugPromptInput);
            DeactivateInputField(_debugResultInput);

            if (EventSystem.current != null)
            {
                EventSystem.current.SetSelectedGameObject(null);
            }

            ConvaiNPCManager manager = ConvaiNPCManager.Instance;
            if (conversationNPC != null && manager != null && manager.activeConvaiNPC != conversationNPC)
            {
                manager.SetActiveConvaiNPC(conversationNPC);
            }
        }

        private static void DeactivateInputField(TMP_InputField inputField)
        {
            if (inputField != null && inputField.isFocused)
            {
                inputField.DeactivateInputField();
            }
        }

        private bool ShouldSuppressAutoActiveNpcUpdate()
        {
            return _coachFeedbackSpeechActive;
        }

        private void EnterIntro()
        {
            Phase = OrchestrationPhase.Intro;
            SetControlCursor(true);
            if (_stageText != null)
            {
                _stageText.text = "CREEI Stage: Not started";
            }
            SetMockDebateTimerVisible(false);
            SetStatus(_studyStarted
                ? "Starting common CREEI prerequisite practice..."
                : "Researcher setup is required before the session starts.");
            _playerText.text = "Your response transcript will appear here after you speak.";
            ShowCoachLineInLeftUi(_studyStarted
                ? "Coach is disabled during the five common prerequisite stages."
                : "Coach mode will be locked after researcher setup.");
            if (!_studyStarted)
            {
                FreezePlayerMovement();
                if (_root != null) _root.SetActive(false);
                experimentView?.ShowSetup();
            }
            UpdateStageSelectionVisuals();
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
            rootRect.sizeDelta = new Vector2(540f, 520f);

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
            _stageText = CreateText(_root.transform, "CREEI Stage: Not started", 18, FontStyles.Bold, 34f);
            _stageText.color = new Color(1f, 0.82f, 0.30f);
            BuildStageSelector(_root.transform);
            BuildMockDebateTimer();
            _taskText = CreateText(_root.transform, string.Empty, 15, FontStyles.Normal, 78f);
            _statusText = CreateText(_root.transform, string.Empty, 15, FontStyles.Bold, 54f);
            _playerText = CreateText(_root.transform, string.Empty, 15, FontStyles.Normal, 82f);
            _playerText.overflowMode = TextOverflowModes.Ellipsis;
            _coachText = CreateText(_root.transform, string.Empty, 16, FontStyles.Normal, 122f);
            HideTextFromLayout(_taskText);
            HideTextFromLayout(_statusText);
            HideTextFromLayout(_playerText);
            HideTextFromLayout(_coachText);
            _learnerNoteInput = CreateInput(_root.transform, "Optional typed fallback if voice transcription is unavailable.", 70f);

            _startButton = CreateButton(_root.transform, "Start Conversation", BeginConditionC, new Color(0.20f, 0.48f, 0.36f));
            _askCoachButton = CreateButton(_root.transform, "Ask Anna", AskAnnaForReadyFeedback, new Color(0.72f, 0.50f, 0.18f));
            _exampleButton = CreateButton(_root.transform, "Need an example?", RequestExample, new Color(0.42f, 0.36f, 0.64f));
            _continueButton = CreateButton(_root.transform, "Continue", ContinueConversation, new Color(0.24f, 0.43f, 0.70f));
            _endButton = CreateButton(_root.transform, "End Session", EndConditionC, new Color(0.62f, 0.22f, 0.20f));

            _taskText.text =
                $"Topic: {debateTopic}\n" +
                $"Your stance: {learnerStance}\n" +
                $"Selected strategy: {selectedStrategy}";

            BuildDebugUi();
            UpdateCoachDebugIdle();
        }

        private void BuildCoachFeedbackBoard()
        {
            _coachBoardRoot = new GameObject(
                "Coach Feedback World Canvas",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(GraphicRaycaster),
                typeof(Image));

            RectTransform rootRect = _coachBoardRoot.GetComponent<RectTransform>();
            rootRect.sizeDelta = new Vector2(1450f, 1150f);
            rootRect.localScale = Vector3.one * 0.00185f;
            rootRect.SetPositionAndRotation(
                new Vector3(0.18f, 1.52f, 3.7437f),
                Quaternion.identity);

            Canvas canvas = _coachBoardRoot.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.overrideSorting = true;
            canvas.sortingOrder = 100;
            canvas.worldCamera = Camera.main;

            CanvasScaler scaler = _coachBoardRoot.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            _coachBoardRoot.GetComponent<Image>().color = new Color(0.93f, 0.95f, 0.94f, 0.99f);

            VerticalLayoutGroup layout = _coachBoardRoot.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(46, 46, 38, 38);
            layout.spacing = 12f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            _coachBoardTitleText = CreateBoardText(
                _coachBoardRoot.transform,
                "COACH FEEDBACK",
                46,
                FontStyles.Bold,
                66f,
                new Color(0.05f, 0.12f, 0.16f));
            _coachBoardTitleText.alignment = TextAlignmentOptions.Left;

            _coachBoardStageText = CreateBoardText(
                _coachBoardRoot.transform,
                string.Empty,
                32,
                FontStyles.Bold,
                50f,
                new Color(0.12f, 0.30f, 0.48f));

            _coachBoardFeedbackText = CreateBoardScrollView(
                _coachBoardRoot.transform,
                out _coachBoardScrollRect);
            SetCoachFeedbackBoardTextVisible(false);
        }

        private static TMP_Text CreateBoardText(
            Transform parent,
            string text,
            int fontSize,
            FontStyles style,
            float height,
            Color color)
        {
            TMP_Text component = CreateText(parent, text, fontSize, style, height);
            component.color = color;
            return component;
        }

        private static TMP_Text CreateBoardScrollView(Transform parent, out ScrollRect scrollRect)
        {
            GameObject scrollObject = CreateRect("Coach Feedback Scroll View", parent);
            LayoutElement scrollLayout = scrollObject.AddComponent<LayoutElement>();
            scrollLayout.preferredHeight = 900f;
            scrollLayout.minHeight = 900f;
            Image scrollBackground = scrollObject.AddComponent<Image>();
            scrollBackground.color = new Color(0.86f, 0.89f, 0.88f, 0.72f);

            GameObject viewportObject = CreateRect("Viewport", scrollObject.transform);
            RectTransform viewportRect = viewportObject.GetComponent<RectTransform>();
            viewportRect.anchorMin = Vector2.zero;
            viewportRect.anchorMax = Vector2.one;
            viewportRect.offsetMin = new Vector2(28f, 24f);
            viewportRect.offsetMax = new Vector2(-54f, -24f);
            Image viewportImage = viewportObject.AddComponent<Image>();
            viewportImage.color = new Color(1f, 1f, 1f, 0.001f);
            viewportObject.AddComponent<RectMask2D>();

            GameObject contentObject = CreateRect("Content", viewportObject.transform);
            RectTransform contentRect = contentObject.GetComponent<RectTransform>();
            contentRect.anchorMin = new Vector2(0f, 1f);
            contentRect.anchorMax = Vector2.one;
            contentRect.pivot = new Vector2(0.5f, 1f);
            contentRect.anchoredPosition = Vector2.zero;
            contentRect.sizeDelta = Vector2.zero;

            TMP_Text text = contentObject.AddComponent<TextMeshProUGUI>();
            text.text = string.Empty;
            text.fontSize = 40f;
            text.color = new Color(0.08f, 0.10f, 0.12f);
            text.alignment = TextAlignmentOptions.TopLeft;
            text.textWrappingMode = TextWrappingModes.Normal;
            text.overflowMode = TextOverflowModes.Overflow;
            text.raycastTarget = false;

            ContentSizeFitter fitter = contentObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            GameObject scrollbarObject = CreateRect("Scrollbar", scrollObject.transform);
            RectTransform scrollbarRect = scrollbarObject.GetComponent<RectTransform>();
            scrollbarRect.anchorMin = new Vector2(1f, 0f);
            scrollbarRect.anchorMax = Vector2.one;
            scrollbarRect.pivot = new Vector2(1f, 0.5f);
            scrollbarRect.offsetMin = new Vector2(-28f, 24f);
            scrollbarRect.offsetMax = new Vector2(-12f, -24f);
            Image scrollbarBackground = scrollbarObject.AddComponent<Image>();
            scrollbarBackground.color = new Color(0.10f, 0.16f, 0.18f, 0.16f);

            GameObject handleObject = CreateRect("Handle", scrollbarObject.transform);
            RectTransform handleRect = handleObject.GetComponent<RectTransform>();
            handleRect.anchorMin = Vector2.zero;
            handleRect.anchorMax = Vector2.one;
            handleRect.offsetMin = Vector2.zero;
            handleRect.offsetMax = Vector2.zero;
            Image handleImage = handleObject.AddComponent<Image>();
            handleImage.color = new Color(0.12f, 0.30f, 0.48f, 0.88f);

            Scrollbar scrollbar = scrollbarObject.AddComponent<Scrollbar>();
            scrollbar.handleRect = handleRect;
            scrollbar.targetGraphic = handleImage;
            scrollbar.direction = Scrollbar.Direction.BottomToTop;

            scrollRect = scrollObject.AddComponent<ScrollRect>();
            scrollRect.viewport = viewportRect;
            scrollRect.content = contentRect;
            scrollRect.horizontal = false;
            scrollRect.vertical = true;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;
            scrollRect.verticalScrollbar = scrollbar;
            scrollRect.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
            scrollRect.scrollSensitivity = 34f;
            return text;
        }

        private void ShowCoachFeedbackOnBoard(string heading, string feedback)
        {
            if (_coachBoardStageText != null)
            {
                _coachBoardStageText.text = heading?.Trim() ?? string.Empty;
            }

            if (_coachBoardFeedbackText == null)
            {
                return;
            }

            _coachBoardFeedbackText.text = feedback?.Trim() ?? string.Empty;
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(_coachBoardFeedbackText.rectTransform);
            if (_coachBoardScrollRect != null)
            {
                _coachBoardScrollRect.StopMovement();
                _coachBoardScrollRect.verticalNormalizedPosition = 1f;
            }
        }

        private void ClearCoachFeedbackBoard()
        {
            ShowCoachFeedbackOnBoard(string.Empty, string.Empty);
            SetCoachFeedbackBoardTextVisible(false);
        }

        private void SetCoachFeedbackBoardTextVisible(bool visible)
        {
            if (_coachBoardTitleText != null)
            {
                _coachBoardTitleText.enabled = visible;
            }

            if (_coachBoardStageText != null)
            {
                _coachBoardStageText.enabled = visible;
            }

            if (_coachBoardFeedbackText != null)
            {
                _coachBoardFeedbackText.enabled = visible;
            }

            if (visible && _coachBoardFeedbackText != null)
            {
                Canvas.ForceUpdateCanvases();
                LayoutRebuilder.ForceRebuildLayoutImmediate(_coachBoardFeedbackText.rectTransform);
                if (_coachBoardScrollRect != null)
                {
                    _coachBoardScrollRect.StopMovement();
                    _coachBoardScrollRect.verticalNormalizedPosition = 1f;
                }
            }
        }

        private static void HideTextFromLayout(TMP_Text text)
        {
            if (text == null)
            {
                return;
            }

            text.text = string.Empty;
            text.gameObject.SetActive(false);
        }

        private void BuildStageSelector(Transform parent)
        {
            _stageSelectorRoot = CreateRect("Stage Selection", parent);
            LayoutElement layoutElement = _stageSelectorRoot.AddComponent<LayoutElement>();
            layoutElement.preferredHeight = 76f;
            layoutElement.minHeight = 76f;

            GridLayoutGroup grid = _stageSelectorRoot.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(162f, 34f);
            grid.spacing = new Vector2(8f, 8f);
            grid.startCorner = GridLayoutGroup.Corner.UpperLeft;
            grid.startAxis = GridLayoutGroup.Axis.Horizontal;
            grid.childAlignment = TextAnchor.UpperLeft;
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 3;

            string[] labels = { "Claim", "Reason", "Evidence", "Explanation", "Impact", "Mock Debate" };
            for (int i = 0; i < labels.Length; i++)
            {
                int selectedIndex = i;
                Button button = CreateButton(
                    _stageSelectorRoot.transform,
                    labels[i],
                    () => SelectStage(selectedIndex),
                    new Color(0.16f, 0.22f, 0.28f, 0.96f));
                LayoutElement buttonLayout = button.GetComponent<LayoutElement>();
                if (buttonLayout != null)
                {
                    buttonLayout.preferredHeight = 34f;
                    buttonLayout.minHeight = 34f;
                }

                TMP_Text label = button.GetComponentInChildren<TMP_Text>();
                if (label != null)
                {
                    label.fontSize = 14f;
                }

                _stageSelectionButtons[i] = button;
            }

            UpdateStageSelectionVisuals();
        }

        private void BuildWorldSpeechCaptions()
        {
            _opponentHeadCaptionText = CreateWorldSpeechCaption(
                conversationNPC,
                "Mike Head Caption",
                new Color(0.04f, 0.07f, 0.10f, 0.88f),
                out _opponentHeadCaptionRoot,
                out _opponentHeadCaptionScrollRect);
            _coachHeadCaptionText = CreateWorldSpeechCaption(
                coachNPC,
                "Anna Head Caption",
                new Color(0.13f, 0.08f, 0.04f, 0.88f),
                out _coachHeadCaptionRoot,
                out _coachHeadCaptionScrollRect);
        }

        private void DisableLegacyNpcSpeechBubbles()
        {
            int expectedBubbleCount = 0;
            int disabledBubbleCount = 0;

            DisableLegacyNpcSpeechBubbles(conversationNPC, ref expectedBubbleCount, ref disabledBubbleCount);
            DisableLegacyNpcSpeechBubbles(coachNPC, ref expectedBubbleCount, ref disabledBubbleCount);
            _legacySpeechBubblesDisabled = expectedBubbleCount > 0 && disabledBubbleCount >= expectedBubbleCount;
        }

        private static void DisableLegacyNpcSpeechBubbles(
            ConvaiNPC npc,
            ref int expectedBubbleCount,
            ref int disabledBubbleCount)
        {
            if (npc == null)
            {
                return;
            }

            expectedBubbleCount++;
            NPCSpeechBubble[] speechBubbles = npc.GetComponentsInChildren<NPCSpeechBubble>(true);
            if (speechBubbles.Length == 0)
            {
                return;
            }

            foreach (NPCSpeechBubble speechBubble in speechBubbles)
            {
                speechBubble.HideSpeechBubble();
                speechBubble.gameObject.SetActive(false);
            }

            disabledBubbleCount++;
        }

        private static TMP_Text CreateWorldSpeechCaption(
            ConvaiNPC npc,
            string objectName,
            Color backgroundColor,
            out GameObject captionRoot,
            out ScrollRect scrollRect)
        {
            captionRoot = null;
            scrollRect = null;
            if (npc == null)
            {
                return null;
            }

            captionRoot = new GameObject(
                objectName,
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(GraphicRaycaster),
                typeof(ScrollRect));
            RectTransform rootRect = captionRoot.GetComponent<RectTransform>();
            Vector3 followLocalPosition = GetCaptionLocalPosition(npc);
            rootRect.position = npc.transform.TransformPoint(followLocalPosition);
            rootRect.rotation = Quaternion.identity;
            rootRect.localScale = Vector3.one * 0.0032f;
            rootRect.sizeDelta = new Vector2(460f, 132f);

            Canvas canvas = captionRoot.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.overrideSorting = true;
            canvas.sortingOrder = 220;
            captionRoot.GetComponent<Image>().color = backgroundColor;
            captionRoot.AddComponent<RefereeLabelBillboard>().Follow(npc.transform, followLocalPosition);

            GameObject viewportObject = CreateRect("Viewport", captionRoot.transform);
            RectTransform viewportRect = viewportObject.GetComponent<RectTransform>();
            viewportRect.anchorMin = Vector2.zero;
            viewportRect.anchorMax = Vector2.one;
            viewportRect.offsetMin = new Vector2(14f, 10f);
            viewportRect.offsetMax = new Vector2(-30f, -10f);
            Image viewportImage = viewportObject.AddComponent<Image>();
            viewportImage.color = new Color(1f, 1f, 1f, 0.001f);
            viewportObject.AddComponent<RectMask2D>();

            GameObject textObject = CreateRect("Content", viewportObject.transform);
            RectTransform textRect = textObject.GetComponent<RectTransform>();
            textRect.anchorMin = new Vector2(0f, 1f);
            textRect.anchorMax = Vector2.one;
            textRect.pivot = new Vector2(0.5f, 1f);
            textRect.anchoredPosition = Vector2.zero;
            textRect.sizeDelta = Vector2.zero;

            TMP_Text text = textObject.AddComponent<TextMeshProUGUI>();
            text.text = string.Empty;
            text.fontSize = 21f;
            text.enableAutoSizing = false;
            text.color = Color.white;
            text.alignment = TextAlignmentOptions.TopLeft;
            text.textWrappingMode = TextWrappingModes.Normal;
            text.raycastTarget = false;

            GameObject scrollbarObject = CreateRect("Scrollbar", captionRoot.transform);
            RectTransform scrollbarRect = scrollbarObject.GetComponent<RectTransform>();
            scrollbarRect.anchorMin = new Vector2(1f, 0f);
            scrollbarRect.anchorMax = Vector2.one;
            scrollbarRect.pivot = new Vector2(1f, 0.5f);
            scrollbarRect.offsetMin = new Vector2(-18f, 10f);
            scrollbarRect.offsetMax = new Vector2(-8f, -10f);
            Image scrollbarBackground = scrollbarObject.AddComponent<Image>();
            scrollbarBackground.color = new Color(1f, 1f, 1f, 0.16f);

            GameObject handleObject = CreateRect("Handle", scrollbarObject.transform);
            RectTransform handleRect = handleObject.GetComponent<RectTransform>();
            handleRect.anchorMin = Vector2.zero;
            handleRect.anchorMax = Vector2.one;
            handleRect.offsetMin = Vector2.zero;
            handleRect.offsetMax = Vector2.zero;
            Image handleImage = handleObject.AddComponent<Image>();
            handleImage.color = new Color(1f, 0.84f, 0.30f, 0.92f);

            Scrollbar scrollbar = scrollbarObject.AddComponent<Scrollbar>();
            scrollbar.handleRect = handleRect;
            scrollbar.targetGraphic = handleImage;
            scrollbar.direction = Scrollbar.Direction.BottomToTop;

            scrollRect = captionRoot.GetComponent<ScrollRect>();
            scrollRect.content = textRect;
            scrollRect.viewport = viewportRect;
            scrollRect.horizontal = false;
            scrollRect.vertical = true;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;
            scrollRect.inertia = true;
            scrollRect.scrollSensitivity = 22f;
            scrollRect.verticalScrollbar = scrollbar;
            scrollRect.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;

            captionRoot.SetActive(false);
            return text;
        }

        private static Vector3 GetCaptionLocalPosition(ConvaiNPC npc)
        {
            Renderer[] renderers = npc.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                return new Vector3(0f, 2.15f, 0f);
            }

            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
            {
                bounds.Encapsulate(renderers[i].bounds);
            }

            Vector3 worldTop = new(bounds.center.x, bounds.max.y + 0.24f, bounds.center.z);
            return npc.transform.InverseTransformPoint(worldTop);
        }

        private static void ShowWorldCaption(
            GameObject root,
            TMP_Text text,
            ScrollRect scrollRect,
            string value)
        {
            if (root == null || text == null || string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            root.SetActive(true);
            text.text = value.Trim();
            text.ForceMeshUpdate();

            RectTransform textRect = text.rectTransform;
            float viewportHeight = scrollRect != null && scrollRect.viewport != null
                ? scrollRect.viewport.rect.height
                : 0f;
            float preferredHeight = text.GetPreferredValues(
                text.text,
                Mathf.Max(1f, textRect.rect.width),
                Mathf.Infinity).y;
            textRect.SetSizeWithCurrentAnchors(
                RectTransform.Axis.Vertical,
                Mathf.Max(viewportHeight, preferredHeight + 4f));

            Canvas.ForceUpdateCanvases();
            if (scrollRect != null)
            {
                scrollRect.StopMovement();
                scrollRect.verticalNormalizedPosition = 0f;
            }
        }

        private void ScheduleWorldCaptionHide(GameObject root, ref Coroutine routine, float delaySeconds)
        {
            if (root == null)
            {
                return;
            }

            if (routine != null)
            {
                StopCoroutine(routine);
            }

            routine = StartCoroutine(HideWorldCaptionAfterDelay(root, delaySeconds));
        }

        private static IEnumerator HideWorldCaptionAfterDelay(GameObject root, float delaySeconds)
        {
            yield return new WaitForSecondsRealtime(Mathf.Max(0f, delaySeconds));
            if (root != null)
            {
                root.SetActive(false);
            }
        }

        private void HideWorldCaption(GameObject root, ref Coroutine routine)
        {
            if (routine != null)
            {
                StopCoroutine(routine);
                routine = null;
            }

            if (root != null)
            {
                root.SetActive(false);
            }
        }

        private void BuildDebugUi()
        {
            _debugRoot = CreateRect("Coach GPT Debug Panel", uiCanvas.transform);
            RectTransform rootRect = _debugRoot.GetComponent<RectTransform>();
            rootRect.anchorMin = new Vector2(1f, 1f);
            rootRect.anchorMax = new Vector2(1f, 1f);
            rootRect.pivot = new Vector2(1f, 1f);
            rootRect.anchoredPosition = new Vector2(-16f, -16f);

            Image background = _debugRoot.AddComponent<Image>();
            background.color = new Color(0.04f, 0.05f, 0.06f, 0.82f);

            VerticalLayoutGroup layout = _debugRoot.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(16, 16, 14, 14);
            layout.spacing = 8f;
            layout.childControlWidth = true;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            CreateText(_debugRoot.transform, "GPT Debug", 22, FontStyles.Bold, 30f);
            _detailToggle = CreateToggle(_debugRoot.transform, "Detail");
            _debugStatusText = CreateText(_debugRoot.transform, string.Empty, 13, FontStyles.Bold, 84f);
            CreateText(_debugRoot.transform, "Prompt prepared for GPT", 14, FontStyles.Bold, 22f);
            _debugPromptInput = CreateReadOnlyInput(_debugRoot.transform, 220f);
            CreateText(_debugRoot.transform, "GPT response / validation status", 14, FontStyles.Bold, 22f);
            _debugResultInput = CreateReadOnlyInput(_debugRoot.transform, 260f);
            ApplyDebugPanelLayout();
            _debugRoot.SetActive(false);
        }

        private void ApplyDebugPanelLayout()
        {
            if (_debugRoot == null || uiCanvas == null)
            {
                return;
            }

            RectTransform canvasRect = uiCanvas.transform as RectTransform;
            RectTransform rootRect = _debugRoot.GetComponent<RectTransform>();
            if (canvasRect == null || rootRect == null)
            {
                return;
            }

            float canvasWidth = Mathf.Max(1f, canvasRect.rect.width);
            float canvasHeight = Mathf.Max(1f, canvasRect.rect.height);
            float panelWidth = Mathf.Clamp(canvasWidth * 0.48f, 300f, 680f);
            float panelHeight = Mathf.Clamp(canvasHeight - 32f, 480f, 820f);
            rootRect.sizeDelta = new Vector2(panelWidth, panelHeight);

            const float fixedHeight = 268f;
            float textAreaHeight = Mathf.Max(220f, panelHeight - fixedHeight);
            SetPreferredHeight(_debugPromptInput, textAreaHeight * 0.45f);
            SetPreferredHeight(_debugResultInput, textAreaHeight * 0.55f);

            _debugLayoutScreenWidth = Screen.width;
            _debugLayoutScreenHeight = Screen.height;
        }

        private static void SetPreferredHeight(Component component, float height)
        {
            if (component == null)
            {
                return;
            }

            LayoutElement layout = component.GetComponent<LayoutElement>();
            if (layout != null)
            {
                layout.preferredHeight = height;
                layout.minHeight = height;
            }
        }

        private void UpdateCoachDebugIdle()
        {
            SetCoachDebugStatus("No Coach request yet.");
            SetDebugInputText(_debugPromptInput, "After you finish speaking, the exact Coach prompt sent to GPT will appear here.");
            SetDebugInputText(_debugResultInput, "The GPT response, validation status, and Anna / Convai prompt will appear here.");
        }

        private void UpdateCreeiStageUi()
        {
            SetMockDebateTimerVisible(false);
            if (_stageText == null)
            {
                return;
            }

            _stageText.text = $"CREEI Stage {_creeiStageIndex + 1}/{CreeiStages.Length}: {CurrentCreeiStage}";
            UpdateStageSelectionVisuals();
        }

        private void UpdateMockDebateStageUi()
        {
            if (_stageText != null)
            {
                _stageText.text = Phase == OrchestrationPhase.MockDebateOpponentSpeaking
                    ? "CREEI Stage 6/6: Mock Debate - Leo"
                    : "CREEI Stage 6/6: Mock Debate - You";
            }

            bool leoTurn = Phase == OrchestrationPhase.MockDebateOpponentSpeaking;
            float shownDuration = leoTurn ? _mockDebateLeoDurationSeconds : _mockDebateDurationSeconds;
            int elapsedSeconds = Mathf.Max(0, Mathf.FloorToInt(shownDuration));
            int targetSeconds = Mathf.Max(1, Mathf.RoundToInt(mockDebateTargetSeconds));
            string elapsed = $"{elapsedSeconds / 60:00}:{elapsedSeconds % 60:00}";
            string target = $"{targetSeconds / 60:00}:{targetSeconds % 60:00}";
            string met = shownDuration >= mockDebateTargetSeconds ? " | Duration met" : string.Empty;
            if (_mockDebateTimerText != null)
            {
                string speaker = leoTurn ? "Leo's Speech" : "Your Speech";
                _mockDebateTimerText.text = $"{speaker}  {elapsed} / {target}{met}";
                SetMockDebateTimerVisible(true);
            }

            UpdateStageSelectionVisuals();
        }

        private void UpdateStageSelectionVisuals()
        {
            int activeIndex = -1;
            if (IsMockDebatePhase())
            {
                activeIndex = CreeiStages.Length;
            }
            else if (Phase != OrchestrationPhase.Intro && Phase != OrchestrationPhase.Complete)
            {
                activeIndex = Mathf.Clamp(_creeiStageIndex, 0, CreeiStages.Length - 1);
            }

            for (int i = 0; i < _stageSelectionButtons.Length; i++)
            {
                Button button = _stageSelectionButtons[i];
                if (button == null)
                {
                    continue;
                }

                Image image = button.targetGraphic as Image;
                if (image != null)
                {
                    image.color = i == activeIndex
                        ? new Color(0.82f, 0.55f, 0.16f, 1f)
                        : new Color(0.16f, 0.22f, 0.28f, 0.96f);
                }
            }
        }

        private void BuildMockDebateTimer()
        {
            GameObject timerRoot = CreateRect("Mock Debate Timer", uiCanvas.transform);
            RectTransform timerRect = timerRoot.GetComponent<RectTransform>();
            timerRect.anchorMin = new Vector2(0.5f, 1f);
            timerRect.anchorMax = new Vector2(0.5f, 1f);
            timerRect.pivot = new Vector2(0.5f, 1f);
            timerRect.anchoredPosition = new Vector2(0f, -18f);
            timerRect.sizeDelta = new Vector2(440f, 58f);

            Image background = timerRoot.AddComponent<Image>();
            background.color = new Color(0.04f, 0.05f, 0.07f, 0.82f);
            background.raycastTarget = false;

            _mockDebateTimerText = CreateText(
                timerRoot.transform,
                "Mock Debate  00:00 / 03:00",
                28,
                FontStyles.Bold,
                58f);
            RectTransform textRect = _mockDebateTimerText.rectTransform;
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(12f, 0f);
            textRect.offsetMax = new Vector2(-12f, 0f);
            _mockDebateTimerText.alignment = TextAlignmentOptions.Center;
            _mockDebateTimerText.color = new Color(1f, 0.84f, 0.30f);
            _mockDebateTimerText.enableWordWrapping = false;
            timerRoot.SetActive(false);
        }

        private void SetMockDebateTimerVisible(bool visible)
        {
            if (_mockDebateTimerText != null && _mockDebateTimerText.transform.parent != null)
            {
                _mockDebateTimerText.transform.parent.gameObject.SetActive(visible);
            }
        }

        private void UpdateMockDebateDebugIdle()
        {
            SetCoachDebugStatus("Mock Debate ready.");
            SetDebugInputText(
                _debugPromptInput,
                "After the Mock Debate recording ends, the full CREEI evaluation prompt sent to GPT will appear here.");
            SetDebugInputText(
                _debugResultInput,
                "The complete CREEI summary, advice, and Anna / Convai delivery prompt will appear here.");
        }

        private void UpdateMockDebateDebugPrompt(MockDebateEvaluationRequest request)
        {
            SetCoachDebugStatus("Mock Debate GPT evaluation in progress...");
            SetDebugInputText(_debugPromptInput, MockDebateEvaluationGenerator.BuildPrompt(request));
            SetDebugInputText(_debugResultInput, "Waiting for Mock Debate GPT response and validation...");
        }

        private void UpdateMockDebateDebugResult(
            MockDebateEvaluationRequest request,
            MockDebateEvaluationResult result)
        {
            string raw = string.IsNullOrWhiteSpace(result?.RawJson)
                ? "(No raw GPT JSON was returned to the app.)"
                : result.RawJson.Trim();
            string feedback = string.IsNullOrWhiteSpace(result?.FeedbackText)
                ? "(empty / not speakable)"
                : result.FeedbackText.Trim();

            SetCoachDebugStatus(
                $"Mock Debate request sent: YES\n" +
                $"Valid full CREEI feedback returned: {(result?.IsValid == true ? "YES" : "NO")}");
            SetDebugInputText(
                _debugResultInput,
                "Validation: " + (result?.DebugInfo ?? "No Mock Debate result object was returned.") + "\n\n" +
                "Speech duration: " + request.DurationSeconds.ToString("0.0") + " / " + request.TargetDurationSeconds.ToString("0.0") + " seconds\n" +
                "Parsed feedback_text:\n" + feedback + "\n\n" +
                "Raw GPT content:\n" + raw);
        }

        private void UpdateOpponentDebugPrompt(CreeiOpponentStatementRequest request)
        {
            SetCoachDebugStatus($"Leo GPT request: {CurrentCreeiStage}");
            SetDebugInputText(_debugPromptInput, CreeiOpponentStatementGenerator.BuildPrompt(request));
            SetDebugInputText(_debugResultInput, "Waiting for Leo GPT response...");
        }

        private void UpdateOpponentDebugResult(CreeiOpponentStatementResult result)
        {
            if (result == null)
            {
                SetCoachDebugStatus("Leo GPT returned no result.");
                SetDebugInputText(_debugResultInput, "No Leo GPT result reached the scene.");
                return;
            }

            SetCoachDebugStatus(result.IsValid
                ? $"Leo GPT returned a valid {CurrentCreeiStage} statement."
                : "Leo GPT did not return a valid statement.");
            SetDebugInputText(
                _debugResultInput,
                "Validation: " + result.DebugInfo + "\n\n" +
                "speech_text:\n" + (result.SpeechText ?? string.Empty) + "\n\n" +
                "Raw GPT content:\n" + (result.RawJson ?? string.Empty));
        }

        private void UpdateCoachDebugPrompt(CoachFeedbackRequest request)
        {
            SetCoachDebugStatus("Preparing Coach request...");
            SetDebugInputText(_debugPromptInput, DebateCoachFeedbackGenerator.BuildPrompt(request));
            SetDebugInputText(_debugResultInput, "Waiting for GPT response and validation...");
        }

        private void UpdateCoachDebugResult(CoachFeedbackRequest request, CoachFeedbackResult result, string feedback)
        {
            string source = result.Source == CoachFeedbackSource.OpenAI ? "GPT" : "No valid GPT feedback";
            string diagnosis = BuildCoachFailureSummary(result);
            bool requestWasSent = !diagnosis.Contains("GPT was not called", StringComparison.OrdinalIgnoreCase);
            bool validFeedbackReturned = result.Source == CoachFeedbackSource.OpenAI &&
                                         !string.IsNullOrWhiteSpace(result.FeedbackText);
            SetCoachDebugStatus(
                $"Request sent: {(requestWasSent ? "YES" : "NO")}\n" +
                $"Valid GPT feedback returned: {(validFeedbackReturned ? "YES" : "NO")}\n" +
                diagnosis);

            string raw = string.IsNullOrWhiteSpace(result.RawJson)
                ? "(No raw GPT JSON was returned to the app.)"
                : result.RawJson.Trim();

            SetDebugInputText(
                _debugResultInput,
                "Source: " + source + "\n\n" +
                "Human-readable diagnosis:\n" + diagnosis + "\n\n" +
                "Debug: " + result.DebugInfo + "\n\n" +
                "Parsed feedback_text:\n" + (string.IsNullOrWhiteSpace(feedback) ? "(empty / not speakable)" : feedback) + "\n\n" +
                "Raw response / diagnostic body:\n" + raw + "\n\n" +
                "Request summary:\n" +
                "level=" + request.FeedbackLevel + "\n" +
                "player_utterance=" + request.PlayerUtteranceText + "\n" +
                "opponent_utterance=" + request.OpponentUtteranceText);
        }

        private static string BuildCoachFailureSummary(CoachFeedbackResult result)
        {
            if (result == null)
            {
                return "Reason: no Coach result object was returned.";
            }

            if (result.Source == CoachFeedbackSource.OpenAI && !string.IsNullOrWhiteSpace(result.FeedbackText))
            {
                return "Reason: GPT returned valid feedback.";
            }

            string debug = result.DebugInfo ?? string.Empty;
            string lower = debug.ToLowerInvariant();
            if (lower.Contains("api key missing") || lower.Contains("api key is empty"))
            {
                return "Reason: API key problem. GPT was not called.";
            }

            if (lower.Contains("http_status=401") || lower.Contains("http_status=403"))
            {
                return "Reason: API key or permission problem. GPT rejected the request.";
            }

            if (lower.Contains("http_status=429"))
            {
                return "Reason: quota or rate-limit problem.";
            }

            if (lower.Contains("http_status=0") || lower.Contains("connection") || lower.Contains("timeout") || lower.Contains("network"))
            {
                return "Reason: network/proxy/DNS/timeout problem.";
            }

            if (lower.Contains("could not be parsed") || lower.Contains("parser error") || lower.Contains("did not contain output_text"))
            {
                return "Reason: GPT replied, but the app could not parse the response format.";
            }

            if (lower.Contains("feedback_text was empty"))
            {
                return "Reason: GPT replied, but feedback_text was empty.";
            }

            return "Reason: GPT did not return valid speakable feedback.";
        }

        private void UpdateCoachDebugConvaiPrompt(string speechPrompt)
        {
            if (_debugResultInput == null)
            {
                return;
            }

            string existing = _debugResultInput.text ?? string.Empty;
            SetDebugInputText(
                _debugResultInput,
                existing + "\n\nPrompt sent to Anna / Convai:\n" + (speechPrompt ?? string.Empty));
        }

        private void AppendCoachDebugLine(string line)
        {
            if (_debugResultInput == null || string.IsNullOrWhiteSpace(line))
            {
                return;
            }

            string existing = _debugResultInput.text ?? string.Empty;
            SetDebugInputText(_debugResultInput, existing + "\n\n" + line.Trim());
        }

        private void SetCoachDebugStatus(string text)
        {
            if (_debugStatusText != null)
            {
                _debugStatusText.text = text;
            }
        }

        private static void SetDebugInputText(TMP_InputField input, string text)
        {
            if (input != null)
            {
                input.SetTextWithoutNotify(text ?? string.Empty);
            }
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
            UpdateStageSelectionVisuals();
            bool intro = Phase == OrchestrationPhase.Intro || Phase == OrchestrationPhase.Complete;
            bool waitingForPlayer = Phase == OrchestrationPhase.WaitingForPlayer;
            bool playerResponseReady = Phase == OrchestrationPhase.PlayerResponseReady;
            bool coachReady = Phase == OrchestrationPhase.CoachSuggestionReady;
            bool coachGenerating = Phase == OrchestrationPhase.CoachGenerating;
            bool canChooseJsonMode = Phase == OrchestrationPhase.Intro ||
                                     Phase == OrchestrationPhase.OpponentGenerating ||
                                     Phase == OrchestrationPhase.OpponentSpeaking ||
                                     Phase == OrchestrationPhase.WaitingForPlayer;
            bool canAskAnna = coachReady &&
                              HasPreparedCoachFeedback() &&
                              !_coachFeedbackSpeechActive &&
                              !_coachFeedbackSentToAnna;
            bool canRequestExample = coachReady &&
                                     HasPreparedCoachFeedback() &&
                                     _coachFeedbackSentToAnna &&
                                     !_exampleRequestedForCurrentTurn &&
                                     !_mockDebateFeedbackReady;
            bool activeSession = Phase != OrchestrationPhase.Intro && Phase != OrchestrationPhase.Complete;
            bool mockDebate = Phase == OrchestrationPhase.MockDebateReady ||
                              Phase == OrchestrationPhase.MockDebateOpponentSpeaking ||
                              Phase == OrchestrationPhase.MockDebateSpeaking ||
                              Phase == OrchestrationPhase.MockDebateAwaitingTranscript ||
                              Phase == OrchestrationPhase.MockDebateEvaluating;

            if (_studyStarted)
            {
                SetActive(_startButton, false);
                SetActive(_detailToggle, false);
                SetActive(_askCoachButton, false);
                SetActive(_exampleButton, false);
                SetActive(_continueButton, false);
                SetActive(_endButton, false);
                return;
            }

            SetActive(_startButton, intro);
            SetActive(_detailToggle, canChooseJsonMode);
            SetActive(_askCoachButton, coachReady || coachGenerating || playerResponseReady || (showManualPauseButton && waitingForPlayer));
            SetActive(_exampleButton, canRequestExample);
            SetActive(_continueButton, coachReady);
            SetActive(_endButton, activeSession && !mockDebate);

            SetButtonInteractable(_askCoachButton, canAskAnna || (showManualPauseButton && waitingForPlayer));
            SetButtonInteractable(_detailToggle, canChooseJsonMode);
            SetButtonInteractable(_continueButton, coachReady && !_coachFeedbackSpeechActive);
            SetButtonInteractable(_exampleButton, canRequestExample && !_coachFeedbackSpeechActive);

            if (_askCoachButton != null)
            {
                TMP_Text label = _askCoachButton.GetComponentInChildren<TMP_Text>();
                if (label != null)
                {
                    label.text = "Ask Anna";
                }
            }

            if (_continueButton != null)
            {
                TMP_Text label = _continueButton.GetComponentInChildren<TMP_Text>();
                if (label != null)
                {
                    label.text = _mockDebateFeedbackReady
                        ? "Finish Session"
                        : _creeiStageIndex >= CreeiStages.Length - 1
                            ? "Start Mock Debate"
                            : "Continue to " + CreeiStages[_creeiStageIndex + 1];
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
            _isUiMode = visible;
            ApplyControlCursorState();
        }

        private void ApplyControlCursorState()
        {
            Cursor.lockState = _isUiMode ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = _isUiMode;
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

        private static Toggle CreateToggle(Transform parent, string label)
        {
            GameObject toggleObject = CreateRect(label, parent);
            LayoutElement layout = toggleObject.AddComponent<LayoutElement>();
            layout.preferredHeight = 34f;
            layout.minHeight = 34f;

            Toggle toggle = toggleObject.AddComponent<Toggle>();

            GameObject backgroundObject = CreateRect("Background", toggleObject.transform);
            RectTransform backgroundRect = backgroundObject.GetComponent<RectTransform>();
            backgroundRect.anchorMin = new Vector2(0f, 0.5f);
            backgroundRect.anchorMax = new Vector2(0f, 0.5f);
            backgroundRect.pivot = new Vector2(0f, 0.5f);
            backgroundRect.anchoredPosition = Vector2.zero;
            backgroundRect.sizeDelta = new Vector2(26f, 26f);
            Image background = backgroundObject.AddComponent<Image>();
            background.color = new Color(0.10f, 0.12f, 0.14f, 0.96f);

            GameObject checkmarkObject = CreateRect("Checkmark", backgroundObject.transform);
            RectTransform checkmarkRect = checkmarkObject.GetComponent<RectTransform>();
            checkmarkRect.anchorMin = new Vector2(0.5f, 0.5f);
            checkmarkRect.anchorMax = new Vector2(0.5f, 0.5f);
            checkmarkRect.pivot = new Vector2(0.5f, 0.5f);
            checkmarkRect.anchoredPosition = Vector2.zero;
            checkmarkRect.sizeDelta = new Vector2(16f, 16f);
            Image checkmark = checkmarkObject.AddComponent<Image>();
            checkmark.color = new Color(0.24f, 0.78f, 0.48f, 1f);

            GameObject labelObject = CreateRect("Label", toggleObject.transform);
            RectTransform labelRect = labelObject.GetComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = new Vector2(38f, 0f);
            labelRect.offsetMax = Vector2.zero;
            TMP_Text text = labelObject.AddComponent<TextMeshProUGUI>();
            text.text = label;
            text.fontSize = 15;
            text.fontStyle = FontStyles.Bold;
            text.color = Color.white;
            text.alignment = TextAlignmentOptions.MidlineLeft;
            text.raycastTarget = false;

            toggle.targetGraphic = background;
            toggle.graphic = checkmark;
            toggle.isOn = false;
            return toggle;
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
            inputField.textViewport = inputObject.GetComponent<RectTransform>();

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

        private static TMP_InputField CreateReadOnlyInput(Transform parent, float height)
        {
            GameObject inputObject = CreateRect("Coach Debug Text", parent);
            Image image = inputObject.AddComponent<Image>();
            image.color = new Color(0f, 0f, 0f, 0.58f);

            LayoutElement layout = inputObject.AddComponent<LayoutElement>();
            layout.preferredHeight = height;
            layout.minHeight = height;

            TMP_InputField inputField = inputObject.AddComponent<TMP_InputField>();
            inputField.lineType = TMP_InputField.LineType.MultiLineNewline;
            inputField.readOnly = true;
            inputField.interactable = true;

            GameObject viewportObject = CreateRect("Viewport", inputObject.transform);
            RectTransform viewportRect = viewportObject.GetComponent<RectTransform>();
            viewportRect.anchorMin = Vector2.zero;
            viewportRect.anchorMax = Vector2.one;
            viewportRect.offsetMin = new Vector2(6f, 6f);
            viewportRect.offsetMax = new Vector2(-6f, -6f);
            viewportObject.AddComponent<RectMask2D>();
            inputField.textViewport = viewportRect;

            GameObject textObject = CreateRect("Text", viewportObject.transform);
            RectTransform textRect = textObject.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(4f, 2f);
            textRect.offsetMax = new Vector2(-4f, -2f);

            TMP_Text textComponent = textObject.AddComponent<TextMeshProUGUI>();
            textComponent.fontSize = 12;
            textComponent.color = Color.white;
            textComponent.textWrappingMode = TextWrappingModes.Normal;
            textComponent.alignment = TextAlignmentOptions.TopLeft;

            GameObject placeholderObject = CreateRect("Placeholder", viewportObject.transform);
            RectTransform placeholderRect = placeholderObject.GetComponent<RectTransform>();
            placeholderRect.anchorMin = Vector2.zero;
            placeholderRect.anchorMax = Vector2.one;
            placeholderRect.offsetMin = new Vector2(4f, 2f);
            placeholderRect.offsetMax = new Vector2(-4f, -2f);

            TMP_Text placeholderText = placeholderObject.AddComponent<TextMeshProUGUI>();
            placeholderText.text = string.Empty;
            placeholderText.fontSize = 12;
            placeholderText.color = new Color(1f, 1f, 1f, 0.45f);
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

        private static void SetButtonInteractable(Selectable button, bool interactable)
        {
            if (button != null)
            {
                button.interactable = interactable;
            }
        }
    }
}
