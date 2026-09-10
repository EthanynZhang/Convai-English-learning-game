using System;
using System.IO;
using System.Reflection;
using Game.Debate;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Game.Tests.EditMode
{
    public sealed class ResearchExperienceEnhancementTests
    {
        [Test]
        public void Scene01EmbedsTheControlsGuideAndQuestionnaireQrInItsFirstPage()
        {
            Type controller = typeof(DebateLearningContent).Assembly.GetType(
                "Game.Debate.NpcDebateLearningPhaseController");
            Assert.IsNotNull(controller);
            Assert.IsNotNull(controller.GetField("controlsGuideSprite",
                BindingFlags.NonPublic | BindingFlags.Instance));
            Assert.IsNotNull(controller.GetField("questionnaireQrSprite",
                BindingFlags.NonPublic | BindingFlags.Instance));

            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<Sprite>(
                "Assets/Game/UI/Controls/DebateControlsGuide.png"));
            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<Sprite>(
                "Assets/Game/UI/Controls/Questionire.png"));

            string scene = File.ReadAllText(
                "Assets/Game/Scenes/01Level_NPCVsNPCDebate.unity");
            StringAssert.Contains("controlsGuideSprite:", scene);
            StringAssert.Contains("questionnaireQrSprite:", scene);
            StringAssert.DoesNotContain("controlsGuideSprite: {fileID: 0}", scene);
            StringAssert.DoesNotContain("questionnaireQrSprite: {fileID: 0}", scene);
        }

        [Test]
        public void GlobalRuntimeControlsSupportRightClickCursorAndF5Exit()
        {
            string source = File.ReadAllText(
                "Assets/Game/Scripts/DebugSceneJumpMenu.cs");
            StringAssert.Contains("WasRightMousePressed", source);
            StringAssert.Contains("Cursor.lockState = CursorLockMode.None", source);
            StringAssert.Contains("Exit Application", source);
            StringAssert.Contains("Application.Quit()", source);
        }

        [Test]
        public void Scene03And05HavePurposeCopyAndAConfirmedStartGate()
        {
            MethodInfo purpose = typeof(DebatePreparationController).GetMethod(
                "GetScenePurpose", BindingFlags.Public | BindingFlags.Static);
            Assert.IsNotNull(purpose);
            string scene03 = (string)purpose.Invoke(null,
                new object[] { "03Level_PlayerVsNPCDebate" });
            string scene05 = (string)purpose.Invoke(null,
                new object[] { "05Level_PlayerVsNPCDebate 1" });
            Assert.IsNotEmpty(scene03);
            Assert.IsNotEmpty(scene05);

            string source = File.ReadAllText(
                "Assets/Game/Scripts/DebatePreparationController.cs");
            StringAssert.Contains("Confirm & Start Preparation", source);
            StringAssert.Contains("Scene Task Introduction", source);
        }

        [Test]
        public void Scene03And05InstallAFirstPersonDebateStation()
        {
            Type stationType = typeof(DebateLearningContent).Assembly.GetType(
                "Game.Debate.DebateSpeakerStation");
            Assert.IsNotNull(stationType);
            MethodInfo targetsScene = stationType.GetMethod(
                "TargetsScene", BindingFlags.Public | BindingFlags.Static);
            Assert.IsNotNull(targetsScene);
            Assert.IsTrue((bool)targetsScene.Invoke(null,
                new object[] { "03Level_PlayerVsNPCDebate" }));
            Assert.IsTrue((bool)targetsScene.Invoke(null,
                new object[] { "05Level_PlayerVsNPCDebate 1" }));
            Assert.IsFalse((bool)targetsScene.Invoke(null,
                new object[] { "04 coach Agent" }));
        }

        [Test]
        public void Scene03And05UseTheImportedWoodenPodiumAtAFixedWorldPosition()
        {
            const string podiumAsset =
                "Assets/Game/Environment/WoodenPodium/Wooden Podium PBR Low-poly 3D model.fbx";
            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<GameObject>(podiumAsset));

            Type stationType = typeof(DebateLearningContent).Assembly.GetType(
                "Game.Debate.DebateSpeakerStation");
            MethodInfo calculatePosition = stationType.GetMethod(
                "CalculateWorldPosition", BindingFlags.Public | BindingFlags.Static);
            Assert.IsNotNull(calculatePosition);
            Vector3 position = (Vector3)calculatePosition.Invoke(null,
                new object[] { new Vector3(0f, 1f, -1.2f), Vector3.forward, 0f });
            Assert.AreEqual(0f, position.y, 0.001f);
            Assert.Greater(position.z, 0f);
            Assert.Less(position.z, 0.5f);

            string source = File.ReadAllText(
                "Assets/Game/Scripts/DebateSpeakerStation.cs");
            StringAssert.Contains("WoodenPodiumResourcePath", source);
            StringAssert.Contains("transform.SetPositionAndRotation", source);
            StringAssert.DoesNotContain("transform.SetParent(camera.transform", source);
        }

        [Test]
        public void WoodenPodiumFacesTheLearnerIsTallerAndHasNoMicrophone()
        {
            string source = File.ReadAllText(
                "Assets/Game/Scripts/DebateSpeakerStation.cs");
            StringAssert.Contains("new Vector3(1.2f, 1.35f, 1.2f)", source);
            StringAssert.Contains("Quaternion.Euler(0f, 180f, 0f)", source);
            StringAssert.Contains("PodiumVerticalOffset", source);
            StringAssert.Contains("BuiltInMicrophoneMeshName", source);
            StringAssert.Contains("builtInMicrophone.gameObject.SetActive(false)", source);
            StringAssert.DoesNotContain("Microphone Stand", source);
            StringAssert.DoesNotContain("Debate Microphone", source);
        }

        [Test]
        public void Scene03And04ExposeExplicitContinueActions()
        {
            Assert.IsNotNull(typeof(PlayerOralPracticeController).GetMethod(
                "ContinueToNextScene", BindingFlags.Public | BindingFlags.Instance));
            Assert.IsNotNull(typeof(ThreeStageDebatePracticeView).GetEvent(
                "ContinueRequested", BindingFlags.Public | BindingFlags.Instance));
            Assert.IsNotNull(typeof(ThreeStageDebatePracticeView).GetMethod(
                "ShowCompletion", BindingFlags.Public | BindingFlags.Instance));
        }

        [Test]
        public void Scene05PreparationFormatsAndDisplaysTheTransferTopic()
        {
            Assert.IsNotNull(typeof(DebateRoundManager).GetProperty(
                "DebateTopic", BindingFlags.Public | BindingFlags.Instance));
            MethodInfo formatter = typeof(DebatePreparationController).GetMethod(
                "FormatTopicLine", BindingFlags.Public | BindingFlags.Static);
            Assert.IsNotNull(formatter);
            string topic = (string)formatter.Invoke(null,
                new object[] { "Classroom instruction or real-life context?" });
            StringAssert.Contains("Classroom instruction or real-life context?", topic);
        }
    }
}
