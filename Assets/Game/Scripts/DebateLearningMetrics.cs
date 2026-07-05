using System;

namespace Game.Debate
{
    [Serializable]
    public sealed class DebateLearningMetrics
    {
        public float CardViewTime { get; private set; }
        public float DemoViewTime { get; private set; }
        public float NaturalViewTime { get; private set; }
        public float StructureViewTime { get; private set; }
        public string StrategyVersionViewed { get; private set; } = string.Empty;
        public string MicroChoiceStrategy { get; private set; } = string.Empty;
        public string MicroPractice1Choice { get; private set; } = string.Empty;
        public bool MicroPractice1Correct { get; private set; }
        public bool MicroPractice1FeedbackShown { get; private set; }
        public string MicroPractice2TemplateChoice { get; private set; } = string.Empty;
        public string MicroPractice2ShortText { get; private set; } = string.Empty;
        public string MicroPractice3Strategy { get; private set; } = string.Empty;
        public string MicroPractice3TemplateChoice { get; private set; } = string.Empty;
        public string MicroPractice3ShortText { get; private set; } = string.Empty;
        public string MicroChoiceRationaleType { get; private set; } = string.Empty;
        public string MicroChoiceRationaleText { get; private set; } = string.Empty;
        public float MicroPracticeTotalTime { get; private set; }
        public bool OptionalBadExampleViewed { get; private set; }
        public int RewatchCount { get; private set; }
        public float TotalLearningPhaseTime { get; private set; }

        public void RecordStage(
            string stage,
            float seconds,
            DebateLearningViewKind viewKind,
            string strategyVersion)
        {
            float safeSeconds = Math.Max(0f, seconds);
            TotalLearningPhaseTime += safeSeconds;

            switch (viewKind)
            {
                case DebateLearningViewKind.Card:
                    CardViewTime += safeSeconds;
                    break;
                case DebateLearningViewKind.Natural:
                    NaturalViewTime += safeSeconds;
                    DemoViewTime += safeSeconds;
                    break;
                case DebateLearningViewKind.Structure:
                    StructureViewTime += safeSeconds;
                    DemoViewTime += safeSeconds;
                    break;
                case DebateLearningViewKind.Demo:
                    DemoViewTime += safeSeconds;
                    break;
                case DebateLearningViewKind.MicroPractice:
                    MicroPracticeTotalTime += safeSeconds;
                    break;
            }

            if (!string.IsNullOrWhiteSpace(strategyVersion))
            {
                AppendStrategy(strategyVersion.Trim());
            }
        }

        public void RecordMicroChoice(string strategy)
        {
            MicroChoiceStrategy = strategy ?? string.Empty;
        }

        public void RecordMicroPractice1(string choice, bool correct, bool feedbackShown)
        {
            MicroPractice1Choice = choice ?? string.Empty;
            MicroPractice1Correct = correct;
            MicroPractice1FeedbackShown = feedbackShown;
        }

        public void RecordMicroPractice2(string templateChoice, string shortText)
        {
            MicroPractice2TemplateChoice = templateChoice ?? string.Empty;
            MicroPractice2ShortText = shortText ?? string.Empty;
        }

        public void RecordMicroPractice3(string strategy, string templateChoice, string shortText)
        {
            MicroPractice3Strategy = strategy ?? string.Empty;
            MicroPractice3TemplateChoice = templateChoice ?? string.Empty;
            MicroPractice3ShortText = shortText ?? string.Empty;
        }

        public void RecordMicroChoiceRationale(string rationaleType, string rationaleText)
        {
            MicroChoiceRationaleType = rationaleType ?? string.Empty;
            MicroChoiceRationaleText = rationaleText ?? string.Empty;
        }

        public void RecordRewatch()
        {
            RewatchCount++;
        }

        private void AppendStrategy(string strategy)
        {
            string[] requested = strategy.Split(new[] { ';', '|' }, StringSplitOptions.RemoveEmptyEntries);
            if (requested.Length > 1)
            {
                for (int i = 0; i < requested.Length; i++)
                {
                    AppendStrategy(requested[i].Trim());
                }

                return;
            }

            if (string.IsNullOrWhiteSpace(StrategyVersionViewed))
            {
                StrategyVersionViewed = strategy;
                return;
            }

            string[] existing = StrategyVersionViewed.Split(';');
            for (int i = 0; i < existing.Length; i++)
            {
                if (string.Equals(existing[i], strategy, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }

            StrategyVersionViewed += ";" + strategy;
        }
    }
}
