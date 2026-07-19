using System;

namespace Game.Debate
{
    public static class CoachFocusCatalog
    {
        public static readonly string[] Values =
        {
            "Claim", "Reason", "Evidence", "Explanation", "Impact",
            "Ethos", "Pathos", "Logos"
        };

        public static bool TryNormalize(string value, out string normalized)
        {
            string candidate = value?.Trim() ?? string.Empty;
            foreach (string registered in Values)
            {
                if (string.Equals(candidate, registered, StringComparison.OrdinalIgnoreCase))
                {
                    normalized = registered;
                    return true;
                }
            }

            normalized = string.Empty;
            return false;
        }

        public static string Normalize(string value, string fallback = "Explanation")
        {
            return TryNormalize(value, out string normalized)
                ? normalized
                : TryNormalize(fallback, out normalized)
                    ? normalized
                    : "Explanation";
        }
    }

    public enum CoachOrchestrationMode
    {
        Disabled,
        LearnerLed,
        SharedControl,
        AiLed
    }

    public enum CoachAgendaSource
    {
        None,
        LearnerRequest,
        SharedAiProposal,
        AiDiagnosis,
        LearnerModifiedRequest
    }

    public enum CoachEpisodeState
    {
        Inactive,
        PostTurnEligible,
        Diagnosing,
        Available,
        Invited,
        Active,
        AwaitingLearnerAction,
        AwaitingLearnerVoice,
        Reassessing,
        Completed,
        Declined,
        Overridden,
        TimedOut
    }

    public enum ControlOwner
    {
        Learner,
        Coach,
        Shared,
        SystemSafety
    }

    public enum CoachTerminationReason
    {
        None,
        LearnerEnded,
        LearnerDeclined,
        TargetResolved,
        TurnLimit,
        TimeLimit,
        SafetyOverride,
        NoEligibleIssue,
        LearnerPracticeDeferred,
        TechnicalFailure,
        CycleSkipped
    }

    public enum CoachPolicyAction
    {
        None,
        MakeAvailable,
        Invite,
        AutoStart,
        Start,
        GenerateFeedback,
        Continue,
        Replay,
        End,
        RetryDiagnosis,
        SkipCycle
    }

    public enum CoachLearnerAction
    {
        None,
        RequestCoach,
        Accept,
        Decline,
        ConfirmFocus,
        ChangeFocus,
        NeedExample,
        Replay,
        ApplyNextCycle,
        Override,
        ExitCoaching,
        RetryFeedback,
        RetryDiagnosis,
        SkipCycle
    }

    [Serializable]
    public sealed class CoachPolicyConfig
    {
        public int MaxEpisodesPerCycle = 1;
        public int MaxCoachTurnsPerEpisode = int.MaxValue;
        public float MaxEpisodeSeconds = 90f;
        public int TriggerSeverityThreshold = 2;
        public float TriggerConfidenceThreshold = 0.65f;
        public float AiLedActionWindowSeconds = 15f;
        public float LearnerSpeechMinimumSeconds = 60f;
        public float LearnerSpeechMaximumSeconds = 90f;
        public string PolicyVersion = "three-mode-v1";
        public string DiagnosisModelVersion = "coach-diagnosis-v2";
        public string FeedbackModelVersion = "coach-feedback-v2";

        public static CoachPolicyConfig CreateDefault() => new();
    }

    [Serializable]
    public sealed class CoachDiagnosisRequest
    {
        public string ParticipantId = string.Empty;
        public string Stage = "PracticeDebate";
        public string TopicId = "reading_vs_speaking";
        public int PracticeCycleId;
        public int TurnId;
        public string Topic = string.Empty;
        public string LearnerSide = string.Empty;
        public string OpponentUtteranceText = string.Empty;
        public string PlayerUtteranceText = string.Empty;
        public string PreviousConfirmedStages = string.Empty;
        public string PreviousConfirmedAttempt = string.Empty;
        public string SelectedStrategy = "Any";
        public string[] PreviousStrategyCommands = Array.Empty<string>();
        public string[] PreviousNpcVersionsViewed = Array.Empty<string>();
    }

    [Serializable]
    public sealed class CoachDiagnosisResult
    {
        public bool Success;
        public string Error = string.Empty;
        public string DiagnosisIssueCode = string.Empty;
        public string StrongComponent = string.Empty;
        public string WeakComponent = string.Empty;
        public string DominantStrategy = string.Empty;
        public string RecommendedStrategy = string.Empty;
        public string EvidenceQuality = string.Empty;
        public string ReasoningConnection = string.Empty;
        public int Severity;
        public float Confidence;
        public string RecommendedFocus = string.Empty;
        public string RecommendedNextAction = string.Empty;
        public string RevisionImprovementStatus = "NotApplicable";
        public string RevisionImprovementSummary = string.Empty;
        public string[] CreeiMissingOrWeakComponents = Array.Empty<string>();
        public string CreeiGapSummary = string.Empty;
        public CoachSuggestion[] RankedSuggestions = Array.Empty<CoachSuggestion>();
        public string RawJson = string.Empty;
        public string ModelVersion = string.Empty;

        public bool HasEligibleIssue =>
            Success && Severity > 0 && !string.IsNullOrWhiteSpace(DiagnosisIssueCode);
    }

    [Serializable]
    public sealed class CoachPolicyInput
    {
        public CoachOrchestrationMode Mode;
        public CoachEpisodeState EpisodeState;
        public CoachDiagnosisResult Diagnosis;
        public CoachLearnerAction LearnerAction;
        public string LearnerSelectedFocus = string.Empty;
        public string ConfirmedFocus = string.Empty;
        public int CoachTurnIndex;
        public float ElapsedSeconds;
        public bool ActionWindowExpired;
        public bool EpisodeOpportunityUsed;
    }

    [Serializable]
    public sealed class CoachPolicyDecision
    {
        public CoachPolicyAction Action;
        public string PolicyReason = string.Empty;
        public string ProposedFocus = string.Empty;
        public CoachEpisodeState NextState;
        public ControlOwner StartAuthority;
        public ControlOwner AgendaOwner;
        public ControlOwner PacingOwner;
        public ControlOwner TerminationOwner;
        public CoachTerminationReason TerminationReason;
    }

    [Serializable]
    public sealed class CoachEventRecord
    {
        public string ParticipantId = string.Empty;
        public string SessionId = string.Empty;
        public CoachOrchestrationMode OrchestrationMode;
        public string Stage = string.Empty;
        public string TopicId = string.Empty;
        public int PracticeCycleId;
        public int TurnId;
        public string CoachEpisodeId = string.Empty;
        public string EventId = string.Empty;
        public string EventTimestamp = string.Empty;
        public CoachEpisodeState EpisodeStateBefore;
        public CoachEpisodeState EpisodeStateAfter;
        public string EventType = string.Empty;
        public string DiagnosisIssueCode = string.Empty;
        public int DiagnosisSeverity;
        public float DiagnosisConfidence;
        public string CreeiMissingOrWeakComponents = string.Empty;
        public string CreeiGapSummary = string.Empty;
        public string AgendaSource = string.Empty;
        public string AgendaText = string.Empty;
        public string PolicyAction = string.Empty;
        public string PolicyReason = string.Empty;
        public string ProposedFocus = string.Empty;
        public string ConfirmedFocus = string.Empty;
        public ControlOwner StartAuthority;
        public ControlOwner AgendaOwner;
        public ControlOwner PacingOwner;
        public ControlOwner TerminationOwner;
        public string LearnerControlAction = string.Empty;
        public string RequestInputModality = string.Empty;
        public int DecisionLatencyMilliseconds;
        public string ConfirmedLearnerText = string.Empty;
        public string OpponentUtteranceText = string.Empty;
        public int ChallengeCycleIndex;
        public string ChallengeDifficulty = string.Empty;
        public string RevisionImprovementStatus = string.Empty;
        public string RevisionImprovementSummary = string.Empty;
        public bool FocusOverrideUsed;
        public bool Level3Requested;
        public string CoachFeedbackLevel = string.Empty;
        public string CoachFeedbackType = string.Empty;
        public string CoachFeedbackText = string.Empty;
        public int CoachTurnIndex;
        public float ElapsedEpisodeSeconds;
        public bool TargetResolved;
        public CoachTerminationReason TerminationReason;
        public string DiagnosisModelVersion = string.Empty;
        public string FeedbackModelVersion = string.Empty;
        public string PolicyVersion = string.Empty;
    }

    [Serializable]
    public sealed class CoachEpisodeSummary
    {
        public string ParticipantId = string.Empty;
        public string SessionId = string.Empty;
        public CoachOrchestrationMode OrchestrationMode;
        public string TopicId = string.Empty;
        public int PracticeCycleId;
        public int TurnId;
        public string CoachEpisodeId = string.Empty;
        public string DiagnosisIssueCode = string.Empty;
        public int DiagnosisSeverity;
        public float DiagnosisConfidence;
        public string EpisodeStartTimestamp = string.Empty;
        public string EpisodeEndTimestamp = string.Empty;
        public float EpisodeDurationSeconds;
        public bool InvitationShown;
        public bool EpisodeActivated;
        public ControlOwner StartAuthority;
        public string InitialProposedFocus = string.Empty;
        public string FinalConfirmedFocus = string.Empty;
        public ControlOwner AgendaOwner;
        public ControlOwner PacingOwner;
        public ControlOwner TerminationOwner;
        public int CoachFeedbackTurnCount;
        public int Level3RequestCount;
        public int LearnerVoiceResponseCount;
        public bool FocusOverrideUsed;
        public bool InvitationDeclined;
        public bool TargetResolved;
        public CoachTerminationReason TerminationReason;
        public string DiagnosisModelVersion = string.Empty;
        public string FeedbackModelVersion = string.Empty;
        public string PolicyVersion = string.Empty;
    }
}
