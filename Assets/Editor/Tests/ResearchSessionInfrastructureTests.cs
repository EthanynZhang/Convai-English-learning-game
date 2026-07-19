using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Game.Debate;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests.EditMode
{
    public sealed class ResearchSessionInfrastructureTests
    {
        [Test]
        public void ResearchInfrastructureTypesExist()
        {
            Type runtimeType = typeof(CoachResearchLogger);
            Assert.IsNotNull(runtimeType.Assembly.GetType("Game.Debate.ResearchSessionManager"));
            Assert.IsNotNull(runtimeType.Assembly.GetType("Game.Debate.ResearchSessionSnapshot"));
            Assert.IsNotNull(runtimeType.Assembly.GetType("Game.Debate.ResearchEventSink"));
            Assert.IsNotNull(runtimeType.Assembly.GetType("Game.Debate.ResearchSessionPaths"));
        }

        [Test]
        public void ResearchInfrastructureExposesTheRequiredApi()
        {
            Assembly runtimeAssembly = typeof(CoachResearchLogger).Assembly;
            Type snapshot = runtimeAssembly.GetType("Game.Debate.ResearchSessionSnapshot");
            Type paths = runtimeAssembly.GetType("Game.Debate.ResearchSessionPaths");
            Type sink = runtimeAssembly.GetType("Game.Debate.ResearchEventSink");
            Type manager = runtimeAssembly.GetType("Game.Debate.ResearchSessionManager");

            Assert.IsNotNull(runtimeAssembly.GetType("Game.Debate.ResearchLogEvent"));
            Assert.IsNotNull(runtimeAssembly.GetType("Game.Debate.ResearchTranscriptRecord"));
            Assert.IsNotNull(runtimeAssembly.GetType("Game.Debate.ResearchTechnicalEvent"));
            Assert.IsNotNull(runtimeAssembly.GetType("Game.Debate.ResearchSceneContract"));

            Assert.IsNotNull(snapshot?.GetField("SchemaVersion"));
            Assert.IsNotNull(snapshot?.GetField("ParticipantId"));
            Assert.IsNotNull(snapshot?.GetField("SessionId"));
            Assert.IsNotNull(snapshot?.GetField("Condition"));
            Assert.IsNotNull(snapshot?.GetField("SessionDirectory"));

            Assert.IsNotNull(paths?.GetMethod("GetDefaultRootDirectory"));
            Assert.IsNotNull(paths?.GetMethod("BuildSessionDirectory"));
            Assert.IsNotNull(paths?.GetMethod("SanitizeSegment"));

            Assert.IsNotNull(sink?.GetConstructor(new[] { typeof(string) }));
            Assert.IsNotNull(sink?.GetMethod("WriteManifest"));
            Assert.IsNotNull(sink?.GetMethod("WriteEvent"));
            Assert.IsNotNull(sink?.GetMethod("WriteTranscript"));
            Assert.IsNotNull(sink?.GetMethod("WriteTechnical"));
            Assert.IsNotNull(sink?.GetMethod("Flush"));

            Assert.IsNotNull(manager?.GetMethod("StartSession"));
            Assert.IsNotNull(manager?.GetMethod("AssignCondition"));
            Assert.IsNotNull(manager?.GetMethod("EnterStage"));
            Assert.IsNotNull(manager?.GetMethod("RecordEvent"));
            Assert.IsNotNull(manager?.GetMethod("RecordTranscript"));
            Assert.IsNotNull(manager?.GetMethod("RecordTechnical"));
            Assert.IsNotNull(manager?.GetMethod("EndSession"));
            Assert.IsNotNull(manager?.GetMethod("Flush"));
        }

        [Test]
        public void CollectionAndExportTypesExposeTheRequiredApi()
        {
            Assembly runtimeAssembly = typeof(CoachResearchLogger).Assembly;
            Type capture = runtimeAssembly.GetType("Game.Debate.ResearchCapture");
            Type exporter = runtimeAssembly.GetType("Game.Debate.ResearchCsvExporter");

            Assert.IsNotNull(capture);
            Assert.IsNotNull(exporter);
            Assert.IsNotNull(capture?.GetMethod("TryStartSession"));
            Assert.IsNotNull(capture?.GetMethod("TryAssignCondition"));
            Assert.IsNotNull(capture?.GetMethod("SyncCoachSession"));
            Assert.IsNotNull(capture?.GetMethod("RecordEvent"));
            Assert.IsNotNull(capture?.GetMethod("BeginAttempt"));
            Assert.IsNotNull(capture?.GetMethod("StopAttempt"));
            Assert.IsNotNull(capture?.GetMethod("ConfirmTranscript"));
            Assert.IsNotNull(capture?.GetMethod("RecordCoachEvent"));
            Assert.IsNotNull(capture?.GetMethod("RecordTechnicalFailure"));
            Assert.IsNotNull(capture?.GetMethod("CompleteScene"));
            Assert.IsNotNull(exporter?.GetMethod("ExportSession"));
        }

        [Test]
        public void AudioCaptureAndPersistenceTypesExposeTheRequiredApi()
        {
            Assembly runtimeAssembly = typeof(XfyunRealtimeTranscriber).Assembly;
            Type capture = runtimeAssembly.GetType("Game.Debate.XfyunAudioCapture");
            Type artifact = runtimeAssembly.GetType("Game.Debate.ResearchAudioArtifactRecord");
            Type recorder = runtimeAssembly.GetType("Game.Debate.ResearchAudioRecorder");
            Type sink = runtimeAssembly.GetType("Game.Debate.ResearchEventSink");
            Type manager = runtimeAssembly.GetType("Game.Debate.ResearchSessionManager");
            Type researchCapture = runtimeAssembly.GetType("Game.Debate.ResearchCapture");

            Assert.IsNotNull(capture);
            Assert.IsNotNull(artifact);
            Assert.IsNotNull(recorder);
            Assert.IsNotNull(capture?.GetConstructor(new[]
            {
                typeof(byte[]), typeof(int), typeof(int)
            }));
            Assert.IsNotNull(recorder?.GetMethod("SaveCapture"));
            Assert.IsNotNull(sink?.GetMethod("WriteAudioArtifact"));
            Assert.IsNotNull(manager?.GetMethod("RecordAudioArtifact"));
            Assert.IsNotNull(researchCapture?.GetMethod("SaveAudio"));
            Assert.IsNotNull(typeof(XfyunRealtimeTranscriber).GetProperty("LastAudioCapture"));
        }

        [Test]
        public void CompletionSummaryTypesExposeTheRequiredApi()
        {
            Assembly runtimeAssembly = typeof(ResearchSessionManager).Assembly;
            Type summary = runtimeAssembly.GetType("Game.Debate.ResearchCompletionSummary");
            Type checker = runtimeAssembly.GetType("Game.Debate.ResearchCompletenessChecker");
            Type sink = runtimeAssembly.GetType("Game.Debate.ResearchEventSink");

            Assert.IsNotNull(summary);
            Assert.IsNotNull(checker);
            Assert.IsNotNull(checker?.GetMethod("Evaluate"));
            Assert.IsNotNull(sink?.GetMethod("WriteCompletionSummary"));
            Assert.IsNotNull(typeof(ResearchSessionSnapshot).GetField("DataComplete"));
            Assert.IsNotNull(typeof(ResearchSessionSnapshot).GetField("MissingRequiredData"));
        }

        [Test]
        public void AudioCaptureComputesDurationFromPcm16Shape()
        {
            XfyunAudioCapture capture = new(new byte[32000], 16000, 1);

            Assert.AreEqual(1f, capture.DurationSeconds, 0.0001f);
            Assert.AreEqual(16000, capture.SampleRateHz);
            Assert.AreEqual(1, capture.Channels);
        }

        [Test]
        public void AudioRecorderWritesValidPcm16WavAndMetadata()
        {
            string root = Path.Combine(Path.GetTempPath(), "research-audio-wav-" + Guid.NewGuid().ToString("N"));
            try
            {
                byte[] pcm =
                {
                    0x00, 0x00,
                    0xff, 0x7f,
                    0x00, 0x80,
                    0x34, 0x12
                };
                ResearchAudioRecorder recorder = new();

                ResearchAudioArtifactRecord artifact = recorder.SaveCapture(
                    root,
                    "03",
                    "same_topic_baseline",
                    "ATTEMPT/01",
                    new XfyunAudioCapture(pcm, 16000, 1));

                Assert.IsNotNull(artifact);
                Assert.AreEqual("ATTEMPT_01", artifact.AttemptId);
                Assert.AreEqual("same_topic_baseline", artifact.ResponseRole);
                Assert.AreEqual(16000, artifact.SampleRateHz);
                Assert.AreEqual(1, artifact.Channels);
                Assert.AreEqual(pcm.Length + 44, artifact.ByteCount);
                Assert.AreEqual(64, artifact.Sha256.Length);
                StringAssert.StartsWith("audio/scene03_baseline_ATTEMPT_01", artifact.RelativePath.Replace('\\', '/'));
                string wavPath = Path.Combine(root, artifact.RelativePath);
                Assert.IsTrue(File.Exists(wavPath));
                byte[] wav = File.ReadAllBytes(wavPath);
                Assert.AreEqual("RIFF", System.Text.Encoding.ASCII.GetString(wav, 0, 4));
                Assert.AreEqual("WAVE", System.Text.Encoding.ASCII.GetString(wav, 8, 4));
                Assert.AreEqual("fmt ", System.Text.Encoding.ASCII.GetString(wav, 12, 4));
                Assert.AreEqual("data", System.Text.Encoding.ASCII.GetString(wav, 36, 4));
                Assert.AreEqual(pcm.Length, BitConverter.ToInt32(wav, 40));
                CollectionAssert.AreEqual(pcm, wav.Skip(44).ToArray());
            }
            finally
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }

        [Test]
        public void CaptureSavesAudioWithSessionContextAndExportsAudioCsv()
        {
            string root = Path.Combine(Path.GetTempPath(), "research-audio-session-" + Guid.NewGuid().ToString("N"));
            GameObject gameObject = new("Research Audio Session Test");
            try
            {
                ResearchSessionManager manager = gameObject.AddComponent<ResearchSessionManager>();
                manager.StartSession("P31", root, "S31");
                manager.EnterStage("03", "same_topic_baseline",
                    ResearchSceneContract.PracticeTopicId, ResearchSceneContract.PracticeTopic);

                ResearchAudioArtifactRecord artifact = ResearchCapture.SaveAudio(
                    "A31",
                    "same_topic_baseline",
                    new XfyunAudioCapture(new byte[3200], 16000, 1));
                manager.EndSession();

                Assert.IsNotNull(artifact);
                Assert.AreEqual("P31", artifact.ParticipantId);
                Assert.AreEqual("S31", artifact.SessionId);
                Assert.AreEqual("03", artifact.SceneId);
                Assert.AreEqual(ResearchSceneContract.PracticeTopicId, artifact.TopicId);
                Assert.AreEqual(1, File.ReadAllLines(manager.Sink.AudioArtifactLogPath).Length);
                StringAssert.Contains("audio_saved", File.ReadAllText(manager.Sink.EventLogPath));
                string csv = Path.Combine(manager.Current.SessionDirectory, "exports", "audio_artifacts.csv");
                Assert.IsTrue(File.Exists(csv));
                StringAssert.Contains("attempt_id,audio_file_id,response_role,relative_path", File.ReadAllText(csv));
                StringAssert.Contains("A31", File.ReadAllText(csv));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(gameObject);
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }

        [Test]
        public void MissingAudioCaptureWritesStructuredTechnicalFailure()
        {
            string root = Path.Combine(Path.GetTempPath(), "research-audio-missing-" + Guid.NewGuid().ToString("N"));
            GameObject gameObject = new("Research Missing Audio Test");
            try
            {
                ResearchSessionManager manager = gameObject.AddComponent<ResearchSessionManager>();
                manager.StartSession("P32", root, "S32");
                manager.EnterStage("05", "unsupported_transfer",
                    ResearchSceneContract.TransferTopicId, ResearchSceneContract.TransferTopic);

                ResearchAudioArtifactRecord artifact = ResearchCapture.SaveAudio(
                    "A32", "transfer_speech", null);

                Assert.IsNull(artifact);
                Assert.IsTrue(File.Exists(manager.Sink.TechnicalEventLogPath));
                Assert.AreEqual(1, File.ReadAllLines(manager.Sink.TechnicalEventLogPath).Length);
                string technical = File.ReadAllText(manager.Sink.TechnicalEventLogPath);
                StringAssert.Contains("audio_missing", technical);
                StringAssert.Contains("A32", technical);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(gameObject);
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }

        [Test]
        public void TranscriberRetainsResampledPcmUntilTheNextSessionReset()
        {
            GameObject gameObject = new("Xfyun Research Audio Test");
            try
            {
                XfyunRealtimeTranscriber transcriber = gameObject.AddComponent<XfyunRealtimeTranscriber>();
                Type type = typeof(XfyunRealtimeTranscriber);
                type.GetField("_sourceSampleRate", BindingFlags.NonPublic | BindingFlags.Instance)
                    ?.SetValue(transcriber, 16000);
                type.GetField("_sourceChannels", BindingFlags.NonPublic | BindingFlags.Instance)
                    ?.SetValue(transcriber, 1);
                List<float> source = type.GetField(
                        "_sourceSamples", BindingFlags.NonPublic | BindingFlags.Instance)
                    ?.GetValue(transcriber) as List<float>;
                Assert.IsNotNull(source);
                source.AddRange(new[] { 0f, 0.5f, -0.5f, 0f });

                type.GetMethod("ResampleAndQueuePackets", BindingFlags.NonPublic | BindingFlags.Instance)
                    ?.Invoke(transcriber, null);
                MethodInfo finalize = type.GetMethod(
                    "FinalizeResearchAudioCapture", BindingFlags.NonPublic | BindingFlags.Instance);
                Assert.IsNotNull(finalize);
                finalize.Invoke(transcriber, null);

                Assert.IsNotNull(transcriber.LastAudioCapture);
                Assert.AreEqual(16000, transcriber.LastAudioCapture.SampleRateHz);
                Assert.AreEqual(1, transcriber.LastAudioCapture.Channels);
                Assert.AreEqual(6, transcriber.LastAudioCapture.Pcm16Bytes.Length);

                type.GetMethod("ResetSessionState", BindingFlags.NonPublic | BindingFlags.Instance)
                    ?.Invoke(transcriber, null);
                Assert.IsNull(transcriber.LastAudioCapture);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void OralAndCoachControllersPersistTheTranscriberAudioForEachAttempt()
        {
            string scripts = Path.Combine(Application.dataPath, "Game", "Scripts");
            string scene04 = File.ReadAllText(Path.Combine(scripts,
                "ThreeStageDebatePracticeController.cs"));
            string oralPractice = File.ReadAllText(Path.Combine(scripts,
                "PlayerOralPracticeController.cs"));

            StringAssert.Contains("ResearchCapture.SaveAudio", scene04);
            StringAssert.Contains("realtimeTranscriber?.LastAudioCapture", scene04);
            StringAssert.Contains("ResearchCapture.SaveAudio", oralPractice);
            StringAssert.Contains("_realtimeTranscriber?.LastAudioCapture", oralPractice);
        }

        [Test]
        public void CompletenessCheckerFindsMissingAndOrphanAudioAttempts()
        {
            string root = Path.Combine(Path.GetTempPath(), "research-completeness-" + Guid.NewGuid().ToString("N"));
            try
            {
                ResearchEventSink sink = new(root);
                sink.WriteTranscript(new ResearchTranscriptRecord { AttemptId = "A1", ResponseId = "R1" });
                sink.WriteTranscript(new ResearchTranscriptRecord { AttemptId = "A2", ResponseId = "R2" });
                sink.WriteAudioArtifact(new ResearchAudioArtifactRecord { AttemptId = "A1", AudioFileId = "F1" });
                sink.WriteAudioArtifact(new ResearchAudioArtifactRecord { AttemptId = "A3", AudioFileId = "F3" });

                ResearchCompletionSummary summary = ResearchCompletenessChecker.Evaluate(
                    root,
                    new ResearchSessionSnapshot { ParticipantId = "P40", SessionId = "S40" });

                Assert.IsNotNull(summary);
                Assert.IsFalse(summary.DataComplete);
                Assert.AreEqual(2, summary.TranscriptCount);
                Assert.AreEqual(2, summary.AudioArtifactCount);
                CollectionAssert.AreEqual(new[] { "A2" }, summary.MissingAudioAttemptIds);
                CollectionAssert.AreEqual(new[] { "A3" }, summary.OrphanAudioAttemptIds);
                Assert.IsNotEmpty(summary.GeneratedAtUtc);
            }
            finally
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }

        [Test]
        public void EndSessionPersistsCompletenessSummaryAndExportsItsStatus()
        {
            string root = Path.Combine(Path.GetTempPath(), "research-completion-export-" + Guid.NewGuid().ToString("N"));
            GameObject gameObject = new("Research Completion Export Test");
            try
            {
                ResearchSessionManager manager = gameObject.AddComponent<ResearchSessionManager>();
                manager.StartSession("P41", root, "S41");
                manager.EnterStage("05", "unsupported_transfer",
                    ResearchSceneContract.TransferTopicId, ResearchSceneContract.TransferTopic);
                manager.RecordTranscript(new ResearchTranscriptRecord
                {
                    AttemptId = "A41",
                    ResponseId = "R41",
                    ResponseRole = "transfer_speech",
                    TranscriptText = "A transfer response without audio."
                });

                manager.EndSession();

                Assert.IsFalse(manager.Current.DataComplete);
                StringAssert.Contains("audio_for_attempt:A41", manager.Current.MissingRequiredData);
                Assert.IsTrue(File.Exists(manager.Sink.CompletionSummaryPath));
                JObject summary = JObject.Parse(File.ReadAllText(manager.Sink.CompletionSummaryPath));
                Assert.AreEqual(false, summary["DataComplete"]?.Value<bool>());
                Assert.AreEqual("A41", summary["MissingAudioAttemptIds"]?[0]?.Value<string>());
                string sessionsCsv = File.ReadAllText(Path.Combine(
                    manager.Current.SessionDirectory, "exports", "sessions.csv"));
                StringAssert.Contains("data_complete,missing_required_data", sessionsCsv);
                StringAssert.Contains("audio_for_attempt:A41", sessionsCsv);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(gameObject);
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }

        [Test]
        public void SessionPathsStayInsideTheRequestedRootAndSanitizeSegments()
        {
            string root = Path.Combine(Path.GetTempPath(), "research-paths-" + Guid.NewGuid().ToString("N"));

            string directory = ResearchSessionPaths.BuildSessionDirectory(
                root,
                " P/01 ",
                " S:01 ");

            Assert.AreEqual(
                Path.Combine(Path.GetFullPath(root), "P_01", "S_01"),
                directory);
            StringAssert.StartsWith(Path.GetFullPath(root), directory);
            StringAssert.EndsWith(Path.Combine("ResearchData", "schema_v2"),
                ResearchSessionPaths.GetDefaultRootDirectory());
        }

        [Test]
        public void EventSinkWritesOneValidJsonObjectPerLine()
        {
            string directory = Path.Combine(Path.GetTempPath(), "research-sink-" + Guid.NewGuid().ToString("N"));
            try
            {
                ResearchEventSink sink = new(directory);
                sink.WriteManifest(new ResearchSessionSnapshot
                {
                    SchemaVersion = 2,
                    ParticipantId = "P01",
                    SessionId = "S01"
                });
                sink.WriteEvent(new ResearchLogEvent
                {
                    SchemaVersion = 2,
                    ParticipantId = "P01",
                    SessionId = "S01",
                    EventId = "E01",
                    EventType = "multiline_event",
                    PayloadJson = "line 1\nline \"2\""
                });
                sink.WriteTranscript(new ResearchTranscriptRecord
                {
                    SchemaVersion = 2,
                    ParticipantId = "P01",
                    SessionId = "S01",
                    ResponseId = "R01",
                    TranscriptText = "first line\nsecond line"
                });
                sink.WriteTechnical(new ResearchTechnicalEvent
                {
                    SchemaVersion = 2,
                    ParticipantId = "P01",
                    SessionId = "S01",
                    EventType = "asr_failure",
                    ErrorCode = "ASR_TIMEOUT"
                });
                sink.Flush();

                Assert.IsTrue(File.Exists(sink.ManifestPath));
                Assert.AreEqual(1, File.ReadAllLines(sink.EventLogPath).Length);
                Assert.AreEqual(1, File.ReadAllLines(sink.TranscriptLogPath).Length);
                Assert.AreEqual(1, File.ReadAllLines(sink.TechnicalEventLogPath).Length);
                Assert.AreEqual("multiline_event",
                    JObject.Parse(File.ReadAllText(sink.EventLogPath))["EventType"]?.Value<string>());
                Assert.AreEqual("first line\nsecond line",
                    JObject.Parse(File.ReadAllText(sink.TranscriptLogPath))["TranscriptText"]?.Value<string>());
                Assert.AreEqual(2,
                    JObject.Parse(File.ReadAllText(sink.ManifestPath))["SchemaVersion"]?.Value<int>());
            }
            finally
            {
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        }

        [Test]
        public void ManagerCreatesOneSessionAndInheritsItsContextIntoEvents()
        {
            string root = Path.Combine(Path.GetTempPath(), "research-manager-" + Guid.NewGuid().ToString("N"));
            GameObject gameObject = new("Research Session Manager Test");
            try
            {
                ResearchSessionManager manager = gameObject.AddComponent<ResearchSessionManager>();
                ResearchSessionSnapshot snapshot = manager.StartSession(" P014 ", root, "SESSION01");

                Assert.IsTrue(manager.HasActiveSession);
                Assert.AreEqual(2, snapshot.SchemaVersion);
                Assert.AreEqual("P014", snapshot.ParticipantId);
                Assert.AreEqual("SESSION01", snapshot.SessionId);
                Assert.AreEqual(CoachOrchestrationMode.Disabled, snapshot.Condition);
                Assert.IsTrue(File.Exists(manager.Sink.ManifestPath));

                manager.AssignCondition(CoachOrchestrationMode.SharedControl);
                manager.EnterStage(
                    "04",
                    "coach_supported_practice",
                    "individual_practice_vs_interaction_speaking",
                    "Individual practice and interaction with others");
                ResearchLogEvent custom = manager.RecordEvent("custom_event", record =>
                {
                    record.Actor = "learner";
                    record.PayloadJson = "{\"choice\":\"continue\"}";
                });

                Assert.AreEqual("P014", custom.ParticipantId);
                Assert.AreEqual("SESSION01", custom.SessionId);
                Assert.AreEqual("SharedControl", custom.Condition);
                Assert.AreEqual("04", custom.SceneId);
                Assert.AreEqual("coach_supported_practice", custom.StudyStage);
                Assert.AreEqual("individual_practice_vs_interaction_speaking", custom.TopicId);
                Assert.IsNotEmpty(custom.EventId);
                Assert.IsNotEmpty(custom.EventTimestampUtc);

                manager.EndSession();

                Assert.IsFalse(manager.HasActiveSession);
                Assert.AreEqual("completed", snapshot.Status);
                Assert.IsNotEmpty(snapshot.EndedAtUtc);
                JObject manifest = JObject.Parse(File.ReadAllText(manager.Sink.ManifestPath));
                Assert.AreEqual("SharedControl", manifest["Condition"]?.Value<string>());
                Assert.AreEqual("completed", manifest["Status"]?.Value<string>());
                Assert.GreaterOrEqual(File.ReadAllLines(manager.Sink.EventLogPath).Length, 5);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(gameObject);
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }

        [Test]
        public void ManagerRejectsInvalidOrConflictingSessionSetup()
        {
            string root = Path.Combine(Path.GetTempPath(), "research-invalid-" + Guid.NewGuid().ToString("N"));
            GameObject gameObject = new("Research Session Manager Validation Test");
            try
            {
                ResearchSessionManager manager = gameObject.AddComponent<ResearchSessionManager>();
                Assert.Throws<ArgumentException>(() => manager.StartSession(" ", root, "S01"));

                manager.StartSession("P01", root, "S01");
                Assert.Throws<InvalidOperationException>(() => manager.StartSession("P02", root, "S02"));
                Assert.Throws<ArgumentException>(() =>
                    manager.AssignCondition(CoachOrchestrationMode.Disabled));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(gameObject);
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }

        [Test]
        public void ManagerCompletesIdentifiersAndContextForTranscriptAndTechnicalRecords()
        {
            string root = Path.Combine(Path.GetTempPath(), "research-records-" + Guid.NewGuid().ToString("N"));
            GameObject gameObject = new("Research Session Record Test");
            try
            {
                ResearchSessionManager manager = gameObject.AddComponent<ResearchSessionManager>();
                manager.StartSession("P09", root, "S09");
                manager.AssignCondition(CoachOrchestrationMode.AiLed);
                manager.EnterStage(
                    "05",
                    "unsupported_transfer",
                    ResearchSceneContract.TransferTopicId,
                    ResearchSceneContract.TransferTopic);

                ResearchTranscriptRecord transcript = new()
                {
                    ResponseRole = "transfer",
                    TranscriptText = "A confirmed transfer response."
                };
                manager.RecordTranscript(transcript);
                ResearchTechnicalEvent technical = new()
                {
                    EventType = "asr_failure",
                    ErrorCode = "ASR_TIMEOUT"
                };
                manager.RecordTechnical(technical);
                manager.Flush();

                Assert.IsNotEmpty(transcript.AttemptId);
                Assert.IsNotEmpty(transcript.ResponseId);
                Assert.IsNotEmpty(transcript.ConfirmedAtUtc);
                Assert.AreEqual("P09", transcript.ParticipantId);
                Assert.AreEqual("S09", transcript.SessionId);
                Assert.AreEqual("AiLed", transcript.Condition);
                Assert.AreEqual("05", transcript.SceneId);
                Assert.AreEqual("unsupported_transfer", transcript.StudyStage);
                Assert.AreEqual(ResearchSceneContract.TransferTopicId, transcript.TopicId);

                Assert.AreEqual("P09", technical.ParticipantId);
                Assert.AreEqual("S09", technical.SessionId);
                Assert.AreEqual("05", technical.SceneId);
                Assert.IsNotEmpty(technical.EventTimestampUtc);
                Assert.AreEqual(1, File.ReadAllLines(manager.Sink.TranscriptLogPath).Length);
                Assert.AreEqual(1, File.ReadAllLines(manager.Sink.TechnicalEventLogPath).Length);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(gameObject);
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }

        [Test]
        public void SceneContractsKeep03And04OnTheSameTopicAnd05OnTransfer()
        {
            Assert.IsTrue(ResearchSceneContract.TryResolve(
                "03Level_PlayerVsNPCDebate",
                out string scene03,
                out string stage03,
                out string topic03,
                out string text03));
            Assert.IsTrue(ResearchSceneContract.TryResolve(
                "04 coach Agent",
                out string scene04,
                out string stage04,
                out string topic04,
                out string text04));
            Assert.IsTrue(ResearchSceneContract.TryResolve(
                "05Level_PlayerVsNPCDebate 1",
                out string scene05,
                out string stage05,
                out string topic05,
                out string text05));

            Assert.AreEqual("03", scene03);
            Assert.AreEqual("same_topic_baseline", stage03);
            Assert.AreEqual("04", scene04);
            Assert.AreEqual("coach_supported_practice", stage04);
            Assert.AreEqual(topic03, topic04);
            Assert.AreEqual(text03, text04);
            Assert.AreEqual(ResearchSceneContract.PracticeTopicId, topic04);
            Assert.AreEqual("05", scene05);
            Assert.AreEqual("unsupported_transfer", stage05);
            Assert.AreEqual(ResearchSceneContract.TransferTopicId, topic05);
            Assert.AreEqual(ResearchSceneContract.TransferTopic, text05);
            Assert.AreNotEqual(topic04, topic05);
        }

        [Test]
        public void CaptureStartsOneSessionAndLocksTheAssignedCondition()
        {
            string root = Path.Combine(Path.GetTempPath(), "research-capture-" + Guid.NewGuid().ToString("N"));
            GameObject gameObject = new("Research Capture Test");
            try
            {
                ResearchSessionManager manager = gameObject.AddComponent<ResearchSessionManager>();

                Assert.IsTrue(ResearchCapture.TryStartSession(" P21 ", "S21", root));
                Assert.IsTrue(manager.HasActiveSession);
                Assert.AreEqual("P21", manager.Current.ParticipantId);
                Assert.AreEqual("S21", manager.Current.SessionId);
                Assert.IsFalse(ResearchCapture.TryStartSession("P21", "S21", root));
                Assert.IsTrue(ResearchCapture.TryAssignCondition(CoachOrchestrationMode.SharedControl));
                Assert.IsFalse(ResearchCapture.TryAssignCondition(CoachOrchestrationMode.AiLed));
                Assert.AreEqual(CoachOrchestrationMode.SharedControl, manager.Current.Condition);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(gameObject);
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }

        [Test]
        public void CaptureLinksAttemptTranscriptAndCoachEventsToCurrentSession()
        {
            string root = Path.Combine(Path.GetTempPath(), "research-linking-" + Guid.NewGuid().ToString("N"));
            GameObject gameObject = new("Research Linking Test");
            try
            {
                ResearchSessionManager manager = gameObject.AddComponent<ResearchSessionManager>();
                manager.StartSession("P22", root, "S22");
                manager.AssignCondition(CoachOrchestrationMode.LearnerLed);
                manager.EnterStage("04", "micro_independent_response",
                    ResearchSceneContract.PracticeTopicId, ResearchSceneContract.PracticeTopic);

                string attemptId = ResearchCapture.BeginAttempt("micro_independent_response", "interaction");
                ResearchCapture.StopAttempt(attemptId, 31.25f, "learner_stopped");
                ResearchTranscriptRecord transcript = ResearchCapture.ConfirmTranscript(
                    attemptId,
                    "micro_independent_response",
                    "Interaction helps me respond, revise, and improve.",
                    31.25f);
                ResearchCapture.RecordCoachEvent(new CoachEventRecord
                {
                    EventId = "COACH-E1",
                    EventTimestamp = "2026-07-18T08:00:00.0000000+00:00",
                    EventType = "FeedbackPresented",
                    Stage = "MicroChallenge",
                    CoachEpisodeId = "CE-1",
                    ChallengeCycleIndex = 1,
                    CoachFeedbackLevel = "Level2",
                    CoachFeedbackType = "evidence",
                    CoachFeedbackText = "Add a concrete example.",
                    PolicyVersion = "policy-v1"
                });

                Assert.IsNotEmpty(attemptId);
                Assert.IsNotNull(transcript);
                Assert.AreEqual(attemptId, transcript.AttemptId);
                Assert.IsNotEmpty(transcript.ResponseId);
                Assert.AreEqual("micro_independent_response", transcript.ResponseRole);
                Assert.AreEqual(31.25f, transcript.RecordingDurationSeconds);
                Assert.AreEqual(1, File.ReadAllLines(manager.Sink.TranscriptLogPath).Length);

                string events = File.ReadAllText(manager.Sink.EventLogPath);
                StringAssert.Contains("recording_started", events);
                StringAssert.Contains("recording_stopped", events);
                StringAssert.Contains("FeedbackPresented", events);
                StringAssert.Contains("COACH-E1", events);
                StringAssert.Contains("Add a concrete example.", events);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(gameObject);
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }

        [Test]
        public void CsvExporterWritesNormalizedEscapedTablesFromSessionJsonl()
        {
            string root = Path.Combine(Path.GetTempPath(), "research-export-" + Guid.NewGuid().ToString("N"));
            GameObject gameObject = new("Research Export Test");
            try
            {
                ResearchSessionManager manager = gameObject.AddComponent<ResearchSessionManager>();
                manager.StartSession("P23", root, "S23");
                manager.EnterStage("05", "unsupported_transfer",
                    ResearchSceneContract.TransferTopicId, ResearchSceneContract.TransferTopic);
                manager.RecordEvent("transfer_prompt_presented", row =>
                {
                    row.Actor = "system";
                    row.PayloadJson = "{\"prompt\":\"classroom, real life\"}";
                });
                manager.RecordTranscript(new ResearchTranscriptRecord
                {
                    AttemptId = "A23",
                    ResponseId = "R23",
                    ResponseRole = "transfer_speech",
                    TranscriptText = "Real life, \"because it matters\".\nThen classroom support helps."
                });
                manager.RecordTechnical(new ResearchTechnicalEvent
                {
                    EventType = "asr_failure",
                    ErrorCode = "ASR_TIMEOUT",
                    ErrorMessage = "Retry, then recovered",
                    Recovered = true
                });
                manager.Flush();

                string exports = ResearchCsvExporter.ExportSession(manager.Current.SessionDirectory);

                Assert.AreEqual(Path.Combine(manager.Current.SessionDirectory, "exports"), exports);
                string sessionsPath = Path.Combine(exports, "sessions.csv");
                string eventsPath = Path.Combine(exports, "events.csv");
                string transcriptsPath = Path.Combine(exports, "transcripts.csv");
                string technicalPath = Path.Combine(exports, "technical_events.csv");
                Assert.IsTrue(File.Exists(sessionsPath));
                Assert.IsTrue(File.Exists(eventsPath));
                Assert.IsTrue(File.Exists(transcriptsPath));
                Assert.IsTrue(File.Exists(technicalPath));
                StringAssert.StartsWith("schema_version,study_id,participant_id,session_id", File.ReadAllText(sessionsPath));
                StringAssert.Contains("event_type,actor,recipient,payload_json", File.ReadAllText(eventsPath));
                StringAssert.Contains("\"{\"\"prompt\"\":\"\"classroom, real life\"\"}\"", File.ReadAllText(eventsPath));
                StringAssert.Contains("\"Real life, \"\"because it matters\"\".\nThen classroom support helps.\"",
                    File.ReadAllText(transcriptsPath));
                StringAssert.Contains("ASR_TIMEOUT,\"Retry, then recovered\",0,true",
                    File.ReadAllText(technicalPath));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(gameObject);
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }

        [Test]
        public void CoachSessionSyncKeepsExistingScene01SessionAndAssignsScene04Condition()
        {
            string root = Path.Combine(Path.GetTempPath(), "research-sync-" + Guid.NewGuid().ToString("N"));
            GameObject gameObject = new("Research Session Sync Test");
            try
            {
                ResearchSessionManager manager = gameObject.AddComponent<ResearchSessionManager>();
                manager.StartSession("P24", root, "SCENE01-SESSION");

                MethodInfo sync = typeof(ResearchCapture).GetMethod("SyncCoachSession");
                Assert.IsNotNull(sync);
                object synchronized = sync.Invoke(null, new object[]
                {
                    new CoachStudySessionSnapshot
                    {
                        ParticipantId = "P24",
                        SessionId = "COACH-LEGACY-SESSION",
                        Mode = CoachOrchestrationMode.AiLed
                    }
                });
                Assert.AreEqual(true, synchronized);

                Assert.AreEqual("SCENE01-SESSION", manager.Current.SessionId);
                Assert.AreEqual(CoachOrchestrationMode.AiLed, manager.Current.Condition);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(gameObject);
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }

        [Test]
        public void CoachLoggerMirrorsLegacyEventsIntoTheUnifiedEventSink()
        {
            string root = Path.Combine(Path.GetTempPath(), "research-mirror-" + Guid.NewGuid().ToString("N"));
            string legacy = Path.Combine(root, "legacy");
            GameObject gameObject = new("Research Logger Mirror Test");
            try
            {
                ResearchSessionManager manager = gameObject.AddComponent<ResearchSessionManager>();
                manager.StartSession("P25", root, "S25");
                manager.EnterStage("04", "micro_coach_episode",
                    ResearchSceneContract.PracticeTopicId, ResearchSceneContract.PracticeTopic);
                CoachResearchLogger logger = new(legacy);

                logger.LogEvent(new CoachEventRecord
                {
                    EventId = "MIRROR-E1",
                    EventType = "OpponentChallengePresented",
                    OpponentUtteranceText = "Why is interaction better?",
                    ChallengeCycleIndex = 2,
                    ChallengeDifficulty = "Hard"
                });

                string unified = File.ReadAllText(manager.Sink.EventLogPath);
                StringAssert.Contains("MIRROR-E1", unified);
                StringAssert.Contains("opponent_challenge_presented", unified);
                StringAssert.Contains("Why is interaction better?", unified);
                Assert.IsTrue(File.Exists(logger.EventLogPath));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(gameObject);
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }

        [Test]
        public void EndingSessionAutomaticallyCreatesNormalizedCsvExports()
        {
            string root = Path.Combine(Path.GetTempPath(), "research-end-export-" + Guid.NewGuid().ToString("N"));
            GameObject gameObject = new("Research End Export Test");
            try
            {
                ResearchSessionManager manager = gameObject.AddComponent<ResearchSessionManager>();
                manager.StartSession("P26", root, "S26");
                manager.RecordEvent("study_progress");

                manager.EndSession();

                string exports = Path.Combine(manager.Current.SessionDirectory, "exports");
                Assert.IsTrue(File.Exists(Path.Combine(exports, "sessions.csv")));
                Assert.IsTrue(File.Exists(Path.Combine(exports, "events.csv")));
                Assert.IsTrue(File.Exists(Path.Combine(exports, "transcripts.csv")));
                Assert.IsTrue(File.Exists(Path.Combine(exports, "technical_events.csv")));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(gameObject);
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }

        [Test]
        public void StudyFlowControllersDeclareUnifiedResearchCaptureBoundaries()
        {
            string scripts = Path.Combine(Application.dataPath, "Game", "Scripts");
            string scene01 = File.ReadAllText(Path.Combine(scripts,
                "NpcDebateLearningPhaseController.cs"));
            string scene04 = File.ReadAllText(Path.Combine(scripts,
                "ThreeStageDebatePracticeController.cs"));
            string oralPractice = File.ReadAllText(Path.Combine(scripts,
                "PlayerOralPracticeController.cs"));

            StringAssert.Contains("ResearchStudySetupCoordinator.BeginOrReuse", scene01);
            StringAssert.Contains("ResearchCapture.SyncCoachSession", scene04);
            StringAssert.Contains("ResearchCapture.BeginAttempt", oralPractice);
            StringAssert.Contains("ResearchCapture.StopAttempt", oralPractice);
            StringAssert.Contains("ResearchCapture.ConfirmTranscript", oralPractice);
            StringAssert.Contains("ResearchCapture.RecordTechnicalFailure", oralPractice);
            StringAssert.Contains("ResearchCapture.CompleteScene", oralPractice);
        }

        [Test]
        public void CompletingScene05EndsSessionAndExportsCsvButScene03DoesNot()
        {
            string root = Path.Combine(Path.GetTempPath(), "research-final-scene-" + Guid.NewGuid().ToString("N"));
            GameObject gameObject = new("Research Final Scene Test");
            try
            {
                ResearchSessionManager manager = gameObject.AddComponent<ResearchSessionManager>();
                manager.StartSession("P27", root, "S27");
                manager.EnterStage("03", "same_topic_baseline",
                    ResearchSceneContract.PracticeTopicId, ResearchSceneContract.PracticeTopic);
                ResearchCapture.CompleteScene();
                Assert.IsTrue(manager.HasActiveSession);

                manager.EnterStage("05", "unsupported_transfer",
                    ResearchSceneContract.TransferTopicId, ResearchSceneContract.TransferTopic);
                ResearchCapture.CompleteScene();

                Assert.IsFalse(manager.HasActiveSession);
                Assert.AreEqual("completed", manager.Current.Status);
                Assert.IsTrue(File.Exists(Path.Combine(
                    manager.Current.SessionDirectory, "exports", "sessions.csv")));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(gameObject);
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }

        [Test]
        public void ParticipantWithdrawalIsRecordedBeforeTheSessionEnds()
        {
            string root = Path.Combine(Path.GetTempPath(), "research-withdrawal-" + Guid.NewGuid().ToString("N"));
            GameObject gameObject = new("Research Withdrawal Test");
            try
            {
                ResearchSessionManager manager = gameObject.AddComponent<ResearchSessionManager>();
                manager.StartSession("P28", root, "S28");
                manager.EnterStage("04", "micro_independent_response",
                    ResearchSceneContract.PracticeTopicId, ResearchSceneContract.PracticeTopic);

                bool recorded = ResearchCapture.RecordWithdrawal(
                    "Participant chose to stop.",
                    "retain_anonymized_data");

                Assert.IsTrue(recorded);
                Assert.IsFalse(manager.HasActiveSession);
                Assert.AreEqual("withdrawn", manager.Current.Status);
                string events = File.ReadAllText(manager.Sink.EventLogPath);
                StringAssert.Contains("participant_withdrawal", events);
                StringAssert.Contains("Participant chose to stop.", events);
                StringAssert.Contains("retain_anonymized_data", events);
                Assert.IsTrue(File.Exists(Path.Combine(
                    manager.Current.SessionDirectory, "exports", "events.csv")));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(gameObject);
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }

        [Test]
        public void WithdrawalRejectsUnknownRetentionChoiceWithoutEndingSession()
        {
            string root = Path.Combine(Path.GetTempPath(), "research-withdrawal-invalid-" + Guid.NewGuid().ToString("N"));
            GameObject gameObject = new("Research Withdrawal Validation Test");
            try
            {
                ResearchSessionManager manager = gameObject.AddComponent<ResearchSessionManager>();
                manager.StartSession("P29", root, "S29");

                Assert.IsFalse(ResearchCapture.RecordWithdrawal("", "erase_sometime"));
                Assert.IsTrue(manager.HasActiveSession);
                Assert.IsFalse(File.ReadAllText(manager.Sink.EventLogPath)
                    .Contains("participant_withdrawal"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(gameObject);
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }

        [Test]
        public void Scene01AndScene03DeclareCurrentPrototypeCollectionEvents()
        {
            string scripts = Path.Combine(Application.dataPath, "Game", "Scripts");
            string scene01 = File.ReadAllText(Path.Combine(scripts,
                "NpcDebateLearningPhaseController.cs"));
            string scene03 = File.ReadAllText(Path.Combine(scripts,
                "DebatePreparationController.cs"));

            StringAssert.Contains("learning_stage_viewed", scene01);
            StringAssert.Contains("learning_practice_response", scene01);
            StringAssert.Contains("baseline_preparation_completed", scene03);
            StringAssert.Contains("preparation_duration_seconds", scene03);
        }

        [Test]
        public void ConditionBlindRatingsImportLinksOnlyKnownResponseIds()
        {
            string root = Path.Combine(Path.GetTempPath(), "research-ratings-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                string transcripts = Path.Combine(root, "transcripts.jsonl");
                File.WriteAllText(transcripts, Newtonsoft.Json.JsonConvert.SerializeObject(
                    new ResearchTranscriptRecord { ResponseId = "R-001", ResponseRole = "transfer_speech" }) + "\n");
                string input = Path.Combine(root, "blind_ratings.csv");
                File.WriteAllText(input,
                    "response_id,rater_id,rubric_version,claim_score,reason_score,evidence_score,explanation_score,impact_score,language_delivery_score,rater_comment\n" +
                    "R-001,J02,CREEI-1,4,4,3,4,5,4,Clear impact\n");
                string output = Path.Combine(root, "ratings.csv");

                ResearchRatingImportResult result = ResearchRatingImportService.ValidateAndImport(
                    input, transcripts, output);

                Assert.IsTrue(result.Success, string.Join("; ", result.Errors));
                Assert.AreEqual(1, result.ImportedRowCount);
                Assert.IsTrue(File.Exists(output));
                string normalized = File.ReadAllText(output);
                StringAssert.Contains("R-001,J02,CREEI-1", normalized);
                Assert.IsFalse(normalized.ToLowerInvariant().Contains("condition"));
                Assert.IsFalse(normalized.ToLowerInvariant().Contains("participant"));
            }
            finally
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }

        [Test]
        public void RatingsImportRejectsConditionLeakageAndUnknownResponses()
        {
            string root = Path.Combine(Path.GetTempPath(), "research-ratings-invalid-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                string transcripts = Path.Combine(root, "transcripts.jsonl");
                File.WriteAllText(transcripts, Newtonsoft.Json.JsonConvert.SerializeObject(
                    new ResearchTranscriptRecord { ResponseId = "R-001" }) + "\n");
                string input = Path.Combine(root, "ratings_with_leak.csv");
                File.WriteAllText(input,
                    "response_id,rater_id,rubric_version,claim_score,reason_score,evidence_score,explanation_score,impact_score,language_delivery_score,condition\n" +
                    "R-999,J02,CREEI-1,4,4,3,4,5,4,AI-led\n");

                ResearchRatingImportResult result = ResearchRatingImportService.ValidateAndImport(
                    input, transcripts, Path.Combine(root, "ratings.csv"));

                Assert.IsFalse(result.Success);
                Assert.IsTrue(result.Errors.Any(error => error.Contains("condition")));
                Assert.IsTrue(result.Errors.Any(error => error.Contains("R-999")));
            }
            finally
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }

        [Test]
        public void ConditionAssignmentRecordsMethodSeedAndTimestamp()
        {
            string root = Path.Combine(Path.GetTempPath(), "research-assignment-" + Guid.NewGuid().ToString("N"));
            GameObject gameObject = new("Research Assignment Metadata Test");
            try
            {
                ResearchSessionManager manager = gameObject.AddComponent<ResearchSessionManager>();
                manager.StartSession("P30", root, "S30");

                manager.AssignCondition(
                    CoachOrchestrationMode.SharedControl,
                    "blocked_randomization",
                    "seed-20260718");

                JObject assignment = File.ReadLines(manager.Sink.EventLogPath)
                    .Select(JObject.Parse)
                    .Last(row => (string)row["EventType"] == "condition_assigned");
                JObject payload = JObject.Parse((string)assignment["PayloadJson"]);
                Assert.AreEqual("SharedControl", (string)payload["condition"]);
                Assert.AreEqual("blocked_randomization", (string)payload["assignment_method"]);
                Assert.AreEqual("seed-20260718", (string)payload["assignment_seed"]);
                Assert.IsNotEmpty((string)payload["assigned_at_utc"]);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(gameObject);
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }

        [Test]
        public void SceneContractValidatesTheFormalStudyOrder()
        {
            Assert.IsTrue(ResearchSceneContract.IsAllowedTransition("01", "03", out string firstIssue));
            Assert.IsEmpty(firstIssue);
            Assert.IsTrue(ResearchSceneContract.IsAllowedTransition("03", "04", out string secondIssue));
            Assert.IsEmpty(secondIssue);
            Assert.IsTrue(ResearchSceneContract.IsAllowedTransition("04", "05", out string thirdIssue));
            Assert.IsEmpty(thirdIssue);
            Assert.IsTrue(ResearchSceneContract.IsAllowedTransition("04", "04", out string sameSceneIssue));
            Assert.IsEmpty(sameSceneIssue);

            Assert.IsFalse(ResearchSceneContract.IsAllowedTransition("03", "05", out string skippedIssue));
            StringAssert.Contains("03 -> 04 -> 05", skippedIssue);
            Assert.IsFalse(ResearchSceneContract.IsAllowedTransition("04", "03", out string backwardIssue));
            StringAssert.Contains("backward", backwardIssue.ToLowerInvariant());
        }

        [Test]
        public void RuntimeSceneSequenceViolationsContributeToMissingRequiredData()
        {
            string scripts = Path.Combine(Application.dataPath, "Game", "Scripts");
            string infrastructure = File.ReadAllText(Path.Combine(scripts,
                "ResearchSessionInfrastructure.cs"));

            StringAssert.Contains("Application.isPlaying && !sequenceValid", infrastructure);
            StringAssert.Contains("study_flow_sequence", infrastructure);
            StringAssert.Contains("RegisterMissingRequiredData", infrastructure);
        }

        [Test]
        public void Scene01DemoCompletionDeclaresVersionDurationAndRewatchFields()
        {
            string scripts = Path.Combine(Application.dataPath, "Game", "Scripts");
            string scene01 = File.ReadAllText(Path.Combine(scripts,
                "NpcDebateLearningPhaseController.cs"));

            StringAssert.Contains("demo_viewed", scene01);
            StringAssert.Contains("demo_version", scene01);
            StringAssert.Contains("playback_duration_seconds", scene01);
            StringAssert.Contains("rewatch_count", scene01);
            StringAssert.Contains("completed = true", scene01);
        }

        [Test]
        public void OralSceneCompletionGateRequiresAudioTranscriptAndTransferCoachCheck()
        {
            ResearchSceneCompletionStatus baseline = ResearchSceneCompletionGate.EvaluateOral(
                "03", 1, 1, 1, true);
            Assert.IsTrue(baseline.DataComplete);
            Assert.IsEmpty(baseline.MissingRequiredData);

            ResearchSceneCompletionStatus missingBaseline = ResearchSceneCompletionGate.EvaluateOral(
                "03", 1, 0, 0, true);
            Assert.IsFalse(missingBaseline.DataComplete);
            StringAssert.Contains("confirmed_transcript", missingBaseline.MissingRequiredData);
            StringAssert.Contains("audio_artifact", missingBaseline.MissingRequiredData);

            ResearchSceneCompletionStatus transferViolation = ResearchSceneCompletionGate.EvaluateOral(
                "05", 1, 1, 1, false);
            Assert.IsFalse(transferViolation.DataComplete);
            StringAssert.Contains("no_coach_verified", transferViolation.MissingRequiredData);
        }

        [Test]
        public void OralPracticeDeclaresBaselineAndTransferCompletionEvents()
        {
            string scripts = Path.Combine(Application.dataPath, "Game", "Scripts");
            string oral = File.ReadAllText(Path.Combine(scripts,
                "PlayerOralPracticeController.cs"));

            StringAssert.Contains("baseline_completed", oral);
            StringAssert.Contains("transfer_completed", oral);
            StringAssert.Contains("transcript_available", oral);
            StringAssert.Contains("audio_available", oral);
            StringAssert.Contains("no_coach_verified", oral);
        }

        [Test]
        public void MissingDataFromAnEarlierScenePersistsIntoFinalSessionCompleteness()
        {
            string root = Path.Combine(Path.GetTempPath(), "research-cross-scene-missing-" + Guid.NewGuid().ToString("N"));
            GameObject gameObject = new("Research Cross Scene Missing Test");
            try
            {
                ResearchSessionManager manager = gameObject.AddComponent<ResearchSessionManager>();
                manager.StartSession("P31", root, "S31");
                manager.EnterStage("04", "coach_supported_practice",
                    ResearchSceneContract.PracticeTopicId, ResearchSceneContract.PracticeTopic);
                ResearchCapture.CompleteScene("incomplete", "scene04:revision_speech");

                manager.EnterStage("05", "unsupported_transfer",
                    ResearchSceneContract.TransferTopicId, ResearchSceneContract.TransferTopic);
                ResearchCapture.CompleteScene("completed");

                Assert.IsFalse(manager.Current.DataComplete);
                StringAssert.Contains("scene04:revision_speech", manager.Current.MissingRequiredData);
                string summary = File.ReadAllText(Path.Combine(
                    manager.Current.SessionDirectory, "completion_summary.json"));
                StringAssert.Contains("scene04:revision_speech", summary);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(gameObject);
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }

        [Test]
        public void PracticeCompletionGateRequiresAllResponsesAudioDiagnosesAndCoachEpisodes()
        {
            ResearchSceneCompletionStatus complete = ResearchSceneCompletionGate.EvaluatePractice(
                2, 2, true, true, 5, 5, 6, 3);
            Assert.IsTrue(complete.DataComplete);
            Assert.IsEmpty(complete.MissingRequiredData);

            ResearchSceneCompletionStatus missing = ResearchSceneCompletionGate.EvaluatePractice(
                1, 0, true, false, 4, 3, 2, 1);
            Assert.IsFalse(missing.DataComplete);
            StringAssert.Contains("micro_independent_cycle_2", missing.MissingRequiredData);
            StringAssert.Contains("micro_revision_cycle_1", missing.MissingRequiredData);
            StringAssert.Contains("revision_speech", missing.MissingRequiredData);
            StringAssert.Contains("scene04_audio_artifacts", missing.MissingRequiredData);
            StringAssert.Contains("scene04_diagnoses", missing.MissingRequiredData);
            StringAssert.Contains("coach_episodes", missing.MissingRequiredData);
        }

        [Test]
        public void Scene04DeclaresDiagnosisFullCoachAndCompletionPayloads()
        {
            string scripts = Path.Combine(Application.dataPath, "Game", "Scripts");
            string scene04 = File.ReadAllText(Path.Combine(scripts,
                "ThreeStageDebatePracticeController.cs"));

            StringAssert.Contains("diagnosis_completed", scene04);
            StringAssert.Contains("target_response_id", scene04);
            StringAssert.Contains("latency_ms", scene04);
            StringAssert.Contains("error_code", scene04);
            StringAssert.Contains("full_speech_coach_episode", scene04);
            StringAssert.Contains("practice_completed", scene04);
            StringAssert.Contains("coach_episode_count", scene04);
            StringAssert.Contains("total_coach_seconds", scene04);
            StringAssert.Contains("completion_status", scene04);
        }

        [Test]
        public void PilotAuditAcceptsACompleteCurrentPrototypeSession()
        {
            string root = Path.Combine(Path.GetTempPath(),
                "research-pilot-audit-pass-" + Guid.NewGuid().ToString("N"));
            try
            {
                string sessionDirectory = CreateCompletePilotSession(root);
                Type service = typeof(CoachResearchLogger).Assembly.GetType(
                    "Game.Debate.ResearchPilotAuditService");
                Assert.IsNotNull(service, "The pilot audit service must exist.");
                if (service == null) return;

                object audit = service.GetMethod("AuditSession")?.Invoke(
                    null, new object[] { sessionDirectory });
                Assert.IsNotNull(audit);
                Assert.IsTrue((bool)audit.GetType().GetField("Passed")?.GetValue(audit));
                Assert.AreEqual(3,
                    audit.GetType().GetField("SceneTransitionCount")?.GetValue(audit));
                Assert.AreEqual("01>03|03>04|04>05",
                    audit.GetType().GetField("SceneTransitionSequence")?.GetValue(audit));
                Assert.IsTrue((bool)audit.GetType().GetField("FormalTransitionsValid")?.GetValue(audit));
                CollectionAssert.IsEmpty((string[])audit.GetType().GetField("Issues")?.GetValue(audit));
            }
            finally
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }

        [Test]
        public void PilotAuditRejectsScene05CoachPollution()
        {
            string root = Path.Combine(Path.GetTempPath(),
                "research-pilot-audit-coach-" + Guid.NewGuid().ToString("N"));
            try
            {
                string sessionDirectory = CreateCompletePilotSession(root);
                new ResearchEventSink(sessionDirectory).WriteEvent(new ResearchLogEvent
                {
                    SchemaVersion = ResearchSessionPaths.SchemaVersion,
                    ParticipantId = "P-AUDIT",
                    SessionId = "S-AUDIT",
                    Condition = CoachOrchestrationMode.SharedControl.ToString(),
                    SceneId = "05",
                    TopicId = ResearchSceneContract.TransferTopicId,
                    EventType = "coach_feedback_shown",
                    Actor = "coach",
                    EventTimestampUtc = DateTimeOffset.UtcNow.ToString("o")
                });

                Type service = typeof(CoachResearchLogger).Assembly.GetType(
                    "Game.Debate.ResearchPilotAuditService");
                Assert.IsNotNull(service, "The pilot audit service must exist.");
                if (service == null) return;

                object audit = service.GetMethod("AuditSession")?.Invoke(
                    null, new object[] { sessionDirectory });
                Assert.IsNotNull(audit);
                Assert.IsFalse((bool)audit.GetType().GetField("Passed")?.GetValue(audit));
                CollectionAssert.Contains(
                    (string[])audit.GetType().GetField("Issues")?.GetValue(audit),
                    "scene05_coach_pollution");
            }
            finally
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }

        [Test]
        public void PilotAuditRejectsEarlyRecordsWithTheWrongCondition()
        {
            string root = Path.Combine(Path.GetTempPath(),
                "research-pilot-audit-condition-" + Guid.NewGuid().ToString("N"));
            try
            {
                string sessionDirectory = CreateCompletePilotSession(root);
                new ResearchEventSink(sessionDirectory).WriteEvent(new ResearchLogEvent
                {
                    SchemaVersion = ResearchSessionPaths.SchemaVersion,
                    ParticipantId = "P-AUDIT",
                    SessionId = "S-AUDIT",
                    Condition = CoachOrchestrationMode.Disabled.ToString(),
                    SceneId = "01",
                    TopicId = "common_instruction_v1",
                    EventType = "learning_session_started",
                    Actor = "system",
                    EventTimestampUtc = DateTimeOffset.UtcNow.ToString("o")
                });

                Type service = typeof(CoachResearchLogger).Assembly.GetType(
                    "Game.Debate.ResearchPilotAuditService");
                object audit = service?.GetMethod("AuditSession")?.Invoke(
                    null, new object[] { sessionDirectory });
                Assert.IsNotNull(audit);
                Assert.IsFalse((bool)audit.GetType().GetField("Passed")?.GetValue(audit));
                Assert.AreEqual(1,
                    audit.GetType().GetField("ConditionMismatchRecordCount")?.GetValue(audit));
                CollectionAssert.Contains(
                    (string[])audit.GetType().GetField("Issues")?.GetValue(audit),
                    "condition_record_mismatch");
            }
            finally
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }

        [Test]
        public void PilotAuditRejectsACompleteSessionWithoutFormalTransitionEvents()
        {
            string root = Path.Combine(Path.GetTempPath(),
                "research-pilot-audit-transitions-missing-" + Guid.NewGuid().ToString("N"));
            try
            {
                string sessionDirectory = CreateCompletePilotSession(root, false);
                object audit = ResearchPilotAuditService.AuditSession(sessionDirectory);
                Type auditType = audit.GetType();

                Assert.IsFalse((bool)auditType.GetField("Passed")?.GetValue(audit));
                Assert.IsNotNull(auditType.GetField("SceneTransitionCount"));
                Assert.AreEqual(0, auditType.GetField("SceneTransitionCount")?.GetValue(audit));
                Assert.IsNotNull(auditType.GetField("FormalTransitionsValid"));
                Assert.IsFalse((bool)auditType.GetField("FormalTransitionsValid")?.GetValue(audit));
                CollectionAssert.Contains(
                    (string[])auditType.GetField("Issues")?.GetValue(audit),
                    "scene_transition_contract_invalid");
            }
            finally
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }

        [Test]
        public void PilotAuditRejectsDuplicateFormalTransitionEvents()
        {
            string root = Path.Combine(Path.GetTempPath(),
                "research-pilot-audit-transitions-duplicate-" + Guid.NewGuid().ToString("N"));
            try
            {
                string sessionDirectory = CreateCompletePilotSession(root);
                ResearchEventSink sink = new(sessionDirectory);
                WritePilotTransitionEvent(sink, "04", "05", "05Level_PlayerVsNPCDebate 1",
                    ResearchSceneContract.PracticeTopicId);

                object audit = ResearchPilotAuditService.AuditSession(sessionDirectory);
                Type auditType = audit.GetType();

                Assert.IsFalse((bool)auditType.GetField("Passed")?.GetValue(audit));
                Assert.IsNotNull(auditType.GetField("SceneTransitionCount"));
                Assert.AreEqual(4, auditType.GetField("SceneTransitionCount")?.GetValue(audit));
                Assert.IsNotNull(auditType.GetField("FormalTransitionsValid"));
                Assert.IsFalse((bool)auditType.GetField("FormalTransitionsValid")?.GetValue(audit));
                CollectionAssert.Contains(
                    (string[])auditType.GetField("Issues")?.GetValue(audit),
                    "scene_transition_contract_invalid");
            }
            finally
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }

        [Test]
        public void PilotAuditExportsOneClearCsvRowPerSession()
        {
            string root = Path.Combine(Path.GetTempPath(),
                "research-pilot-audit-csv-" + Guid.NewGuid().ToString("N"));
            try
            {
                CreateCompletePilotSession(root);
                Type service = typeof(CoachResearchLogger).Assembly.GetType(
                    "Game.Debate.ResearchPilotAuditService");
                Assert.IsNotNull(service, "The pilot audit service must exist.");
                if (service == null) return;

                string csvPath = (string)service.GetMethod("ExportRootSummary")?.Invoke(
                    null, new object[] { root, null });
                Assert.IsTrue(File.Exists(csvPath));
                string[] lines = File.ReadAllLines(csvPath);
                Assert.AreEqual(2, lines.Length);
                StringAssert.Contains("audit_passed", lines[0]);
                StringAssert.Contains("formal_transitions_valid", lines[0]);
                StringAssert.Contains("scene_transition_count", lines[0]);
                StringAssert.Contains("scene05_coach_event_count", lines[0]);
                StringAssert.Contains("P-AUDIT", lines[1]);
                StringAssert.Contains("SharedControl", lines[1]);
            }
            finally
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }

        [Test]
        public void UnityEditorExposesPilotAuditExportMenu()
        {
            string menuPath = Path.Combine(Application.dataPath, "Editor",
                "ResearchPilotAuditMenu.cs");
            Assert.IsTrue(File.Exists(menuPath));
            string source = File.ReadAllText(menuPath);
            StringAssert.Contains("Tools/Research/Export Pilot Audit Summary", source);
            StringAssert.Contains("ResearchSessionPaths.GetDefaultRootDirectory", source);
            StringAssert.Contains("ResearchPilotAuditService.ExportRootSummary", source);
        }

        private static string CreateCompletePilotSession(
            string root,
            bool includeFormalTransitions = true)
        {
            string sessionDirectory = ResearchSessionPaths.BuildSessionDirectory(
                root, "P-AUDIT", "S-AUDIT");
            ResearchEventSink sink = new(sessionDirectory);
            sink.WriteManifest(new ResearchSessionSnapshot
            {
                SchemaVersion = ResearchSessionPaths.SchemaVersion,
                StudyId = ResearchSessionPaths.StudyId,
                ParticipantId = "P-AUDIT",
                SessionId = "S-AUDIT",
                Condition = CoachOrchestrationMode.SharedControl,
                SessionDirectory = sessionDirectory,
                Status = "completed",
                CurrentSceneId = "05",
                CurrentStage = "unsupported_transfer",
                TopicId = ResearchSceneContract.TransferTopicId,
                TopicText = ResearchSceneContract.TransferTopic,
                DataComplete = true
            });

            WritePilotEvent(sink, "condition_assigned", "01", "common_instruction_v1", "system");
            WritePilotEvent(sink, "scene_entered", "01", "common_instruction_v1", "system");
            WritePilotEvent(sink, "demo_viewed", "01", "common_instruction_v1", "learner");
            if (includeFormalTransitions)
                WritePilotTransitionEvent(sink, "01", "03", "03Level_PlayerVsNPCDebate",
                    "common_instruction_v1");
            WritePilotEvent(sink, "scene_entered", "03", ResearchSceneContract.PracticeTopicId, "system");
            WritePilotEvent(sink, "baseline_preparation_completed", "03", ResearchSceneContract.PracticeTopicId, "learner");
            WritePilotEvent(sink, "baseline_completed", "03", ResearchSceneContract.PracticeTopicId, "system");
            if (includeFormalTransitions)
                WritePilotTransitionEvent(sink, "03", "04", "04 coach Agent",
                    ResearchSceneContract.PracticeTopicId);
            WritePilotEvent(sink, "scene_entered", "04", ResearchSceneContract.PracticeTopicId, "system");
            for (int index = 0; index < 6; index++)
                WritePilotEvent(sink, "diagnosis_completed", "04", ResearchSceneContract.PracticeTopicId, "coach");
            WritePilotEvent(sink, "full_speech_coach_episode", "04", ResearchSceneContract.PracticeTopicId, "coach");
            WritePilotEvent(sink, "practice_completed", "04", ResearchSceneContract.PracticeTopicId, "system");
            if (includeFormalTransitions)
                WritePilotTransitionEvent(sink, "04", "05", "05Level_PlayerVsNPCDebate 1",
                    ResearchSceneContract.PracticeTopicId);
            WritePilotEvent(sink, "scene_entered", "05", ResearchSceneContract.TransferTopicId, "system");
            WritePilotEvent(sink, "transfer_completed", "05", ResearchSceneContract.TransferTopicId, "system");

            string[] roles =
            {
                "same_topic_baseline",
                "micro_initial_statement",
                "micro_independent_response",
                "micro_independent_response",
                "micro_revision",
                "micro_revision",
                "full_speech",
                "revision_speech",
                "transfer_speech"
            };
            for (int index = 0; index < roles.Length; index++)
            {
                string role = roles[index];
                string sceneId = index == 0 ? "03" : index == roles.Length - 1 ? "05" : "04";
                string topicId = sceneId == "05"
                    ? ResearchSceneContract.TransferTopicId
                    : ResearchSceneContract.PracticeTopicId;
                string attemptId = "attempt-" + index;
                sink.WriteTranscript(new ResearchTranscriptRecord
                {
                    SchemaVersion = ResearchSessionPaths.SchemaVersion,
                    ParticipantId = "P-AUDIT",
                    SessionId = "S-AUDIT",
                    Condition = CoachOrchestrationMode.SharedControl.ToString(),
                    SceneId = sceneId,
                    TopicId = topicId,
                    AttemptId = attemptId,
                    ResponseId = "response-" + index,
                    ResponseRole = role,
                    TranscriptText = "Complete test response " + index,
                    RecordingDurationSeconds = 0.01f,
                    ConfirmedAtUtc = DateTimeOffset.UtcNow.ToString("o")
                });
                ResearchAudioArtifactRecord artifact = new ResearchAudioRecorder().SaveCapture(
                    sessionDirectory, sceneId, role, attemptId,
                    new XfyunAudioCapture(new byte[320], 16000, 1));
                artifact.SchemaVersion = ResearchSessionPaths.SchemaVersion;
                artifact.ParticipantId = "P-AUDIT";
                artifact.SessionId = "S-AUDIT";
                artifact.SceneId = sceneId;
                artifact.TopicId = topicId;
                sink.WriteAudioArtifact(artifact);
            }

            return sessionDirectory;
        }

        private static void WritePilotEvent(
            ResearchEventSink sink,
            string eventType,
            string sceneId,
            string topicId,
            string actor)
        {
            sink.WriteEvent(new ResearchLogEvent
            {
                SchemaVersion = ResearchSessionPaths.SchemaVersion,
                ParticipantId = "P-AUDIT",
                SessionId = "S-AUDIT",
                Condition = CoachOrchestrationMode.SharedControl.ToString(),
                SceneId = sceneId,
                TopicId = topicId,
                EventId = Guid.NewGuid().ToString("N"),
                EventTimestampUtc = DateTimeOffset.UtcNow.ToString("o"),
                EventType = eventType,
                Actor = actor
            });
        }

        private static void WritePilotTransitionEvent(
            ResearchEventSink sink,
            string fromSceneId,
            string toSceneId,
            string toSceneName,
            string topicId)
        {
            sink.WriteEvent(new ResearchLogEvent
            {
                SchemaVersion = ResearchSessionPaths.SchemaVersion,
                ParticipantId = "P-AUDIT",
                SessionId = "S-AUDIT",
                Condition = CoachOrchestrationMode.SharedControl.ToString(),
                SceneId = fromSceneId,
                TopicId = topicId,
                EventId = Guid.NewGuid().ToString("N"),
                EventTimestampUtc = DateTimeOffset.UtcNow.ToString("o"),
                EventType = "scene_transition_requested",
                Actor = "system",
                PayloadJson = JsonConvert.SerializeObject(new
                {
                    from_scene_id = fromSceneId,
                    to_scene_id = toSceneId,
                    to_scene_name = toSceneName,
                    data_complete = true
                })
            });
        }

        [Test]
        public void SessionCanStartWithAnAssignedConditionBeforeSceneEvents()
        {
            string root = Path.Combine(Path.GetTempPath(),
                "research-initial-condition-" + Guid.NewGuid().ToString("N"));
            GameObject gameObject = new("Research Initial Condition Test");
            try
            {
                ResearchSessionManager manager = gameObject.AddComponent<ResearchSessionManager>();
                MethodInfo start = typeof(ResearchSessionManager).GetMethod("StartSession",
                    new[]
                    {
                        typeof(string), typeof(string), typeof(string),
                        typeof(CoachOrchestrationMode), typeof(string), typeof(string)
                    });
                Assert.IsNotNull(start, "StartSession must accept the initial assigned condition.");
                if (start == null) return;

                start.Invoke(manager, new object[]
                {
                    "P-SETUP", root, "S-SETUP", CoachOrchestrationMode.SharedControl,
                    "scene01_researcher_setup", "manual-seed"
                });

                Assert.AreEqual(CoachOrchestrationMode.SharedControl, manager.Current.Condition);
                ResearchLogEvent[] events = File.ReadAllLines(Path.Combine(
                        manager.Current.SessionDirectory, "events.jsonl"))
                    .Where(line => !string.IsNullOrWhiteSpace(line))
                    .Select(JsonConvert.DeserializeObject<ResearchLogEvent>)
                    .ToArray();
                Assert.IsTrue(events.Any(row => row.EventType == "condition_assigned"));
                Assert.IsTrue(events.All(row =>
                    row.Condition == CoachOrchestrationMode.SharedControl.ToString()));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(gameObject);
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }

        [Test]
        public void StudySetupCoordinatorRejectsCrossSceneIdentityOrConditionChanges()
        {
            string root = Path.Combine(Path.GetTempPath(),
                "research-setup-coordinator-" + Guid.NewGuid().ToString("N"));
            GameObject gameObject = new("Research Setup Coordinator Test");
            try
            {
                gameObject.AddComponent<ResearchSessionManager>();
                Type coordinator = typeof(CoachResearchLogger).Assembly.GetType(
                    "Game.Debate.ResearchStudySetupCoordinator");
                Assert.IsNotNull(coordinator, "The centralized study setup coordinator must exist.");
                if (coordinator == null) return;
                MethodInfo begin = coordinator.GetMethod("BeginOrReuse");
                Assert.IsNotNull(begin);

                object initial = begin.Invoke(null, new object[]
                {
                    "P-CONSISTENT", CoachOrchestrationMode.LearnerLed,
                    "S-CONSISTENT", "scene01_researcher_setup", "", root
                });
                Assert.IsTrue((bool)initial.GetType().GetField("Success")?.GetValue(initial));

                object wrongParticipant = begin.Invoke(null, new object[]
                {
                    "P-OTHER", CoachOrchestrationMode.LearnerLed,
                    null, "scene04_reuse", "", null
                });
                Assert.IsFalse((bool)wrongParticipant.GetType().GetField("Success")?.GetValue(wrongParticipant));
                Assert.AreEqual("participant_mismatch",
                    wrongParticipant.GetType().GetField("ErrorCode")?.GetValue(wrongParticipant));

                object wrongCondition = begin.Invoke(null, new object[]
                {
                    "P-CONSISTENT", CoachOrchestrationMode.AiLed,
                    null, "scene04_reuse", "", null
                });
                Assert.IsFalse((bool)wrongCondition.GetType().GetField("Success")?.GetValue(wrongCondition));
                Assert.AreEqual("condition_mismatch",
                    wrongCondition.GetType().GetField("ErrorCode")?.GetValue(wrongCondition));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(gameObject);
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }

        [Test]
        public void Scene01OwnsSetupAndScene04ReusesTheAssignedSession()
        {
            string scripts = Path.Combine(Application.dataPath, "Game", "Scripts");
            string scene01 = File.ReadAllText(Path.Combine(scripts,
                "NpcDebateLearningPhaseController.cs"));
            string scene04 = File.ReadAllText(Path.Combine(scripts,
                "ThreeStageDebatePracticeController.cs"));

            StringAssert.Contains("_researchParticipantInput", scene01);
            StringAssert.Contains("SelectResearchCondition", scene01);
            StringAssert.Contains("ResearchStudySetupCoordinator.BeginOrReuse", scene01);
            StringAssert.Contains("scene01_researcher_setup", scene01);
            StringAssert.Contains("TryBeginAssignedStudy", scene04);
            StringAssert.Contains("scene04_manager_reuse", scene04);
        }

        [Test]
        public void Scene01UsesACameraSetupCanvasBeforeShowingTheWorldTutorial()
        {
            string scene01 = File.ReadAllText(Path.Combine(
                Application.dataPath, "Game", "Scripts",
                "NpcDebateLearningPhaseController.cs"));

            StringAssert.Contains("Research Setup Screen Canvas", scene01);
            StringAssert.Contains("RenderMode.ScreenSpaceCamera", scene01);
            StringAssert.Contains("setupCanvas.worldCamera", scene01);
            StringAssert.Contains("_researchSetupCanvas?.SetActive(true)", scene01);
            StringAssert.Contains("_root?.SetActive(false)", scene01);
            StringAssert.Contains("_researchSetupCanvas?.SetActive(false)", scene01);
            StringAssert.Contains("_root?.SetActive(true)", scene01);
        }

        [Test]
        public void Scene04SetupDoesNotAskForParticipantIdAgain()
        {
            string scripts = Path.Combine(Application.dataPath, "Game", "Scripts");
            string view = File.ReadAllText(Path.Combine(scripts,
                "ThreeStageDebatePracticeView.cs"));
            string controller = File.ReadAllText(Path.Combine(scripts,
                "ThreeStageDebatePracticeController.cs"));

            StringAssert.DoesNotContain("_participantInput", view);
            StringAssert.DoesNotContain("Anonymous participant ID", view);
            StringAssert.Contains("ConfigureAssignedSession", view);
            StringAssert.Contains("SessionStartRequested?.Invoke(_assignedParticipantId", view);
            StringAssert.Contains("ConfigureAssignedSession(", controller);
            StringAssert.DoesNotContain(
                "BeginStudy(manager.Current.ParticipantId, manager.Current.Condition);",
                controller);
        }

        [Test]
        public void FormalStudyNavigatorDeclaresTheExpectedSceneChain()
        {
            Type navigator = typeof(CoachResearchLogger).Assembly.GetType(
                "Game.Debate.ResearchStudyFlowNavigator");
            Assert.IsNotNull(navigator, "A shared navigator must own the formal study scene chain.");
            if (navigator == null) return;

            MethodInfo tryGetNext = navigator.GetMethod("TryGetNextScene",
                BindingFlags.Public | BindingFlags.Static);
            Assert.IsNotNull(tryGetNext);
            if (tryGetNext == null) return;

            AssertFormalNextScene(tryGetNext, "01", true, "03",
                "03Level_PlayerVsNPCDebate");
            AssertFormalNextScene(tryGetNext, "03", true, "04", "04 coach Agent");
            AssertFormalNextScene(tryGetNext, "04", true, "05",
                "05Level_PlayerVsNPCDebate 1");
            AssertFormalNextScene(tryGetNext, "05", false, string.Empty, string.Empty);
        }

        [Test]
        public void FormalStudyNavigatorResolvesLoadableBuildIndices()
        {
            Type navigator = typeof(CoachResearchLogger).Assembly.GetType(
                "Game.Debate.ResearchStudyFlowNavigator");
            MethodInfo tryGetBuildIndex = navigator?.GetMethod("TryGetNextBuildIndex",
                BindingFlags.Public | BindingFlags.Static);
            Assert.IsNotNull(tryGetBuildIndex,
                "Runtime navigation must resolve the enabled Build Settings index, not use the scene-name loadability overload.");
            if (tryGetBuildIndex == null) return;

            object[] from01 = { "01", -1 };
            Assert.IsTrue((bool)tryGetBuildIndex.Invoke(null, from01));
            Assert.AreEqual(1, from01[1]);
            Assert.IsTrue(Application.CanStreamedLevelBeLoaded((int)from01[1]));

            object[] from03 = { "03", -1 };
            Assert.IsTrue((bool)tryGetBuildIndex.Invoke(null, from03));
            Assert.AreEqual(2, from03[1]);

            object[] from04 = { "04", -1 };
            Assert.IsTrue((bool)tryGetBuildIndex.Invoke(null, from04));
            Assert.AreEqual(3, from04[1]);
        }

        [Test]
        public void NpcRoundManagerExposesAOneShotRoundCompletedEvent()
        {
            EventInfo completed = typeof(NpcDebateRoundManager).GetEvent("RoundCompleted");
            Assert.IsNotNull(completed,
                "Scene 01 must be able to wait for the NPC round before recording completion.");

            string source = File.ReadAllText(Path.Combine(Application.dataPath, "Game", "Scripts",
                "NpcDebateRoundManager.cs"));
            StringAssert.Contains("RaiseRoundCompleted", source);
            StringAssert.Contains("_roundCompletionRaised", source);

            GameObject gameObject = new("NPC Round Completion Test");
            try
            {
                NpcDebateRoundManager manager = gameObject.AddComponent<NpcDebateRoundManager>();
                int completionCount = 0;
                manager.RoundCompleted += () => completionCount++;
                MethodInfo raise = typeof(NpcDebateRoundManager).GetMethod(
                    "RaiseRoundCompleted", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.IsNotNull(raise);
                raise?.Invoke(manager, null);
                raise?.Invoke(manager, null);
                Assert.AreEqual(1, completionCount,
                    "Duplicate closing paths must not trigger two scene transitions.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void FormalSceneControllersUseTheSharedNavigatorAtTrueCompletionPoints()
        {
            string scripts = Path.Combine(Application.dataPath, "Game", "Scripts");
            string scene01 = File.ReadAllText(Path.Combine(scripts,
                "NpcDebateLearningPhaseController.cs"));
            string scene03 = File.ReadAllText(Path.Combine(scripts,
                "PlayerOralPracticeController.cs"));
            string scene04 = File.ReadAllText(Path.Combine(scripts,
                "ThreeStageDebatePracticeController.cs"));

            StringAssert.Contains("HandleRoundCompleted", scene01);
            StringAssert.Contains("ResearchStudyFlowNavigator.TryLoadNextScene(\"01\"", scene01);
            StringAssert.Contains("ResearchStudyFlowNavigator.TryLoadNextScene(\"03\"", scene03);
            StringAssert.Contains("ResearchStudyFlowNavigator.TryLoadNextScene(\"04\"", scene04);
            StringAssert.DoesNotContain("SceneManager.LoadScene(transferSceneName)", scene04);

            int handlerIndex = scene01.IndexOf("HandleRoundCompleted", StringComparison.Ordinal);
            int completeIndex = scene01.IndexOf("ResearchCapture.CompleteScene", handlerIndex,
                StringComparison.Ordinal);
            Assert.GreaterOrEqual(handlerIndex, 0);
            Assert.Greater(completeIndex, handlerIndex,
                "Scene 01 completion must be recorded by the NPC-round completion handler.");
        }

        private static void AssertFormalNextScene(
            MethodInfo method,
            string fromSceneId,
            bool expectedResult,
            string expectedNextId,
            string expectedNextName)
        {
            object[] arguments = { fromSceneId, null, null };
            bool result = (bool)method.Invoke(null, arguments);
            Assert.AreEqual(expectedResult, result, "Unexpected mapping result for scene " + fromSceneId);
            Assert.AreEqual(expectedNextId, arguments[1] as string);
            Assert.AreEqual(expectedNextName, arguments[2] as string);
        }
    }
}
