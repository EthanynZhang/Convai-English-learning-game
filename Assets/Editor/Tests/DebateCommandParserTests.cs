using System.Linq;
using Game.Debate;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests.EditMode
{
    public class DebateCommandParserTests
    {
        [Test]
        public void TaxonomyLabelsContainCoreStrategyMoveAndOperationTags()
        {
            CollectionAssert.AreEquivalent(
                new[] { "Logos", "Ethos", "Pathos", "Mixed", "Any" },
                DebateCommandTaxonomy.StrategyDimensions);

            CollectionAssert.IsSubsetOf(
                new[] { "Claim", "Evidence", "Warrant", "Rebuttal", "CounterQuestion", "Impact", "SummaryClosing" },
                DebateCommandTaxonomy.TargetMoves);

            CollectionAssert.IsSubsetOf(
                new[] { "Clarify", "Strengthen", "Transform", "Compare", "Diagnose", "GenerateAlternative", "Challenge", "CalibrateTone", "PlanNextMove", "Reflect" },
                DebateCommandTaxonomy.Operations);
        }

        [Test]
        public void LocalRulesParseCommonCommandsIntoTaxonomyFields()
        {
            AssertParsed(
                "make it more fair and responsible",
                DebateCommandOperation.Transform,
                DebateTargetMove.Response,
                DebateStrategyDimension.Ethos);

            AssertParsed(
                "add stronger evidence",
                DebateCommandOperation.Strengthen,
                DebateTargetMove.Evidence,
                DebateStrategyDimension.Logos);

            AssertParsed(
                "make it more empathetic",
                DebateCommandOperation.Transform,
                DebateTargetMove.Impact,
                DebateStrategyDimension.Pathos);

            AssertParsed(
                "ask a harder follow-up question",
                DebateCommandOperation.Challenge,
                DebateTargetMove.CounterQuestion,
                DebateStrategyDimension.Any);
        }

        [Test]
        public void StructuredOutputSchemaRequiresAllParserFields()
        {
            JObject request = DebateCommandParser.BuildOpenAIRequestJson(
                "gpt-4o-mini",
                "add stronger evidence",
                "Reading and speaking");

            JObject schema = (JObject)request["text"]["format"]["schema"];
            JArray required = (JArray)schema["required"];
            JArray operationEnum = (JArray)schema["properties"]["operation"]["enum"];

            Assert.AreEqual("json_schema", (string)request["text"]["format"]["type"]);
            Assert.AreEqual(true, (bool)request["text"]["format"]["strict"]);
            CollectionAssert.Contains(required.Select(token => (string)token).ToArray(), "operation");
            CollectionAssert.Contains(required.Select(token => (string)token).ToArray(), "target_move");
            CollectionAssert.Contains(required.Select(token => (string)token).ToArray(), "strategy_dimension");
            CollectionAssert.Contains(operationEnum.Select(token => (string)token).ToArray(), "Challenge");
        }

        [Test]
        public void ThirdPartyRelayBaseUrlResolvesToResponsesEndpoint()
        {
            string endpoint = DebateCommandParser.ResolveResponsesEndpoint("https://api.meding.site");

            Assert.AreEqual("https://api.meding.site/v1/responses", endpoint);
        }

        [Test]
        public void PromptBuilderCreatesExecutableConvaiRewritePrompt()
        {
            DebateCommandParseResult parsed = new()
            {
                Operation = DebateCommandOperation.Strengthen,
                TargetMove = DebateTargetMove.Evidence,
                StrategyDimension = DebateStrategyDimension.Logos,
                Tone = "clear",
                Source = DebateCommandParseSource.Rules
            };

            string prompt = DebateCommandPromptBuilder.BuildPrompt(
                parsed,
                "Reading and speaking, which is more important in learning English?",
                "Speaking is more important because students need real practice.");

            StringAssert.Contains("Debate topic:", prompt);
            StringAssert.Contains("Original response:", prompt);
            StringAssert.Contains("Operation: Strengthen", prompt);
            StringAssert.Contains("Strategy: Logos", prompt);
            StringAssert.Contains("Target move: Evidence", prompt);
            StringAssert.Contains("output only the revised response", prompt.ToLowerInvariant());
        }

        [Test]
        public void PromptBuilderReusesButtonStyleStrategyInstructions()
        {
            DebateCommandParseResult ethos = new()
            {
                Operation = DebateCommandOperation.Transform,
                TargetMove = DebateTargetMove.Response,
                StrategyDimension = DebateStrategyDimension.Ethos,
                Tone = "fair and responsible"
            };

            DebateCommandParseResult pathos = new()
            {
                Operation = DebateCommandOperation.Transform,
                TargetMove = DebateTargetMove.Impact,
                StrategyDimension = DebateStrategyDimension.Pathos,
                Tone = "empathetic"
            };

            string ethosPrompt = DebateCommandPromptBuilder.BuildPrompt(ethos, "Topic", "Original");
            string pathosPrompt = DebateCommandPromptBuilder.BuildPrompt(pathos, "Topic", "Original");

            StringAssert.Contains("fairness, responsibility, honesty, professional credibility", ethosPrompt);
            StringAssert.Contains("balanced position", ethosPrompt);
            StringAssert.Contains("empathy, real pressure, a sense of fairness", pathosPrompt);
            StringAssert.Contains("emotionally resonant", pathosPrompt);
        }

        [Test]
        public void CommandInputPanelIsAnchoredInLowerLeftScreenArea()
        {
            Assert.AreEqual(Vector2.zero, InteractiveNpcDebateController.CommandPanelAnchorMin);
            Assert.LessOrEqual(InteractiveNpcDebateController.CommandPanelAnchorMax.x, 0.5f);
            Assert.LessOrEqual(InteractiveNpcDebateController.CommandPanelAnchorMax.y, 0.45f);
            Assert.GreaterOrEqual(
                InteractiveNpcDebateController.CommandPanelAnchorMax.x * InteractiveNpcDebateController.CommandPanelAnchorMax.y,
                0.12f);
        }

        [Test]
        public void VoiceCommandModeUsesConvaiSpeechInsteadOfTypedCommandPanelByDefault()
        {
            Assert.IsTrue(InteractiveNpcDebateController.DefaultUseConvaiVoiceCommandInput);
            Assert.IsFalse(InteractiveNpcDebateController.DefaultShowTypedCommandPanel);
        }

        [Test]
        public void TalkDurationLimitCanBeDisabledForVoiceCommandMode()
        {
            Assert.IsTrue(InteractiveNpcDebateController.DefaultDisableTalkDurationLimit);
        }

        [Test]
        public void CommandLoggerEscapesCommasQuotesAndNewlines()
        {
            string escaped = DebateCommandLogger.EscapeCsv("one, \"two\"\nthree");

            Assert.AreEqual("\"one, \"\"two\"\"\nthree\"", escaped);
        }

        private static void AssertParsed(
            string command,
            DebateCommandOperation expectedOperation,
            DebateTargetMove expectedTargetMove,
            DebateStrategyDimension expectedStrategy)
        {
            DebateCommandParseResult result = DebateCommandParser.ParseWithLocalRules(command);

            Assert.AreEqual(expectedOperation, result.Operation, command);
            Assert.AreEqual(expectedTargetMove, result.TargetMove, command);
            Assert.AreEqual(expectedStrategy, result.StrategyDimension, command);
            Assert.AreEqual(DebateCommandParseSource.Rules, result.Source, command);
            Assert.Greater(result.Confidence, 0f, command);
        }
    }
}
