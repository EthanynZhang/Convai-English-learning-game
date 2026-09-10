using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Game.Debate
{
    [Serializable]
    public sealed class ResearchPilotAuditRecord
    {
        public int SchemaVersion;
        public string ParticipantId = string.Empty;
        public string SessionId = string.Empty;
        public string Condition = string.Empty;
        public string SessionStatus = string.Empty;
        public bool Passed;
        public string SceneSequence = string.Empty;
        public int SceneTransitionCount;
        public string SceneTransitionSequence = string.Empty;
        public bool FormalTransitionsValid;
        public bool PracticeTopicValid;
        public bool TransferTopicValid;
        public int ConditionMismatchRecordCount;
        public int ConditionAssignedCount;
        public int DemoViewedCount;
        public int BaselinePreparationCount;
        public int BaselineCompletionCount;
        public int DiagnosisCount;
        public int FullSpeechCoachEpisodeCount;
        public int PracticeCompletionCount;
        public int TransferCompletionCount;
        public int TranscriptCount;
        public int AudioArtifactCount;
        public int MissingAudioAttemptCount;
        public int OrphanAudioAttemptCount;
        public int MissingAudioFileCount;
        public int Sha256MismatchCount;
        public int Scene05CoachEventCount;
        public int UnrecoveredTechnicalEventCount;
        public string[] Issues = Array.Empty<string>();
        public string SessionDirectory = string.Empty;
    }

    public static class ResearchPilotAuditService
    {
        private static readonly Encoding Utf8WithoutBom = new UTF8Encoding(false);
        private static readonly string[] FormalSceneOrder = { "01", "03", "04", "05" };
        private static readonly string[] FormalTransitionSequence =
        {
            "01>03", "03>04", "04>05"
        };
        private static readonly string[] FormalTransitionSceneNames =
        {
            "03Level_PlayerVsNPCDebate", "04 coach Agent", "05Level_PlayerVsNPCDebate 1"
        };

        private static readonly KeyValuePair<string, int>[] RequiredResponseRoles =
        {
            new("same_topic_baseline", 1),
            new("micro_initial_statement", 1),
            new("micro_independent_response", 2),
            new("micro_revision", 2),
            new("full_speech", 1),
            new("revision_speech", 1),
            new("transfer_speech", 1)
        };

        public static ResearchPilotAuditRecord AuditSession(string sessionDirectory)
        {
            if (string.IsNullOrWhiteSpace(sessionDirectory))
                throw new ArgumentException("A session directory is required.", nameof(sessionDirectory));

            string fullDirectory = Path.GetFullPath(sessionDirectory);
            ResearchPilotAuditRecord audit = new()
            {
                SchemaVersion = ResearchSessionPaths.SchemaVersion,
                SessionDirectory = fullDirectory
            };
            List<string> issues = new();

            ResearchSessionSnapshot manifest = ReadManifest(fullDirectory, issues);
            if (manifest != null)
            {
                audit.SchemaVersion = manifest.SchemaVersion;
                audit.ParticipantId = manifest.ParticipantId;
                audit.SessionId = manifest.SessionId;
                audit.Condition = manifest.Condition.ToString();
                audit.SessionStatus = manifest.Status;
                if (manifest.Condition == CoachOrchestrationMode.Disabled)
                    AddIssue(issues, "condition_missing");
                if (!string.Equals(manifest.Status, "completed", StringComparison.OrdinalIgnoreCase) ||
                    !manifest.DataComplete)
                    AddIssue(issues, "manifest_incomplete");
            }

            List<ResearchLogEvent> events = ReadJsonLines<ResearchLogEvent>(
                Path.Combine(fullDirectory, "events.jsonl"), "events_invalid", issues);
            List<ResearchTranscriptRecord> transcripts = ReadJsonLines<ResearchTranscriptRecord>(
                Path.Combine(fullDirectory, "transcripts.jsonl"), "transcripts_invalid", issues);
            List<ResearchAudioArtifactRecord> audio = ReadJsonLines<ResearchAudioArtifactRecord>(
                Path.Combine(fullDirectory, "audio_artifacts.jsonl"), "audio_artifacts_invalid", issues);
            List<ResearchTechnicalEvent> technical = ReadJsonLines<ResearchTechnicalEvent>(
                Path.Combine(fullDirectory, "technical_events.jsonl"),
                "technical_events_invalid", issues);

            if (manifest != null && manifest.Condition != CoachOrchestrationMode.Disabled)
            {
                string expectedCondition = manifest.Condition.ToString();
                audit.ConditionMismatchRecordCount = events.Count(row => !string.Equals(
                                                         Clean(row.Condition), expectedCondition,
                                                         StringComparison.Ordinal)) +
                                                     transcripts.Count(row => !string.Equals(
                                                         Clean(row.Condition), expectedCondition,
                                                         StringComparison.Ordinal));
                if (audit.ConditionMismatchRecordCount > 0)
                    AddIssue(issues, "condition_record_mismatch");
            }

            audit.ConditionAssignedCount = CountEvents(events, "condition_assigned");
            audit.DemoViewedCount = CountEvents(events, "demo_viewed");
            audit.BaselinePreparationCount = CountEvents(events, "baseline_preparation_completed");
            audit.BaselineCompletionCount = CountEvents(events, "baseline_completed");
            audit.DiagnosisCount = CountEvents(events, "diagnosis_completed");
            audit.FullSpeechCoachEpisodeCount = CountEvents(events, "full_speech_coach_episode");
            audit.PracticeCompletionCount = CountEvents(events, "practice_completed");
            audit.TransferCompletionCount = CountEvents(events, "transfer_completed");
            RequireCount(audit.ConditionAssignedCount, 1, "condition_assigned_missing", issues);
            RequireCount(audit.DemoViewedCount, 1, "demo_viewed_missing", issues);
            RequireCount(audit.BaselinePreparationCount, 1, "baseline_preparation_missing", issues);
            RequireCount(audit.BaselineCompletionCount, 1, "baseline_completion_missing", issues);
            RequireCount(audit.DiagnosisCount, 6, "scene04_diagnoses_incomplete", issues);
            RequireCount(audit.FullSpeechCoachEpisodeCount, 1,
                "full_speech_coach_episode_missing", issues);
            RequireCount(audit.PracticeCompletionCount, 1, "practice_completion_missing", issues);
            RequireCount(audit.TransferCompletionCount, 1, "transfer_completion_missing", issues);

            string[] sequence = CollapseConsecutive(events
                .Where(row => EventIs(row, "scene_entered"))
                .Select(row => Clean(row.SceneId)));
            audit.SceneSequence = string.Join(">", sequence);
            if (!sequence.SequenceEqual(FormalSceneOrder, StringComparer.Ordinal))
                AddIssue(issues, "scene_sequence_invalid");

            ResearchLogEvent[] transitionEvents = events
                .Where(row => EventIs(row, "scene_transition_requested"))
                .ToArray();
            audit.SceneTransitionCount = transitionEvents.Length;
            List<string> transitionSequence = new();
            bool transitionPayloadsValid = true;
            for (int index = 0; index < transitionEvents.Length; index++)
            {
                if (!TryReadFormalTransition(
                        transitionEvents[index],
                        out string signature,
                        out string toSceneName))
                {
                    transitionPayloadsValid = false;
                    transitionSequence.Add("invalid");
                    continue;
                }

                transitionSequence.Add(signature);
                if (index >= FormalTransitionSceneNames.Length ||
                    !string.Equals(toSceneName, FormalTransitionSceneNames[index],
                        StringComparison.Ordinal))
                    transitionPayloadsValid = false;
            }
            audit.SceneTransitionSequence = string.Join("|", transitionSequence);
            audit.FormalTransitionsValid = transitionPayloadsValid &&
                                           transitionSequence.SequenceEqual(
                                               FormalTransitionSequence,
                                               StringComparer.Ordinal);
            if (!audit.FormalTransitionsValid)
                AddIssue(issues, "scene_transition_contract_invalid");

            audit.PracticeTopicValid = TopicIsValid(events, transcripts, "03",
                                                ResearchSceneContract.PracticeTopicId) &&
                                           TopicIsValid(events, transcripts, "04",
                                                ResearchSceneContract.PracticeTopicId);
            audit.TransferTopicValid = TopicIsValid(events, transcripts, "05",
                ResearchSceneContract.TransferTopicId);
            if (!audit.PracticeTopicValid) AddIssue(issues, "practice_topic_invalid");
            if (!audit.TransferTopicValid) AddIssue(issues, "transfer_topic_invalid");

            foreach (KeyValuePair<string, int> requirement in RequiredResponseRoles)
            {
                int count = transcripts.Count(row => string.Equals(
                    Clean(row.ResponseRole), requirement.Key, StringComparison.Ordinal));
                RequireCount(count, requirement.Value,
                    "response_role_missing:" + requirement.Key, issues);
            }

            audit.TranscriptCount = transcripts.Count;
            audit.AudioArtifactCount = audio.Count;
            string[] transcriptAttempts = transcripts.Select(row => Clean(row.AttemptId))
                .Where(value => value.Length > 0).Distinct(StringComparer.Ordinal).ToArray();
            string[] audioAttempts = audio.Select(row => Clean(row.AttemptId))
                .Where(value => value.Length > 0).Distinct(StringComparer.Ordinal).ToArray();
            audit.MissingAudioAttemptCount = transcriptAttempts
                .Except(audioAttempts, StringComparer.Ordinal).Count();
            audit.OrphanAudioAttemptCount = audioAttempts
                .Except(transcriptAttempts, StringComparer.Ordinal).Count();
            if (audit.MissingAudioAttemptCount > 0) AddIssue(issues, "audio_pairing_missing");
            if (audit.OrphanAudioAttemptCount > 0) AddIssue(issues, "audio_pairing_orphan");

            foreach (ResearchAudioArtifactRecord artifact in audio)
            {
                string fullAudioPath = ResolveArtifactPath(fullDirectory, artifact.RelativePath);
                if (fullAudioPath.Length == 0 || !File.Exists(fullAudioPath))
                {
                    audit.MissingAudioFileCount++;
                    continue;
                }
                if (!string.IsNullOrWhiteSpace(artifact.Sha256) &&
                    !string.Equals(ComputeSha256(fullAudioPath), artifact.Sha256,
                        StringComparison.OrdinalIgnoreCase))
                    audit.Sha256MismatchCount++;
            }
            if (audit.MissingAudioFileCount > 0) AddIssue(issues, "audio_file_missing");
            if (audit.Sha256MismatchCount > 0) AddIssue(issues, "audio_sha256_mismatch");

            audit.Scene05CoachEventCount = events.Count(row =>
                string.Equals(Clean(row.SceneId), "05", StringComparison.Ordinal) &&
                (string.Equals(Clean(row.Actor), "coach", StringComparison.OrdinalIgnoreCase) ||
                 Clean(row.EventType).IndexOf("coach", StringComparison.OrdinalIgnoreCase) >= 0));
            if (audit.Scene05CoachEventCount > 0) AddIssue(issues, "scene05_coach_pollution");

            audit.UnrecoveredTechnicalEventCount = technical.Count(row => !row.Recovered);
            if (audit.UnrecoveredTechnicalEventCount > 0)
                AddIssue(issues, "unrecovered_technical_events");

            audit.Issues = issues.OrderBy(value => value, StringComparer.Ordinal).ToArray();
            audit.Passed = audit.Issues.Length == 0;
            return audit;
        }

        public static string ExportRootSummary(string rootDirectory, string outputCsvPath = null)
        {
            if (string.IsNullOrWhiteSpace(rootDirectory))
                throw new ArgumentException("A research data root directory is required.", nameof(rootDirectory));
            string fullRoot = Path.GetFullPath(rootDirectory);
            Directory.CreateDirectory(fullRoot);
            string output = string.IsNullOrWhiteSpace(outputCsvPath)
                ? Path.Combine(fullRoot, "pilot_audit_summary.csv")
                : Path.GetFullPath(outputCsvPath);
            string outputDirectory = Path.GetDirectoryName(output);
            if (!string.IsNullOrWhiteSpace(outputDirectory)) Directory.CreateDirectory(outputDirectory);

            ResearchPilotAuditRecord[] audits = Directory
                .GetFiles(fullRoot, "session_manifest.json", SearchOption.AllDirectories)
                .Select(Path.GetDirectoryName)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(AuditSession)
                .OrderBy(row => row.ParticipantId, StringComparer.Ordinal)
                .ThenBy(row => row.SessionId, StringComparer.Ordinal)
                .ToArray();
            WriteCsv(output, audits);
            return output;
        }

        private static ResearchSessionSnapshot ReadManifest(string directory, ICollection<string> issues)
        {
            string path = Path.Combine(directory, "session_manifest.json");
            if (!File.Exists(path))
            {
                AddIssue(issues, "manifest_missing");
                return null;
            }
            try
            {
                ResearchSessionSnapshot manifest =
                    JsonConvert.DeserializeObject<ResearchSessionSnapshot>(File.ReadAllText(path));
                if (manifest != null) return manifest;
            }
            catch (JsonException)
            {
            }
            AddIssue(issues, "manifest_invalid");
            return null;
        }

        private static List<T> ReadJsonLines<T>(
            string path,
            string invalidIssue,
            ICollection<string> issues) where T : class
        {
            List<T> records = new();
            if (!File.Exists(path)) return records;
            foreach (string line in File.ReadLines(path))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                try
                {
                    T record = JsonConvert.DeserializeObject<T>(line);
                    if (record != null) records.Add(record);
                    else AddIssue(issues, invalidIssue);
                }
                catch (JsonException)
                {
                    AddIssue(issues, invalidIssue);
                }
            }
            return records;
        }

        private static int CountEvents(IEnumerable<ResearchLogEvent> events, string eventType)
        {
            return events.Count(row => EventIs(row, eventType));
        }

        private static bool EventIs(ResearchLogEvent row, string eventType)
        {
            return string.Equals(Clean(row.EventType), eventType, StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryReadFormalTransition(
            ResearchLogEvent row,
            out string signature,
            out string toSceneName)
        {
            signature = string.Empty;
            toSceneName = string.Empty;
            if (row == null || string.IsNullOrWhiteSpace(row.PayloadJson)) return false;
            try
            {
                JObject payload = JObject.Parse(row.PayloadJson);
                string fromSceneId = Clean(payload.Value<string>("from_scene_id"));
                string toSceneId = Clean(payload.Value<string>("to_scene_id"));
                toSceneName = Clean(payload.Value<string>("to_scene_name"));
                bool dataComplete = payload.Value<bool?>("data_complete") == true;
                if (fromSceneId.Length == 0 || toSceneId.Length == 0 ||
                    toSceneName.Length == 0 || !dataComplete ||
                    !string.Equals(Clean(row.SceneId), fromSceneId, StringComparison.Ordinal))
                    return false;
                signature = fromSceneId + ">" + toSceneId;
                return true;
            }
            catch (JsonException)
            {
                return false;
            }
        }

        private static void RequireCount(
            int actual,
            int minimum,
            string issue,
            ICollection<string> issues)
        {
            if (actual < minimum) AddIssue(issues, issue);
        }

        private static bool TopicIsValid(
            IEnumerable<ResearchLogEvent> events,
            IEnumerable<ResearchTranscriptRecord> transcripts,
            string sceneId,
            string expectedTopicId)
        {
            string[] topicIds = events
                .Where(row => string.Equals(Clean(row.SceneId), sceneId, StringComparison.Ordinal))
                .Select(row => Clean(row.TopicId))
                .Concat(transcripts
                    .Where(row => string.Equals(Clean(row.SceneId), sceneId, StringComparison.Ordinal))
                    .Select(row => Clean(row.TopicId)))
                .Where(value => value.Length > 0)
                .ToArray();
            return topicIds.Length > 0 && topicIds.All(value =>
                string.Equals(value, expectedTopicId, StringComparison.Ordinal));
        }

        private static string[] CollapseConsecutive(IEnumerable<string> values)
        {
            List<string> collapsed = new();
            foreach (string value in values.Where(value => value.Length > 0))
            {
                if (collapsed.Count == 0 ||
                    !string.Equals(collapsed[collapsed.Count - 1], value, StringComparison.Ordinal))
                    collapsed.Add(value);
            }
            return collapsed.ToArray();
        }

        private static string ResolveArtifactPath(string sessionDirectory, string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath)) return string.Empty;
            string fullSession = Path.GetFullPath(sessionDirectory).TrimEnd(
                                     Path.DirectorySeparatorChar,
                                     Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string candidate = Path.GetFullPath(Path.Combine(sessionDirectory,
                relativePath.Replace('/', Path.DirectorySeparatorChar)));
            return candidate.StartsWith(fullSession, StringComparison.OrdinalIgnoreCase)
                ? candidate
                : string.Empty;
        }

        private static string ComputeSha256(string path)
        {
            using FileStream stream = File.OpenRead(path);
            using SHA256 sha = SHA256.Create();
            return BitConverter.ToString(sha.ComputeHash(stream))
                .Replace("-", string.Empty).ToLowerInvariant();
        }

        private static void WriteCsv(string path, IEnumerable<ResearchPilotAuditRecord> rows)
        {
            string[] header =
            {
                "schema_version", "participant_id", "session_id", "condition", "session_status",
                "audit_passed", "scene_sequence", "scene_transition_count",
                "scene_transition_sequence", "formal_transitions_valid",
                "practice_topic_valid", "transfer_topic_valid",
                "condition_mismatch_record_count", "condition_assigned_count", "demo_viewed_count", "baseline_preparation_count",
                "baseline_completion_count", "diagnosis_count", "full_speech_coach_episode_count",
                "practice_completion_count", "transfer_completion_count", "transcript_count",
                "audio_artifact_count", "missing_audio_attempt_count", "orphan_audio_attempt_count",
                "missing_audio_file_count", "sha256_mismatch_count", "scene05_coach_event_count",
                "unrecovered_technical_event_count", "issues", "session_directory"
            };
            StringBuilder csv = new();
            csv.AppendLine(string.Join(",", header.Select(EscapeCsv)));
            foreach (ResearchPilotAuditRecord row in rows)
            {
                string[] values =
                {
                    row.SchemaVersion.ToString(CultureInfo.InvariantCulture), row.ParticipantId,
                    row.SessionId, row.Condition, row.SessionStatus, Bool(row.Passed),
                    row.SceneSequence, Number(row.SceneTransitionCount),
                    row.SceneTransitionSequence, Bool(row.FormalTransitionsValid),
                    Bool(row.PracticeTopicValid), Bool(row.TransferTopicValid),
                    Number(row.ConditionMismatchRecordCount), Number(row.ConditionAssignedCount), Number(row.DemoViewedCount),
                    Number(row.BaselinePreparationCount), Number(row.BaselineCompletionCount),
                    Number(row.DiagnosisCount), Number(row.FullSpeechCoachEpisodeCount),
                    Number(row.PracticeCompletionCount), Number(row.TransferCompletionCount),
                    Number(row.TranscriptCount), Number(row.AudioArtifactCount),
                    Number(row.MissingAudioAttemptCount), Number(row.OrphanAudioAttemptCount),
                    Number(row.MissingAudioFileCount), Number(row.Sha256MismatchCount),
                    Number(row.Scene05CoachEventCount), Number(row.UnrecoveredTechnicalEventCount),
                    string.Join(";", row.Issues ?? Array.Empty<string>()), row.SessionDirectory
                };
                csv.AppendLine(string.Join(",", values.Select(EscapeCsv)));
            }
            File.WriteAllText(path, csv.ToString(), Utf8WithoutBom);
        }

        private static string EscapeCsv(string value)
        {
            string clean = value ?? string.Empty;
            return clean.IndexOfAny(new[] { ',', '"', '\r', '\n' }) < 0
                ? clean
                : "\"" + clean.Replace("\"", "\"\"") + "\"";
        }

        private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);
        private static string Bool(bool value) => value ? "true" : "false";
        private static string Clean(string value) => value?.Trim() ?? string.Empty;

        private static void AddIssue(ICollection<string> issues, string issue)
        {
            if (!issues.Contains(issue)) issues.Add(issue);
        }
    }
}
