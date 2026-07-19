using System;
using System.Collections;

namespace Game.Debate
{
    public enum GuidedPracticeStageKind
    {
        Claim,
        Reason,
        Evidence,
        Explanation,
        Impact,
        IntegratedPracticeOne,
        IntegratedPracticeTwo
    }

    public enum GuidedStageSkipKind
    {
        None,
        Safety,
        Technical
    }

    public enum GuidedCreeiStudyPhase
    {
        Setup,
        ReadyToRecord,
        Recording,
        AwaitingTranscript,
        ConfirmingTranscript,
        AwaitingCoachChoice,
        Diagnosing,
        Evaluating,
        GeneratingFeedback,
        AwaitingRevision,
        TechnicalError,
        Complete
    }

    [Serializable]
    public sealed class CoachSuggestion
    {
        public string SuggestionId = string.Empty;
        public int Rank;
        public string IssueCode = string.Empty;
        public string Focus = string.Empty;
        public string ProblemDescription = string.Empty;
        public string ImprovementGoal = string.Empty;
    }

    [Serializable]
    public sealed class CoachStageEvaluationResult
    {
        public bool Success;
        public bool CriterionMet;
        public float Confidence;
        public string EvidenceSpan = string.Empty;
        public string IssueCode = string.Empty;
        public string NextAction = string.Empty;
        public string Error = string.Empty;
        public string RawJson = string.Empty;
        public string RubricVersion = string.Empty;

        public bool IsPassing => Success && CriterionMet && Confidence >= 0.65f;
    }

    [Serializable]
    public sealed class GuidedCoachPolicyInput
    {
        public CoachOrchestrationMode Mode;
        public GuidedPracticeStageKind StageKind;
        public CoachLearnerAction LearnerAction;
        public int VisibleFeedbackTurns;
        public bool TranscriptConfirmed;
        public bool HasLearnerRequest;
        public bool HasSelectedSuggestion;
        public bool IsRevision;
        public CoachStageEvaluationResult Evaluation;
    }

    [Serializable]
    public sealed class GuidedCoachPolicyDecision
    {
        public CoachPolicyAction Action;
        public bool MayAdvance;
        public bool RequiresRevision;
        public bool ShowLearnerRequest;
        public bool ShowSharedSuggestions;
        public bool AutoGenerateFeedback;
        public bool RunEvaluation;
        public CoachTerminationReason TerminationReason;
        public string PolicyReason = string.Empty;
    }

    public sealed class GuidedCoachPolicy
    {
        public GuidedCoachPolicyDecision Evaluate(GuidedCoachPolicyInput input)
        {
            input ??= new GuidedCoachPolicyInput();
            GuidedCoachPolicyDecision decision = new();
            if (!input.TranscriptConfirmed)
            {
                decision.PolicyReason = "TranscriptMustBeConfirmed";
                return decision;
            }

            if (input.LearnerAction == CoachLearnerAction.ExitCoaching)
            {
                decision.Action = CoachPolicyAction.End;
                decision.MayAdvance = true;
                decision.TerminationReason = CoachTerminationReason.SafetyOverride;
                decision.PolicyReason = "SafetySkip";
                return decision;
            }

            if (input.StageKind == GuidedPracticeStageKind.IntegratedPracticeTwo)
            {
                decision.RunEvaluation = true;
                decision.MayAdvance = input.Evaluation != null;
                decision.PolicyReason = "SilentPostTestDiagnosis";
                return decision;
            }

            bool feedbackBudgetUsed = input.Mode != CoachOrchestrationMode.AiLed &&
                                      input.VisibleFeedbackTurns >= 2;
            switch (input.Mode)
            {
                case CoachOrchestrationMode.LearnerLed:
                    decision.MayAdvance = true;
                    decision.ShowLearnerRequest = true;
                    if (input.LearnerAction == CoachLearnerAction.RequestCoach)
                    {
                        decision.Action = feedbackBudgetUsed || !input.HasLearnerRequest
                            ? CoachPolicyAction.End
                            : CoachPolicyAction.GenerateFeedback;
                        decision.PolicyReason = feedbackBudgetUsed
                            ? "VisibleFeedbackBudgetReached"
                            : input.HasLearnerRequest
                                ? "LearnerRequestedFeedback"
                                : "LearnerRequestRequired";
                    }
                    else
                    {
                        decision.PolicyReason = "LearnerMayAdvanceOrRequestCoach";
                    }
                    return decision;

                case CoachOrchestrationMode.SharedControl:
                    decision.MayAdvance = true;
                    decision.ShowSharedSuggestions = true;
                    if (input.IsRevision && !feedbackBudgetUsed)
                    {
                        decision.Action = CoachPolicyAction.GenerateFeedback;
                        decision.AutoGenerateFeedback = true;
                        decision.RunEvaluation = true;
                        decision.PolicyReason = "SharedRevisionAutoReassessment";
                    }
                    else if (input.LearnerAction == CoachLearnerAction.ConfirmFocus &&
                             input.HasSelectedSuggestion && !feedbackBudgetUsed)
                    {
                        decision.Action = CoachPolicyAction.GenerateFeedback;
                        decision.PolicyReason = "SharedSuggestionConfirmed";
                    }
                    else if (feedbackBudgetUsed)
                    {
                        decision.Action = CoachPolicyAction.End;
                        decision.PolicyReason = "VisibleFeedbackBudgetReached";
                    }
                    else
                    {
                        decision.PolicyReason = "SharedDiagnosisSuggestionsRequired";
                    }
                    return decision;

                case CoachOrchestrationMode.AiLed:
                    if (input.StageKind == GuidedPracticeStageKind.IntegratedPracticeOne)
                    {
                        decision.Action = CoachPolicyAction.GenerateFeedback;
                        decision.AutoGenerateFeedback = true;
                        decision.RunEvaluation = true;
                        decision.MayAdvance = true;
                        decision.PolicyReason = "AiLedIntegratedFeedbackWithoutGate";
                        return decision;
                    }

                    decision.AutoGenerateFeedback = true;
                    decision.RunEvaluation = input.IsRevision;
                    if (!input.IsRevision)
                    {
                        decision.Action = CoachPolicyAction.GenerateFeedback;
                        decision.RequiresRevision = true;
                        decision.PolicyReason = "AiLedInitialFeedbackRequiresRevision";
                        return decision;
                    }

                    if (input.Evaluation != null && input.Evaluation.IsPassing)
                    {
                        decision.Action = CoachPolicyAction.End;
                        decision.MayAdvance = true;
                        decision.PolicyReason = "StageCriterionPassed";
                        return decision;
                    }

                    decision.Action = CoachPolicyAction.GenerateFeedback;
                    decision.RequiresRevision = true;
                    decision.PolicyReason = "StageCriterionNeedsRevision";
                    return decision;

                default:
                    decision.MayAdvance = true;
                    decision.PolicyReason = "CoachDisabled";
                    return decision;
            }
        }
    }

    public static class GuidedCreeiRubric
    {
        public const string Version = "guided-creei-rubric-v1";

        public static string GetCriterion(GuidedPracticeStageKind stage)
        {
            return stage switch
            {
                GuidedPracticeStageKind.Claim => "The response clearly states one identifiable debate position.",
                GuidedPracticeStageKind.Reason => "The response gives at least one relevant reason that directly supports the learner's confirmed claim.",
                GuidedPracticeStageKind.Evidence => "The response gives at least one relevant and specific example, fact, or experience.",
                GuidedPracticeStageKind.Explanation => "The response explicitly explains why the evidence supports the reason or claim.",
                GuidedPracticeStageKind.Impact => "The response identifies who is affected, the consequence, and why it matters.",
                _ => throw new ArgumentOutOfRangeException(nameof(stage), stage, "Integrated practice does not use a single-stage rubric.")
            };
        }
    }

    [Serializable]
    public sealed class CoachStageEvaluationContext
    {
        public GuidedPracticeStageKind StageKind;
        public string Topic = string.Empty;
        public string LearnerStance = string.Empty;
        public string ConfirmedTranscript = string.Empty;
        public string PreviousConfirmedStages = string.Empty;
        public int AttemptIndex;
        public int RevisionIndex;
    }

    public interface ICoachStageEvaluator
    {
        IEnumerator Evaluate(CoachStageEvaluationContext context, Action<CoachStageEvaluationResult> onComplete);
        void Cancel();
    }

    [Serializable]
    public sealed class GuidedCoachEventRecord
    {
        public string ParticipantId = string.Empty;
        public string SessionId = string.Empty;
        public CoachOrchestrationMode Mode;
        public GuidedPracticeStageKind StageKind;
        public int StageIndex;
        public int AttemptIndex;
        public int RevisionIndex;
        public string EventType = string.Empty;
        public string LearnerRequestText = string.Empty;
        public string ConfirmedLearnerText = string.Empty;
        public string RankedSuggestionsJson = string.Empty;
        public string SelectedSuggestionId = string.Empty;
        public string SuggestionModification = string.Empty;
        public string RevisionImprovementStatus = string.Empty;
        public string RevisionImprovementSummary = string.Empty;
        public bool EvaluationPerformed;
        public string AssessmentStatus = string.Empty;
        public bool CriterionMet;
        public float EvaluationConfidence;
        public string EvaluationEvidence = string.Empty;
        public string IssueCode = string.Empty;
        public string NextAction = string.Empty;
        public string FeedbackText = string.Empty;
        public int VisibleFeedbackTurn;
        public GuidedStageSkipKind SkipKind;
        public CoachTerminationReason TerminationReason;
        public ControlOwner StartAuthority;
        public ControlOwner AgendaOwner;
        public ControlOwner PacingOwner;
        public ControlOwner TerminationOwner;
        public string DiagnosisModelVersion = string.Empty;
        public string FeedbackModelVersion = string.Empty;
        public string PolicyVersion = string.Empty;
        public string RubricVersion = string.Empty;
        public string EventTimestamp = string.Empty;
    }

    [Serializable]
    public sealed class GuidedCoachStageSummary
    {
        public string ParticipantId = string.Empty;
        public string SessionId = string.Empty;
        public CoachOrchestrationMode Mode;
        public GuidedPracticeStageKind StageKind;
        public int StageIndex;
        public int AttemptCount;
        public int RevisionCount;
        public int VisibleFeedbackCount;
        public string InitialConfirmedText = string.Empty;
        public string FinalConfirmedText = string.Empty;
        public bool EvaluationPerformed;
        public string AssessmentStatus = string.Empty;
        public bool CriterionMet;
        public float EvaluationConfidence;
        public GuidedStageSkipKind SkipKind;
        public CoachTerminationReason TerminationReason;
        public string StartTimestamp = string.Empty;
        public string EndTimestamp = string.Empty;
        public string DiagnosisModelVersion = string.Empty;
        public string FeedbackModelVersion = string.Empty;
        public string PolicyVersion = string.Empty;
        public string RubricVersion = string.Empty;
    }
}
