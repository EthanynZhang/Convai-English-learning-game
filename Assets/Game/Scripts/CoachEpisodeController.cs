using System;
using UnityEngine;

namespace Game.Debate
{
    public sealed class CoachEpisodeController : MonoBehaviour
    {
        private CoachPolicyConfig _config = CoachPolicyConfig.CreateDefault();
        private CoachOrchestrationPolicy _policy;
        private CoachResearchLogger _logger;
        private CoachDiagnosisResult _diagnosis;
        private CoachPolicyDecision _lastDecision;
        private CoachEpisodeSummary _summary;
        private bool _ownershipInitialized;
        private bool _configured;
        private bool _opportunityUsed;
        private bool _ended;
        private float _elapsedSeconds;
        private float _actionWindowRemaining;
        private string _confirmedLearnerText = string.Empty;
        private string _topicId = string.Empty;
        private int _turnId;

        public event Action<CoachPolicyDecision> DecisionMade;
        public event Action<CoachTerminationReason> EpisodeEnded;

        public CoachOrchestrationMode Mode { get; private set; }
        public int PracticeCycleId { get; private set; }
        public CoachEpisodeState State { get; private set; } = CoachEpisodeState.Inactive;
        public CoachTerminationReason TerminationReason { get; private set; }
        public int FeedbackTurnIndex { get; private set; }
        public string ConfirmedFocus { get; private set; } = string.Empty;
        public string LatestFeedbackText { get; private set; } = string.Empty;
        public bool IsActive => !_ended &&
                                State is CoachEpisodeState.Active or
                                    CoachEpisodeState.AwaitingLearnerAction or
                                    CoachEpisodeState.Overridden;
        public bool HasOpenOpportunity => !_ended && _opportunityUsed &&
                                          State is not CoachEpisodeState.Completed and
                                              not CoachEpisodeState.Declined and
                                              not CoachEpisodeState.TimedOut;
        public float RemainingSeconds => Mathf.Max(0f, _config.MaxEpisodeSeconds - _elapsedSeconds);
        public float ElapsedSeconds => Mathf.Max(0f, _elapsedSeconds);
        public float ActionWindowRemaining => Mathf.Max(0f, _actionWindowRemaining);
        public string CoachEpisodeId => _summary?.CoachEpisodeId ?? string.Empty;
        public bool AutomaticTick { get; set; } = true;

        private void Update()
        {
            if (Application.isPlaying && AutomaticTick)
            {
                Advance(Time.unscaledDeltaTime);
            }
        }

        private void OnDisable()
        {
            if (Application.isPlaying && HasOpenOpportunity)
            {
                Complete(CoachTerminationReason.SafetyOverride, ControlOwner.SystemSafety);
            }
        }

        public void Configure(
            CoachOrchestrationMode mode,
            int practiceCycleId,
            CoachPolicyConfig config,
            CoachResearchLogger logger)
        {
            Mode = mode;
            PracticeCycleId = Mathf.Max(1, practiceCycleId);
            _config = config ?? CoachPolicyConfig.CreateDefault();
            _policy = new CoachOrchestrationPolicy(_config);
            _logger = logger;
            _diagnosis = null;
            _lastDecision = null;
            _summary = null;
            _ownershipInitialized = false;
            _configured = true;
            _opportunityUsed = false;
            _ended = false;
            _elapsedSeconds = 0f;
            _actionWindowRemaining = 0f;
            _confirmedLearnerText = string.Empty;
            _topicId = string.Empty;
            _turnId = 0;
            State = CoachEpisodeState.Inactive;
            TerminationReason = CoachTerminationReason.None;
            FeedbackTurnIndex = 0;
            ConfirmedFocus = string.Empty;
            LatestFeedbackText = string.Empty;
        }

        public CoachPolicyDecision BeginOpportunity(
            CoachDiagnosisResult diagnosis,
            string confirmedLearnerText,
            string topicId,
            int turnId)
        {
            EnsureConfigured();
            if (_opportunityUsed)
            {
                return _lastDecision ?? new CoachPolicyDecision
                {
                    Action = CoachPolicyAction.End,
                    NextState = State,
                    TerminationReason = TerminationReason,
                    PolicyReason = "EpisodeOpportunityAlreadyUsed"
                };
            }

            _opportunityUsed = true;
            _diagnosis = diagnosis;
            _confirmedLearnerText = confirmedLearnerText?.Trim() ?? string.Empty;
            _topicId = topicId?.Trim() ?? string.Empty;
            _turnId = turnId;
            State = CoachEpisodeState.Diagnosing;

            CoachPolicyDecision decision = _policy.Evaluate(CreatePolicyInput());
            bool createsEpisode = !(Mode == CoachOrchestrationMode.AiLed &&
                                    decision.Action == CoachPolicyAction.End &&
                                    decision.TerminationReason == CoachTerminationReason.NoEligibleIssue);
            _summary = createsEpisode ? CreateSummary() : null;
            ApplyDecision(decision, "DiagnosisCompleted", CoachLearnerAction.None);
            return decision;
        }

        public CoachPolicyDecision SubmitLearnerAction(
            CoachLearnerAction action,
            string selectedFocus)
        {
            EnsureConfigured();
            if (_ended)
            {
                return _lastDecision ?? new CoachPolicyDecision
                {
                    Action = CoachPolicyAction.End,
                    NextState = State,
                    TerminationReason = TerminationReason,
                    PolicyReason = "EpisodeAlreadyEnded"
                };
            }

            CoachPolicyInput input = CreatePolicyInput();
            input.LearnerAction = action;
            input.LearnerSelectedFocus = selectedFocus?.Trim() ?? string.Empty;
            CoachPolicyDecision decision = _policy.Evaluate(input);
            if (decision.Action is CoachPolicyAction.Start or CoachPolicyAction.Continue)
            {
                if (!string.IsNullOrWhiteSpace(decision.ProposedFocus))
                {
                    ConfirmedFocus = decision.ProposedFocus.Trim();
                }

                if (State is CoachEpisodeState.Available or CoachEpisodeState.Invited)
                {
                    if (_summary != null)
                    {
                        _summary.EpisodeActivated = true;
                        _summary.EpisodeStartTimestamp = DateTimeOffset.UtcNow.ToString("o");
                    }
                }

                if (action == CoachLearnerAction.NeedExample)
                {
                    if (_summary != null) _summary.Level3RequestCount++;
                }

                if (action is CoachLearnerAction.ChangeFocus or CoachLearnerAction.Override)
                {
                    if (_summary != null) _summary.FocusOverrideUsed = true;
                }
            }
            else if (action == CoachLearnerAction.Decline)
            {
                if (_summary != null) _summary.InvitationDeclined = true;
            }

            ApplyDecision(decision, action.ToString(), action);
            return decision;
        }

        public void NotifyFeedbackPresented(string level, string feedbackType, string feedbackText)
        {
            if (_ended || State is not (CoachEpisodeState.Active or CoachEpisodeState.Overridden))
            {
                return;
            }

            FeedbackTurnIndex = Mathf.Min(
                _config.MaxCoachTurnsPerEpisode,
                FeedbackTurnIndex + 1);
            LatestFeedbackText = feedbackText?.Trim() ?? string.Empty;
            CoachEpisodeState before = State;
            State = CoachEpisodeState.AwaitingLearnerAction;
            _actionWindowRemaining = Mode == CoachOrchestrationMode.AiLed
                ? _config.AiLedActionWindowSeconds
                : 0f;
            if (_summary != null)
            {
                _summary.CoachFeedbackTurnCount = FeedbackTurnIndex;
            }

            CoachPolicyDecision feedbackDecision = _policy.Evaluate(CreatePolicyInput());
            feedbackDecision.Action = CoachPolicyAction.GenerateFeedback;
            feedbackDecision.PolicyReason = "FeedbackPresented";
            feedbackDecision.NextState = State;
            LogEvent(
                before,
                State,
                "FeedbackShown",
                CoachLearnerAction.None,
                feedbackDecision,
                level,
                feedbackType,
                LatestFeedbackText);
        }

        public void Advance(float unscaledDeltaTime, bool coachSpeechPlaying = false)
        {
            if (_ended || !IsActive)
            {
                return;
            }

            float delta = Mathf.Max(0f, unscaledDeltaTime);
            _elapsedSeconds = Mathf.Min(_config.MaxEpisodeSeconds, _elapsedSeconds + delta);
            if (_elapsedSeconds >= _config.MaxEpisodeSeconds)
            {
                CoachPolicyDecision timeout = _policy.Evaluate(CreatePolicyInput());
                ApplyDecision(timeout, "EpisodeTimedOut", CoachLearnerAction.None);
                return;
            }

            if (Mode == CoachOrchestrationMode.AiLed &&
                State == CoachEpisodeState.AwaitingLearnerAction &&
                !coachSpeechPlaying)
            {
                _actionWindowRemaining = Mathf.Max(0f, _actionWindowRemaining - delta);
                if (_actionWindowRemaining <= 0f)
                {
                    CoachPolicyInput input = CreatePolicyInput();
                    input.ActionWindowExpired = true;
                    CoachPolicyDecision decision = _policy.Evaluate(input);
                    ApplyDecision(decision, "AiActionWindowElapsed", CoachLearnerAction.None);
                }
            }
        }

        public void Complete(CoachTerminationReason reason, ControlOwner owner)
        {
            if (_ended)
            {
                return;
            }

            CoachPolicyDecision decision = _policy.Evaluate(CreatePolicyInput());
            decision.Action = CoachPolicyAction.End;
            decision.NextState = reason == CoachTerminationReason.TimeLimit
                ? CoachEpisodeState.TimedOut
                : reason == CoachTerminationReason.LearnerDeclined
                    ? CoachEpisodeState.Declined
                    : CoachEpisodeState.Completed;
            decision.TerminationReason = reason;
            decision.TerminationOwner = owner;
            decision.PolicyReason = "EpisodeCompletedExplicitly";
            ApplyDecision(decision, "EpisodeCompleted", CoachLearnerAction.None);
        }

        private CoachPolicyInput CreatePolicyInput()
        {
            return new CoachPolicyInput
            {
                Mode = Mode,
                EpisodeState = State,
                Diagnosis = _diagnosis,
                ConfirmedFocus = ConfirmedFocus,
                CoachTurnIndex = FeedbackTurnIndex,
                ElapsedSeconds = _elapsedSeconds,
                EpisodeOpportunityUsed = _opportunityUsed
            };
        }

        private void ApplyDecision(
            CoachPolicyDecision decision,
            string eventType,
            CoachLearnerAction learnerAction)
        {
            decision ??= new CoachPolicyDecision
            {
                Action = CoachPolicyAction.None,
                NextState = State,
                PolicyReason = "NullPolicyDecision"
            };
            CoachEpisodeState before = State;
            _lastDecision = decision;
            State = decision.NextState;
            if (decision.Action == CoachPolicyAction.Invite && _summary != null)
            {
                _summary.InvitationShown = true;
            }

            if (decision.Action == CoachPolicyAction.AutoStart && _summary != null)
            {
                _summary.EpisodeActivated = true;
                _summary.EpisodeStartTimestamp = DateTimeOffset.UtcNow.ToString("o");
                ConfirmedFocus = decision.ProposedFocus?.Trim() ?? string.Empty;
            }

            if (_summary != null)
            {
                if (string.IsNullOrWhiteSpace(_summary.InitialProposedFocus))
                {
                    _summary.InitialProposedFocus = decision.ProposedFocus ?? string.Empty;
                }

                _summary.FinalConfirmedFocus = ConfirmedFocus;
                if (!_ownershipInitialized)
                {
                    _summary.StartAuthority = decision.StartAuthority;
                    _summary.AgendaOwner = decision.AgendaOwner;
                    _summary.PacingOwner = decision.PacingOwner;
                    _summary.TerminationOwner = decision.TerminationOwner;
                    _ownershipInitialized = true;
                }

                if (learnerAction is CoachLearnerAction.ConfirmFocus or
                    CoachLearnerAction.ChangeFocus or CoachLearnerAction.Override)
                {
                    _summary.AgendaOwner = decision.AgendaOwner;
                }

                if (decision.Action == CoachPolicyAction.End)
                {
                    _summary.TerminationOwner = decision.TerminationOwner;
                }
            }

            LogEvent(before, State, eventType, learnerAction, decision);
            DecisionMade?.Invoke(decision);
            if (decision.Action == CoachPolicyAction.End)
            {
                Finish(decision.TerminationReason);
            }
        }

        private void Finish(CoachTerminationReason reason)
        {
            if (_ended)
            {
                return;
            }

            _ended = true;
            TerminationReason = reason;
            if (_summary != null)
            {
                _summary.EpisodeEndTimestamp = DateTimeOffset.UtcNow.ToString("o");
                _summary.EpisodeDurationSeconds = _elapsedSeconds;
                _summary.FinalConfirmedFocus = ConfirmedFocus;
                _summary.CoachFeedbackTurnCount = FeedbackTurnIndex;
                _summary.TerminationReason = reason;
                _logger?.CompleteEpisode(_summary);
            }

            EpisodeEnded?.Invoke(reason);
        }

        private CoachEpisodeSummary CreateSummary()
        {
            CoachStudySessionSnapshot session = CoachStudySessionContext.Current;
            return new CoachEpisodeSummary
            {
                ParticipantId = session?.ParticipantId ?? string.Empty,
                SessionId = session?.SessionId ?? string.Empty,
                OrchestrationMode = Mode,
                TopicId = _topicId,
                PracticeCycleId = PracticeCycleId,
                TurnId = _turnId,
                CoachEpisodeId = Guid.NewGuid().ToString("N"),
                DiagnosisIssueCode = _diagnosis?.DiagnosisIssueCode ?? string.Empty,
                DiagnosisSeverity = _diagnosis?.Severity ?? 0,
                DiagnosisConfidence = _diagnosis?.Confidence ?? 0f,
                DiagnosisModelVersion = _config.DiagnosisModelVersion,
                FeedbackModelVersion = _config.FeedbackModelVersion,
                PolicyVersion = _config.PolicyVersion
            };
        }

        private void LogEvent(
            CoachEpisodeState before,
            CoachEpisodeState after,
            string eventType,
            CoachLearnerAction learnerAction,
            CoachPolicyDecision decision,
            string feedbackLevel = "",
            string feedbackType = "",
            string feedbackText = "")
        {
            if (_logger == null)
            {
                return;
            }

            CoachStudySessionSnapshot session = CoachStudySessionContext.Current;
            _logger.LogEvent(new CoachEventRecord
            {
                ParticipantId = session?.ParticipantId ?? string.Empty,
                SessionId = session?.SessionId ?? string.Empty,
                OrchestrationMode = Mode,
                Stage = "PracticeDebate",
                TopicId = _topicId,
                PracticeCycleId = PracticeCycleId,
                TurnId = _turnId,
                CoachEpisodeId = _summary?.CoachEpisodeId ?? string.Empty,
                EpisodeStateBefore = before,
                EpisodeStateAfter = after,
                EventType = eventType,
                DiagnosisIssueCode = _diagnosis?.DiagnosisIssueCode ?? string.Empty,
                DiagnosisSeverity = _diagnosis?.Severity ?? 0,
                DiagnosisConfidence = _diagnosis?.Confidence ?? 0f,
                CreeiMissingOrWeakComponents = string.Join("|",
                    _diagnosis?.CreeiMissingOrWeakComponents ?? Array.Empty<string>()),
                CreeiGapSummary = _diagnosis?.CreeiGapSummary ?? string.Empty,
                PolicyAction = decision.Action.ToString(),
                PolicyReason = decision.PolicyReason,
                ProposedFocus = decision.ProposedFocus,
                ConfirmedFocus = ConfirmedFocus,
                StartAuthority = decision.StartAuthority,
                AgendaOwner = decision.AgendaOwner,
                PacingOwner = decision.PacingOwner,
                TerminationOwner = decision.TerminationOwner,
                LearnerControlAction = learnerAction == CoachLearnerAction.None
                    ? string.Empty
                    : learnerAction.ToString(),
                ConfirmedLearnerText = _confirmedLearnerText,
                FocusOverrideUsed = _summary?.FocusOverrideUsed ?? false,
                Level3Requested = learnerAction == CoachLearnerAction.NeedExample,
                CoachFeedbackLevel = feedbackLevel,
                CoachFeedbackType = feedbackType,
                CoachFeedbackText = feedbackText,
                CoachTurnIndex = FeedbackTurnIndex,
                ElapsedEpisodeSeconds = _elapsedSeconds,
                TerminationReason = decision.TerminationReason,
                DiagnosisModelVersion = _config.DiagnosisModelVersion,
                FeedbackModelVersion = _config.FeedbackModelVersion,
                PolicyVersion = _config.PolicyVersion
            });
        }

        private void EnsureConfigured()
        {
            if (!_configured)
            {
                throw new InvalidOperationException("Configure the Coach episode before using it.");
            }
        }
    }
}
