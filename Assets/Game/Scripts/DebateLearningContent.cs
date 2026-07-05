using System;

namespace Game.Debate
{
    public enum DebateLearningStageKey
    {
        WarmUp,
        CreeiReading,
        MicroPracticeSpotMissing,
        CreeiDialogueDemo,
        CreeiStructureStudy,
        MicroPracticeOneSentence,
        StrategyReading,
        LogosDialogueDemo,
        EthosDialogueDemo,
        PathosDialogueDemo,
        MicroPracticeStrategyTry,
        BufferTransition,
        MicroChoice,
        StartDebate
    }

    public enum DebateLearningViewKind
    {
        Card,
        Natural,
        Structure,
        Demo,
        MicroPractice
    }

    public readonly struct CreeiPart
    {
        public CreeiPart(string label, string text)
        {
            Label = label;
            Text = text;
        }

        public string Label { get; }
        public string Text { get; }
    }

    public readonly struct DemoDialogueLine
    {
        public DemoDialogueLine(string speakerId, string speakerName, string text, string strategyTag = "", string creeiPart = "")
        {
            SpeakerId = speakerId;
            SpeakerName = speakerName;
            Text = text;
            StrategyTag = strategyTag;
            CreeiPart = creeiPart;
        }

        public string SpeakerId { get; }
        public string SpeakerName { get; }
        public string Text { get; }
        public string StrategyTag { get; }
        public string CreeiPart { get; }
    }

    public readonly struct DebateLearningStageSpec
    {
        public DebateLearningStageSpec(
            DebateLearningStageKey key,
            string title,
            string body,
            float minimumSeconds,
            DebateLearningViewKind viewKind,
            bool isTerminal = false)
        {
            Key = key;
            Title = title;
            Body = body;
            MinimumSeconds = minimumSeconds;
            ViewKind = viewKind;
            IsTerminal = isTerminal;
        }

        public DebateLearningStageKey Key { get; }
        public string Title { get; }
        public string Body { get; }
        public float MinimumSeconds { get; }
        public DebateLearningViewKind ViewKind { get; }
        public bool IsTerminal { get; }
    }

    public static class DebateLearningContent
    {
        public const float WarmUpSeconds = 45f;
        public const float FramedWarmUpSeconds = 30f;
        public const float CreeiReadingSeconds = 90f;
        public const float SpotMissingPracticeSeconds = 60f;
        public const float CreeiDialogueDemoSeconds = 120f;
        public const float CreeiStructureSeconds = 30f;
        public const float OneSentencePracticeSeconds = 90f;
        public const float StrategyReadingSeconds = 90f;
        public const float StrategyDialogueDemoSeconds = 40f;
        public const float StrategyDemoSeconds = StrategyDialogueDemoSeconds * 3f;
        public const float StrategyMiniTrySeconds = 90f;
        public const float MicroChoiceSeconds = 45f;
        public const float BufferTransitionSeconds = 45f;

        public static readonly CreeiPart[] CreeiParts =
        {
            new("Claim", "Claim: state your position clearly in one sentence."),
            new("Reason", "Reason: explain why the claim makes sense."),
            new("Evidence", "Evidence: support the reason with a fact, example, observation, or experience."),
            new("Explanation", "Explanation: connect the evidence back to the reason instead of leaving it alone."),
            new("Impact", "Impact: show why the point matters for learners, teachers, or the final decision.")
        };

        public static readonly string[] StrategyNames =
        {
            "Logos",
            "Ethos",
            "Pathos"
        };

        public static readonly string[] MicroChoiceOptions = Array.Empty<string>();
        public static readonly string[] MicroChoiceRationaleOptions = Array.Empty<string>();
        public static readonly string[] SpotMissingOptions = Array.Empty<string>();
        public static readonly string[] OneSentenceTryOptions = Array.Empty<string>();
        public static readonly string[] StrategyMiniTryOptions = Array.Empty<string>();

        public static string CreeiReadingText =>
            "CREEI is a simple structure for building a debate argument.\n\n" +
            string.Join("\n", Array.ConvertAll(CreeiParts, part => $"{part.Label}: {part.Text}"));

        public static string CreeiDemoTranscript =>
            "Reading is more important for learning English because it gives learners the language input they need before they speak. " +
            "For example, students who read short articles every day meet useful words and sentence patterns many times. " +
            "This repeated input helps them understand how English works, so their speaking becomes clearer and more accurate. " +
            "That matters because confident speaking usually grows from a strong base of words, ideas, and grammar.";

        public static string CreeiStructureText =>
            "Claim: Reading is more important for learning English.\n" +
            "Reason: It gives learners language input before they speak.\n" +
            "Evidence: Students who read short articles every day meet useful words and sentence patterns many times.\n" +
            "Explanation: Repeated input helps learners understand how English works, so their speaking becomes clearer and more accurate.\n" +
            "Impact: Confident speaking grows from a strong base of words, ideas, and grammar.";

        public static string SpotMissingPracticeText =>
            "Micro Practice 1: Spot the missing CREEI part.\n\n" +
            "Students should be allowed to use AI tools with clear rules.\n" +
            "AI can help them organize ideas before writing.\n" +
            "For example, a student can use AI to brainstorm an outline.\n\n" +
            "Think silently: what is still missing? Does the argument need an Explanation, an Impact, or both?";

        public static string SpotMissingFeedback =>
            "This argument needs Explanation first: the speaker should explain why brainstorming an outline supports learning rather than replacing learning.";

        public static string OneSentenceTryText =>
            "Micro Practice 2: One sentence try.\n\n" +
            "Complete the sentence with one useful CREEI connection.\n\n" +
            "This means that __________.\n\n" +
            "Think of one sentence in your mind. If this later becomes a voice-input activity, this is where the learner can say one short completion.";

        public static string OneSentenceTryFeedback =>
            "Standard example: This means that AI can support the learning process when students still make the final decisions and do the real writing.";

        public static string StrategyReadingText =>
            "Three persuasive strategies can strengthen an argument.\n\n" +
            "Logos uses logic, reasons, evidence, cause and effect, and practical tradeoffs.\n" +
            "Ethos uses credibility, fairness, responsibility, honesty, and a balanced position.\n" +
            "Pathos uses emotion, empathy, pressure, hopes, fears, and human consequences.";

        public static string StrategyMiniTryText =>
            "Micro Practice 3: Strategy mini-try.\n\n" +
            "Choose one strategy and complete one sentence.\n\n" +
            "Logos: This policy is practical because __________.\n" +
            "Ethos: A responsible university should __________.\n" +
            "Pathos: Many students feel __________, so __________.\n\n" +
            "Think of one sentence. Do not choose an option here; just notice how the three sentence frames feel different.";

        public static readonly DemoDialogueLine[] CreeiDialogueLines =
        {
            new("Anna", "Anna Reed", "I think reading is more important for learning English.", creeiPart: "Claim"),
            new("Mike", "Mike Carter", "What is your reason? Speaking practice also matters."),
            new("Anna", "Anna Reed", "My reason is that reading gives learners language input before they speak.", creeiPart: "Reason"),
            new("Mike", "Mike Carter", "Can you give evidence for that point?"),
            new("Anna", "Anna Reed", "Students who read short articles every day meet useful words and sentence patterns many times.", creeiPart: "Evidence"),
            new("Mike", "Mike Carter", "So how does that evidence connect to speaking?"),
            new("Anna", "Anna Reed", "Repeated input helps learners understand how English works, so their speaking becomes clearer and more accurate.", creeiPart: "Explanation"),
            new("Mike", "Mike Carter", "And why does this matter in the debate?"),
            new("Anna", "Anna Reed", "It matters because confident speaking grows from a strong base of words, ideas, and grammar.", creeiPart: "Impact")
        };

        public static readonly DemoDialogueLine[] LogosDialogueLines =
        {
            new("Anna", "Anna Reed", "Logos means using clear logic, reasons, and evidence.", "Logos"),
            new("Mike", "Mike Carter", "Show me a Logos argument about reading and speaking.", "Logos"),
            new("Anna", "Anna Reed", "Reading builds vocabulary and grammar input. When learners see patterns many times, they can organize spoken answers more accurately.", "Logos"),
            new("Mike", "Mike Carter", "So the logic is input first, clearer output later.", "Logos")
        };

        public static readonly DemoDialogueLine[] EthosDialogueLines =
        {
            new("Anna", "Anna Reed", "Ethos means sounding fair, responsible, and credible.", "Ethos"),
            new("Mike", "Mike Carter", "How would Ethos support reading in this debate?", "Ethos"),
            new("Anna", "Anna Reed", "A responsible learner should build a strong foundation before speaking quickly. Reading shows patience and respect for accurate communication.", "Ethos"),
            new("Mike", "Mike Carter", "That sounds balanced because it values careful learning, not just fast performance.", "Ethos")
        };

        public static readonly DemoDialogueLine[] PathosDialogueLines =
        {
            new("Anna", "Anna Reed", "Pathos means using emotion and empathy in a controlled way.", "Pathos"),
            new("Mike", "Mike Carter", "How can Pathos make the reading argument stronger?", "Pathos"),
            new("Anna", "Anna Reed", "Many learners feel nervous when they speak without enough words. Reading gives them confidence, so they can join conversations without fear.", "Pathos"),
            new("Mike", "Mike Carter", "That helps the audience care about how learners actually feel.", "Pathos")
        };

        public static readonly DebateLearningStageSpec[] StageSequence =
        {
            new(
                DebateLearningStageKey.WarmUp,
                "Warm-up",
                "Before the NPC debate, review how strong debate arguments are built. You will read a structure, watch a fixed demonstration, compare three strategy versions, then choose one strategy to take into the debate.",
                FramedWarmUpSeconds,
                DebateLearningViewKind.Card),
            new(
                DebateLearningStageKey.CreeiReading,
                "CREEI Reading",
                CreeiReadingText,
                CreeiReadingSeconds,
                DebateLearningViewKind.Card),
            new(
                DebateLearningStageKey.MicroPracticeSpotMissing,
                "Micro Practice 1: Spot the Missing Part",
                SpotMissingPracticeText,
                SpotMissingPracticeSeconds,
                DebateLearningViewKind.MicroPractice),
            new(
                DebateLearningStageKey.CreeiDialogueDemo,
                "CREEI Dialogue Demo",
                "Watch Anna and Mike demonstrate how a CREEI argument is built through dialogue.",
                CreeiDialogueDemoSeconds,
                DebateLearningViewKind.Demo),
            new(
                DebateLearningStageKey.CreeiStructureStudy,
                "CREEI Structure Study",
                CreeiStructureText,
                CreeiStructureSeconds,
                DebateLearningViewKind.Structure),
            new(
                DebateLearningStageKey.MicroPracticeOneSentence,
                "Micro Practice 2: One Sentence Try",
                OneSentenceTryText,
                OneSentencePracticeSeconds,
                DebateLearningViewKind.MicroPractice),
            new(
                DebateLearningStageKey.StrategyReading,
                "Strategy Reading",
                StrategyReadingText,
                StrategyReadingSeconds,
                DebateLearningViewKind.Card),
            new(
                DebateLearningStageKey.LogosDialogueDemo,
                "Logos Dialogue Demo",
                "Watch a fixed Logos demonstration.",
                StrategyDialogueDemoSeconds,
                DebateLearningViewKind.Demo),
            new(
                DebateLearningStageKey.EthosDialogueDemo,
                "Ethos Dialogue Demo",
                "Watch a fixed Ethos demonstration.",
                StrategyDialogueDemoSeconds,
                DebateLearningViewKind.Demo),
            new(
                DebateLearningStageKey.PathosDialogueDemo,
                "Pathos Dialogue Demo",
                "Watch a fixed Pathos demonstration.",
                StrategyDialogueDemoSeconds,
                DebateLearningViewKind.Demo),
            new(
                DebateLearningStageKey.MicroPracticeStrategyTry,
                "Micro Practice 3: Strategy Mini-Try",
                StrategyMiniTryText,
                StrategyMiniTrySeconds,
                DebateLearningViewKind.MicroPractice),
            new(
                DebateLearningStageKey.MicroChoice,
                "Strategy Reflection",
                "Before the NPC debate begins, think silently about one strategy you may want to notice next.\n\nLogos: clear reasoning.\nEthos: responsible and fair.\nPathos: human concern.\n\nNo choice is required here. Just keep one focus in mind.",
                MicroChoiceSeconds,
                DebateLearningViewKind.Card),
            new(
                DebateLearningStageKey.BufferTransition,
                "Transition",
                "You have finished the shared reading, watching, and micro practice. The NPC debate will begin next.",
                BufferTransitionSeconds,
                DebateLearningViewKind.Card),
            new(
                DebateLearningStageKey.StartDebate,
                "Start Debate",
                string.Empty,
                0f,
                DebateLearningViewKind.Card,
                true)
        };

        public static string GetRepeatExactlyPrompt(string transcript)
        {
            return "You are performing a scripted research demo. " +
                   "Do not answer, explain, or add anything. " +
                   "Say only the exact line inside the brackets. " +
                   $"Repeat the following exactly as it is: [{transcript}]";
        }

        public static string GetDialogueClipResourcePath(DebateLearningStageKey key, int lineIndex, DemoDialogueLine line)
        {
            string speakerId = string.IsNullOrWhiteSpace(line.SpeakerId) ? "NPC" : line.SpeakerId.Trim();
            return $"DebateLearningTts/{key}_{lineIndex:00}_{speakerId}";
        }

        public static DemoDialogueLine[] GetDialogueLines(DebateLearningStageKey key)
        {
            return key switch
            {
                DebateLearningStageKey.CreeiDialogueDemo => CreeiDialogueLines,
                DebateLearningStageKey.LogosDialogueDemo => LogosDialogueLines,
                DebateLearningStageKey.EthosDialogueDemo => EthosDialogueLines,
                DebateLearningStageKey.PathosDialogueDemo => PathosDialogueLines,
                _ => Array.Empty<DemoDialogueLine>()
            };
        }

        public static string[] GetMicroPracticeOptions(DebateLearningStageKey key)
        {
            return key switch
            {
                DebateLearningStageKey.MicroPracticeSpotMissing => SpotMissingOptions,
                DebateLearningStageKey.MicroPracticeOneSentence => OneSentenceTryOptions,
                DebateLearningStageKey.MicroPracticeStrategyTry => StrategyMiniTryOptions,
                _ => Array.Empty<string>()
            };
        }

        public static string GetMicroPracticeFeedback(DebateLearningStageKey key, string choice)
        {
            return key switch
            {
                DebateLearningStageKey.MicroPracticeSpotMissing => SpotMissingFeedback,
                DebateLearningStageKey.MicroPracticeOneSentence => OneSentenceTryFeedback,
                DebateLearningStageKey.MicroPracticeStrategyTry => GetStrategyMiniTryFeedback(choice),
                _ => string.Empty
            };
        }

        public static bool IsCorrectMicroPracticeChoice(DebateLearningStageKey key, string choice)
        {
            return key != DebateLearningStageKey.MicroPracticeSpotMissing ||
                   (!string.IsNullOrWhiteSpace(choice) &&
                    choice.StartsWith("Explanation:", StringComparison.OrdinalIgnoreCase));
        }

        public static string ExtractStrategyFromChoice(string choice)
        {
            if (string.IsNullOrWhiteSpace(choice))
            {
                return string.Empty;
            }

            int separator = choice.IndexOf(':');
            return separator > 0 ? choice[..separator].Trim() : choice.Trim();
        }

        private static string GetStrategyMiniTryFeedback(string choice)
        {
            string strategy = ExtractStrategyFromChoice(choice);
            string focus = strategy switch
            {
                "Logos" => "reason and practical logic",
                "Ethos" => "fairness and responsibility",
                "Pathos" => "human concern",
                _ => "a persuasive focus"
            };

            return string.IsNullOrWhiteSpace(strategy)
                ? string.Empty
                : $"Good. This version mainly uses {strategy} because it focuses on {focus}.";
        }

        public static string GetStrategyForStage(DebateLearningStageKey key)
        {
            return key switch
            {
                DebateLearningStageKey.LogosDialogueDemo => "Logos",
                DebateLearningStageKey.EthosDialogueDemo => "Ethos",
                DebateLearningStageKey.PathosDialogueDemo => "Pathos",
                _ => string.Empty
            };
        }
    }
}
