using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using UnityEngine;

namespace Game.Debate
{
    public static class ResearchCapture
    {
        public static bool TryStartSession(
            string participantId,
            string sessionId = null,
            string rootDirectory = null,
            CoachOrchestrationMode initialCondition = CoachOrchestrationMode.Disabled,
            string assignmentMethod = "manual",
            string assignmentSeed = "")
        {
            ResearchSessionManager manager = ActiveManager();
            if (manager == null || manager.HasActiveSession) return false;
            try
            {
                manager.StartSession(participantId, rootDirectory, sessionId,
                    initialCondition, assignmentMethod, assignmentSeed);
                return true;
            }
            catch (ArgumentException exception)
            {
                Debug.LogWarning("Research session was not started: " + exception.Message);
                return false;
            }
            catch (InvalidOperationException exception)
            {
                Debug.LogWarning("Research session was not started: " + exception.Message);
                return false;
            }
        }

        public static bool TryAssignCondition(
            CoachOrchestrationMode condition,
            string assignmentMethod = "manual",
            string assignmentSeed = "")
        {
            ResearchSessionManager manager = ActiveManager();
            if (manager == null || !manager.HasActiveSession) return false;
            try
            {
                manager.AssignCondition(condition, assignmentMethod, assignmentSeed);
                return true;
            }
            catch (ArgumentException exception)
            {
                Debug.LogWarning("Research condition was not assigned: " + exception.Message);
                return false;
            }
            catch (InvalidOperationException exception)
            {
                Debug.LogWarning("Research condition was not assigned: " + exception.Message);
                return false;
            }
        }

        public static bool SyncCoachSession(CoachStudySessionSnapshot coachSession)
        {
            if (coachSession == null || string.IsNullOrWhiteSpace(coachSession.ParticipantId) ||
                coachSession.Mode == CoachOrchestrationMode.Disabled) return false;
            ResearchSessionManager manager = ActiveManager();
            if (manager == null) return false;
            if (!manager.HasActiveSession &&
                !TryStartSession(coachSession.ParticipantId, coachSession.SessionId)) return false;
            if (!string.Equals(manager.Current.ParticipantId,
                    ResearchSessionPaths.SanitizeSegment(coachSession.ParticipantId),
                    StringComparison.OrdinalIgnoreCase))
            {
                Debug.LogWarning("Coach session participant does not match the active research session.");
                return false;
            }
            if (manager.Current.Condition == coachSession.Mode) return true;
            return TryAssignCondition(coachSession.Mode, "coach_session_sync");
        }

        public static ResearchLogEvent RecordEvent(
            string eventType,
            string actor = "system",
            string recipient = "",
            object payload = null)
        {
            ResearchSessionManager manager = ActiveManager();
            if (manager == null || !manager.HasActiveSession ||
                string.IsNullOrWhiteSpace(eventType)) return null;
            return manager.RecordEvent(eventType, record =>
            {
                record.Actor = Clean(actor);
                record.Recipient = Clean(recipient);
                record.PayloadJson = payload == null
                    ? string.Empty
                    : JsonConvert.SerializeObject(payload);
            });
        }

        public static string BeginAttempt(string responseRole, string stance = "")
        {
            ResearchSessionManager manager = ActiveManager();
            if (manager == null || !manager.HasActiveSession) return string.Empty;
            string attemptId = Guid.NewGuid().ToString("N");
            manager.RecordEvent("recording_started", record =>
            {
                record.Actor = "learner";
                record.PayloadJson = JsonConvert.SerializeObject(new
                {
                    attempt_id = attemptId,
                    response_role = Clean(responseRole),
                    stance = Clean(stance)
                });
            });
            return attemptId;
        }

        public static void StopAttempt(
            string attemptId,
            float durationSeconds,
            string stopReason)
        {
            ResearchSessionManager manager = ActiveManager();
            if (manager == null || !manager.HasActiveSession ||
                string.IsNullOrWhiteSpace(attemptId)) return;
            manager.RecordEvent("recording_stopped", record =>
            {
                record.Actor = "learner";
                record.PayloadJson = JsonConvert.SerializeObject(new
                {
                    attempt_id = attemptId.Trim(),
                    speech_duration_seconds = Math.Max(0f, durationSeconds),
                    stop_reason = Clean(stopReason)
                });
            });
        }

        public static ResearchTranscriptRecord ConfirmTranscript(
            string attemptId,
            string responseRole,
            string transcriptText,
            float recordingDurationSeconds = 0f,
            string parentResponseId = "")
        {
            ResearchSessionManager manager = ActiveManager();
            string confirmed = Clean(transcriptText);
            if (manager == null || !manager.HasActiveSession ||
                string.IsNullOrWhiteSpace(confirmed)) return null;

            ResearchTranscriptRecord transcript = new()
            {
                AttemptId = Clean(attemptId),
                ResponseRole = Clean(responseRole),
                TranscriptText = confirmed,
                RecordingDurationSeconds = Math.Max(0f, recordingDurationSeconds),
                ParentResponseId = Clean(parentResponseId)
            };
            manager.RecordTranscript(transcript);
            manager.RecordEvent(TranscriptEventName(transcript.ResponseRole), record =>
            {
                record.Actor = "learner";
                record.PayloadJson = JsonConvert.SerializeObject(new
                {
                    attempt_id = transcript.AttemptId,
                    response_id = transcript.ResponseId,
                    parent_response_id = transcript.ParentResponseId,
                    response_role = transcript.ResponseRole,
                    recording_duration_seconds = transcript.RecordingDurationSeconds
                });
            });
            return transcript;
        }

        public static void RecordCoachEvent(CoachEventRecord source)
        {
            ResearchSessionManager manager = ActiveManager();
            if (manager == null || !manager.HasActiveSession || source == null) return;
            manager.RecordEvent(NormalizeCoachEventName(source.EventType), record =>
            {
                record.EventId = string.IsNullOrWhiteSpace(source.EventId)
                    ? record.EventId
                    : source.EventId.Trim();
                record.EventTimestampUtc = string.IsNullOrWhiteSpace(source.EventTimestamp)
                    ? record.EventTimestampUtc
                    : source.EventTimestamp.Trim();
                record.Actor = IsLearnerControlEvent(source.EventType) ? "learner" : "coach";
                record.Recipient = record.Actor == "coach" ? "learner" : "coach";
                record.PayloadJson = JsonConvert.SerializeObject(source);
            });
        }

        public static void RecordTechnicalFailure(
            string eventType,
            string errorCode,
            string errorMessage,
            int retryCount = 0,
            bool recovered = false)
        {
            ResearchSessionManager manager = ActiveManager();
            if (manager == null || !manager.HasActiveSession) return;
            manager.RecordTechnical(new ResearchTechnicalEvent
            {
                EventType = Clean(eventType),
                ErrorCode = Clean(errorCode),
                ErrorMessage = Clean(errorMessage),
                RetryCount = Math.Max(0, retryCount),
                Recovered = recovered
            });
        }

        public static ResearchAudioArtifactRecord SaveAudio(
            string attemptId,
            string responseRole,
            XfyunAudioCapture capture)
        {
            ResearchSessionManager manager = ActiveManager();
            if (manager == null || !manager.HasActiveSession) return null;
            if (capture == null || capture.Pcm16Bytes.Length == 0)
            {
                RecordTechnicalFailure(
                    "audio_missing",
                    "AUDIO_CAPTURE_MISSING",
                    "No PCM16 audio was available for attempt " + Clean(attemptId) + ".");
                return null;
            }

            try
            {
                ResearchAudioArtifactRecord artifact = new ResearchAudioRecorder().SaveCapture(
                    manager.Current.SessionDirectory,
                    manager.Current.CurrentSceneId,
                    responseRole,
                    attemptId,
                    capture);
                manager.RecordAudioArtifact(artifact);
                return artifact;
            }
            catch (Exception exception)
            {
                RecordTechnicalFailure(
                    "audio_save_failed",
                    exception.GetType().Name,
                    "Attempt " + Clean(attemptId) + ": " + exception.Message);
                return null;
            }
        }

        public static bool RecordWithdrawal(
            string reasonOptional,
            string dataRetentionChoice)
        {
            ResearchSessionManager manager = ActiveManager();
            if (manager == null || !manager.HasActiveSession) return false;

            string retentionChoice = Clean(dataRetentionChoice).ToLowerInvariant();
            if (retentionChoice != "retain_anonymized_data" &&
                retentionChoice != "delete_session_data")
            {
                Debug.LogWarning(
                    "Research withdrawal was not recorded: dataRetentionChoice must be " +
                    "retain_anonymized_data or delete_session_data.");
                return false;
            }

            manager.RecordEvent("participant_withdrawal", record =>
            {
                record.Actor = "learner";
                record.PayloadJson = JsonConvert.SerializeObject(new
                {
                    withdrawal_timestamp_utc = DateTimeOffset.UtcNow.ToString("O"),
                    reason_optional = Clean(reasonOptional),
                    data_retention_choice = retentionChoice,
                    deletion_status = retentionChoice == "delete_session_data"
                        ? "pending_researcher_action"
                        : "not_requested"
                });
            });
            manager.EndSession("withdrawn");
            return true;
        }

        public static void CompleteScene(
            string completionStatus = "completed",
            string missingRequiredData = "")
        {
            ResearchSessionManager manager = ActiveManager();
            if (manager == null || !manager.HasActiveSession) return;
            if (!string.IsNullOrWhiteSpace(missingRequiredData))
                manager.RegisterMissingRequiredData(missingRequiredData);
            manager.RecordEvent("scene_completed", record =>
            {
                record.Actor = "system";
                record.PayloadJson = JsonConvert.SerializeObject(new
                {
                    completion_status = string.IsNullOrWhiteSpace(completionStatus)
                        ? "completed"
                        : completionStatus.Trim(),
                    missing_required_data = Clean(missingRequiredData)
                });
            });
            manager.Flush();
            if (string.Equals(manager.Current.CurrentSceneId, "05", StringComparison.Ordinal))
                manager.EndSession(string.IsNullOrWhiteSpace(completionStatus)
                    ? "completed"
                    : completionStatus.Trim());
        }

        private static ResearchSessionManager ActiveManager()
        {
            return ResearchSessionManager.Instance != null
                ? ResearchSessionManager.Instance
                : UnityEngine.Object.FindAnyObjectByType<ResearchSessionManager>(
                    FindObjectsInactive.Include);
        }

        private static string Clean(string value)
        {
            return value?.Trim() ?? string.Empty;
        }

        private static string TranscriptEventName(string responseRole)
        {
            return responseRole switch
            {
                "micro_initial_statement" => "micro_initial_statement_confirmed",
                "micro_independent_response" => "micro_independent_response_confirmed",
                "micro_revision" => "micro_revision_confirmed",
                "full_speech" => "full_speech_confirmed",
                "revision_speech" => "revision_speech_confirmed",
                "transfer_speech" => "transfer_transcript_confirmed",
                _ => "transcript_confirmed"
            };
        }

        private static string NormalizeCoachEventName(string eventType)
        {
            return eventType switch
            {
                "OpponentChallengePresented" => "opponent_challenge_presented",
                "FeedbackPresented" => "coach_feedback_shown",
                "RevisionEvaluationCompleted" => "micro_revision_evaluated",
                "SilentRevisionDiagnosisCompleted" => "silent_revision_diagnosis_completed",
                "PolicyDecision" => "coach_opportunity_decided",
                "LearnerAction" => "learner_control_action",
                "EpisodeCompleted" => "coach_episode_completed",
                _ => string.IsNullOrWhiteSpace(eventType)
                    ? "coach_event"
                    : eventType.Trim()
            };
        }

        private static bool IsLearnerControlEvent(string eventType)
        {
            return string.Equals(eventType, "LearnerAction", StringComparison.Ordinal) ||
                   string.Equals(eventType, "LearnerRequest", StringComparison.Ordinal) ||
                   string.Equals(eventType, "coach_request_opened", StringComparison.Ordinal) ||
                   string.Equals(eventType, "coach_request_submitted", StringComparison.Ordinal) ||
                   string.Equals(eventType, "coach_skipped", StringComparison.Ordinal) ||
                   string.Equals(eventType, "suggestion_accepted", StringComparison.Ordinal) ||
                   string.Equals(eventType, "suggestion_change_requested", StringComparison.Ordinal) ||
                   string.Equals(eventType, "suggestion_declined", StringComparison.Ordinal);
        }
    }

    [Serializable]
    public sealed class ResearchStudySetupResult
    {
        public bool Success;
        public string ParticipantId = string.Empty;
        public string SessionId = string.Empty;
        public CoachOrchestrationMode Condition;
        public string ErrorCode = string.Empty;
        public string Message = string.Empty;
    }

    public static class ResearchStudySetupCoordinator
    {
        public static ResearchStudySetupResult BeginOrReuse(
            string participantId,
            CoachOrchestrationMode condition,
            string sessionId = null,
            string assignmentMethod = "manual",
            string assignmentSeed = "",
            string rootDirectory = null)
        {
            string safeParticipant = ResearchSessionPaths.SanitizeSegment(participantId);
            if (string.IsNullOrWhiteSpace(safeParticipant))
                return Failure("participant_required", "An anonymous participant ID is required.");
            if (condition == CoachOrchestrationMode.Disabled ||
                !Enum.IsDefined(typeof(CoachOrchestrationMode), condition))
                return Failure("condition_required", "A study condition must be selected.");

            ResearchSessionManager manager = ResearchSessionManager.Instance != null
                ? ResearchSessionManager.Instance
                : UnityEngine.Object.FindAnyObjectByType<ResearchSessionManager>(
                    FindObjectsInactive.Include);
            if (manager == null)
                return Failure("manager_missing", "ResearchSessionManager is unavailable.");

            if (!manager.HasActiveSession)
            {
                try
                {
                    manager.StartSession(safeParticipant, rootDirectory, sessionId, condition,
                        assignmentMethod, assignmentSeed);
                }
                catch (Exception exception) when (exception is ArgumentException or
                                                  InvalidOperationException)
                {
                    return Failure("session_start_failed", exception.Message);
                }
            }
            else
            {
                if (!string.Equals(manager.Current.ParticipantId, safeParticipant,
                        StringComparison.OrdinalIgnoreCase))
                    return Failure("participant_mismatch",
                        "Participant ID does not match the active research session.");
                if (manager.Current.Condition != CoachOrchestrationMode.Disabled &&
                    manager.Current.Condition != condition)
                    return Failure("condition_mismatch",
                        "Study condition does not match the active research session.");
                if (manager.Current.Condition == CoachOrchestrationMode.Disabled)
                {
                    try
                    {
                        manager.AssignCondition(condition, assignmentMethod, assignmentSeed);
                    }
                    catch (Exception exception) when (exception is ArgumentException or
                                                      InvalidOperationException)
                    {
                        return Failure("condition_assignment_failed", exception.Message);
                    }
                }
            }

            return new ResearchStudySetupResult
            {
                Success = true,
                ParticipantId = manager.Current.ParticipantId,
                SessionId = manager.Current.SessionId,
                Condition = manager.Current.Condition
            };
        }

        private static ResearchStudySetupResult Failure(string code, string message)
        {
            return new ResearchStudySetupResult
            {
                Success = false,
                ErrorCode = code ?? string.Empty,
                Message = message ?? string.Empty
            };
        }
    }

    public static class ResearchCsvExporter
    {
        public static string ExportSession(string sessionDirectory)
        {
            if (string.IsNullOrWhiteSpace(sessionDirectory))
                throw new ArgumentException("A session directory is required.", nameof(sessionDirectory));
            string fullSessionDirectory = Path.GetFullPath(sessionDirectory);
            string manifestPath = Path.Combine(fullSessionDirectory, "session_manifest.json");
            if (!File.Exists(manifestPath))
                throw new FileNotFoundException("The research session manifest was not found.", manifestPath);

            ResearchSessionSnapshot session = JsonConvert.DeserializeObject<ResearchSessionSnapshot>(
                File.ReadAllText(manifestPath));
            if (session == null)
                throw new InvalidDataException("The research session manifest is invalid.");

            string exportsDirectory = Path.Combine(fullSessionDirectory, "exports");
            Directory.CreateDirectory(exportsDirectory);
            WriteSessions(Path.Combine(exportsDirectory, "sessions.csv"), session);
            WriteEvents(Path.Combine(exportsDirectory, "events.csv"),
                ReadJsonLines<ResearchLogEvent>(Path.Combine(fullSessionDirectory, "events.jsonl")));
            WriteTranscripts(Path.Combine(exportsDirectory, "transcripts.csv"),
                ReadJsonLines<ResearchTranscriptRecord>(Path.Combine(fullSessionDirectory, "transcripts.jsonl")));
            WriteTechnical(Path.Combine(exportsDirectory, "technical_events.csv"),
                ReadJsonLines<ResearchTechnicalEvent>(Path.Combine(fullSessionDirectory, "technical_events.jsonl")));
            WriteAudioArtifacts(Path.Combine(exportsDirectory, "audio_artifacts.csv"),
                ReadJsonLines<ResearchAudioArtifactRecord>(Path.Combine(fullSessionDirectory, "audio_artifacts.jsonl")));
            return exportsDirectory;
        }

        private static void WriteSessions(string path, ResearchSessionSnapshot row)
        {
            WriteTable(path,
                new[]
                {
                    "schema_version", "study_id", "participant_id", "session_id", "condition",
                    "started_at_utc", "ended_at_utc", "status", "current_scene_id", "current_stage",
                    "topic_id", "topic_text", "build_version", "device_id_hash", "session_directory",
                    "data_complete", "missing_required_data"
                },
                new[]
                {
                    new[]
                    {
                        I(row.SchemaVersion), row.StudyId, row.ParticipantId, row.SessionId,
                        row.Condition.ToString(), row.StartedAtUtc, row.EndedAtUtc, row.Status,
                        row.CurrentSceneId, row.CurrentStage, row.TopicId, row.TopicText,
                        row.BuildVersion, row.DeviceIdHash, row.SessionDirectory,
                        B(row.DataComplete), row.MissingRequiredData
                    }
                });
        }

        private static void WriteEvents(string path, IReadOnlyList<ResearchLogEvent> rows)
        {
            WriteTable(path,
                new[]
                {
                    "schema_version", "participant_id", "session_id", "condition", "scene_id",
                    "study_stage", "topic_id", "event_id", "parent_event_id",
                    "event_timestamp_utc", "event_type", "actor", "recipient",
                    "epistemic_schema_version", "epistemic_action", "epistemic_actor",
                    "epistemic_initiator", "epistemic_decision_owner", "epistemic_target",
                    "epistemic_outcome", "payload_json"
                },
                rows.Select(row => new[]
                {
                    I(row.SchemaVersion), row.ParticipantId, row.SessionId, row.Condition, row.SceneId,
                    row.StudyStage, row.TopicId, row.EventId, row.ParentEventId,
                    row.EventTimestampUtc, row.EventType, row.Actor, row.Recipient,
                    row.EpistemicSchemaVersion, row.EpistemicAction, row.EpistemicActor,
                    row.EpistemicInitiator, row.EpistemicDecisionOwner, row.EpistemicTarget,
                    row.EpistemicOutcome, row.PayloadJson
                }));
        }

        private static void WriteTranscripts(string path, IReadOnlyList<ResearchTranscriptRecord> rows)
        {
            WriteTable(path,
                new[]
                {
                    "schema_version", "participant_id", "session_id", "condition", "scene_id",
                    "study_stage", "topic_id", "attempt_id", "response_id", "parent_response_id",
                    "response_role", "transcript_text", "recording_duration_seconds", "confirmed_at_utc"
                },
                rows.Select(row => new[]
                {
                    I(row.SchemaVersion), row.ParticipantId, row.SessionId, row.Condition, row.SceneId,
                    row.StudyStage, row.TopicId, row.AttemptId, row.ResponseId, row.ParentResponseId,
                    row.ResponseRole, row.TranscriptText, F(row.RecordingDurationSeconds), row.ConfirmedAtUtc
                }));
        }

        private static void WriteTechnical(string path, IReadOnlyList<ResearchTechnicalEvent> rows)
        {
            WriteTable(path,
                new[]
                {
                    "schema_version", "participant_id", "session_id", "scene_id",
                    "event_timestamp_utc", "event_type", "error_code", "error_message",
                    "retry_count", "recovered"
                },
                rows.Select(row => new[]
                {
                    I(row.SchemaVersion), row.ParticipantId, row.SessionId, row.SceneId,
                    row.EventTimestampUtc, row.EventType, row.ErrorCode, row.ErrorMessage,
                    I(row.RetryCount), B(row.Recovered)
                }));
        }

        private static void WriteAudioArtifacts(
            string path,
            IReadOnlyList<ResearchAudioArtifactRecord> rows)
        {
            WriteTable(path,
                new[]
                {
                    "schema_version", "participant_id", "session_id", "scene_id", "study_stage",
                    "topic_id", "attempt_id", "audio_file_id", "response_role", "relative_path",
                    "duration_seconds", "sample_rate_hz", "channels", "byte_count", "sha256",
                    "saved_at_utc"
                },
                rows.Select(row => new[]
                {
                    I(row.SchemaVersion), row.ParticipantId, row.SessionId, row.SceneId, row.StudyStage,
                    row.TopicId, row.AttemptId, row.AudioFileId, row.ResponseRole, row.RelativePath,
                    F(row.DurationSeconds), I(row.SampleRateHz), I(row.Channels),
                    row.ByteCount.ToString(CultureInfo.InvariantCulture), row.Sha256, row.SavedAtUtc
                }));
        }

        private static IReadOnlyList<T> ReadJsonLines<T>(string path) where T : class
        {
            if (!File.Exists(path)) return Array.Empty<T>();
            List<T> records = new();
            int lineNumber = 0;
            foreach (string line in File.ReadLines(path))
            {
                lineNumber++;
                if (string.IsNullOrWhiteSpace(line)) continue;
                T record;
                try
                {
                    record = JsonConvert.DeserializeObject<T>(line);
                }
                catch (JsonException exception)
                {
                    throw new InvalidDataException(
                        $"Invalid JSONL record in {Path.GetFileName(path)} at line {lineNumber}.",
                        exception);
                }
                if (record == null)
                    throw new InvalidDataException(
                        $"Empty JSONL record in {Path.GetFileName(path)} at line {lineNumber}.");
                records.Add(record);
            }
            return records;
        }

        private static void WriteTable(
            string path,
            IReadOnlyList<string> header,
            IEnumerable<IReadOnlyList<string>> rows)
        {
            StringBuilder csv = new();
            AppendRow(csv, header);
            foreach (IReadOnlyList<string> row in rows) AppendRow(csv, row);
            File.WriteAllText(path, csv.ToString(), new UTF8Encoding(false));
        }

        private static void AppendRow(StringBuilder csv, IReadOnlyList<string> values)
        {
            for (int index = 0; index < values.Count; index++)
            {
                if (index > 0) csv.Append(',');
                csv.Append(Escape(values[index]));
            }
            csv.AppendLine();
        }

        private static string Escape(string value)
        {
            string text = value ?? string.Empty;
            if (text.IndexOfAny(new[] { ',', '"', '\r', '\n' }) < 0) return text;
            return "\"" + text.Replace("\"", "\"\"") + "\"";
        }

        private static string I(int value) => value.ToString(CultureInfo.InvariantCulture);
        private static string F(float value) => value.ToString("0.###", CultureInfo.InvariantCulture);
        private static string B(bool value) => value ? "true" : "false";
    }
}
