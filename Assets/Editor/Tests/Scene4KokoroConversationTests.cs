using System.IO;
using Game.Debate;
using NUnit.Framework;
using UnityEngine;

namespace Game.Debate.Tests
{
    public sealed class Scene4KokoroConversationTests
    {
        [Test]
        public void Scene04UsesRequestedFemaleAndMaleKokoroSpeakers()
        {
            Assert.AreEqual(0, Scene4KokoroConversationController.CoachSpeakerId);
            Assert.AreEqual(5, Scene4KokoroConversationController.OpponentSpeakerId);
        }

        [Test]
        public void Scene04RoutesCoachOpponentAndIntroductionSpeechThroughLocalFlow()
        {
            string path = Path.Combine(
                Application.dataPath,
                "Game/Scripts/ThreeStageDebatePracticeController.cs");
            string source = File.ReadAllText(path);

            StringAssert.Contains("SetUseLocalConversationFlow", source);
            StringAssert.Contains("Scene4KokoroConversationController.CoachSpeakerId", source);
            StringAssert.Contains("Scene4KokoroConversationController.OpponentSpeakerId", source);
            StringAssert.Contains("if (_useLocalConversationFlow)", source);
        }

        [Test]
        public void Scene04ListensToTheSharedF5ConversationSetting()
        {
            string path = Path.Combine(
                Application.dataPath,
                "Game/Scripts/Scene4KokoroConversationController.cs");
            string source = File.ReadAllText(path);

            StringAssert.Contains("ConversationFlowSettings.DisableConvaiChanged", source);
            StringAssert.Contains("ConversationFlowSettings.DisableConvai", source);
            StringAssert.DoesNotContain("InstallF10SettingsToggle", source);
        }
    }
}
