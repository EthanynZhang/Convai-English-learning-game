using System;
using System.IO;
using System.Linq;
using Game.Debate;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace Game.Tests.EditMode
{
    public class ThreeModeCoachAgentTests
    {
        [TearDown]
        public void TearDown()
        {
            CoachStudySessionContext.Clear();
        }

        [Test]
        public void PolicyConfigurationMatchesRegisteredExperimentDefaults()
        {
            CoachPolicyConfig config = CoachPolicyConfig.CreateDefault();

            Assert.AreEqual(1, config.MaxEpisodesPerCycle);
            Assert.AreEqual(int.MaxValue, config.MaxCoachTurnsPerEpisode);
            Assert.AreEqual(90f, config.MaxEpisodeSeconds);
            Assert.AreEqual(2, config.TriggerSeverityThreshold);
            Assert.AreEqual(0.65f, config.TriggerConfidenceThreshold);
            Assert.AreEqual(15f, config.AiLedActionWindowSeconds);
            Assert.AreEqual(60f, config.LearnerSpeechMinimumSeconds);
            Assert.AreEqual(90f, config.LearnerSpeechMaximumSeconds);
        }

        [Test]
        public void LearnerLedMakesDiagnosisAvailableWithoutAutoStarting()
        {
            CoachOrchestrationPolicy policy = new(CoachPolicyConfig.CreateDefault());

            CoachPolicyDecision decision = policy.Evaluate(CreateDiagnosingInput(
                CoachOrchestrationMode.LearnerLed,
                severity: 3,
                confidence: 0.95f));

            Assert.AreEqual(CoachPolicyAction.MakeAvailable, decision.Action);
            Assert.AreEqual(CoachEpisodeState.Available, decision.NextState);
            Assert.AreEqual(ControlOwner.Learner, decision.StartAuthority);
        }

        [Test]
        public void SharedControlInvitesAtSeverityThresholdButRequiresConfirmation()
        {
            CoachOrchestrationPolicy policy = new(CoachPolicyConfig.CreateDefault());

            CoachPolicyDecision decision = policy.Evaluate(CreateDiagnosingInput(
                CoachOrchestrationMode.SharedControl,
                severity: 2,
                confidence: 0.20f));

            Assert.AreEqual(CoachPolicyAction.Invite, decision.Action);
            Assert.AreEqual(CoachEpisodeState.Invited, decision.NextState);
            Assert.AreEqual(ControlOwner.Shared, decision.StartAuthority);
        }

        [Test]
        public void AiLedRequiresBothSeverityAndConfidenceThresholds()
        {
            CoachOrchestrationPolicy policy = new(CoachPolicyConfig.CreateDefault());

            CoachPolicyDecision lowConfidence = policy.Evaluate(CreateDiagnosingInput(
                CoachOrchestrationMode.AiLed,
                severity: 3,
                confidence: 0.64f));
            CoachPolicyDecision eligible = policy.Evaluate(CreateDiagnosingInput(
                CoachOrchestrationMode.AiLed,
                severity: 2,
                confidence: 0.65f));

            Assert.AreEqual(CoachPolicyAction.End, lowConfidence.Action);
            Assert.AreEqual(CoachTerminationReason.NoEligibleIssue, lowConfidence.TerminationReason);
            Assert.AreEqual(CoachPolicyAction.AutoStart, eligible.Action);
            Assert.AreEqual(CoachEpisodeState.Active, eligible.NextState);
            Assert.AreEqual(ControlOwner.Coach, eligible.StartAuthority);
        }

        [Test]
        public void AiLedBelowThresholdLogsOpportunityWithoutCreatingEpisodeSummary()
        {
            string directory = Path.Combine(Path.GetTempPath(), "coach-no-episode-" + Guid.NewGuid().ToString("N"));
            UnityEngine.GameObject gameObject = new("Episode Test");
            try
            {
                CoachStudySessionContext.Initialize("P01", CoachOrchestrationMode.AiLed, "Practice", "Transfer");
                CoachResearchLogger logger = new(directory);
                CoachEpisodeController episode = gameObject.AddComponent<CoachEpisodeController>();
                episode.Configure(CoachOrchestrationMode.AiLed, 1, CoachPolicyConfig.CreateDefault(), logger);

                CoachPolicyDecision decision = episode.BeginOpportunity(
                    CreateDiagnosingInput(CoachOrchestrationMode.AiLed, 3, 0.64f).Diagnosis,
                    "Confirmed learner argument",
                    "reading_vs_speaking",
                    1);

                Assert.AreEqual(CoachPolicyAction.End, decision.Action);
                Assert.AreEqual(string.Empty, episode.CoachEpisodeId);
                Assert.IsTrue(File.Exists(logger.EventLogPath));
                Assert.IsFalse(File.Exists(logger.EpisodeSummaryPath));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(gameObject);
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        }

        [Test]
        public void AiLedHighSeverityUsesSecondTurnBeforeDeferringPractice()
        {
            CoachOrchestrationPolicy policy = new(CoachPolicyConfig.CreateDefault());
            CoachPolicyInput input = CreateDiagnosingInput(CoachOrchestrationMode.AiLed, 3, 0.9f);
            input.EpisodeState = CoachEpisodeState.AwaitingLearnerAction;
            input.ActionWindowExpired = true;
            input.CoachTurnIndex = 1;

            CoachPolicyDecision decision = policy.Evaluate(input);

            Assert.AreEqual(CoachPolicyAction.Continue, decision.Action);
            Assert.AreEqual(CoachEpisodeState.Active, decision.NextState);
            Assert.AreNotEqual(CoachTerminationReason.TargetResolved, decision.TerminationReason);
        }

        [Test]
        public void SpeechWindowPreventsSubmissionBeforeSixtySecondsAndStopsAtNinety()
        {
            CoachPolicyConfig config = CoachPolicyConfig.CreateDefault();

            Assert.IsFalse(CoachSpeechWindow.CanSubmit(59.99f, config));
            Assert.IsTrue(CoachSpeechWindow.CanSubmit(60f, config));
            Assert.IsFalse(CoachSpeechWindow.ShouldAutoStop(89.99f, config));
            Assert.IsTrue(CoachSpeechWindow.ShouldAutoStop(90f, config));
        }

        [Test]
        public void SessionContextLocksAnonymousParticipantAndModeAcrossScenes()
        {
            CoachStudySessionSnapshot snapshot = CoachStudySessionContext.Initialize(
                " P014 ",
                CoachOrchestrationMode.SharedControl,
                "Reading vs Speaking",
                "Reading vs Speaking");

            Assert.IsTrue(CoachStudySessionContext.IsInitialized);
            Assert.AreEqual("P014", snapshot.ParticipantId);
            Assert.AreEqual(CoachOrchestrationMode.SharedControl, snapshot.Mode);
            Assert.IsNotEmpty(snapshot.SessionId);
            Assert.IsTrue(snapshot.PilotUsesSameTransferTopic);
            Assert.AreSame(snapshot, CoachStudySessionContext.Current);
        }

        [Test]
        public void DiagnosisPromptIsConditionBlindAndRequestsFixedLabels()
        {
            CoachDiagnosisRequest request = new()
            {
                Topic = "Reading vs Speaking",
                LearnerSide = "Speaking is more important.",
                PlayerUtteranceText = "Speaking helps people communicate.",
                PracticeCycleId = 1,
                TurnId = 1
            };

            string prompt = CoachDiagnosisEngine.BuildPrompt(request);

            StringAssert.Contains("diagnosis_severity", prompt);
            StringAssert.Contains("diagnosis_confidence", prompt);
            StringAssert.Contains("recommended_focus", prompt);
            StringAssert.DoesNotContain("orchestration_mode", prompt);
            StringAssert.DoesNotContain("LearnerLed", prompt);
            StringAssert.DoesNotContain("SharedControl", prompt);
            StringAssert.DoesNotContain("AiLed", prompt);
        }

        [Test]
        public void EpisodeDoesNotStartUntilLearnerLedFocusIsConfirmed()
        {
            UnityEngine.GameObject gameObject = new("Episode Test");
            try
            {
                CoachEpisodeController episode = gameObject.AddComponent<CoachEpisodeController>();
                episode.Configure(CoachOrchestrationMode.LearnerLed, 1, CoachPolicyConfig.CreateDefault(), null);

                CoachPolicyDecision available = episode.BeginOpportunity(
                    CreateDiagnosingInput(CoachOrchestrationMode.LearnerLed, 2, 0.8f).Diagnosis,
                    "Confirmed learner argument",
                    "reading_vs_speaking",
                    1);

                Assert.AreEqual(CoachEpisodeState.Available, episode.State);
                Assert.IsFalse(episode.IsActive);
                Assert.AreEqual(CoachPolicyAction.MakeAvailable, available.Action);

                CoachPolicyDecision started = episode.SubmitLearnerAction(
                    CoachLearnerAction.ConfirmFocus,
                    "Logos");

                Assert.AreEqual(CoachPolicyAction.Start, started.Action);
                Assert.AreEqual(CoachEpisodeState.Active, episode.State);
                Assert.AreEqual("Logos", episode.ConfirmedFocus);
                Assert.IsTrue(episode.IsActive);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void EpisodeCountsFeedbackTurnsWithoutApplyingATurnLimit()
        {
            UnityEngine.GameObject gameObject = new("Episode Test");
            try
            {
                CoachEpisodeController episode = gameObject.AddComponent<CoachEpisodeController>();
                episode.Configure(CoachOrchestrationMode.AiLed, 1, CoachPolicyConfig.CreateDefault(), null);
                episode.BeginOpportunity(
                    CreateDiagnosingInput(CoachOrchestrationMode.AiLed, 3, 0.9f).Diagnosis,
                    "Confirmed learner argument",
                    "reading_vs_speaking",
                    1);

                episode.NotifyFeedbackPresented("Level2", "Explanation", "Short feedback.");
                Assert.AreEqual(1, episode.FeedbackTurnIndex);
                Assert.AreEqual(CoachEpisodeState.AwaitingLearnerAction, episode.State);

                CoachPolicyDecision replay = episode.SubmitLearnerAction(CoachLearnerAction.Replay, string.Empty);
                Assert.AreEqual(CoachPolicyAction.Replay, replay.Action);
                Assert.AreEqual(1, episode.FeedbackTurnIndex);

                CoachPolicyDecision example = episode.SubmitLearnerAction(CoachLearnerAction.NeedExample, string.Empty);
                Assert.AreEqual(CoachPolicyAction.Continue, example.Action);
                episode.NotifyFeedbackPresented("Level3", "Example", "For example, ...");
                Assert.AreEqual(2, episode.FeedbackTurnIndex);

                CoachPolicyDecision third = episode.SubmitLearnerAction(CoachLearnerAction.NeedExample, string.Empty);
                Assert.AreEqual(CoachPolicyAction.Continue, third.Action);
                Assert.AreEqual(CoachEpisodeState.Active, episode.State);
                Assert.AreEqual(CoachTerminationReason.None, episode.TerminationReason);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void NeedExampleKeepsTheLearnersPreviouslyConfirmedFocus()
        {
            UnityEngine.GameObject gameObject = new("Episode Test");
            try
            {
                CoachEpisodeController episode = gameObject.AddComponent<CoachEpisodeController>();
                episode.Configure(CoachOrchestrationMode.LearnerLed, 1, CoachPolicyConfig.CreateDefault(), null);
                episode.BeginOpportunity(
                    CreateDiagnosingInput(CoachOrchestrationMode.LearnerLed, 3, 0.9f).Diagnosis,
                    "Confirmed learner argument",
                    "reading_vs_speaking",
                    1);
                episode.SubmitLearnerAction(CoachLearnerAction.ConfirmFocus, "Pathos");
                episode.NotifyFeedbackPresented("Level2", "Strategy", "Short feedback.");

                CoachPolicyDecision example = episode.SubmitLearnerAction(
                    CoachLearnerAction.NeedExample,
                    string.Empty);

                Assert.AreEqual(CoachPolicyAction.Continue, example.Action);
                Assert.AreEqual("Pathos", episode.ConfirmedFocus);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void EpisodeTimeoutAndRepeatedAdvanceRaiseCompletionOnlyOnce()
        {
            UnityEngine.GameObject gameObject = new("Episode Test");
            try
            {
                CoachEpisodeController episode = gameObject.AddComponent<CoachEpisodeController>();
                episode.Configure(CoachOrchestrationMode.AiLed, 1, CoachPolicyConfig.CreateDefault(), null);
                int completionCount = 0;
                episode.EpisodeEnded += _ => completionCount++;
                episode.BeginOpportunity(
                    CreateDiagnosingInput(CoachOrchestrationMode.AiLed, 3, 0.9f).Diagnosis,
                    "Confirmed learner argument",
                    "reading_vs_speaking",
                    1);

                episode.Advance(90f);
                episode.Advance(90f);

                Assert.AreEqual(CoachEpisodeState.TimedOut, episode.State);
                Assert.AreEqual(CoachTerminationReason.TimeLimit, episode.TerminationReason);
                Assert.AreEqual(0f, episode.RemainingSeconds);
                Assert.AreEqual(1, completionCount);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void AiOverrideStillTimesOutAtTheEpisodeBudget()
        {
            UnityEngine.GameObject gameObject = new("Episode Test");
            try
            {
                CoachEpisodeController episode = gameObject.AddComponent<CoachEpisodeController>();
                episode.Configure(CoachOrchestrationMode.AiLed, 1, CoachPolicyConfig.CreateDefault(), null);
                episode.BeginOpportunity(
                    CreateDiagnosingInput(CoachOrchestrationMode.AiLed, 3, 0.9f).Diagnosis,
                    "Confirmed learner argument",
                    "reading_vs_speaking",
                    1);
                episode.NotifyFeedbackPresented("Level2", "Explanation", "Short feedback.");
                episode.SubmitLearnerAction(CoachLearnerAction.Override, "Pathos");

                episode.Advance(90f);

                Assert.AreEqual(CoachEpisodeState.TimedOut, episode.State);
                Assert.AreEqual(CoachTerminationReason.TimeLimit, episode.TerminationReason);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void AiLedActionWindowCanContinueAfterTheSecondFeedbackTurn()
        {
            UnityEngine.GameObject gameObject = new("Episode Test");
            try
            {
                CoachEpisodeController episode = gameObject.AddComponent<CoachEpisodeController>();
                CoachPolicyConfig config = CoachPolicyConfig.CreateDefault();
                episode.Configure(CoachOrchestrationMode.AiLed, 1, config, null);
                episode.BeginOpportunity(
                    CreateDiagnosingInput(CoachOrchestrationMode.AiLed, 3, 0.9f).Diagnosis,
                    "Confirmed learner argument",
                    "reading_vs_speaking",
                    1);
                episode.NotifyFeedbackPresented("Level2", "Explanation", "Short feedback.");

                episode.Advance(config.AiLedActionWindowSeconds);

                Assert.AreEqual(CoachEpisodeState.Active, episode.State);
                episode.NotifyFeedbackPresented("Level2", "Explanation", "One final focused step.");
                episode.Advance(config.AiLedActionWindowSeconds);

                Assert.AreEqual(CoachEpisodeState.Active, episode.State);
                Assert.AreEqual(CoachTerminationReason.None, episode.TerminationReason);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void SafetyCleanupCompletesAnAvailableButNotYetActivatedOpportunity()
        {
            UnityEngine.GameObject gameObject = new("Episode Test");
            try
            {
                CoachEpisodeController episode = gameObject.AddComponent<CoachEpisodeController>();
                episode.Configure(CoachOrchestrationMode.LearnerLed, 1, CoachPolicyConfig.CreateDefault(), null);
                episode.BeginOpportunity(
                    CreateDiagnosingInput(CoachOrchestrationMode.LearnerLed, 2, 0.8f).Diagnosis,
                    "Confirmed learner argument",
                    "reading_vs_speaking",
                    1);

                Assert.IsTrue(episode.HasOpenOpportunity);
                episode.Complete(CoachTerminationReason.SafetyOverride, ControlOwner.SystemSafety);

                Assert.IsFalse(episode.HasOpenOpportunity);
                Assert.AreEqual(CoachEpisodeState.Completed, episode.State);
                Assert.AreEqual(CoachTerminationReason.SafetyOverride, episode.TerminationReason);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void ResearchLoggerWritesEventAndSummaryHeadersWithCsvEscaping()
        {
            string directory = Path.Combine(Path.GetTempPath(), "coach-research-" + Guid.NewGuid().ToString("N"));
            try
            {
                CoachResearchLogger logger = new(directory);
                logger.LogEvent(new CoachEventRecord
                {
                    ParticipantId = "P,01",
                    SessionId = "S01",
                    OrchestrationMode = CoachOrchestrationMode.LearnerLed,
                    EventType = "FeedbackShown",
                    ConfirmedLearnerText = "one, \"two\"\nthree"
                });
                logger.CompleteEpisode(new CoachEpisodeSummary
                {
                    ParticipantId = "P,01",
                    SessionId = "S01",
                    OrchestrationMode = CoachOrchestrationMode.LearnerLed,
                    CoachEpisodeId = "E01"
                });

                string events = File.ReadAllText(logger.EventLogPath);
                string summaries = File.ReadAllText(logger.EpisodeSummaryPath);
                StringAssert.Contains("participant_id,session_id,orchestration_mode", events);
                StringAssert.Contains("policy_version", events);
                StringAssert.Contains("\"P,01\"", events);
                StringAssert.Contains("\"one, \"\"two\"\"\nthree\"", events);
                StringAssert.Contains("participant_id,session_id,orchestration_mode", summaries);
                StringAssert.Contains("termination_reason", summaries);
                StringAssert.Contains("diagnosis_model_version,feedback_model_version,policy_version", summaries);
                Assert.IsEmpty(logger.Errors);
            }
            finally
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, true);
                }
            }
        }

        [Test]
        public void ResearchLoggerQueuesFailedWritesAndFlushesThemLater()
        {
            string blocker = Path.Combine(Path.GetTempPath(), "coach-log-blocker-" + Guid.NewGuid().ToString("N"));
            try
            {
                File.WriteAllText(blocker, "temporarily blocks directory creation");
                CoachResearchLogger logger = new(blocker);
                LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex(
                    "Coach research log write failed:"));
                logger.LogEvent(new CoachEventRecord { ParticipantId = "P01", EventType = "Queued" });
                Assert.AreEqual(1, logger.PendingWriteCount);

                File.Delete(blocker);
                Directory.CreateDirectory(blocker);
                logger.Flush();

                Assert.AreEqual(0, logger.PendingWriteCount);
                Assert.IsTrue(File.Exists(logger.EventLogPath));
                StringAssert.Contains("Queued", File.ReadAllText(logger.EventLogPath));
            }
            finally
            {
                if (File.Exists(blocker)) File.Delete(blocker);
                if (Directory.Exists(blocker)) Directory.Delete(blocker, true);
            }
        }

        [Test]
        public void FeedbackShownEventKeepsSharedControlOwnershipFields()
        {
            string directory = Path.Combine(Path.GetTempPath(), "coach-ownership-" + Guid.NewGuid().ToString("N"));
            UnityEngine.GameObject gameObject = new("Episode Test");
            try
            {
                CoachStudySessionContext.Initialize(
                    "P01",
                    CoachOrchestrationMode.SharedControl,
                    "Practice",
                    "Transfer");
                CoachResearchLogger logger = new(directory);
                CoachEpisodeController episode = gameObject.AddComponent<CoachEpisodeController>();
                episode.Configure(CoachOrchestrationMode.SharedControl, 1, CoachPolicyConfig.CreateDefault(), logger);
                episode.BeginOpportunity(
                    CreateDiagnosingInput(CoachOrchestrationMode.SharedControl, 2, 0.8f).Diagnosis,
                    "Confirmed learner argument",
                    "reading_vs_speaking",
                    1);
                episode.SubmitLearnerAction(CoachLearnerAction.Accept, string.Empty);
                episode.NotifyFeedbackPresented("Level2", "Explanation", "Short feedback.");

                string[] lines = File.ReadAllLines(logger.EventLogPath);
                string[] headers = lines[0].Split(',');
                string[] values = lines[lines.Length - 1].Split(',');
                Assert.AreEqual("FeedbackShown", values[Array.IndexOf(headers, "event_type")]);
                Assert.AreEqual("Shared", values[Array.IndexOf(headers, "start_authority")]);
                Assert.AreEqual("Shared", values[Array.IndexOf(headers, "agenda_owner")]);
                Assert.AreEqual("Shared", values[Array.IndexOf(headers, "pacing_owner")]);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(gameObject);
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        }

        [Test]
        public void FeedbackPromptUsesConfirmedDiagnosisButNeverReceivesMode()
        {
            CoachFeedbackRequest request = new()
            {
                Topic = "Reading vs Speaking",
                PlayerSide = "Speaking is more important.",
                PlayerUtteranceText = "Speaking helps learners communicate.",
                ConfirmedFocus = "Explanation",
                DiagnosisIssueCode = "CREEI_EXPLANATION_WEAK",
                RecommendedStrategy = "Logos",
                TargetSuccessCriterion = "Connect the example to the claim.",
                FeedbackLevel = CoachFeedbackLevel.Level2
            };

            string prompt = DebateCoachFeedbackGenerator.BuildPrompt(request);

            StringAssert.Contains("confirmed_focus: Explanation", prompt);
            StringAssert.Contains("diagnosis_issue_code: CREEI_EXPLANATION_WEAK", prompt);
            StringAssert.Contains("target_success_criterion", prompt);
            StringAssert.DoesNotContain("condition:", prompt.ToLowerInvariant());
            StringAssert.DoesNotContain("orchestration_mode", prompt.ToLowerInvariant());
        }

        [Test]
        public void ExperimentViewOffersRegisteredFocusChoices()
        {
            CollectionAssert.AreEquivalent(
                new[]
                {
                    "Claim", "Reason", "Evidence", "Explanation", "Impact",
                    "Ethos", "Pathos", "Logos"
                },
                CoachExperimentView.FocusOptions);
        }

        [Test]
        public void DiagnosisSchemaAndPolicyRestrictFocusToRegisteredCatalog()
        {
            JObject schema = CoachDiagnosisEngine.BuildStructuredOutputSchema();
            string[] schemaFocuses = ((JArray)schema["properties"]?["recommended_focus"]?["enum"])
                ?.ToObject<string[]>() ?? Array.Empty<string>();
            CollectionAssert.AreEquivalent(CoachFocusCatalog.Values, schemaFocuses);

            CoachPolicyInput input = CreateDiagnosingInput(CoachOrchestrationMode.SharedControl, 2, 0.9f);
            input.Diagnosis.RecommendedFocus = "invented-focus";
            CoachPolicyDecision decision = new CoachOrchestrationPolicy(CoachPolicyConfig.CreateDefault())
                .Evaluate(input);

            Assert.AreEqual("Explanation", decision.ProposedFocus);
            Assert.IsFalse(CoachFocusCatalog.TryNormalize("invented-focus", out _));
        }

        [Test]
        public void LevelThreeRhetoricalFocusUsesRhetoricalExampleTask()
        {
            CoachFeedbackRequest request = new()
            {
                Topic = "Reading vs Speaking",
                PlayerSide = "Speaking is more important.",
                PlayerUtteranceText = "Speaking helps learners communicate.",
                CurrentCreeiStage = "Explanation",
                ConfirmedFocus = "Pathos",
                DiagnosisIssueCode = "RHETORICAL_PATHOS_WEAK",
                FeedbackLevel = CoachFeedbackLevel.Level3,
                DetailedJson = true
            };

            string prompt = DebateCoachFeedbackGenerator.BuildPrompt(request);

            StringAssert.Contains("Confirmed rhetorical focus: Pathos", prompt);
            StringAssert.Contains("audience consequence", prompt);
            StringAssert.DoesNotContain("Current CREEI stage: Explanation", prompt);
        }

        [Test]
        public void EpisodeSummaryPreservesInitialControlAndLearnerFocusOverride()
        {
            string directory = Path.Combine(Path.GetTempPath(), "coach-summary-owner-" + Guid.NewGuid().ToString("N"));
            GameObject gameObject = new("Episode Test");
            try
            {
                CoachStudySessionContext.Initialize("P01", CoachOrchestrationMode.SharedControl, "Practice", "Transfer");
                CoachResearchLogger logger = new(directory);
                CoachEpisodeController episode = gameObject.AddComponent<CoachEpisodeController>();
                episode.Configure(CoachOrchestrationMode.SharedControl, 1, CoachPolicyConfig.CreateDefault(), logger);
                episode.BeginOpportunity(
                    CreateDiagnosingInput(CoachOrchestrationMode.SharedControl, 2, 0.8f).Diagnosis,
                    "Confirmed learner argument", "reading_vs_speaking", 1);
                episode.SubmitLearnerAction(CoachLearnerAction.Accept, string.Empty);
                episode.NotifyFeedbackPresented("Level2", "Explanation", "Short feedback.");
                episode.SubmitLearnerAction(CoachLearnerAction.ChangeFocus, "Pathos");
                episode.NotifyFeedbackPresented("Level2", "Pathos", "Revised feedback.");
                episode.SubmitLearnerAction(CoachLearnerAction.ApplyNextCycle, string.Empty);

                string[] lines = File.ReadAllLines(logger.EpisodeSummaryPath);
                string[] headers = lines[0].Split(',');
                string[] values = lines[1].Split(',');
                Assert.AreEqual("Shared", values[Array.IndexOf(headers, "start_authority")]);
                Assert.AreEqual("Learner", values[Array.IndexOf(headers, "agenda_owner")]);
                Assert.AreEqual("Shared", values[Array.IndexOf(headers, "pacing_owner")]);
                Assert.AreEqual("Learner", values[Array.IndexOf(headers, "termination_owner")]);
                Assert.AreEqual("Pathos", values[Array.IndexOf(headers, "final_confirmed_focus")]);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(gameObject);
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        }

        [Test]
        public void NeutralResultEndsAsNoEligibleIssueAndLogsApplyAction()
        {
            string directory = Path.Combine(Path.GetTempPath(), "coach-neutral-" + Guid.NewGuid().ToString("N"));
            GameObject gameObject = new("Episode Test");
            try
            {
                CoachStudySessionContext.Initialize("P01", CoachOrchestrationMode.LearnerLed, "Practice", "Transfer");
                CoachResearchLogger logger = new(directory);
                CoachEpisodeController episode = gameObject.AddComponent<CoachEpisodeController>();
                episode.Configure(CoachOrchestrationMode.LearnerLed, 1, CoachPolicyConfig.CreateDefault(), logger);
                episode.BeginOpportunity(new CoachDiagnosisResult
                {
                    Success = true,
                    Severity = 0,
                    Confidence = 0.9f,
                    RecommendedFocus = "Explanation"
                }, "Confirmed learner argument", "reading_vs_speaking", 1);

                CoachPolicyDecision decision = episode.SubmitLearnerAction(
                    CoachLearnerAction.ApplyNextCycle, string.Empty);

                Assert.AreEqual(CoachTerminationReason.NoEligibleIssue, decision.TerminationReason);
                string[] lines = File.ReadAllLines(logger.EventLogPath);
                string[] headers = lines[0].Split(',');
                string[] values = lines[lines.Length - 1].Split(',');
                Assert.AreEqual("ApplyNextCycle", values[Array.IndexOf(headers, "learner_control_action")]);
                Assert.AreEqual("NoEligibleIssue", values[Array.IndexOf(headers, "termination_reason")]);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(gameObject);
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        }

        [Test]
        public void LearnerLedFocusChoiceDoesNotRevealAiSuggestion()
        {
            string source = File.ReadAllText("Assets/Game/Scripts/SharedInitiativeOrchestrationController.cs");
            StringAssert.Contains("_orchestrationMode == CoachOrchestrationMode.SharedControl", source);
            StringAssert.Contains("experimentView?.SetSelectedFocus(_practiceDiagnosis?.RecommendedFocus)", source);
            StringAssert.Contains("Choose the CREEI or rhetorical focus you want Coach to address", source);
        }

        [Test]
        public void MultilineTranscriptViewDeclaresViewportMaskAndScrollbar()
        {
            string source = File.ReadAllText("Assets/Game/Scripts/CoachExperimentView.cs");
            StringAssert.Contains("typeof(RectMask2D)", source);
            StringAssert.Contains("input.textViewport = viewportRect", source);
            StringAssert.Contains("input.verticalScrollbar = CreateVerticalScrollbar", source);
        }

        [Test]
        public void TechnicalTranscriptionFallbackStillEnforcesSpeechMinimum()
        {
            string source = File.ReadAllText("Assets/Game/Scripts/SharedInitiativeOrchestrationController.cs");
            StringAssert.Contains("Transcription is unavailable. Keep speaking for 01:00–01:30", source);
            StringAssert.Contains("Keep speaking until at least 01:00", source);
            StringAssert.Contains("CoachSpeechWindow.CanSubmit(_practiceCycleDurationSeconds", source);
        }

        [Test]
        public void TechnicalRecoveryEventsRetainRegisteredLearnerActions()
        {
            string source = File.ReadAllText("Assets/Game/Scripts/SharedInitiativeOrchestrationController.cs");
            StringAssert.Contains("CoachLearnerAction.RetryDiagnosis", source);
            StringAssert.Contains("LearnerControlAction = CoachLearnerAction.SkipCycle.ToString()", source);
            StringAssert.Contains("CoachLearnerAction.RetryFeedback", source);
        }

        [Test]
        public void FeedbackFailureHasAnExplicitRetryAction()
        {
            Assert.IsTrue(Enum.IsDefined(typeof(CoachLearnerAction), "RetryFeedback"));
        }

        [Test]
        public void OrchestrationDeclaresThreeSoloPracticeCyclePhases()
        {
            Assert.AreEqual(3, SharedInitiativeOrchestrationController.PracticeCycleCount);
            Assert.IsTrue(Enum.IsDefined(typeof(OrchestrationPhase), "PracticeCycleReady"));
            Assert.IsTrue(Enum.IsDefined(typeof(OrchestrationPhase), "PracticeCycleSpeaking"));
            Assert.IsTrue(Enum.IsDefined(typeof(OrchestrationPhase), "PracticeCycleConfirmingTranscript"));
            Assert.IsTrue(Enum.IsDefined(typeof(OrchestrationPhase), "PracticeCycleDiagnosing"));
        }

        [Test]
        public void SceneBackupIsExcludedAndTransferSceneIsEnabledInBuildSettings()
        {
            const string backup = "Assets/Game/Scenes/backup/04 coach Agent pre-three-mode 2026-07-15.unity";
            const string transfer = "Assets/Game/Scenes/05Level_PlayerVsNPCDebate 1.unity";

            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<SceneAsset>(backup));
            EditorBuildSettingsScene[] scenes = EditorBuildSettings.scenes;
            Assert.IsFalse(Array.Exists(scenes, scene => scene.path == backup));
            Assert.IsTrue(Array.Exists(scenes, scene => scene.enabled && scene.path == transfer));
        }

        [Test]
        public void SceneContractsWireThreeStageCoachOnlyIn04AndKeep05Disabled()
        {
            const string scene04Path = "Assets/Game/Scenes/04 coach Agent.unity";
            const string scene05Path = "Assets/Game/Scenes/05Level_PlayerVsNPCDebate 1.unity";
            string scene04 = File.ReadAllText(scene04Path);
            string scene05 = File.ReadAllText(scene05Path);
            string oldControllerGuid = AssetDatabase.AssetPathToGUID(
                "Assets/Game/Scripts/SharedInitiativeOrchestrationController.cs");
            string oldViewGuid = AssetDatabase.AssetPathToGUID(
                "Assets/Game/Scripts/CoachExperimentView.cs");
            string episodeGuid = AssetDatabase.AssetPathToGUID(
                "Assets/Game/Scripts/CoachEpisodeController.cs");
            string controllerGuid = AssetDatabase.AssetPathToGUID(
                "Assets/Game/Scripts/ThreeStageDebatePracticeController.cs");
            string viewGuid = AssetDatabase.AssetPathToGUID(
                "Assets/Game/Scripts/ThreeStageDebatePracticeView.cs");
            string transferGuid = AssetDatabase.AssetPathToGUID(
                "Assets/Game/Scripts/TransferDebateStageGuard.cs");

            StringAssert.Contains(controllerGuid, scene04);
            StringAssert.Contains(viewGuid, scene04);
            StringAssert.Contains(episodeGuid, scene04);
            StringAssert.DoesNotContain(oldControllerGuid, scene04);
            StringAssert.DoesNotContain(oldViewGuid, scene04);
            StringAssert.DoesNotContain("studyView: {fileID: 0}", scene04);

            StringAssert.Contains(transferGuid, scene05);
            StringAssert.DoesNotContain(controllerGuid, scene05);
            StringAssert.DoesNotContain(viewGuid, scene05);
            StringAssert.DoesNotContain(oldControllerGuid, scene05);
            StringAssert.DoesNotContain(oldViewGuid, scene05);
            StringAssert.DoesNotContain(episodeGuid, scene05);
        }

        [Test]
        public void Scene04DetailedFeedbackFormatRequiresTwoSectionsAndFourToSixShortSentences()
        {
            Type formatType = typeof(CoachFeedbackRequest).Assembly.GetType(
                "Game.Debate.CoachFeedbackFormat");
            Assert.IsNotNull(formatType);
            CoachFeedbackRequest request = new()
            {
                PlayerUtteranceText =
                    "Speaking helps shy learners because they can practise with classmates.",
                LearnerRequest = "Please help me explain the effect on shy learners.",
                ConfirmedFocus = "Explanation",
                DiagnosisIssueCode = "CREEI_EXPLANATION_WEAK",
                TargetSuccessCriterion = "Connect the example to the claim.",
                FeedbackLevel = CoachFeedbackLevel.Level2
            };
            SetPublicField(request, "FeedbackFormat",
                Enum.Parse(formatType, "Scene04CreeiDetailed"));
            SetPublicField(request, "CreeiMissingOrWeakComponents",
                new[] { "Explanation", "Impact" });
            SetPublicField(request, "CreeiGapSummary",
                "The explanation and impact are underdeveloped.");

            string prompt = DebateCoachFeedbackGenerator.BuildPrompt(request);

            StringAssert.Contains("CREEI gaps:", prompt);
            StringAssert.Contains("Specific advice:", prompt);
            StringAssert.Contains("4 to 6 short sentences", prompt);
            StringAssert.Contains("1 to 2 short sentences", prompt);
            StringAssert.Contains("3 to 4 short sentences", prompt);
            StringAssert.Contains("Quote one short exact phrase from the learner", prompt);
            StringAssert.Contains("adaptable revision example", prompt);
            StringAssert.Contains("creei_missing_or_weak_components: Explanation | Impact", prompt);
            StringAssert.Contains(
                "creei_gap_summary: The explanation and impact are underdeveloped.", prompt);
            StringAssert.DoesNotContain("orchestration_mode", prompt.ToLowerInvariant());
            StringAssert.DoesNotContain("condition:", prompt.ToLowerInvariant());

            string defaultPrompt = DebateCoachFeedbackGenerator.BuildPrompt(
                new CoachFeedbackRequest
                {
                    PlayerUtteranceText = request.PlayerUtteranceText,
                    FeedbackLevel = CoachFeedbackLevel.Level2
                });
            StringAssert.Contains("three short sentences", defaultPrompt);
            StringAssert.DoesNotContain("CREEI gaps:", defaultPrompt);
        }

        [Test]
        public void Scene04DetailedFeedbackValidatorRejectsMissingSectionsOrWrongLength()
        {
            var method = typeof(DebateCoachFeedbackGenerator).GetMethod(
                "IsDetailedCreeiFeedbackText");
            Assert.IsNotNull(method);
            const string valid =
                "CREEI gaps: Your claim and reason are present, but the explanation is weak. " +
                "Impact is also missing.\nSpecific advice: You said speaking helps shy learners. " +
                "Explain how practice changes their confidence. Add a consequence such as joining class discussions.";
            Assert.IsTrue((bool)method.Invoke(null, new object[] { valid }));
            Assert.IsFalse((bool)method.Invoke(null, new object[]
            {
                "Your explanation is weak. Add one consequence."
            }));
            Assert.IsFalse((bool)method.Invoke(null, new object[]
            {
                "CREEI gaps: Explanation is weak. Specific advice: Add a consequence."
            }));
        }

        [Test]
        public void DiagnosisReturnsAConditionBlindWholeCreeiGapSummary()
        {
            JObject schema = CoachDiagnosisEngine.BuildStructuredOutputSchema();
            JObject properties = (JObject)schema["properties"];
            JArray required = (JArray)schema["required"];
            Assert.IsNotNull(properties["creei_missing_or_weak_components"]);
            Assert.IsNotNull(properties["creei_gap_summary"]);
            CollectionAssert.Contains(required.Select(token => (string)token).ToArray(),
                "creei_missing_or_weak_components");
            CollectionAssert.Contains(required.Select(token => (string)token).ToArray(),
                "creei_gap_summary");
            string prompt = CoachDiagnosisEngine.BuildPrompt(new CoachDiagnosisRequest
            {
                PlayerUtteranceText = "Speaking is useful because students need practice."
            });
            StringAssert.Contains("creei_missing_or_weak_components", prompt);
            StringAssert.Contains("creei_gap_summary", prompt);
            StringAssert.Contains("Claim, Reason, Evidence, Explanation, and Impact", prompt);
            StringAssert.DoesNotContain("orchestration_mode", prompt.ToLowerInvariant());

            CoachDiagnosisResult parsed = CoachDiagnosisEngine.ParseStructuredResult(@"{
              'diagnosis_issue_code':'CREEI_EXPLANATION_WEAK',
              'strong_component':'Claim','weak_component':'Explanation',
              'dominant_strategy':'Logos','recommended_strategy':'Logos',
              'evidence_quality':'general','reasoning_connection':'unclear',
              'diagnosis_severity':2,'diagnosis_confidence':0.85,
              'recommended_focus':'Explanation',
              'recommended_next_action':'Connect the evidence to the claim.',
              'revision_improvement_status':'NotApplicable',
              'revision_improvement_summary':'',
              'creei_missing_or_weak_components':['Explanation','Impact'],
              'creei_gap_summary':'The explanation and impact are underdeveloped.',
              'ranked_suggestions':[] }".Replace('\'', '"'));
            CollectionAssert.AreEqual(new[] { "Explanation", "Impact" },
                (string[])ReadPublicField(parsed, "CreeiMissingOrWeakComponents"));
            Assert.AreEqual("The explanation and impact are underdeveloped.",
                ReadPublicField(parsed, "CreeiGapSummary"));
        }

        [Test]
        public void AgendaAndCreeiGapFieldsAreVersionedAndDeclaredInTheCoachEventCsv()
        {
            CoachPolicyConfig config = CoachPolicyConfig.CreateDefault();
            Assert.AreEqual("coach-diagnosis-v2", config.DiagnosisModelVersion);
            Assert.AreEqual("coach-feedback-v2", config.FeedbackModelVersion);
            foreach (string field in new[]
                     {
                         "CreeiMissingOrWeakComponents",
                         "CreeiGapSummary",
                         "AgendaSource",
                         "AgendaText"
                     })
                Assert.IsNotNull(typeof(CoachEventRecord).GetField(field), field);

            string logger = File.ReadAllText("Assets/Game/Scripts/CoachResearchLogger.cs");
            StringAssert.Contains("creei_missing_or_weak_components", logger);
            StringAssert.Contains("creei_gap_summary", logger);
            StringAssert.Contains("agenda_source", logger);
            StringAssert.Contains("agenda_text", logger);
        }

        private static CoachPolicyInput CreateDiagnosingInput(
            CoachOrchestrationMode mode,
            int severity,
            float confidence)
        {
            return new CoachPolicyInput
            {
                Mode = mode,
                EpisodeState = CoachEpisodeState.Diagnosing,
                Diagnosis = new CoachDiagnosisResult
                {
                    Success = true,
                    DiagnosisIssueCode = "CREEI_EXPLANATION_WEAK",
                    Severity = severity,
                    Confidence = confidence,
                    RecommendedFocus = "Explanation"
                }
            };
        }

        private static void SetPublicField(object instance, string name, object value)
        {
            var field = instance?.GetType().GetField(name);
            Assert.IsNotNull(field, name);
            field.SetValue(instance, value);
        }

        private static object ReadPublicField(object instance, string name)
        {
            var field = instance?.GetType().GetField(name);
            Assert.IsNotNull(field, name);
            return field.GetValue(instance);
        }
    }
}
