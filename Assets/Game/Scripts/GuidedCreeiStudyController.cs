using System;
using System.Collections;
using System.Text;
using Convai.Scripts.Runtime.Addons;
using Convai.Scripts.Runtime.Core;
using Convai.Scripts.Runtime.Features;
using Convai.Scripts.Runtime.UI;
using Convai.Scripts.Runtime.Utils;
using Newtonsoft.Json;
using Service;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Game.Debate
{
    public enum CoachSpeechLifecycleDecision
    {
        None,
        Start,
        Finish,
        Timeout,
        InterruptForLearnerMic
    }

    public enum CoachRecordingStartDecision
    {
        StartRecording,
        InterruptStaleCoachThenRecord,
        BlockForCurrentCoachFeedback
    }

    public sealed class GuidedCreeiStudyController : MonoBehaviour
    {
        public const float IntegratedMinimumSeconds = 60f;
        public const float IntegratedMaximumSeconds = 90f;
        public const string PolicyVersion = "guided-creei-policy-v1";

        private static readonly GuidedPracticeStageKind[] StageSequence =
        {
            GuidedPracticeStageKind.Claim,
            GuidedPracticeStageKind.Reason,
            GuidedPracticeStageKind.Evidence,
            GuidedPracticeStageKind.Explanation,
            GuidedPracticeStageKind.Impact,
            GuidedPracticeStageKind.IntegratedPracticeOne,
            GuidedPracticeStageKind.IntegratedPracticeTwo
        };

        private enum TechnicalOperation
        {
            None,
            Transcription,
            Diagnosis,
            Evaluation,
            Feedback,
            SilentPostTest
        }

        [Header("Study")]
        [SerializeField] private string topicId = "reading_vs_speaking";
        [SerializeField] private string debateTopic =
            "Reading and speaking, which is more important in learning English?";
        [SerializeField] private string learnerStance =
            "Speaking is more important for learning English.";
        [SerializeField] private string transferSceneName = "05Level_PlayerVsNPCDebate 1";

        [Header("Scene References")]
        [SerializeField] private Canvas uiCanvas;
        [SerializeField] private ConvaiNPC coachNPC;
        [SerializeField] private XfyunRealtimeTranscriber realtimeTranscriber;
        [SerializeField] private GuidedCreeiStudyView studyView;
        [SerializeField] private GameObject legacyInteractiveControls;
        [SerializeField] private GameObject legacyStartButton;
        [SerializeField] private GameObject legacyRoundTimer;

        [Header("Coach Services")]
        [SerializeField] private string openAIModel = "gpt-4o-mini";
        [SerializeField] private string openAIBaseUrl = "https://api.meding.site/v1/chat/completions";
        [SerializeField] private string openAIApiKeyOverride = string.Empty;
        [SerializeField, Min(5f)] private float responseTimeoutSeconds = 30f;
        [SerializeField] private bool speakCoachFeedback = true;

        [Header("Study Presentation")]
        [SerializeField] private bool applyStudyWorldLayout = true;
        [SerializeField] private Vector3 coachStudyWorldPosition = new(1.1f, 0f, 2.33f);
        [SerializeField, Min(0f)] private float learnerMoveTowardCoachMeters = 1.25f;

        private readonly string[] _confirmedStageTexts = new string[5];
        private readonly StringBuilder _liveTranscript = new();
        private GuidedCoachPolicy _policy;
        private ICoachDiagnosisEngine _diagnosisEngine;
        private ICoachStageEvaluator _stageEvaluator;
        private DebateCoachFeedbackGenerator _feedbackGenerator;
        private GuidedCoachResearchLogger _logger;
        private CoachDiagnosisResult _latestDiagnosis;
        private CoachStageEvaluationResult _latestEvaluation;
        private CoachSuggestion[] _currentSuggestions = Array.Empty<CoachSuggestion>();
        private Coroutine _networkRoutine;
        private bool _revisionPending;
        private bool _recording;
        private bool _integratedFallbackTimerActive;
        private bool _advanceAfterFeedback;
        private bool _studyStarted;
        private bool _sceneLoadRequested;
        private bool _coachSpeechPending;
        private bool _coachSpeechStarted;
        private float _coachSpeechDeadline;
        private float _recordingStartedAt;
        private float _recordingDuration;
        private string _learnerRequest = string.Empty;
        private string _selectedSuggestionId = string.Empty;
        private string _suggestionModification = string.Empty;
        private string _confirmedFocus = string.Empty;
        private string _initialConfirmedText = string.Empty;
        private string _previousConfirmedAttempt = string.Empty;
        private string _latestFeedback = string.Empty;
        private string _stageStartedAt = string.Empty;
        private TechnicalOperation _technicalOperation;
        private int _stageGeneration;
        private CoachTerminationReason _normalTerminationReason;
        private ConvaiPlayerMovement[] _movementComponents = Array.Empty<ConvaiPlayerMovement>();
        private bool[] _movementOriginalStates = Array.Empty<bool>();
        private bool _movementFrozen;
        private CursorLockMode _originalCursorLockMode;
        private bool _originalCursorVisible;

        public CoachOrchestrationMode Mode { get; private set; }
        public GuidedPracticeStageKind CurrentStage { get; private set; } = GuidedPracticeStageKind.Claim;
        public GuidedCreeiStudyPhase Phase { get; private set; } = GuidedCreeiStudyPhase.Setup;
        public int StageIndex { get; private set; }
        public int AttemptIndex { get; private set; }
        public int RevisionIndex { get; private set; }
        public int VisibleFeedbackTurns { get; private set; }
        public bool CriterionMet { get; private set; }
        public string ConfirmedTranscript { get; private set; } = string.Empty;
        public float RecordingDuration => _recordingDuration;
        public bool IsIntegratedStage => CurrentStage is
            GuidedPracticeStageKind.IntegratedPracticeOne or GuidedPracticeStageKind.IntegratedPracticeTwo;
        public CoachSuggestion[] CurrentSuggestions => _currentSuggestions;
        public string ConfirmedFocus => _confirmedFocus;
        private GuidedCoachPolicy Policy => _policy ??= new GuidedCoachPolicy();

        public static bool CanSubmitIntegrated(float seconds) => seconds >= IntegratedMinimumSeconds;
        public static bool ShouldAutoStopIntegrated(float seconds) => seconds >= IntegratedMaximumSeconds;
        public static string BuildCoachSpeechPrompt(string feedback)
        {
            if (string.IsNullOrWhiteSpace(feedback)) return string.Empty;
            return JsonConvert.SerializeObject(new
            {
                instruction =
                    "Anna must speak the exact English Coach feedback in feedback_text once. " +
                    "Do not paraphrase it or add any additional words.",
                feedback_text = feedback.Trim()
            });
        }
        public static bool CanStartRecording(bool speechPending, bool characterTalking) =>
            !speechPending && !characterTalking;
        public static bool CanStartRecording(
            bool speechPending,
            bool characterTalking,
            bool queuedAudio) =>
            !speechPending && !characterTalking && !queuedAudio;

        public static CoachRecordingStartDecision EvaluateRecordingStart(
            bool ownedCoachSpeech,
            bool characterTalking,
            bool queuedAudio)
        {
            if (ownedCoachSpeech)
                return CoachRecordingStartDecision.BlockForCurrentCoachFeedback;
            if (characterTalking || queuedAudio)
                return CoachRecordingStartDecision.InterruptStaleCoachThenRecord;
            return CoachRecordingStartDecision.StartRecording;
        }

        public static float GetCoachSpeechStartTimeoutSeconds(float serviceTimeoutSeconds) =>
            Mathf.Max(20f, serviceTimeoutSeconds + 8f);

        public static Vector3 CalculateCloserLearnerPosition(
            Vector3 learnerPosition,
            Vector3 coachPosition,
            float moveMeters)
        {
            Vector2 learnerGround = new(learnerPosition.x, learnerPosition.z);
            Vector2 coachGround = new(coachPosition.x, coachPosition.z);
            Vector2 movedGround = Vector2.MoveTowards(
                learnerGround,
                coachGround,
                Mathf.Max(0f, moveMeters));
            return new Vector3(movedGround.x, learnerPosition.y, movedGround.y);
        }

        public static CoachSpeechLifecycleDecision EvaluateCoachSpeechLifecycle(
            bool ownedPending,
            bool ownedStarted,
            bool characterTalking,
            bool queuedAudio,
            bool learnerMicActive,
            bool deadlineElapsed)
        {
            if (learnerMicActive && (characterTalking || queuedAudio))
                return CoachSpeechLifecycleDecision.InterruptForLearnerMic;
            if (ownedPending && characterTalking)
                return CoachSpeechLifecycleDecision.Start;
            if (ownedPending && deadlineElapsed && !queuedAudio)
                return CoachSpeechLifecycleDecision.Timeout;
            if (ownedStarted && !characterTalking && !queuedAudio)
                return CoachSpeechLifecycleDecision.Finish;
            return CoachSpeechLifecycleDecision.None;
        }

        private void Awake()
        {
            _originalCursorLockMode = Cursor.lockState;
            _originalCursorVisible = Cursor.visible;
            _policy = new GuidedCoachPolicy();
            _diagnosisEngine = new CoachDiagnosisEngine(
                openAIModel, responseTimeoutSeconds, openAIBaseUrl, openAIApiKeyOverride);
            _stageEvaluator = new CoachStageEvaluator(
                openAIModel, responseTimeoutSeconds, openAIBaseUrl, openAIApiKeyOverride);
            _feedbackGenerator = new DebateCoachFeedbackGenerator(
                openAIModel, responseTimeoutSeconds, openAIBaseUrl, openAIApiKeyOverride);
            if (!Application.isPlaying) return;
            _logger = new GuidedCoachResearchLogger();
            HideLegacyUi();
            DisableCoachNpcToNpcFlow();
            if (studyView == null) studyView = GetComponent<GuidedCreeiStudyView>();
            if (studyView == null) studyView = gameObject.AddComponent<GuidedCreeiStudyView>();
            studyView.Build(uiCanvas);
            SubscribeView();
            SubscribeTranscriber();
            RegisterInputRouting();
            FreezePlayerMovement();
            ApplyStudyWorldLayout();
            SetCursor(true);
        }

        private void Update()
        {
            UpdateCoachSpeechState();
            if (!_recording && !_integratedFallbackTimerActive) return;
            _recordingDuration = Mathf.Max(0f, Time.realtimeSinceStartup - _recordingStartedAt);
            if (_recording)
            {
                studyView?.ShowRecording(_liveTranscript.ToString(), _recordingDuration, IsIntegratedStage);
                if (IsIntegratedStage && ShouldAutoStopIntegrated(_recordingDuration)) StopRecording();
            }
            else
            {
                studyView?.SetTimer(_recordingDuration, true);
                if (_recordingDuration >= IntegratedMaximumSeconds)
                    _recordingDuration = IntegratedMaximumSeconds;
            }
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
            if (string.IsNullOrWhiteSpace(participantId) || mode == CoachOrchestrationMode.Disabled) return;

            if (Application.isPlaying)
            {
                CoachStudySessionContext.Initialize(participantId, mode, debateTopic, debateTopic);
            }
            Mode = mode;
            StageIndex = 0;
            CurrentStage = StageSequence[0];
            _studyStarted = true;
            Array.Clear(_confirmedStageTexts, 0, _confirmedStageTexts.Length);
            ResetStageState();
            LogEvent("StudyStarted");
            ShowReadyStage();
        }

        public void SubmitConfirmedTranscript(string text)
        {
            string confirmed = text?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(confirmed) || Phase == GuidedCreeiStudyPhase.Complete) return;
            if (IsIntegratedStage && !CanSubmitIntegrated(_recordingDuration))
            {
                ShowTechnicalError(
                    "Integrated practice requires 60 seconds of speaking time before a fallback transcript can be confirmed.",
                    TechnicalOperation.Transcription,
                    true);
                return;
            }

            ConfirmedTranscript = confirmed;
            _integratedFallbackTimerActive = false;
            AttemptIndex++;
            if (string.IsNullOrWhiteSpace(_initialConfirmedText)) _initialConfirmedText = confirmed;
            if (_revisionPending) RevisionIndex++;
            if (IsGuidedCreeiStage(CurrentStage)) _confirmedStageTexts[StageIndex] = confirmed;
            LogEvent("TranscriptConfirmed");

            if (CurrentStage == GuidedPracticeStageKind.IntegratedPracticeTwo)
            {
                Phase = GuidedCreeiStudyPhase.Evaluating;
                if (Application.isPlaying) StartDiagnosis(true, false);
                return;
            }

            GuidedCoachPolicyDecision decision = Policy.Evaluate(new GuidedCoachPolicyInput
            {
                Mode = Mode,
                StageKind = CurrentStage,
                VisibleFeedbackTurns = VisibleFeedbackTurns,
                TranscriptConfirmed = true,
                IsRevision = _revisionPending
            });
            if (decision.ShowLearnerRequest)
            {
                Phase = GuidedCreeiStudyPhase.AwaitingCoachChoice;
                studyView?.ShowLearnerChoice(_latestFeedback, VisibleFeedbackTurns < 2, true, true);
            }
            else if (Mode == CoachOrchestrationMode.SharedControl)
            {
                Phase = GuidedCreeiStudyPhase.Diagnosing;
                if (Application.isPlaying) StartDiagnosis(false, _revisionPending);
            }
            else if (decision.RunEvaluation && IsGuidedCreeiStage(CurrentStage))
            {
                Phase = GuidedCreeiStudyPhase.Evaluating;
                if (Application.isPlaying) StartStageEvaluation();
            }
            else if (decision.AutoGenerateFeedback)
            {
                Phase = GuidedCreeiStudyPhase.GeneratingFeedback;
                if (Application.isPlaying) StartDiagnosis(false, false);
            }
        }

        public void SubmitLearnerRequest(string requestText)
        {
            if (string.IsNullOrWhiteSpace(requestText) || VisibleFeedbackTurns >= 2) return;
            if (Mode == CoachOrchestrationMode.SharedControl)
            {
                SelectSharedSuggestion(string.Empty, requestText);
                return;
            }
            if (Mode != CoachOrchestrationMode.LearnerLed) return;
            _learnerRequest = requestText.Trim();
            _confirmedFocus = CurrentStageFocus();
            Phase = GuidedCreeiStudyPhase.GeneratingFeedback;
            LogEvent("LearnerCoachRequestSubmitted");
            if (Application.isPlaying) StartFeedback(CurrentStageFocus(), _learnerRequest);
        }

        public void SelectSharedSuggestion(string suggestionId, string optionalModification)
        {
            if (Mode != CoachOrchestrationMode.SharedControl || VisibleFeedbackTurns >= 2) return;
            CoachSuggestion suggestion = Array.Find(
                _currentSuggestions,
                item => string.Equals(item.SuggestionId, suggestionId, StringComparison.Ordinal));
            if (suggestion == null && string.IsNullOrWhiteSpace(optionalModification)) return;

            _selectedSuggestionId = suggestion?.SuggestionId ?? string.Empty;
            _suggestionModification = optionalModification?.Trim() ?? string.Empty;
            _learnerRequest = _suggestionModification;
            _confirmedFocus = CoachFocusCatalog.Normalize(suggestion?.Focus, CurrentStageFocus());
            Phase = GuidedCreeiStudyPhase.GeneratingFeedback;
            LogEvent(suggestion == null ? "SharedCustomFocusSubmitted" : "SharedSuggestionConfirmed");
            if (Application.isPlaying)
            {
                StartFeedback(suggestion?.Focus ?? CurrentStageFocus(), _suggestionModification);
            }
        }

        public void SubmitSafetySkip()
        {
            if (Phase is GuidedCreeiStudyPhase.Setup or GuidedCreeiStudyPhase.Complete) return;
            CancelNetworkRoutine();
            realtimeTranscriber?.CancelSession();
            _recording = false;
            _integratedFallbackTimerActive = false;
            StopCoachSpeech();
            CriterionMet = false;
            LogEvent("SafetySkip", GuidedStageSkipKind.Safety);
            CompleteStage(GuidedStageSkipKind.Safety);
            AdvanceStage();
        }

        public void AdvanceWithoutCoach()
        {
            if (Phase is not (GuidedCreeiStudyPhase.AwaitingCoachChoice or
                GuidedCreeiStudyPhase.AwaitingRevision)) return;
            CancelNetworkRoutine();
            StopCoachSpeech();
            if (Mode == CoachOrchestrationMode.SharedControl && _latestDiagnosis != null &&
                VisibleFeedbackTurns == 0)
            {
                _normalTerminationReason = CoachTerminationReason.LearnerDeclined;
                LogEvent("SharedSuggestionDeclined");
            }
            else if (Mode == CoachOrchestrationMode.LearnerLed && VisibleFeedbackTurns == 0)
            {
                _normalTerminationReason = CoachTerminationReason.LearnerEnded;
                LogEvent("LearnerSkippedCoach");
            }
            CompleteStage(GuidedStageSkipKind.None);
            AdvanceStage();
        }

        public void BeginRevision()
        {
            if (Phase is not (GuidedCreeiStudyPhase.AwaitingRevision or
                GuidedCreeiStudyPhase.AwaitingCoachChoice)) return;
            _revisionPending = true;
            StopCoachSpeech();
            _previousConfirmedAttempt = ConfirmedTranscript;
            ConfirmedTranscript = string.Empty;
            _liveTranscript.Clear();
            Phase = GuidedCreeiStudyPhase.ReadyToRecord;
            LogEvent("RevisionStarted");
            ShowReadyStage();
        }

        public void NotifyFeedbackPresented(string feedbackText)
        {
            if (Phase != GuidedCreeiStudyPhase.GeneratingFeedback || string.IsNullOrWhiteSpace(feedbackText)) return;
            _latestFeedback = feedbackText.Trim();
            VisibleFeedbackTurns++;
            bool advanceAfterFeedback = _advanceAfterFeedback;
            _advanceAfterFeedback = false;
            Phase = Mode == CoachOrchestrationMode.AiLed && IsGuidedCreeiStage(CurrentStage) &&
                    !advanceAfterFeedback
                ? GuidedCreeiStudyPhase.AwaitingRevision
                : GuidedCreeiStudyPhase.AwaitingCoachChoice;
            LogEvent("FeedbackShown");
            bool revisionRequired = Mode == CoachOrchestrationMode.AiLed &&
                                    IsGuidedCreeiStage(CurrentStage) && !advanceAfterFeedback;
            studyView?.ShowFeedback(_latestFeedback, revisionRequired, !revisionRequired);
            SpeakCoachFeedback(_latestFeedback);
        }

        public void ApplyStageEvaluation(CoachStageEvaluationResult result)
        {
            if (Phase != GuidedCreeiStudyPhase.Evaluating || result == null || !result.Success)
            {
                if (result == null || !result.Success) ShowTechnicalError(
                    result?.Error ?? "Stage evaluation failed.", TechnicalOperation.Evaluation);
                return;
            }

            _latestEvaluation = result;
            CriterionMet = result.IsPassing;
            LogEvent(CriterionMet ? "StageCriterionPassed" : "StageCriterionNeedsRevision");
            GuidedCoachPolicyDecision decision = Policy.Evaluate(new GuidedCoachPolicyInput
            {
                Mode = Mode,
                StageKind = CurrentStage,
                VisibleFeedbackTurns = VisibleFeedbackTurns,
                TranscriptConfirmed = true,
                IsRevision = true,
                Evaluation = result
            });
            Phase = GuidedCreeiStudyPhase.GeneratingFeedback;
            _advanceAfterFeedback = decision.MayAdvance;
            if (Application.isPlaying)
            {
                string request = decision.MayAdvance
                    ? "Briefly acknowledge that the frozen criterion is met and name one strength to carry forward."
                    : result.NextAction;
                StartFeedback(CurrentStageFocus(), request);
            }
        }

        public void ApplySilentDiagnosisResult(bool success)
        {
            if (CurrentStage != GuidedPracticeStageKind.IntegratedPracticeTwo ||
                Phase != GuidedCreeiStudyPhase.Evaluating) return;
            if (!success)
            {
                ShowTechnicalError("The silent post-test diagnosis failed.", TechnicalOperation.SilentPostTest);
                return;
            }
            CompleteStage(GuidedStageSkipKind.None);
            CompleteStudy();
        }

        private void ToggleRecording()
        {
            if (Phase == GuidedCreeiStudyPhase.ReadyToRecord)
            {
                StartRecording();
            }
            else if (Phase == GuidedCreeiStudyPhase.Recording)
            {
                if (IsIntegratedStage && !CanSubmitIntegrated(_recordingDuration))
                {
                    studyView?.ShowRecording(_liveTranscript.ToString(), _recordingDuration, true);
                    return;
                }
                StopRecording();
            }
        }

        private void StartRecording()
        {
            bool characterTalking = coachNPC != null && coachNPC.IsCharacterTalking;
            bool queuedAudio = coachNPC != null && coachNPC.GetAudioResponseCount() > 0;
            CoachRecordingStartDecision startDecision = EvaluateRecordingStart(
                _coachSpeechPending || _coachSpeechStarted,
                characterTalking,
                queuedAudio);
            if (startDecision == CoachRecordingStartDecision.BlockForCurrentCoachFeedback) return;
            if (startDecision == CoachRecordingStartDecision.InterruptStaleCoachThenRecord)
            {
                coachNPC?.InterruptCharacterSpeech();
                ClearCoachSpeechState();
            }

            if (realtimeTranscriber == null)
            {
                if (IsIntegratedStage)
                {
                    _recordingStartedAt = Time.realtimeSinceStartup;
                    _recordingDuration = 0f;
                    _integratedFallbackTimerActive = true;
                }
                ShowTechnicalError(
                    "Realtime transcription is unavailable. A researcher may enter a fallback transcript.",
                    TechnicalOperation.Transcription,
                    true);
                return;
            }
            _liveTranscript.Clear();
            _recordingDuration = 0f;
            _recordingStartedAt = Time.realtimeSinceStartup;
            _integratedFallbackTimerActive = IsIntegratedStage;
            Phase = GuidedCreeiStudyPhase.AwaitingTranscript;
            string device = MicrophoneManager.Instance?.SelectedMicrophoneName ?? string.Empty;
            realtimeTranscriber.StartSession(device);
            studyView?.ShowWorking("Connecting to English realtime transcription...");
        }

        private void StopRecording()
        {
            if (!_recording) return;
            _recording = false;
            Phase = GuidedCreeiStudyPhase.AwaitingTranscript;
            realtimeTranscriber?.StopSession();
            studyView?.ShowWorking("Finalizing the confirmed transcript...");
        }

        private void HandleTranscriptionStarted()
        {
            if (Phase != GuidedCreeiStudyPhase.AwaitingTranscript) return;
            _recording = true;
            if (!IsIntegratedStage)
            {
                _recordingStartedAt = Time.realtimeSinceStartup;
                _recordingDuration = 0f;
            }
            Phase = GuidedCreeiStudyPhase.Recording;
            studyView?.ShowRecording(string.Empty, 0f, IsIntegratedStage);
        }

        private void HandleTranscriptUpdated(string transcript)
        {
            if (Phase is not (GuidedCreeiStudyPhase.Recording or GuidedCreeiStudyPhase.AwaitingTranscript)) return;
            _liveTranscript.Clear();
            _liveTranscript.Append(transcript?.Trim() ?? string.Empty);
        }

        private void HandleTranscriptionCompleted(string transcript)
        {
            _recording = false;
            _integratedFallbackTimerActive = false;
            string final = string.IsNullOrWhiteSpace(transcript) ? _liveTranscript.ToString() : transcript.Trim();
            Phase = GuidedCreeiStudyPhase.ConfirmingTranscript;
            studyView?.ShowTranscriptConfirmation(
                final,
                string.IsNullOrWhiteSpace(final)
                    ? "No English speech was transcribed. Re-record or enter a researcher fallback."
                    : "Confirm this transcript or re-record before Coach processes it.",
                string.IsNullOrWhiteSpace(final));
        }

        private void HandleTranscriptionFailed(string error)
        {
            _recording = false;
            _integratedFallbackTimerActive = IsIntegratedStage;
            ShowTechnicalError(
                (error ?? "Realtime transcription failed.") +
                " Retry, re-record, or enter a researcher fallback transcript.",
                TechnicalOperation.Transcription,
                true);
        }

        private void Rerecord()
        {
            realtimeTranscriber?.CancelSession();
            _recording = false;
            _integratedFallbackTimerActive = false;
            _liveTranscript.Clear();
            ConfirmedTranscript = string.Empty;
            Phase = GuidedCreeiStudyPhase.ReadyToRecord;
            ShowReadyStage();
        }

        private void StartDiagnosis(bool silentPostTest, bool sharedRevision)
        {
            CancelNetworkRoutine();
            int generation = _stageGeneration;
            Phase = silentPostTest ? GuidedCreeiStudyPhase.Evaluating : GuidedCreeiStudyPhase.Diagnosing;
            _technicalOperation = silentPostTest ? TechnicalOperation.SilentPostTest : TechnicalOperation.Diagnosis;
            studyView?.ShowWorking(silentPostTest
                ? "Saving the post-test and running a silent diagnosis..."
                : "Preparing condition-blind Coach suggestions...");
            CoachDiagnosisRequest request = new()
            {
                ParticipantId = CoachStudySessionContext.Current?.ParticipantId ?? string.Empty,
                Stage = CurrentStage.ToString(),
                TopicId = topicId,
                PracticeCycleId = StageIndex + 1,
                TurnId = AttemptIndex,
                Topic = debateTopic,
                LearnerSide = learnerStance,
                PlayerUtteranceText = ConfirmedTranscript,
                PreviousConfirmedStages = BuildPreviousStageContext(),
                PreviousConfirmedAttempt = _previousConfirmedAttempt,
                SelectedStrategy = "Any"
            };
            _networkRoutine = StartCoroutine(_diagnosisEngine.Diagnose(
                request,
                result =>
                {
                    if (generation != _stageGeneration) return;
                    HandleDiagnosisCompleted(result, silentPostTest, sharedRevision);
                }));
        }

        private void HandleDiagnosisCompleted(
            CoachDiagnosisResult result,
            bool silentPostTest,
            bool sharedRevision)
        {
            _networkRoutine = null;
            if (result == null || !result.Success)
            {
                ShowTechnicalError(
                    result?.Error ?? "Condition-blind diagnosis failed.",
                    silentPostTest ? TechnicalOperation.SilentPostTest : TechnicalOperation.Diagnosis);
                return;
            }
            _latestDiagnosis = result;
            _currentSuggestions = result.RankedSuggestions ?? Array.Empty<CoachSuggestion>();
            LogEvent(silentPostTest ? "SilentPostTestDiagnosisCompleted" : "DiagnosisCompleted");

            if (silentPostTest)
            {
                ApplySilentDiagnosisResult(true);
                return;
            }
            if (Mode == CoachOrchestrationMode.SharedControl)
            {
                if (sharedRevision && VisibleFeedbackTurns < 2)
                {
                    CoachSuggestion first = _currentSuggestions.Length > 0 ? _currentSuggestions[0] : null;
                    Phase = GuidedCreeiStudyPhase.GeneratingFeedback;
                    string improvement = string.IsNullOrWhiteSpace(result.RevisionImprovementSummary)
                        ? result.RevisionImprovementStatus
                        : result.RevisionImprovementStatus + ": " + result.RevisionImprovementSummary;
                    string request = improvement + "\nUpdated target: " +
                                     (first?.ImprovementGoal ?? result.RecommendedNextAction);
                    StartFeedback(first?.Focus ?? CurrentStageFocus(), request);
                    studyView?.ShowWorking("Revision assessment: " + improvement +
                                           " Coach is preparing the updated feedback...");
                }
                else
                {
                    Phase = GuidedCreeiStudyPhase.AwaitingCoachChoice;
                    studyView?.ShowSharedSuggestions(
                        _currentSuggestions,
                        _currentSuggestions.Length == 0
                            ? "No priority suggestion was found. Continue or enter a custom request."
                            : "Confirm one suggestion or enter a natural-language modification.",
                        true);
                }
                return;
            }

            Phase = GuidedCreeiStudyPhase.GeneratingFeedback;
            StartFeedback(result.RecommendedFocus, result.RecommendedNextAction);
        }

        private void StartStageEvaluation()
        {
            CancelNetworkRoutine();
            int generation = _stageGeneration;
            Phase = GuidedCreeiStudyPhase.Evaluating;
            _technicalOperation = TechnicalOperation.Evaluation;
            studyView?.ShowWorking("Checking the revised response against the frozen stage criterion...");
            CoachStageEvaluationContext context = new()
            {
                StageKind = CurrentStage,
                Topic = debateTopic,
                LearnerStance = learnerStance,
                ConfirmedTranscript = ConfirmedTranscript,
                PreviousConfirmedStages = BuildPreviousStageContext(),
                AttemptIndex = AttemptIndex,
                RevisionIndex = RevisionIndex
            };
            _networkRoutine = StartCoroutine(_stageEvaluator.Evaluate(context, result =>
            {
                if (generation != _stageGeneration) return;
                _networkRoutine = null;
                ApplyStageEvaluation(result);
            }));
        }

        private void StartFeedback(string focus, string learnerRequest)
        {
            CancelNetworkRoutine();
            int generation = _stageGeneration;
            Phase = GuidedCreeiStudyPhase.GeneratingFeedback;
            _technicalOperation = TechnicalOperation.Feedback;
            studyView?.ShowWorking("Coach is preparing focused feedback...");
            string normalizedFocus = CoachFocusCatalog.Normalize(focus, CurrentStageFocus());
            _confirmedFocus = normalizedFocus;
            CoachFeedbackRequest request = new()
            {
                Stage = CurrentStage.ToString(),
                TopicId = topicId,
                Topic = debateTopic,
                PlayerSide = learnerStance,
                CurrentCreeiStage = CurrentStageFocus(),
                TurnId = AttemptIndex,
                PlayerUtteranceText = ConfirmedTranscript,
                PreviousCoachFeedbackText = _latestFeedback,
                ConfirmedFocus = normalizedFocus,
                LearnerRequest = learnerRequest?.Trim() ?? string.Empty,
                DiagnosisIssueCode = _latestEvaluation?.IssueCode ?? _latestDiagnosis?.DiagnosisIssueCode ?? string.Empty,
                RecommendedStrategy = _latestDiagnosis?.RecommendedStrategy ?? string.Empty,
                TargetSuccessCriterion = IsGuidedCreeiStage(CurrentStage)
                    ? GuidedCreeiRubric.GetCriterion(CurrentStage)
                    : _latestDiagnosis?.RecommendedNextAction ?? string.Empty,
                PreviousLearnerCreeiStages = BuildPreviousStageContext(),
                FeedbackLevel = CoachFeedbackLevel.Level2,
                DetailedJson = true
            };
            _networkRoutine = StartCoroutine(_feedbackGenerator.GenerateFeedback(request, result =>
            {
                if (generation != _stageGeneration) return;
                _networkRoutine = null;
                if (result == null || result.Source != CoachFeedbackSource.OpenAI ||
                    string.IsNullOrWhiteSpace(result.FeedbackText))
                {
                    ShowTechnicalError(
                        result?.DebugInfo ?? "Coach feedback could not be generated.",
                        TechnicalOperation.Feedback);
                    return;
                }
                NotifyFeedbackPresented(result.FeedbackText);
            }));
        }

        private void RetryTechnicalOperation()
        {
            switch (_technicalOperation)
            {
                case TechnicalOperation.Transcription:
                    Phase = GuidedCreeiStudyPhase.ReadyToRecord;
                    ShowReadyStage();
                    break;
                case TechnicalOperation.Diagnosis:
                    StartDiagnosis(false, Mode == CoachOrchestrationMode.SharedControl && _revisionPending);
                    break;
                case TechnicalOperation.Evaluation:
                    StartStageEvaluation();
                    break;
                case TechnicalOperation.Feedback:
                    StartFeedback(
                        string.IsNullOrWhiteSpace(_confirmedFocus)
                            ? _latestDiagnosis?.RecommendedFocus ?? CurrentStageFocus()
                            : _confirmedFocus,
                        _learnerRequest);
                    break;
                case TechnicalOperation.SilentPostTest:
                    StartDiagnosis(true, false);
                    break;
            }
        }

        private void SubmitTechnicalSkip()
        {
            CancelNetworkRoutine();
            realtimeTranscriber?.CancelSession();
            _recording = false;
            _integratedFallbackTimerActive = false;
            StopCoachSpeech();
            LogEvent("TechnicalSkip", GuidedStageSkipKind.Technical);
            CompleteStage(GuidedStageSkipKind.Technical);
            if (CurrentStage == GuidedPracticeStageKind.IntegratedPracticeTwo)
            {
                CompleteStudy();
            }
            else
            {
                AdvanceStage();
            }
        }

        private void ShowTechnicalError(
            string error,
            TechnicalOperation operation,
            bool allowFallbackTranscript = false)
        {
            _technicalOperation = operation;
            Phase = GuidedCreeiStudyPhase.TechnicalError;
            LogEvent("TechnicalError");
            studyView?.ShowTechnicalError(error, allowFallbackTranscript);
        }

        private void AdvanceStage()
        {
            StopCoachSpeech();
            if (StageIndex >= StageSequence.Length - 1)
            {
                CompleteStudy();
                return;
            }
            StageIndex++;
            CurrentStage = StageSequence[StageIndex];
            ResetStageState();
            ShowReadyStage();
        }

        private void ResetStageState()
        {
            _stageGeneration++;
            AttemptIndex = 0;
            RevisionIndex = 0;
            VisibleFeedbackTurns = 0;
            CriterionMet = false;
            ConfirmedTranscript = string.Empty;
            _revisionPending = false;
            _recording = false;
            _integratedFallbackTimerActive = false;
            _advanceAfterFeedback = false;
            _recordingDuration = 0f;
            _learnerRequest = string.Empty;
            _selectedSuggestionId = string.Empty;
            _suggestionModification = string.Empty;
            _confirmedFocus = string.Empty;
            _initialConfirmedText = string.Empty;
            _previousConfirmedAttempt = string.Empty;
            _latestFeedback = string.Empty;
            _latestDiagnosis = null;
            _latestEvaluation = null;
            _currentSuggestions = Array.Empty<CoachSuggestion>();
            _normalTerminationReason = CoachTerminationReason.None;
            _stageStartedAt = DateTimeOffset.UtcNow.ToString("o");
            Phase = GuidedCreeiStudyPhase.ReadyToRecord;
        }

        private void ShowReadyStage()
        {
            RegisterInputRouting();
            SetCursor(true);
            studyView?.ShowStage(
                CurrentStage,
                BuildStageInstruction(),
                IsIntegratedStage
                    ? "Press T to begin one complete 60–90 second argument."
                    : "Press T to record this stage. Confirm the transcript before Coach processes it.",
                IsIntegratedStage);
        }

        private string BuildStageInstruction()
        {
            if (CurrentStage == GuidedPracticeStageKind.IntegratedPracticeOne)
                return $"Topic: {debateTopic}\nYour side: {learnerStance}\nDeliver one complete CREEI argument using the five stages you practiced.";
            if (CurrentStage == GuidedPracticeStageKind.IntegratedPracticeTwo)
                return $"Post-test: deliver a second complete CREEI argument. Your result will be saved without visible Coach feedback.\nTopic: {debateTopic}";
            return $"Topic: {debateTopic}\nYour side: {learnerStance}\nTask: {GuidedCreeiRubric.GetCriterion(CurrentStage)}\nPreviously confirmed:\n{BuildPreviousStageContext()}";
        }

        private string BuildPreviousStageContext()
        {
            StringBuilder builder = new();
            for (int index = 0; index < Mathf.Min(StageIndex, _confirmedStageTexts.Length); index++)
            {
                if (string.IsNullOrWhiteSpace(_confirmedStageTexts[index])) continue;
                if (builder.Length > 0) builder.AppendLine();
                builder.Append(StageSequence[index]).Append(": ").Append(_confirmedStageTexts[index]);
            }
            return builder.Length == 0 ? "(none yet)" : builder.ToString();
        }

        private string CurrentStageFocus()
        {
            return IsGuidedCreeiStage(CurrentStage) ? CurrentStage.ToString() : "Explanation";
        }

        private void CompleteStudy()
        {
            if (_sceneLoadRequested) return;
            Phase = GuidedCreeiStudyPhase.Complete;
            _logger?.Flush();
            StopCoachSpeech();
            RestorePlayerMovement();
            studyView?.HideAll();
            if (!Application.isPlaying) return;
            _sceneLoadRequested = true;
            SceneManager.LoadScene(transferSceneName);
        }

        private void LogEvent(string eventType, GuidedStageSkipKind skipKind = GuidedStageSkipKind.None)
        {
            if (_logger == null) return;
            CoachStudySessionSnapshot session = CoachStudySessionContext.Current;
            ControlOwner startOwner = Mode switch
            {
                CoachOrchestrationMode.LearnerLed => ControlOwner.Learner,
                CoachOrchestrationMode.SharedControl => ControlOwner.Shared,
                CoachOrchestrationMode.AiLed => ControlOwner.Coach,
                _ => ControlOwner.SystemSafety
            };
            ControlOwner agendaOwner = Mode == CoachOrchestrationMode.SharedControl &&
                                       (eventType.Contains("Suggestion", StringComparison.Ordinal) ||
                                        eventType.Contains("CustomFocus", StringComparison.Ordinal))
                ? ControlOwner.Learner
                : startOwner;
            ControlOwner pacingOwner = Mode == CoachOrchestrationMode.AiLed
                ? ControlOwner.Coach
                : ControlOwner.Learner;
            ControlOwner terminationOwner = skipKind is GuidedStageSkipKind.Safety or GuidedStageSkipKind.Technical
                ? ControlOwner.SystemSafety
                : ControlOwner.Learner;
            CoachTerminationReason terminationReason = skipKind switch
            {
                GuidedStageSkipKind.Safety => CoachTerminationReason.SafetyOverride,
                GuidedStageSkipKind.Technical => CoachTerminationReason.TechnicalFailure,
                _ when eventType == "SharedSuggestionDeclined" => CoachTerminationReason.LearnerDeclined,
                _ when eventType == "LearnerSkippedCoach" => CoachTerminationReason.LearnerEnded,
                _ => CoachTerminationReason.None
            };
            bool evaluationPerformed = _latestEvaluation != null ||
                                       CurrentStage == GuidedPracticeStageKind.IntegratedPracticeTwo &&
                                       _latestDiagnosis?.Success == true;
            _logger.LogEvent(new GuidedCoachEventRecord
            {
                ParticipantId = session?.ParticipantId ?? string.Empty,
                SessionId = session?.SessionId ?? string.Empty,
                Mode = Mode,
                StageKind = CurrentStage,
                StageIndex = StageIndex + 1,
                AttemptIndex = AttemptIndex,
                RevisionIndex = RevisionIndex,
                EventType = eventType,
                LearnerRequestText = _learnerRequest,
                ConfirmedLearnerText = ConfirmedTranscript,
                RankedSuggestionsJson = Newtonsoft.Json.JsonConvert.SerializeObject(_currentSuggestions),
                SelectedSuggestionId = _selectedSuggestionId,
                SuggestionModification = _suggestionModification,
                RevisionImprovementStatus = _latestDiagnosis?.RevisionImprovementStatus ?? string.Empty,
                RevisionImprovementSummary = _latestDiagnosis?.RevisionImprovementSummary ?? string.Empty,
                EvaluationPerformed = evaluationPerformed,
                AssessmentStatus = CurrentAssessmentStatus(),
                CriterionMet = CriterionMet,
                EvaluationConfidence = _latestEvaluation?.Confidence ?? _latestDiagnosis?.Confidence ?? 0f,
                EvaluationEvidence = _latestEvaluation?.EvidenceSpan ?? string.Empty,
                IssueCode = _latestEvaluation?.IssueCode ?? _latestDiagnosis?.DiagnosisIssueCode ?? string.Empty,
                NextAction = _latestEvaluation?.NextAction ?? _latestDiagnosis?.RecommendedNextAction ?? string.Empty,
                FeedbackText = _latestFeedback,
                VisibleFeedbackTurn = VisibleFeedbackTurns,
                SkipKind = skipKind,
                TerminationReason = terminationReason,
                StartAuthority = startOwner,
                AgendaOwner = agendaOwner,
                PacingOwner = pacingOwner,
                TerminationOwner = terminationOwner,
                DiagnosisModelVersion = "coach-diagnosis-v2",
                FeedbackModelVersion = "coach-feedback-v2",
                PolicyVersion = PolicyVersion,
                RubricVersion = GuidedCreeiRubric.Version
            });
        }

        private void CompleteStage(GuidedStageSkipKind skipKind)
        {
            if (_logger == null) return;
            CoachStudySessionSnapshot session = CoachStudySessionContext.Current;
            bool evaluationPerformed = _latestEvaluation != null ||
                                       CurrentStage == GuidedPracticeStageKind.IntegratedPracticeTwo &&
                                       _latestDiagnosis?.Success == true;
            CoachTerminationReason terminationReason = skipKind switch
            {
                GuidedStageSkipKind.Safety => CoachTerminationReason.SafetyOverride,
                GuidedStageSkipKind.Technical => CoachTerminationReason.TechnicalFailure,
                _ when _normalTerminationReason != CoachTerminationReason.None => _normalTerminationReason,
                _ when CriterionMet => CoachTerminationReason.TargetResolved,
                _ => CoachTerminationReason.LearnerEnded
            };
            _logger.CompleteStage(new GuidedCoachStageSummary
            {
                ParticipantId = session?.ParticipantId ?? string.Empty,
                SessionId = session?.SessionId ?? string.Empty,
                Mode = Mode,
                StageKind = CurrentStage,
                StageIndex = StageIndex + 1,
                AttemptCount = AttemptIndex,
                RevisionCount = RevisionIndex,
                VisibleFeedbackCount = VisibleFeedbackTurns,
                InitialConfirmedText = _initialConfirmedText,
                FinalConfirmedText = ConfirmedTranscript,
                EvaluationPerformed = evaluationPerformed,
                AssessmentStatus = CurrentAssessmentStatus(),
                CriterionMet = CriterionMet,
                EvaluationConfidence = _latestEvaluation?.Confidence ?? _latestDiagnosis?.Confidence ?? 0f,
                SkipKind = skipKind,
                TerminationReason = terminationReason,
                StartTimestamp = _stageStartedAt,
                EndTimestamp = DateTimeOffset.UtcNow.ToString("o"),
                DiagnosisModelVersion = "coach-diagnosis-v2",
                FeedbackModelVersion = "coach-feedback-v2",
                PolicyVersion = PolicyVersion,
                RubricVersion = GuidedCreeiRubric.Version
            });
        }

        private string CurrentAssessmentStatus()
        {
            if (_latestEvaluation != null)
                return CriterionMet ? "CriterionPassed" : "CriterionNeedsRevision";
            if (CurrentStage == GuidedPracticeStageKind.IntegratedPracticeTwo &&
                _latestDiagnosis?.Success == true)
                return "SilentDiagnosisCompleted";
            return "NotEvaluated";
        }

        private void SpeakCoachFeedback(string feedback)
        {
            StopCoachSpeech();
            if (!speakCoachFeedback || coachNPC == null) return;

            string prompt = BuildCoachSpeechPrompt(feedback);
            if (string.IsNullOrWhiteSpace(prompt)) return;

            _coachSpeechPending = true;
            _coachSpeechStarted = false;
            _coachSpeechDeadline = Time.realtimeSinceStartup +
                                   GetCoachSpeechStartTimeoutSeconds(responseTimeoutSeconds);
            ConvaiNPCManager.Instance?.SetActiveConvaiNPC(coachNPC);
            coachNPC.SendTextDataAsync(prompt);
        }

        private void StopCoachSpeech()
        {
            coachNPC?.InterruptCharacterSpeech();
            ClearCoachSpeechState();
        }

        private void ClearCoachSpeechState()
        {
            _coachSpeechPending = false;
            _coachSpeechStarted = false;
            _coachSpeechDeadline = 0f;
            studyView?.SetCoachSpeaking(false);
        }

        private void UpdateCoachSpeechState()
        {
            bool talking = coachNPC != null && coachNPC.IsCharacterTalking;
            bool queuedAudio = coachNPC != null && coachNPC.GetAudioResponseCount() > 0;
            bool learnerMicActive = Phase is GuidedCreeiStudyPhase.AwaitingTranscript or
                GuidedCreeiStudyPhase.Recording;
            CoachSpeechLifecycleDecision decision = EvaluateCoachSpeechLifecycle(
                _coachSpeechPending,
                _coachSpeechStarted,
                talking,
                queuedAudio,
                learnerMicActive,
                _coachSpeechPending && Time.realtimeSinceStartup >= _coachSpeechDeadline);

            switch (decision)
            {
                case CoachSpeechLifecycleDecision.Start:
                    _coachSpeechPending = false;
                    _coachSpeechStarted = true;
                    _coachSpeechDeadline = 0f;
                    break;
                case CoachSpeechLifecycleDecision.Finish:
                    ClearCoachSpeechState();
                    return;
                case CoachSpeechLifecycleDecision.Timeout:
                case CoachSpeechLifecycleDecision.InterruptForLearnerMic:
                    coachNPC?.InterruptCharacterSpeech();
                    ClearCoachSpeechState();
                    return;
            }

            studyView?.SetCoachSpeaking(_coachSpeechStarted && talking);
        }

        private void CancelNetworkRoutine()
        {
            if (_networkRoutine != null)
            {
                StopCoroutine(_networkRoutine);
                _networkRoutine = null;
            }
            _diagnosisEngine?.Cancel();
            _stageEvaluator?.Cancel();
            _feedbackGenerator?.Cancel();
        }

        private void SubscribeView()
        {
            if (studyView == null) return;
            studyView.SessionStartRequested += BeginStudy;
            studyView.ToggleRecordingRequested += ToggleRecording;
            studyView.ConfirmTranscriptRequested += SubmitConfirmedTranscript;
            studyView.RerecordRequested += Rerecord;
            studyView.LearnerRequestSubmitted += SubmitLearnerRequest;
            studyView.SharedSuggestionSelected += SelectSharedSuggestion;
            studyView.ContinueRequested += AdvanceWithoutCoach;
            studyView.BeginRevisionRequested += BeginRevision;
            studyView.SafetySkipRequested += SubmitSafetySkip;
            studyView.RetryRequested += RetryTechnicalOperation;
            studyView.TechnicalSkipRequested += SubmitTechnicalSkip;
        }

        private void UnsubscribeView()
        {
            if (studyView == null) return;
            studyView.SessionStartRequested -= BeginStudy;
            studyView.ToggleRecordingRequested -= ToggleRecording;
            studyView.ConfirmTranscriptRequested -= SubmitConfirmedTranscript;
            studyView.RerecordRequested -= Rerecord;
            studyView.LearnerRequestSubmitted -= SubmitLearnerRequest;
            studyView.SharedSuggestionSelected -= SelectSharedSuggestion;
            studyView.ContinueRequested -= AdvanceWithoutCoach;
            studyView.BeginRevisionRequested -= BeginRevision;
            studyView.SafetySkipRequested -= SubmitSafetySkip;
            studyView.RetryRequested -= RetryTechnicalOperation;
            studyView.TechnicalSkipRequested -= SubmitTechnicalSkip;
        }

        private void SubscribeTranscriber()
        {
            if (realtimeTranscriber == null) return;
            realtimeTranscriber.SessionStarted += HandleTranscriptionStarted;
            realtimeTranscriber.TranscriptUpdated += HandleTranscriptUpdated;
            realtimeTranscriber.SessionCompleted += HandleTranscriptionCompleted;
            realtimeTranscriber.SessionFailed += HandleTranscriptionFailed;
        }

        private void UnsubscribeTranscriber()
        {
            if (realtimeTranscriber == null) return;
            realtimeTranscriber.SessionStarted -= HandleTranscriptionStarted;
            realtimeTranscriber.TranscriptUpdated -= HandleTranscriptUpdated;
            realtimeTranscriber.SessionCompleted -= HandleTranscriptionCompleted;
            realtimeTranscriber.SessionFailed -= HandleTranscriptionFailed;
        }

        private void RegisterInputRouting()
        {
            ConvaiInputManager.ShouldUseTapToTalk = ShouldUseTapToTalk;
            ConvaiInputManager.TapToTalkRequested = ToggleRecording;
            ConvaiInputManager.ShouldSuppressTalkInput = SuppressDefaultTalk;
            ConvaiPlayerInteractionManager.ShouldSuppressTalkInput = SuppressDefaultTalk;
        }

        private void UnregisterInputRouting()
        {
            if (ConvaiInputManager.ShouldUseTapToTalk == (Func<bool>)ShouldUseTapToTalk)
                ConvaiInputManager.ShouldUseTapToTalk = null;
            if (ConvaiInputManager.TapToTalkRequested == (Action)ToggleRecording)
                ConvaiInputManager.TapToTalkRequested = null;
            if (ConvaiInputManager.ShouldSuppressTalkInput == (Func<bool>)SuppressDefaultTalk)
                ConvaiInputManager.ShouldSuppressTalkInput = null;
            if (ConvaiPlayerInteractionManager.ShouldSuppressTalkInput == (Func<bool>)SuppressDefaultTalk)
                ConvaiPlayerInteractionManager.ShouldSuppressTalkInput = null;
        }

        private bool ShouldUseTapToTalk() => _studyStarted;
        private bool SuppressDefaultTalk() => _studyStarted || Phase == GuidedCreeiStudyPhase.Setup;

        private void HideLegacyUi()
        {
            legacyInteractiveControls?.SetActive(false);
            legacyStartButton?.SetActive(false);
            legacyRoundTimer?.SetActive(false);
        }

        private void DisableCoachNpcToNpcFlow()
        {
            if (coachNPC == null) return;
            ConvaiGroupNPCController[] groupControllers =
                coachNPC.GetComponents<ConvaiGroupNPCController>();
            for (int index = 0; index < groupControllers.Length; index++)
            {
                if (groupControllers[index] != null)
                    groupControllers[index].enabled = false;
            }
        }

        private void FreezePlayerMovement()
        {
            if (_movementFrozen) return;
            _movementComponents = FindObjectsByType<ConvaiPlayerMovement>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            _movementOriginalStates = new bool[_movementComponents.Length];
            for (int index = 0; index < _movementComponents.Length; index++)
            {
                _movementOriginalStates[index] = _movementComponents[index] != null && _movementComponents[index].enabled;
                if (_movementComponents[index] != null) _movementComponents[index].enabled = false;
            }
            _movementFrozen = true;
        }

        private void ApplyStudyWorldLayout()
        {
            if (!applyStudyWorldLayout || coachNPC == null) return;

            coachNPC.transform.position = coachStudyWorldPosition;
            Transform learner = null;
            for (int index = 0; index < _movementComponents.Length; index++)
            {
                if (_movementComponents[index] == null) continue;
                learner = _movementComponents[index].transform;
                break;
            }

            if (learner != null)
            {
                learner.position = CalculateCloserLearnerPosition(
                    learner.position,
                    coachNPC.transform.position,
                    learnerMoveTowardCoachMeters);
            }
        }

        private void RestorePlayerMovement()
        {
            if (!_movementFrozen) return;
            for (int index = 0; index < _movementComponents.Length; index++)
            {
                if (_movementComponents[index] != null && index < _movementOriginalStates.Length)
                    _movementComponents[index].enabled = _movementOriginalStates[index];
            }
            _movementFrozen = false;
        }

        private void SetCursor(bool visible)
        {
            Cursor.visible = visible;
            Cursor.lockState = visible ? CursorLockMode.None : CursorLockMode.Locked;
        }

        private void Cleanup()
        {
            CancelNetworkRoutine();
            realtimeTranscriber?.CancelSession();
            _recording = false;
            StopCoachSpeech();
            UnsubscribeView();
            UnsubscribeTranscriber();
            UnregisterInputRouting();
            _logger?.Flush();
            RestorePlayerMovement();
            Cursor.lockState = _originalCursorLockMode;
            Cursor.visible = _originalCursorVisible;
        }

        private static bool IsGuidedCreeiStage(GuidedPracticeStageKind stage)
        {
            return stage is >= GuidedPracticeStageKind.Claim and <= GuidedPracticeStageKind.Impact;
        }
    }
}
