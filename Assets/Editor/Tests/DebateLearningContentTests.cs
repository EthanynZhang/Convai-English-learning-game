using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
#if CROSSTALES_RTVOICE
using Crosstales.RTVoice.Model;
using Crosstales.RTVoice.Model.Enum;
#endif
using Game.Debate;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    public class DebateLearningContentTests
    {
        [Test]
        public void CreeiContentContainsExactlyTheRequiredFiveParts()
        {
            string[] expected = { "Claim", "Reason", "Evidence", "Explanation", "Impact" };

            CollectionAssert.AreEqual(expected, DebateLearningContent.CreeiParts.Select(part => part.Label).ToArray());
            foreach (string label in expected)
            {
                StringAssert.Contains(label, DebateLearningContent.CreeiReadingText);
            }
        }

        [Test]
        public void StrategyContentContainsExactlyLogosEthosPathos()
        {
            string[] expected = { "Logos", "Ethos", "Pathos" };

            CollectionAssert.AreEqual(expected, DebateLearningContent.StrategyNames);
            foreach (string strategy in expected)
            {
                StringAssert.Contains(strategy, DebateLearningContent.StrategyReadingText);
            }
        }

        [Test]
        public void CsvEscapingHandlesCommasQuotesAndNewlines()
        {
            string escaped = DebateLearningLogger.EscapeCsv("one, \"two\"\nthree");

            Assert.AreEqual("\"one, \"\"two\"\"\nthree\"", escaped);
        }

        [Test]
        public void VoicePracticeContentDefinesFiveOrderedPromptsAndSpeakingFirstTopic()
        {
            Type promptType = typeof(DebateLearningContent).Assembly.GetType("Game.Debate.CreeiVoicePracticePrompt");
            Assert.IsNotNull(promptType, "The immutable CREEI voice prompt model is required.");

            FieldInfo promptField = typeof(DebateLearningContent).GetField(
                "CreeiVoicePracticePrompts",
                BindingFlags.Public | BindingFlags.Static);
            Assert.IsNotNull(promptField, "The fixed five-step prompt collection is required.");

            object[] prompts = ((IEnumerable)promptField.GetValue(null)).Cast<object>().ToArray();
            string[] expectedTitles = { "Claim", "Reason", "Evidence", "Explanation", "Impact" };
            CollectionAssert.AreEqual(expectedTitles, prompts.Select(prompt => ReadProperty<string>(prompt, "Title")).ToArray());
            Assert.IsTrue(prompts.All(prompt => !string.IsNullOrWhiteSpace(ReadProperty<string>(prompt, "Prompt"))));

            StringAssert.Contains("Reading and speaking", DebateLearningContent.OneSentenceTryText);
            StringAssert.Contains("Speaking is more important", DebateLearningContent.CreeiVoicePracticePrompts[0].Prompt);
            Assert.AreEqual(240f, DebateLearningContent.OneSentencePracticeSeconds, 0.001f);
        }

        [Test]
        public void VoicePracticeMetricsKeepOnlyConfirmedPartsAggregateTextAndCountRerecords()
        {
            DebateLearningMetrics metrics = new();
            Type partKeyType = typeof(DebateLearningContent).Assembly.GetType("Game.Debate.CreeiPartKey");
            Assert.IsNotNull(partKeyType, "A stable CREEI part key is required for research logging.");

            MethodInfo recordConfirmedPart = typeof(DebateLearningMetrics).GetMethod("RecordMicroPractice2ConfirmedPart");
            MethodInfo recordRerecord = typeof(DebateLearningMetrics).GetMethod("RecordMicroPractice2Rerecord");
            Assert.IsNotNull(recordConfirmedPart, "Confirmed voice transcripts require an explicit metrics API.");
            Assert.IsNotNull(recordRerecord, "Re-record attempts require an explicit metrics API.");

            recordConfirmedPart.Invoke(metrics, new[] { Enum.Parse(partKeyType, "Claim"), "I support guided AI use." });
            recordConfirmedPart.Invoke(metrics, new[] { Enum.Parse(partKeyType, "Reason"), "It helps students plan." });
            recordConfirmedPart.Invoke(metrics, new[] { Enum.Parse(partKeyType, "Evidence"), "Our class used it to make outlines." });
            recordConfirmedPart.Invoke(metrics, new[] { Enum.Parse(partKeyType, "Explanation"), "That planning leaves students responsible for writing." });
            recordConfirmedPart.Invoke(metrics, new[] { Enum.Parse(partKeyType, "Impact"), "This supports fair learning." });
            recordRerecord.Invoke(metrics, null);

            Assert.AreEqual("CREEI_voice_5_step", metrics.MicroPractice2TemplateChoice);
            StringAssert.Contains("Claim: I support guided AI use.", metrics.MicroPractice2ShortText);
            StringAssert.Contains("Impact: This supports fair learning.", metrics.MicroPractice2ShortText);
            Assert.AreEqual("I support guided AI use.", ReadProperty<string>(metrics, "MicroPractice2Claim"));
            Assert.AreEqual("This supports fair learning.", ReadProperty<string>(metrics, "MicroPractice2Impact"));
            Assert.IsTrue(ReadProperty<bool>(metrics, "MicroPractice2Completed"));
            Assert.AreEqual(1, ReadProperty<int>(metrics, "MicroPractice2RerecordCount"));
        }

        [Test]
        public void VoicePracticeCsvAppendsConfirmedPartsCompletionAndRerecordCount()
        {
            string path = Path.Combine(Path.GetTempPath(), "debate-learning-" + Guid.NewGuid().ToString("N") + ".csv");
            try
            {
                DebateLearningMetrics metrics = new();
                Type partKeyType = typeof(DebateLearningContent).Assembly.GetType("Game.Debate.CreeiPartKey");
                MethodInfo recordConfirmedPart = typeof(DebateLearningMetrics).GetMethod("RecordMicroPractice2ConfirmedPart");
                Assert.IsNotNull(partKeyType);
                Assert.IsNotNull(recordConfirmedPart);

                recordConfirmedPart.Invoke(metrics, new[] { Enum.Parse(partKeyType, "Claim"), "AI, with \"rules\"\nand guidance." });
                foreach (string part in new[] { "Reason", "Evidence", "Explanation", "Impact" })
                {
                    recordConfirmedPart.Invoke(metrics, new[] { Enum.Parse(partKeyType, part), part + " final." });
                }

                new DebateLearningLogger(path).LogFinalSummary("P_TEST", "npc_vs_npc", metrics);
                string[] lines = File.ReadAllLines(path);
                StringAssert.Contains("micro_practice_2_claim", lines[0]);
                StringAssert.Contains("micro_practice_2_completed", lines[0]);
                StringAssert.Contains("micro_practice_2_rerecord_count", lines[0]);
                StringAssert.Contains("\"AI, with \"\"rules\"\"", File.ReadAllText(path));
            }
            finally
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }

        [Test]
        public void LearningPhaseMetricsAccumulateStageDurations()
        {
            DebateLearningMetrics metrics = new();

            metrics.RecordStage("Warm-up", 45f, DebateLearningViewKind.Card, string.Empty);
            metrics.RecordStage("CREEI Dialogue Demo", 50f, DebateLearningViewKind.Demo, string.Empty);
            metrics.RecordStage("CREEI Structure Study", 70f, DebateLearningViewKind.Structure, string.Empty);
            metrics.RecordStage("Micro Practice 1", 60f, DebateLearningViewKind.MicroPractice, string.Empty);
            metrics.RecordStage("Logos Dialogue Demo", 150f, DebateLearningViewKind.Demo, "Logos");
            metrics.RecordRewatch();
            metrics.RecordRewatch();

            Assert.AreEqual(45f, metrics.CardViewTime, 0.001f);
            Assert.AreEqual(270f, metrics.DemoViewTime, 0.001f);
            Assert.AreEqual(0f, metrics.NaturalViewTime, 0.001f);
            Assert.AreEqual(70f, metrics.StructureViewTime, 0.001f);
            Assert.AreEqual(60f, metrics.MicroPracticeTotalTime, 0.001f);
            Assert.AreEqual(375f, metrics.TotalLearningPhaseTime, 0.001f);
            Assert.AreEqual("Logos", metrics.StrategyVersionViewed);
            Assert.AreEqual(2, metrics.RewatchCount);
        }

        private static T ReadProperty<T>(object target, string name)
        {
            PropertyInfo property = target.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            Assert.IsNotNull(property, $"Missing required property: {name}");
            return (T)property.GetValue(target);
        }

        [Test]
        public void LearningPhaseOrderIsLinearAndEndsWithStartDebate()
        {
            DebateLearningStageKey[] expected =
            {
                DebateLearningStageKey.WarmUp,
                DebateLearningStageKey.CreeiReading,
                DebateLearningStageKey.MicroPracticeSpotMissing,
                DebateLearningStageKey.CreeiDialogueDemo,
                DebateLearningStageKey.CreeiStructureStudy,
                DebateLearningStageKey.MicroPracticeOneSentence,
                DebateLearningStageKey.StrategyReading,
                DebateLearningStageKey.LogosDialogueDemo,
                DebateLearningStageKey.EthosDialogueDemo,
                DebateLearningStageKey.PathosDialogueDemo,
                DebateLearningStageKey.MicroPracticeStrategyTry,
                DebateLearningStageKey.MicroChoice,
                DebateLearningStageKey.BufferTransition,
                DebateLearningStageKey.StartDebate
            };

            CollectionAssert.AreEqual(expected, DebateLearningContent.StageSequence.Select(stage => stage.Key).ToArray());
        }

        [Test]
        public void LearningPhaseDefaultDurationIncludesTheFourMinuteVoicePracticeReference()
        {
            float seconds = DebateLearningContent.StageSequence
                .Where(stage => !stage.IsTerminal)
                .Sum(stage => stage.MinimumSeconds);

            Assert.GreaterOrEqual(seconds, DebateLearningContent.OneSentencePracticeSeconds);
            Assert.AreEqual(240f, DebateLearningContent.OneSentencePracticeSeconds, 0.001f);
        }

        [Test]
        public void MicroPracticeContentIsReflectionOnlyWithoutForcedChoices()
        {
            Assert.IsEmpty(DebateLearningContent.SpotMissingOptions);
            Assert.IsEmpty(DebateLearningContent.OneSentenceTryOptions);
            Assert.IsEmpty(DebateLearningContent.StrategyMiniTryOptions);
            Assert.IsEmpty(DebateLearningContent.MicroChoiceOptions);
            Assert.IsEmpty(DebateLearningContent.MicroChoiceRationaleOptions);
            StringAssert.Contains("Think silently", DebateLearningContent.SpotMissingPracticeText);
            Assert.IsFalse(DebateLearningContent.OneSentenceTryText.Contains("Choose one option"));
            Assert.IsFalse(DebateLearningContent.StrategyMiniTryText.Contains("Choose one version"));
        }

        [Test]
        public void EveryDemoStageHasFixedDialogueForTwoSpeakers()
        {
            DebateLearningStageKey[] demoKeys =
            {
                DebateLearningStageKey.CreeiDialogueDemo,
                DebateLearningStageKey.LogosDialogueDemo,
                DebateLearningStageKey.EthosDialogueDemo,
                DebateLearningStageKey.PathosDialogueDemo
            };

            foreach (DebateLearningStageKey key in demoKeys)
            {
                DemoDialogueLine[] lines = DebateLearningContent.GetDialogueLines(key);
                Assert.GreaterOrEqual(lines.Length, 2, key.ToString());
                CollectionAssert.AreEquivalent(
                    new[] { "Anna", "Mike" },
                    lines.Select(line => line.SpeakerId).Distinct().ToArray(),
                    key.ToString());
                Assert.IsTrue(lines.All(line => !string.IsNullOrWhiteSpace(line.Text)), key.ToString());
            }
        }

        [Test]
        public void RepeatExactlyPromptFramesDemoLineAsScriptNotQuestion()
        {
            const string line = "What is your reason? Speaking practice also matters.";

            string prompt = DebateLearningContent.GetRepeatExactlyPrompt(line);

            StringAssert.Contains("Do not answer", prompt);
            StringAssert.Contains("Say only the exact line", prompt);
            StringAssert.Contains($"[{line}]", prompt);
        }

        [Test]
        public void DialogueClipResourcePathUsesStageIndexAndSpeaker()
        {
            DemoDialogueLine line = DebateLearningContent.CreeiDialogueLines[1];

            string path = DebateLearningContent.GetDialogueClipResourcePath(DebateLearningStageKey.CreeiDialogueDemo, 1, line);

            Assert.AreEqual("DebateLearningTts/CreeiDialogueDemo_01_Mike", path);
        }

        [Test]
        public void LearningControllerUsesKeyboardHintsInsteadOfPreviousAndNextButtons()
        {
            string source = ReadLearningControllerSource();

            StringAssert.Contains("CreateKeyboardNavigationHint(panel.transform);", source);
            StringAssert.Contains("\"Learning Keyboard Hint\"", source);
            StringAssert.Contains("\"Previous\"", source);
            StringAssert.Contains("\"Next / Confirm\"", source);
            StringAssert.Contains("CreateRect(\"Key Glyph\", keycap.transform)", source);
            StringAssert.Contains("rootLayout.preferredHeight = 96f;", source);
            StringAssert.Contains("keycapSize.preferredWidth = 88f;", source);
            StringAssert.Contains("keyText.fontSize = 42f;", source);
            StringAssert.DoesNotContain("_previousButton = CreateButton", source);
            StringAssert.DoesNotContain("_nextButton = CreateButton", source);
        }

        [Test]
        public void LearningControllerIsolatesOrdinaryConvaiConversationForEntireTutorial()
        {
            string source = ReadLearningControllerSource();

            StringAssert.Contains("RegisterTutorialInputIsolation();", source);
            StringAssert.Contains("ConvaiGRPCAPI.TryHandleUserVoiceTranscript = TryHandleTutorialVoiceTranscript;", source);
            StringAssert.Contains("ConvaiPlayerInteractionManager.TryHandleTextSubmission = TryHandleTutorialTextSubmission;", source);
            StringAssert.Contains("ConvaiInputManager.ShouldSuppressTalkInput = ShouldSuppressTutorialTalkInput;", source);
            StringAssert.Contains("return enabled && _started && !_completed;", source);
            StringAssert.Contains("UnregisterTutorialInputIsolation();", source);
        }

        private static string ReadLearningControllerSource()
        {
            return File.ReadAllText(Path.Combine(
                "Assets",
                "Game",
                "Scripts",
                "NpcDebateLearningPhaseController.cs"));
        }

#if CROSSTALES_RTVOICE
        [Test]
        public void RtVoicePickerUsesFemaleHintForAnna()
        {
            Voice[] voices =
            {
                new("Microsoft David", string.Empty, Gender.MALE, "Adult", "en-US"),
                new("Microsoft Jenny", string.Empty, Gender.FEMALE, "Adult", "en-US", neural: true),
                new("Microsoft Zira", string.Empty, Gender.FEMALE, "Adult", "en-US")
            };

            Voice selected = NpcDebateLearningPhaseController.PickRtVoice(voices, Gender.FEMALE, "en", "Jenny;Zira");

            Assert.AreEqual("Microsoft Jenny", selected.Name);
            Assert.AreEqual(Gender.FEMALE, selected.Gender);
        }

        [Test]
        public void RtVoicePickerUsesMaleHintForMike()
        {
            Voice[] voices =
            {
                new("Microsoft Zira", string.Empty, Gender.FEMALE, "Adult", "en-US"),
                new("Microsoft David", string.Empty, Gender.MALE, "Adult", "en-US"),
                new("Microsoft Mark", string.Empty, Gender.MALE, "Adult", "en-US", neural: true)
            };

            Voice selected = NpcDebateLearningPhaseController.PickRtVoice(voices, Gender.MALE, "en", "David;Mark");

            Assert.AreEqual("Microsoft David", selected.Name);
            Assert.AreEqual(Gender.MALE, selected.Gender);
        }

        [Test]
        public void RtVoicePickerDoesNotFallbackAcrossGender()
        {
            Voice[] voices =
            {
                new("Microsoft Zira", string.Empty, Gender.FEMALE, "Adult", "en-US"),
                new("Microsoft Huihui", string.Empty, Gender.FEMALE, "Adult", "zh-CN")
            };

            Voice selected = NpcDebateLearningPhaseController.PickRtVoice(voices, Gender.MALE, "en", "David;Guy;Mark");

            Assert.IsNull(selected);
        }
#endif
    }
}
