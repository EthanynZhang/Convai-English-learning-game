using System;
using System.IO;
using System.Linq;
using Game.Debate;
using Newtonsoft.Json;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests.EditMode
{
    public sealed class ResearchEpistemicActionTests
    {
        [TestCase("DiagnosisCompleted", "problem_identification", "coach", "coach", "identified")]
        [TestCase("coach_advice_presented", "strategy_generation", "coach", "coach", "presented")]
        [TestCase("micro_revision_evaluated", "evaluation", "coach", "coach", "evaluated")]
        [TestCase("critical_feedback_accepted", "decision", "learner", "learner", "accepted")]
        [TestCase("micro_revision_confirmed", "revision", "learner", "learner", "submitted")]
        [TestCase("coach_episode_completed", "termination", "learner", "learner", "completed")]
        public void CanonicalEventsReceiveControlledEpistemicAnnotations(
            string eventType,
            string expectedAction,
            string expectedActor,
            string expectedDecisionOwner,
            string expectedOutcome)
        {
            ResearchLogEvent row = new()
            {
                EventType = eventType,
                Actor = "system",
                PayloadJson = "{\"component\":\"Evidence\",\"termination_owner\":\"Learner\"}"
            };

            ResearchEpistemicActionAnnotator.Annotate(row);

            Assert.AreEqual("epistemic-labour-v1", row.EpistemicSchemaVersion);
            Assert.AreEqual(expectedAction, row.EpistemicAction);
            Assert.AreEqual(expectedActor, row.EpistemicActor);
            Assert.AreEqual(expectedActor, row.EpistemicInitiator);
            Assert.AreEqual(expectedDecisionOwner, row.EpistemicDecisionOwner);
            Assert.AreEqual("evidence", row.EpistemicTarget);
            Assert.AreEqual(expectedOutcome, row.EpistemicOutcome);
            CollectionAssert.Contains(ResearchEpistemicActionCatalog.Values, row.EpistemicAction);
        }

        [Test]
        [TestCase("coach_request_started")]
        [TestCase("coach_request_submitted")]
        [TestCase("local_diagnosis_completed")]
        public void TechnicalOrCompatibilityEventsDoNotPretendToBeEpistemicLabour(
            string eventType)
        {
            ResearchLogEvent row = new()
            {
                EventType = eventType,
                Actor = "system"
            };

            ResearchEpistemicActionAnnotator.Annotate(row);

            Assert.IsEmpty(row.EpistemicSchemaVersion);
            Assert.IsEmpty(row.EpistemicAction);
            Assert.IsEmpty(row.EpistemicActor);
        }

        [Test]
        public void RevisionDiagnosisIsEvaluationRatherThanASecondProblemIdentification()
        {
            ResearchLogEvent row = new()
            {
                EventType = "DiagnosisCompleted",
                Actor = "system",
                PayloadJson = "{\"revision_kind\":\"CoachedRevision\",\"primary_issue\":\"Impact\"}"
            };

            ResearchEpistemicActionAnnotator.Annotate(row);

            Assert.AreEqual("evaluation", row.EpistemicAction);
            Assert.AreEqual("coach", row.EpistemicActor);
            Assert.AreEqual("impact", row.EpistemicTarget);
            Assert.AreEqual("evaluated", row.EpistemicOutcome);
        }

        [Test]
        public void SessionEventsPersistEpistemicFieldsToJsonlAndCsv()
        {
            string root = Path.Combine(Path.GetTempPath(),
                "research-epistemic-export-" + Guid.NewGuid().ToString("N"));
            GameObject gameObject = new("Research Epistemic Export Test");
            try
            {
                ResearchSessionManager manager = gameObject.AddComponent<ResearchSessionManager>();
                manager.StartSession("P-EPI", root, "S-EPI",
                    CoachOrchestrationMode.SharedControl, "test", "seed");
                manager.EnterStage("04", "coach_supported_practice",
                    ResearchSceneContract.PracticeTopicId, ResearchSceneContract.PracticeTopic);
                manager.RecordEvent("coach_advice_presented", row =>
                {
                    row.Actor = "system";
                    row.PayloadJson = "{\"component\":\"Explanation\"}";
                });
                manager.EndSession("completed");

                ResearchLogEvent saved = File.ReadLines(manager.Sink.EventLogPath)
                    .Select(JsonConvert.DeserializeObject<ResearchLogEvent>)
                    .Single(row => row.EventType == "coach_advice_presented");
                Assert.AreEqual("strategy_generation", saved.EpistemicAction);
                Assert.AreEqual("coach", saved.EpistemicActor);
                Assert.AreEqual("explanation", saved.EpistemicTarget);

                string csv = File.ReadAllText(Path.Combine(
                    manager.Current.SessionDirectory, "exports", "events.csv"));
                StringAssert.Contains(
                    "epistemic_schema_version,epistemic_action,epistemic_actor," +
                    "epistemic_initiator,epistemic_decision_owner,epistemic_target,epistemic_outcome",
                    csv);
                StringAssert.Contains("epistemic-labour-v1,strategy_generation,coach,coach,coach,explanation,presented",
                    csv);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(gameObject);
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }

        [Test]
        public void LocalCoachCsvWriteDoesNotDuplicateTheCanonicalSessionEvent()
        {
            string root = Path.Combine(Path.GetTempPath(),
                "research-no-duplicate-" + Guid.NewGuid().ToString("N"));
            string legacy = Path.Combine(root, "legacy");
            GameObject gameObject = new("Research Duplicate Event Test");
            try
            {
                ResearchSessionManager manager = gameObject.AddComponent<ResearchSessionManager>();
                manager.StartSession("P-DUP", root, "S-DUP",
                    CoachOrchestrationMode.AiLed, "test", "seed");
                manager.EnterStage("04", "coach_supported_practice",
                    ResearchSceneContract.PracticeTopicId, ResearchSceneContract.PracticeTopic);
                ResearchCapture.RecordEvent("coach_advice_presented", "system", "learner",
                    new { component = "Reason" });
                int beforeLegacyWrite = File.ReadLines(manager.Sink.EventLogPath)
                    .Count(line => line.Contains("coach_advice_presented"));

                CoachResearchLogger logger = new(legacy);
                logger.LogLocalEvent(new CoachEventRecord
                {
                    ParticipantId = "P-DUP",
                    SessionId = "S-DUP",
                    OrchestrationMode = CoachOrchestrationMode.AiLed,
                    EventType = "coach_advice_presented",
                    CoachFeedbackText = "Use a clearer reason."
                });

                int afterLegacyWrite = File.ReadLines(manager.Sink.EventLogPath)
                    .Count(line => line.Contains("coach_advice_presented"));
                Assert.AreEqual(1, beforeLegacyWrite);
                Assert.AreEqual(beforeLegacyWrite, afterLegacyWrite,
                    "Writing the compatibility Coach CSV must not mirror the event into the session twice.");
                Assert.AreEqual(1, File.ReadAllLines(logger.EventLogPath).Length - 1);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(gameObject);
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }

        [Test]
        public void Scene04MicroEventHostUsesTheLocalOnlyCompatibilityWrite()
        {
            string source = File.ReadAllText(Path.Combine(
                Application.dataPath, "Game", "Scripts", "ThreeStageDebatePracticeController.cs"));
            int method = source.IndexOf("public void RecordMicroEvent", StringComparison.Ordinal);
            Assert.GreaterOrEqual(method, 0);
            string body = source.Substring(method, Math.Min(2600, source.Length - method));
            StringAssert.Contains("ResearchCapture.RecordEvent", body);
            StringAssert.Contains("_logger?.LogLocalEvent", body);
            StringAssert.DoesNotContain("_logger?.LogEvent", body);
        }
    }
}
