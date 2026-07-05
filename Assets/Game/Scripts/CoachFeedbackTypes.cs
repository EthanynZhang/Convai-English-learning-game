using System;

namespace Game.Debate
{
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

    [Serializable]
    public sealed class CoachFeedbackRequest
    {
        public string Condition = "Condition C";
        public string Stage = "Practice Debate";
        public string TopicId = "reading_vs_speaking";
        public string Topic = string.Empty;
        public string PlayerSide = string.Empty;
        public int TurnId;
        public string OpponentUtteranceText = string.Empty;
        public string PlayerUtteranceText = string.Empty;
        public string SelectedStrategy = string.Empty;
        public string[] PreviousCommands = Array.Empty<string>();
        public string[] PreviousNpcVersionsViewed = Array.Empty<string>();
        public CoachFeedbackLevel FeedbackLevel = CoachFeedbackLevel.Level2;
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
        public CoachFeedbackSource Source = CoachFeedbackSource.Rules;
        public string RawJson = string.Empty;
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
        public bool ExampleRequested;
        public string TimestampFeedbackShown = string.Empty;
        public string TimestampNextPlayerTurnStarted = string.Empty;
    }
}
