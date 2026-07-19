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

    public enum CreeiPartKey
    {
        Claim,
        Reason,
        Evidence,
        Explanation,
        Impact
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

    public readonly struct CreeiVoicePracticePrompt
    {
        public CreeiVoicePracticePrompt(CreeiPartKey part, string title, string prompt, string example)
        {
            Part = part;
            Title = title;
            Prompt = prompt;
            Example = example;
        }

        public CreeiPartKey Part { get; }
        public string Title { get; }
        public string Prompt { get; }
        public string Example { get; }
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
        public const float OneSentencePracticeSeconds = 240f;
        public const float StrategyReadingSeconds = 90f;
        public const float StrategyDialogueDemoSeconds = 40f;
        public const float StrategyDemoSeconds = StrategyDialogueDemoSeconds * 3f;
        public const float StrategyMiniTrySeconds = 90f;
        public const float MicroChoiceSeconds = 45f;
        public const float BufferTransitionSeconds = 45f;

        public static readonly CreeiPart[] CreeiParts =
        {
            new("Claim", "State your position clearly in one sentence."),
            new("Reason", "Explain why the claim makes sense."),
            new("Evidence", "Support the reason with a fact, example, observation, or experience."),
            new("Explanation", "Connect the evidence back to the reason instead of leaving it alone."),
            new("Impact", "Show why the point matters for learners, teachers, or the final decision.")
        };

        public const string PracticeDebateTopic =
            "Reading and speaking, which is more important in learning English?";

        public const string PracticeStance =
            "Speaking is more important for learning English.";

        public const string CreeiVoicePracticeTopic = PracticeDebateTopic;

        public static readonly CreeiVoicePracticePrompt[] CreeiVoicePracticePrompts =
        {
            new(
                CreeiPartKey.Claim,
                "Claim",
                "State this position in one clear sentence: Speaking is more important for learning English.",
                "For example: Speaking is more important than reading for learning English."),
            new(
                CreeiPartKey.Reason,
                "Reason",
                "Give one reason why speaking is more important for learning English.",
                "For example: Speaking makes learners retrieve and use English in real time."),
            new(
                CreeiPartKey.Evidence,
                "Evidence",
                "Give one fact, example, observation, or personal experience that shows how speaking practice helps English learners.",
                "For example: In a weekly conversation club, learners listen, form sentences, and respond without reading a prepared answer."),
            new(
                CreeiPartKey.Explanation,
                "Explanation",
                "Explain how your evidence shows that speaking turns language knowledge into practical communication.",
                "For example: This real-time practice reveals what learners cannot say yet and turns passive knowledge into active communication."),
            new(
                CreeiPartKey.Impact,
                "Impact",
                "Explain why stronger speaking ability matters for learners in real communication.",
                "For example: Learners become more confident and can communicate outside the classroom.")
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
            "Speaking is more important for learning English because it turns passive knowledge into active communication. " +
            "For example, learners in a conversation club must retrieve words, form sentences, listen, and respond in real time. " +
            "This active use reveals gaps and gives learners immediate chances to adjust their pronunciation and word choice. " +
            "That matters because learners need confidence and practical speaking ability to communicate outside the classroom.";

        public static string CreeiStructureText =>
            "Claim: Speaking is more important for learning English.\n" +
            "Reason: It turns language knowledge into active communication.\n" +
            "Evidence: In conversation practice, learners retrieve words, form sentences, listen, and respond in real time.\n" +
            "Explanation: Active use reveals gaps and lets learners adjust their pronunciation and word choice immediately.\n" +
            "Impact: Learners gain the confidence and practical ability needed for communication outside the classroom.";

        public static string SpotMissingPracticeText =>
            "Speaking should be the central activity in English learning.\n" +
            "It requires learners to retrieve and use language actively.\n" +
            "For example, a learner in a conversation club must listen and respond without reading a prepared answer.\n\n" +
            "Think silently: what is still missing? Does the argument need an Explanation, an Impact, or both?";

        public static string SpotMissingFeedback =>
            "This argument needs Explanation first: the speaker should explain how real-time listening and responding turns language knowledge into practical speaking ability.";

        public static string OneSentenceTryText =>
            "You will speak five required sentences: one each for Claim, Reason, Evidence, Explanation, and Impact.\n\n" +
            "Debate topic\n" + CreeiVoicePracticeTopic + "\n\n" +
            "Press T to start speaking. Press T again to stop. Your transcript will appear here before you confirm each step.";

        public static string OneSentenceTryFeedback =>
            "Standard example: This means that speaking practice helps learners retrieve language quickly, notice gaps, and communicate with greater confidence.";

        public static string StrategyReadingText =>
            "Three persuasive strategies can strengthen an argument.\n\n" +
            "Logos uses logic, reasons, evidence, cause and effect, and practical tradeoffs.\n" +
            "Ethos uses credibility, fairness, responsibility, honesty, and a balanced position.\n" +
            "Pathos uses emotion, empathy, pressure, hopes, fears, and human consequences.";

        public static string StrategyMiniTryText =>
            "Use the topic: Speaking is more important for learning English.\n\n" +
            "Logos: Speaking practice is effective because __________.\n" +
            "Ethos: A responsible English learner should __________.\n" +
            "Pathos: Many learners feel __________ when they cannot speak, so __________.\n\n" +
            "Think of one sentence. Do not choose an option here; just notice how the three sentence frames feel different.";

        public static readonly DemoDialogueLine[] CreeiDialogueLines =
        {
            new("Anna", "Anna Reed", "I think speaking is more important for learning English.", creeiPart: "Claim"),
            new("Mike", "Mike Carter", "What is your reason? Reading also gives learners useful language input."),
            new("Anna", "Anna Reed", "My reason is that speaking turns language knowledge into active communication.", creeiPart: "Reason"),
            new("Mike", "Mike Carter", "Can you give evidence for that point?"),
            new("Anna", "Anna Reed", "In conversation practice, learners retrieve words, form sentences, listen, and respond in real time.", creeiPart: "Evidence"),
            new("Mike", "Mike Carter", "How does that evidence support your claim?"),
            new("Anna", "Anna Reed", "Active use reveals gaps and lets learners adjust their pronunciation and word choice immediately.", creeiPart: "Explanation"),
            new("Mike", "Mike Carter", "And why does this matter in the debate?"),
            new("Anna", "Anna Reed", "It matters because learners need confidence and practical speaking ability for communication outside the classroom.", creeiPart: "Impact")
        };

        public static readonly DemoDialogueLine[] LogosDialogueLines =
        {
            new("Anna", "Anna Reed", "Logos means using clear logic, reasons, and evidence.", "Logos"),
            new("Mike", "Mike Carter", "Show me a Logos argument about reading and speaking.", "Logos"),
            new("Anna", "Anna Reed", "Speaking requires learners to retrieve vocabulary, form sentences, and respond in real time. Repeated practice therefore makes communication faster and more automatic.", "Logos"),
            new("Mike", "Mike Carter", "So the logic is active retrieval first, more fluent communication later.", "Logos")
        };

        public static readonly DemoDialogueLine[] EthosDialogueLines =
        {
            new("Anna", "Anna Reed", "Ethos means sounding fair, responsible, and credible.", "Ethos"),
            new("Mike", "Mike Carter", "How would Ethos support speaking in this debate?", "Ethos"),
            new("Anna", "Anna Reed", "A responsible English program should give learners safe, regular chances to speak while still valuing reading as useful preparation.", "Ethos"),
            new("Mike", "Mike Carter", "That sounds credible because it supports speaking without dismissing the value of reading.", "Ethos")
        };

        public static readonly DemoDialogueLine[] PathosDialogueLines =
        {
            new("Anna", "Anna Reed", "Pathos means using emotion and empathy in a controlled way.", "Pathos"),
            new("Mike", "Mike Carter", "How can Pathos make the speaking argument stronger?", "Pathos"),
            new("Anna", "Anna Reed", "Many learners know English on paper but feel silent and isolated in real conversations. Speaking practice helps them find their voice and connect with other people.", "Pathos"),
            new("Mike", "Mike Carter", "That helps the audience care about the confidence and connection learners gain through speaking.", "Pathos")
        };

        public static readonly DebateLearningStageSpec[] StageSequence =
        {
            new(
                DebateLearningStageKey.WarmUp,
                "Warm-up",
                "The debate topic is: Reading and speaking, which is more important in learning English? In this tutorial, you will practise building the position that speaking is more important. You will study CREEI, watch fixed demonstrations, and compare Logos, Ethos, and Pathos before the NPC debate begins.",
                FramedWarmUpSeconds,
                DebateLearningViewKind.Card),
            new(
                DebateLearningStageKey.CreeiReading,
                "CREEI Reading",
                CreeiReadingText,
                CreeiReadingSeconds,
                DebateLearningViewKind.Card),
            new(
                DebateLearningStageKey.CreeiDialogueDemo,
                "CREEI Dialogue Demo",
                "Watch Anna and Mike build the argument through dialogue.",
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
                "Micro Practice 2: Build Your CREEI Argument",
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
                "Watch Anna and Mike demonstrate this strategy using the same debate topic.",
                StrategyDialogueDemoSeconds,
                DebateLearningViewKind.Demo),
            new(
                DebateLearningStageKey.EthosDialogueDemo,
                "Ethos Dialogue Demo",
                "Watch Anna and Mike demonstrate this strategy using the same debate topic.",
                StrategyDialogueDemoSeconds,
                DebateLearningViewKind.Demo),
            new(
                DebateLearningStageKey.PathosDialogueDemo,
                "Pathos Dialogue Demo",
                "Watch Anna and Mike demonstrate this strategy using the same debate topic.",
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
