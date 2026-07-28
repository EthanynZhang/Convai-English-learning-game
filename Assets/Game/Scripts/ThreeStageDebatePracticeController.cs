using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Convai.Scripts.Runtime.Addons;
using Convai.Scripts.Runtime.Core;
using Convai.Scripts.Runtime.Features;
using Convai.Scripts.Runtime.UI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Game.Debate
{
    public sealed class ThreeStageDebatePracticeController : MonoBehaviour, IMicroCreeiPracticeHost
    {
        private const string StandaloneDebugParticipantId = "DEBUG_SCENE04";

        [Header("Study")]
        [SerializeField] private string topicId =
            "individual_practice_vs_interaction_speaking";
        [SerializeField] private string debateTopic =
            ThreeStageDebatePracticeRules.MockDebateTopic;
        [SerializeField] private string learnerStance =
            ThreeStageDebatePracticeRules.LearnerStance;
        [SerializeField] private string opponentStance =
            ThreeStageDebatePracticeRules.OpponentStance;
        [SerializeField] private string transferDebateTopic =
            TransferDebateStageGuard.TransferTopic;
        [SerializeField] private bool loadTransferSceneOnCompletion = true;

        [Header("Scene References")]
        [SerializeField] private Canvas uiCanvas;
        [SerializeField] private ConvaiNPC coachNPC;
        [SerializeField] private ConvaiNPC opponentNPC;
        [SerializeField] private XfyunRealtimeTranscriber realtimeTranscriber;
        [SerializeField] private ThreeStageDebatePracticeView studyView;
        [SerializeField] private CoachEpisodeController episodeController;

        [Header("World Layout")]
        [SerializeField] private Vector3 coachFixedWorldPosition = new(0.8f, 0f, 2.33f);
        [SerializeField] private Vector3 opponentFixedWorldPosition = new(-0.25f, 0f, 2.5f);

        [Header("Coach Services")]
        [SerializeField] private string openAIModel = "gpt-4o-mini";
        [SerializeField] private float responseTimeoutSeconds = 30f;
        [SerializeField] private string openAIBaseUrl = "https://api.meding.site/v1/chat/completions";
        [SerializeField] private string openAIApiKeyOverride = "";
        [SerializeField] private bool speakCoachFeedback = true;
        [SerializeField] private bool speakOpponentChallenges = true;
        [SerializeField] private bool speakPracticeIntroductions = true;

        private readonly StringBuilder _liveTranscript = new();
        private CoachPolicyConfig _policyConfig;
        private CoachDiagnosisEngine _diagnosisEngine;
        private ICreeiArgumentDiagnosisEngine _structuredDiagnosisEngine;
        private DebateCoachFeedbackGenerator _feedbackGenerator;
        private OpponentChallengeGenerator _challengeGenerator;
        private CoachResearchLogger _logger;
        private CoachDiagnosisResult _latestDiagnosis;
        private Coroutine _diagnosisRoutine;
        private Coroutine _feedbackRoutine;
        private Coroutine _challengeRoutine;
        private Coroutine _restartRecordingRoutine;
        private CoachFixedPoseAnchor _coachAnchor;
        private CoachFixedPoseAnchor _opponentAnchor;
        private ConvaiPlayerMovement[] _movementComponents = Array.Empty<ConvaiPlayerMovement>();
        private bool[] _movementStates = Array.Empty<bool>();
        private CursorLockMode _originalCursorLockMode;
        private bool _originalCursorVisible;
        private bool _studyStarted;
        private bool _recording;
        private DebateVoiceCaptureTarget _voiceCaptureTarget;
        private string _learnerRequestVoicePrefix = string.Empty;
        private bool _cleanedUp;
        private bool _transitioning;
        private float _stageStartedAt;
        private float _recordingStartedAt;
        private float _stageElapsed;
        private int _attemptIndex;
        private string _researchAttemptId = string.Empty;
        private string _fullSpeechResponseId = string.Empty;
        private string _revisionSpeechResponseId = string.Empty;
        private string _microInitialResponseId = string.Empty;
        private readonly Dictionary<int, string> _microIndependentResponseIds = new();
        private readonly Dictionary<int, string> _microRevisionResponseIds = new();
        private int _researchConfirmedTranscriptCount;
        private int _researchAudioArtifactCount;
        private int _successfulDiagnosisCount;
        private int _coachEpisodeCount;
        private float _totalCoachSeconds;
        private float _diagnosisStartedAt;
        private string _fullSpeechTranscript = string.Empty;
        private string _latestFeedback = string.Empty;
        private string _learnerRequest = string.Empty;
        private string _lastRequestInputModality = "text";
        private float _coachOpportunityPresentedAt;
        private bool _feedbackPresentedInCurrentEpisode;
        private CoachFeedbackLevel _latestFeedbackLevel = CoachFeedbackLevel.Level2;
        private bool _coachSpeechPending;
        private bool _coachSpeechStarted;
        private float _coachSpeechDeadline;
        private bool _stageIntroductionPending;
        private bool _stageIntroductionSpeechStarted;
        private float _stageIntroductionDeadline;
        private System.Diagnostics.Process _stageIntroductionSpeechProcess;
        private MicroChallengeLoop _microLoop;
        private string _microInitialStatement = string.Empty;
        private string _currentChallenge = string.Empty;
        private string _currentAttackFocus = string.Empty;
        private string _independentAnswer = string.Empty;
        private string _latestRevision = string.Empty;
        private bool _opponentSpeechPending;
        private bool _opponentSpeechStarted;
        private float _opponentSpeechDeadline;
        private TechnicalOperation _technicalOperation;
        private bool _completionDataComplete;
        private MicroCreeiPracticeController _microCreeiController;
        private int _microCreeiSnapshotCount;
        private int _microCreeiCompletedRoundCount;
        private Action<bool> _microLeoSpeechCompletion;
        private Action<bool> _microAnnaSpeechCompletion;
        private float _microCoachVoiceRequestedAt;
        private float _microCoachVoiceStartedAt;

        private enum TechnicalOperation
        {
            None,
            ChallengeGeneration,
            Diagnosis,
            RevisionEvaluation
        }

        public DebatePracticeStage CurrentStage { get; private set; } =
            DebatePracticeStage.MicroPractice;
        public DebatePracticePhase Phase { get; private set; } = DebatePracticePhase.Setup;
        public CoachOrchestrationMode Mode { get; private set; } =
            CoachOrchestrationMode.Disabled;
        public float RecordingElapsedSeconds { get; private set; }
        public string ConfirmedTranscript { get; private set; } = string.Empty;
        public float StageElapsedSeconds => Mathf.Max(0f, _stageElapsed);
        public bool IsCapturingLearnerRequest =>
            _voiceCaptureTarget == DebateVoiceCaptureTarget.LearnerRequest;
        public string ParticipantId => CoachStudySessionContext.Current?.ParticipantId ?? string.Empty;
        public string SessionId => CoachStudySessionContext.Current?.SessionId ?? string.Empty;
        public string TopicId => topicId;
        public string Topic => debateTopic;
        public string LearnerStance => learnerStance;
        public string DiagnosisModelVersion => _policyConfig?.DiagnosisModelVersion ?? "coach-diagnosis-local-v3";
        public string PolicyVersion => _policyConfig?.PolicyVersion ?? "three-mode-v3";
        public string LogDirectory
        {
            get
            {
                ResearchSessionManager manager = ResearchSessionManager.Instance != null
                    ? ResearchSessionManager.Instance
                    : FindAnyObjectByType<ResearchSessionManager>(FindObjectsInactive.Include);
                return manager != null && manager.HasActiveSession
                    ? manager.Current.SessionDirectory
                    : string.Empty;
            }
        }
        public bool IsNpcSpeechActive => _coachSpeechPending || _coachSpeechStarted;

        private void Awake()
        {
            if (!Application.isPlaying) return;
            _originalCursorLockMode = Cursor.lockState;
            _originalCursorVisible = Cursor.visible;
            ResolveSceneReferences();
            PrepareOpponentFlow();
            DisableCoachNpcToNpcFlow();
            if (coachNPC != null)
            {
                coachNPC.transform.position = coachFixedWorldPosition;
                ConvaiPlayerMovement learner =
                    FindAnyObjectByType<ConvaiPlayerMovement>(FindObjectsInactive.Include);
                if (learner != null)
                    coachNPC.transform.rotation = CoachFixedPoseAnchor.CalculateFacingRotation(
                        coachFixedWorldPosition, learner.transform.position);
                _coachAnchor = new CoachFixedPoseAnchor(coachNPC.transform);
            }
            if (opponentNPC != null)
            {
                opponentNPC.transform.position = opponentFixedWorldPosition;
                ConvaiPlayerMovement learner =
                    FindAnyObjectByType<ConvaiPlayerMovement>(FindObjectsInactive.Include);
                if (learner != null)
                    opponentNPC.transform.rotation = CoachFixedPoseAnchor.CalculateFacingRotation(
                        opponentFixedWorldPosition, learner.transform.position);
                _opponentAnchor = new CoachFixedPoseAnchor(opponentNPC.transform);
                opponentNPC.gameObject.SetActive(false);
            }
            if (coachNPC != null)
                NpcRoleWorldLabel.Ensure(coachNPC.transform, "Coach (Anna)",
                    new Color(0.16f, 0.68f, 1f), new Vector3(0f, 2.15f, 0f));
            _policyConfig = CoachPolicyConfig.CreateDefault();
            _diagnosisEngine = new CoachDiagnosisEngine(
                openAIModel, responseTimeoutSeconds, openAIBaseUrl, openAIApiKeyOverride);
            _structuredDiagnosisEngine = new Scene04LocalCreeiDiagnosisEngine();
            _feedbackGenerator = new DebateCoachFeedbackGenerator(
                openAIModel, responseTimeoutSeconds, openAIBaseUrl, openAIApiKeyOverride);
            _logger = new CoachResearchLogger();
            studyView.Build(uiCanvas);
            _microCreeiController = GetComponent<MicroCreeiPracticeController>();
            if (_microCreeiController == null)
                _microCreeiController = gameObject.AddComponent<MicroCreeiPracticeController>();
            _microCreeiController.Configure(this, uiCanvas, realtimeTranscriber);
            Subscribe();
            RegisterInputRouting();
            FreezePlayer();
            SetCursor(true);
        }

        private IEnumerator Start()
        {
            // ResearchSessionManager receives the sceneLoaded callback after scene Awake.
            // Defer one frame so all Scene 04 events use the correct stage metadata.
            yield return null;
            TryBeginAssignedStudy();
        }

        private void TryBeginAssignedStudy()
        {
            if (_studyStarted) return;
            ResearchSessionManager manager = ResearchSessionManager.Instance != null
                ? ResearchSessionManager.Instance
                : FindAnyObjectByType<ResearchSessionManager>(FindObjectsInactive.Include);
            if (manager == null || !manager.HasActiveSession)
            {
                studyView.ConfigureAssignedSession(
                    StandaloneDebugParticipantId,
                    CoachOrchestrationMode.Disabled);
                studyView.ShowSetup(
                    "Standalone debug mode. Choose any condition to start Scene 04.");
                return;
            }

            studyView.ConfigureAssignedSession(
                manager.Current.ParticipantId,
                manager.Current.Condition);
        }

        private void Update()
        {
            if (ThreeStageDebatePracticeRules.ShouldKeepCursorVisible(Phase))
                SetCursor(true);
            UpdateCoachSpeech();
            UpdateStageIntroduction();
            UpdateOpponentSpeech();
            if (_microCreeiController != null && _microCreeiController.IsActive)
            {
                _microCreeiController.Tick(Time.unscaledDeltaTime);
                _stageElapsed = ThreeStageDebatePracticeRules.MicroPracticeSeconds -
                                _microCreeiController.RemainingSeconds;
                return;
            }
            if (!_studyStarted || Phase is DebatePracticePhase.Complete or
                DebatePracticePhase.Introduction or DebatePracticePhase.StageIntroduction) return;

            _stageElapsed = Mathf.Max(0f, Time.realtimeSinceStartup - _stageStartedAt);
            if (CurrentStage == DebatePracticeStage.MicroPractice &&
                ThreeStageDebatePracticeRules.IsStageTimeExpired(CurrentStage, _stageElapsed))
            {
                ExpireMicroPractice();
                return;
            }

            if (_recording &&
                _voiceCaptureTarget == DebateVoiceCaptureTarget.PracticeSpeech)
            {
                RecordingElapsedSeconds = Mathf.Max(0f,
                    Time.realtimeSinceStartup - _recordingStartedAt);
                if (ThreeStageDebatePracticeRules.ShouldAutoStopRecording(
                        CurrentStage, RecordingElapsedSeconds))
                    StopRecording();
            }

            if (Phase is DebatePracticePhase.ReadyToRecord or
                DebatePracticePhase.Recording or
                DebatePracticePhase.AwaitingTranscript)
                ShowCurrentPractice();

            if (episodeController != null && episodeController.IsActive)
                episodeController.Advance(Time.unscaledDeltaTime,
                    _coachSpeechPending || _coachSpeechStarted);
        }

        private void LateUpdate()
        {
            _coachAnchor?.Apply();
            _opponentAnchor?.Apply();
        }

        private void OnDisable()
        {
            Cleanup();
        }

        private void OnDestroy()
        {
            Cleanup();
        }

        public void BeginStudy(string participantId, CoachOrchestrationMode mode)
        {
            if (_studyStarted) return;
            ResearchSessionManager manager = ResearchSessionManager.Instance != null
                ? ResearchSessionManager.Instance
                : FindAnyObjectByType<ResearchSessionManager>(FindObjectsInactive.Include);
            string assignmentMethod = manager != null && manager.HasActiveSession
                ? "scene04_manager_reuse"
                : "scene04_researcher_setup";
            ResearchStudySetupResult setup = ResearchStudySetupCoordinator.BeginOrReuse(
                participantId, mode, null, assignmentMethod);
            if (!setup.Success)
            {
                studyView.ShowSetup(setup.Message);
                return;
            }
            CoachStudySessionSnapshot coachSession = CoachStudySessionContext.Initialize(
                setup.ParticipantId,
                setup.Condition,
                debateTopic,
                transferDebateTopic);
            coachSession.SessionId = setup.SessionId;
            ResearchCapture.SyncCoachSession(coachSession);
            ResearchCapture.RecordEvent("practice_started", "system", string.Empty, new
            {
                condition = setup.Condition.ToString(),
                topic_id = topicId,
                topic_text = debateTopic,
                policy_version = _policyConfig?.PolicyVersion ?? string.Empty,
                diagnosis_model_version = _policyConfig?.DiagnosisModelVersion ?? string.Empty,
                feedback_model_version = _policyConfig?.FeedbackModelVersion ?? string.Empty
            });
            Mode = setup.Condition;
            _studyStarted = true;
            _attemptIndex = 0;
            _fullSpeechTranscript = string.Empty;
            _fullSpeechResponseId = string.Empty;
            _revisionSpeechResponseId = string.Empty;
            _microInitialResponseId = string.Empty;
            _microIndependentResponseIds.Clear();
            _microRevisionResponseIds.Clear();
            _researchConfirmedTranscriptCount = 0;
            _researchAudioArtifactCount = 0;
            _successfulDiagnosisCount = 0;
            _coachEpisodeCount = 0;
            _totalCoachSeconds = 0f;
            _completionDataComplete = false;
            _microCreeiSnapshotCount = 0;
            _microCreeiCompletedRoundCount = 0;
            _learnerRequest = string.Empty;
            ResetVoiceCaptureState(true);
            studyView.ClearLearnerRequest();
            studyView.ClearFeedbackHistory();
            Phase = DebatePracticePhase.Introduction;
            studyView.ShowIntroduction(debateTopic, learnerStance);
        }

        public void BeginPractice()
        {
            if (!_studyStarted || Phase != DebatePracticePhase.Introduction) return;
            EnterStage(DebatePracticeStage.MicroPractice);
        }

        public void ToggleRecording()
        {
            if (_microCreeiController != null && _microCreeiController.IsActive)
            {
                _microCreeiController.ToggleComponentRecording();
                return;
            }
            if (_voiceCaptureTarget == DebateVoiceCaptureTarget.LearnerRequest)
            {
                ToggleLearnerRequestRecording();
                return;
            }
            bool requestInputAvailable = studyView != null &&
                                         studyView.IsLearnerRequestVisible &&
                                         !studyView.IsLearnerRequestFocused;
            if (ThreeStageDebatePracticeRules.CanDictateLearnerRequest(
                    Mode, Phase, requestInputAvailable))
            {
                ToggleLearnerRequestRecording();
                return;
            }
            if (Phase == DebatePracticePhase.ReadyToRecord)
            {
                StartRecording();
                return;
            }
            if (Phase != DebatePracticePhase.Recording) return;
            if (!ThreeStageDebatePracticeRules.CanStopRecording(
                    CurrentStage, RecordingElapsedSeconds))
            {
                ShowCurrentPractice("Keep speaking until the 01:30 minimum is reached.");
                return;
            }
            StopRecording();
        }

        public void ToggleLearnerRequestRecording()
        {
            if (_voiceCaptureTarget == DebateVoiceCaptureTarget.LearnerRequest)
            {
                if (_recording) StopLearnerRequestRecording();
                return;
            }
            bool requestInputAvailable = studyView != null &&
                                         studyView.IsLearnerRequestVisible &&
                                         !studyView.IsLearnerRequestFocused;
            if (!ThreeStageDebatePracticeRules.CanDictateLearnerRequest(
                    Mode, Phase, requestInputAvailable)) return;
            StartLearnerRequestRecording();
        }

        public void SubmitConfirmedTranscript(string transcript)
        {
            string confirmed = transcript?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(confirmed) ||
                Phase is not (DebatePracticePhase.AwaitingTranscript or
                    DebatePracticePhase.TechnicalError)) return;
            if (CurrentStage != DebatePracticeStage.MicroPractice &&
                RecordingElapsedSeconds < ThreeStageDebatePracticeRules.SustainedSpeechMinimumSeconds)
            {
                ShowCurrentPractice("A complete speech must last at least 01:30.");
                return;
            }

            ConfirmedTranscript = confirmed;
            string responseRole = GetResearchResponseRole();
            string parentResponseId = GetParentResponseId(responseRole);
            ResearchTranscriptRecord researchTranscript = ResearchCapture.ConfirmTranscript(
                _researchAttemptId,
                responseRole,
                confirmed,
                RecordingElapsedSeconds,
                parentResponseId);
            RememberResponseId(responseRole, researchTranscript?.ResponseId);
            if (researchTranscript != null) _researchConfirmedTranscriptCount++;
            _researchAttemptId = string.Empty;
            _attemptIndex++;
            if (CurrentStage == DebatePracticeStage.MicroPractice && _microLoop != null)
            {
                switch (_microLoop.Step)
                {
                    case MicroChallengeStep.InitialStatement:
                        _microInitialStatement = confirmed;
                        LogConfirmedTranscript();
                        _microLoop.ConfirmInitialStatement();
                        BeginOpponentChallenge();
                        return;
                    case MicroChallengeStep.IndependentResponse:
                        _independentAnswer = confirmed;
                        LogConfirmedTranscript();
                        _microLoop.ConfirmIndependentResponse();
                        BeginDiagnosis();
                        return;
                    case MicroChallengeStep.RevisionResponse:
                        _latestRevision = confirmed;
                        LogConfirmedTranscript();
                        _microLoop.ConfirmRevision();
                        BeginDiagnosis();
                        return;
                    default:
                        return;
                }
            }
            if (CurrentStage == DebatePracticeStage.FullSpeechWithFeedback)
                _fullSpeechTranscript = confirmed;
            LogConfirmedTranscript();
            BeginDiagnosis();
        }

        private void ResolveSceneReferences()
        {
            if (uiCanvas == null) uiCanvas = FindAnyObjectByType<Canvas>(FindObjectsInactive.Include);
            if (realtimeTranscriber == null)
                realtimeTranscriber = FindAnyObjectByType<XfyunRealtimeTranscriber>(FindObjectsInactive.Include);
            if (coachNPC == null)
            {
                foreach (ConvaiNPC npc in FindObjectsByType<ConvaiNPC>(FindObjectsInactive.Include))
                    if (npc != null && string.Equals(npc.characterName, "Coach", StringComparison.OrdinalIgnoreCase))
                    {
                        coachNPC = npc;
                        break;
                    }
            }
            if (opponentNPC == null)
            {
                foreach (ConvaiNPC npc in FindObjectsByType<ConvaiNPC>(FindObjectsInactive.Include))
                {
                    if (npc == null || npc == coachNPC) continue;
                    bool isLeo = string.Equals(npc.characterName, "leo", StringComparison.OrdinalIgnoreCase) ||
                                 npc.gameObject.name.IndexOf("Mike", StringComparison.OrdinalIgnoreCase) >= 0;
                    if (!isLeo) continue;
                    opponentNPC = npc;
                    break;
                }
            }
            if (studyView == null) studyView = GetComponent<ThreeStageDebatePracticeView>();
            if (studyView == null) studyView = gameObject.AddComponent<ThreeStageDebatePracticeView>();
            if (episodeController == null) episodeController = GetComponent<CoachEpisodeController>();
            if (episodeController == null) episodeController = gameObject.AddComponent<CoachEpisodeController>();
        }

        private void PrepareOpponentFlow()
        {
            if (opponentNPC == null) return;
            foreach (ConvaiGroupNPCController controller in
                     opponentNPC.GetComponents<ConvaiGroupNPCController>())
                if (controller != null) controller.enabled = false;
            opponentNPC.gameObject.SetActive(false);
        }

        private void DisableCoachNpcToNpcFlow()
        {
            if (coachNPC != null)
                foreach (ConvaiGroupNPCController controller in
                         coachNPC.GetComponents<ConvaiGroupNPCController>())
                    if (controller != null) controller.enabled = false;
            if (opponentNPC != null)
                foreach (ConvaiGroupNPCController controller in
                         opponentNPC.GetComponents<ConvaiGroupNPCController>())
                    if (controller != null) controller.enabled = false;
        }

        private void EnterStage(DebatePracticeStage stage)
        {
            _transitioning = false;
            studyView?.HideCoachAgenda();
            CurrentStage = stage;
            Phase = DebatePracticePhase.StageIntroduction;
            _stageStartedAt = 0f;
            _stageElapsed = 0f;
            RecordingElapsedSeconds = 0f;
            ConfirmedTranscript = string.Empty;
            _voiceCaptureTarget = DebateVoiceCaptureTarget.None;
            _learnerRequestVoicePrefix = string.Empty;
            _liveTranscript.Clear();
            _latestDiagnosis = null;
            _latestFeedback = string.Empty;
            _technicalOperation = TechnicalOperation.None;
            bool useOpponent = ThreeStageDebatePracticeRules.UsesOpponent(stage);
            if (opponentNPC != null)
            {
                StopOpponentSpeech();
                opponentNPC.gameObject.SetActive(useOpponent);
            }
            if (stage == DebatePracticeStage.MicroPractice)
            {
                _microLoop = null;
                _microInitialStatement = string.Empty;
                _currentChallenge = string.Empty;
                _currentAttackFocus = string.Empty;
                _independentAnswer = string.Empty;
                _latestRevision = string.Empty;
            }
            else
            {
                _microLoop = null;
            }
            RegisterInputRouting();
            BeginStageIntroduction();
        }

        private void BeginStageIntroduction()
        {
            Phase = DebatePracticePhase.StageIntroduction;
            _stageIntroductionPending = true;
            _stageIntroductionSpeechStarted = false;
            _stageIntroductionDeadline = Time.realtimeSinceStartup +
                                         GetFixedIntroductionTimeoutSeconds(
                                             ThreeStageDebatePracticeRules.GetSpokenIntroduction(CurrentStage));
            string introduction = ThreeStageDebatePracticeRules.GetSpokenIntroduction(CurrentStage);
            ShowCurrentPractice(introduction);
            ResearchCapture.RecordEvent("practice_introduction_started", "coach", "learner", new
            {
                practice_stage = CurrentStage.ToString(),
                practice_number = ThreeStageDebatePracticeRules.GetStageNumber(CurrentStage),
                introduction_text = introduction
            });
            if (!speakPracticeIntroductions)
            {
                CompleteStageIntroduction(false, "speech_disabled");
                return;
            }
            StartFixedStageIntroductionSpeech(introduction);
        }

        private void UpdateStageIntroduction()
        {
            if (!_stageIntroductionPending || Phase != DebatePracticePhase.StageIntroduction) return;
            if (ThreeStageDebatePracticeRules.HasStageIntroductionTimedOut(
                    Time.realtimeSinceStartup, _stageIntroductionDeadline))
            {
                StopFixedStageIntroductionSpeech(true);
                CompleteStageIntroduction(false, "hard_timeout");
                return;
            }
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            if (_stageIntroductionSpeechProcess != null)
            {
                try
                {
                    if (!_stageIntroductionSpeechProcess.HasExited) return;
                    StopFixedStageIntroductionSpeech(false);
                    CompleteStageIntroduction(true, string.Empty);
                }
                catch (InvalidOperationException exception)
                {
                    Debug.LogWarning("Could not observe the fixed introduction voice: " + exception.Message);
                    StopFixedStageIntroductionSpeech(false);
                    CompleteStageIntroduction(false, "offline_tts_process_error");
                }
                return;
            }
#endif
            if (_coachSpeechStarted) _stageIntroductionSpeechStarted = true;
            if (_coachSpeechPending || _coachSpeechStarted) return;
            CompleteStageIntroduction(
                _stageIntroductionSpeechStarted,
                _stageIntroductionSpeechStarted ? string.Empty : "speech_timeout_or_unavailable");
        }

        private void StartFixedStageIntroductionSpeech(string text)
        {
            StopFixedStageIntroductionSpeech(true);
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            if (string.IsNullOrWhiteSpace(text))
            {
                CompleteStageIntroduction(false, "empty_introduction");
                return;
            }

            try
            {
                string escapedText = text.Replace("'", "''");
                string command =
                    "Add-Type -AssemblyName System.Speech; " +
                    "$s = New-Object System.Speech.Synthesis.SpeechSynthesizer; " +
                    "$englishVoices = $s.GetInstalledVoices() | Where-Object { $_.Enabled -and $_.VoiceInfo.Culture.Name -like 'en-*' }; " +
                    "$voice = $englishVoices | Where-Object { $_.VoiceInfo.Gender -eq 'Female' } | Select-Object -First 1; " +
                    "if (-not $voice) { $voice = $englishVoices | Select-Object -First 1 }; " +
                    "if ($voice) { $s.SelectVoice($voice.VoiceInfo.Name) }; " +
                    "$s.Rate = 1; $s.Volume = 100; $s.Speak('" + escapedText + "'); $s.Dispose();";
                string encodedCommand = Convert.ToBase64String(Encoding.Unicode.GetBytes(command));
                System.Diagnostics.ProcessStartInfo startInfo = new()
                {
                    FileName = "powershell.exe",
                    Arguments = "-NoProfile -NonInteractive -WindowStyle Hidden -EncodedCommand " + encodedCommand,
                    CreateNoWindow = true,
                    UseShellExecute = false
                };
                _stageIntroductionSpeechProcess = System.Diagnostics.Process.Start(startInfo);
                if (_stageIntroductionSpeechProcess == null)
                {
                    CompleteStageIntroduction(false, "offline_tts_process_unavailable");
                    return;
                }
                _stageIntroductionSpeechStarted = true;
                Debug.Log("Started fixed practice introduction with offline Windows TTS.");
            }
            catch (Exception exception)
            {
                Debug.LogWarning("Offline practice introduction TTS failed: " + exception.Message);
                CompleteStageIntroduction(false, "offline_tts_start_failed");
            }
#else
            if (coachNPC == null)
            {
                CompleteStageIntroduction(false, "coach_npc_unavailable");
                return;
            }
            StartCoachSpeech(text);
#endif
        }

        private static float GetFixedIntroductionTimeoutSeconds(string text)
        {
            int wordCount = string.IsNullOrWhiteSpace(text)
                ? 0
                : text.Split(new[] { ' ', '\t', '\r', '\n' },
                    StringSplitOptions.RemoveEmptyEntries).Length;
            return Mathf.Clamp(8f + wordCount / 1.8f, 12f, 25f);
        }

        private void StopFixedStageIntroductionSpeech(bool terminate)
        {
            if (_stageIntroductionSpeechProcess == null) return;
            try
            {
                if (terminate && !_stageIntroductionSpeechProcess.HasExited)
                    _stageIntroductionSpeechProcess.Kill();
            }
            catch (Exception exception)
            {
                Debug.LogWarning("Could not stop the fixed introduction voice: " + exception.Message);
            }
            finally
            {
                _stageIntroductionSpeechProcess.Dispose();
                _stageIntroductionSpeechProcess = null;
            }
        }

        private void CompleteStageIntroduction(bool spoken, string failureReason)
        {
            if (!_stageIntroductionPending || Phase != DebatePracticePhase.StageIntroduction) return;
            _stageIntroductionPending = false;
            _stageIntroductionDeadline = 0f;
            string eventType = spoken
                ? "practice_introduction_completed"
                : "practice_introduction_failed";
            ResearchCapture.RecordEvent(eventType, spoken ? "coach" : "system", "learner", new
            {
                practice_stage = CurrentStage.ToString(),
                practice_number = ThreeStageDebatePracticeRules.GetStageNumber(CurrentStage),
                failure_reason = failureReason ?? string.Empty
            });
            Phase = DebatePracticePhase.ReadyToRecord;
            _stageStartedAt = Time.realtimeSinceStartup;
            _stageElapsed = 0f;
            if (CurrentStage == DebatePracticeStage.MicroPractice)
            {
                studyView.SetRootVisible(false);
                _microCreeiController.BeginMicroCreeiPractice();
                return;
            }
            studyView.SetRootVisible(true);
            ShowCurrentPractice();
        }

        private void AdvanceStage()
        {
            if (_transitioning) return;
            _transitioning = true;
            studyView?.HideCoachAgenda();
            CancelNetworkActivity();
            StopFixedStageIntroductionSpeech(true);
            StopCoachSpeech();
            StopOpponentSpeech();
            ResetVoiceCaptureState(true);
            DebatePracticeStage? next = ThreeStageDebatePracticeRules.Next(CurrentStage);
            if (next.HasValue)
            {
                EnterStage(next.Value);
                return;
            }
            CompleteStudy();
        }

        private void ExpireMicroPractice()
        {
            if (_transitioning || CurrentStage != DebatePracticeStage.MicroPractice) return;
            _microLoop?.Expire();
            if (episodeController != null && episodeController.HasOpenOpportunity)
                episodeController.Complete(CoachTerminationReason.TimeLimit, ControlOwner.SystemSafety);
            AdvanceStage();
        }

        private void BeginOpponentChallenge()
        {
            if (CurrentStage != DebatePracticeStage.MicroPractice || _microLoop == null ||
                _microLoop.Step != MicroChallengeStep.GeneratingChallenge) return;
            CancelNetworkActivity();
            _technicalOperation = TechnicalOperation.ChallengeGeneration;
            Phase = DebatePracticePhase.GeneratingChallenge;
            ShowCurrentPractice();
            OpponentChallengeRequest request = new()
            {
                Topic = debateTopic,
                LearnerStance = learnerStance,
                OpponentStance = opponentStance,
                LearnerStatement = _microLoop.CycleIndex == 1
                    ? _microInitialStatement
                    : _latestRevision,
                PreviousRevision = _latestRevision,
                CycleIndex = _microLoop.CycleIndex,
                Difficulty = _microLoop.Difficulty
            };
            _challengeRoutine = StartCoroutine(
                _challengeGenerator.Generate(request, CompleteOpponentChallenge));
        }

        private void CompleteOpponentChallenge(OpponentChallengeResult result)
        {
            _challengeRoutine = null;
            if (CurrentStage != DebatePracticeStage.MicroPractice || _microLoop == null ||
                _microLoop.Step != MicroChallengeStep.GeneratingChallenge) return;
            if (result == null || !result.Success || string.IsNullOrWhiteSpace(result.SpeechText))
            {
                Phase = DebatePracticePhase.TechnicalError;
                _technicalOperation = TechnicalOperation.ChallengeGeneration;
                ShowCurrentPractice(result?.Error ??
                    "Leo's challenge could not be generated. Retry or technically skip.", false, true);
                return;
            }

            _currentChallenge = result.SpeechText.Trim();
            _currentAttackFocus = result.AttackFocus?.Trim() ?? string.Empty;
            _technicalOperation = TechnicalOperation.None;
            _microLoop.ChallengeReady();
            Phase = DebatePracticePhase.OpponentSpeaking;
            LogMicroEvent("OpponentChallengePresented");
            ShowCurrentPractice();
            StartOpponentSpeech(_currentChallenge);
        }

        private void StartOpponentSpeech(string text)
        {
            StopOpponentSpeech();
            if (!speakOpponentChallenges || opponentNPC == null || string.IsNullOrWhiteSpace(text))
            {
                CompleteOpponentSpeech(false);
                return;
            }
            opponentNPC.gameObject.SetActive(true);
            string prompt = JsonConvert.SerializeObject(new
            {
                instruction = "Leo must speak challenge_text exactly once. Do not add advice or extra words.",
                challenge_text = text.Trim()
            });
            _opponentSpeechPending = true;
            _opponentSpeechStarted = false;
            _opponentSpeechDeadline = Time.realtimeSinceStartup +
                                      Mathf.Max(20f, responseTimeoutSeconds + 8f);
            ConvaiNPCManager.Instance?.SetActiveConvaiNPC(opponentNPC);
            opponentNPC.SendTextDataAsync(prompt);
        }

        private void UpdateOpponentSpeech()
        {
            if (!_opponentSpeechPending && !_opponentSpeechStarted) return;
            bool talking = opponentNPC != null && opponentNPC.IsCharacterTalking;
            bool queued = opponentNPC != null && opponentNPC.GetAudioResponseCount() > 0;
            if (_opponentSpeechPending && talking)
            {
                _opponentSpeechPending = false;
                _opponentSpeechStarted = true;
                return;
            }
            if (_opponentSpeechPending && !queued &&
                Time.realtimeSinceStartup >= _opponentSpeechDeadline)
            {
                _opponentSpeechPending = false;
                _opponentSpeechStarted = false;
                CompleteOpponentSpeech(false);
                return;
            }
            if (_opponentSpeechStarted && !talking && !queued)
            {
                _opponentSpeechStarted = false;
                _opponentSpeechPending = false;
                CompleteOpponentSpeech(true);
            }
        }

        private void CompleteOpponentSpeech(bool spoken)
        {
            Action<bool> completion = _microLeoSpeechCompletion;
            _microLeoSpeechCompletion = null;
            if (completion != null)
            {
                completion(spoken);
                return;
            }
            BeginCoachAfterOpponent();
        }

        private void BeginCoachAfterOpponent()
        {
            if (CurrentStage != DebatePracticeStage.MicroPractice || _microLoop == null ||
                _microLoop.Step != MicroChallengeStep.OpponentSpeaking) return;
            _microLoop.OpponentFinishedSpeaking();
            _technicalOperation = TechnicalOperation.None;
            _independentAnswer = _microLoop.CycleIndex == 1
                ? _microInitialStatement
                : _latestRevision;
            ConfirmedTranscript = _independentAnswer;
            string sourceResponseId = _microLoop.CycleIndex == 1
                ? _microInitialResponseId
                : _microRevisionResponseIds.TryGetValue(
                    _microLoop.CycleIndex - 1,
                    out string previousRevisionResponseId)
                    ? previousRevisionResponseId
                    : string.Empty;
            if (!string.IsNullOrWhiteSpace(sourceResponseId))
                _microIndependentResponseIds[_microLoop.CycleIndex] = sourceResponseId;
            _liveTranscript.Clear();
            RecordingElapsedSeconds = 0f;
            ResearchCapture.RecordEvent(
                "opponent_to_coach_direct",
                "system",
                payload: new
                {
                    challenge_cycle_index = _microLoop.CycleIndex,
                    source_response_id = sourceResponseId,
                    challenge_text = _currentChallenge,
                    coach_follows_without_new_learner_reply = true
                });
            BeginDiagnosis();
        }

        private void StopOpponentSpeech()
        {
            if (opponentNPC != null && opponentNPC.gameObject.activeInHierarchy &&
                (_opponentSpeechPending || _opponentSpeechStarted))
                opponentNPC.InterruptCharacterSpeech();
            _opponentSpeechPending = false;
            _opponentSpeechStarted = false;
            _opponentSpeechDeadline = 0f;
        }

        private void StartRecording()
        {
            if (_coachSpeechPending || _coachSpeechStarted) return;
            _technicalOperation = TechnicalOperation.None;
            if (coachNPC != null && (coachNPC.IsCharacterTalking || coachNPC.GetAudioResponseCount() > 0))
                StopCoachSpeech();
            if (realtimeTranscriber == null)
            {
                Phase = DebatePracticePhase.TechnicalError;
                studyView.SetTranscriptEditable(false);
                ShowCurrentPractice(
                    "Realtime transcription is unavailable. Check the microphone, then use Restart Recording.",
                    true);
                return;
            }
            _liveTranscript.Clear();
            RecordingElapsedSeconds = 0f;
            _recordingStartedAt = Time.realtimeSinceStartup;
            _voiceCaptureTarget = DebateVoiceCaptureTarget.PracticeSpeech;
            Phase = DebatePracticePhase.AwaitingTranscript;
            string device = MicrophoneManager.Instance?.SelectedMicrophoneName ?? string.Empty;
            ShowCurrentPractice("Connecting to English realtime transcription...");
            realtimeTranscriber.StartSession(device);
        }

        private void StopRecording()
        {
            if (!_recording ||
                _voiceCaptureTarget != DebateVoiceCaptureTarget.PracticeSpeech) return;
            _recording = false;
            Phase = DebatePracticePhase.AwaitingTranscript;
            realtimeTranscriber?.StopSession();
            ShowCurrentPractice("Finalizing the transcript...");
        }

        private void StartLearnerRequestRecording()
        {
            if (_coachSpeechPending || _coachSpeechStarted) StopCoachSpeech();
            if (realtimeTranscriber == null)
            {
                studyView.SetLearnerRequestVoiceState(false,
                    "Voice input is unavailable. You can still type your request.");
                return;
            }
            _learnerRequestVoicePrefix = studyView.LearnerRequestText;
            _liveTranscript.Clear();
            _recording = false;
            _voiceCaptureTarget = DebateVoiceCaptureTarget.LearnerRequest;
            studyView.SetLearnerRequestVoiceState(true,
                "Connecting to voice input... Press T again after recording starts to stop.");
            string device = MicrophoneManager.Instance?.SelectedMicrophoneName ?? string.Empty;
            realtimeTranscriber.StartSession(device);
        }

        private void StopLearnerRequestRecording()
        {
            if (!_recording ||
                _voiceCaptureTarget != DebateVoiceCaptureTarget.LearnerRequest) return;
            _recording = false;
            studyView.SetLearnerRequestVoiceState(true,
                "Finalizing your coaching request...");
            realtimeTranscriber?.StopSession();
        }

        private void HandleTranscriptionStarted()
        {
            if (_microCreeiController != null && _microCreeiController.IsActive) return;
            if (_voiceCaptureTarget == DebateVoiceCaptureTarget.LearnerRequest &&
                Phase == DebatePracticePhase.CoachInteraction)
            {
                _recording = true;
                studyView.SetLearnerRequestVoiceState(true,
                    "Listening... Press T to stop and insert the transcript.");
                return;
            }
            if (Phase != DebatePracticePhase.AwaitingTranscript) return;
            _recording = true;
            _recordingStartedAt = Time.realtimeSinceStartup;
            RecordingElapsedSeconds = 0f;
            _researchAttemptId = ResearchCapture.BeginAttempt(
                GetResearchResponseRole(), learnerStance);
            Phase = DebatePracticePhase.Recording;
            ShowCurrentPractice();
        }

        private void HandleTranscriptUpdated(string transcript)
        {
            if (_microCreeiController != null && _microCreeiController.IsActive) return;
            if (_voiceCaptureTarget == DebateVoiceCaptureTarget.LearnerRequest &&
                Phase == DebatePracticePhase.CoachInteraction)
            {
                _liveTranscript.Clear();
                _liveTranscript.Append(transcript?.Trim() ?? string.Empty);
                studyView.SetLearnerRequestText(
                    ThreeStageDebatePracticeRules.MergeLearnerRequestText(
                        _learnerRequestVoicePrefix, _liveTranscript.ToString()));
                return;
            }
            if (Phase is not (DebatePracticePhase.Recording or DebatePracticePhase.AwaitingTranscript)) return;
            _liveTranscript.Clear();
            _liveTranscript.Append(transcript?.Trim() ?? string.Empty);
        }

        private void HandleTranscriptionCompleted(string transcript)
        {
            if (_microCreeiController != null && _microCreeiController.IsActive) return;
            _recording = false;
            string final = string.IsNullOrWhiteSpace(transcript)
                ? _liveTranscript.ToString().Trim()
                : transcript.Trim();
            if (_voiceCaptureTarget == DebateVoiceCaptureTarget.LearnerRequest)
            {
                _voiceCaptureTarget = DebateVoiceCaptureTarget.None;
                _liveTranscript.Clear();
                studyView.SetLearnerRequestText(
                    ThreeStageDebatePracticeRules.MergeLearnerRequestText(
                        _learnerRequestVoicePrefix, final));
                if (!string.IsNullOrWhiteSpace(final)) _lastRequestInputModality = "voice";
                _learnerRequestVoicePrefix = string.Empty;
                studyView.SetLearnerRequestVoiceState(false,
                    string.IsNullOrWhiteSpace(final)
                        ? "No speech was detected. Press T to try again, or type your request."
                        : "Voice text added. Edit it if needed, then select Ask Coach.");
                return;
            }
            _liveTranscript.Clear();
            _liveTranscript.Append(final);
            _voiceCaptureTarget = DebateVoiceCaptureTarget.None;
            ResearchCapture.StopAttempt(
                _researchAttemptId,
                RecordingElapsedSeconds,
                "transcription_completed");
            ResearchAudioArtifactRecord audioArtifact = ResearchCapture.SaveAudio(
                _researchAttemptId,
                GetResearchResponseRole(),
                realtimeTranscriber?.LastAudioCapture);
            if (audioArtifact != null) _researchAudioArtifactCount++;
            studyView.SetTranscriptEditable(false);
            if (!ThreeStageDebatePracticeRules.ShouldAutomaticallyAcceptTranscript(final))
            {
                Phase = DebatePracticePhase.ReadyToRecord;
                _liveTranscript.Clear();
                RecordingElapsedSeconds = 0f;
                ShowCurrentPractice("No speech was detected. Press T to try again.");
                return;
            }

            Phase = DebatePracticePhase.AwaitingTranscript;
            SubmitConfirmedTranscript(final);
        }

        private void HandleTranscriptionFailed(string error)
        {
            if (_microCreeiController != null && _microCreeiController.IsActive) return;
            _recording = false;
            string partial = realtimeTranscriber?.LatestTranscriptSnapshot?.Trim() ??
                             _liveTranscript.ToString().Trim();
            if (_voiceCaptureTarget == DebateVoiceCaptureTarget.LearnerRequest)
            {
                _voiceCaptureTarget = DebateVoiceCaptureTarget.None;
                _liveTranscript.Clear();
                studyView.SetLearnerRequestText(
                    ThreeStageDebatePracticeRules.MergeLearnerRequestText(
                        _learnerRequestVoicePrefix, partial));
                _learnerRequestVoicePrefix = string.Empty;
                studyView.SetLearnerRequestVoiceState(false,
                    (error ?? "Voice input failed.") +
                    " Any partial text was kept; you can retry with T or type.");
                return;
            }
            _liveTranscript.Clear();
            _liveTranscript.Append(partial);
            _voiceCaptureTarget = DebateVoiceCaptureTarget.None;
            ResearchCapture.StopAttempt(
                _researchAttemptId,
                RecordingElapsedSeconds,
                "transcription_failed");
            ResearchCapture.SaveAudio(
                _researchAttemptId,
                GetResearchResponseRole(),
                realtimeTranscriber?.LastAudioCapture);
            ResearchCapture.RecordTechnicalFailure(
                "asr_failure",
                "XFYUN_TRANSCRIPTION_FAILED",
                error ?? "Transcription failed.");
            _researchAttemptId = string.Empty;
            Phase = DebatePracticePhase.TechnicalError;
            studyView.SetTranscriptEditable(false);
            ShowCurrentPractice((error ?? "Transcription failed.") +
                                " Use Restart Recording to try again.", true);
        }

        private void Rerecord()
        {
            bool activePracticeCapture =
                (Phase is DebatePracticePhase.Recording or DebatePracticePhase.AwaitingTranscript) &&
                _voiceCaptureTarget == DebateVoiceCaptureTarget.PracticeSpeech;
            if (!activePracticeCapture && Phase != DebatePracticePhase.TechnicalError) return;
            if (Phase == DebatePracticePhase.TechnicalError &&
                _technicalOperation != TechnicalOperation.None) return;

            if (_restartRecordingRoutine != null)
            {
                StopCoroutine(_restartRecordingRoutine);
                _restartRecordingRoutine = null;
            }
            if (activePracticeCapture)
                realtimeTranscriber?.CancelSession();

            _recording = false;
            _voiceCaptureTarget = DebateVoiceCaptureTarget.None;
            ResetVoiceCaptureState(true);
            ConfirmedTranscript = string.Empty;
            _liveTranscript.Clear();
            RecordingElapsedSeconds = 0f;
            Phase = DebatePracticePhase.ReadyToRecord;
            studyView.SetTranscriptEditable(false);
            ShowCurrentPractice("Restarting recording...");
            _restartRecordingRoutine = StartCoroutine(RestartRecordingWhenReady());
        }

        private IEnumerator RestartRecordingWhenReady()
        {
            while (realtimeTranscriber != null && realtimeTranscriber.IsSessionActive)
                yield return null;

            _restartRecordingRoutine = null;
            if (!_studyStarted || Phase != DebatePracticePhase.ReadyToRecord) yield break;
            StartRecording();
        }

        private void BeginDiagnosis()
        {
            CancelNetworkActivity();
            bool microRevision = CurrentStage == DebatePracticeStage.MicroPractice &&
                                 _microLoop?.Step == MicroChallengeStep.EvaluatingRevision;
            Phase = microRevision
                ? DebatePracticePhase.EvaluatingRevision
                : DebatePracticePhase.Diagnosing;
            _technicalOperation = microRevision
                ? TechnicalOperation.RevisionEvaluation
                : TechnicalOperation.Diagnosis;
            ShowCurrentPractice(microRevision || CurrentStage == DebatePracticeStage.RevisionSpeech
                ? "Comparing the revision with the response Coach previously reviewed..."
                : "Analyzing your current position against Leo's challenge before Coach feedback...");
            CoachDiagnosisRequest request = new()
            {
                ParticipantId = CoachStudySessionContext.Current?.ParticipantId ?? string.Empty,
                Stage = CurrentStage == DebatePracticeStage.MicroPractice
                    ? microRevision
                        ? "MicroChallengeRevision"
                        : "MicroChallengeIndependentResponse"
                    : CurrentStage.ToString(),
                TopicId = topicId,
                PracticeCycleId = CurrentStage == DebatePracticeStage.MicroPractice
                    ? _microLoop?.CycleIndex ?? 1
                    : ThreeStageDebatePracticeRules.GetStageNumber(CurrentStage),
                TurnId = _attemptIndex,
                Topic = debateTopic,
                LearnerSide = learnerStance,
                OpponentUtteranceText = CurrentStage == DebatePracticeStage.MicroPractice
                    ? _currentChallenge
                    : string.Empty,
                PlayerUtteranceText = ConfirmedTranscript,
                PreviousConfirmedAttempt = microRevision
                    ? _independentAnswer
                    : CurrentStage == DebatePracticeStage.RevisionSpeech
                        ? _fullSpeechTranscript
                        : string.Empty,
                SelectedStrategy = "Any"
            };
            _diagnosisStartedAt = Time.realtimeSinceStartup;
            _diagnosisRoutine = StartCoroutine(
                _diagnosisEngine.Diagnose(request, CompleteDiagnosis));
        }

        private void CompleteDiagnosis(CoachDiagnosisResult diagnosis)
        {
            _diagnosisRoutine = null;
            if (Phase is not (DebatePracticePhase.Diagnosing or
                DebatePracticePhase.EvaluatingRevision)) return;
            bool microRevision = CurrentStage == DebatePracticeStage.MicroPractice &&
                                 _microLoop?.Step == MicroChallengeStep.EvaluatingRevision;
            float latencyMs = Mathf.Max(0f, Time.realtimeSinceStartup - _diagnosisStartedAt) * 1000f;
            string targetResponseId = GetDiagnosisTargetResponseId(microRevision);
            ResearchCapture.RecordEvent(
                "diagnosis_completed",
                "system",
                payload: new
                {
                    challenge_cycle_index = CurrentStage == DebatePracticeStage.MicroPractice
                        ? _microLoop?.CycleIndex ?? 0
                        : 0,
                    target_response_id = targetResponseId,
                    issue_code = diagnosis?.DiagnosisIssueCode ?? string.Empty,
                    severity = diagnosis?.Severity ?? 0,
                    confidence = diagnosis?.Confidence ?? 0f,
                    creei_missing_or_weak_components = diagnosis?.CreeiMissingOrWeakComponents ??
                                                       Array.Empty<string>(),
                    creei_gap_summary = diagnosis?.CreeiGapSummary ?? string.Empty,
                    recommended_focus = diagnosis?.RecommendedFocus ?? string.Empty,
                    diagnosis_model_version = diagnosis?.ModelVersion ??
                                              _policyConfig?.DiagnosisModelVersion ?? string.Empty,
                    latency_ms = latencyMs,
                    success = diagnosis?.Success == true,
                    error_code = diagnosis?.Success == true ? string.Empty : "DIAGNOSIS_FAILED",
                    error_message = diagnosis?.Error ?? string.Empty
                });
            if (diagnosis == null || !diagnosis.Success)
            {
                _latestDiagnosis = diagnosis;
                Phase = DebatePracticePhase.TechnicalError;
                ShowCurrentPractice(diagnosis?.Error ?? "Diagnosis failed. Retry or technically skip.",
                    false, true);
                return;
            }
            _latestDiagnosis = diagnosis;
            _successfulDiagnosisCount++;
            _technicalOperation = TechnicalOperation.None;
            if (microRevision)
            {
                LogMicroEvent("RevisionEvaluationCompleted", ConfirmedTranscript, diagnosis);
                _microLoop.RevisionEvaluationCompleted();
                if (_microLoop.Step == MicroChallengeStep.Completed)
                    AdvanceStage();
                else
                    BeginOpponentChallenge();
                return;
            }
            if (CurrentStage == DebatePracticeStage.RevisionSpeech)
            {
                LogSilentRevisionAssessment();
                AdvanceStage();
                return;
            }

            if (CurrentStage == DebatePracticeStage.MicroPractice)
                _microLoop?.DiagnosisCompleted();
            Phase = DebatePracticePhase.CoachInteraction;
            _feedbackPresentedInCurrentEpisode = false;
            _lastRequestInputModality = "text";
            _coachOpportunityPresentedAt = Time.realtimeSinceStartup;
            episodeController.Configure(
                Mode,
                CurrentStage == DebatePracticeStage.MicroPractice
                    ? _microLoop?.CycleIndex ?? 1
                    : ThreeStageDebatePracticeRules.GetStageNumber(CurrentStage),
                _policyConfig,
                _logger);
            episodeController.AutomaticTick = false;
            episodeController.BeginOpportunity(
                diagnosis,
                ConfirmedTranscript,
                topicId,
                _attemptIndex);
        }

        private void RetryDiagnosis()
        {
            if (Phase != DebatePracticePhase.TechnicalError) return;
            if (_technicalOperation == TechnicalOperation.ChallengeGeneration)
            {
                BeginOpponentChallenge();
                return;
            }
            if (!string.IsNullOrWhiteSpace(ConfirmedTranscript)) BeginDiagnosis();
        }

        private void TechnicalSkip()
        {
            if (Phase != DebatePracticePhase.TechnicalError) return;
            if (CurrentStage == DebatePracticeStage.MicroPractice)
            {
                LogMicroEvent("TechnicalSkip", ConfirmedTranscript, _latestDiagnosis);
                if (_microLoop?.Step == MicroChallengeStep.GeneratingChallenge)
                {
                    _currentChallenge = "Technical skip: no opponent challenge was available.";
                    _currentAttackFocus = string.Empty;
                    _microLoop.ChallengeReady();
                    Phase = DebatePracticePhase.OpponentSpeaking;
                    BeginCoachAfterOpponent();
                }
                else if (_microLoop?.Step == MicroChallengeStep.Diagnosing)
                {
                    _microLoop.DiagnosisCompleted();
                    _microLoop.CompleteCoaching();
                    Phase = DebatePracticePhase.ReadyToRecord;
                    ConfirmedTranscript = string.Empty;
                    _liveTranscript.Clear();
                    ShowCurrentPractice("Diagnosis skipped for a technical reason. Revise your current position.");
                }
                else if (_microLoop?.Step == MicroChallengeStep.EvaluatingRevision)
                {
                    _microLoop.RevisionEvaluationCompleted();
                    if (_microLoop.Step == MicroChallengeStep.Completed) AdvanceStage();
                    else BeginOpponentChallenge();
                }
            }
            else
            {
                AdvanceStage();
            }
        }

        private void HandleLearnerAction(CoachLearnerAction action, string focus)
        {
            if (Phase != DebatePracticePhase.CoachInteraction || episodeController == null) return;
            if (_voiceCaptureTarget == DebateVoiceCaptureTarget.LearnerRequest) return;
            if (action == CoachLearnerAction.RequestCoach &&
                Mode == CoachOrchestrationMode.LearnerLed)
            {
                if (string.IsNullOrWhiteSpace(focus))
                {
                    studyView.ShowConditionRequestEntry(CoachOrchestrationMode.LearnerLed);
                    RecordConditionControlEvent(
                        "coach_request_opened", CoachLearnerAction.RequestCoach);
                    return;
                }

                _learnerRequest = focus.Trim();
                studyView.ClearLearnerRequest();
                studyView.ShowCoachAgenda(
                    CoachAgendaSource.LearnerRequest,
                    "Your request to Anna:",
                    _learnerRequest);
                RecordConditionControlEvent(
                    "coach_agenda_presented",
                    CoachLearnerAction.RequestCoach,
                    _learnerRequest,
                    _lastRequestInputModality,
                    CoachAgendaSource.LearnerRequest,
                    _learnerRequest);
                RecordConditionControlEvent(
                    "coach_request_submitted",
                    CoachLearnerAction.RequestCoach,
                    _learnerRequest,
                    _lastRequestInputModality);
                _lastRequestInputModality = "text";
                episodeController.SubmitLearnerAction(CoachLearnerAction.RequestCoach, string.Empty);
                return;
            }
            if (Mode == CoachOrchestrationMode.SharedControl &&
                action == CoachLearnerAction.ChangeFocus)
            {
                studyView.ShowConditionRequestEntry(CoachOrchestrationMode.SharedControl);
                RecordConditionControlEvent(
                    "suggestion_change_requested", CoachLearnerAction.ChangeFocus);
                return;
            }
            if (Mode == CoachOrchestrationMode.SharedControl &&
                action == CoachLearnerAction.RequestCoach)
            {
                if (string.IsNullOrWhiteSpace(focus))
                {
                    studyView.ShowConditionRequestEntry(CoachOrchestrationMode.SharedControl);
                    RecordConditionControlEvent(
                        "coach_request_opened", CoachLearnerAction.RequestCoach);
                    return;
                }

                _learnerRequest = focus.Trim();
                studyView.ClearLearnerRequest();
                studyView.UpdateCoachAgenda(
                    CoachAgendaSource.LearnerModifiedRequest,
                    "Updated request to Anna:",
                    _learnerRequest);
                RecordConditionControlEvent(
                    "coach_agenda_updated",
                    CoachLearnerAction.RequestCoach,
                    _learnerRequest,
                    _lastRequestInputModality,
                    CoachAgendaSource.LearnerModifiedRequest,
                    _learnerRequest);
                RecordConditionControlEvent(
                    "coach_request_submitted",
                    CoachLearnerAction.RequestCoach,
                    _learnerRequest,
                    _lastRequestInputModality);
                _lastRequestInputModality = "text";
                episodeController.SubmitLearnerAction(CoachLearnerAction.RequestCoach, string.Empty);
                return;
            }
            if (Mode == CoachOrchestrationMode.SharedControl &&
                action == CoachLearnerAction.Accept)
            {
                RecordConditionControlEvent(
                    "suggestion_accepted", CoachLearnerAction.Accept);
            }
            if (Mode == CoachOrchestrationMode.SharedControl &&
                action == CoachLearnerAction.Decline)
            {
                RecordConditionControlEvent(
                    "suggestion_declined", CoachLearnerAction.Decline);
                RecordConditionControlEvent(
                    "coach_skipped", CoachLearnerAction.Decline);
            }
            if (Mode == CoachOrchestrationMode.LearnerLed &&
                action == CoachLearnerAction.ApplyNextCycle &&
                !_feedbackPresentedInCurrentEpisode)
            {
                RecordConditionControlEvent(
                    "coach_skipped", CoachLearnerAction.ApplyNextCycle);
            }
            if (action == CoachLearnerAction.Replay)
            {
                episodeController.SubmitLearnerAction(action, string.Empty);
                StartCoachSpeech(_latestFeedback);
                return;
            }
            if (action == CoachLearnerAction.RetryFeedback)
            {
                StartFeedback(_latestFeedbackLevel);
                return;
            }
            if (action is CoachLearnerAction.ExitCoaching or CoachLearnerAction.ApplyNextCycle)
                StopCoachSpeech();
            string selected = action == CoachLearnerAction.Accept
                ? string.Empty
                : action is CoachLearnerAction.ConfirmFocus or
                    CoachLearnerAction.ChangeFocus or CoachLearnerAction.Override
                    ? focus
                    : string.Empty;
            episodeController.SubmitLearnerAction(action, selected);
        }

        private void HandlePolicyDecision(CoachPolicyDecision decision)
        {
            if (decision == null || Phase != DebatePracticePhase.CoachInteraction) return;
            switch (decision.Action)
            {
                case CoachPolicyAction.MakeAvailable:
                    RecordConditionControlEvent("coach_opportunity_presented");
                    studyView.ShowConditionOpportunity(Mode, _latestDiagnosis);
                    RecordPresentedAgendaIfVisible();
                    break;
                case CoachPolicyAction.Invite:
                    RecordConditionControlEvent("coach_opportunity_presented");
                    studyView.ShowConditionOpportunity(
                        CoachOrchestrationMode.SharedControl, _latestDiagnosis);
                    RecordPresentedAgendaIfVisible();
                    break;
                case CoachPolicyAction.AutoStart:
                    RecordConditionControlEvent("coach_opportunity_presented");
                    studyView.ShowConditionOpportunity(
                        CoachOrchestrationMode.AiLed, _latestDiagnosis);
                    RecordPresentedAgendaIfVisible();
                    RecordConditionControlEvent("coach_auto_started");
                    StartFeedback(CoachFeedbackLevel.Level2);
                    break;
                case CoachPolicyAction.Start:
                    StartFeedback(CoachFeedbackLevel.Level2);
                    break;
                case CoachPolicyAction.Continue:
                    StartFeedback(decision.PolicyReason == CoachLearnerAction.NeedExample.ToString()
                        ? CoachFeedbackLevel.Level3
                        : CoachFeedbackLevel.Level2);
                    break;
            }
        }

        private void StartFeedback(CoachFeedbackLevel level)
        {
            if (_feedbackRoutine != null) StopCoroutine(_feedbackRoutine);
            _latestFeedbackLevel = level;
            _feedbackRoutine = StartCoroutine(GenerateFeedback(level));
        }

        private IEnumerator GenerateFeedback(CoachFeedbackLevel level)
        {
            string preparingStatus = level == CoachFeedbackLevel.Level3
                ? "Preparing one more concrete step..."
                : "Preparing focused Coach feedback...";
            studyView.ShowConditionLoading(Mode, preparingStatus);
            CoachFeedbackRequest request = new()
            {
                Stage = CurrentStage.ToString(),
                TopicId = topicId,
                Topic = debateTopic,
                PlayerSide = learnerStance,
                OpponentUtteranceText = CurrentStage == DebatePracticeStage.MicroPractice
                    ? _currentChallenge
                    : string.Empty,
                CurrentCreeiStage = episodeController.ConfirmedFocus,
                TurnId = _attemptIndex,
                PlayerUtteranceText = ConfirmedTranscript,
                PreviousCoachFeedbackText = _latestFeedback,
                SelectedStrategy = "Any",
                ConfirmedFocus = episodeController.ConfirmedFocus,
                LearnerRequest = _learnerRequest,
                DiagnosisIssueCode = _latestDiagnosis?.DiagnosisIssueCode ?? string.Empty,
                CreeiMissingOrWeakComponents = _latestDiagnosis?.CreeiMissingOrWeakComponents ??
                                               Array.Empty<string>(),
                CreeiGapSummary = _latestDiagnosis?.CreeiGapSummary ?? string.Empty,
                RecommendedStrategy = _latestDiagnosis?.RecommendedStrategy ?? string.Empty,
                TargetSuccessCriterion = _latestDiagnosis?.RecommendedNextAction ?? string.Empty,
                FeedbackLevel = level,
                FeedbackFormat = CoachFeedbackFormat.Scene04CreeiDetailed,
                DetailedJson = true
            };
            CoachFeedbackResult result = null;
            yield return _feedbackGenerator.GenerateFeedback(request, value => result = value);
            _feedbackRoutine = null;
            if (Phase != DebatePracticePhase.CoachInteraction) yield break;
            if (result == null || result.Source != CoachFeedbackSource.OpenAI ||
                string.IsNullOrWhiteSpace(result.FeedbackText))
            {
                studyView.ShowCoach(
                    "Coach feedback could not be generated. Retry or end coaching safely.",
                    _latestFeedback,
                    false,
                    CoachLearnerAction.RetryFeedback,
                    CoachLearnerAction.ApplyNextCycle);
                yield break;
            }
            _latestFeedback = result.FeedbackText.Trim();
            _feedbackPresentedInCurrentEpisode = true;
            studyView.AppendFeedbackHistory(_learnerRequest, _latestFeedback);
            _learnerRequest = string.Empty;
            studyView.ClearLearnerRequest();
            episodeController.NotifyFeedbackPresented(level.ToString(), result.FeedbackType,
                _latestFeedback);
            ShowFeedbackActions();
            StartCoachSpeech(_latestFeedback);
        }

        private void ShowFeedbackActions()
        {
            studyView.ShowConditionFeedback(Mode, _latestFeedback);
        }

        private void HandleEpisodeEnded(CoachTerminationReason reason)
        {
            if (_transitioning || Phase != DebatePracticePhase.CoachInteraction) return;
            studyView?.HideCoachAgenda();
            _coachEpisodeCount++;
            _totalCoachSeconds += episodeController?.ElapsedSeconds ?? 0f;
            if (CurrentStage == DebatePracticeStage.FullSpeechWithFeedback)
            {
                ResearchCapture.RecordEvent(
                    "full_speech_coach_episode",
                    "coach",
                    "learner",
                    new
                    {
                        coach_episode_id = episodeController?.CoachEpisodeId ?? string.Empty,
                        target_response_id = _fullSpeechResponseId,
                        condition = Mode.ToString(),
                        feedback_text = episodeController?.LatestFeedbackText ?? string.Empty,
                        feedback_turn_count = episodeController?.FeedbackTurnIndex ?? 0,
                        dose_seconds = episodeController?.ElapsedSeconds ?? 0f,
                        termination_reason = reason.ToString()
                    });
            }
            StopCoachSpeech();
            if (CurrentStage == DebatePracticeStage.MicroPractice && _microLoop != null &&
                _microLoop.Step == MicroChallengeStep.CoachInteraction)
            {
                _microLoop.CompleteCoaching();
                Phase = DebatePracticePhase.ReadyToRecord;
                ConfirmedTranscript = string.Empty;
                _liveTranscript.Clear();
                RecordingElapsedSeconds = 0f;
                ShowCurrentPractice("Now revise your answer to Leo using the Coach feedback. Press T to record.");
                return;
            }
            if (!ThreeStageDebatePracticeRules.ShouldAdvanceAfterCoachEpisode(
                    CurrentStage, reason, _stageElapsed))
            {
                Phase = DebatePracticePhase.ReadyToRecord;
                ConfirmedTranscript = string.Empty;
                _liveTranscript.Clear();
                RecordingElapsedSeconds = 0f;
                ShowCurrentPractice("Coach episode complete. Press T for another micro practice attempt.");
                return;
            }
            AdvanceStage();
        }

        private void StartCoachSpeech(string text)
        {
            StopCoachSpeech();
            if (!speakCoachFeedback || coachNPC == null || string.IsNullOrWhiteSpace(text)) return;
            string prompt = JsonConvert.SerializeObject(new
            {
                instruction = "Anna must speak the exact English Coach message in feedback_text once. Do not add words.",
                feedback_text = text.Trim()
            });
            _coachSpeechPending = true;
            _coachSpeechStarted = false;
            _coachSpeechDeadline = Time.realtimeSinceStartup + Mathf.Max(20f, responseTimeoutSeconds + 8f);
            if (_microAnnaSpeechCompletion != null)
            {
                _microCoachVoiceRequestedAt = Time.realtimeSinceStartup;
                _microCoachVoiceStartedAt = 0f;
                RecordMicroEvent("coach_tts_requested", new
                {
                    round_index = _microCreeiController?.CurrentRoundIndex ?? 0,
                    feedback_purpose = "coach_feedback"
                });
            }
            ConvaiNPCManager.Instance?.SetActiveConvaiNPC(coachNPC);
            coachNPC.SendTextDataAsync(prompt);
        }

        private void UpdateCoachSpeech()
        {
            if (!_coachSpeechPending && !_coachSpeechStarted) return;
            bool talking = coachNPC != null && coachNPC.IsCharacterTalking;
            bool queued = coachNPC != null && coachNPC.GetAudioResponseCount() > 0;
            if (_coachSpeechPending && talking)
            {
                _coachSpeechPending = false;
                _coachSpeechStarted = true;
                if (_microAnnaSpeechCompletion != null)
                {
                    _microCoachVoiceStartedAt = Time.realtimeSinceStartup;
                    RecordMicroEvent("coach_tts_started", new
                    {
                        round_index = _microCreeiController?.CurrentRoundIndex ?? 0,
                        wait_milliseconds = Mathf.Max(0, Mathf.RoundToInt(
                            (_microCoachVoiceStartedAt - _microCoachVoiceRequestedAt) * 1000f))
                    });
                }
                return;
            }
            if (_coachSpeechPending && !queued && Time.realtimeSinceStartup >= _coachSpeechDeadline)
            {
                _coachSpeechPending = false;
                _coachSpeechStarted = false;
                CompleteCoachSpeechCallback(false);
                return;
            }
            if (_coachSpeechStarted && !talking && !queued)
            {
                _coachSpeechStarted = false;
                _coachSpeechPending = false;
                CompleteCoachSpeechCallback(true);
            }
        }

        private void CompleteCoachSpeechCallback(bool spoken)
        {
            Action<bool> completion = _microAnnaSpeechCompletion;
            _microAnnaSpeechCompletion = null;
            if (completion != null)
            {
                float completedAt = Time.realtimeSinceStartup;
                float voiceStarted = _microCoachVoiceStartedAt > 0f
                    ? _microCoachVoiceStartedAt
                    : completedAt;
                float voiceRequested = _microCoachVoiceRequestedAt > 0f
                    ? _microCoachVoiceRequestedAt
                    : completedAt;
                RecordMicroEvent("coach_tts_completed", new
                {
                    round_index = _microCreeiController?.CurrentRoundIndex ?? 0,
                    spoken,
                    wait_milliseconds = Mathf.Max(0, Mathf.RoundToInt(
                        (voiceStarted - voiceRequested) * 1000f)),
                    speech_milliseconds = Mathf.Max(0, Mathf.RoundToInt(
                        (completedAt - voiceStarted) * 1000f))
                });
            }
            _microCoachVoiceRequestedAt = 0f;
            _microCoachVoiceStartedAt = 0f;
            completion?.Invoke(spoken);
        }

        private void StopCoachSpeech()
        {
            if ((_coachSpeechPending || _coachSpeechStarted) && coachNPC != null)
                coachNPC.InterruptCharacterSpeech();
            _coachSpeechPending = false;
            _coachSpeechStarted = false;
            _coachSpeechDeadline = 0f;
        }

        public void RequestDiagnosis(
            CreeiArgumentDiagnosisRequest request,
            Action<CreeiArgumentDiagnosisResult> onComplete)
        {
            if (_structuredDiagnosisEngine == null)
            {
                onComplete?.Invoke(new CreeiArgumentDiagnosisResult
                    { Success = false, Error = "Diagnosis service is unavailable." });
                return;
            }
            if (_diagnosisRoutine != null) StopCoroutine(_diagnosisRoutine);
            _diagnosisRoutine = StartCoroutine(_structuredDiagnosisEngine.Diagnose(request, result =>
            {
                _diagnosisRoutine = null;
                if (result?.Success == true) _successfulDiagnosisCount++;
                onComplete?.Invoke(result);
            }));
        }

        public void RequestFeedback(
            CreeiArgumentSnapshot current,
            CreeiArgumentSnapshot previous,
            CreeiComponent focus,
            CreeiComponentDiagnosis diagnosis,
            string learnerRequest,
            string previousCoachFeedback,
            CoachFeedbackPurpose purpose,
            IReadOnlyList<CoachConversationTurn> conversationHistory,
            string acceptedCriticalFeedback,
            Action<CoachFeedbackResult> onComplete)
        {
            if (_feedbackRoutine != null) StopCoroutine(_feedbackRoutine);
            _feedbackRoutine = StartCoroutine(_feedbackGenerator.GenerateFeedback(
                new CoachFeedbackRequest
                {
                    Stage = "MicroCreeiWorkbench",
                    TopicId = topicId,
                    Topic = debateTopic,
                    PlayerSide = learnerStance,
                    CurrentCreeiStage = focus.ToString(),
                    TurnId = _microCreeiController?.CurrentRoundIndex ?? 1,
                    PlayerUtteranceText = current?.GetText(focus) ?? string.Empty,
                    CurrentCreeiSnapshot = current,
                    PreviousCreeiSnapshot = previous,
                    ComponentDiagnosis = diagnosis,
                    ConfirmedFocus = focus.ToString(),
                    LearnerRequest = learnerRequest ?? string.Empty,
                    LearnerRequestIsPrimaryAgenda =
                        purpose == CoachFeedbackPurpose.LearnerSocratic &&
                        !string.IsNullOrWhiteSpace(learnerRequest),
                    Purpose = purpose,
                    ConversationHistory = conversationHistory?.ToArray() ??
                                          Array.Empty<CoachConversationTurn>(),
                    AcceptedCriticalFeedback = acceptedCriticalFeedback ?? string.Empty,
                    PreviousCoachFeedbackText = previousCoachFeedback ?? string.Empty,
                    FeedbackLevel = CoachFeedbackLevel.Level2,
                    FeedbackFormat = CoachFeedbackFormat.Scene04CreeiWorkbench,
                    DetailedJson = true
                }, result =>
                {
                    _feedbackRoutine = null;
                    onComplete?.Invoke(result);
                }));
        }

        void IMicroCreeiPracticeHost.RequestFeedback(
            CreeiArgumentSnapshot current,
            CreeiArgumentSnapshot previous,
            CreeiComponent focus,
            CreeiComponentDiagnosis diagnosis,
            string learnerRequest,
            string previousCoachFeedback,
            CoachFeedbackPurpose purpose,
            IReadOnlyList<CoachConversationTurn> conversationHistory,
            string acceptedCriticalFeedback,
            Action<CoachFeedbackResult> onComplete) =>
            RequestFeedback(
                current,
                previous,
                focus,
                diagnosis,
                learnerRequest,
                previousCoachFeedback,
                purpose,
                conversationHistory,
                acceptedCriticalFeedback,
                onComplete);

        public void SpeakAnna(string text, Action<bool> onComplete)
        {
            _microAnnaSpeechCompletion = onComplete;
            StartCoachSpeech(text);
            if (!_coachSpeechPending && !_coachSpeechStarted)
                CompleteCoachSpeechCallback(false);
        }

        public void CancelMicroOperations()
        {
            CancelNetworkActivity();
            _structuredDiagnosisEngine?.Cancel();
            StopCoachSpeech();
            StopOpponentSpeech();
            _microLeoSpeechCompletion = null;
            _microAnnaSpeechCompletion = null;
        }

        public void RecordMicroEvent(string eventType, object payload = null)
        {
            ResearchCapture.RecordEvent(eventType ?? string.Empty, "system", "learner", payload);
            JObject values = ToEventPayload(payload);
            _logger?.LogLocalEvent(new CoachEventRecord
            {
                ParticipantId = ParticipantId,
                SessionId = SessionId,
                OrchestrationMode = Mode,
                Stage = "MicroCreeiWorkbench",
                TopicId = topicId,
                PracticeCycleId = _microCreeiController?.CurrentRoundIndex ?? 0,
                EventType = eventType ?? string.Empty,
                CreeiMissingOrWeakComponents = ReadPayload(values, "creei_missing_or_weak_components"),
                CreeiGapSummary = ReadPayload(values, "creei_gap_summary"),
                AgendaSource = ReadPayload(values, "agenda_source"),
                AgendaText = ReadPayload(values, "agenda_text"),
                PolicyAction = ReadPayload(values, "feedback_purpose"),
                PolicyReason = ReadPayload(values, "policy_reason"),
                LearnerControlAction = ReadPayload(values, "learner_control_action"),
                RequestInputModality = ReadPayload(values, "request_input_modality"),
                ConfirmedLearnerText = ReadPayload(values, "learner_request"),
                CoachFeedbackType = ReadPayload(values, "feedback_purpose"),
                CoachFeedbackText = ReadPayload(values, "feedback_text"),
                CoachTurnIndex = ReadPayloadInt(values, "coach_turn_index"),
                PolicyVersion = PolicyVersion,
                DiagnosisModelVersion = DiagnosisModelVersion,
                FeedbackModelVersion = _policyConfig?.FeedbackModelVersion ?? "coach-feedback-v4"
            });
        }

        private static JObject ToEventPayload(object payload)
        {
            if (payload == null) return new JObject();
            try
            {
                return JObject.FromObject(payload);
            }
            catch (JsonException)
            {
                return new JObject();
            }
        }

        private static string ReadPayload(JObject payload, string key)
        {
            JToken value = payload?[key];
            if (value == null || value.Type == JTokenType.Null) return string.Empty;
            return value.Type is JTokenType.Array or JTokenType.Object
                ? value.ToString(Formatting.None)
                : value.ToString();
        }

        private static int ReadPayloadInt(JObject payload, string key)
        {
            JToken value = payload?[key];
            return value != null && int.TryParse(value.ToString(), out int parsed) ? parsed : 0;
        }

        public void RecordMicroTechnicalFailure(
            string operation,
            string message,
            int retryCount = 0,
            bool recovered = false)
        {
            string safeOperation = string.IsNullOrWhiteSpace(operation)
                ? "UNKNOWN"
                : operation.Trim().ToUpperInvariant();
            ResearchCapture.RecordTechnicalFailure(
                "scene04_workbench_failure",
                "CREEI_" + safeOperation + "_FAILED",
                message ?? "Workbench operation failed.",
                retryCount,
                recovered);
        }

        public void AdvanceFromMicroCreei(int completedRounds, bool timedOut)
        {
            _microCreeiSnapshotCount = _microCreeiController?.SnapshotCount ?? 0;
            _microCreeiCompletedRoundCount = completedRounds;
            RecordMicroEvent("PracticeOneCompleted", new
            {
                committed_snapshot_count = _microCreeiSnapshotCount,
                completed_round_count = completedRounds,
                timed_out = timedOut
            });
            studyView.SetRootVisible(true);
            EnterStage(DebatePracticeStage.FullSpeechWithFeedback);
        }

        private void ShowCurrentPractice(string overrideStatus = null, bool canRestartRecording = false,
            bool diagnosisFailed = false)
        {
            string context = string.Empty;
            string status = overrideStatus;
            if (status == null && CurrentStage == DebatePracticeStage.MicroPractice && _microLoop != null)
            {
                status = _microLoop.Step switch
                {
                    MicroChallengeStep.InitialStatement =>
                        "Give your opening position. Press T to start or stop. The transcript is accepted automatically.",
                    MicroChallengeStep.GeneratingChallenge =>
                        $"Leo is preparing challenge {_microLoop.CycleIndex}/2.",
                    MicroChallengeStep.OpponentSpeaking =>
                        "Listen to Leo's challenge. Coach Anna's focus and advice will follow immediately.",
                    MicroChallengeStep.IndependentResponse =>
                        "Answer Leo independently. Anna remains silent. Press T to start or stop.",
                    MicroChallengeStep.Diagnosing =>
                        "Analyzing your current position against Leo's challenge for Coach focus and advice.",
                    MicroChallengeStep.CoachInteraction =>
                        "Coach support is now controlled by your assigned experimental condition.",
                    MicroChallengeStep.RevisionResponse =>
                        "Revise your answer to Leo. Press T to start or stop.",
                    MicroChallengeStep.EvaluatingRevision =>
                        "Comparing your revision with your independent answer.",
                    _ => "Practice 1 is complete."
                };
                if (!string.IsNullOrWhiteSpace(_currentChallenge))
                {
                    context = $"Leo - Cycle {_microLoop.CycleIndex}/2 - {_microLoop.Difficulty}\n" +
                              _currentChallenge;
                    if (_microLoop.Step == MicroChallengeStep.RevisionResponse &&
                        !string.IsNullOrWhiteSpace(_independentAnswer))
                        context += "\n\nResponse reviewed by Coach:\n" + _independentAnswer;
                }
            }
            status ??= CurrentStage switch
            {
                DebatePracticeStage.MicroPractice =>
                    "Give your opening position. Press T to start or stop.",
                DebatePracticeStage.FullSpeechWithFeedback =>
                    "Press T and deliver one complete 90-180 second argument. Coach feedback follows automatically.",
                DebatePracticeStage.RevisionSpeech =>
                    "Deliver a second complete 90-180 second argument. Anna remains silent during this assessment.",
                _ => string.Empty
            };
            string transcript = Phase is DebatePracticePhase.TechnicalError or
                DebatePracticePhase.Recording or DebatePracticePhase.AwaitingTranscript
                ? _liveTranscript.ToString()
                : string.Empty;
            bool restart = canRestartRecording ||
                           (Phase == DebatePracticePhase.Recording &&
                            _voiceCaptureTarget == DebateVoiceCaptureTarget.PracticeSpeech) ||
                           (Phase == DebatePracticePhase.TechnicalError &&
                            _technicalOperation == TechnicalOperation.None);
            studyView.ShowPractice(CurrentStage, _stageElapsed, RecordingElapsedSeconds, status,
                transcript, restart, diagnosisFailed, context);
        }

        private void CompleteStudy()
        {
            Phase = DebatePracticePhase.Complete;
            _studyStarted = false;
            _logger?.Flush();
            CancelNetworkActivity();
            StopCoachSpeech();
            StopOpponentSpeech();
            if (opponentNPC != null) opponentNPC.gameObject.SetActive(false);
            ResetVoiceCaptureState(true);
            RestorePlayer();
            ResearchSceneCompletionStatus completion =
                ResearchSceneCompletionGate.EvaluateCreeiWorkbenchPractice(
                _microCreeiSnapshotCount,
                _microCreeiCompletedRoundCount,
                !string.IsNullOrWhiteSpace(_fullSpeechResponseId),
                !string.IsNullOrWhiteSpace(_revisionSpeechResponseId),
                _researchConfirmedTranscriptCount,
                _researchAudioArtifactCount,
                _successfulDiagnosisCount);
            ResearchCapture.RecordEvent(
                "practice_completed",
                "system",
                payload: new
                {
                    condition = Mode.ToString(),
                    micro_creei_snapshot_count = _microCreeiSnapshotCount,
                    micro_creei_rounds_completed = _microCreeiCompletedRoundCount,
                    full_speech_completed = !string.IsNullOrWhiteSpace(_fullSpeechResponseId),
                    revision_speech_completed = !string.IsNullOrWhiteSpace(_revisionSpeechResponseId),
                    confirmed_transcript_count = _researchConfirmedTranscriptCount,
                    audio_artifact_count = _researchAudioArtifactCount,
                    successful_diagnosis_count = _successfulDiagnosisCount,
                    coach_episode_count = _coachEpisodeCount,
                    total_coach_seconds = _totalCoachSeconds,
                    completion_status = completion.DataComplete ? "completed" : "incomplete",
                    missing_required_data = completion.MissingRequiredData
                });
            ResearchCapture.CompleteScene(
                completion.DataComplete ? "completed" : "incomplete",
                completion.MissingRequiredData);
            if (!completion.DataComplete)
                Debug.LogError("Scene 04 research completion gate failed: " + completion.MissingRequiredData);
            _completionDataComplete = completion.DataComplete;
            studyView.ShowCompletion(completion.DataComplete, completion.MissingRequiredData);
            SetCursor(true);
        }

        private void ContinueToTransferScene()
        {
            if (!loadTransferSceneOnCompletion || !_completionDataComplete)
            {
                return;
            }

            ResearchStudyFlowNavigator.TryLoadNextScene("04", true);
        }

        private string GetResearchResponseRole()
        {
            if (CurrentStage == DebatePracticeStage.FullSpeechWithFeedback)
                return "full_speech";
            if (CurrentStage == DebatePracticeStage.RevisionSpeech)
                return "revision_speech";
            return _microLoop?.Step switch
            {
                MicroChallengeStep.InitialStatement => "micro_initial_statement",
                MicroChallengeStep.IndependentResponse => "micro_independent_response",
                MicroChallengeStep.RevisionResponse => "micro_revision",
                _ => "micro_practice"
            };
        }

        private string GetParentResponseId(string responseRole)
        {
            if (responseRole == "revision_speech") return _fullSpeechResponseId;
            if (responseRole != "micro_revision" || _microLoop == null) return string.Empty;
            return _microIndependentResponseIds.TryGetValue(_microLoop.CycleIndex, out string value)
                ? value
                : string.Empty;
        }

        private void RememberResponseId(string responseRole, string responseId)
        {
            if (string.IsNullOrWhiteSpace(responseId)) return;
            if (responseRole == "full_speech")
            {
                _fullSpeechResponseId = responseId;
                return;
            }
            if (responseRole == "revision_speech")
            {
                _revisionSpeechResponseId = responseId;
                return;
            }
            if (responseRole == "micro_initial_statement")
            {
                _microInitialResponseId = responseId;
                return;
            }
            if (responseRole == "micro_independent_response" && _microLoop != null)
                _microIndependentResponseIds[_microLoop.CycleIndex] = responseId;
            if (responseRole == "micro_revision" && _microLoop != null)
                _microRevisionResponseIds[_microLoop.CycleIndex] = responseId;
        }

        private string GetDiagnosisTargetResponseId(bool microRevision)
        {
            if (CurrentStage == DebatePracticeStage.FullSpeechWithFeedback)
                return _fullSpeechResponseId;
            if (CurrentStage == DebatePracticeStage.RevisionSpeech)
                return _revisionSpeechResponseId;
            if (_microLoop == null) return string.Empty;
            Dictionary<int, string> source = microRevision
                ? _microRevisionResponseIds
                : _microIndependentResponseIds;
            return source.TryGetValue(_microLoop.CycleIndex, out string responseId)
                ? responseId
                : string.Empty;
        }

        private void LogConfirmedTranscript()
        {
            CoachStudySessionSnapshot session = CoachStudySessionContext.Current;
            _logger?.LogEvent(new CoachEventRecord
            {
                ParticipantId = session?.ParticipantId ?? string.Empty,
                SessionId = session?.SessionId ?? string.Empty,
                OrchestrationMode = Mode,
                Stage = CurrentStage.ToString(),
                TopicId = topicId,
                PracticeCycleId = ThreeStageDebatePracticeRules.GetStageNumber(CurrentStage),
                TurnId = _attemptIndex,
                EventType = "TranscriptConfirmed",
                ConfirmedLearnerText = ConfirmedTranscript,
                LearnerControlAction = "ConfirmTranscript",
                OpponentUtteranceText = CurrentStage == DebatePracticeStage.MicroPractice
                    ? _currentChallenge
                    : string.Empty,
                ChallengeCycleIndex = CurrentStage == DebatePracticeStage.MicroPractice
                    ? _microLoop?.CycleIndex ?? 0
                    : 0,
                ChallengeDifficulty = CurrentStage == DebatePracticeStage.MicroPractice && _microLoop != null
                    ? _microLoop.Difficulty.ToString()
                    : string.Empty,
                PolicyVersion = _policyConfig.PolicyVersion,
                DiagnosisModelVersion = _policyConfig.DiagnosisModelVersion,
                FeedbackModelVersion = _policyConfig.FeedbackModelVersion
            });
        }

        private void LogMicroEvent(string eventType, string learnerText = "",
            CoachDiagnosisResult diagnosis = null)
        {
            CoachStudySessionSnapshot session = CoachStudySessionContext.Current;
            _logger?.LogEvent(new CoachEventRecord
            {
                ParticipantId = session?.ParticipantId ?? string.Empty,
                SessionId = session?.SessionId ?? string.Empty,
                OrchestrationMode = Mode,
                Stage = "MicroChallenge",
                TopicId = topicId,
                PracticeCycleId = _microLoop?.CycleIndex ?? 0,
                TurnId = _attemptIndex,
                EventType = eventType ?? string.Empty,
                DiagnosisIssueCode = diagnosis?.DiagnosisIssueCode ?? string.Empty,
                DiagnosisSeverity = diagnosis?.Severity ?? 0,
                DiagnosisConfidence = diagnosis?.Confidence ?? 0f,
                CreeiMissingOrWeakComponents = string.Join("|",
                    diagnosis?.CreeiMissingOrWeakComponents ?? Array.Empty<string>()),
                CreeiGapSummary = diagnosis?.CreeiGapSummary ?? string.Empty,
                ConfirmedLearnerText = learnerText ?? string.Empty,
                OpponentUtteranceText = _currentChallenge,
                ChallengeCycleIndex = _microLoop?.CycleIndex ?? 0,
                ChallengeDifficulty = _microLoop?.Difficulty.ToString() ?? string.Empty,
                ProposedFocus = _currentAttackFocus,
                RevisionImprovementStatus = diagnosis?.RevisionImprovementStatus ?? string.Empty,
                RevisionImprovementSummary = diagnosis?.RevisionImprovementSummary ?? string.Empty,
                PolicyVersion = _policyConfig?.PolicyVersion ?? string.Empty,
                DiagnosisModelVersion = diagnosis?.ModelVersion ??
                                        _policyConfig?.DiagnosisModelVersion ?? string.Empty,
                FeedbackModelVersion = _policyConfig?.FeedbackModelVersion ?? string.Empty
            });
        }

        private void RecordConditionControlEvent(
            string eventType,
            CoachLearnerAction action = CoachLearnerAction.None,
            string requestText = "",
            string inputModality = "",
            CoachAgendaSource agendaSource = CoachAgendaSource.None,
            string agendaText = "")
        {
            ControlOwner startAuthority;
            ControlOwner agendaOwner;
            ControlOwner pacingOwner;
            ControlOwner terminationOwner;
            switch (Mode)
            {
                case CoachOrchestrationMode.AiLed:
                    startAuthority = ControlOwner.Coach;
                    agendaOwner = ControlOwner.Coach;
                    pacingOwner = ControlOwner.Coach;
                    terminationOwner = ControlOwner.Coach;
                    break;
                case CoachOrchestrationMode.SharedControl:
                    startAuthority = ControlOwner.Shared;
                    agendaOwner = action is CoachLearnerAction.RequestCoach or
                        CoachLearnerAction.ChangeFocus
                        ? ControlOwner.Learner
                        : ControlOwner.Shared;
                    pacingOwner = ControlOwner.Shared;
                    terminationOwner = ControlOwner.Learner;
                    break;
                default:
                    startAuthority = ControlOwner.Learner;
                    agendaOwner = ControlOwner.Learner;
                    pacingOwner = ControlOwner.Learner;
                    terminationOwner = ControlOwner.Learner;
                    break;
            }

            int latencyMilliseconds = _coachOpportunityPresentedAt > 0f
                ? Mathf.Max(0, Mathf.RoundToInt(
                    (Time.realtimeSinceStartup - _coachOpportunityPresentedAt) * 1000f))
                : 0;
            CoachStudySessionSnapshot session = CoachStudySessionContext.Current;
            _logger?.LogEvent(new CoachEventRecord
            {
                ParticipantId = session?.ParticipantId ?? string.Empty,
                SessionId = session?.SessionId ?? string.Empty,
                OrchestrationMode = Mode,
                Stage = CurrentStage.ToString(),
                TopicId = topicId,
                PracticeCycleId = episodeController?.PracticeCycleId ??
                    ThreeStageDebatePracticeRules.GetStageNumber(CurrentStage),
                TurnId = _attemptIndex,
                CoachEpisodeId = episodeController?.CoachEpisodeId ?? string.Empty,
                EpisodeStateBefore = episodeController?.State ?? CoachEpisodeState.Inactive,
                EpisodeStateAfter = episodeController?.State ?? CoachEpisodeState.Inactive,
                EventType = eventType ?? string.Empty,
                DiagnosisIssueCode = _latestDiagnosis?.DiagnosisIssueCode ?? string.Empty,
                DiagnosisSeverity = _latestDiagnosis?.Severity ?? 0,
                DiagnosisConfidence = _latestDiagnosis?.Confidence ?? 0f,
                CreeiMissingOrWeakComponents = string.Join("|",
                    _latestDiagnosis?.CreeiMissingOrWeakComponents ?? Array.Empty<string>()),
                CreeiGapSummary = _latestDiagnosis?.CreeiGapSummary ?? string.Empty,
                AgendaSource = agendaSource == CoachAgendaSource.None
                    ? studyView?.CurrentCoachAgendaSource.ToString() ?? string.Empty
                    : agendaSource.ToString(),
                AgendaText = string.IsNullOrWhiteSpace(agendaText)
                    ? studyView?.CoachAgendaText ?? string.Empty
                    : agendaText.Trim(),
                PolicyAction = action == CoachLearnerAction.None ? string.Empty : action.ToString(),
                PolicyReason = eventType ?? string.Empty,
                ProposedFocus = _latestDiagnosis?.RecommendedFocus ?? string.Empty,
                ConfirmedFocus = episodeController?.ConfirmedFocus ?? string.Empty,
                StartAuthority = startAuthority,
                AgendaOwner = agendaOwner,
                PacingOwner = pacingOwner,
                TerminationOwner = terminationOwner,
                LearnerControlAction = action == CoachLearnerAction.None
                    ? string.Empty
                    : action.ToString(),
                RequestInputModality = inputModality ?? string.Empty,
                DecisionLatencyMilliseconds = latencyMilliseconds,
                ConfirmedLearnerText = string.IsNullOrWhiteSpace(requestText)
                    ? ConfirmedTranscript
                    : requestText.Trim(),
                OpponentUtteranceText = _currentChallenge,
                ChallengeCycleIndex = CurrentStage == DebatePracticeStage.MicroPractice
                    ? _microLoop?.CycleIndex ?? 0
                    : 0,
                ChallengeDifficulty = CurrentStage == DebatePracticeStage.MicroPractice
                    ? _microLoop?.Difficulty.ToString() ?? string.Empty
                    : string.Empty,
                CoachTurnIndex = episodeController?.FeedbackTurnIndex ?? 0,
                ElapsedEpisodeSeconds = episodeController?.ElapsedSeconds ?? 0f,
                PolicyVersion = _policyConfig?.PolicyVersion ?? string.Empty,
                DiagnosisModelVersion = _latestDiagnosis?.ModelVersion ??
                    _policyConfig?.DiagnosisModelVersion ?? string.Empty,
                FeedbackModelVersion = _policyConfig?.FeedbackModelVersion ?? string.Empty
            });
        }

        private void RecordPresentedAgendaIfVisible()
        {
            if (studyView == null || !studyView.IsCoachAgendaVisible) return;
            RecordConditionControlEvent(
                "coach_agenda_presented",
                agendaSource: studyView.CurrentCoachAgendaSource,
                agendaText: studyView.CoachAgendaText);
        }

        private void LogSilentRevisionAssessment()
        {
            CoachStudySessionSnapshot session = CoachStudySessionContext.Current;
            _logger?.LogEvent(new CoachEventRecord
            {
                ParticipantId = session?.ParticipantId ?? string.Empty,
                SessionId = session?.SessionId ?? string.Empty,
                OrchestrationMode = Mode,
                Stage = CurrentStage.ToString(),
                TopicId = topicId,
                PracticeCycleId = 3,
                TurnId = _attemptIndex,
                EventType = "SilentRevisionDiagnosisCompleted",
                DiagnosisIssueCode = _latestDiagnosis?.DiagnosisIssueCode ?? string.Empty,
                DiagnosisSeverity = _latestDiagnosis?.Severity ?? 0,
                DiagnosisConfidence = _latestDiagnosis?.Confidence ?? 0f,
                CreeiMissingOrWeakComponents = string.Join("|",
                    _latestDiagnosis?.CreeiMissingOrWeakComponents ?? Array.Empty<string>()),
                CreeiGapSummary = _latestDiagnosis?.CreeiGapSummary ?? string.Empty,
                ConfirmedLearnerText = ConfirmedTranscript,
                PolicyReason = "VisibleCoachDisabledForRevisionAssessment",
                PolicyVersion = _policyConfig.PolicyVersion,
                DiagnosisModelVersion = _policyConfig.DiagnosisModelVersion,
                FeedbackModelVersion = _policyConfig.FeedbackModelVersion
            });
        }

        private void Subscribe()
        {
            studyView.SessionStartRequested += BeginStudy;
            studyView.IntroductionCompleted += BeginPractice;
            studyView.ContinueRequested += ContinueToTransferScene;
            studyView.RerecordRequested += Rerecord;
            studyView.RetryDiagnosisRequested += RetryDiagnosis;
            studyView.SkipRequested += TechnicalSkip;
            studyView.LearnerActionRequested += HandleLearnerAction;
            if (realtimeTranscriber != null)
            {
                realtimeTranscriber.SessionStarted += HandleTranscriptionStarted;
                realtimeTranscriber.TranscriptUpdated += HandleTranscriptUpdated;
                realtimeTranscriber.SessionCompleted += HandleTranscriptionCompleted;
                realtimeTranscriber.SessionFailed += HandleTranscriptionFailed;
            }
            episodeController.DecisionMade += HandlePolicyDecision;
            episodeController.EpisodeEnded += HandleEpisodeEnded;
        }

        private void Unsubscribe()
        {
            if (studyView != null)
            {
                studyView.SessionStartRequested -= BeginStudy;
                studyView.IntroductionCompleted -= BeginPractice;
                studyView.ContinueRequested -= ContinueToTransferScene;
                studyView.RerecordRequested -= Rerecord;
                studyView.RetryDiagnosisRequested -= RetryDiagnosis;
                studyView.SkipRequested -= TechnicalSkip;
                studyView.LearnerActionRequested -= HandleLearnerAction;
            }
            if (realtimeTranscriber != null)
            {
                realtimeTranscriber.SessionStarted -= HandleTranscriptionStarted;
                realtimeTranscriber.TranscriptUpdated -= HandleTranscriptUpdated;
                realtimeTranscriber.SessionCompleted -= HandleTranscriptionCompleted;
                realtimeTranscriber.SessionFailed -= HandleTranscriptionFailed;
            }
            if (episodeController != null)
            {
                episodeController.DecisionMade -= HandlePolicyDecision;
                episodeController.EpisodeEnded -= HandleEpisodeEnded;
            }
        }

        private void RegisterInputRouting()
        {
            ConvaiInputManager.ShouldUseTapToTalk = () => _studyStarted;
            ConvaiInputManager.TapToTalkRequested = ToggleRecording;
            ConvaiInputManager.ShouldSuppressTalkInput = () => _studyStarted || Phase == DebatePracticePhase.Setup;
            ConvaiPlayerInteractionManager.ShouldSuppressTalkInput =
                () => _studyStarted || Phase == DebatePracticePhase.Setup;
        }

        private void UnregisterInputRouting()
        {
            if (ConvaiInputManager.TapToTalkRequested == (Action)ToggleRecording)
                ConvaiInputManager.TapToTalkRequested = null;
            ConvaiInputManager.ShouldUseTapToTalk = null;
            ConvaiInputManager.ShouldSuppressTalkInput = null;
            ConvaiPlayerInteractionManager.ShouldSuppressTalkInput = null;
        }

        private void FreezePlayer()
        {
            _movementComponents = FindObjectsByType<ConvaiPlayerMovement>(FindObjectsInactive.Include);
            _movementStates = new bool[_movementComponents.Length];
            for (int i = 0; i < _movementComponents.Length; i++)
            {
                _movementStates[i] = _movementComponents[i] != null && _movementComponents[i].enabled;
                if (_movementComponents[i] != null) _movementComponents[i].enabled = false;
            }
        }

        private void RestorePlayer()
        {
            for (int i = 0; i < _movementComponents.Length; i++)
                if (_movementComponents[i] != null && i < _movementStates.Length)
                    _movementComponents[i].enabled = _movementStates[i];
        }

        private void SetCursor(bool visible)
        {
            Cursor.visible = visible;
            Cursor.lockState = visible ? CursorLockMode.None : CursorLockMode.Locked;
        }

        private void CancelNetworkActivity()
        {
            if (_restartRecordingRoutine != null)
            {
                StopCoroutine(_restartRecordingRoutine);
                _restartRecordingRoutine = null;
            }
            if (_challengeRoutine != null)
            {
                StopCoroutine(_challengeRoutine);
                _challengeRoutine = null;
            }
            _challengeGenerator?.Cancel();
            if (_diagnosisRoutine != null)
            {
                StopCoroutine(_diagnosisRoutine);
                _diagnosisRoutine = null;
            }
            _diagnosisEngine?.Cancel();
            if (_feedbackRoutine != null)
            {
                StopCoroutine(_feedbackRoutine);
                _feedbackRoutine = null;
            }
            _feedbackGenerator?.Cancel();
        }

        private void ResetVoiceCaptureState(bool cancelSession)
        {
            if (cancelSession) realtimeTranscriber?.CancelSession();
            _recording = false;
            _voiceCaptureTarget = DebateVoiceCaptureTarget.None;
            _learnerRequestVoicePrefix = string.Empty;
            _liveTranscript.Clear();
            studyView?.SetLearnerRequestVoiceState(false);
        }

        private void Cleanup()
        {
            if (_cleanedUp || !Application.isPlaying) return;
            _cleanedUp = true;
            studyView?.HideCoachAgenda();
            CancelNetworkActivity();
            ResetVoiceCaptureState(true);
            StopFixedStageIntroductionSpeech(true);
            StopCoachSpeech();
            StopOpponentSpeech();
            if (opponentNPC != null) opponentNPC.gameObject.SetActive(false);
            Unsubscribe();
            UnregisterInputRouting();
            _logger?.Flush();
            RestorePlayer();
            Cursor.lockState = _originalCursorLockMode;
            Cursor.visible = _originalCursorVisible;
        }
    }
}
