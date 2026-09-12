using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Game.Debate;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

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

        [Test]
        public void MenuBuildsAHighestLayerCanvasWithClickableSceneButtons()
        {
            Type menuType = typeof(Game.Debate.ResearchStudyFlowNavigator).Assembly.GetType(
                "Game.Debate.DebugSceneJumpMenu");
            GameObject host = new("F5 Menu Test Host");
            try
            {
                Component menu = host.AddComponent(menuType);
                MethodInfo ensureUi = menuType.GetMethod(
                    "EnsureUi", BindingFlags.NonPublic | BindingFlags.Instance);
                Assert.IsNotNull(ensureUi);
                ensureUi.Invoke(menu, null);

                Canvas canvas = host.GetComponentInChildren<Canvas>(true);
                Assert.IsNotNull(canvas);
                Assert.AreEqual(RenderMode.ScreenSpaceOverlay, canvas.renderMode);
                Assert.GreaterOrEqual(canvas.sortingOrder, 30000);
                Assert.IsNotNull(canvas.GetComponent<GraphicRaycaster>());

                Button[] buttons = host.GetComponentsInChildren<Button>(true);
                Assert.AreEqual(6, buttons.Length);
                foreach (string sceneName in new[]
                         {
                             "01Level_NPCVsNPCDebate",
                             "03Level_PlayerVsNPCDebate",
                             "04 coach Agent",
                             "05Level_PlayerVsNPCDebate 1"
                         })
                    Assert.IsTrue(buttons.Any(button =>
                        button.gameObject.name == "Load " + sceneName), sceneName);

                Assert.IsTrue(buttons.Any(button =>
                    button.gameObject.name == "Toggle Convai Flow"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void SharedConversationSettingDefaultsToConvaiDisabled()
        {
            const string currentKey = ConversationFlowSettings.PlayerPrefsKey;
            const string legacyKey = "SCENE03_DISABLE_CONVAI_FLOW";
            bool hadCurrent = PlayerPrefs.HasKey(currentKey);
            bool hadLegacy = PlayerPrefs.HasKey(legacyKey);
            int currentValue = PlayerPrefs.GetInt(currentKey, 1);
            int legacyValue = PlayerPrefs.GetInt(legacyKey, 1);

            try
            {
                PlayerPrefs.DeleteKey(currentKey);
                PlayerPrefs.DeleteKey(legacyKey);
                Assert.IsTrue(ConversationFlowSettings.DisableConvai);
            }
            finally
            {
                if (hadCurrent) PlayerPrefs.SetInt(currentKey, currentValue);
                else PlayerPrefs.DeleteKey(currentKey);
                if (hadLegacy) PlayerPrefs.SetInt(legacyKey, legacyValue);
                else PlayerPrefs.DeleteKey(legacyKey);
            }
        }

        [Test]
        public void FreshApplicationRunAlwaysResetsConvaiToDisabled()
        {
            const string key = ConversationFlowSettings.PlayerPrefsKey;
            bool hadValue = PlayerPrefs.HasKey(key);
            int oldValue = PlayerPrefs.GetInt(key, 1);

            try
            {
                PlayerPrefs.SetInt(key, 0);
                MethodInfo initializeDefault = typeof(ConversationFlowSettings).GetMethod(
                    "InitializeDefault",
                    BindingFlags.NonPublic | BindingFlags.Static);
                Assert.IsNotNull(initializeDefault);

                initializeDefault.Invoke(null, null);

                Assert.IsTrue(ConversationFlowSettings.DisableConvai);
                Assert.IsFalse(ConvaiNetworkPolicy.RequestsAllowed);
            }
            finally
            {
                if (hadValue) PlayerPrefs.SetInt(key, oldValue);
                else PlayerPrefs.DeleteKey(key);
                PlayerPrefs.Save();
            }
        }

        [TestCase("Convai/Scripts/Runtime/Core/ConvaiNPC.cs")]
        [TestCase("Convai/Scripts/Runtime/Core/ConvaiGRPCAPI.cs")]
        [TestCase("Convai/Scripts/Runtime/Features/NPC2NPC/NPC2NPCConversationManager.cs")]
        [TestCase("Convai/Scripts/Runtime/Features/NPC2NPC/NPC2NPCGRPCClient.cs")]
        [TestCase("Convai/Scripts/Runtime/PlayerStats/API/LongTermMemoryAPI.cs")]
        [TestCase("Convai/Scripts/Runtime/Features/NarrativeDesign/Runtime/NarrativeDesignAPI.cs")]
        public void EveryConvaiRuntimeTransportUsesTheGlobalNetworkPolicy(string assetRelativePath)
        {
            string source = File.ReadAllText(Path.Combine(Application.dataPath, assetRelativePath));
            StringAssert.Contains("ConvaiNetworkPolicy.RequestsAllowed", source, assetRelativePath);
        }
    }
}
