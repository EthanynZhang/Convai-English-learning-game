using System;
using System.Linq;
using System.Reflection;
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
    public class DebatePreparationControllerTests
    {
        private const string TargetScenePath = "Assets/Game/Scenes/03Level_PlayerVsNPCDebate.unity";

        [Test]
        public void CountdownFormattingRoundsUpAndClampsAtZero()
        {
            Assert.AreEqual("03:00", DebatePreparationController.FormatCountdown(180f));
            Assert.AreEqual("01:00", DebatePreparationController.FormatCountdown(60f));
            Assert.AreEqual("00:01", DebatePreparationController.FormatCountdown(0.01f));
            Assert.AreEqual("00:00", DebatePreparationController.FormatCountdown(0f));
            Assert.AreEqual("00:00", DebatePreparationController.FormatCountdown(-5f));
        }

        [Test]
        public void OralPracticeUsesThreeMinuteCountdownAndOnlyTogglesWhileActive()
        {
            Type oralPracticeType = typeof(DebatePreparationController).Assembly.GetType(
                "Game.Debate.PlayerOralPracticeController");
            Assert.IsNotNull(
                oralPracticeType,
                "Scene 03 needs a dedicated player oral-practice controller after the referee opening.");

            MethodInfo formatCountdown = oralPracticeType.GetMethod(
                "FormatPracticeCountdown",
                BindingFlags.Public | BindingFlags.Static);
            MethodInfo canToggleRecording = oralPracticeType.GetMethod(
                "CanToggleRecordingFromKeyboard",
                BindingFlags.Public | BindingFlags.Static);

            Assert.IsNotNull(formatCountdown);
            Assert.IsNotNull(canToggleRecording);
            Assert.AreEqual("03:00", (string)formatCountdown.Invoke(null, new object[] { 180f }));
            Assert.AreEqual("00:00", (string)formatCountdown.Invoke(null, new object[] { -1f }));
            Assert.IsTrue((bool)canToggleRecording.Invoke(null, new object[] { true, false }));
            Assert.IsFalse((bool)canToggleRecording.Invoke(null, new object[] { false, false }));
            Assert.IsFalse((bool)canToggleRecording.Invoke(null, new object[] { true, true }));
        }

        [Test]
        public void OralPracticeTranscriptEntriesRemainReadableAndSeparate()
        {
            Type oralPracticeType = typeof(DebatePreparationController).Assembly.GetType(
                "Game.Debate.PlayerOralPracticeController");
            Assert.IsNotNull(oralPracticeType);

            MethodInfo appendTranscript = oralPracticeType.GetMethod(
                "AppendTranscriptEntry",
                BindingFlags.Public | BindingFlags.Static);
            Assert.IsNotNull(appendTranscript);
            Assert.AreEqual(
                "First argument\n\u2022 Second argument",
                (string)appendTranscript.Invoke(null, new object[] { "First argument", " Second argument " }));
            Assert.AreEqual(
                "First argument",
                (string)appendTranscript.Invoke(null, new object[] { "First argument", "  " }));
        }

        [Test]
        public void OralPracticeUsesACompactTopLeftPanelWithoutArgumentTranscriptArea()
        {
            GameObject controllerObject = new("Oral Practice Controller Test");
            GameObject practiceRoot = null;
            try
            {
                PlayerOralPracticeController controller =
                    controllerObject.AddComponent<PlayerOralPracticeController>();
                MethodInfo buildUi = typeof(PlayerOralPracticeController).GetMethod(
                    "BuildPracticeUi",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.IsNotNull(buildUi);
                buildUi.Invoke(controller, null);

                FieldInfo rootField = typeof(PlayerOralPracticeController).GetField(
                    "_practiceRoot",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.IsNotNull(rootField);
                practiceRoot = (GameObject)rootField.GetValue(controller);
                Assert.IsNotNull(practiceRoot);

                RectTransform panel = practiceRoot.transform.Find("Practice Panel")
                    .GetComponent<RectTransform>();
                Assert.AreEqual(new Vector2(0f, 1f), panel.anchorMin);
                Assert.AreEqual(new Vector2(0f, 1f), panel.anchorMax);
                Assert.AreEqual(new Vector2(0f, 1f), panel.pivot);
                Assert.LessOrEqual(panel.sizeDelta.x, 700f);
                Assert.LessOrEqual(panel.sizeDelta.y, 420f);
                Assert.IsFalse(panel.GetComponentsInChildren<TMP_Text>(true)
                    .Any(text => string.Equals(text.text, "Your argument", StringComparison.Ordinal)));
                Assert.IsNull(panel.Find("Transcript Scroll"));
                Assert.AreEqual(
                    5,
                    panel.GetComponentsInChildren<TMP_Text>(true).Length,
                    "The compact panel should add only the completion button label, not a live transcript area.");
                Assert.IsNotNull(panel.GetComponentsInChildren<Button>(true)
                    .SingleOrDefault(button => button.gameObject.name.Contains("Continue")));
            }
            finally
            {
                if (practiceRoot != null)
                {
                    UnityEngine.Object.DestroyImmediate(practiceRoot);
                }

                UnityEngine.Object.DestroyImmediate(controllerObject);
            }
        }

        [Test]
        public void TargetSceneUsesAnEnlargedPreparationNotesPanel()
        {
            Scene existingScene = SceneManager.GetSceneByPath(TargetScenePath);
            bool openedHere = !existingScene.IsValid() || !existingScene.isLoaded;
            Scene scene = openedHere
                ? EditorSceneManager.OpenScene(TargetScenePath, OpenSceneMode.Additive)
                : existingScene;

            try
            {
                RectTransform panel = scene.GetRootGameObjects()
                    .SelectMany(root => root.GetComponentsInChildren<RectTransform>(true))
                    .Single(rect => rect.gameObject.name == "Debate Notes Panel");
                Assert.GreaterOrEqual(panel.sizeDelta.x, 1050f);
                Assert.GreaterOrEqual(panel.sizeDelta.y, 300f);
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
        public void RoundManagerUsesGeneratedRefereeSpeechWhenNoClipIsAssigned()
        {
            MethodInfo method = typeof(DebateRoundManager).GetMethod(
                "ShouldUseMiniMaxRefereeSpeech",
                BindingFlags.Public | BindingFlags.Static);

            Assert.IsNotNull(method, "The round manager must expose its MiniMax fallback decision.");
            Assert.IsTrue((bool)method.Invoke(null, new object[] { true, null, "Opening line" }));
            Assert.IsFalse((bool)method.Invoke(null, new object[] { false, null, "Opening line" }));
        }

        [Test]
        public void DefaultRefereeOpeningOnlyIntroducesTheTopic()
        {
            GameObject gameObject = new("Round Manager Test");
            try
            {
                DebateRoundManager manager = gameObject.AddComponent<DebateRoundManager>();
                FieldInfo openingLineField = typeof(DebateRoundManager).GetField(
                    "refereeOpeningLine",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.IsNotNull(openingLineField);
                string openingLine = (string)openingLineField.GetValue(manager);

                StringAssert.DoesNotContain("Berance", openingLine);
                StringAssert.DoesNotContain("debate begins", openingLine.ToLowerInvariant());
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void PracticeOnlyRoundNeverRequestsOpponentOpening()
        {
            MethodInfo method = typeof(DebateRoundManager).GetMethod(
                "ShouldSendOpponentOpeningPrompt",
                BindingFlags.Public | BindingFlags.Static);

            Assert.IsNotNull(method, "The round manager must expose the opponent-opening decision.");
            Assert.IsFalse((bool)method.Invoke(null, new object[] { true, true, true }));
            Assert.IsTrue((bool)method.Invoke(null, new object[] { true, true, false }));
        }

        [Test]
        public void RoundTimerGateWaitsForPlayerSpeechOnlyWhenConfigured()
        {
            MethodInfo gate = typeof(DebateRoundManager).GetMethod(
                "ShouldAdvanceRoundTimer",
                BindingFlags.Public | BindingFlags.Static);
            Assert.IsNotNull(gate);
            Assert.IsTrue((bool)gate.Invoke(null, new object[] { false, false }));
            Assert.IsFalse((bool)gate.Invoke(null, new object[] { true, false }));
            Assert.IsTrue((bool)gate.Invoke(null, new object[] { true, true }));
        }

        [Test]
        public void OralTranscriptionStartNotifiesTheRoundTimerGate()
        {
            GameObject managerObject = new("Round Manager Test");
            GameObject oralObject = new("Oral Practice Test");
            GameObject practiceRoot = null;
            try
            {
                DebateRoundManager manager = managerObject.AddComponent<DebateRoundManager>();
                PlayerOralPracticeController oral =
                    oralObject.AddComponent<PlayerOralPracticeController>();
                SetPrivateField(oral, "_roundManager", manager);

                PropertyInfo speechStarted = typeof(DebateRoundManager).GetProperty(
                    "HasPlayerSpeechStarted",
                    BindingFlags.Public | BindingFlags.Instance);
                Assert.IsNotNull(speechStarted);
                Assert.IsFalse((bool)speechStarted.GetValue(manager));

                InvokePrivate(oral, "HandleTranscriptionStarted");
                Assert.IsTrue((bool)speechStarted.GetValue(manager));

                FieldInfo rootField = typeof(PlayerOralPracticeController).GetField(
                    "_practiceRoot",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                practiceRoot = rootField?.GetValue(oral) as GameObject;
            }
            finally
            {
                if (practiceRoot != null)
                {
                    UnityEngine.Object.DestroyImmediate(practiceRoot);
                }

                UnityEngine.Object.DestroyImmediate(managerObject);
                UnityEngine.Object.DestroyImmediate(oralObject);
            }
        }

        [Test]
        public void OralPracticeCountdownDoesNotAdvanceBeforePlayerSpeechStarts()
        {
            GameObject managerObject = new("Round Manager Test");
            GameObject oralObject = new("Oral Practice Test");
            GameObject practiceRoot = null;
            try
            {
                DebateRoundManager manager = managerObject.AddComponent<DebateRoundManager>();
                SetPrivateField(manager, "waitForPlayerSpeechBeforeTimer", true);
                PlayerOralPracticeController oral =
                    oralObject.AddComponent<PlayerOralPracticeController>();
                SetPrivateField(oral, "_roundManager", manager);
                oral.BeginPractice();

                MethodInfo advance = typeof(PlayerOralPracticeController).GetMethod(
                    "AdvancePractice",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.IsNotNull(advance);
                advance.Invoke(oral, new object[] { 1f });
                Assert.AreEqual(180f, oral.RemainingSeconds);

                manager.NotifyPlayerSpeechStarted();
                advance.Invoke(oral, new object[] { 1f });
                Assert.AreEqual(179f, oral.RemainingSeconds);

                FieldInfo rootField = typeof(PlayerOralPracticeController).GetField(
                    "_practiceRoot",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                practiceRoot = rootField?.GetValue(oral) as GameObject;
            }
            finally
            {
                if (practiceRoot != null)
                {
                    UnityEngine.Object.DestroyImmediate(practiceRoot);
                }

                UnityEngine.Object.DestroyImmediate(managerObject);
                UnityEngine.Object.DestroyImmediate(oralObject);
            }
        }

        [Test]
        public void PlayerPracticeCanTemporarilySuppressNormalConvaiInput()
        {
            GameObject managerObject = new("Round Manager Test");
            try
            {
                DebateRoundManager manager = managerObject.AddComponent<DebateRoundManager>();
                MethodInfo setSuppressed = typeof(DebateRoundManager).GetMethod(
                    "SetPlayerPracticeInputSuppressed",
                    BindingFlags.Public | BindingFlags.Instance);
                MethodInfo shouldSuppress = typeof(DebateRoundManager).GetMethod(
                    "ShouldSuppressOpponentConversation",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                Assert.IsNotNull(setSuppressed);
                Assert.IsNotNull(shouldSuppress);

                setSuppressed.Invoke(manager, new object[] { true });
                Assert.IsTrue((bool)shouldSuppress.Invoke(manager, null));
                setSuppressed.Invoke(manager, new object[] { false });
                Assert.IsFalse((bool)shouldSuppress.Invoke(manager, null));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(managerObject);
            }
        }

        [Test]
        public void RuntimePreparationUiProvidesNotesAndNKeyAccess()
        {
            GameObject controllerObject = new("Runtime Preparation Test");
            GameObject runtimeCanvas = null;
            GameObject practiceRoot = null;
            try
            {
                controllerObject.AddComponent<XfyunRealtimeTranscriber>();
                DebatePreparationController controller =
                    controllerObject.AddComponent<DebatePreparationController>();
                MethodInfo build = typeof(DebatePreparationController).GetMethod(
                    "BuildRuntimeUiIfNeeded",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.IsNotNull(build);
                build.Invoke(controller, null);

                SerializedObject serialized = new(controller);
                Assert.IsNotNull(serialized.FindProperty("preparationRoot").objectReferenceValue);
                Assert.IsNotNull(serialized.FindProperty("notesInput").objectReferenceValue);
                Assert.IsNotNull(serialized.FindProperty("skipPreparationButton").objectReferenceValue);
                Assert.IsNotNull(serialized.FindProperty("debateNotesPanel").objectReferenceValue);
                Assert.IsNotNull(serialized.FindProperty("debateNotesText").objectReferenceValue);

                GameObject notesTab = (GameObject)serialized.FindProperty("debateNotesTab")
                    .objectReferenceValue;
                StringAssert.Contains(
                    "N",
                    notesTab.GetComponentInChildren<TMP_Text>(true).text);
                TMP_Text notesText = (TMP_Text)serialized.FindProperty("debateNotesText")
                    .objectReferenceValue;
                Assert.IsNotNull(notesText.transform.parent.GetComponent<UnityEngine.UI.VerticalLayoutGroup>());

                FieldInfo canvasField = typeof(DebatePreparationController).GetField(
                    "_runtimeUiCanvas",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                runtimeCanvas = canvasField?.GetValue(controller) as GameObject;
                PlayerOralPracticeController oral =
                    controllerObject.GetComponent<PlayerOralPracticeController>();
                if (oral != null)
                {
                    FieldInfo practiceRootField = typeof(PlayerOralPracticeController).GetField(
                        "_practiceRoot",
                        BindingFlags.Instance | BindingFlags.NonPublic);
                    practiceRoot = practiceRootField?.GetValue(oral) as GameObject;
                }
            }
            finally
            {
                if (runtimeCanvas != null)
                {
                    UnityEngine.Object.DestroyImmediate(runtimeCanvas);
                }

                if (practiceRoot != null)
                {
                    UnityEngine.Object.DestroyImmediate(practiceRoot);
                }

                UnityEngine.Object.DestroyImmediate(controllerObject);
            }
        }

        [Test]
        public void PreparationStartsWithThreeMinutes()
        {
            GameObject gameObject = new("Preparation Controller Test");
            try
            {
                DebatePreparationController controller =
                    gameObject.AddComponent<DebatePreparationController>();

                controller.BeginPreparation();

                Assert.AreEqual(180f, controller.RemainingSeconds);
                Assert.AreEqual("03:00", DebatePreparationController.FormatCountdown(controller.RemainingSeconds));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void VoiceEntriesAppendWithoutReplacingTypedNotes()
        {
            Assert.AreEqual(
                "Claim: speaking builds fluency.\n\u2022 Reading supplies useful evidence.",
                DebatePreparationController.AppendVoiceEntry(
                    "Claim: speaking builds fluency.",
                    "Reading supplies useful evidence."));

            Assert.AreEqual(
                "\u2022 Start with a clear claim.",
                DebatePreparationController.AppendVoiceEntry(string.Empty, " Start with a clear claim. "));
        }

        [Test]
        public void SameVoiceCaptureCanOnlyBeCommittedOnce()
        {
            GameObject gameObject = new("Preparation Controller Test");
            try
            {
                DebatePreparationController controller = gameObject.AddComponent<DebatePreparationController>();
                SetPrivateField(controller, "_notesText", "Typed note");
                SetPrivateField(controller, "_voiceEntryCommitted", false);

                InvokePrivate(controller, "CommitCurrentVoiceEntry", "Voice note");
                InvokePrivate(controller, "CommitCurrentVoiceEntry", "Voice note");

                Assert.AreEqual("Typed note\n\u2022 Voice note", controller.NotesText);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void VoiceHotkeyIsIgnoredWhileTextInputHasFocusOrTranscriptIsFinalizing()
        {
            Assert.IsTrue(DebatePreparationController.CanToggleVoiceFromKeyboard(true, false, false));
            Assert.IsFalse(DebatePreparationController.CanToggleVoiceFromKeyboard(true, true, false));
            Assert.IsFalse(DebatePreparationController.CanToggleVoiceFromKeyboard(true, false, true));
            Assert.IsFalse(DebatePreparationController.CanToggleVoiceFromKeyboard(false, false, false));
        }

        [Test]
        public void PreparationCompletionIsRaisedOnlyOnce()
        {
            GameObject gameObject = new("Preparation Controller Test");
            try
            {
                DebatePreparationController controller = gameObject.AddComponent<DebatePreparationController>();
                int completedCount = 0;
                controller.PreparationCompleted += () => completedCount++;

                controller.BeginPreparation();
                InvokePrivate(controller, "AdvancePreparation", 180f);
                InvokePrivate(controller, "AdvancePreparation", 180f);

                Assert.IsFalse(controller.IsPreparing);
                Assert.AreEqual(0f, controller.RemainingSeconds);
                Assert.AreEqual(1, completedCount);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void SkipPreparationCompletesImmediatelyAndOnlyOnce()
        {
            GameObject gameObject = new("Preparation Controller Test");
            try
            {
                DebatePreparationController controller = gameObject.AddComponent<DebatePreparationController>();
                int completedCount = 0;
                controller.PreparationCompleted += () => completedCount++;

                controller.BeginPreparation();
                MethodInfo skipMethod = typeof(DebatePreparationController).GetMethod(
                    "SkipPreparation",
                    BindingFlags.Instance | BindingFlags.Public);

                Assert.IsNotNull(skipMethod, "The preparation controller must expose SkipPreparation for UI wiring.");
                skipMethod.Invoke(controller, null);
                skipMethod.Invoke(controller, null);

                Assert.IsFalse(controller.IsPreparing);
                Assert.AreEqual(0f, controller.RemainingSeconds);
                Assert.AreEqual(1, completedCount);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void PlayerControlsRestoreTheirOriginalEnabledStates()
        {
            GameObject gameObject = new("Preparation Controller Test");
            GameObject enabledControlObject = new("Enabled Control");
            GameObject disabledControlObject = new("Disabled Control");
            try
            {
                DebatePreparationController controller = gameObject.AddComponent<DebatePreparationController>();
                Camera enabledControl = enabledControlObject.AddComponent<Camera>();
                AudioListener disabledControl = disabledControlObject.AddComponent<AudioListener>();
                enabledControl.enabled = true;
                disabledControl.enabled = false;
                SetPrivateField(
                    controller,
                    "controlsToDisableDuringPreparation",
                    new Behaviour[] { enabledControl, disabledControl });

                controller.BeginPreparation();
                Assert.IsFalse(enabledControl.enabled);
                Assert.IsFalse(disabledControl.enabled);

                InvokePrivate(controller, "CompletePreparation");
                Assert.IsTrue(enabledControl.enabled);
                Assert.IsFalse(disabledControl.enabled);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(gameObject);
                UnityEngine.Object.DestroyImmediate(enabledControlObject);
                UnityEngine.Object.DestroyImmediate(disabledControlObject);
            }
        }

        [Test]
        public void TimeoutUsesNewestTranscriberSnapshotBeforeQueuedUiCallbackRuns()
        {
            GameObject gameObject = new("Preparation Controller Test");
            try
            {
                XfyunRealtimeTranscriber transcriber =
                    gameObject.AddComponent<XfyunRealtimeTranscriber>();
                DebatePreparationController controller =
                    gameObject.AddComponent<DebatePreparationController>();
                SetPrivateField(controller, "realtimeTranscriber", transcriber);

                controller.BeginPreparation();
                SetPrivateField(transcriber, "_latestTranscript", "Newest queued evidence");
                SetPrivateField(controller, "_latestVoiceTranscript", "Older visible evidence");
                SetPrivateField(controller, "_voiceSessionActive", true);
                SetPrivateField(controller, "_voiceEntryCommitted", false);

                InvokePrivate(controller, "AdvancePreparation", 180f);

                Assert.AreEqual("Newest queued evidence", transcriber.LatestTranscriptSnapshot);
                Assert.AreEqual("\u2022 Newest queued evidence", controller.NotesText);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void RoundEndHidesNotesTabAndExpandedPanel()
        {
            GameObject controllerObject = new("Preparation Controller Test");
            GameObject roundObject = new("Round Manager Test");
            GameObject tab = new("Notes Tab");
            GameObject panel = new("Notes Panel");
            try
            {
                DebatePreparationController controller =
                    controllerObject.AddComponent<DebatePreparationController>();
                DebateRoundManager roundManager = roundObject.AddComponent<DebateRoundManager>();
                SetPrivateField(controller, "roundManager", roundManager);
                SetPrivateField(controller, "debateNotesTab", tab);
                SetPrivateField(controller, "debateNotesPanel", panel);
                SetPrivateField(controller, "_hasCompletedPreparation", true);
                SetPrivateField(roundManager, "<HasRoundEnded>k__BackingField", true);
                tab.SetActive(true);
                panel.SetActive(true);

                InvokePrivate(controller, "UpdateDebateNotesAvailability");

                Assert.IsFalse(tab.activeSelf);
                Assert.IsFalse(panel.activeSelf);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(controllerObject);
                UnityEngine.Object.DestroyImmediate(roundObject);
                UnityEngine.Object.DestroyImmediate(tab);
                UnityEngine.Object.DestroyImmediate(panel);
            }
        }

        [Test]
        public void SuspendedPreparationHidesOverlayAndResumeRecapturesControls()
        {
            GameObject controllerObject = new("Preparation Controller Test");
            GameObject overlay = new("Preparation Overlay");
            GameObject controlObject = new("Player Control");
            GameObject previewObject = new("Voice Preview");
            GameObject statusObject = new("Voice Status");
            GameObject buttonObject = new("Microphone Button");
            GameObject buttonLabelObject = new("Microphone Button Label");
            try
            {
                DebatePreparationController controller =
                    controllerObject.AddComponent<DebatePreparationController>();
                Camera control = controlObject.AddComponent<Camera>();
                TMP_Text preview = previewObject.AddComponent<TextMeshProUGUI>();
                TMP_Text status = statusObject.AddComponent<TextMeshProUGUI>();
                UnityEngine.UI.Button button = buttonObject.AddComponent<UnityEngine.UI.Button>();
                TMP_Text buttonLabel = buttonLabelObject.AddComponent<TextMeshProUGUI>();
                SetPrivateField(controller, "preparationRoot", overlay);
                SetPrivateField(
                    controller,
                    "controlsToDisableDuringPreparation",
                    new Behaviour[] { control });
                SetPrivateField(controller, "voicePreviewText", preview);
                SetPrivateField(controller, "voiceStatusText", status);
                SetPrivateField(controller, "microphoneButton", button);
                SetPrivateField(controller, "microphoneButtonLabel", buttonLabel);

                controller.BeginPreparation();
                Assert.IsTrue(overlay.activeSelf);
                Assert.IsFalse(control.enabled);

                preview.text = "Live: Preserved evidence";
                status.text = "Finalizing voice note...";
                buttonLabel.text = "Finalizing...";
                button.interactable = false;
                SetPrivateField(controller, "_latestVoiceTranscript", "Preserved evidence");
                SetPrivateField(controller, "_voiceSessionActive", true);
                SetPrivateField(controller, "_voiceFinalizing", true);

                InvokePrivate(controller, "SuspendPreparation");
                Assert.IsFalse(overlay.activeSelf);
                Assert.IsTrue(control.enabled);

                InvokePrivate(controller, "ResumePreparation");
                Assert.IsTrue(overlay.activeSelf);
                Assert.IsFalse(control.enabled);
                Assert.AreEqual("\u2022 Preserved evidence", controller.NotesText);
                Assert.AreEqual(string.Empty, preview.text);
                StringAssert.Contains("preserved", status.text.ToLowerInvariant());
                Assert.AreEqual("Start voice note", buttonLabel.text);
                Assert.IsTrue(button.interactable);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(controllerObject);
                UnityEngine.Object.DestroyImmediate(overlay);
                UnityEngine.Object.DestroyImmediate(controlObject);
                UnityEngine.Object.DestroyImmediate(previewObject);
                UnityEngine.Object.DestroyImmediate(statusObject);
                UnityEngine.Object.DestroyImmediate(buttonObject);
                UnityEngine.Object.DestroyImmediate(buttonLabelObject);
            }
        }

        [Test]
        public void TargetSceneContainsFullyWiredPreparationSystem()
        {
            Scene existingScene = SceneManager.GetSceneByPath(TargetScenePath);
            bool openedHere = !existingScene.IsValid() || !existingScene.isLoaded;
            Scene scene = openedHere
                ? EditorSceneManager.OpenScene(TargetScenePath, OpenSceneMode.Additive)
                : existingScene;

            try
            {
                GameObject system = scene.GetRootGameObjects()
                    .FirstOrDefault(root => root.name == "Debate Preparation System");
                Assert.IsNotNull(system);

                DebatePreparationController controller = system.GetComponent<DebatePreparationController>();
                Assert.IsNotNull(controller);
                Assert.IsNotNull(system.GetComponent<XfyunRealtimeTranscriber>());

                SerializedObject serializedController = new(controller);
                Assert.AreEqual(
                    180f,
                    serializedController.FindProperty("preparationDurationSeconds").floatValue);
                AssertObjectReferenceAssigned(serializedController, "roundManager");
                AssertObjectReferenceAssigned(serializedController, "realtimeTranscriber");
                AssertObjectReferenceAssigned(serializedController, "preparationRoot");
                AssertObjectReferenceAssigned(serializedController, "countdownText");
                AssertObjectReferenceAssigned(serializedController, "notesInput");
                AssertObjectReferenceAssigned(serializedController, "voicePreviewText");
                AssertObjectReferenceAssigned(serializedController, "voiceStatusText");
                AssertObjectReferenceAssigned(serializedController, "microphoneButton");
                AssertObjectReferenceAssigned(serializedController, "skipPreparationButton");
                AssertObjectReferenceAssigned(serializedController, "roundTimerRoot");
                AssertObjectReferenceAssigned(serializedController, "debateNotesTab");
                AssertObjectReferenceAssigned(serializedController, "debateNotesPanel");
                AssertObjectReferenceAssigned(serializedController, "debateNotesText");

                SerializedProperty controls = serializedController.FindProperty(
                    "controlsToDisableDuringPreparation");
                Assert.IsNotNull(controls);
                Assert.GreaterOrEqual(controls.arraySize, 2);
                for (int index = 0; index < controls.arraySize; index++)
                {
                    Assert.IsNotNull(controls.GetArrayElementAtIndex(index).objectReferenceValue);
                }

                DebateRoundManager roundManager = scene.GetRootGameObjects()
                    .SelectMany(root => root.GetComponentsInChildren<DebateRoundManager>(true))
                    .SingleOrDefault();
                Assert.IsNotNull(roundManager);
                Assert.AreEqual(TargetScenePath, roundManager.gameObject.scene.path);
                Assert.AreSame(
                    roundManager,
                    serializedController.FindProperty("roundManager").objectReferenceValue);
                SerializedObject serializedRoundManager = new(roundManager);
                Assert.IsFalse(serializedRoundManager.FindProperty("startOnPlay").boolValue);
                Assert.IsNull(serializedRoundManager.FindProperty("startButton").objectReferenceValue);

                TMP_Text debateNotesText = (TMP_Text)serializedController
                    .FindProperty("debateNotesText")
                    .objectReferenceValue;
                Assert.IsFalse(debateNotesText.richText);
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
        public void TargetSceneUsesIndividualPracticeTopicAcrossUiRefereeAndOpponent()
        {
            const string expectedTopic =
                "Individual practice and interaction with others, which is more beneficial for developing English speaking skills?";
            Scene existingScene = SceneManager.GetSceneByPath(TargetScenePath);
            bool openedHere = !existingScene.IsValid() || !existingScene.isLoaded;
            Scene scene = openedHere
                ? EditorSceneManager.OpenScene(TargetScenePath, OpenSceneMode.Additive)
                : existingScene;

            try
            {
                DebateRoundManager roundManager = scene.GetRootGameObjects()
                    .SelectMany(root => root.GetComponentsInChildren<DebateRoundManager>(true))
                    .Single();
                SerializedObject serializedRoundManager = new(roundManager);

                Assert.AreEqual(
                    expectedTopic,
                    serializedRoundManager.FindProperty("debateTopic").stringValue);
                Assert.GreaterOrEqual(
                    serializedRoundManager.FindProperty("openingCaptionSeconds").floatValue,
                    10f);
                Assert.IsNull(
                    serializedRoundManager.FindProperty("refereeOpeningClip").objectReferenceValue,
                    "The old referee recording must not speak the previous topic over the new caption.");
                SerializedProperty useMiniMaxSpeech = serializedRoundManager.FindProperty(
                    "useMiniMaxRefereeNarration");
                SerializedProperty disableOpponentConversation = serializedRoundManager.FindProperty(
                    "disableOpponentConversation");
                Assert.IsNotNull(useMiniMaxSpeech, "The scene must enable a generated referee voice.");
                Assert.IsNotNull(disableOpponentConversation, "The scene must explicitly be practice-only.");
                Assert.IsTrue(useMiniMaxSpeech.boolValue);
                Assert.AreEqual(
                    MiniMaxTtsClient.MaleVoiceId,
                    serializedRoundManager.FindProperty("refereeMiniMaxVoiceId").stringValue);
                Assert.IsTrue(disableOpponentConversation.boolValue);
                Assert.IsFalse(serializedRoundManager.FindProperty("sendOpponentOpeningPrompt").boolValue);
                StringAssert.Contains(
                    "interaction with others is more beneficial",
                    serializedRoundManager.FindProperty("opponentOpeningPrompt").stringValue);
                StringAssert.Contains(
                    "Individual practice",
                    serializedRoundManager.FindProperty("refereeOpeningLine").stringValue);
                StringAssert.DoesNotContain(
                    "Berance",
                    serializedRoundManager.FindProperty("refereeOpeningLine").stringValue);
                StringAssert.DoesNotContain(
                    "debate begins",
                    serializedRoundManager.FindProperty("refereeOpeningLine").stringValue.ToLowerInvariant());

                TMP_Text topicText = scene.GetRootGameObjects()
                    .SelectMany(root => root.GetComponentsInChildren<TMP_Text>(true))
                    .Single(text => text.gameObject.name == "Topic");
                Assert.AreEqual("Topic: " + expectedTopic, topicText.text);

                DebatePreparationController controller = scene.GetRootGameObjects()
                    .SelectMany(root => root.GetComponentsInChildren<DebatePreparationController>(true))
                    .Single();
                SerializedObject serializedController = new(controller);
                UnityEngine.UI.Button skipButton = (UnityEngine.UI.Button)serializedController
                    .FindProperty("skipPreparationButton")
                    .objectReferenceValue;
                Assert.IsNotNull(skipButton);
                Assert.AreEqual("Skip Preparation", skipButton.GetComponentInChildren<TMP_Text>(true).text);
            }
            finally
            {
                if (openedHere && scene.IsValid())
                {
                    EditorSceneManager.CloseScene(scene, true);
                }
            }
        }

        private static void AssertObjectReferenceAssigned(SerializedObject serializedObject, string propertyName)
        {
            SerializedProperty property = serializedObject.FindProperty(propertyName);
            Assert.IsNotNull(property, propertyName + " property was not found.");
            Assert.IsNotNull(property.objectReferenceValue, propertyName + " is not assigned.");
        }

        private static void SetPrivateField(object target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, fieldName + " field was not found.");
            field.SetValue(target, value);
        }

        private static object InvokePrivate(object target, string methodName, params object[] arguments)
        {
            MethodInfo method = target.GetType().GetMethod(
                methodName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(method, methodName + " method was not found.");
            return method.Invoke(target, arguments);
        }
    }
}
