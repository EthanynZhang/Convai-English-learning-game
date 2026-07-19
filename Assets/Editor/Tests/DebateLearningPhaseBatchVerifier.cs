using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Game.Debate;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Game.Tests.EditMode
{
    public static class DebateLearningPhaseBatchVerifier
    {
        private const string ScenePath = "Assets/Game/Scenes/01Level_NPCVsNPCDebate.unity";
        private const string RunningKey = "Codex.DebateLearningPhaseBatchVerifier.Running";
        private const string ParticipantKey = "Codex.DebateLearningPhaseBatchVerifier.Participant";

        private static string _participantId;
        private static int _framesAfterCompletion;
        private static bool _completedLearning;

        [InitializeOnLoadMethod]
        private static void ResumeAfterDomainReload()
        {
            if (!SessionState.GetBool(RunningKey, false))
            {
                return;
            }

            _participantId = SessionState.GetString(ParticipantKey, string.Empty);
            EditorApplication.playModeStateChanged -= HandlePlayModeStateChanged;
            EditorApplication.playModeStateChanged += HandlePlayModeStateChanged;

            if (EditorApplication.isPlaying)
            {
                Debug.Log("CODEX_DEBATE_LEARNING_BATCH_VERIFIER_RESUME_PLAYMODE");
                EditorApplication.update -= VerifyRuntime;
                EditorApplication.update += VerifyRuntime;
            }
        }

        public static void Run()
        {
            _participantId = "BATCH_" + Guid.NewGuid().ToString("N");
            _framesAfterCompletion = 0;
            _completedLearning = false;
            SessionState.SetBool(RunningKey, true);
            SessionState.SetString(ParticipantKey, _participantId);
            Debug.Log("CODEX_DEBATE_LEARNING_BATCH_VERIFIER_RUN " + _participantId);

            EditorApplication.playModeStateChanged -= HandlePlayModeStateChanged;
            EditorApplication.playModeStateChanged += HandlePlayModeStateChanged;

            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            EditorApplication.EnterPlaymode();
        }

        private static void HandlePlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredPlayMode)
            {
                Debug.Log("CODEX_DEBATE_LEARNING_BATCH_VERIFIER_ENTERED_PLAYMODE");
                EditorApplication.update -= VerifyRuntime;
                EditorApplication.update += VerifyRuntime;
            }
        }

        private static void VerifyRuntime()
        {
            try
            {
                if (!_completedLearning)
                {
                    RunLearningPhaseChecks();
                    _completedLearning = true;
                    return;
                }

                _framesAfterCompletion++;
                if (_framesAfterCompletion < 3)
                {
                    return;
                }

                VerifyRoundAndLog();
                Debug.Log("CODEX_DEBATE_LEARNING_BATCH_VERIFIER_PASS");
                CleanupAndExit(0);
            }
            catch (Exception ex)
            {
                Debug.LogError("CODEX_DEBATE_LEARNING_BATCH_VERIFIER_FAIL " + ex);
                CleanupAndExit(1);
            }
        }

        private static void RunLearningPhaseChecks()
        {
            Debug.Log("CODEX_DEBATE_LEARNING_BATCH_VERIFIER_CHECK_LEARNING");
            NpcDebateLearningPhaseController controller = UnityEngine.Object.FindAnyObjectByType<NpcDebateLearningPhaseController>();
            NpcDebateRoundManager roundManager = UnityEngine.Object.FindAnyObjectByType<NpcDebateRoundManager>();

            Assert.IsNotNull(controller);
            Assert.IsNotNull(roundManager);
            Assert.AreEqual(1, UnityEngine.Object.FindObjectsByType<EventSystem>(FindObjectsInactive.Exclude).Length);
            Assert.IsFalse(roundManager.IsRoundRunning);
            Assert.IsTrue(roundManager.WaitForExternalStart);

            SetPrivateField(controller, "participantId", _participantId);
            SetPrivateField(controller, "playNpcVoice", false);
            SetPrivateField(controller, "demoLineSeconds", 0.01f);

            GameObject root = GetPrivateField<GameObject>(controller, "_root");
            Transform startGate = root.transform.Find("Debate Learning Start Gate");
            Assert.IsNotNull(startGate);
            Assert.IsTrue(startGate.gameObject.activeSelf);
            InvokePrivate(controller, "BeginLearningFromStartGate");
            Assert.IsFalse(startGate.gameObject.activeSelf);
            Assert.IsFalse(string.IsNullOrWhiteSpace(
                GetPrivateField<string>(controller, "_learningSessionId")));

            Button replayButton = GetPrivateField<Button>(controller, "_replayButton");
            List<Button> choiceButtons = GetPrivateField<List<Button>>(controller, "_choiceButtons");
            TMP_Text countdownText = GetPrivateField<TMP_Text>(controller, "_countdownText");
            TMP_Text transcriptText = GetPrivateField<TMP_Text>(controller, "_transcriptText");
            TMP_Text strategyLabelText = GetPrivateField<TMP_Text>(controller, "_strategyLabelText");

            Assert.IsNotNull(root);
            Assert.IsTrue(root.activeSelf);
            Assert.AreEqual(RenderMode.WorldSpace, root.GetComponent<Canvas>().renderMode);
            Assert.IsNotNull(root.transform.Find("Learning Panel/Learning Keyboard Hint"));
            Assert.IsNull(root.transform.Find("Learning Panel/Learning Navigation Buttons/Previous"));
            Assert.IsNull(root.transform.Find("Learning Panel/Learning Navigation Buttons/Next"));
            Assert.IsNotNull(replayButton);
            Assert.AreEqual("Warm-up", CurrentStage(controller).Title);
            Assert.IsFalse(replayButton.gameObject.activeSelf);
            Assert.IsEmpty(countdownText.text);
            Assert.IsFalse(roundManager.IsRoundRunning);

            PressShortcut(controller, KeyCode.RightArrow);
            Assert.AreEqual("CREEI Reading", CurrentStage(controller).Title);
            Assert.IsFalse(roundManager.IsRoundRunning);

            PressShortcut(controller, KeyCode.RightArrow);
            AssertDemoStage(controller, replayButton, transcriptText, strategyLabelText, "CREEI Dialogue Demo", "Anna Reed", "Mike Carter", string.Empty);
            PressShortcut(controller, KeyCode.DownArrow);
            DebateLearningMetrics metrics = GetPrivateField<DebateLearningMetrics>(controller, "_metrics");
            Assert.AreEqual(1, metrics.RewatchCount);
            PressShortcut(controller, KeyCode.LeftArrow);
            Assert.AreEqual("CREEI Reading", CurrentStage(controller).Title);
            PressShortcut(controller, KeyCode.RightArrow);
            Assert.AreEqual("CREEI Dialogue Demo", CurrentStage(controller).Title);

            PressShortcut(controller, KeyCode.RightArrow);
            Assert.AreEqual("CREEI Structure Study", CurrentStage(controller).Title);

            PressShortcut(controller, KeyCode.RightArrow);
            Assert.AreEqual("Strategy Reading", CurrentStage(controller).Title);

            PressShortcut(controller, KeyCode.RightArrow);
            AssertDemoStage(controller, replayButton, transcriptText, strategyLabelText, "Logos Dialogue Demo", "Anna Reed", "Mike Carter", "Logos");

            PressShortcut(controller, KeyCode.RightArrow);
            AssertDemoStage(controller, replayButton, transcriptText, strategyLabelText, "Ethos Dialogue Demo", "Anna Reed", "Mike Carter", "Ethos");

            PressShortcut(controller, KeyCode.RightArrow);
            AssertDemoStage(controller, replayButton, transcriptText, strategyLabelText, "Pathos Dialogue Demo", "Anna Reed", "Mike Carter", "Pathos");

            PressShortcut(controller, KeyCode.RightArrow);
            Assert.AreEqual("Micro-choice", CurrentStage(controller).Title);

            foreach (Button button in choiceButtons)
            {
                Assert.IsTrue(button.gameObject.activeSelf);
                Assert.IsTrue(button.interactable);
            }

            PressShortcut(controller, KeyCode.LeftArrow);
            Assert.IsFalse(root.activeSelf);
        }

        private static void VerifyRoundAndLog()
        {
            Debug.Log("CODEX_DEBATE_LEARNING_BATCH_VERIFIER_CHECK_ROUND_AND_LOG");
            NpcDebateRoundManager roundManager = UnityEngine.Object.FindAnyObjectByType<NpcDebateRoundManager>();
            Assert.IsNotNull(roundManager);
            Assert.IsTrue(roundManager.IsRoundRunning);

            string logPath = Path.Combine(Application.persistentDataPath, DebateLearningLogger.FileName);
            Assert.IsTrue(File.Exists(logPath), logPath);
            string csv = File.ReadAllText(logPath);
            StringAssert.Contains(_participantId, csv);
            StringAssert.Contains("npc_vs_npc", csv);
            StringAssert.Contains("learning_started", csv);
            StringAssert.Contains("learning_session_id", csv);
            StringAssert.Contains("final_summary", csv);
            StringAssert.Contains("Logos;Ethos;Pathos", csv);
            StringAssert.Contains(",Logos,", csv);
            StringAssert.Contains("CREEI Dialogue Demo", csv);
            StringAssert.Contains("Pathos Dialogue Demo", csv);
        }

        private static void AdvanceThroughStage(NpcDebateLearningPhaseController controller, string expectedStage)
        {
            Assert.AreEqual(expectedStage, CurrentStage(controller).Title);
            InvokePrivate(controller, "Update");
            InvokePrivate(controller, "AdvanceStage");
        }

        private static void PressShortcut(NpcDebateLearningPhaseController controller, KeyCode key)
        {
            InvokePrivate(controller, "HandleKeyboardShortcut", key);
        }

        private static void AssertDemoStage(
            NpcDebateLearningPhaseController controller,
            Button replayButton,
            TMP_Text transcriptText,
            TMP_Text strategyLabelText,
            string expectedStage,
            string expectedFirstSpeaker,
            string expectedSecondSpeaker,
            string expectedStrategy)
        {
            Assert.AreEqual(expectedStage, CurrentStage(controller).Title);
            Assert.IsTrue(replayButton.gameObject.activeSelf);
            Assert.IsTrue(replayButton.interactable);
            StringAssert.Contains(expectedFirstSpeaker, transcriptText.text);
            StringAssert.Contains(expectedSecondSpeaker, transcriptText.text);
            if (!string.IsNullOrEmpty(expectedStrategy))
            {
                StringAssert.Contains(expectedStrategy, strategyLabelText.text);
            }
        }

        private static DebateLearningStageSpec CurrentStage(NpcDebateLearningPhaseController controller)
        {
            int stageIndex = GetPrivateField<int>(controller, "_stageIndex");
            return DebateLearningContent.StageSequence[stageIndex];
        }

        private static T GetPrivateField<T>(object target, string name)
        {
            FieldInfo field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, name);
            return (T)field.GetValue(target);
        }

        private static void SetPrivateField(object target, string name, object value)
        {
            FieldInfo field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, name);
            field.SetValue(target, value);
        }

        private static void InvokePrivate(object target, string name, params object[] args)
        {
            MethodInfo method = target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(method, name);
            method.Invoke(target, args);
        }

        private static void CleanupAndExit(int exitCode)
        {
            SessionState.EraseBool(RunningKey);
            SessionState.EraseString(ParticipantKey);
            EditorApplication.update -= VerifyRuntime;
            EditorApplication.playModeStateChanged -= HandlePlayModeStateChanged;
            EditorApplication.Exit(exitCode);
        }
    }

    public static class DebateLearningStartGateBatchVerifier
    {
        private const string ScenePath = "Assets/Game/Scenes/01Level_NPCVsNPCDebate.unity";
        private const string RunningKey = "Codex.DebateLearningStartGateBatchVerifier.Running";
        private const string ParticipantKey = "Codex.DebateLearningStartGateBatchVerifier.Participant";
        private const string FrameKey = "Codex.DebateLearningStartGateBatchVerifier.Frames";

        [InitializeOnLoadMethod]
        private static void ResumeAfterDomainReload()
        {
            if (!SessionState.GetBool(RunningKey, false))
            {
                return;
            }

            EditorApplication.playModeStateChanged -= HandlePlayModeStateChanged;
            EditorApplication.playModeStateChanged += HandlePlayModeStateChanged;
            if (EditorApplication.isPlaying)
            {
                Application.runInBackground = true;
                EditorApplication.update -= VerifyRuntime;
                EditorApplication.update += VerifyRuntime;
            }
        }

        public static void Run()
        {
            SessionState.SetBool(RunningKey, true);
            SessionState.SetString(ParticipantKey, "START_GATE_" + Guid.NewGuid().ToString("N"));
            SessionState.SetInt(FrameKey, 0);
            EditorApplication.playModeStateChanged -= HandlePlayModeStateChanged;
            EditorApplication.playModeStateChanged += HandlePlayModeStateChanged;
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            EditorApplication.EnterPlaymode();
        }

        private static void HandlePlayModeStateChanged(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.EnteredPlayMode)
            {
                return;
            }

            Application.runInBackground = true;
            EditorApplication.update -= VerifyRuntime;
            EditorApplication.update += VerifyRuntime;
        }

        private static void VerifyRuntime()
        {
            int frames = SessionState.GetInt(FrameKey, 0) + 1;
            SessionState.SetInt(FrameKey, frames);
            if (frames < 5)
            {
                return;
            }

            try
            {
                NpcDebateLearningPhaseController controller =
                    UnityEngine.Object.FindAnyObjectByType<NpcDebateLearningPhaseController>();
                NpcDebateRoundManager roundManager =
                    UnityEngine.Object.FindAnyObjectByType<NpcDebateRoundManager>();
                Assert.IsNotNull(controller);
                Assert.IsNotNull(roundManager);

                string participant = SessionState.GetString(ParticipantKey, string.Empty);
                SetPrivateField(controller, "participantId", participant);
                SetPrivateField(controller, "playNpcVoice", false);

                Assert.IsFalse(GetPrivateField<bool>(controller, "_started"));
                Assert.IsFalse(roundManager.IsRoundRunning);
                GameObject root = GetPrivateField<GameObject>(controller, "_root");
                GameObject learningPanel = GetPrivateField<GameObject>(controller, "_learningPanel");
                GameObject startGate = GetPrivateField<GameObject>(controller, "_startGate");
                Button startButton = GetPrivateField<Button>(controller, "_startLearningButton");
                Assert.IsTrue(root.activeSelf);
                Assert.IsFalse(learningPanel.activeSelf);
                Assert.IsTrue(startGate.activeSelf);
                Assert.AreEqual("Start Debate Learning", startButton.GetComponentInChildren<TMP_Text>().text);

                startButton.onClick.Invoke();

                string sessionId = GetPrivateField<string>(controller, "_learningSessionId");
                Assert.IsTrue(GetPrivateField<bool>(controller, "_started"));
                StringAssert.StartsWith("DL-", sessionId);
                Assert.IsTrue(learningPanel.activeSelf);
                Assert.IsFalse(startGate.activeSelf);
                Assert.AreEqual("Warm-up", GetPrivateField<TMP_Text>(controller, "_titleText").text);
                Assert.IsFalse(roundManager.IsRoundRunning);

                string logPath = Path.Combine(Application.persistentDataPath, DebateLearningLogger.FileName);
                string csv = File.ReadAllText(logPath);
                StringAssert.Contains("learning_session_id", csv);
                StringAssert.Contains("learning_started", csv);
                StringAssert.Contains(participant, csv);
                StringAssert.Contains(sessionId, csv);

                Debug.Log("CODEX_DEBATE_START_GATE_BATCH_VERIFIER_PASS " + sessionId);
                CleanupAndExit(0);
            }
            catch (Exception exception)
            {
                Debug.LogError("CODEX_DEBATE_START_GATE_BATCH_VERIFIER_FAIL " + exception);
                CleanupAndExit(1);
            }
        }

        private static T GetPrivateField<T>(object target, string name)
        {
            FieldInfo field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, name);
            return (T)field.GetValue(target);
        }

        private static void SetPrivateField(object target, string name, object value)
        {
            FieldInfo field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, name);
            field.SetValue(target, value);
        }

        private static void InvokePrivate(object target, string name)
        {
            MethodInfo method = target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(method, name);
            method.Invoke(target, null);
        }

        private static void CleanupAndExit(int exitCode)
        {
            SessionState.EraseBool(RunningKey);
            SessionState.EraseString(ParticipantKey);
            SessionState.EraseInt(FrameKey);
            EditorApplication.update -= VerifyRuntime;
            EditorApplication.playModeStateChanged -= HandlePlayModeStateChanged;
            EditorApplication.Exit(exitCode);
        }
    }
}
