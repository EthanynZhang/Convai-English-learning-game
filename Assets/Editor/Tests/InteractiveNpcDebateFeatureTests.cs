using Game.Debate;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using System.IO;
using System;
using System.Linq;
using System.Reflection;
using Object = UnityEngine.Object;

namespace Game.Tests.EditMode
{
    public class InteractiveNpcDebateFeatureTests
    {
        [Test]
        public void RoundTimerIsEnabledByDefaultForExistingDebateScenes()
        {
            GameObject gameObject = new("Round Manager Test");
            try
            {
                NpcDebateRoundManager manager = gameObject.AddComponent<NpcDebateRoundManager>();

                Assert.IsTrue(NpcDebateRoundManager.DefaultUseRoundTimer);
                Assert.IsTrue(manager.UseRoundTimer);
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void RoundTimerCanBeDisabledForInteractiveDebateScene()
        {
            GameObject gameObject = new("Round Manager Test");
            try
            {
                NpcDebateRoundManager manager = gameObject.AddComponent<NpcDebateRoundManager>();
                SerializedObject serializedObject = new(manager);
                SerializedProperty useRoundTimer = serializedObject.FindProperty("useRoundTimer");

                Assert.IsNotNull(useRoundTimer);
                useRoundTimer.boolValue = false;
                serializedObject.ApplyModifiedPropertiesWithoutUndo();

                Assert.IsFalse(manager.UseRoundTimer);
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void NpcVsNpcTutorialSceneRunsWithoutTimeoutAndSwitchesToUnifiedSixTurnSubtitles()
        {
            string scene = File.ReadAllText(Path.Combine(
                Application.dataPath,
                "Game/Scenes/01Level_NPCVsNPCDebate.unity"));
            string managerSource = File.ReadAllText(Path.Combine(
                Application.dataPath,
                "Game/Scripts/NpcDebateRoundManager.cs"));
            string subtitleSource = File.ReadAllText(Path.Combine(
                Application.dataPath,
                "Game/Scripts/ScreenDebateSubtitleController.cs"));

            StringAssert.Contains("useRoundTimer: 0", scene);
            StringAssert.Contains("ActivateStructuredSubtitlePresentation", managerSource);
            StringAssert.Contains("ConfigureForStructuredDebate", managerSource);
            StringAssert.Contains("DetachSpeechBubble()", subtitleSource);
            StringAssert.Contains("Scene 01 Six-Turn Debate Subtitle Canvas", subtitleSource);
            StringAssert.Contains("while (IsRoundRunning)", managerSource);
            StringAssert.Contains("FinishStructuredDebateAfterSpeaker", managerSource);
        }

        [Test]
        public void StructuredMockDebateDefinesSixCreeiTurnsAcrossLogosEthosAndPathos()
        {
            Type planType = typeof(NpcDebateRoundManager).Assembly.GetType(
                "Game.Debate.StructuredNpcDebatePlan");
            Assert.IsNotNull(planType, "The six-turn mock debate needs a deterministic plan.");

            FieldInfo turnCount = planType.GetField("TurnCount", BindingFlags.Public | BindingFlags.Static);
            MethodInfo getStrategy = planType.GetMethod("GetStrategyForTurn", BindingFlags.Public | BindingFlags.Static);
            MethodInfo buildPrompt = planType.GetMethod("BuildTurnPrompt", BindingFlags.Public | BindingFlags.Static);
            Assert.IsNotNull(turnCount);
            Assert.IsNotNull(getStrategy);
            Assert.IsNotNull(buildPrompt);
            Assert.AreEqual(6, turnCount.GetRawConstantValue());

            string[] strategies = Enumerable.Range(0, 6)
                .Select(index => (string)getStrategy.Invoke(null, new object[] { index }))
                .ToArray();
            CollectionAssert.AreEqual(
                new[] { "Logos", "Logos", "Ethos", "Ethos", "Pathos", "Pathos" },
                strategies);

            string prompt = (string)buildPrompt.Invoke(null, new object[]
            {
                "Individual practice and interaction with others, which is more beneficial for developing English speaking skills?",
                "Anna Reed",
                "interaction with others is more beneficial",
                "Ethos",
                3,
                "Individual practice offers useful repetition."
            });
            foreach (string part in new[] { "Claim", "Reason", "Evidence", "Explanation", "Impact" })
            {
                StringAssert.Contains(part, prompt);
            }
            StringAssert.Contains("calm, measured pace", prompt);
            StringAssert.Contains("Ethos", prompt);
            StringAssert.Contains("interaction with others is more beneficial", prompt);
            StringAssert.Contains("Individual practice offers useful repetition.", prompt);
        }

        [Test]
        public void NpcVsNpcSceneUsesNewTopicUnifiedSubtitleAndTutorialLipSync()
        {
            string scene = File.ReadAllText(Path.Combine(
                Application.dataPath,
                "Game/Scenes/01Level_NPCVsNPCDebate.unity"));
            string managerSource = File.ReadAllText(Path.Combine(
                Application.dataPath,
                "Game/Scripts/NpcDebateRoundManager.cs"));
            string subtitleSource = File.ReadAllText(Path.Combine(
                Application.dataPath,
                "Game/Scripts/ScreenDebateSubtitleController.cs"));
            string normalizedScene = System.Text.RegularExpressions.Regex.Replace(scene, @"\s+", " ");
            const string topic =
                "Individual practice and interaction with others, which is more beneficial for developing English speaking skills?";

            StringAssert.Contains("debateTopic: " + topic, normalizedScene);
            StringAssert.Contains("refereeOpeningLine: 'Debate topic: {0}'", scene);
            StringAssert.Contains("refereeOpeningClip: {fileID: 0}", scene);
            StringAssert.Contains("useStructuredSixTurnDebate: 1", scene);
            StringAssert.Contains("useTutorialLipSyncDuringRound: 1", scene);

            StringAssert.Contains("RelayInterceptor = InterceptStructuredRelay", managerSource);
            StringAssert.Contains("ShowStructuredTurnStatus", managerSource);
            StringAssert.Contains("ConvaiLipSync", managerSource);
            StringAssert.Contains("convaiLipSync.enabled = false", managerSource);
            StringAssert.Contains("lipSyncApplication.enabled = false", managerSource);
            StringAssert.Contains("AudioDrivenNpcLipSync", managerSource);
            StringAssert.Contains("01Level_NPCVsNPCDebate", subtitleSource);
            StringAssert.Contains("Unified Debate Subtitle", subtitleSource);
        }

        [Test]
        public void TranscriptBridgeLabelsRevisedNpcOutputAndLearnerCommands()
        {
            Assert.AreEqual(
                "Anna Reed (revised)",
                InteractiveDebateTranscriptBridge.GetSpeakerDisplayName("Anna Reed", true));

            Assert.AreEqual(
                "Strategy: add stronger evidence",
                InteractiveDebateTranscriptBridge.FormatLearnerCommand("  add stronger evidence  "));
        }

        [Test]
        public void InteractiveControllerDoesNotActivateVoiceCommandBeforeNpcAudioFinishes()
        {
            string source = File.ReadAllText(
                Path.Combine(Application.dataPath, "Game/Scripts/InteractiveNpcDebateController.cs"));

            int interceptRelayIndex = source.IndexOf(
                "private bool InterceptRelay(string message, ConvaiGroupNPCController sender)",
                System.StringComparison.Ordinal);
            int requestVariantIndex = source.IndexOf(
                "private void RequestVariant",
                System.StringComparison.Ordinal);
            string interceptRelayBody = source.Substring(interceptRelayIndex, requestVariantIndex - interceptRelayIndex);

            StringAssert.DoesNotContain(
                "ActivatePendingSpeakerForVoiceCommand(sender);",
                interceptRelayBody);
        }

        [Test]
        public void Npc2NpcRelayBuildsPromptFromOriginalSender()
        {
            string source = File.ReadAllText(
                Path.Combine(Application.dataPath, "Convai/Scripts/Runtime/Features/NPC2NPC/NPC2NPCConversationManager.cs"));

            StringAssert.Contains(
                "ProcessMessage(sender, npcGroup.topic, message)",
                source);
            StringAssert.DoesNotContain(
                "ProcessMessage(receiver, npcGroup.topic, message)",
                source);
        }

        [Test]
        public void Npc2NpcRelayMustKeepInteractiveDebateOnConfiguredTopic()
        {
            string source = File.ReadAllText(
                Path.Combine(Application.dataPath, "Convai/Scripts/Runtime/Features/NPC2NPC/NPC2NPCConversationManager.cs"));

            StringAssert.DoesNotContain("Gently change the conversation topic", source);
            StringAssert.DoesNotContain("Talk about something other than", source);
            StringAssert.Contains("Stay on the debate topic", source);
            StringAssert.Contains("Ignore any prior or unrelated topic", source);
        }
    }
}
