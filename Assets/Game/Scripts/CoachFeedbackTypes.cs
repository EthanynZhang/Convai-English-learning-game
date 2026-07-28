using System;

namespace Game.Debate
{
    public enum CreeiStage
    {
        Claim,
        Reason,
        Evidence,
        Explanation,
        Impact
    }

    public static class CreeiStageGuidance
    {
        public static CreeiStage Parse(string value)
        {
            return Enum.TryParse(value, true, out CreeiStage stage) ? stage : CreeiStage.Claim;
        }

        public static string GetMethod(CreeiStage stage)
        {
            return stage switch
            {
                CreeiStage.Claim =>
                    "State one precise, focused, and debatable position that directly answers the debate topic. Do not add reasons or evidence yet.",
                CreeiStage.Reason =>
                    "Give one relevant logical reason that answers why the claim should be accepted. The reason must support the claim, not merely repeat it.",
                CreeiStage.Evidence =>
                    "Provide one concrete and relevant fact, example, observation, experience, or credible source that supports the reason. Do not invent statistics or citations.",
                CreeiStage.Explanation =>
                    "Explain the logical link: show how the evidence supports the reason and therefore strengthens the claim. Make the connection explicit.",
                CreeiStage.Impact =>
                    "Explain why the argument matters by identifying who is affected and the important consequence, benefit, or harm.",
                _ => string.Empty
            };
        }

        public static string GetCoachTask(CreeiStage stage)
        {
            return stage switch
            {
                CreeiStage.Claim => "Check whether the learner's position is clear, specific, debatable, and directly answers the topic.",
                CreeiStage.Reason => "Check whether the reason directly supports the learner's claim and answers why it is true or preferable.",
                CreeiStage.Evidence => "Check whether the support is concrete, relevant, credible, and clearly supports the stated reason.",
                CreeiStage.Explanation => "Check whether the learner explicitly explains how the evidence supports the reason and claim.",
                CreeiStage.Impact => "Check whether the learner identifies who is affected and why the consequence is important.",
                _ => string.Empty
            };
        }

        public static string GetExampleTask(CreeiStage stage)
        {
            return stage switch
            {
                CreeiStage.Claim => "Write one improved claim sentence that clearly states the learner's position without adding a reason, evidence, explanation, or impact.",
                CreeiStage.Reason => "Write one improved reason sentence that directly explains why the learner's claim should be accepted.",
                CreeiStage.Evidence => "Write one concrete evidence sentence, preferably beginning with 'For example,' and do not invent statistics, studies, or citations.",
                CreeiStage.Explanation => "Write one explanation sentence that explicitly connects the learner's evidence to the reason and claim.",
                CreeiStage.Impact => "Write one impact sentence that identifies who is affected and states the important consequence.",
                _ => "Write one improved sentence for the current stage."
            };
        }
    }

    public enum CoachFeedbackLevel
    {
        Level1,
        Level2,
        Level3,
        Summary
    }

    public enum CoachFeedbackSource
    {
        OpenAI,
        Rules
    }

    public enum CoachFeedbackFormat
    {
        FocusedShort,
        Scene04CreeiDetailed,
        Scene04CreeiWorkbench
    }

    public enum CoachFeedbackPurpose
    {
        LearnerSocratic,
        CriticalIssue,
        TargetedAdvice,
        Example,
        AdditionalSuggestion,
        DirectAdvice,
        ConversationalFollowUp
    }

    [Serializable]
    public sealed class CoachConversationTurn
    {
        public int TurnIndex;
        public string LearnerRequest = string.Empty;
        public string CoachResponse = string.Empty;
        public CoachFeedbackPurpose Purpose;
    }

    [Serializable]
    public sealed class CoachFeedbackRequest
    {
        public string Stage = "Practice Debate";
        public string TopicId = "reading_vs_speaking";
        public string Topic = string.Empty;
        public string PlayerSide = string.Empty;
        public string CurrentCreeiStage = "Claim";
        public string PreviousLearnerCreeiStages = string.Empty;
        public int TurnId;
        public string OpponentUtteranceText = string.Empty;
        public string PlayerUtteranceText = string.Empty;
        public CreeiArgumentSnapshot CurrentCreeiSnapshot;
        public CreeiArgumentSnapshot PreviousCreeiSnapshot;
        public CreeiComponentDiagnosis ComponentDiagnosis;
        public string PreviousCoachFeedbackText = string.Empty;
        public string SelectedStrategy = string.Empty;
        public string ConfirmedFocus = string.Empty;
        public string LearnerRequest = string.Empty;
        public bool LearnerRequestIsPrimaryAgenda;
        public CoachFeedbackPurpose? Purpose;
        public CoachConversationTurn[] ConversationHistory = Array.Empty<CoachConversationTurn>();
        public string AcceptedCriticalFeedback = string.Empty;
        public string DiagnosisIssueCode = string.Empty;
        public string RecommendedStrategy = string.Empty;
        public string TargetSuccessCriterion = string.Empty;
        public string[] CreeiMissingOrWeakComponents = Array.Empty<string>();
        public string CreeiGapSummary = string.Empty;
        public string[] PreviousCommands = Array.Empty<string>();
        public string[] PreviousNpcVersionsViewed = Array.Empty<string>();
        public CoachFeedbackLevel FeedbackLevel = CoachFeedbackLevel.Level2;
        public CoachFeedbackFormat FeedbackFormat = CoachFeedbackFormat.FocusedShort;
        public bool DetailedJson;
    }

    [Serializable]
    public sealed class CoachFeedbackResult
    {
        public string StrongComponent = "Claim";
        public string WeakComponent = "Evidence";
        public string DominantStrategy = "Logos";
        public string RecommendedStrategy = "Logos";
        public string FeedbackType = "Evidence Support";
        public CoachFeedbackLevel FeedbackLevel = CoachFeedbackLevel.Level2;
        public string FeedbackText = string.Empty;
        public string NextAction = "add evidence";
        public string TargetSuccessCriterion = string.Empty;
        public CoachFeedbackSource Source = CoachFeedbackSource.Rules;
        public string RawJson = string.Empty;
        public string DebugInfo = string.Empty;
        public int RequestAttemptCount;
        public int RequestByteCount;
        public int RequestElapsedMilliseconds;
    }

    [Serializable]
    public sealed class CoachFeedbackLogRow
    {
        public string ParticipantId = "PILOT";
        public string Condition = "Condition C";
        public string Stage = "Practice Debate";
        public string TopicId = "reading_vs_speaking";
        public int TurnId;
        public string PlayerSide = string.Empty;
        public string OpponentUtteranceText = string.Empty;
        public string PlayerUtteranceText = string.Empty;
        public string SelectedStrategy = string.Empty;
        public int PreviousCommandCount;
        public string PreviousCommandTypes = string.Empty;
        public bool CoachTriggered = true;
        public string CoachFeedbackLevel = "Level2";
        public string CoachFeedbackType = string.Empty;
        public string CoachStrongComponent = string.Empty;
        public string CoachWeakComponent = string.Empty;
        public string CoachDominantStrategy = string.Empty;
        public string CoachRecommendedStrategy = string.Empty;
        public string CoachNextAction = string.Empty;
        public string CoachFeedbackText = string.Empty;
        public bool DetailedJson;
        public bool ExampleRequested;
        public string TimestampFeedbackShown = string.Empty;
        public string TimestampNextPlayerTurnStarted = string.Empty;
    }
}
