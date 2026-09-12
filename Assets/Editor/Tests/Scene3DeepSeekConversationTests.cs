using System.Collections.Generic;
using Game.Debate;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Game.Debate.Tests
{
    public sealed class Scene3DeepSeekConversationTests
    {
        [Test]
        public void SpeakerSelection_UsesRequestedKokoroVoices()
        {
            Assert.AreEqual(
                Scene3DeepSeekConversationController.FemaleSpeakerId,
                Scene3DeepSeekConversationController.ResolveKokoroSpeakerId("Berance"));
            Assert.AreEqual(
                Scene3DeepSeekConversationController.MaleSpeakerId,
                Scene3DeepSeekConversationController.ResolveKokoroSpeakerId("Leo"));
        }

        [Test]
        public void SystemPrompt_PreservesPersonaPositionAndSpokenReplyContract()
        {
            string prompt = Scene3DeepSeekConversationController.BuildSystemPrompt(
                "Should students practice alone or with other people?",
                "Berance");

            StringAssert.Contains("You are Berance", prompt);
            StringAssert.Contains("Interaction with other people is more beneficial", prompt);
            StringAssert.Contains("2 to 4 short sentences", prompt);
            StringAssert.Contains("not a tutor, examiner, narrator, or AI assistant", prompt);
            StringAssert.Contains("Return only the exact words Berance should say aloud", prompt);
        }

        [Test]
        public void RequestJson_UsesDeepSeekFlashAndKeepsConversationHistory()
        {
            KeyValuePair<string, string>[] history =
            {
                new("user", "Individual practice gives me more time to think."),
                new("assistant", "Time to think helps, but speaking also requires quick interaction.")
            };

            JObject request = Scene3DeepSeekConversationController.BuildRequestJson(
                "Practice alone or interact with others?",
                "Berance",
                history,
                "Feedback from a partner can reveal my mistakes.");

            Assert.AreEqual("deepseek-flash", (string)request["model"]);
            Assert.AreEqual("disabled", (string)request["thinking"]?["type"]);
            Assert.AreEqual(4, ((JArray)request["messages"]).Count);
            Assert.AreEqual("user", (string)request["messages"]?[3]?["role"]);
        }

        [Test]
        public void PlayerVsNpcFlow_SupportsBaselineAndTransferScenes()
        {
            Assert.IsTrue(Scene3DeepSeekConversationController.IsSupportedScene(
                "03Level_PlayerVsNPCDebate"));
            Assert.IsTrue(Scene3DeepSeekConversationController.IsSupportedScene(
                "05Level_PlayerVsNPCDebate 1"));
            Assert.IsFalse(Scene3DeepSeekConversationController.IsSupportedScene(
                "04 coach Agent"));
        }

        [Test]
        public void TransferRequest_UsesClassroomInstructionOpponentPosition()
        {
            JObject request = Scene3DeepSeekConversationController.BuildRequestJson(
                "Classroom instruction or real-life context?",
                "Berance",
                null,
                "Please present your opening argument.",
                Scene3DeepSeekConversationController.TransferOpponentPosition);

            string systemPrompt = (string)request["messages"]?[0]?["content"];
            StringAssert.Contains(
                "Classroom instruction is more beneficial than real-life context",
                systemPrompt);
        }

        [TestCase("\"That is a fair point.\"", "That is a fair point.")]
        [TestCase("```\nThat is a fair point.\n```", "That is a fair point.")]
        public void NormalizeSpokenReply_RemovesNonSpokenWrapping(string input, string expected)
        {
            Assert.AreEqual(
                expected,
                Scene3DeepSeekConversationController.NormalizeSpokenReply(input));
        }
    }
}
