using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Game.Debate;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests.EditMode
{
    public class SharedInitiativeCoachAgentTests
    {
        private const string SharedInitiativeScenePath = "Game/Scenes/Level_SharedInitiativeOrchestration.unity";
        private const string SharedInitiativeControllerGuid = "1bedf553246c4a589354b41b7f20d6df";
        private const string TranscriptBridgeGuid = "0ee0de56c9b14c74ae428d45f2e04d50";

        private static readonly Assembly RuntimeAssembly = typeof(SharedInitiativeOrchestrationController).Assembly;

        [Test]
        public void CoachFeedbackSchemaRequiresSupplementFieldsAndEnums()
        {
            Type generatorType = GetRuntimeType("Game.Debate.DebateCoachFeedbackGenerator");
            Assert.IsNotNull(generatorType, "DebateCoachFeedbackGenerator should exist.");

            MethodInfo schemaMethod = generatorType.GetMethod(
                "BuildStructuredOutputSchema",
                BindingFlags.Public | BindingFlags.Static);
            Assert.IsNotNull(schemaMethod, "BuildStructuredOutputSchema should be public for tests and prompt validation.");

            JObject schema = (JObject)schemaMethod.Invoke(null, null);
            JArray required = (JArray)schema["required"];
            JObject properties = (JObject)schema["properties"];

            CollectionAssert.IsSubsetOf(
                new[]
                {
                    "strong_component",
                    "weak_component",
                    "dominant_strategy",
                    "recommended_strategy",
                    "feedback_type",
                    "feedback_level",
                    "feedback_text",
                    "next_action"
                },
                required.Select(token => (string)token).ToArray());

            CollectionAssert.AreEquivalent(
                new[] { "Claim", "Reason", "Evidence", "Explanation", "Impact" },
                ((JArray)properties["strong_component"]["enum"]).Select(token => (string)token).ToArray());
            CollectionAssert.Contains(
                ((JArray)properties["feedback_level"]["enum"]).Select(token => (string)token).ToArray(),
                "Level2");
            CollectionAssert.Contains(
                ((JArray)properties["feedback_level"]["enum"]).Select(token => (string)token).ToArray(),
                "Level3");
        }

        [Test]
        public void LevelPromptsEnforcePostTurnFeedbackBoundaries()
        {
            Type requestType = GetRuntimeType("Game.Debate.CoachFeedbackRequest");
            Type levelType = GetRuntimeType("Game.Debate.CoachFeedbackLevel");
            Type generatorType = GetRuntimeType("Game.Debate.DebateCoachFeedbackGenerator");
            Assert.IsNotNull(requestType);
            Assert.IsNotNull(levelType);
            Assert.IsNotNull(generatorType);

            object request = Activator.CreateInstance(requestType);
            SetField(request, "Topic", "Reading and speaking");
            SetField(request, "PlayerSide", "Speaking is more important.");
            SetField(request, "OpponentUtteranceText", "Reading gives students vocabulary.");
            SetField(request, "PlayerUtteranceText", "Speaking is better because practice is important.");
            SetField(request, "FeedbackLevel", Enum.Parse(levelType, "Level2"));

            string level2Prompt = InvokeString(generatorType, "BuildPrompt", request);
            StringAssert.Contains("only provide short post-turn feedback", level2Prompt);
            StringAssert.Contains("Do not write a full answer", level2Prompt);
            StringAssert.Contains("two short sentences", level2Prompt);

            SetField(request, "FeedbackLevel", Enum.Parse(levelType, "Level3"));
            string level3Prompt = InvokeString(generatorType, "BuildPrompt", request);
            StringAssert.Contains("sentence frame", level3Prompt);
            StringAssert.Contains("Do not write a full answer", level3Prompt);
        }

        [Test]
        public void LocalFallbackProducesValidLevel2AdviceForWeakEvidence()
        {
            Type requestType = GetRuntimeType("Game.Debate.CoachFeedbackRequest");
            Type resultType = GetRuntimeType("Game.Debate.CoachFeedbackResult");
            Type levelType = GetRuntimeType("Game.Debate.CoachFeedbackLevel");
            Type generatorType = GetRuntimeType("Game.Debate.DebateCoachFeedbackGenerator");
            Assert.IsNotNull(requestType);
            Assert.IsNotNull(resultType);
            Assert.IsNotNull(levelType);
            Assert.IsNotNull(generatorType);

            object request = Activator.CreateInstance(requestType);
            SetField(request, "PlayerUtteranceText", "Speaking is better because I think so.");
            SetField(request, "SelectedStrategy", "Logos");
            SetField(request, "FeedbackLevel", Enum.Parse(levelType, "Level2"));

            object result = generatorType.GetMethod("BuildLocalFallback", BindingFlags.Public | BindingFlags.Static)
                ?.Invoke(null, new[] { request });
            Assert.IsNotNull(result);
            Assert.AreEqual("Evidence", GetField(result, "WeakComponent"));
            Assert.AreEqual("Logos", GetField(result, "RecommendedStrategy"));
            StringAssert.Contains("evidence", ((string)GetField(result, "FeedbackText")).ToLowerInvariant());
        }

        [Test]
        public void CoachLoggerWritesSupplementHeaderAndEscapesRows()
        {
            Type loggerType = GetRuntimeType("Game.Debate.DebateCoachLogger");
            Type rowType = GetRuntimeType("Game.Debate.CoachFeedbackLogRow");
            Assert.IsNotNull(loggerType);
            Assert.IsNotNull(rowType);

            string path = Path.Combine(Path.GetTempPath(), "coach-log-" + Guid.NewGuid().ToString("N") + ".csv");
            object logger = Activator.CreateInstance(loggerType, path);
            object row = Activator.CreateInstance(rowType);
            SetField(row, "ParticipantId", "P,01");
            SetField(row, "Condition", "Condition C");
            SetField(row, "Stage", "Practice Debate");
            SetField(row, "TurnId", 2);
            SetField(row, "PlayerUtteranceText", "one, \"two\"\nthree");
            SetField(row, "CoachFeedbackLevel", "Level2");
            SetField(row, "CoachFeedbackText", "Your evidence is weak. Add one example.");
            SetField(row, "TimestampFeedbackShown", "2026-07-03T00:00:00Z");

            loggerType.GetMethod("LogFeedback")?.Invoke(logger, new[] { row });

            string csv = File.ReadAllText(path);
            StringAssert.Contains("participant_id,condition,stage,topic_id,turn_id,player_side", csv);
            StringAssert.Contains("\"P,01\"", csv);
            StringAssert.Contains("\"one, \"\"two\"\"\nthree\"", csv);
        }

        [Test]
        public void SharedInitiativeControllerDefaultsToAutomaticVoiceCoachFlow()
        {
            Assert.IsTrue(SharedInitiativeOrchestrationController.DefaultAutomaticCoachAfterPlayerVoice);
            Assert.IsFalse(SharedInitiativeOrchestrationController.DefaultShowManualPauseButton);

            string source = File.ReadAllText(
                Path.Combine(GetAssetsPath(), "Game/Scripts/SharedInitiativeOrchestrationController.cs"));

            StringAssert.Contains("ConvaiGRPCAPI.TryHandleUserVoiceTranscript", source);
            StringAssert.Contains("Phase == OrchestrationPhase.OpponentSpeaking", source);
            StringAssert.Contains("accepted player voice while the opponent-speaking phase was still active", source);
            StringAssert.Contains("Need an example?", source);
            StringAssert.Contains("DebateCoachLogger", source);
            StringAssert.Contains("OnDisable()", source);
            StringAssert.Contains("UnregisterVoiceInterceptor();", source);
        }

        [Test]
        public void SharedInitiativeSceneWiresCoachControllerAndTranscriptBridge()
        {
            string scene = ReadAssetText(SharedInitiativeScenePath);

            StringAssert.Contains(
                $"m_Script: {{fileID: 11500000, guid: {SharedInitiativeControllerGuid}, type: 3}}",
                scene);
            StringAssert.Contains("automaticCoachAfterPlayerVoice: 1", scene);
            StringAssert.Contains("showManualPauseButton: 0", scene);
            StringAssert.Contains("speakCoachFeedback: 1", scene);
            StringAssert.Contains("previousNpcVersionsViewed: []", scene);
            StringAssert.Contains("legacyRoundTimer: {fileID: 1186777322}", scene);
            StringAssert.Contains("transcriptBridge: {fileID: 8800100002}", scene);
            StringAssert.Contains(
                $"m_Script: {{fileID: 11500000, guid: {TranscriptBridgeGuid}, type: 3}}",
                scene);
            AssertSceneObjectInactive(scene, "Round Timer");
        }

        [Test]
        public void CoachControllerIsOnlyPresentInSharedInitiativeScene()
        {
            string scenesRoot = Path.Combine(GetAssetsPath(), "Game/Scenes");
            string[] scenePaths = Directory.GetFiles(scenesRoot, "*.unity", SearchOption.AllDirectories);
            string[] scenesWithCoach = scenePaths
                .Where(path => File.ReadAllText(path).Contains(SharedInitiativeControllerGuid))
                .Select(path => Path.GetFileName(path))
                .ToArray();

            CollectionAssert.AreEquivalent(
                new[] { "Level_SharedInitiativeOrchestration.unity" },
                scenesWithCoach);
        }

        private static Type GetRuntimeType(string typeName)
        {
            return RuntimeAssembly.GetType(typeName);
        }

        private static void SetField(object instance, string fieldName, object value)
        {
            FieldInfo field = instance.GetType().GetField(fieldName, BindingFlags.Public | BindingFlags.Instance);
            Assert.IsNotNull(field, fieldName);
            field.SetValue(instance, value);
        }

        private static object GetField(object instance, string fieldName)
        {
            FieldInfo field = instance.GetType().GetField(fieldName, BindingFlags.Public | BindingFlags.Instance);
            Assert.IsNotNull(field, fieldName);
            return field.GetValue(instance);
        }

        private static string InvokeString(Type type, string methodName, object argument)
        {
            MethodInfo method = type.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static);
            Assert.IsNotNull(method, methodName);
            return (string)method.Invoke(null, new[] { argument });
        }

        private static string ReadAssetText(string relativePath)
        {
            return File.ReadAllText(Path.Combine(GetAssetsPath(), relativePath));
        }

        private static string GetAssetsPath()
        {
            string assetsPath;
            try
            {
                assetsPath = Application.dataPath;
            }
            catch
            {
                assetsPath = null;
            }

            if (string.IsNullOrWhiteSpace(assetsPath) || !Directory.Exists(assetsPath))
            {
                assetsPath = Path.Combine(Directory.GetCurrentDirectory(), "Assets");
            }

            return assetsPath;
        }

        private static void AssertSceneObjectInactive(string scene, string objectName)
        {
            int nameIndex = scene.IndexOf("m_Name: " + objectName, StringComparison.Ordinal);
            Assert.GreaterOrEqual(nameIndex, 0, objectName);

            int objectStart = scene.LastIndexOf("--- !u!1", nameIndex, StringComparison.Ordinal);
            Assert.GreaterOrEqual(objectStart, 0, objectName);

            int objectEnd = scene.IndexOf("--- !u!", nameIndex + 1, StringComparison.Ordinal);
            if (objectEnd < 0)
            {
                objectEnd = scene.Length;
            }

            string objectBlock = scene.Substring(objectStart, objectEnd - objectStart);
            StringAssert.Contains("m_IsActive: 0", objectBlock);
        }
    }
}
