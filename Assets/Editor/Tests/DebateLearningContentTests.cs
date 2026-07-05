using System;
using System.Linq;
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
        public void LearningPhaseDefaultDurationStaysWithinTwelveMinutes()
        {
            float seconds = DebateLearningContent.StageSequence
                .Where(stage => !stage.IsTerminal)
                .Sum(stage => stage.MinimumSeconds);

            Assert.LessOrEqual(seconds, 720f);
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
