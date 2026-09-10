using System;
using System.Linq;
using System.Reflection;
using Game.Debate;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Game.Tests.EditMode
{
    public sealed class TransferDebateSceneTests
    {
        private const string ScenePath =
            "Assets/Game/Scenes/05Level_PlayerVsNPCDebate 1.unity";
        private const string ExpectedTopic =
            "Classroom instruction or real-life context: which is more beneficial for English speaking learning?";

        [Test]
        public void Scene05UsesTheNewTransferTopicAndOpponentFirstFlow()
        {
            Scene existingScene = SceneManager.GetSceneByPath(ScenePath);
            bool openedHere = !existingScene.IsValid() || !existingScene.isLoaded;
            Scene scene = openedHere
                ? EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive)
                : existingScene;

            try
            {
                DebateRoundManager manager = scene.GetRootGameObjects()
                    .SelectMany(root => root.GetComponentsInChildren<DebateRoundManager>(true))
                    .Single();
                SerializedObject serialized = new(manager);

                SerializedProperty waitForOpponent = serialized.FindProperty(
                    "waitForOpponentOpeningBeforePlayerPractice");
                SerializedProperty waitForPlayer = serialized.FindProperty(
                    "waitForPlayerSpeechBeforeTimer");
                Assert.IsNotNull(waitForOpponent);
                Assert.IsNotNull(waitForPlayer);

                Assert.AreEqual(ExpectedTopic,
                    serialized.FindProperty("debateTopic").stringValue);
                Assert.IsTrue(serialized.FindProperty("sendOpponentOpeningPrompt").boolValue);
                Assert.IsFalse(serialized.FindProperty("disableOpponentConversation").boolValue);
                Assert.IsTrue(waitForOpponent.boolValue);
                Assert.IsTrue(waitForPlayer.boolValue);
                Assert.IsNull(serialized.FindProperty("refereeOpeningClip").objectReferenceValue);
                Assert.IsTrue(serialized.FindProperty("useMiniMaxRefereeNarration").boolValue);
                Assert.IsNull(serialized.FindProperty("refereeCaptionText").objectReferenceValue);
                Assert.IsNull(serialized.FindProperty("startButton").objectReferenceValue);

                GameObject obsoleteStartButton = scene.GetRootGameObjects()
                    .SelectMany(root => root.GetComponentsInChildren<Transform>(true))
                    .Single(transform => transform.gameObject.name == "Start Debate Button")
                    .gameObject;
                Assert.IsFalse(obsoleteStartButton.activeSelf);

                string opening = serialized.FindProperty("refereeOpeningLine").stringValue;
                string opponentPrompt = serialized.FindProperty("opponentOpeningPrompt").stringValue;
                StringAssert.Contains("classroom instruction", opening.ToLowerInvariant());
                StringAssert.Contains("transfer argument", opening.ToLowerInvariant());
                StringAssert.Contains("speak first", opponentPrompt.ToLowerInvariant());
                StringAssert.Contains("classroom instruction", opponentPrompt.ToLowerInvariant());
                StringAssert.Contains("real-life context", opponentPrompt.ToLowerInvariant());
                StringAssert.Contains("CREEI", opponentPrompt);
                Assert.IsNotNull(scene.GetRootGameObjects()
                    .SelectMany(root => root.GetComponentsInChildren<TransferDebateStageGuard>(true))
                    .SingleOrDefault());
                DebatePreparationController preparation = scene.GetRootGameObjects()
                    .SelectMany(root => root.GetComponentsInChildren<DebatePreparationController>(true))
                    .SingleOrDefault();
                Assert.IsNotNull(preparation);
                Assert.IsNotNull(preparation.GetComponent<XfyunRealtimeTranscriber>());
                SerializedObject serializedPreparation = new(preparation);
                Assert.AreEqual(180f,
                    serializedPreparation.FindProperty("preparationDurationSeconds").floatValue);
                Assert.AreSame(manager,
                    serializedPreparation.FindProperty("roundManager").objectReferenceValue);
                Assert.AreSame(preparation.GetComponent<XfyunRealtimeTranscriber>(),
                    serializedPreparation.FindProperty("realtimeTranscriber").objectReferenceValue);
            }
            finally
            {
                if (openedHere && scene.IsValid())
                {
                    EditorSceneManager.CloseScene(scene, true);
                }
            }
        }

        [TestCase("Assets/Game/Scenes/03Level_PlayerVsNPCDebate.unity")]
        [TestCase("Assets/Game/Scenes/05Level_PlayerVsNPCDebate 1.unity")]
        public void RefereeLabelAndYellowCaptionAreDisabled(string scenePath)
        {
            Scene existingScene = SceneManager.GetSceneByPath(scenePath);
            bool openedHere = !existingScene.IsValid() || !existingScene.isLoaded;
            Scene scene = openedHere
                ? EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive)
                : existingScene;
            try
            {
                DebateRoundManager manager = scene.GetRootGameObjects()
                    .SelectMany(root => root.GetComponentsInChildren<DebateRoundManager>(true))
                    .Single();
                Assert.IsNull(new SerializedObject(manager)
                    .FindProperty("refereeCaptionText").objectReferenceValue);

                GameObject label = scene.GetRootGameObjects()
                    .SelectMany(root => root.GetComponentsInChildren<Transform>(true))
                    .Single(transform => transform.gameObject.name == "Referee Label")
                    .gameObject;
                GameObject captionPanel = scene.GetRootGameObjects()
                    .SelectMany(root => root.GetComponentsInChildren<Transform>(true))
                    .Single(transform => transform.gameObject.name == "Referee Caption Panel")
                    .gameObject;
                Assert.IsFalse(label.activeSelf);
                Assert.IsFalse(captionPanel.activeSelf);
            }
            finally
            {
                if (openedHere && scene.IsValid())
                {
                    EditorSceneManager.CloseScene(scene, true);
                }
            }
        }

        [Test]
        public void CoachFlowDeclaresTheDistinctScene05TransferTopic()
        {
            FieldInfo topic = typeof(TransferDebateStageGuard).GetField(
                "TransferTopic",
                BindingFlags.Public | BindingFlags.Static);
            Assert.IsNotNull(topic);
            Assert.AreEqual(ExpectedTopic, topic.GetValue(null));

            const string scene04Path = "Assets/Game/Scenes/04 coach Agent.unity";
            Scene existingScene = SceneManager.GetSceneByPath(scene04Path);
            bool openedHere = !existingScene.IsValid() || !existingScene.isLoaded;
            Scene scene = openedHere
                ? EditorSceneManager.OpenScene(scene04Path, OpenSceneMode.Additive)
                : existingScene;
            try
            {
                ThreeStageDebatePracticeController controller = scene.GetRootGameObjects()
                    .SelectMany(root => root.GetComponentsInChildren<ThreeStageDebatePracticeController>(true))
                    .Single();
                SerializedProperty transferTopic = new SerializedObject(controller)
                    .FindProperty("transferDebateTopic");
                Assert.IsNotNull(transferTopic);
                Assert.AreEqual(ExpectedTopic, transferTopic.stringValue);
            }
            finally
            {
                if (openedHere && scene.IsValid())
                {
                    EditorSceneManager.CloseScene(scene, true);
                }
            }
        }
    }
}
