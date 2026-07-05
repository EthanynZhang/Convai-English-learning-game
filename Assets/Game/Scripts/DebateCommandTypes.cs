using System;

namespace Game.Debate
{
    public enum DebateCommandOperation
    {
        Clarify,
        Strengthen,
        Transform,
        Compare,
        Diagnose,
        GenerateAlternative,
        Challenge,
        CalibrateTone,
        PlanNextMove,
        Reflect
    }

    public enum DebateTargetMove
    {
        Claim,
        Evidence,
        Warrant,
        Backing,
        Qualifier,
        Concession,
        Rebuttal,
        CounterQuestion,
        Impact,
        SummaryClosing,
        Response,
        Any
    }

    public enum DebateStrategyDimension
    {
        Logos,
        Ethos,
        Pathos,
        Mixed,
        Any
    }

    public enum DebateCommandParseSource
    {
        OpenAI,
        Rules
    }

    public static class DebateCommandTaxonomy
    {
        public static readonly string[] Operations = Enum.GetNames(typeof(DebateCommandOperation));
        public static readonly string[] TargetMoves = Enum.GetNames(typeof(DebateTargetMove));
        public static readonly string[] StrategyDimensions = Enum.GetNames(typeof(DebateStrategyDimension));
    }

    [Serializable]
    public sealed class DebateCommandParseResult
    {
        public DebateCommandOperation Operation = DebateCommandOperation.Transform;
        public DebateTargetMove TargetMove = DebateTargetMove.Response;
        public DebateStrategyDimension StrategyDimension = DebateStrategyDimension.Any;
        public string SubStrategy = string.Empty;
        public string Tone = string.Empty;
        public string TargetSide = "Any";
        public float Confidence = 0.35f;
        public DebateCommandParseSource Source = DebateCommandParseSource.Rules;
        public string RawJson = string.Empty;

        public string OperationLabel => Operation.ToString();
        public string TargetMoveLabel => TargetMove.ToString();
        public string StrategyDimensionLabel => StrategyDimension.ToString();
    }
}
