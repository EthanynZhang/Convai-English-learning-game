using Game.Debate;
using NUnit.Framework;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Tests.EditMode
{
    public sealed class ThreeStageDebatePracticeTests
    {
        [Test]
        public void FlowContainsOnlyTheThreeApprovedPracticeStages()
        {
            CollectionAssert.AreEqual(
                new[]
                {
                    DebatePracticeStage.MicroPractice,
                    DebatePracticeStage.FullSpeechWithFeedback,
                    DebatePracticeStage.RevisionSpeech
                },
                ThreeStageDebatePracticeRules.StageSequence);
        }

        [Test]
        public void MicroPracticeUsesTenMinuteWindowAndAllowsFreeToggling()
        {
            Assert.AreEqual(600f, ThreeStageDebatePracticeRules.MicroPracticeSeconds);
            Assert.IsTrue(ThreeStageDebatePracticeRules.CanStopRecording(
                DebatePracticeStage.MicroPractice, 0f));
            Assert.IsTrue(ThreeStageDebatePracticeRules.CanStopRecording(
                DebatePracticeStage.MicroPractice, 12.5f));
            Assert.IsTrue(ThreeStageDebatePracticeRules.IsStageTimeExpired(
                DebatePracticeStage.MicroPractice, 600f));
        }

        [Test]
        public void BothCompleteSpeechesRequireNinetySecondsAndStopAtThreeMinutes()
        {
            foreach (DebatePracticeStage stage in new[]
                     {
                         DebatePracticeStage.FullSpeechWithFeedback,
                         DebatePracticeStage.RevisionSpeech
                     })
            {
                Assert.IsFalse(ThreeStageDebatePracticeRules.CanStopRecording(stage, 89.99f));
                Assert.IsTrue(ThreeStageDebatePracticeRules.CanStopRecording(stage, 90f));
                Assert.IsFalse(ThreeStageDebatePracticeRules.ShouldAutoStopRecording(stage, 179.99f));
                Assert.IsTrue(ThreeStageDebatePracticeRules.ShouldAutoStopRecording(stage, 180f));
            }
        }

        [Test]
        public void RevisionSpeechUsesSilentDiagnosisWithoutVisibleCoach()
        {
            Assert.IsTrue(ThreeStageDebatePracticeRules.UsesVisibleCoach(
                DebatePracticeStage.MicroPractice));
            Assert.IsTrue(ThreeStageDebatePracticeRules.UsesVisibleCoach(
                DebatePracticeStage.FullSpeechWithFeedback));
            Assert.IsFalse(ThreeStageDebatePracticeRules.UsesVisibleCoach(
                DebatePracticeStage.RevisionSpeech));
        }

        [TestCase(CoachOrchestrationMode.LearnerLed, CoachEpisodeState.Available,
            CoachDecisionCue.AskCoach)]
        [TestCase(CoachOrchestrationMode.LearnerLed, CoachEpisodeState.Active,
            CoachDecisionCue.SpeakFeedback)]
        [TestCase(CoachOrchestrationMode.SharedControl, CoachEpisodeState.Invited,
            CoachDecisionCue.Invite)]
        [TestCase(CoachOrchestrationMode.SharedControl, CoachEpisodeState.Active,
            CoachDecisionCue.SpeakFeedback)]
        [TestCase(CoachOrchestrationMode.AiLed, CoachEpisodeState.Active,
            CoachDecisionCue.AnnounceFocusAndSpeak)]
        public void DecisionMomentCueMatchesTheThreeOrchestrationConditions(
            CoachOrchestrationMode mode,
            CoachEpisodeState state,
            CoachDecisionCue expected)
        {
            Assert.AreEqual(expected, CoachDecisionMomentRules.ResolveCue(mode, state));
        }

        [Test]
        public void AnnaFixedAnchorRestoresPositionAndRotation()
        {
            GameObject anna = new("Anna");
            try
            {
                anna.transform.SetPositionAndRotation(
                    new Vector3(1.1f, 0f, 2.33f),
                    Quaternion.Euler(0f, 180f, 0f));
                CoachFixedPoseAnchor anchor = new(anna.transform);

                anna.transform.SetPositionAndRotation(
                    new Vector3(-3f, 4f, 8f),
                    Quaternion.identity);
                anchor.Apply();

                Assert.AreEqual(new Vector3(1.1f, 0f, 2.33f), anna.transform.position);
                Assert.Less(Quaternion.Angle(
                    Quaternion.Euler(0f, 180f, 0f),
                    anna.transform.rotation), 0.001f);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(anna);
            }
        }

        [Test]
        public void RevisionStageAdvancesToCompletionInsteadOfAnotherCoachEpisode()
        {
            Assert.AreEqual(
                DebatePracticeStage.FullSpeechWithFeedback,
                ThreeStageDebatePracticeRules.Next(DebatePracticeStage.MicroPractice));
            Assert.AreEqual(
                DebatePracticeStage.RevisionSpeech,
                ThreeStageDebatePracticeRules.Next(DebatePracticeStage.FullSpeechWithFeedback));
            Assert.IsNull(ThreeStageDebatePracticeRules.Next(DebatePracticeStage.RevisionSpeech));
        }

        [TestCase(DebatePracticeStage.MicroPractice, "Challenge & Revision Lab", 1, 600f)]
        [TestCase(DebatePracticeStage.FullSpeechWithFeedback, "Full Speech + Coach Feedback", 2, 180f)]
        [TestCase(DebatePracticeStage.RevisionSpeech, "Revision Speech", 3, 180f)]
        public void StagePresentationIsStable(
            DebatePracticeStage stage,
            string expectedTitle,
            int expectedNumber,
            float expectedLimit)
        {
            Assert.AreEqual(expectedTitle, ThreeStageDebatePracticeRules.GetTitle(stage));
            Assert.AreEqual(expectedNumber, ThreeStageDebatePracticeRules.GetStageNumber(stage));
            Assert.AreEqual(expectedLimit, ThreeStageDebatePracticeRules.GetDisplayLimitSeconds(stage));
        }

        [TestCase("Game.Debate.ThreeStageDebatePracticeController")]
        [TestCase("Game.Debate.ThreeStageDebatePracticeView")]
        public void RuntimeContractsExist(string fullName)
        {
            Assert.IsNotNull(
                typeof(DebatePracticeStage).Assembly.GetType(fullName),
                fullName + " must exist for the approved three-stage scene flow.");
        }

        [Test]
        public void ControllerExposesReadOnlyStudyState()
        {
            Type controller = typeof(DebatePracticeStage).Assembly.GetType(
                "Game.Debate.ThreeStageDebatePracticeController");

            Assert.IsNotNull(controller.GetProperty("CurrentStage"));
            Assert.IsNotNull(controller.GetProperty("Phase"));
            Assert.IsNotNull(controller.GetProperty("Mode"));
            Assert.IsNotNull(controller.GetProperty("RecordingElapsedSeconds"));
            Assert.IsNotNull(controller.GetProperty("ConfirmedTranscript"));
        }

        [Test]
        public void AiLedFeedbackDoesNotEndBecauseOfFeedbackTurnCount()
        {
            CoachPolicyConfig config = CoachPolicyConfig.CreateDefault();
            CoachOrchestrationPolicy policy = new(config);
            CoachDiagnosisResult diagnosis = new()
            {
                Success = true,
                DiagnosisIssueCode = "ARGUMENT_EXPLANATION_UNCLEAR",
                Severity = 3,
                Confidence = 0.9f,
                RecommendedFocus = "Explanation"
            };

            CoachPolicyDecision continueDecision = policy.Evaluate(new CoachPolicyInput
            {
                Mode = CoachOrchestrationMode.AiLed,
                EpisodeState = CoachEpisodeState.AwaitingLearnerAction,
                Diagnosis = diagnosis,
                CoachTurnIndex = 1,
                ActionWindowExpired = true
            });
            CoachPolicyDecision laterDecision = policy.Evaluate(new CoachPolicyInput
            {
                Mode = CoachOrchestrationMode.AiLed,
                EpisodeState = CoachEpisodeState.AwaitingLearnerAction,
                Diagnosis = diagnosis,
                CoachTurnIndex = 1000,
                ActionWindowExpired = true
            });

            Assert.AreEqual(CoachPolicyAction.Continue, continueDecision.Action);
            Assert.AreEqual(CoachEpisodeState.Active, continueDecision.NextState);
            Assert.AreEqual(CoachPolicyAction.Continue, laterDecision.Action);
            Assert.AreEqual(CoachEpisodeState.Active, laterDecision.NextState);
        }

        [Test]
        public void Scene04UsesThreeStageRuntimeAndKeepsAnnaStationary()
        {
            const string scenePath = "Assets/Game/Scenes/04 coach Agent.unity";
            string scene = File.ReadAllText(scenePath);
            string controllerGuid = AssetDatabase.AssetPathToGUID(
                "Assets/Game/Scripts/ThreeStageDebatePracticeController.cs");
            string viewGuid = AssetDatabase.AssetPathToGUID(
                "Assets/Game/Scripts/ThreeStageDebatePracticeView.cs");

            StringAssert.Contains(controllerGuid, scene);
            StringAssert.Contains(viewGuid, scene);
            StringAssert.DoesNotContain("coachNPC: {fileID: 0}", scene);
            StringAssert.DoesNotContain("opponentNPC: {fileID: 0}", scene);
            StringAssert.DoesNotContain("realtimeTranscriber: {fileID: 0}", scene);
            StringAssert.DoesNotContain("studyView: {fileID: 0}", scene);
            StringAssert.Contains("coachFixedWorldPosition: {x: 1.1, y: 0, z: 2.33}", scene);
            StringAssert.Contains("opponentFixedWorldPosition: {x: -0.25, y: 0, z: 2.5}", scene);
            StringAssert.Contains("speakOpponentChallenges: 1", scene);
        }

        [Test]
        public void TransferGuardRejectsThreeStageCoachComponents()
        {
            string guard = File.ReadAllText(
                "Assets/Game/Scripts/TransferDebateStageGuard.cs");

            StringAssert.Contains("FindAnyObjectByType<ThreeStageDebatePracticeController>", guard);
            StringAssert.Contains("FindAnyObjectByType<ThreeStageDebatePracticeView>", guard);
        }

        [Test]
        public void LatestSceneBackupExistsAndIsNotInBuildSettings()
        {
            const string backup =
                "Assets/Game/Scenes/backup/04 coach Agent pre-leo-challenge-loop 2026-07-17.unity";

            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<SceneAsset>(backup));
            Assert.IsFalse(Array.Exists(EditorBuildSettings.scenes, scene => scene.path == backup));
        }

        [Test]
        public void CoachFacingRotationPointsAnnaTowardTheLearner()
        {
            MethodInfo method = typeof(CoachFixedPoseAnchor).GetMethod(
                "CalculateFacingRotation",
                BindingFlags.Public | BindingFlags.Static);
            Assert.IsNotNull(method);

            Vector3 coach = new(1.1f, 0f, 2.33f);
            Vector3 learner = new(0.35f, 1f, -0.2f);
            Quaternion rotation = (Quaternion)method.Invoke(null, new object[] { coach, learner });
            Vector3 expected = learner - coach;
            expected.y = 0f;

            Assert.Less(Vector3.Angle(rotation * Vector3.forward, expected.normalized), 0.01f);
        }

        [Test]
        public void SetupReusesAssignedParticipantAndOnlyShowsConditionButtons()
        {
            GameObject canvasObject = new("Canvas", typeof(Canvas));
            GameObject viewObject = new("View");
            try
            {
                ThreeStageDebatePracticeView view =
                    viewObject.AddComponent<ThreeStageDebatePracticeView>();
                view.Build(canvasObject.GetComponent<Canvas>());
                MethodInfo configure = typeof(ThreeStageDebatePracticeView).GetMethod(
                    "ConfigureAssignedSession", BindingFlags.Public | BindingFlags.Instance);
                Assert.IsNotNull(configure,
                    "Scene 04 setup must receive the participant from the active Scene 01 session.");
                configure?.Invoke(view, new object[]
                {
                    "P-CLICK", CoachOrchestrationMode.SharedControl
                });
                Button shared = view.RootRect.GetComponentsInChildren<Button>(true)
                    .Single(button => button.gameObject.name == "Shared Control");
                Button start = view.RootRect.GetComponentsInChildren<Button>(true)
                    .Single(button => button.gameObject.name == "Start Practice");
                string assignedParticipant = string.Empty;
                CoachOrchestrationMode selected = CoachOrchestrationMode.Disabled;
                view.SessionStartRequested += (participant, mode) =>
                {
                    assignedParticipant = participant;
                    selected = mode;
                };

                shared.onClick.Invoke();
                start.onClick.Invoke();

                Assert.AreEqual("P-CLICK", assignedParticipant);
                Assert.AreEqual(CoachOrchestrationMode.SharedControl, selected);
                Assert.IsFalse(view.RootRect.GetComponentsInChildren<TMP_InputField>(true)
                    .Any(input => input.gameObject.name == "Participant ID"));
                Assert.IsEmpty(view.RootRect.GetComponentsInChildren<TMP_Dropdown>(true));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(canvasObject);
                UnityEngine.Object.DestroyImmediate(viewObject);
            }
        }

        [Test]
        public void StandaloneScene04UsesADebugParticipantInsteadOfBlockingSetup()
        {
            GameObject canvasObject = new("Canvas", typeof(Canvas));
            GameObject viewObject = new("View");
            GameObject controllerObject = new("Controller");
            controllerObject.SetActive(false);
            try
            {
                foreach (ResearchSessionManager existing in
                         UnityEngine.Object.FindObjectsByType<ResearchSessionManager>(
                             FindObjectsInactive.Include, FindObjectsSortMode.None))
                    UnityEngine.Object.DestroyImmediate(existing.gameObject);

                ThreeStageDebatePracticeView view =
                    viewObject.AddComponent<ThreeStageDebatePracticeView>();
                view.Build(canvasObject.GetComponent<Canvas>());
                ThreeStageDebatePracticeController controller =
                    controllerObject.AddComponent<ThreeStageDebatePracticeController>();
                typeof(ThreeStageDebatePracticeController).GetField(
                        "studyView", BindingFlags.NonPublic | BindingFlags.Instance)
                    ?.SetValue(controller, view);

                typeof(ThreeStageDebatePracticeController).GetMethod(
                        "TryBeginAssignedStudy", BindingFlags.NonPublic | BindingFlags.Instance)
                    ?.Invoke(controller, null);

                string assignedParticipant = (string)typeof(ThreeStageDebatePracticeView)
                    .GetField("_assignedParticipantId",
                        BindingFlags.NonPublic | BindingFlags.Instance)
                    ?.GetValue(view);
                Assert.AreEqual("DEBUG_SCENE04", assignedParticipant,
                    "Standalone Scene 04 must remain usable for developer bug fixing.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(controllerObject);
                UnityEngine.Object.DestroyImmediate(canvasObject);
                UnityEngine.Object.DestroyImmediate(viewObject);
            }
        }

        [Test]
        public void FocusSelectionUsesClickableButtons()
        {
            GameObject canvasObject = new("Canvas", typeof(Canvas));
            GameObject viewObject = new("View");
            try
            {
                ThreeStageDebatePracticeView view =
                    viewObject.AddComponent<ThreeStageDebatePracticeView>();
                view.Build(canvasObject.GetComponent<Canvas>());
                Button evidence = view.RootRect.GetComponentsInChildren<Button>(true)
                    .Single(button => button.gameObject.name == "Focus Evidence");

                evidence.onClick.Invoke();

                Assert.AreEqual("Evidence", view.SelectedFocus);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(canvasObject);
                UnityEngine.Object.DestroyImmediate(viewObject);
            }
        }

        [Test]
        public void CoachOpportunityUiMakesTheThreeAuthorityStructuresVisiblyDifferent()
        {
            GameObject canvasObject = new("Canvas", typeof(Canvas));
            GameObject viewObject = new("View");
            try
            {
                ThreeStageDebatePracticeView view =
                    viewObject.AddComponent<ThreeStageDebatePracticeView>();
                view.Build(canvasObject.GetComponent<Canvas>());
                MethodInfo show = typeof(ThreeStageDebatePracticeView).GetMethod(
                    "ShowConditionOpportunity", BindingFlags.Public | BindingFlags.Instance);
                Assert.IsNotNull(show);
                CoachDiagnosisResult diagnosis = new()
                {
                    Success = true,
                    DiagnosisIssueCode = "ARGUMENT_EVIDENCE_GENERAL",
                    WeakComponent = "Evidence",
                    Severity = 3,
                    Confidence = 0.9f,
                    RecommendedFocus = "Explanation",
                    RecommendedNextAction = "Add a complete example and explain it."
                };

                show.Invoke(view, new object[] { CoachOrchestrationMode.AiLed, diagnosis });
                CollectionAssert.IsEmpty(VisibleCoachButtonLabels(view));
                StringAssert.Contains("AI-LED", ActiveAuthorityBanner(view));
                Assert.IsFalse(view.IsLearnerRequestVisible);

                show.Invoke(view, new object[] { CoachOrchestrationMode.LearnerLed, diagnosis });
                CollectionAssert.AreEquivalent(
                    new[] { "Ask Coach", "Continue Without Coach" },
                    VisibleCoachButtonLabels(view));
                StringAssert.Contains("LEARNER-LED", ActiveAuthorityBanner(view));
                Assert.IsFalse(view.IsLearnerRequestVisible);

                show.Invoke(view, new object[] { CoachOrchestrationMode.SharedControl, diagnosis });
                CollectionAssert.AreEquivalent(
                    new[] { "Accept Suggestion", "Change Request", "Continue Without Coach" },
                    VisibleCoachButtonLabels(view));
                StringAssert.Contains("SHARED CONTROL", ActiveAuthorityBanner(view));
                Assert.IsFalse(view.IsLearnerRequestVisible);
                Assert.IsFalse(view.RootRect.GetComponentsInChildren<Transform>(true)
                    .Single(item => item.gameObject.name == "Focus Selector").gameObject.activeSelf);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(canvasObject);
                UnityEngine.Object.DestroyImmediate(viewObject);
            }
        }

        [Test]
        public void LearnerAndSharedRequestEntrySupportTextOrVoiceWithoutGenericFocusButtons()
        {
            GameObject canvasObject = new("Canvas", typeof(Canvas));
            GameObject viewObject = new("View");
            try
            {
                ThreeStageDebatePracticeView view =
                    viewObject.AddComponent<ThreeStageDebatePracticeView>();
                view.Build(canvasObject.GetComponent<Canvas>());
                MethodInfo show = typeof(ThreeStageDebatePracticeView).GetMethod(
                    "ShowConditionRequestEntry", BindingFlags.Public | BindingFlags.Instance);
                Assert.IsNotNull(show);

                show.Invoke(view, new object[] { CoachOrchestrationMode.LearnerLed });
                Assert.IsTrue(view.IsLearnerRequestVisible);
                CollectionAssert.AreEquivalent(
                    new[] { "Send Request", "Continue Without Coach" },
                    VisibleCoachButtonLabels(view));

                show.Invoke(view, new object[] { CoachOrchestrationMode.SharedControl });
                Assert.IsTrue(view.IsLearnerRequestVisible);
                CollectionAssert.AreEquivalent(
                    new[] { "Send Alternative Request", "Continue Without Coach" },
                    VisibleCoachButtonLabels(view));
                Assert.IsFalse(view.RootRect.GetComponentsInChildren<Transform>(true)
                    .Single(item => item.gameObject.name == "Focus Selector").gameObject.activeSelf);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(canvasObject);
                UnityEngine.Object.DestroyImmediate(viewObject);
            }
        }

        [Test]
        public void SharedControlSuggestionIsOneSentenceAndDoesNotLeakFullAdvice()
        {
            Type experience = typeof(DebatePracticeStage).Assembly.GetType(
                "Game.Debate.CoachConditionExperience");
            Assert.IsNotNull(experience);
            MethodInfo build = experience.GetMethod(
                "BuildSharedSuggestion", BindingFlags.Public | BindingFlags.Static);
            Assert.IsNotNull(build);
            CoachDiagnosisResult diagnosis = new()
            {
                Success = true,
                WeakComponent = "Evidence",
                RecommendedFocus = "Explanation",
                RecommendedNextAction = "Add a classroom example and explain every detail."
            };

            string suggestion = (string)build.Invoke(null, new object[] { diagnosis });

            StringAssert.Contains("Evidence", suggestion);
            StringAssert.Contains("Explanation", suggestion);
            StringAssert.DoesNotContain(diagnosis.RecommendedNextAction, suggestion);
            Assert.That(suggestion.Count(character => character == '.'), Is.InRange(1, 2));
        }

        [Test]
        public void SharedAlternativeRequestTransfersAgendaOwnershipToTheLearner()
        {
            CoachOrchestrationPolicy policy = new(CoachPolicyConfig.CreateDefault());
            CoachPolicyDecision decision = policy.Evaluate(new CoachPolicyInput
            {
                Mode = CoachOrchestrationMode.SharedControl,
                EpisodeState = CoachEpisodeState.Invited,
                Diagnosis = new CoachDiagnosisResult
                {
                    Success = true,
                    DiagnosisIssueCode = "ARGUMENT_EVIDENCE_GENERAL",
                    Severity = 3,
                    Confidence = 0.9f,
                    RecommendedFocus = "Explanation"
                },
                LearnerAction = CoachLearnerAction.RequestCoach
            });

            Assert.AreEqual(CoachPolicyAction.Start, decision.Action);
            Assert.AreEqual(ControlOwner.Learner, decision.AgendaOwner);
            Assert.AreEqual("SharedLearnerChangedRequest", decision.PolicyReason);
        }

        [Test]
        public void Scene04DeclaresConditionSpecificControlEvents()
        {
            string source = File.ReadAllText(
                "Assets/Game/Scripts/ThreeStageDebatePracticeController.cs");
            foreach (string eventType in new[]
                     {
                         "coach_opportunity_presented",
                         "coach_auto_started",
                         "coach_request_opened",
                         "coach_request_submitted",
                         "coach_skipped",
                         "suggestion_accepted",
                         "suggestion_change_requested",
                         "suggestion_declined"
                     })
                StringAssert.Contains(eventType, source);
        }

        [Test]
        public void LearnerLedAskCoachSendsTheNaturalLanguageRequest()
        {
            GameObject canvasObject = new("Canvas", typeof(Canvas));
            GameObject viewObject = new("View");
            try
            {
                ThreeStageDebatePracticeView view =
                    viewObject.AddComponent<ThreeStageDebatePracticeView>();
                view.Build(canvasObject.GetComponent<Canvas>());
                MethodInfo configure = typeof(ThreeStageDebatePracticeView).GetMethod(
                    "SetLearnerRequestVisible", BindingFlags.Public | BindingFlags.Instance);
                Assert.IsNotNull(configure);
                configure.Invoke(view, new object[] { true });
                TMP_InputField request = view.RootRect
                    .GetComponentsInChildren<TMP_InputField>(true)
                    .Single(input => input.gameObject.name == "Learner Request");
                Button ask = view.RootRect.GetComponentsInChildren<Button>(true)
                    .Single(button => button.gameObject.name == "Ask Coach");
                CoachLearnerAction action = CoachLearnerAction.None;
                string payload = string.Empty;
                view.LearnerActionRequested += (value, text) =>
                {
                    action = value;
                    payload = text;
                };

                request.text = "Please explain how I can make my example more convincing.";
                ask.onClick.Invoke();

                Assert.AreEqual(CoachLearnerAction.RequestCoach, action);
                Assert.AreEqual(request.text, payload);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(canvasObject);
                UnityEngine.Object.DestroyImmediate(viewObject);
            }
        }

        [Test]
        public void CoachPanelHasOneEndButtonAndRetainsEveryFeedbackEntry()
        {
            GameObject canvasObject = new("Canvas", typeof(Canvas));
            GameObject viewObject = new("View");
            try
            {
                ThreeStageDebatePracticeView view =
                    viewObject.AddComponent<ThreeStageDebatePracticeView>();
                view.Build(canvasObject.GetComponent<Canvas>());
                MethodInfo append = typeof(ThreeStageDebatePracticeView).GetMethod(
                    "AppendFeedbackHistory", BindingFlags.Public | BindingFlags.Instance);
                Assert.IsNotNull(append);

                append.Invoke(view, new object[] { "Check my evidence.", "Your phrase 'many things' is too broad." });
                append.Invoke(view, new object[] { "Give me another example.", "Name one classroom speaking task." });
                TMP_Text history = canvasObject.GetComponentsInChildren<TMP_Text>(true)
                    .Single(text => text.gameObject.name == "Feedback History Text");
                Button[] endButtons = view.RootRect.GetComponentsInChildren<Button>(true)
                    .Where(button => button.gameObject.name is "End Coaching" or "Exit Coaching")
                    .ToArray();

                StringAssert.Contains("Check my evidence.", history.text);
                StringAssert.Contains("Your phrase 'many things' is too broad.", history.text);
                StringAssert.Contains("Give me another example.", history.text);
                StringAssert.Contains("Name one classroom speaking task.", history.text);
                Assert.AreEqual(1, endButtons.Length);
                Assert.AreEqual("End Coaching", endButtons[0].gameObject.name);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(canvasObject);
                UnityEngine.Object.DestroyImmediate(viewObject);
            }
        }

        [Test]
        public void LearnerLedNaturalLanguageFollowUpHasNoFeedbackTurnLimit()
        {
            CoachPolicyConfig config = CoachPolicyConfig.CreateDefault();
            CoachOrchestrationPolicy policy = new(config);
            CoachDiagnosisResult diagnosis = new()
            {
                Success = true,
                DiagnosisIssueCode = "ARGUMENT_EVIDENCE_GENERAL",
                Severity = 2,
                Confidence = 0.9f,
                RecommendedFocus = "Evidence"
            };

            CoachPolicyDecision followUp = policy.Evaluate(new CoachPolicyInput
            {
                Mode = CoachOrchestrationMode.LearnerLed,
                EpisodeState = CoachEpisodeState.AwaitingLearnerAction,
                Diagnosis = diagnosis,
                CoachTurnIndex = 1,
                LearnerAction = CoachLearnerAction.RequestCoach
            });
            CoachPolicyDecision laterFollowUp = policy.Evaluate(new CoachPolicyInput
            {
                Mode = CoachOrchestrationMode.LearnerLed,
                EpisodeState = CoachEpisodeState.AwaitingLearnerAction,
                Diagnosis = diagnosis,
                CoachTurnIndex = 1000,
                LearnerAction = CoachLearnerAction.RequestCoach
            });

            Assert.AreEqual(CoachPolicyAction.Continue, followUp.Action);
            Assert.AreEqual(CoachEpisodeState.Active, followUp.NextState);
            Assert.AreEqual(CoachPolicyAction.Continue, laterFollowUp.Action);
            Assert.AreEqual(CoachEpisodeState.Active, laterFollowUp.NextState);
        }

        [Test]
        public void LevelTwoFeedbackMustUseLearnerWordsAndGiveASpecificRevisionExample()
        {
            string prompt = DebateCoachFeedbackGenerator.BuildPrompt(new CoachFeedbackRequest
            {
                PlayerUtteranceText = "Speaking helps us learn many things quickly.",
                LearnerRequest = "Please make my evidence more convincing.",
                ConfirmedFocus = "Evidence",
                FeedbackLevel = CoachFeedbackLevel.Level2
            });

            StringAssert.Contains("Quote one short exact phrase from the learner", prompt);
            StringAssert.Contains("concrete revision example", prompt);
            StringAssert.Contains("Please make my evidence more convincing.", prompt);
        }

        [Test]
        public void EndCoachingInMicroPracticeDoesNotSkipTheRequiredRevision()
        {
            MethodInfo method = typeof(ThreeStageDebatePracticeRules).GetMethod(
                "ShouldAdvanceAfterCoachEpisode",
                BindingFlags.Public | BindingFlags.Static);
            Assert.IsNotNull(method);

            bool learnerEnded = (bool)method.Invoke(null, new object[]
            {
                DebatePracticeStage.MicroPractice,
                CoachTerminationReason.LearnerEnded,
                120f
            });
            bool automaticTurnLimit = (bool)method.Invoke(null, new object[]
            {
                DebatePracticeStage.MicroPractice,
                CoachTerminationReason.TurnLimit,
                120f
            });

            Assert.IsFalse(learnerEnded);
            Assert.IsFalse(automaticTurnLimit);
        }

        [Test]
        public void MicroPracticeRunsTwoCompleteChallengeAnswerCoachRevisionCycles()
        {
            Type loopType = typeof(DebatePracticeStage).Assembly.GetType(
                "Game.Debate.MicroChallengeLoop");
            Assert.IsNotNull(loopType);
            object loop = Activator.CreateInstance(loopType);

            Assert.AreEqual("InitialStatement", GetProperty(loop, "Step").ToString());
            Invoke(loop, "ConfirmInitialStatement");
            Assert.AreEqual(1, GetProperty(loop, "CycleIndex"));
            Assert.AreEqual("Standard", GetProperty(loop, "Difficulty").ToString());
            Assert.AreEqual("GeneratingChallenge", GetProperty(loop, "Step").ToString());
            CompleteOneMicroChallengeCycle(loop);

            Assert.AreEqual(2, GetProperty(loop, "CycleIndex"));
            Assert.AreEqual("Hard", GetProperty(loop, "Difficulty").ToString());
            Assert.AreEqual("GeneratingChallenge", GetProperty(loop, "Step").ToString());
            CompleteOneMicroChallengeCycle(loop);

            Assert.AreEqual("Completed", GetProperty(loop, "Step").ToString());
            Assert.AreEqual(2, GetProperty(loop, "CompletedCycleCount"));
        }

        [Test]
        public void EveryLeoChallengeMovesDirectlyToCoachDiagnosis()
        {
            Type loopType = typeof(DebatePracticeStage).Assembly.GetType(
                "Game.Debate.MicroChallengeLoop");
            Assert.IsNotNull(loopType);
            object loop = Activator.CreateInstance(loopType);
            Invoke(loop, "ConfirmInitialStatement");
            Invoke(loop, "ChallengeReady");
            Invoke(loop, "OpponentFinishedSpeaking");

            Assert.AreEqual("Diagnosing", GetProperty(loop, "Step").ToString());
        }

        [Test]
        public void OpponentChallengePromptIsConditionBlindAndGroundedInLearnerSpeech()
        {
            Type requestType = typeof(DebatePracticeStage).Assembly.GetType(
                "Game.Debate.OpponentChallengeRequest");
            Type generatorType = typeof(DebatePracticeStage).Assembly.GetType(
                "Game.Debate.OpponentChallengeGenerator");
            Type difficultyType = typeof(DebatePracticeStage).Assembly.GetType(
                "Game.Debate.OpponentChallengeDifficulty");
            Assert.IsNotNull(requestType);
            Assert.IsNotNull(generatorType);
            Assert.IsNotNull(difficultyType);
            Assert.IsNull(requestType.GetField("Mode"));

            object request = Activator.CreateInstance(requestType);
            requestType.GetField("Topic").SetValue(request, "Reading versus speaking");
            requestType.GetField("LearnerStatement").SetValue(request,
                "Speaking helps learners react immediately in real conversations.");
            requestType.GetField("CycleIndex").SetValue(request, 1);
            requestType.GetField("Difficulty").SetValue(request,
                Enum.Parse(difficultyType, "Standard"));
            string prompt = (string)generatorType.GetMethod(
                "BuildPrompt", BindingFlags.Public | BindingFlags.Static)
                .Invoke(null, new[] { request });

            StringAssert.Contains(
                "Speaking helps learners react immediately in real conversations.", prompt);
            StringAssert.Contains("one direct challenge question", prompt);
            StringAssert.Contains("20 to 35 words", prompt);
            StringAssert.Contains("Do not infer or mention the experimental condition", prompt);
        }

        [Test]
        public void OnlyPracticeOneActivatesTheOpponent()
        {
            MethodInfo method = typeof(ThreeStageDebatePracticeRules).GetMethod(
                "UsesOpponent", BindingFlags.Public | BindingFlags.Static);
            Assert.IsNotNull(method);
            Assert.IsTrue((bool)method.Invoke(null,
                new object[] { DebatePracticeStage.MicroPractice }));
            Assert.IsFalse((bool)method.Invoke(null,
                new object[] { DebatePracticeStage.FullSpeechWithFeedback }));
            Assert.IsFalse((bool)method.Invoke(null,
                new object[] { DebatePracticeStage.RevisionSpeech }));
        }

        [Test]
        public void PracticeViewContainsAVisibleChallengeContextArea()
        {
            GameObject canvasObject = new("Canvas", typeof(Canvas));
            GameObject viewObject = new("View");
            try
            {
                ThreeStageDebatePracticeView view =
                    viewObject.AddComponent<ThreeStageDebatePracticeView>();
                view.Build(canvasObject.GetComponent<Canvas>());

                Transform context = view.RootRect.GetComponentsInChildren<Transform>(true)
                    .Single(transform => transform.name == "Practice Context");

                Assert.IsNotNull(context.GetComponent<TMP_Text>());
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(canvasObject);
                UnityEngine.Object.DestroyImmediate(viewObject);
            }
        }

        [Test]
        public void CoachEventLogIncludesStandardizedChallengeAndRevisionFields()
        {
            string directory = Path.Combine(Path.GetTempPath(),
                "micro-challenge-log-" + Guid.NewGuid().ToString("N"));
            try
            {
                CoachResearchLogger logger = new(directory);
                logger.LogEvent(new CoachEventRecord { EventType = "OpponentChallengePresented" });
                string header = File.ReadLines(logger.EventLogPath).First();

                StringAssert.Contains("opponent_utterance_text", header);
                StringAssert.Contains("challenge_cycle_index", header);
                StringAssert.Contains("challenge_difficulty", header);
                StringAssert.Contains("revision_improvement_status", header);
                StringAssert.Contains("revision_improvement_summary", header);
            }
            finally
            {
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        }

        [Test]
        public void StudyCursorPolicyKeepsTheMouseVisibleInEveryStudyPhase()
        {
            MethodInfo method = typeof(ThreeStageDebatePracticeRules).GetMethod(
                "ShouldKeepCursorVisible", BindingFlags.Public | BindingFlags.Static);
            Assert.IsNotNull(method,
                "The three-stage study needs an explicit always-visible cursor policy.");

            foreach (DebatePracticePhase phase in Enum.GetValues(typeof(DebatePracticePhase)))
                Assert.IsTrue((bool)method.Invoke(null, new object[] { phase }), phase.ToString());
        }

        [Test]
        public void FeedbackHistoryUsesACompactUpperRightPanelOnWideScreens()
        {
            GameObject canvasObject = new("Canvas", typeof(Canvas));
            GameObject viewObject = new("View");
            try
            {
                canvasObject.GetComponent<RectTransform>().sizeDelta = new Vector2(1600f, 900f);
                ThreeStageDebatePracticeView view =
                    viewObject.AddComponent<ThreeStageDebatePracticeView>();
                view.Build(canvasObject.GetComponent<Canvas>());
                view.RefreshResponsiveLayout();

                Transform panel = canvasObject.transform.Find("Coach Feedback History Panel");
                Assert.IsNotNull(panel);
                Assert.AreNotEqual(view.RootRect, panel.parent);
                RectTransform rect = panel.GetComponent<RectTransform>();
                Assert.AreEqual(1f, rect.anchorMin.x);
                Assert.AreEqual(1f, rect.anchorMax.x);
                Assert.AreEqual(1f, rect.anchorMin.y);
                Assert.AreEqual(1f, rect.anchorMax.y);
                Assert.AreEqual(1f, rect.pivot.x);
                Assert.AreEqual(1f, rect.pivot.y);
                Assert.AreEqual(360f, rect.sizeDelta.x, 0.1f);
                Assert.GreaterOrEqual(rect.sizeDelta.y, 600f);
                Assert.Less(rect.anchoredPosition.y, 0f);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(canvasObject);
                UnityEngine.Object.DestroyImmediate(viewObject);
            }
        }

        [Test]
        public void RecordingViewHasRestartButNoManualTranscriptConfirmation()
        {
            GameObject canvasObject = new("Canvas", typeof(Canvas));
            GameObject viewObject = new("View");
            try
            {
                ThreeStageDebatePracticeView view =
                    viewObject.AddComponent<ThreeStageDebatePracticeView>();
                view.Build(canvasObject.GetComponent<Canvas>());
                view.ShowPractice(DebatePracticeStage.MicroPractice, 5f, 5f,
                    "Listening...", "Live words", true, false);

                Button[] buttons = view.RootRect.GetComponentsInChildren<Button>(true);
                Assert.IsFalse(buttons.Any(button => button.gameObject.name == "Confirm Transcript"));
                Button restart = buttons.Single(button => button.gameObject.name == "Restart Recording");
                Assert.IsTrue(restart.gameObject.activeSelf);
                Assert.IsNull(typeof(ThreeStageDebatePracticeView).GetEvent(
                    "ConfirmTranscriptRequested", BindingFlags.Public | BindingFlags.Instance));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(canvasObject);
                UnityEngine.Object.DestroyImmediate(viewObject);
            }
        }

        [TestCase("A complete answer.", true)]
        [TestCase("   ", false)]
        public void CompletedSpeechUsesAutomaticTranscriptAcceptance(string transcript, bool expected)
        {
            MethodInfo method = typeof(ThreeStageDebatePracticeRules).GetMethod(
                "ShouldAutomaticallyAcceptTranscript", BindingFlags.Public | BindingFlags.Static);
            Assert.IsNotNull(method);
            Assert.AreEqual(expected, method.Invoke(null, new object[] { transcript }));
        }

        [Test]
        public void RealtimeTranscriberExposesSessionActivityForSafeRecordingRestart()
        {
            PropertyInfo property = typeof(XfyunRealtimeTranscriber).GetProperty(
                "IsSessionActive", BindingFlags.Public | BindingFlags.Instance);
            Assert.IsNotNull(property);
            Assert.AreEqual(typeof(bool), property.PropertyType);
            Assert.IsTrue(property.CanRead);
        }

        [Test]
        public void AiLedFeedbackViewHasNoFocusOrActionButtons()
        {
            GameObject canvasObject = new("Canvas", typeof(Canvas));
            GameObject viewObject = new("View");
            try
            {
                ThreeStageDebatePracticeView view =
                    viewObject.AddComponent<ThreeStageDebatePracticeView>();
                view.Build(canvasObject.GetComponent<Canvas>());
                MethodInfo method = typeof(ThreeStageDebatePracticeView).GetMethod(
                    "ShowAutomatedCoachFeedback", BindingFlags.Public | BindingFlags.Instance);
                Assert.IsNotNull(method);

                method.Invoke(view, new object[] { "Anna's feedback", "Use a concrete example." });

                Transform coach = view.RootRect.Find("Coach");
                Assert.IsNotNull(coach);
                Transform focus = coach.Find("Focus Selector");
                Assert.IsNotNull(focus);
                Assert.IsFalse(focus.gameObject.activeSelf);
                Assert.IsFalse(coach.GetComponentsInChildren<Button>(true)
                    .Any(button => button.gameObject.activeInHierarchy));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(canvasObject);
                UnityEngine.Object.DestroyImmediate(viewObject);
            }
        }

        [Test]
        public void FeedbackHistoryRemainsVisibleAcrossPracticeAndCoachPanels()
        {
            GameObject canvasObject = new("Canvas", typeof(Canvas));
            GameObject viewObject = new("View");
            try
            {
                canvasObject.GetComponent<RectTransform>().sizeDelta = new Vector2(1600f, 900f);
                ThreeStageDebatePracticeView view =
                    viewObject.AddComponent<ThreeStageDebatePracticeView>();
                view.Build(canvasObject.GetComponent<Canvas>());
                view.RefreshResponsiveLayout();
                view.ClearFeedbackHistory();
                view.AppendFeedbackHistory("Help with my explanation.", "First feedback.");
                view.ShowPractice(DebatePracticeStage.MicroPractice, 0f, 0f,
                    "Practice", string.Empty, false, false);

                Transform panel = canvasObject.transform.Find("Coach Feedback History Panel");
                Assert.IsTrue(panel.gameObject.activeSelf);
                view.ShowCoach("Coach", "Second feedback.", false);
                view.AppendFeedbackHistory(string.Empty, "Second feedback.");
                Assert.IsTrue(panel.gameObject.activeSelf);
                TMP_Text history = panel.GetComponentsInChildren<TMP_Text>(true)
                    .Single(text => text.gameObject.name == "Feedback History Text");
                StringAssert.Contains("First feedback.", history.text);
                StringAssert.Contains("Second feedback.", history.text);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(canvasObject);
                UnityEngine.Object.DestroyImmediate(viewObject);
            }
        }

        [TestCase("Coach (Anna)")]
        [TestCase("Opponent Leo")]
        public void NpcRoleLabelIsAWorldSpaceCanvasAboveItsTarget(string roleText)
        {
            Type labelType = typeof(ThreeStageDebatePracticeController).Assembly.GetType(
                "Game.Debate.NpcRoleWorldLabel");
            Assert.IsNotNull(labelType);
            MethodInfo ensure = labelType.GetMethod("Ensure",
                BindingFlags.Public | BindingFlags.Static);
            Assert.IsNotNull(ensure);
            GameObject target = new("NPC Target");
            try
            {
                GameObject label = (GameObject)ensure.Invoke(null, new object[]
                {
                    target.transform,
                    roleText,
                    Color.cyan,
                    new Vector3(0f, 2.15f, 0f)
                });
                Assert.IsTrue(label.transform.IsChildOf(target.transform));
                Assert.AreEqual(RenderMode.WorldSpace, label.GetComponent<Canvas>().renderMode);
                Assert.IsNotNull(label.GetComponent<RefereeLabelBillboard>());
                Assert.AreEqual(roleText, label.GetComponentInChildren<TMP_Text>(true).text);
                Assert.Greater(label.transform.localPosition.y, 1.8f);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(target);
            }
        }

        [Test]
        public void MockDebateUsesIndividualPracticeVersusInteractionTopic()
        {
            const string expectedTopic =
                "Individual practice and interaction with others, which is more beneficial for developing English speaking skills?";
            FieldInfo topicField = typeof(ThreeStageDebatePracticeRules).GetField(
                "MockDebateTopic", BindingFlags.Public | BindingFlags.Static);
            Assert.IsNotNull(topicField,
                "The active three-stage flow needs one frozen topic shared by UI and prompts.");
            Assert.AreEqual(expectedTopic, topicField.GetValue(null));

            string scene = File.ReadAllText("Assets/Game/Scenes/04 coach Agent.unity");
            StringAssert.Contains("topicId: individual_practice_vs_interaction_speaking", scene);
            StringAssert.Contains(
                "debateTopic: Individual practice and interaction with others, which is more beneficial",
                scene);
            StringAssert.Contains("for developing English speaking skills?", scene);
            StringAssert.Contains(
                "learnerStance: Interaction with others is more beneficial for developing English",
                scene);
            StringAssert.Contains("speaking skills.", scene);
            StringAssert.Contains(
                "opponentStance: Individual practice is more beneficial for developing English speaking",
                scene);
        }

        [Test]
        public void LearnerLedAskCoachCanRouteTToVoiceDictation()
        {
            MethodInfo method = typeof(ThreeStageDebatePracticeRules).GetMethod(
                "CanDictateLearnerRequest", BindingFlags.Public | BindingFlags.Static);
            Assert.IsNotNull(method,
                "Learner-led Coach interaction needs an explicit T-key dictation policy.");

            Assert.IsTrue((bool)method.Invoke(null, new object[]
            {
                CoachOrchestrationMode.LearnerLed,
                DebatePracticePhase.CoachInteraction,
                true
            }));
            Assert.IsFalse((bool)method.Invoke(null, new object[]
            {
                CoachOrchestrationMode.SharedControl,
                DebatePracticePhase.CoachInteraction,
                true
            }));
            Assert.IsFalse((bool)method.Invoke(null, new object[]
            {
                CoachOrchestrationMode.LearnerLed,
                DebatePracticePhase.ReadyToRecord,
                true
            }));
        }

        [Test]
        public void LearnerRequestVoiceTextAppendsWithoutOverwritingTypedText()
        {
            MethodInfo merge = typeof(ThreeStageDebatePracticeRules).GetMethod(
                "MergeLearnerRequestText", BindingFlags.Public | BindingFlags.Static);
            Assert.IsNotNull(merge);

            Assert.AreEqual(
                "Please check my evidence. Give me a concrete example.",
                merge.Invoke(null, new object[]
                {
                    "Please check my evidence.",
                    "Give me a concrete example."
                }));
            Assert.AreEqual("Give me a concrete example.",
                merge.Invoke(null, new object[] { string.Empty, " Give me a concrete example. " }));
            Assert.AreEqual("Please check my evidence.",
                merge.Invoke(null, new object[] { "Please check my evidence.", string.Empty }));
        }

        [Test]
        public void LearnerRequestViewAdvertisesTAndAcceptsVoiceText()
        {
            GameObject canvasObject = new("Canvas", typeof(Canvas));
            GameObject viewObject = new("View");
            try
            {
                ThreeStageDebatePracticeView view =
                    viewObject.AddComponent<ThreeStageDebatePracticeView>();
                view.Build(canvasObject.GetComponent<Canvas>());
                MethodInfo setText = typeof(ThreeStageDebatePracticeView).GetMethod(
                    "SetLearnerRequestText", BindingFlags.Public | BindingFlags.Instance);
                PropertyInfo visible = typeof(ThreeStageDebatePracticeView).GetProperty(
                    "IsLearnerRequestVisible", BindingFlags.Public | BindingFlags.Instance);
                Assert.IsNotNull(setText);
                Assert.IsNotNull(visible);

                view.SetLearnerRequestVisible(true);
                setText.Invoke(view, new object[] { "Please give me a stronger example." });
                TMP_InputField request = view.RootRect
                    .GetComponentsInChildren<TMP_InputField>(true)
                    .Single(input => input.gameObject.name == "Learner Request");
                TMP_Text instruction = view.RootRect.GetComponentsInChildren<TMP_Text>(true)
                    .Single(text => text.gameObject.name == "Learner Request Voice Hint");

                Assert.IsTrue((bool)visible.GetValue(view));
                Assert.AreEqual("Please give me a stronger example.", request.text);
                StringAssert.Contains("T", instruction.text);
                StringAssert.Contains("voice", instruction.text.ToLowerInvariant());
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(canvasObject);
                UnityEngine.Object.DestroyImmediate(viewObject);
            }
        }

        [Test]
        public void StartingAnAssignedStudyPausesAtIntroductionBeforePracticeOne()
        {
            GameObject canvasObject = new("Canvas", typeof(Canvas));
            GameObject viewObject = new("View");
            GameObject controllerObject = new("Controller");
            GameObject managerObject = null;
            string tempRoot = Path.Combine(Path.GetTempPath(),
                "scene04-introduction-" + Guid.NewGuid().ToString("N"));
            try
            {
                foreach (ResearchSessionManager existing in
                         UnityEngine.Object.FindObjectsByType<ResearchSessionManager>(
                             FindObjectsInactive.Include, FindObjectsSortMode.None))
                    UnityEngine.Object.DestroyImmediate(existing.gameObject);

                managerObject = new GameObject("Research Session Manager");
                ResearchSessionManager manager =
                    managerObject.AddComponent<ResearchSessionManager>();
                manager.StartSession("P-INTRO", tempRoot, "S-INTRO",
                    CoachOrchestrationMode.LearnerLed, "test_assignment");

                ThreeStageDebatePracticeView view =
                    viewObject.AddComponent<ThreeStageDebatePracticeView>();
                view.Build(canvasObject.GetComponent<Canvas>());
                ThreeStageDebatePracticeController controller =
                    controllerObject.AddComponent<ThreeStageDebatePracticeController>();
                typeof(ThreeStageDebatePracticeController).GetField(
                        "studyView", BindingFlags.NonPublic | BindingFlags.Instance)
                    ?.SetValue(controller, view);

                controller.BeginStudy("P-INTRO", CoachOrchestrationMode.LearnerLed);

                Assert.AreEqual("Introduction", controller.Phase.ToString());
                Assert.AreEqual(0f, controller.StageElapsedSeconds);
                Transform introduction = view.RootRect.Find("Study Introduction");
                Assert.IsNotNull(introduction);
                Assert.IsTrue(introduction.gameObject.activeSelf);
            }
            finally
            {
                Convai.Scripts.Runtime.Core.ConvaiInputManager.ShouldUseTapToTalk = null;
                Convai.Scripts.Runtime.Core.ConvaiInputManager.TapToTalkRequested = null;
                Convai.Scripts.Runtime.Core.ConvaiInputManager.ShouldSuppressTalkInput = null;
                Convai.Scripts.Runtime.Core.ConvaiPlayerInteractionManager.ShouldSuppressTalkInput = null;
                if (managerObject != null)
                    UnityEngine.Object.DestroyImmediate(managerObject);
                UnityEngine.Object.DestroyImmediate(controllerObject);
                UnityEngine.Object.DestroyImmediate(canvasObject);
                UnityEngine.Object.DestroyImmediate(viewObject);
                if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, true);
            }
        }

        [Test]
        public void IntroductionExplainsTopicRolesAndAllThreePractices()
        {
            GameObject canvasObject = new("Canvas", typeof(Canvas));
            GameObject viewObject = new("View");
            try
            {
                ThreeStageDebatePracticeView view =
                    viewObject.AddComponent<ThreeStageDebatePracticeView>();
                view.Build(canvasObject.GetComponent<Canvas>());
                MethodInfo show = typeof(ThreeStageDebatePracticeView).GetMethod(
                    "ShowIntroduction", BindingFlags.Public | BindingFlags.Instance);
                EventInfo completed = typeof(ThreeStageDebatePracticeView).GetEvent(
                    "IntroductionCompleted", BindingFlags.Public | BindingFlags.Instance);
                Assert.IsNotNull(show);
                Assert.IsNotNull(completed);
                bool beginRequested = false;
                Action handler = () => beginRequested = true;
                completed.AddEventHandler(view, handler);

                show.Invoke(view, new object[]
                {
                    ThreeStageDebatePracticeRules.MockDebateTopic,
                    ThreeStageDebatePracticeRules.LearnerStance
                });

                Transform panel = view.RootRect.Find("Study Introduction");
                Assert.IsNotNull(panel);
                string copy = string.Join("\n", panel.GetComponentsInChildren<TMP_Text>(true)
                    .Select(text => text.text));
                StringAssert.Contains(ThreeStageDebatePracticeRules.MockDebateTopic, copy);
                StringAssert.Contains("Coach (Anna)", copy);
                StringAssert.Contains("Opponent Leo", copy);
                StringAssert.Contains("Practice 1", copy);
                StringAssert.Contains("Practice 2", copy);
                StringAssert.Contains("Practice 3", copy);
                StringAssert.Contains("90 seconds", copy);
                StringAssert.Contains("Press T", copy);
                Button begin = panel.GetComponentsInChildren<Button>(true)
                    .Single(button => button.gameObject.name == "Begin Practice 1");
                begin.onClick.Invoke();

                Assert.IsTrue(beginRequested);
                Transform history = canvasObject.transform.Find("Coach Feedback History Panel");
                Assert.IsFalse(history.gameObject.activeSelf);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(canvasObject);
                UnityEngine.Object.DestroyImmediate(viewObject);
            }
        }

        [Test]
        public void Scene04ResponsiveLayoutProtectsTheOpponentLaneAndCollapsesHistoryOnNarrowScreens()
        {
            GameObject canvasObject = new("Canvas", typeof(Canvas));
            GameObject viewObject = new("View");
            try
            {
                RectTransform canvasRect = canvasObject.GetComponent<RectTransform>();
                canvasRect.sizeDelta = new Vector2(879f, 861f);
                ThreeStageDebatePracticeView view =
                    viewObject.AddComponent<ThreeStageDebatePracticeView>();
                view.Build(canvasObject.GetComponent<Canvas>());
                MethodInfo refresh = typeof(ThreeStageDebatePracticeView).GetMethod(
                    "RefreshResponsiveLayout", BindingFlags.Public | BindingFlags.Instance);
                Assert.IsNotNull(refresh);
                refresh.Invoke(view, null);
                view.ShowConditionOpportunity(CoachOrchestrationMode.SharedControl,
                    CreateDetailedDiagnosis());

                Assert.AreEqual(879f * 0.58f, view.RootRect.sizeDelta.x, 0.6f);
                Transform agenda = canvasObject.transform.Find("Coach Agenda Bubble");
                Transform history = canvasObject.transform.Find("Coach Feedback History Panel");
                Transform historyToggle = canvasObject.transform.Find("Coach History Toggle");
                Assert.IsNotNull(agenda);
                Assert.IsNotNull(history);
                Assert.IsNotNull(historyToggle);
                Assert.IsTrue(agenda.gameObject.activeSelf);
                Assert.IsFalse(history.gameObject.activeSelf);
                Assert.IsTrue(historyToggle.gameObject.activeSelf);
                RectTransform agendaRect = agenda.GetComponent<RectTransform>();
                RectTransform historyToggleRect = historyToggle.GetComponent<RectTransform>();
                Assert.GreaterOrEqual(agendaRect.anchoredPosition.x,
                    view.RootRect.anchoredPosition.x + view.RootRect.sizeDelta.x + 16f);
                Assert.LessOrEqual(
                    historyToggleRect.anchoredPosition.y,
                    agendaRect.anchoredPosition.y - agendaRect.sizeDelta.y - 8f,
                    "The narrow-screen History button must sit below the Agenda bubble.");

                canvasRect.sizeDelta = new Vector2(1600f, 900f);
                refresh.Invoke(view, null);

                Assert.AreEqual(640f, view.RootRect.sizeDelta.x, 0.1f);
                Assert.IsTrue(history.gameObject.activeSelf);
                Assert.IsFalse(historyToggle.gameObject.activeSelf);
                RectTransform historyRect = history.GetComponent<RectTransform>();
                Assert.AreEqual(360f, historyRect.sizeDelta.x, 0.1f);
                Assert.LessOrEqual(
                    agendaRect.anchoredPosition.x + agendaRect.sizeDelta.x,
                    1600f - historyRect.sizeDelta.x - 16f);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(canvasObject);
                UnityEngine.Object.DestroyImmediate(viewObject);
            }
        }

        [Test]
        public void Scene04ResponsiveLayoutConvertsPhysicalPixelsThroughCanvasScaler()
        {
            GameObject canvasObject = new("Canvas", typeof(Canvas));
            GameObject viewObject = new("View");
            try
            {
                RectTransform canvasRect = canvasObject.GetComponent<RectTransform>();
                canvasRect.sizeDelta = new Vector2(1455f, 1425f);
                ThreeStageDebatePracticeView view =
                    viewObject.AddComponent<ThreeStageDebatePracticeView>();
                view.Build(canvasObject.GetComponent<Canvas>());
                MethodInfo refresh = typeof(ThreeStageDebatePracticeView).GetMethod(
                    "RefreshResponsiveLayout",
                    BindingFlags.NonPublic | BindingFlags.Instance,
                    null,
                    new[] { typeof(Vector2) },
                    null);
                Assert.IsNotNull(refresh,
                    "The responsive view needs a pixel-viewport overload for CanvasScaler output.");
                view.ShowConditionOpportunity(CoachOrchestrationMode.SharedControl,
                    CreateDetailedDiagnosis());
                refresh.Invoke(view, new object[] { new Vector2(879f, 861f) });

                float canvasUnitsPerPixel = 1455f / 879f;
                Assert.AreEqual(879f * 0.58f,
                    view.RootRect.sizeDelta.x / canvasUnitsPerPixel, 0.6f);
                Transform history = canvasObject.transform.Find("Coach Feedback History Panel");
                Transform toggle = canvasObject.transform.Find("Coach History Toggle");
                Assert.IsFalse(history.gameObject.activeSelf);
                Assert.IsTrue(toggle.gameObject.activeSelf);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(canvasObject);
                UnityEngine.Object.DestroyImmediate(viewObject);
            }
        }

        [Test]
        public void CoachAgendaShowsTheAgendaButNeverReplacesTheFullFeedbackArea()
        {
            GameObject canvasObject = new("Canvas", typeof(Canvas));
            GameObject viewObject = new("View");
            try
            {
                ThreeStageDebatePracticeView view =
                    viewObject.AddComponent<ThreeStageDebatePracticeView>();
                view.Build(canvasObject.GetComponent<Canvas>());
                Type sourceType = typeof(DebatePracticeStage).Assembly.GetType(
                    "Game.Debate.CoachAgendaSource");
                Assert.IsNotNull(sourceType);
                MethodInfo showAgenda = typeof(ThreeStageDebatePracticeView).GetMethod(
                    "ShowCoachAgenda", BindingFlags.Public | BindingFlags.Instance);
                MethodInfo updateAgenda = typeof(ThreeStageDebatePracticeView).GetMethod(
                    "UpdateCoachAgenda", BindingFlags.Public | BindingFlags.Instance);
                MethodInfo hideAgenda = typeof(ThreeStageDebatePracticeView).GetMethod(
                    "HideCoachAgenda", BindingFlags.Public | BindingFlags.Instance);
                Assert.IsNotNull(showAgenda);
                Assert.IsNotNull(updateAgenda);
                Assert.IsNotNull(hideAgenda);

                view.ShowConditionOpportunity(CoachOrchestrationMode.LearnerLed,
                    CreateDetailedDiagnosis());
                Transform bubble = canvasObject.transform.Find("Coach Agenda Bubble");
                Assert.IsNotNull(bubble);
                Assert.IsFalse(bubble.gameObject.activeSelf);

                const string learnerRequest = "Please help me explain why my example supports my claim.";
                showAgenda.Invoke(view, new[]
                {
                    Enum.Parse(sourceType, "LearnerRequest"),
                    "Your request to Anna",
                    learnerRequest
                });
                Assert.IsTrue(bubble.gameObject.activeSelf);
                StringAssert.Contains(learnerRequest, ReadPanelText(bubble));

                const string fullFeedback =
                    "CREEI gaps: Your evidence is present, but the explanation is weak. " +
                    "Specific advice: You said 'speaking helps people.' Explain the connection. " +
                    "Add one consequence. For example, connect the practice to faster communication.";
                view.ShowConditionLoading(CoachOrchestrationMode.LearnerLed, "Preparing feedback...");
                view.ShowConditionFeedback(CoachOrchestrationMode.LearnerLed, fullFeedback);
                Assert.IsTrue(bubble.gameObject.activeSelf);
                StringAssert.Contains(learnerRequest, ReadPanelText(bubble));
                StringAssert.DoesNotContain(fullFeedback, ReadPanelText(bubble));
                Transform coach = view.RootRect.Find("Coach");
                StringAssert.Contains(fullFeedback, ReadPanelText(coach));

                const string modified = "Please focus on the impact on shy learners.";
                updateAgenda.Invoke(view, new[]
                {
                    Enum.Parse(sourceType, "LearnerModifiedRequest"),
                    "Updated request to Anna",
                    modified
                });
                StringAssert.Contains(modified, ReadPanelText(bubble));
                StringAssert.DoesNotContain(learnerRequest, ReadPanelText(bubble));
                hideAgenda.Invoke(view, null);
                Assert.IsFalse(bubble.gameObject.activeSelf);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(canvasObject);
                UnityEngine.Object.DestroyImmediate(viewObject);
            }
        }

        [Test]
        public void AiAndSharedOpportunitiesUseTheConditionBlindDiagnosisInTheAgendaBubble()
        {
            GameObject canvasObject = new("Canvas", typeof(Canvas));
            GameObject viewObject = new("View");
            try
            {
                ThreeStageDebatePracticeView view =
                    viewObject.AddComponent<ThreeStageDebatePracticeView>();
                view.Build(canvasObject.GetComponent<Canvas>());
                CoachDiagnosisResult diagnosis = CreateDetailedDiagnosis();
                Transform bubble = canvasObject.transform.Find("Coach Agenda Bubble");
                Assert.IsNotNull(bubble);

                view.ShowConditionOpportunity(CoachOrchestrationMode.SharedControl, diagnosis);
                string shared = ReadPanelText(bubble);
                StringAssert.Contains(ReadStringField(diagnosis, "CreeiGapSummary"), shared);
                StringAssert.Contains("suggests focusing on Explanation", shared);

                view.ShowConditionOpportunity(CoachOrchestrationMode.AiLed, diagnosis);
                string ai = ReadPanelText(bubble);
                StringAssert.Contains(ReadStringField(diagnosis, "CreeiGapSummary"), ai);
                StringAssert.Contains("will focus on Explanation", ai);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(canvasObject);
                UnityEngine.Object.DestroyImmediate(viewObject);
            }
        }

        [Test]
        public void Scene04ControllerDeclaresAgendaPresentationUpdateAndCleanupEvents()
        {
            string source = File.ReadAllText(
                "Assets/Game/Scripts/ThreeStageDebatePracticeController.cs");
            StringAssert.Contains("coach_agenda_presented", source);
            StringAssert.Contains("coach_agenda_updated", source);
            StringAssert.Contains("ShowCoachAgenda", source);
            StringAssert.Contains("UpdateCoachAgenda", source);
            StringAssert.Contains("HideCoachAgenda", source);
        }

        private static string[] VisibleCoachButtonLabels(ThreeStageDebatePracticeView view)
        {
            return view.RootRect.GetComponentsInChildren<Button>(true)
                .Where(button => button.gameObject.activeInHierarchy &&
                                 button.transform.IsChildOf(view.RootRect.Find("Coach")))
                .Select(button => button.GetComponentInChildren<TMP_Text>(true)?.text ?? string.Empty)
                .Where(label => !string.IsNullOrWhiteSpace(label))
                .ToArray();
        }

        private static string ActiveAuthorityBanner(ThreeStageDebatePracticeView view)
        {
            Transform banner = view.RootRect.GetComponentsInChildren<Transform>(true)
                .Single(item => item.gameObject.name == "Condition Authority Banner");
            return banner.GetComponentInChildren<TMP_Text>(true)?.text ?? string.Empty;
        }

        private static CoachDiagnosisResult CreateDetailedDiagnosis()
        {
            CoachDiagnosisResult diagnosis = new()
            {
                Success = true,
                DiagnosisIssueCode = "CREEI_EXPLANATION_WEAK",
                WeakComponent = "Explanation",
                Severity = 3,
                Confidence = 0.9f,
                RecommendedFocus = "Explanation",
                RecommendedNextAction = "Connect the evidence explicitly to the claim."
            };
            FieldInfo components = typeof(CoachDiagnosisResult).GetField(
                "CreeiMissingOrWeakComponents", BindingFlags.Public | BindingFlags.Instance);
            FieldInfo summary = typeof(CoachDiagnosisResult).GetField(
                "CreeiGapSummary", BindingFlags.Public | BindingFlags.Instance);
            Assert.IsNotNull(components);
            Assert.IsNotNull(summary);
            components.SetValue(diagnosis, new[] { "Explanation", "Impact" });
            summary.SetValue(diagnosis,
                "Your argument has a claim and evidence, but its explanation and impact need development.");
            return diagnosis;
        }

        private static string ReadStringField(object instance, string fieldName)
        {
            FieldInfo field = instance?.GetType().GetField(
                fieldName, BindingFlags.Public | BindingFlags.Instance);
            Assert.IsNotNull(field, fieldName);
            return field.GetValue(instance) as string ?? string.Empty;
        }

        private static string ReadPanelText(Transform panel)
        {
            return panel == null
                ? string.Empty
                : string.Join("\n", panel.GetComponentsInChildren<TMP_Text>(true)
                    .Select(text => text.text));
        }

        private static void CompleteOneMicroChallengeCycle(object loop)
        {
            Invoke(loop, "ChallengeReady");
            Invoke(loop, "OpponentFinishedSpeaking");
            Invoke(loop, "DiagnosisCompleted");
            Invoke(loop, "CompleteCoaching");
            Assert.AreEqual("RevisionResponse", GetProperty(loop, "Step").ToString());
            Invoke(loop, "ConfirmRevision");
            Assert.AreEqual("EvaluatingRevision", GetProperty(loop, "Step").ToString());
            Invoke(loop, "RevisionEvaluationCompleted");
        }

        private static object GetProperty(object instance, string name)
        {
            PropertyInfo property = instance.GetType().GetProperty(
                name, BindingFlags.Public | BindingFlags.Instance);
            Assert.IsNotNull(property, name);
            return property.GetValue(instance);
        }

        private static void Invoke(object instance, string name)
        {
            MethodInfo method = instance.GetType().GetMethod(
                name, BindingFlags.Public | BindingFlags.Instance);
            Assert.IsNotNull(method, name);
            method.Invoke(instance, null);
        }
    }
}
