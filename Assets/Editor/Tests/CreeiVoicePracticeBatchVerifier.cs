using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Convai.Scripts.Runtime.Core;
using Game.Debate;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Game.Tests.EditMode
{
    public static class CreeiVoicePracticeBatchVerifier
    {
        private const string ScenePath = "Assets/Game/Scenes/01Level_NPCVsNPCDebate.unity";
        private const string RunningKey = "Codex.CreeiVoicePracticeBatchVerifier.Running";

        [InitializeOnLoadMethod]
        private static void ResumeAfterDomainReload()
        {
            if (SessionState.GetBool(RunningKey, false) && EditorApplication.isPlaying)
            {
                EditorApplication.update -= Verify;
                EditorApplication.update += Verify;
            }
        }

        public static void Run()
        {
            SessionState.SetBool(RunningKey, true);
            EditorApplication.update -= Verify;
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            EditorApplication.EnterPlaymode();
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredPlayMode)
            {
                EditorApplication.update -= Verify;
                EditorApplication.update += Verify;
            }
        }

        private static void Verify()
        {
            try
            {
                NpcDebateLearningPhaseController controller = UnityEngine.Object.FindAnyObjectByType<NpcDebateLearningPhaseController>();
                Assert.IsNotNull(controller, "The learning controller must exist in the NPC-vs-NPC scene.");
                SetPrivateField(controller, "playNpcVoice", false);
                InvokePrivate(controller, "BeginLearningFromStartGate");
                AdvanceToStage(controller, "Micro Practice 2: Build Your CREEI Argument");

                GameObject root = GetPrivateField<GameObject>(controller, "_root");
                TMP_Text statusText = GetPrivateField<TMP_Text>(controller, "_voicePracticeStatusText");
                Assert.IsNotNull(root.transform.Find("Learning Panel/Learning Keyboard Hint"));
                Assert.IsNull(root.transform.Find("Learning Panel/Learning Navigation Buttons/Previous"));
                Assert.IsNull(root.transform.Find("Learning Panel/Learning Navigation Buttons/Next"));
                StringAssert.Contains("Press T to start speaking.", statusText.text);
                Assert.AreEqual(controller, ConvaiGRPCAPI.TryHandleUserVoiceTranscript?.Target);

                int initialStep = GetPrivateField<int>(controller, "_voicePracticeStepIndex");
                PressRight(controller);
                Assert.AreEqual(initialStep, GetPrivateField<int>(controller, "_voicePracticeStepIndex"),
                    "A CREEI part cannot be confirmed before Convai returns text.");

                SetPrivateField(controller, "_voicePracticeRecording", true);
                InvokePrivate(controller, "RenderVoicePractice");
                InvokePrivate(controller, "UpdateVoicePracticeControls");
                StringAssert.Contains("Listening... Press T to stop.", statusText.text);
                InvokePrivate(controller, "PreviousStage");
                Assert.AreEqual("Micro Practice 2: Build Your CREEI Argument", CurrentStage(controller).Title);
                SetPrivateField(controller, "_voicePracticeRecording", false);
                SetPrivateField(controller, "_voicePracticeAwaitingTranscript", true);
                InvokePrivate(controller, "RenderVoicePractice");
                InvokePrivate(controller, "UpdateVoicePracticeControls");
                StringAssert.Contains("Transcribing...", statusText.text);
                InvokePrivate(controller, "PreviousStage");
                Assert.AreEqual("Micro Practice 2: Build Your CREEI Argument", CurrentStage(controller).Title);
                SetPrivateField(controller, "_voicePracticeAwaitingTranscript", false);

                InjectFinalTranscript(controller, "Claim first version.");
                InvokePrivate(controller, "PrepareVoicePracticeRerecord");
                InjectFinalTranscript(controller, "Claim replacement.");
                StringAssert.Contains("Claim replacement.", GetPrivateField<TMP_Text>(controller, "_voicePracticeTranscriptText").text);
                Assert.AreEqual(1, GetPrivateField<DebateLearningMetrics>(controller, "_metrics").MicroPractice2RerecordCount);
                Assert.AreEqual(1, GetPrivateField<int[]>(controller, "_voicePracticeRerecordCounts")[0]);
                PressRight(controller);

                foreach (string transcript in new[]
                {
                    "Reason final.",
                    "Evidence final.",
                    "Explanation final.",
                    "Impact final."
                })
                {
                    InjectFinalTranscript(controller, transcript);
                    PressRight(controller);
                }

                Assert.AreEqual("Strategy Reading", CurrentStage(controller).Title);
                Assert.AreEqual(controller, ConvaiGRPCAPI.TryHandleUserVoiceTranscript?.Target);
                Assert.AreEqual(controller, ConvaiInputManager.ShouldSuppressTalkInput?.Target);
                Assert.AreEqual(controller, ConvaiPlayerInteractionManager.ShouldSuppressTalkInput?.Target);
                Assert.AreEqual(controller, ConvaiPlayerInteractionManager.ShouldSuppressNpcInteraction?.Target);
                string csvPath = Path.Combine(Application.persistentDataPath, DebateLearningLogger.FileName);
                Assert.IsTrue(File.Exists(csvPath), csvPath);
                string csv = File.ReadAllText(csvPath);
                StringAssert.Contains("micro_practice_2_claim", csv);
                StringAssert.Contains("Claim replacement.", csv);
                StringAssert.Contains("Reason final.", csv);
                StringAssert.Contains("Evidence final.", csv);
                StringAssert.Contains("Explanation final.", csv);
                StringAssert.Contains("Impact final.", csv);
                StringAssert.Contains("true,1", csv);

                Assert.IsNull(UnityEngine.Object.FindAnyObjectByType<NpcDebateRoundManager>());
                for (int remaining = DebateLearningContent.StageSequence.Length; remaining > 0; remaining--)
                {
                    if (controller == null || !controller)
                    {
                        break;
                    }

                    GameObject learningRoot = GetPrivateField<GameObject>(controller, "_root");
                    if (learningRoot == null || !learningRoot.activeSelf) break;

                    InvokePrivate(controller, "AdvanceStage");
                }

                StringAssert.StartsWith("03Level_PlayerVsNPCDebate",
                    SceneManager.GetActiveScene().name);
                Assert.AreNotSame(controller, ConvaiGRPCAPI.TryHandleUserVoiceTranscript?.Target);
                Assert.AreNotSame(controller, ConvaiInputManager.ShouldSuppressTalkInput?.Target);
                Assert.AreNotSame(controller, ConvaiPlayerInteractionManager.ShouldSuppressTalkInput?.Target);
                Assert.AreNotSame(controller, ConvaiPlayerInteractionManager.ShouldSuppressNpcInteraction?.Target);
                Assert.AreNotSame(controller, ConvaiNPCManager.ShouldSuppressAutoActiveNPCUpdate?.Target);
                Debug.Log("CODEX_CREEI_VOICE_PRACTICE_BATCH_VERIFIER_PASS");
                Cleanup();
            }
            catch (Exception exception)
            {
                Debug.LogError("CODEX_CREEI_VOICE_PRACTICE_BATCH_VERIFIER_FAIL " + exception);
                Cleanup();
            }
        }

        private static void AdvanceToStage(NpcDebateLearningPhaseController controller, string title)
        {
            for (int remaining = DebateLearningContent.StageSequence.Length; remaining > 0; remaining--)
            {
                if (CurrentStage(controller).Title == title)
                {
                    return;
                }

                InvokePrivate(controller, "AdvanceStage");
            }

            Assert.Fail("Stage was not reached: " + title);
        }

        private static void InjectFinalTranscript(NpcDebateLearningPhaseController controller, string transcript)
        {
            SetPrivateField(controller, "_voicePracticeAwaitingTranscript", true);
            MethodInfo callback = controller.GetType().GetMethod("TryHandleVoicePracticeTranscript", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(callback, "The Convai transcript callback is required.");
            bool handled = (bool)callback.Invoke(controller, new object[] { transcript });
            Assert.IsTrue(handled,
                "Callback rejected transcript at stage " + CurrentStage(controller).Key +
                "; awaiting=" + GetPrivateField<bool>(controller, "_voicePracticeAwaitingTranscript"));
        }

        private static void PressRight(NpcDebateLearningPhaseController controller)
        {
            InvokePrivate(controller, "HandleKeyboardShortcut", KeyCode.RightArrow);
        }

        private static DebateLearningStageSpec CurrentStage(NpcDebateLearningPhaseController controller)
        {
            return DebateLearningContent.StageSequence[GetPrivateField<int>(controller, "_stageIndex")];
        }

        private static T GetPrivateField<T>(object target, string name)
        {
            FieldInfo field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, "Missing private field: " + name);
            return (T)field.GetValue(target);
        }

        private static void SetPrivateField(object target, string name, object value)
        {
            FieldInfo field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, "Missing private field: " + name);
            field.SetValue(target, value);
        }

        private static void InvokePrivate(object target, string name, params object[] args)
        {
            MethodInfo method = target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(method, "Missing private method: " + name);
            method.Invoke(target, args);
        }

        private static void Cleanup()
        {
            SessionState.EraseBool(RunningKey);
            EditorApplication.update -= Verify;
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            if (EditorApplication.isPlaying)
            {
                EditorApplication.isPlaying = false;
            }
        }
    }
}
