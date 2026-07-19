using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests.EditMode
{
    public sealed class DebugSceneJumpMenuTests
    {
        [Test]
        public void MenuOffersTheFourFormalResearchScenesInOrder()
        {
            Type menuType = typeof(Game.Debate.ResearchStudyFlowNavigator).Assembly.GetType(
                "Game.Debate.DebugSceneJumpMenu");
            Assert.IsNotNull(menuType, "The runtime F5 scene menu must exist.");

            MethodInfo getSceneNames = menuType.GetMethod(
                "GetTargetSceneNames",
                BindingFlags.Public | BindingFlags.Static);
            Assert.IsNotNull(getSceneNames);

            string[] sceneNames = (string[])getSceneNames.Invoke(null, null);
            CollectionAssert.AreEqual(
                new[]
                {
                    "01Level_NPCVsNPCDebate",
                    "03Level_PlayerVsNPCDebate",
                    "04 coach Agent",
                    "05Level_PlayerVsNPCDebate 1"
                },
                sceneNames);
        }

        [Test]
        public void MenuBootstrapsAutomaticallyBeforeTheFirstSceneLoads()
        {
            Type menuType = typeof(Game.Debate.ResearchStudyFlowNavigator).Assembly.GetType(
                "Game.Debate.DebugSceneJumpMenu");
            Assert.IsNotNull(menuType);

            MethodInfo bootstrap = menuType.GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
                .FirstOrDefault(method => method.GetCustomAttributes(
                    typeof(RuntimeInitializeOnLoadMethodAttribute), false).Length > 0);

            Assert.IsNotNull(bootstrap, "The F5 menu should not require scene-by-scene setup.");
        }
    }
}
