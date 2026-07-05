using Game.Debate;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using System.IO;

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
    }
}
