using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Convai.Scripts.Runtime.Addons;
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
                Assert.IsNotNull(controller);
                Assert.IsNull(UnityEngine.Object.FindAnyObjectByType<NpcDebateRoundManager>());

                string participant = SessionState.GetString(ParticipantKey, string.Empty);
                SetPrivateField(controller, "playNpcVoice", false);

                Assert.IsFalse(GetPrivateField<bool>(controller, "_started"));
                GameObject root = GetPrivateField<GameObject>(controller, "_root");
                GameObject learningPanel = GetPrivateField<GameObject>(controller, "_learningPanel");
                GameObject startGate = GetPrivateField<GameObject>(controller, "_startGate");
                GameObject onboarding = GetPrivateField<GameObject>(controller, "_participantOnboardingGate");
                GameObject researchCanvas = GetPrivateField<GameObject>(controller, "_researchSetupCanvas");
                Assert.IsFalse(root.activeSelf);
                Assert.IsFalse(learningPanel.activeInHierarchy);
                Assert.IsTrue(researchCanvas.activeSelf);
                Assert.IsTrue(onboarding.activeSelf);
                Assert.IsFalse(startGate.activeSelf);
                Assert.AreEqual(CursorLockMode.None, Cursor.lockState);
                Assert.IsTrue(Cursor.visible);

                InvokePrivate(controller, "ShowResearchSetupFromOnboarding");
                Assert.IsFalse(onboarding.activeSelf);
                Assert.IsTrue(startGate.activeSelf);

                TMP_InputField participantInput =
                    GetPrivateField<TMP_InputField>(controller, "_researchParticipantInput");
                participantInput.SetTextWithoutNotify(participant);
                SetPrivateField(controller, "_selectedResearchCondition",
                    CoachOrchestrationMode.SharedControl);

                InvokePrivate(controller, "BeginLearningFromStartGate");

                string sessionId = GetPrivateField<string>(controller, "_learningSessionId");
                Assert.IsTrue(GetPrivateField<bool>(controller, "_started"));
                StringAssert.StartsWith("DL-", sessionId);
                Assert.IsTrue(root.activeSelf);
                Assert.IsTrue(learningPanel.activeSelf);
                Assert.IsFalse(researchCanvas.activeSelf);
                Assert.AreEqual("Warm-up", GetPrivateField<TMP_Text>(controller, "_titleText").text);
                if (!Application.isBatchMode)
                {
                    Assert.AreEqual(CursorLockMode.Locked, Cursor.lockState);
                    Assert.IsFalse(Cursor.visible);
                }

                GraphicRaycaster learningRaycaster =
                    GetPrivateField<GraphicRaycaster>(controller, "_learningGraphicRaycaster");
                Assert.IsFalse(learningRaycaster.enabled);
                ConvaiPlayerMovement movement =
                    UnityEngine.Object.FindAnyObjectByType<ConvaiPlayerMovement>();
                Assert.IsNotNull(movement);
                Assert.IsTrue(movement.enabled);

                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
                InvokePrivate(controller, "UpdateLearningRaycastMode");
                Assert.IsTrue(learningRaycaster.enabled);

                Assert.IsNull(GameObject.Find("Debate Round UI"));
                Assert.IsNull(GameObject.Find("Start Debate Button"));
                Assert.IsNull(GameObject.Find("Round Timer"));

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
