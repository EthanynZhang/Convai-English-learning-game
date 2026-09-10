using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Convai.Scripts.Runtime.UI;
using UnityEngine;

namespace Game.Debate
{
    public interface IMicroCreeiPracticeHost
    {
        CoachOrchestrationMode Mode { get; }
        string ParticipantId { get; }
        string SessionId { get; }
        string TopicId { get; }
        string Topic { get; }
        string LearnerStance { get; }
        string DiagnosisModelVersion { get; }
        string PolicyVersion { get; }
        string LogDirectory { get; }
        bool IsNpcSpeechActive { get; }

        void RequestDiagnosis(
            CreeiArgumentDiagnosisRequest request,
            Action<CreeiArgumentDiagnosisResult> onComplete);
        void RequestFeedback(
            CreeiArgumentSnapshot current,
            CreeiArgumentSnapshot previous,
            CreeiComponent focus,
            CreeiComponentDiagnosis diagnosis,
            string learnerRequest,
            string previousCoachFeedback,
            CoachFeedbackPurpose purpose,
            IReadOnlyList<CoachConversationTurn> conversationHistory,
            string acceptedCriticalFeedback,
            Action<CoachFeedbackResult> onComplete);
        void SpeakAnna(string text, Action<bool> onComplete);
        void CancelMicroOperations();
        void RecordMicroEvent(string eventType, object payload = null);
        void RecordMicroTechnicalFailure(
            string operation,
            string message,
            int retryCount = 0,
            bool recovered = false);
        void AdvanceFromMicroCreei(int completedRounds, bool timedOut);
    }

    public sealed class MicroCreeiPracticeController : MonoBehaviour
    {
        private enum PendingOperation
        {
            None,
            Diagnosis,
            CriticalFeedback,
            Feedback
        }

        private IMicroCreeiPracticeHost _host;
        private MicroCreeiWorkbenchView _view;
        private XfyunRealtimeTranscriber _transcriber;
        private MicroCreeiPracticeSession _session;
        private MicroCreeiSnapshotLogger _snapshotLogger;
        private readonly List<CoachConversationTurn> _conversationHistory = new();
        private PendingOperation _pendingOperation;
        private CoachFeedbackPurpose _activePurpose = CoachFeedbackPurpose.TargetedAdvice;
        private bool _configured;
        private bool _recording;
        private bool _finished;
        private bool _recordingLearnerRequest;
        private bool _sharedFollowupsVisible;
        private string _voiceOriginalText = string.Empty;
        private string _learnerRequestVoicePrefix = string.Empty;
        private string _lastLoggedSnapshotId = string.Empty;
        private string _criticalFeedback = string.Empty;
        private string _currentFeedback = string.Empty;
        private CreeiModelExampleSet _aiLedCreeiModelExamples;
        private string _learnerRequest = string.Empty;
        private string _learnerRequestInputModality = "text";
        private CreeiComponent? _recordingComponent;
        private Coroutine _restartRoutine;
        private int _technicalRetryCount;
        private bool _technicalFailureActive;
        private PendingOperation _technicalFailureOperation;
        private int _exampleCount;
        private float _diagnosisStartedAt;

        public MicroCreeiPracticeState CurrentState => _session?.CurrentState ??
            MicroCreeiPracticeState.Drafting;
        public float RemainingSeconds => _session?.RemainingSeconds ?? 0f;
        public int CurrentRoundIndex => _session?.CurrentRoundIndex ?? 0;
        public int SnapshotCount => _session?.SnapshotCount ?? 0;
        public int CompletedRoundCount => _session?.CompletedRoundCount ?? 0;
        public CreeiComponent? ActiveComponent => _session?.ActiveComponent;
        public CreeiArgumentDraft CurrentDraft => _session?.CurrentDraft;
        public CreeiArgumentSnapshot LastCommittedSnapshot => _session?.LastCommittedSnapshot;
        public IReadOnlyList<CreeiComponent> ChangedComponents =>
            _session?.ChangedComponents ?? Array.Empty<CreeiComponent>();
        public CreeiComponent? CurrentCoachFocus => _session?.CurrentCoachFocus;
        public CoachOrchestrationMode Mode => _host?.Mode ?? CoachOrchestrationMode.Disabled;
        public bool IsActive { get; private set; }
        public bool IsRecording => _recording || (_transcriber?.IsSessionActive ?? false);

        public static bool CanToggleComponentRecording(
            bool hasActiveComponent,
            bool textInputFocused,
            bool npcSpeechActive) =>
            hasActiveComponent && !textInputFocused && !npcSpeechActive;

        public static bool CanAskCoachAgain(
            MicroCreeiPracticeState state,
            CoachOrchestrationMode mode) =>
            state is MicroCreeiPracticeState.CoachedRevision or
                MicroCreeiPracticeState.IndependentRevision;

        public static bool ShouldRouteVoiceToLearnerRequest(
            MicroCreeiPracticeState state,
            CoachOrchestrationMode mode,
            bool requestVisible,
            bool requestFocused) =>
            requestVisible && !requestFocused &&
            ((state is MicroCreeiPracticeState.CoachedRevision or
                  MicroCreeiPracticeState.IndependentRevision) ||
             (mode == CoachOrchestrationMode.LearnerLed &&
              state == MicroCreeiPracticeState.AwaitingCoachDecision) ||
             (mode == CoachOrchestrationMode.SharedControl &&
              state == MicroCreeiPracticeState.CriticalFeedbackReview));

        public void Configure(
            IMicroCreeiPracticeHost host,
            Canvas canvas,
            XfyunRealtimeTranscriber transcriber)
        {
            if (_configured) return;
            _host = host ?? throw new ArgumentNullException(nameof(host));
            _transcriber = transcriber;
            _view = GetComponent<MicroCreeiWorkbenchView>();
            if (_view == null) _view = gameObject.AddComponent<MicroCreeiWorkbenchView>();
            _view.Build(canvas);
            Subscribe();
            _configured = true;
        }

        public void BeginMicroCreeiPractice()
        {
            if (!_configured) throw new InvalidOperationException("Configure the workbench first.");
            CleanupActiveOperation();
            _snapshotLogger = new MicroCreeiSnapshotLogger(_host.LogDirectory);
            _session = new MicroCreeiPracticeSession(ThreeStageDebatePracticeRules.MicroPracticeSeconds);
            _session.Begin();
            _finished = false;
            _lastLoggedSnapshotId = string.Empty;
            _criticalFeedback = string.Empty;
            _currentFeedback = string.Empty;
            _aiLedCreeiModelExamples = null;
            _learnerRequest = string.Empty;
            _learnerRequestInputModality = "text";
            _conversationHistory.Clear();
            _exampleCount = 0;
            _sharedFollowupsVisible = false;
            _technicalRetryCount = 0;
            _technicalFailureActive = false;
            _technicalFailureOperation = PendingOperation.None;
            IsActive = true;
            _view.Show(_host.Topic, _host.LearnerStance, _session.RemainingSeconds);
            _view.ClearCreeiModelExamples();
            _view.SetDialogue(string.Empty, string.Empty);
            _host.RecordMicroEvent("WorkbenchOpened", new
            {
                round_index = 1,
                time_budget_seconds = ThreeStageDebatePracticeRules.MicroPracticeSeconds
            });
            Render();
        }

        public void Tick(float deltaSeconds)
        {
            if (!IsActive || _session == null || _finished) return;
            _session.Tick(deltaSeconds);
            _view.SetRemainingSeconds(_session.RemainingSeconds);
            if (_session.CurrentState != MicroCreeiPracticeState.TimedOut) return;
            LogSnapshotIfNeeded();
            _host.RecordMicroEvent("PracticeOneTimedOut", new
            {
                round_index = _session.CurrentRoundIndex,
                snapshot_id = _session.LastCommittedSnapshot?.SnapshotId ?? string.Empty
            });
            CompletePractice(true);
        }

        public void SetActiveComponent(CreeiComponent component)
        {
            if (!IsActive || IsRecording) return;
            try
            {
                _session.SetActiveComponent(component);
                _view.SetActiveComponent(component);
                _host.RecordMicroEvent("ComponentFocused", new
                {
                    component = component.ToString(),
                    round_index = _session.CurrentRoundIndex
                });
            }
            catch (InvalidOperationException)
            {
                _view.SetStatus("Wait until the current diagnosis or Coach action finishes.");
            }
        }

        public void SetComponentText(
            CreeiComponent component,
            string text,
            string modality = "keyboard")
        {
            if (!IsActive || IsRecording) return;
            try
            {
                _session.SetComponentText(component, text, modality);
                UpdateCardState(component);
                RenderButtons();
            }
            catch (InvalidOperationException)
            {
                _view.SetComponentText(component, _session.CurrentDraft.GetText(component));
            }
        }

        public void SubmitArgumentSnapshot()
        {
            if (!IsActive || IsRecording) return;
            try
            {
                SanitizeDraftForSubmission();
                CreeiArgumentSnapshot snapshot = _session.SubmitArgumentSnapshot();
                LogSnapshotIfNeeded();
                string eventType = snapshot.RevisionKind switch
                {
                    CreeiRevisionKind.InitialStructure => "ArgumentSnapshotSubmitted",
                    CreeiRevisionKind.IndependentRevision => "IndependentRevisionSubmitted",
                    CreeiRevisionKind.CoachedRevision => "CoachedRevisionSubmitted",
                    _ => "ArgumentSnapshotSubmitted"
                };
                _host.RecordMicroEvent(eventType, new
                {
                    round_index = snapshot.RoundIndex,
                    snapshot_id = snapshot.SnapshotId,
                    parent_snapshot_id = snapshot.ParentSnapshotId,
                    revision_kind = snapshot.RevisionKind.ToString(),
                    changed_components = snapshot.ChangedComponents
                        .Select(value => value.ToString()).ToArray()
                });
                BeginDiagnosis();
            }
            catch (InvalidOperationException exception)
            {
                _view.SetStatus(exception.Message);
                foreach (CreeiComponent component in _session.CurrentDraft.MissingComponents)
                    _view.SetCardState(component, "Missing");
            }
        }

        public void HandleCoachAction(CoachWorkbenchAction action)
        {
            if (!IsActive || _session == null || IsRecording) return;
            switch (action)
            {
                case CoachWorkbenchAction.AskCoach:
                    SubmitLearnerQuestion();
                    break;
                case CoachWorkbenchAction.AcceptIssue:
                    if (Mode == CoachOrchestrationMode.SharedControl)
                        AcceptCriticalFeedback("critical_feedback_accepted");
                    break;
                case CoachWorkbenchAction.ChangeRequest:
                    HandleSharedChangeRequest();
                    break;
                case CoachWorkbenchAction.ContinueWithoutCoach:
                    ContinueWithoutCoach();
                    break;
                case CoachWorkbenchAction.NeedExample:
                    StartStructuredFollowUp(CoachFeedbackPurpose.Example);
                    break;
                case CoachWorkbenchAction.NeedMoreSuggestions:
                    StartStructuredFollowUp(CoachFeedbackPurpose.AdditionalSuggestion);
                    break;
                case CoachWorkbenchAction.UseAdviceAndRevise:
                    _sharedFollowupsVisible = false;
                    _view.SetStatus("Revise one or more cards using Anna's advice, then submit the revision.");
                    Render();
                    break;
                case CoachWorkbenchAction.FinishPractice:
                    FinishPracticeOne();
                    break;
                case CoachWorkbenchAction.Retry:
                    RetryTechnicalOperation();
                    break;
                case CoachWorkbenchAction.TechnicalSkip:
                    TechnicalSkip();
                    break;
            }
        }

        public void FinishPracticeOne()
        {
            if (!IsActive || _session == null) return;
            try
            {
                _session.FinishPractice();
                _host.RecordMicroEvent("PracticeOneFinishedEarly", new
                {
                    completed_rounds = _session.CompletedRoundCount,
                    remaining_seconds = _session.RemainingSeconds
                });
                CompletePractice(false);
            }
            catch (InvalidOperationException exception)
            {
                _view.SetStatus(exception.Message);
            }
        }

        private void BeginDiagnosis()
        {
            _pendingOperation = PendingOperation.Diagnosis;
            _diagnosisStartedAt = Time.realtimeSinceStartup;
            _view.SetInputsInteractable(false);
            _view.SetStatus("Reviewing all five CREEI components and their connections...");
            _host.RequestDiagnosis(new CreeiArgumentDiagnosisRequest
            {
                ParticipantId = _host.ParticipantId,
                Stage = _session.LastCommittedSnapshot.RevisionKind.ToString(),
                TopicId = _host.TopicId,
                Topic = _host.Topic,
                LearnerSide = _host.LearnerStance,
                RoundIndex = _session.CurrentRoundIndex,
                CurrentSnapshot = _session.LastCommittedSnapshot,
                PreviousSnapshot = PreviousCommittedSnapshot()
            }, HandleDiagnosisCompleted);
        }

        private void HandleDiagnosisCompleted(CreeiArgumentDiagnosisResult diagnosis)
        {
            if (!IsActive || _finished) return;
            if (diagnosis == null || !diagnosis.Success)
            {
                EnterTechnicalError(diagnosis?.Error ?? "Diagnosis failed.",
                    PendingOperation.Diagnosis);
                return;
            }
            _pendingOperation = PendingOperation.None;
            MarkTechnicalRecovery(PendingOperation.Diagnosis);
            int completedBefore = _session.CompletedRoundCount;
            _session.ApplyDiagnosis(diagnosis);
            int elapsedMilliseconds = Mathf.Max(0, Mathf.RoundToInt(
                (Time.realtimeSinceStartup - _diagnosisStartedAt) * 1000f));
            _host.RecordMicroEvent("local_diagnosis_completed", new
            {
                round_index = _session.CurrentRoundIndex,
                elapsed_milliseconds = elapsedMilliseconds,
                diagnosis_source = "local_rules",
                model_version = diagnosis.ModelVersion
            });
            _host.RecordMicroEvent("DiagnosisCompleted", new
            {
                round_index = _session.CurrentRoundIndex,
                snapshot_id = _session.LastCommittedSnapshot?.SnapshotId ?? string.Empty,
                revision_kind = _session.LastCommittedSnapshot?.RevisionKind.ToString() ?? string.Empty,
                primary_issue = diagnosis.PrimaryIssue?.ToString() ?? string.Empty,
                ranked_issues = diagnosis.RankedIssues.Select(value => value.ToString()).ToArray(),
                creei_missing_or_weak_components = diagnosis.RankedIssues
                    .Select(value => value.ToString()).ToArray(),
                creei_gap_summary = BuildCreeiGapSummary(diagnosis),
                model_version = diagnosis.ModelVersion
            });
            if (_session.CurrentState == MicroCreeiPracticeState.AwaitingCoachDecision)
                PresentCoachOpportunity();
            else if (_session.CurrentState == MicroCreeiPracticeState.RoundComplete)
                CompleteRound();
            else if (_session.CompletedRoundCount > completedBefore)
            {
                _host.RecordMicroEvent("RoundCompleted", new
                {
                    round_index = _session.CurrentRoundIndex,
                    completed_rounds = _session.CompletedRoundCount,
                    coach_feedback_turns = _conversationHistory.Count
                });
                _view.SetLearnerRequestVisible(true);
                _view.SetStatus("Revision saved. Ask Anna about this submitted version, keep revising, or finish Practice 1.");
            }
            Render();
        }

        private void PresentCoachOpportunity()
        {
            _view.SetLearnerRequestVisible(false);
            if (Mode == CoachOrchestrationMode.LearnerLed)
            {
                _view.SetLearnerRequestVisible(true);
                _view.SetStatus("Anna will stay silent until you ask a question. You may also continue without Coach support.");
                _host.RecordMicroEvent("coach_opportunity_presented", new
                {
                    round_index = _session.CurrentRoundIndex,
                    agenda_owner = ControlOwner.Learner.ToString()
                });
                return;
            }
            if (Mode == CoachOrchestrationMode.AiLed)
            {
                CreeiComponent focus = EffectiveFocus();
                _session.StartCoach(focus);
                IssueFeedbackRequest(focus, CoachFeedbackPurpose.CreeiModelAnswer,
                    false);
                return;
            }
            BeginCriticalFeedback(false);
        }

        private void BeginCriticalFeedback(bool changedByLearner)
        {
            if (changedByLearner)
                _session.RegenerateCriticalFeedback();
            else
                _session.BeginCriticalFeedback();
            _activePurpose = CoachFeedbackPurpose.CriticalIssue;
            _pendingOperation = PendingOperation.CriticalFeedback;
            _view.SetInputsInteractable(false);
            _view.SetLearnerRequestVisible(false);
            _view.SetStatus("Anna is preparing one short critical observation for you to review.");
            RequestFeedback(_activePurpose, HandleCriticalFeedbackGenerated);
        }

        private void HandleCriticalFeedbackGenerated(CoachFeedbackResult result)
        {
            if (!IsActive || _finished) return;
            if (!IsUsable(result))
            {
                EnterTechnicalError(BuildFeedbackFailureMessage(result),
                    PendingOperation.CriticalFeedback);
                return;
            }
            _pendingOperation = PendingOperation.None;
            MarkTechnicalRecovery(PendingOperation.CriticalFeedback);
            _criticalFeedback = EnglishLlmInputSanitizer.Sanitize(result.FeedbackText);
            if (string.IsNullOrWhiteSpace(_criticalFeedback))
            {
                EnterTechnicalError(
                    "Anna returned no usable English critical feedback.",
                    PendingOperation.CriticalFeedback);
                return;
            }
            AddConversationTurn(_learnerRequest, _criticalFeedback,
                CoachFeedbackPurpose.CriticalIssue);
            _session.PresentCriticalFeedback();
            _view.SetDialogue("Coach Anna - Critical feedback", _criticalFeedback);
            _view.AppendFeedbackHistory(
                _session.CurrentRoundIndex,
                CoachFeedbackPurpose.CriticalIssue,
                _learnerRequest,
                _criticalFeedback,
                false);
            _view.ClearLearnerRequest();
            _view.SetLearnerRequestVisible(false);
            string eventType = string.IsNullOrWhiteSpace(_learnerRequest)
                ? "critical_feedback_presented"
                : "critical_feedback_updated";
            _host.RecordMicroEvent(eventType, new
            {
                round_index = _session.CurrentRoundIndex,
                agenda_text = _criticalFeedback,
                component = EffectiveFocus().ToString(),
                agenda_source = string.IsNullOrWhiteSpace(_learnerRequest)
                    ? "AiDiagnosis"
                    : "LearnerModifiedRequest",
                learner_request = _learnerRequest,
                feedback_purpose = CoachFeedbackPurpose.CriticalIssue.ToString(),
                coach_turn_index = _conversationHistory.Count
            });
            _learnerRequest = string.Empty;
            _view.SetStatus(Mode == CoachOrchestrationMode.SharedControl
                ? "Review the issue. Accept it, change the request, or continue without Coach support."
                : "Review Anna's critical feedback, then continue to the detailed advice.");
            Render();
        }

        private void AcceptCriticalFeedback(string eventType)
        {
            if (_session.CurrentState != MicroCreeiPracticeState.CriticalFeedbackReview) return;
            _host.RecordMicroEvent(eventType, new
            {
                round_index = _session.CurrentRoundIndex,
                agenda_text = _criticalFeedback,
                component = EffectiveFocus().ToString(),
                learner_control_action = "AcceptIssue"
            });
            CreeiComponent focus = EffectiveFocus();
            _session.StartCoach(focus);
            IssueFeedbackRequest(focus, CoachFeedbackPurpose.TargetedAdvice, false);
        }

        private void HandleSharedChangeRequest()
        {
            if (Mode != CoachOrchestrationMode.SharedControl ||
                _session.CurrentState != MicroCreeiPracticeState.CriticalFeedbackReview) return;
            if (!_view.IsLearnerRequestVisible)
            {
                _view.SetLearnerRequestVisible(true);
                _view.SetLearnerRequestInteractable(true);
                _view.SetStatus("Type or speak what you want Anna to examine instead, then press Change Request again.");
                _host.RecordMicroEvent("critical_feedback_change_requested", new
                {
                    round_index = _session.CurrentRoundIndex,
                    component = EffectiveFocus().ToString()
                });
                RenderButtons();
                return;
            }
            _learnerRequest = SanitizeAndLog(
                _view.LearnerRequestText, "shared_change_request");
            if (string.IsNullOrWhiteSpace(_learnerRequest))
            {
                _view.SetStatus("Enter a request before asking Anna to change the issue.");
                return;
            }
            _host.RecordMicroEvent("learner_followup_submitted", new
            {
                round_index = _session.CurrentRoundIndex,
                component = EffectiveFocus().ToString(),
                learner_request = _learnerRequest,
                request_input_modality = _learnerRequestInputModality
            });
            _learnerRequestInputModality = "text";
            BeginCriticalFeedback(true);
        }

        private void SubmitLearnerQuestion()
        {
            if (_session.ChangedComponents.Count > 0)
            {
                _view.SetStatus("Submit your CREEI changes before asking Anna, so she reads the latest saved version.");
                return;
            }
            _learnerRequest = SanitizeAndLog(_view.LearnerRequestText, "coach_request");
            if (string.IsNullOrWhiteSpace(_learnerRequest))
            {
                _view.SetStatus("Enter an English question before asking Anna.");
                return;
            }
            CreeiComponent focus = EffectiveFocus();
            if (_session.CurrentState is MicroCreeiPracticeState.CoachedRevision or
                MicroCreeiPracticeState.IndependentRevision)
                _session.StartCoachFollowUp(focus);
            else if (_session.CurrentState == MicroCreeiPracticeState.AwaitingCoachDecision)
                _session.StartCoach(focus);
            else
                return;
            _host.RecordMicroEvent("learner_followup_submitted", new
            {
                round_index = _session.CurrentRoundIndex,
                component = focus.ToString(),
                learner_request = _learnerRequest,
                request_input_modality = _learnerRequestInputModality
            });
            _learnerRequestInputModality = "text";
            CoachFeedbackPurpose purpose = Mode == CoachOrchestrationMode.LearnerLed
                ? CoachFeedbackPurpose.LearnerSocratic
                : CoachFeedbackPurpose.ConversationalFollowUp;
            IssueFeedbackRequest(focus, purpose, true);
        }

        private void StartStructuredFollowUp(CoachFeedbackPurpose purpose)
        {
            if (Mode != CoachOrchestrationMode.SharedControl ||
                _session.CurrentState != MicroCreeiPracticeState.CoachedRevision) return;
            _host.RecordMicroEvent(purpose == CoachFeedbackPurpose.Example
                ? "coach_example_requested"
                : "coach_more_suggestions_requested", new
            {
                round_index = _session.CurrentRoundIndex,
                component = EffectiveFocus().ToString(),
                coach_turn_index = _conversationHistory.Count + 1
            });
            _learnerRequest = purpose == CoachFeedbackPurpose.Example
                ? "Please give me an adaptable example."
                : "Please give me another different suggestion.";
            CreeiComponent focus = EffectiveFocus();
            _session.StartCoachFollowUp(focus);
            IssueFeedbackRequest(focus, purpose, true);
        }

        private void ContinueWithoutCoach()
        {
            if (Mode == CoachOrchestrationMode.AiLed) return;
            try
            {
                _session.ContinueWithoutCoach();
                _view.SetLearnerRequestVisible(true);
                _view.SetStatus("Revise and submit the CREEI cards, or ask Anna before making changes.");
                _host.RecordMicroEvent("coach_skipped", new
                {
                    round_index = _session.CurrentRoundIndex,
                    learner_control_action = "ContinueWithoutCoach"
                });
                Render();
            }
            catch (InvalidOperationException exception)
            {
                _view.SetStatus(exception.Message);
            }
        }

        private void IssueFeedbackRequest(
            CreeiComponent focus,
            CoachFeedbackPurpose purpose,
            bool recordRequest)
        {
            _activePurpose = purpose;
            _pendingOperation = PendingOperation.Feedback;
            _view.SetInputsInteractable(false);
            _view.SetLearnerRequestVisible(false);
            _view.SetStatus(purpose switch
            {
                CoachFeedbackPurpose.LearnerSocratic =>
                    "Anna is preparing a concise response and guiding questions...",
                CoachFeedbackPurpose.TargetedAdvice =>
                    "Anna is preparing detailed advice about the issue you reviewed...",
                CoachFeedbackPurpose.DirectAdvice =>
                    "Anna is preparing direct, detailed feedback on your argument...",
                CoachFeedbackPurpose.CreeiModelAnswer =>
                    "Anna is preparing a complete five-part CREEI model argument...",
                CoachFeedbackPurpose.ConversationalFollowUp =>
                    "Anna is preparing a direct answer to your question...",
                CoachFeedbackPurpose.Example => "Anna is preparing one adaptable example...",
                _ => "Anna is preparing one additional suggestion..."
            });
            if (recordRequest)
                _host.RecordMicroEvent("CoachRequestSubmitted", new
                {
                    round_index = _session.CurrentRoundIndex,
                    component = EffectiveFocus().ToString(),
                    learner_request = _learnerRequest,
                    feedback_purpose = purpose.ToString()
                });
            RequestFeedback(purpose, HandleFeedbackGenerated);
        }

        private void RequestFeedback(
            CoachFeedbackPurpose purpose,
            Action<CoachFeedbackResult> callback)
        {
            CreeiComponent focus = EffectiveFocus();
            CreeiComponentDiagnosis diagnosis = _session.LatestDiagnosis?.Components
                .FirstOrDefault(item => item.Component == focus) ?? new CreeiComponentDiagnosis
            {
                Component = focus,
                IssueCode = "CREEI_CONNECTION_REQUIRES_REVISION",
                RecommendedNextAction =
                    "Strengthen this part and connect it clearly to the complete argument."
            };
            float startedAt = Time.realtimeSinceStartup;
            _host.RecordMicroEvent("coach_request_started", new
            {
                round_index = _session.CurrentRoundIndex,
                component = focus.ToString(),
                feedback_purpose = purpose.ToString(),
                coach_turn_index = _conversationHistory.Count + 1
            });
            _host.RequestFeedback(
                _session.LastCommittedSnapshot,
                PreviousCommittedSnapshot(),
                focus,
                diagnosis,
                _learnerRequest,
                _currentFeedback,
                purpose,
                _conversationHistory,
                _criticalFeedback,
                result =>
                {
                    int elapsed = result?.RequestElapsedMilliseconds > 0
                        ? result.RequestElapsedMilliseconds
                        : Mathf.Max(0, Mathf.RoundToInt(
                            (Time.realtimeSinceStartup - startedAt) * 1000f));
                    if ((result?.RequestAttemptCount ?? 0) > 1)
                        _host.RecordMicroEvent("coach_request_retried", new
                        {
                            round_index = _session.CurrentRoundIndex,
                            component = focus.ToString(),
                            feedback_purpose = purpose.ToString(),
                            attempt_count = result.RequestAttemptCount,
                            elapsed_milliseconds = elapsed
                        });
                    _host.RecordMicroEvent(IsUsable(result)
                        ? "coach_request_completed"
                        : "coach_request_failed", new
                    {
                        round_index = _session.CurrentRoundIndex,
                        component = focus.ToString(),
                        feedback_purpose = purpose.ToString(),
                        attempt_count = result?.RequestAttemptCount ?? 0,
                        request_bytes = result?.RequestByteCount ?? 0,
                        elapsed_milliseconds = elapsed
                    });
                    callback(result);
                });
        }

        private void HandleFeedbackGenerated(CoachFeedbackResult result)
        {
            if (!IsActive || _finished) return;
            if (!IsUsable(result))
            {
                EnterTechnicalError(BuildFeedbackFailureMessage(result), PendingOperation.Feedback);
                return;
            }
            _pendingOperation = PendingOperation.None;
            MarkTechnicalRecovery(PendingOperation.Feedback);
            bool isCreeiModelAnswer =
                _activePurpose == CoachFeedbackPurpose.CreeiModelAnswer;
            if (isCreeiModelAnswer &&
                !DebateCoachFeedbackGenerator.IsValidCreeiModelExampleSet(
                    result.CreeiModelExamples))
            {
                EnterTechnicalError(
                    "Anna returned an incomplete CREEI model argument.",
                    PendingOperation.Feedback);
                return;
            }
            _aiLedCreeiModelExamples = isCreeiModelAnswer
                ? result.CreeiModelExamples
                : _aiLedCreeiModelExamples;
            _currentFeedback = isCreeiModelAnswer
                ? DebateCoachFeedbackGenerator.BuildCreeiModelHistoryText(
                    _aiLedCreeiModelExamples)
                : EnglishLlmInputSanitizer.Sanitize(result.FeedbackText);
            if (string.IsNullOrWhiteSpace(_currentFeedback))
            {
                EnterTechnicalError(
                    "Anna returned no usable English feedback.", PendingOperation.Feedback);
                return;
            }
            AddConversationTurn(_learnerRequest, _currentFeedback, _activePurpose);
            if (isCreeiModelAnswer)
            {
                _view.SetDialogue(string.Empty, string.Empty);
                _view.ShowCreeiModelExamples(_aiLedCreeiModelExamples);
            }
            else
            {
                _view.SetDialogue("Coach Anna", _currentFeedback);
            }
            _view.SetStatus("Listen to Anna, then choose your next step.");
            _view.SetInputsInteractable(true);
            if (isCreeiModelAnswer)
            {
                _host.RecordMicroEvent("ai_creei_model_presented", new
                {
                    round_index = _session.CurrentRoundIndex,
                    turn_index = _conversationHistory.Count,
                    coach_turn_index = _conversationHistory.Count,
                    actor = "coach",
                    initiator = "coach",
                    target = "whole_argument",
                    feedback_purpose = CoachFeedbackPurpose.CreeiModelAnswer.ToString(),
                    example_set_version = "creei-worked-example-v1",
                    claim_example = _aiLedCreeiModelExamples.Claim,
                    reason_example = _aiLedCreeiModelExamples.Reason,
                    evidence_example = _aiLedCreeiModelExamples.Evidence,
                    explanation_example = _aiLedCreeiModelExamples.Explanation,
                    impact_example = _aiLedCreeiModelExamples.Impact,
                    feedback_text = _currentFeedback
                });
            }
            else
            {
                string eventType =
                    _activePurpose is CoachFeedbackPurpose.TargetedAdvice or
                        CoachFeedbackPurpose.DirectAdvice
                        ? "coach_advice_presented"
                        : "CoachFeedbackPresented";
                _host.RecordMicroEvent(eventType, new
                {
                    round_index = _session.CurrentRoundIndex,
                    component = EffectiveFocus().ToString(),
                    learner_request = _learnerRequest,
                    feedback_text = _currentFeedback,
                    feedback_purpose = _activePurpose.ToString(),
                    coach_turn_index = _conversationHistory.Count
                });
            }
            string requestForHistory = _learnerRequest;
            CoachFeedbackPurpose purposeForHistory = _activePurpose;
            string feedbackForHistory = _currentFeedback;
            string speechText = isCreeiModelAnswer
                ? DebateCoachFeedbackGenerator.BuildCreeiModelSpeechText(
                    _aiLedCreeiModelExamples)
                : _currentFeedback;
            _host.SpeakAnna(speechText, spoken =>
            {
                if (!IsActive || _session.CurrentState != MicroCreeiPracticeState.CoachSpeaking)
                    return;
                _view.AppendFeedbackHistory(
                    _session.CurrentRoundIndex,
                    purposeForHistory,
                    requestForHistory,
                    feedbackForHistory,
                    spoken);
                _host.RecordMicroEvent("CoachFeedbackSpoken", new
                {
                    round_index = _session.CurrentRoundIndex,
                    feedback_purpose = purposeForHistory.ToString(),
                    spoken
                });
                if (purposeForHistory == CoachFeedbackPurpose.Example) _exampleCount++;
                _learnerRequest = string.Empty;
                _view.ClearLearnerRequest();
                _session.CompleteCoachSpeech();
                _sharedFollowupsVisible = Mode == CoachOrchestrationMode.SharedControl;
                _view.SetLearnerRequestVisible(true);
                if (Mode == CoachOrchestrationMode.LearnerLed)
                {
                    _view.SetLearnerRequestVisible(true);
                    _view.SetStatus("Ask Anna another question, or revise the cards when you are satisfied.");
                }
                else if (Mode == CoachOrchestrationMode.SharedControl)
                {
                    _view.SetStatus("Ask Anna, request an example, or revise and submit the CREEI cards.");
                }
                else
                {
                    _view.SetStatus(isCreeiModelAnswer
                        ? "Use Anna's five examples as a guide. Ask Anna a question, or revise and submit your own CREEI cards."
                        : "Ask Anna another question, or revise and submit the CREEI cards.");
                }
                Render();
            });
        }

        private static bool IsUsable(CoachFeedbackResult result) =>
            result != null && result.Source == CoachFeedbackSource.OpenAI &&
            (!string.IsNullOrWhiteSpace(result.FeedbackText) ||
             DebateCoachFeedbackGenerator.IsValidCreeiModelExampleSet(
                 result.CreeiModelExamples));

        private void AddConversationTurn(
            string learnerRequest,
            string coachResponse,
            CoachFeedbackPurpose purpose)
        {
            _conversationHistory.Add(new CoachConversationTurn
            {
                TurnIndex = _conversationHistory.Count + 1,
                LearnerRequest = learnerRequest?.Trim() ?? string.Empty,
                CoachResponse = coachResponse?.Trim() ?? string.Empty,
                Purpose = purpose
            });
        }

        private CreeiComponent EffectiveFocus() =>
            _session.LatestDiagnosis?.PrimaryIssue ??
            _session.CurrentCoachFocus ??
            CreeiComponent.Explanation;

        public static string BuildFeedbackFailureMessage(CoachFeedbackResult result)
        {
            const string summary = "Anna's feedback could not be generated.";
            if (result == null) return summary + " No result reached the workbench.";
            string detail = result.DebugInfo?.Trim() ?? string.Empty;
            return string.IsNullOrWhiteSpace(detail) ? summary : summary + " " + detail;
        }

        private void CompleteRound()
        {
            _host.RecordMicroEvent("RoundCompleted", new
            {
                round_index = _session.CurrentRoundIndex,
                completed_rounds = _session.CompletedRoundCount,
                coach_feedback_turns = _conversationHistory.Count
            });
            if (Mode == CoachOrchestrationMode.AiLed)
            {
                if (_session.RemainingSeconds < 30f)
                {
                    FinishPracticeOne();
                    return;
                }
                BeginNextRound();
                return;
            }
            _view.SetStatus("Coaching round complete. Start another coaching round or finish Practice 1.");
            Render();
        }

        private void BeginNextRound()
        {
            if (_session.CurrentState != MicroCreeiPracticeState.RoundComplete) return;
            _session.BeginNextRound();
            _criticalFeedback = string.Empty;
            _currentFeedback = string.Empty;
            _aiLedCreeiModelExamples = null;
            _learnerRequest = string.Empty;
            _sharedFollowupsVisible = false;
            _view.ClearCreeiModelExamples();
            _view.SetDialogue(string.Empty, string.Empty);
            PresentCoachOpportunity();
            Render();
        }

        private void EnterTechnicalError(string message, PendingOperation operation)
        {
            if (!_technicalFailureActive || _technicalFailureOperation != operation)
                _technicalRetryCount = 0;
            _technicalFailureActive = true;
            _technicalFailureOperation = operation;
            _pendingOperation = operation;
            _session.EnterTechnicalError();
            _host.RecordMicroTechnicalFailure(
                operation.ToString(), message ?? "Workbench operation failed.",
                _technicalRetryCount, false);
            _view.SetInputsInteractable(false);
            _view.SetStatus(message +
                            " Use Retry or Technical Skip. The last committed snapshot is preserved.");
            RenderButtons();
        }

        private void RetryTechnicalOperation()
        {
            if (_session.CurrentState != MicroCreeiPracticeState.TechnicalError) return;
            PendingOperation operation = _pendingOperation;
            _technicalRetryCount++;
            _session.RetryTechnicalOperation();
            switch (operation)
            {
                case PendingOperation.Diagnosis:
                    BeginDiagnosis();
                    break;
                case PendingOperation.CriticalFeedback:
                    RequestFeedback(CoachFeedbackPurpose.CriticalIssue,
                        HandleCriticalFeedbackGenerated);
                    break;
                case PendingOperation.Feedback:
                    RequestFeedback(_activePurpose, HandleFeedbackGenerated);
                    break;
            }
        }

        private void MarkTechnicalRecovery(PendingOperation operation)
        {
            if (!_technicalFailureActive || _technicalFailureOperation != operation) return;
            _host.RecordMicroTechnicalFailure(
                operation.ToString(), "Recovered after retry.",
                _technicalRetryCount, true);
            _technicalFailureActive = false;
            _technicalFailureOperation = PendingOperation.None;
        }

        private void TechnicalSkip()
        {
            if (_session.CurrentState != MicroCreeiPracticeState.TechnicalError) return;
            PendingOperation failed = _pendingOperation;
            _session.RetryTechnicalOperation();
            _host.RecordMicroEvent("TechnicalSkip", new
            {
                operation = failed.ToString(),
                round_index = _session.CurrentRoundIndex
            });
            if ((failed is PendingOperation.CriticalFeedback or PendingOperation.Feedback) &&
                Mode != CoachOrchestrationMode.AiLed)
            {
                if (_session.CurrentState == MicroCreeiPracticeState.CoachSpeaking)
                    _session.CompleteCoachSpeech();
                else if (_session.CurrentState is MicroCreeiPracticeState.AwaitingCoachDecision or
                         MicroCreeiPracticeState.CriticalFeedbackGenerating or
                         MicroCreeiPracticeState.CriticalFeedbackReview)
                    _session.ContinueWithoutCoach();
                Render();
                return;
            }
            CompletePractice(false);
        }

        private void LogSnapshotIfNeeded()
        {
            CreeiArgumentSnapshot snapshot = _session?.LastCommittedSnapshot;
            if (snapshot == null || snapshot.SnapshotId == _lastLoggedSnapshotId) return;
            _snapshotLogger?.LogSnapshot(new MicroCreeiSnapshotRecord
            {
                ParticipantId = _host.ParticipantId,
                SessionId = _host.SessionId,
                OrchestrationMode = Mode,
                TopicId = _host.TopicId,
                Snapshot = snapshot,
                SelectedComponents = _session.ActiveComponent.HasValue
                    ? new[] { _session.ActiveComponent.Value }
                    : Array.Empty<CreeiComponent>(),
                ChallengeFocus = null,
                CoachFocus = _session.CurrentCoachFocus,
                DiagnosisModelVersion = _host.DiagnosisModelVersion,
                PolicyVersion = _host.PolicyVersion
            });
            _lastLoggedSnapshotId = snapshot.SnapshotId;
        }

        private CreeiArgumentSnapshot PreviousCommittedSnapshot()
        {
            IReadOnlyList<CreeiArgumentSnapshot> snapshots = _session?.Snapshots;
            return snapshots != null && snapshots.Count > 1
                ? snapshots[snapshots.Count - 2]
                : null;
        }

        private void Render()
        {
            if (_view == null || _session == null) return;
            _view.SetRemainingSeconds(_session.RemainingSeconds);
            bool editable = _session.CurrentState is MicroCreeiPracticeState.Drafting or
                MicroCreeiPracticeState.ReadyToSubmit or
                MicroCreeiPracticeState.IndependentRevision or
                MicroCreeiPracticeState.CoachedRevision or
                MicroCreeiPracticeState.CoachSpeaking;
            _view.SetInputsInteractable(editable && !IsRecording);
            _view.SetLearnerRequestInteractable(
                ShouldRouteVoiceToLearnerRequest(
                    _session.CurrentState,
                    Mode,
                    _view.IsLearnerRequestVisible,
                    false) && !IsRecording);
            foreach (CreeiComponent component in Enum.GetValues(typeof(CreeiComponent)))
                UpdateCardState(component);
            RenderButtons();
        }

        private void RenderButtons()
        {
            if (_view == null || _session == null) return;
            _view.HideAllActionButtons();
            if (IsRecording)
            {
                _view.SetButtonVisible(CoachWorkbenchAction.Retry, false);
                _view.SetRestartRecordingVisible(true);
                return;
            }
            _view.SetRestartRecordingVisible(false);
            switch (_session.CurrentState)
            {
                case MicroCreeiPracticeState.ReadyToSubmit:
                case MicroCreeiPracticeState.IndependentRevision:
                case MicroCreeiPracticeState.CoachedRevision:
                    _view.SetSubmitVisible(true,
                        _session.CurrentDraft.IsComplete &&
                        (_session.LastCommittedSnapshot == null ||
                         _session.ChangedComponents.Count > 0));
                    if (CanAskCoachAgain(_session.CurrentState, Mode))
                        _view.SetButtonVisible(CoachWorkbenchAction.AskCoach, true);
                    _view.SetButtonVisible(CoachWorkbenchAction.FinishPractice,
                        _session.CanFinishPractice);
                    if (Mode == CoachOrchestrationMode.SharedControl && _sharedFollowupsVisible)
                    {
                        _view.SetButtonVisible(CoachWorkbenchAction.NeedExample, true);
                        _view.SetActionLabel(CoachWorkbenchAction.NeedExample,
                            _exampleCount > 0 ? "Another Example?" : "Need an Example?");
                        _view.SetButtonVisible(CoachWorkbenchAction.NeedMoreSuggestions, true);
                        _view.SetButtonVisible(CoachWorkbenchAction.UseAdviceAndRevise, true);
                    }
                    break;
                case MicroCreeiPracticeState.AwaitingCoachDecision:
                    if (Mode == CoachOrchestrationMode.LearnerLed)
                    {
                        _view.SetButtonVisible(CoachWorkbenchAction.AskCoach, true);
                        _view.SetButtonVisible(CoachWorkbenchAction.ContinueWithoutCoach, true);
                    }
                    break;
                case MicroCreeiPracticeState.CriticalFeedbackReview:
                    if (Mode == CoachOrchestrationMode.SharedControl)
                    {
                        _view.SetButtonVisible(CoachWorkbenchAction.AcceptIssue, true);
                        _view.SetButtonVisible(CoachWorkbenchAction.ChangeRequest, true);
                        _view.SetButtonVisible(CoachWorkbenchAction.ContinueWithoutCoach, true);
                    }
                    break;
                case MicroCreeiPracticeState.RoundComplete:
                    _view.SetButtonVisible(CoachWorkbenchAction.FinishPractice,
                        _session.CanFinishPractice);
                    break;
                case MicroCreeiPracticeState.TechnicalError:
                    _view.SetButtonVisible(CoachWorkbenchAction.Retry, true);
                    _view.SetButtonVisible(CoachWorkbenchAction.TechnicalSkip, true);
                    break;
            }
        }

        private void UpdateCardState(CreeiComponent component)
        {
            string value = _session.CurrentDraft.GetText(component);
            string state;
            if (string.IsNullOrWhiteSpace(value)) state = "Empty";
            else if (_session.LastCommittedSnapshot == null) state = "Draft";
            else if (_session.ChangedComponents.Contains(component)) state = "Draft";
            else state = "Submitted";
            _view.SetCardState(component, state);
        }

        public void ToggleComponentRecording()
        {
            if (!IsActive || _session == null) return;
            if (_transcriber != null && _transcriber.IsSessionActive)
            {
                _transcriber.StopSession();
                _view.SetStatus(_recordingLearnerRequest
                    ? "Finalizing your request to Anna..."
                    : "Finalizing speech for the selected CREEI card...");
                return;
            }
            if (_host.IsNpcSpeechActive)
            {
                _view.SetStatus("Wait until Anna finishes speaking before using voice input.");
                return;
            }
            _view.ReleaseTextFocusForVoice();
            if (_view.VoiceTarget == WorkbenchVoiceTarget.CoachRequest)
            {
                if (!ShouldRouteVoiceToLearnerRequest(
                    _session.CurrentState,
                    Mode,
                    _view.IsLearnerRequestVisible,
                    false))
                {
                    _view.SetStatus("Ask Coach voice input is not available at this step.");
                    return;
                }
                if (_transcriber == null)
                {
                    _view.SetStatus("Voice input is unavailable. Type your request to Anna.");
                    return;
                }
                _recordingLearnerRequest = true;
                _learnerRequestVoicePrefix = _view.LearnerRequestText;
                _view.SetInputsInteractable(false);
                _view.SetStatus("Connecting voice input for your request to Anna...");
                _transcriber.StartSession(
                    MicrophoneManager.Instance?.SelectedMicrophoneName ?? string.Empty);
                return;
            }
            if (!CanToggleComponentRecording(
                    _session.ActiveComponent.HasValue,
                    _view.IsAnyTextInputFocused,
                    _host.IsNpcSpeechActive))
            {
                _view.SetStatus(_view.IsAnyTextInputFocused
                    ? "Typing mode is active. Click Select for voice, then press T."
                    : _host.IsNpcSpeechActive
                        ? "Wait until Anna finishes speaking."
                        : "Select a CREEI card before pressing T.");
                return;
            }
            if (_transcriber == null)
            {
                _view.SetStatus("Voice input is unavailable. Continue by typing in the card.");
                return;
            }
            _recordingComponent = _session.ActiveComponent;
            _voiceOriginalText = _session.CurrentDraft.GetText(_recordingComponent.Value);
            _view.SetInputsInteractable(false);
            _view.SetStatus("Connecting to English realtime transcription...");
            _host.RecordMicroEvent("ComponentVoiceStarted", new
            {
                component = _recordingComponent.Value.ToString(),
                round_index = _session.CurrentRoundIndex
            });
            _transcriber.StartSession(
                MicrophoneManager.Instance?.SelectedMicrophoneName ?? string.Empty);
        }

        public void RestartComponentRecording()
        {
            if (!IsActive || _transcriber == null || !_recordingComponent.HasValue) return;
            _transcriber.CancelSession();
            _recording = false;
            _view.SetComponentText(_recordingComponent.Value, _voiceOriginalText);
            _view.SetStatus("Restarting voice input. The card's previous text is preserved.");
            if (_restartRoutine != null) StopCoroutine(_restartRoutine);
            _restartRoutine = StartCoroutine(RestartWhenIdle());
        }

        private IEnumerator RestartWhenIdle()
        {
            while (_transcriber != null && _transcriber.IsSessionActive) yield return null;
            _restartRoutine = null;
            if (!IsActive || !_recordingComponent.HasValue) yield break;
            _session.SetActiveComponent(_recordingComponent.Value);
            ToggleComponentRecording();
        }

        private void HandleTranscriptionStarted()
        {
            if (!IsActive) return;
            _recording = true;
            _view.SetStatus(_recordingLearnerRequest
                ? "Listening to your request for Anna... Press T to stop."
                : $"Listening for {_recordingComponent}... Press T to stop.");
            RenderButtons();
        }

        private void HandleTranscriptUpdated(string transcript)
        {
            if (!IsActive) return;
            if (_recordingLearnerRequest)
            {
                string preview = EnglishLlmInputSanitizer.Sanitize(transcript);
                _view.SetLearnerRequestText(string.IsNullOrWhiteSpace(_learnerRequestVoicePrefix)
                    ? preview
                    : _learnerRequestVoicePrefix.TrimEnd() +
                      (string.IsNullOrWhiteSpace(preview) ? string.Empty : " " + preview));
                return;
            }
            if (!_recordingComponent.HasValue) return;
            string safePreview = EnglishLlmInputSanitizer.Sanitize(transcript);
            _view.SetStatus(string.IsNullOrWhiteSpace(safePreview)
                ? $"Listening for {_recordingComponent.Value}..."
                : "Voice preview: " + safePreview);
        }

        private void HandleTranscriptionCompleted(string transcript)
        {
            if (!IsActive) return;
            _host.RecordMicroEvent("raw_voice_transcript_received", new
            {
                round_index = _session?.CurrentRoundIndex ?? 0,
                input_target = _recordingLearnerRequest
                    ? "CoachRequest"
                    : _recordingComponent?.ToString() ?? "None",
                raw_transcript = transcript ?? string.Empty
            });
            if (_recordingLearnerRequest)
            {
                _recording = false;
                _recordingLearnerRequest = false;
                string finalRequest = SanitizeAndLog(transcript, "coach_request_voice");
                string merged = string.IsNullOrWhiteSpace(_learnerRequestVoicePrefix)
                    ? finalRequest
                    : _learnerRequestVoicePrefix.TrimEnd() +
                      (string.IsNullOrWhiteSpace(finalRequest) ? string.Empty : " " + finalRequest);
                _view.SetLearnerRequestText(merged);
                if (!string.IsNullOrWhiteSpace(finalRequest))
                    _learnerRequestInputModality = "voice";
                _learnerRequestVoicePrefix = string.Empty;
                _view.SetStatus(string.IsNullOrWhiteSpace(finalRequest)
                    ? "No speech was detected. Type or try your request again."
                    : "Voice text added to your request. Edit it or confirm the action.");
                Render();
                return;
            }
            if (!_recordingComponent.HasValue) return;
            CreeiComponent component = _recordingComponent.Value;
            _recording = false;
            _recordingComponent = null;
            string final = SanitizeAndLog(transcript, "component_voice");
            if (!string.IsNullOrWhiteSpace(final))
            {
                string merged = string.IsNullOrWhiteSpace(_voiceOriginalText)
                    ? final
                    : _voiceOriginalText.TrimEnd() + " " + final;
                _session.SetComponentText(component, merged, "voice");
                _view.SetComponentText(component, merged);
                _view.SetStatus($"Voice text added to {component}. Edit it or submit the structure.");
            }
            else
            {
                _view.SetComponentText(component, _voiceOriginalText);
                _view.SetStatus("No final speech was received. The previous card text was preserved.");
            }
            _voiceOriginalText = string.Empty;
            Render();
        }

        private void HandleTranscriptionFailed(string error)
        {
            if (!IsActive) return;
            _recording = false;
            if (_recordingLearnerRequest)
            {
                _recordingLearnerRequest = false;
                _view.SetLearnerRequestText(_learnerRequestVoicePrefix);
                _learnerRequestVoicePrefix = string.Empty;
                _view.SetStatus((error ?? "ASR failed.") +
                                " Your earlier request text was preserved.");
                Render();
                return;
            }
            if (_recordingComponent.HasValue)
                _view.SetComponentText(_recordingComponent.Value, _voiceOriginalText);
            _recordingComponent = null;
            _voiceOriginalText = string.Empty;
            _view.SetStatus((error ?? "ASR failed.") +
                            " The previous card text was preserved; type or record again.");
            Render();
        }

        private void Subscribe()
        {
            _view.ComponentSelected += SetActiveComponent;
            _view.ComponentTextChanged += HandleComponentTextChanged;
            _view.RestartRecordingRequested += RestartComponentRecording;
            _view.SubmitRequested += SubmitArgumentSnapshot;
            _view.CoachActionRequested += HandleCoachAction;
            if (_transcriber == null) return;
            _transcriber.SessionStarted += HandleTranscriptionStarted;
            _transcriber.TranscriptUpdated += HandleTranscriptUpdated;
            _transcriber.SessionCompleted += HandleTranscriptionCompleted;
            _transcriber.SessionFailed += HandleTranscriptionFailed;
        }

        private void Unsubscribe()
        {
            if (_view != null)
            {
                _view.ComponentSelected -= SetActiveComponent;
                _view.ComponentTextChanged -= HandleComponentTextChanged;
                _view.RestartRecordingRequested -= RestartComponentRecording;
                _view.SubmitRequested -= SubmitArgumentSnapshot;
                _view.CoachActionRequested -= HandleCoachAction;
            }
            if (_transcriber == null) return;
            _transcriber.SessionStarted -= HandleTranscriptionStarted;
            _transcriber.TranscriptUpdated -= HandleTranscriptUpdated;
            _transcriber.SessionCompleted -= HandleTranscriptionCompleted;
            _transcriber.SessionFailed -= HandleTranscriptionFailed;
        }

        private void HandleComponentTextChanged(CreeiComponent component, string text) =>
            SetComponentText(component, text, "keyboard");

        private static string BuildCreeiGapSummary(CreeiArgumentDiagnosisResult diagnosis)
        {
            if (diagnosis?.RankedIssues == null || diagnosis.RankedIssues.Length == 0)
                return "The argument has no clearly identified CREEI gap.";
            string components = string.Join(", ", diagnosis.RankedIssues
                .Take(3).Select(value => value.ToString()));
            return $"The weakest or missing CREEI components are {components}.";
        }

        private void SanitizeDraftForSubmission()
        {
            foreach (CreeiComponent component in Enum.GetValues(typeof(CreeiComponent)))
            {
                string original = _session.CurrentDraft.GetText(component);
                string cleaned = SanitizeAndLog(original,
                    "creei_" + component.ToString().ToLowerInvariant());
                if (string.Equals(original, cleaned, StringComparison.Ordinal)) continue;
                _session.SetComponentText(component, cleaned,
                    _session.CurrentDraft.GetInputModality(component));
                _view.SetComponentText(component, cleaned);
                UpdateCardState(component);
            }
        }

        private string SanitizeAndLog(string value, string field)
        {
            string cleaned = EnglishLlmInputSanitizer.Sanitize(
                value, out int removedCharacters);
            if (removedCharacters <= 0) return cleaned;
            _host.RecordMicroEvent("llm_input_sanitized", new
            {
                round_index = _session?.CurrentRoundIndex ?? 0,
                input_field = field ?? string.Empty,
                removed_character_count = removedCharacters,
                cleaned_character_count = cleaned.Length
            });
            return cleaned;
        }

        private void CompletePractice(bool timedOut)
        {
            if (_finished) return;
            _finished = true;
            IsActive = false;
            CleanupActiveOperation();
            _view.Hide();
            _host.AdvanceFromMicroCreei(_session?.CompletedRoundCount ?? 0, timedOut);
        }

        private void CleanupActiveOperation()
        {
            if (_restartRoutine != null)
            {
                StopCoroutine(_restartRoutine);
                _restartRoutine = null;
            }
            _transcriber?.CancelSession();
            _host?.CancelMicroOperations();
            _recording = false;
            _recordingLearnerRequest = false;
            _recordingComponent = null;
            _pendingOperation = PendingOperation.None;
        }

        private void OnDestroy()
        {
            CleanupActiveOperation();
            Unsubscribe();
        }
    }
}
