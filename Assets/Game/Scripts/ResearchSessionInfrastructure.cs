using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Game.Debate
{
    [Serializable]
    public sealed class ResearchSessionSnapshot
    {
        public int SchemaVersion;
        public string StudyId = string.Empty;
        public string ParticipantId = string.Empty;
        public string SessionId = string.Empty;
        public CoachOrchestrationMode Condition;
        public string SessionDirectory = string.Empty;
        public string StartedAtUtc = string.Empty;
        public string EndedAtUtc = string.Empty;
        public string Status = string.Empty;
        public string CurrentSceneId = string.Empty;
        public string CurrentStage = string.Empty;
        public string TopicId = string.Empty;
        public string TopicText = string.Empty;
        public string BuildVersion = string.Empty;
        public string DeviceIdHash = string.Empty;
        public bool DataComplete;
        public string MissingRequiredData = string.Empty;
    }

    [Serializable]
    public sealed class ResearchLogEvent
    {
        public int SchemaVersion;
        public string ParticipantId = string.Empty;
        public string SessionId = string.Empty;
        public string Condition = string.Empty;
        public string SceneId = string.Empty;
        public string StudyStage = string.Empty;
        public string TopicId = string.Empty;
        public string EventId = string.Empty;
        public string ParentEventId = string.Empty;
        public string EventTimestampUtc = string.Empty;
        public string EventType = string.Empty;
        public string Actor = string.Empty;
        public string Recipient = string.Empty;
        public string EpistemicSchemaVersion = string.Empty;
        public string EpistemicAction = string.Empty;
        public string EpistemicActor = string.Empty;
        public string EpistemicInitiator = string.Empty;
        public string EpistemicDecisionOwner = string.Empty;
        public string EpistemicTarget = string.Empty;
        public string EpistemicOutcome = string.Empty;
        public string PayloadJson = string.Empty;
    }

    [Serializable]
    public sealed class ResearchTranscriptRecord
    {
        public int SchemaVersion;
        public string ParticipantId = string.Empty;
        public string SessionId = string.Empty;
        public string Condition = string.Empty;
        public string SceneId = string.Empty;
        public string StudyStage = string.Empty;
        public string TopicId = string.Empty;
        public string AttemptId = string.Empty;
        public string ResponseId = string.Empty;
        public string ParentResponseId = string.Empty;
        public string ResponseRole = string.Empty;
        public string TranscriptText = string.Empty;
        public float RecordingDurationSeconds;
        public string ConfirmedAtUtc = string.Empty;
    }

    [Serializable]
    public sealed class ResearchTechnicalEvent
    {
        public int SchemaVersion;
        public string ParticipantId = string.Empty;
        public string SessionId = string.Empty;
        public string SceneId = string.Empty;
        public string EventTimestampUtc = string.Empty;
        public string EventType = string.Empty;
        public string ErrorCode = string.Empty;
        public string ErrorMessage = string.Empty;
        public int RetryCount;
        public bool Recovered;
    }

    public static class ResearchSessionPaths
    {
        public const int SchemaVersion = 2;
        public const string StudyId = "BJET_Debate";

        private static string _defaultRootDirectory;

        public static string GetDefaultRootDirectory()
        {
            if (!string.IsNullOrWhiteSpace(_defaultRootDirectory))
                return _defaultRootDirectory;

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            string executableDirectory = Path.GetDirectoryName(Application.dataPath);
            const bool preferPortableDirectory = true;
#else
            string executableDirectory = null;
            const bool preferPortableDirectory = false;
#endif
            _defaultRootDirectory = ResolveDefaultRootDirectory(
                executableDirectory,
                Application.persistentDataPath,
                preferPortableDirectory,
                TryEnsureWritableDirectory);
            return _defaultRootDirectory;
        }

        public static string BuildPortableRootDirectory(string executableDirectory)
        {
            if (string.IsNullOrWhiteSpace(executableDirectory))
                throw new ArgumentException("An executable directory is required.",
                    nameof(executableDirectory));

            return Path.Combine(
                Path.GetFullPath(executableDirectory),
                "ResearchData",
                "schema_v2");
        }

        public static string ResolveDefaultRootDirectory(
            string executableDirectory,
            string persistentDataDirectory,
            bool preferPortableDirectory,
            Func<string, bool> ensureWritableDirectory)
        {
            if (string.IsNullOrWhiteSpace(persistentDataDirectory))
                throw new ArgumentException("A persistent data directory is required.",
                    nameof(persistentDataDirectory));
            if (ensureWritableDirectory == null)
                throw new ArgumentNullException(nameof(ensureWritableDirectory));

            string fallback = Path.Combine(
                Path.GetFullPath(persistentDataDirectory),
                "ResearchData",
                "schema_v2");
            if (!preferPortableDirectory || string.IsNullOrWhiteSpace(executableDirectory))
                return fallback;

            string portable = BuildPortableRootDirectory(executableDirectory);
            try
            {
                if (ensureWritableDirectory(portable)) return portable;
            }
            catch
            {
                // Portable builds can be installed in protected Windows directories.
            }

            return fallback;
        }

        public static string BuildLegacyLogDirectory(string sessionRootDirectory)
        {
            if (string.IsNullOrWhiteSpace(sessionRootDirectory))
                throw new ArgumentException("A session root directory is required.",
                    nameof(sessionRootDirectory));

            string fullSessionRoot = Path.GetFullPath(sessionRootDirectory);
            string researchDataDirectory = Path.GetDirectoryName(fullSessionRoot);
            if (string.IsNullOrWhiteSpace(researchDataDirectory))
                throw new InvalidOperationException("The research storage directory is invalid.");
            return Path.Combine(researchDataDirectory, "legacy_logs");
        }

        public static string GetDefaultLegacyLogDirectory()
        {
            return BuildLegacyLogDirectory(GetDefaultRootDirectory());
        }

        private static bool TryEnsureWritableDirectory(string directory)
        {
            string probePath = null;
            try
            {
                Directory.CreateDirectory(directory);
                probePath = Path.Combine(directory,
                    ".debatequick-write-test-" + Guid.NewGuid().ToString("N"));
                File.WriteAllText(probePath, string.Empty);
                File.Delete(probePath);
                return true;
            }
            catch
            {
                if (!string.IsNullOrWhiteSpace(probePath) && File.Exists(probePath))
                {
                    try
                    {
                        File.Delete(probePath);
                    }
                    catch
                    {
                        // Best-effort cleanup; the caller will use the fallback directory.
                    }
                }
                return false;
            }
        }

        public static string BuildSessionDirectory(
            string rootDirectory,
            string participantId,
            string sessionId)
        {
            if (string.IsNullOrWhiteSpace(rootDirectory))
                throw new ArgumentException("A research data root directory is required.",
                    nameof(rootDirectory));

            string safeParticipant = SanitizeSegment(participantId);
            string safeSession = SanitizeSegment(sessionId);
            if (string.IsNullOrWhiteSpace(safeParticipant))
                throw new ArgumentException("An anonymous participant ID is required.",
                    nameof(participantId));
            if (string.IsNullOrWhiteSpace(safeSession))
                throw new ArgumentException("A session ID is required.", nameof(sessionId));

            string fullRoot = Path.GetFullPath(rootDirectory);
            string candidate = Path.GetFullPath(Path.Combine(
                fullRoot,
                safeParticipant,
                safeSession));
            string rootPrefix = fullRoot.TrimEnd(
                                    Path.DirectorySeparatorChar,
                                    Path.AltDirectorySeparatorChar) +
                                Path.DirectorySeparatorChar;
            if (!candidate.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    "The research session directory escaped the configured root.");

            return candidate;
        }

        public static string SanitizeSegment(string value)
        {
            string candidate = value?.Trim() ?? string.Empty;
            if (candidate.Length == 0) return string.Empty;

            char[] invalid = Path.GetInvalidFileNameChars();
            StringBuilder safe = new(candidate.Length);
            for (int index = 0; index < candidate.Length; index++)
            {
                char character = candidate[index];
                bool mustReplace = invalid.Contains(character) ||
                                   character == Path.DirectorySeparatorChar ||
                                   character == Path.AltDirectorySeparatorChar;
                safe.Append(mustReplace ? '_' : character);
            }

            string result = safe.ToString().Trim().Trim('.');
            return result is "." or ".." ? result.Replace('.', '_') : result;
        }
    }

    public sealed class ResearchEventSink
    {
        private static readonly Encoding Utf8WithoutBom = new UTF8Encoding(false);
        private static readonly JsonSerializerSettings JsonSettings = CreateJsonSettings();

        public ResearchEventSink(string sessionDirectory)
        {
            if (string.IsNullOrWhiteSpace(sessionDirectory))
                throw new ArgumentException("A session directory is required.",
                    nameof(sessionDirectory));

            SessionDirectory = Path.GetFullPath(sessionDirectory);
            Directory.CreateDirectory(SessionDirectory);
            ManifestPath = Path.Combine(SessionDirectory, "session_manifest.json");
            EventLogPath = Path.Combine(SessionDirectory, "events.jsonl");
            TranscriptLogPath = Path.Combine(SessionDirectory, "transcripts.jsonl");
            TechnicalEventLogPath = Path.Combine(SessionDirectory, "technical_events.jsonl");
            AudioArtifactLogPath = Path.Combine(SessionDirectory, "audio_artifacts.jsonl");
            CompletionSummaryPath = Path.Combine(SessionDirectory, "completion_summary.json");
        }

        public string SessionDirectory { get; }
        public string ManifestPath { get; }
        public string EventLogPath { get; }
        public string TranscriptLogPath { get; }
        public string TechnicalEventLogPath { get; }
        public string AudioArtifactLogPath { get; }
        public string CompletionSummaryPath { get; }

        public void WriteManifest(ResearchSessionSnapshot snapshot)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            string json = JsonConvert.SerializeObject(snapshot, Formatting.Indented, JsonSettings);
            string temporaryPath = ManifestPath + ".tmp";
            File.WriteAllText(temporaryPath, json, Utf8WithoutBom);
            if (File.Exists(ManifestPath))
            {
                try
                {
                    File.Replace(temporaryPath, ManifestPath, null);
                    return;
                }
                catch (PlatformNotSupportedException)
                {
                }
                catch (IOException)
                {
                }

                File.Copy(temporaryPath, ManifestPath, true);
                File.Delete(temporaryPath);
                return;
            }

            File.Move(temporaryPath, ManifestPath);
        }

        public void WriteEvent(ResearchLogEvent record)
        {
            AppendJsonLine(EventLogPath, record);
        }

        public void WriteTranscript(ResearchTranscriptRecord record)
        {
            AppendJsonLine(TranscriptLogPath, record);
        }

        public void WriteTechnical(ResearchTechnicalEvent record)
        {
            AppendJsonLine(TechnicalEventLogPath, record);
        }

        public void WriteAudioArtifact(ResearchAudioArtifactRecord record)
        {
            AppendJsonLine(AudioArtifactLogPath, record);
        }

        public void WriteCompletionSummary(ResearchCompletionSummary summary)
        {
            if (summary == null) throw new ArgumentNullException(nameof(summary));
            string json = JsonConvert.SerializeObject(summary, Formatting.Indented, JsonSettings);
            string temporaryPath = CompletionSummaryPath + ".tmp";
            File.WriteAllText(temporaryPath, json, Utf8WithoutBom);
            if (File.Exists(CompletionSummaryPath))
            {
                File.Copy(temporaryPath, CompletionSummaryPath, true);
                File.Delete(temporaryPath);
                return;
            }
            File.Move(temporaryPath, CompletionSummaryPath);
        }

        public void Flush()
        {
            // Writes are synchronous and each record is closed after append.
        }

        private static void AppendJsonLine<T>(string path, T record) where T : class
        {
            if (record == null) throw new ArgumentNullException(nameof(record));
            string json = JsonConvert.SerializeObject(record, Formatting.None, JsonSettings);
            File.AppendAllText(path, json + Environment.NewLine, Utf8WithoutBom);
        }

        private static JsonSerializerSettings CreateJsonSettings()
        {
            JsonSerializerSettings settings = new();
            settings.Converters.Add(new StringEnumConverter());
            return settings;
        }
    }

    public static class ResearchSceneContract
    {
        private static readonly string[] FormalSceneOrder = { "01", "03", "04", "05" };

        public const string PracticeTopicId =
            "individual_practice_vs_interaction_speaking";
        public const string PracticeTopic =
            "Individual practice and interaction with others, which is more beneficial for developing English speaking skills?";
        public const string TransferTopicId =
            "classroom_instruction_vs_real_life_context";
        public const string TransferTopic =
            "Classroom instruction or real-life context: which is more beneficial for English speaking learning?";

        public static bool TryResolve(
            string sceneName,
            out string sceneId,
            out string studyStage,
            out string topicId,
            out string topicText)
        {
            sceneId = string.Empty;
            studyStage = string.Empty;
            topicId = string.Empty;
            topicText = string.Empty;
            switch (sceneName)
            {
                case "01Level_NPCVsNPCDebate":
                    sceneId = "01";
                    studyStage = "common_instruction";
                    topicId = "common_instruction_v1";
                    return true;
                case "03Level_PlayerVsNPCDebate":
                    sceneId = "03";
                    studyStage = "same_topic_baseline";
                    topicId = PracticeTopicId;
                    topicText = PracticeTopic;
                    return true;
                case "04 coach Agent":
                    sceneId = "04";
                    studyStage = "coach_supported_practice";
                    topicId = PracticeTopicId;
                    topicText = PracticeTopic;
                    return true;
                case "05Level_PlayerVsNPCDebate 1":
                    sceneId = "05";
                    studyStage = "unsupported_transfer";
                    topicId = TransferTopicId;
                    topicText = TransferTopic;
                    return true;
                default:
                    return false;
            }
        }

        public static bool IsAllowedTransition(
            string previousSceneId,
            string nextSceneId,
            out string issue)
        {
            string previous = previousSceneId?.Trim() ?? string.Empty;
            string next = nextSceneId?.Trim() ?? string.Empty;
            issue = string.Empty;
            int nextIndex = Array.IndexOf(FormalSceneOrder, next);
            if (nextIndex < 0)
            {
                issue = "Unknown research scene: " + next + ".";
                return false;
            }

            if (string.IsNullOrWhiteSpace(previous))
            {
                if (next == FormalSceneOrder[0]) return true;
                issue = "A formal study session must begin in Scene 01, not Scene " + next + ".";
                return false;
            }

            int previousIndex = Array.IndexOf(FormalSceneOrder, previous);
            if (previousIndex < 0)
            {
                issue = "Unknown previous research scene: " + previous + ".";
                return false;
            }
            if (previousIndex == nextIndex) return true;
            if (nextIndex == previousIndex + 1) return true;
            if (nextIndex < previousIndex)
            {
                issue = "Backward study transition is not allowed: " + previous + " -> " + next + ".";
                return false;
            }

            issue = "A study scene was skipped. Required order is 01 -> 03 -> 04 -> 05; received " +
                    previous + " -> " + next + ".";
            return false;
        }
    }

    [DefaultExecutionOrder(-10000)]
    public sealed class ResearchSessionManager : MonoBehaviour
    {
        private bool _hasActiveSession;

        public static ResearchSessionManager Instance { get; private set; }
        public ResearchSessionSnapshot Current { get; private set; }
        public ResearchEventSink Sink { get; private set; }
        public bool HasActiveSession => _hasActiveSession;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            if (Instance != null) return;
            ResearchSessionManager existing = FindAnyObjectByType<ResearchSessionManager>(
                FindObjectsInactive.Include);
            if (existing != null)
            {
                Instance = existing;
                return;
            }

            GameObject root = new("Research Session Manager");
            root.AddComponent<ResearchSessionManager>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                if (Application.isPlaying) Destroy(gameObject);
                return;
            }

            Instance = this;
            if (!Application.isPlaying) return;
            DontDestroyOnLoad(gameObject);
            SceneManager.sceneLoaded += HandleSceneLoaded;
            SceneManager.sceneUnloaded += HandleSceneUnloaded;
        }

        private void OnDestroy()
        {
            if (Instance != this) return;
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            SceneManager.sceneUnloaded -= HandleSceneUnloaded;
            Flush();
            Instance = null;
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused) Flush();
        }

        private void OnApplicationQuit()
        {
            Flush();
        }

        public ResearchSessionSnapshot StartSession(
            string participantId,
            string rootDirectory = null,
            string sessionIdOverride = null,
            CoachOrchestrationMode initialCondition = CoachOrchestrationMode.Disabled,
            string assignmentMethod = "manual",
            string assignmentSeed = "")
        {
            if (_hasActiveSession)
                throw new InvalidOperationException("A research session is already active.");
            if (!Enum.IsDefined(typeof(CoachOrchestrationMode), initialCondition))
                throw new ArgumentOutOfRangeException(nameof(initialCondition));

            string safeParticipant = ResearchSessionPaths.SanitizeSegment(participantId);
            if (string.IsNullOrWhiteSpace(safeParticipant))
                throw new ArgumentException("An anonymous participant ID is required.",
                    nameof(participantId));
            string safeSession = ResearchSessionPaths.SanitizeSegment(
                string.IsNullOrWhiteSpace(sessionIdOverride)
                    ? Guid.NewGuid().ToString("N")
                    : sessionIdOverride);
            string root = string.IsNullOrWhiteSpace(rootDirectory)
                ? ResearchSessionPaths.GetDefaultRootDirectory()
                : rootDirectory;
            string sessionDirectory = ResearchSessionPaths.BuildSessionDirectory(
                root,
                safeParticipant,
                safeSession);

            Current = new ResearchSessionSnapshot
            {
                SchemaVersion = ResearchSessionPaths.SchemaVersion,
                StudyId = ResearchSessionPaths.StudyId,
                ParticipantId = safeParticipant,
                SessionId = safeSession,
                Condition = initialCondition,
                SessionDirectory = sessionDirectory,
                StartedAtUtc = UtcNow(),
                Status = "active",
                BuildVersion = Application.version,
                DeviceIdHash = HashDeviceId(SystemInfo.deviceUniqueIdentifier)
            };
            Sink = new ResearchEventSink(sessionDirectory);
            _hasActiveSession = true;
            Sink.WriteManifest(Current);
            RecordEvent("session_created", record => record.Actor = "researcher");
            if (initialCondition != CoachOrchestrationMode.Disabled)
                RecordConditionAssignment(initialCondition, assignmentMethod, assignmentSeed);
            TryEnterCurrentScene();
            return Current;
        }

        public void AssignCondition(
            CoachOrchestrationMode condition,
            string assignmentMethod = "manual",
            string assignmentSeed = "")
        {
            EnsureActiveSession();
            if (condition == CoachOrchestrationMode.Disabled)
                throw new ArgumentException("A non-disabled experimental condition is required.",
                    nameof(condition));
            if (Current.Condition != CoachOrchestrationMode.Disabled &&
                Current.Condition != condition)
                throw new InvalidOperationException(
                    "The experimental condition cannot change during an active session.");

            Current.Condition = condition;
            Sink.WriteManifest(Current);
            RecordConditionAssignment(condition, assignmentMethod, assignmentSeed);
        }

        private void RecordConditionAssignment(
            CoachOrchestrationMode condition,
            string assignmentMethod,
            string assignmentSeed)
        {
            RecordEvent("condition_assigned", record =>
            {
                record.Actor = "researcher";
                record.PayloadJson = JsonConvert.SerializeObject(new
                {
                    condition = condition.ToString(),
                    assignment_method = string.IsNullOrWhiteSpace(assignmentMethod)
                        ? "manual"
                        : assignmentMethod.Trim(),
                    assignment_seed = assignmentSeed?.Trim() ?? string.Empty,
                    assigned_at_utc = UtcNow()
                });
            });
        }

        public void EnterStage(
            string sceneId,
            string studyStage,
            string topicId,
            string topicText)
        {
            EnsureActiveSession();
            if (string.IsNullOrWhiteSpace(sceneId))
                throw new ArgumentException("A scene ID is required.", nameof(sceneId));
            if (string.IsNullOrWhiteSpace(studyStage))
                throw new ArgumentException("A study stage is required.", nameof(studyStage));

            string previousSceneId = Current.CurrentSceneId;
            string nextSceneId = sceneId.Trim();
            bool sequenceValid = ResearchSceneContract.IsAllowedTransition(
                previousSceneId,
                nextSceneId,
                out string sequenceIssue);

            Current.CurrentSceneId = nextSceneId;
            Current.CurrentStage = studyStage.Trim();
            Current.TopicId = topicId?.Trim() ?? string.Empty;
            Current.TopicText = topicText?.Trim() ?? string.Empty;
            Sink.WriteManifest(Current);
            RecordEvent("scene_entered", record =>
            {
                record.Actor = "system";
                record.PayloadJson = JsonConvert.SerializeObject(new
                {
                    previous_scene_id = previousSceneId ?? string.Empty,
                    entered_scene_id = nextSceneId,
                    entered_at_utc = UtcNow(),
                    sequence_valid = sequenceValid,
                    sequence_issue = sequenceIssue
                });
            });
            if (Application.isPlaying && !sequenceValid)
            {
                RecordTechnical(new ResearchTechnicalEvent
                {
                    EventType = "invalid_scene_sequence",
                    ErrorCode = "STUDY_FLOW_SEQUENCE_INVALID",
                    ErrorMessage = sequenceIssue,
                    Recovered = false
                });
                RegisterMissingRequiredData("study_flow_sequence:" + nextSceneId);
            }
        }

        public ResearchLogEvent RecordEvent(
            string eventType,
            Action<ResearchLogEvent> configure = null)
        {
            EnsureActiveSession();
            if (string.IsNullOrWhiteSpace(eventType))
                throw new ArgumentException("An event type is required.", nameof(eventType));

            ResearchLogEvent record = new()
            {
                SchemaVersion = ResearchSessionPaths.SchemaVersion,
                ParticipantId = Current.ParticipantId,
                SessionId = Current.SessionId,
                Condition = Current.Condition.ToString(),
                SceneId = Current.CurrentSceneId,
                StudyStage = Current.CurrentStage,
                TopicId = Current.TopicId,
                EventId = Guid.NewGuid().ToString("N"),
                EventTimestampUtc = UtcNow(),
                EventType = eventType.Trim()
            };
            configure?.Invoke(record);
            ResearchEpistemicActionAnnotator.Annotate(record);
            Sink.WriteEvent(record);
            return record;
        }

        public void RecordTranscript(ResearchTranscriptRecord record)
        {
            EnsureActiveSession();
            if (record == null) throw new ArgumentNullException(nameof(record));
            record.SchemaVersion = ResearchSessionPaths.SchemaVersion;
            record.ParticipantId = Current.ParticipantId;
            record.SessionId = Current.SessionId;
            record.Condition = Current.Condition.ToString();
            record.SceneId = Current.CurrentSceneId;
            record.StudyStage = Current.CurrentStage;
            record.TopicId = Current.TopicId;
            if (string.IsNullOrWhiteSpace(record.AttemptId))
                record.AttemptId = Guid.NewGuid().ToString("N");
            if (string.IsNullOrWhiteSpace(record.ResponseId))
                record.ResponseId = Guid.NewGuid().ToString("N");
            if (string.IsNullOrWhiteSpace(record.ConfirmedAtUtc))
                record.ConfirmedAtUtc = UtcNow();
            Sink.WriteTranscript(record);
        }

        public void RecordTechnical(ResearchTechnicalEvent record)
        {
            EnsureActiveSession();
            if (record == null) throw new ArgumentNullException(nameof(record));
            record.SchemaVersion = ResearchSessionPaths.SchemaVersion;
            record.ParticipantId = Current.ParticipantId;
            record.SessionId = Current.SessionId;
            record.SceneId = Current.CurrentSceneId;
            if (string.IsNullOrWhiteSpace(record.EventTimestampUtc))
                record.EventTimestampUtc = UtcNow();
            Sink.WriteTechnical(record);
        }

        public void RecordAudioArtifact(ResearchAudioArtifactRecord record)
        {
            EnsureActiveSession();
            if (record == null) throw new ArgumentNullException(nameof(record));
            record.SchemaVersion = ResearchSessionPaths.SchemaVersion;
            record.ParticipantId = Current.ParticipantId;
            record.SessionId = Current.SessionId;
            record.SceneId = Current.CurrentSceneId;
            record.StudyStage = Current.CurrentStage;
            record.TopicId = Current.TopicId;
            if (string.IsNullOrWhiteSpace(record.AudioFileId))
                record.AudioFileId = Guid.NewGuid().ToString("N");
            if (string.IsNullOrWhiteSpace(record.SavedAtUtc))
                record.SavedAtUtc = UtcNow();
            Sink.WriteAudioArtifact(record);
            RecordEvent("audio_saved", eventRecord =>
            {
                eventRecord.Actor = "system";
                eventRecord.PayloadJson = JsonConvert.SerializeObject(record);
            });
        }

        public void RegisterMissingRequiredData(string missingRequiredData)
        {
            EnsureActiveSession();
            Current.MissingRequiredData = MergeMissingRequiredData(
                Current.MissingRequiredData,
                missingRequiredData);
            if (!string.IsNullOrWhiteSpace(Current.MissingRequiredData)) Current.DataComplete = false;
            Sink.WriteManifest(Current);
        }

        public void EndSession(string status = "completed")
        {
            if (!_hasActiveSession) return;
            string requestedStatus = string.IsNullOrWhiteSpace(status)
                ? "completed"
                : status.Trim();
            ResearchCompletionSummary completion = ResearchCompletenessChecker.Evaluate(
                Current.SessionDirectory,
                Current);
            string combinedMissing = MergeMissingRequiredData(
                Current.MissingRequiredData,
                BuildMissingRequiredData(completion));
            completion.MissingRequiredData = combinedMissing;
            completion.DataComplete = completion.DataComplete && string.IsNullOrWhiteSpace(combinedMissing);
            Current.DataComplete = completion.DataComplete;
            Current.MissingRequiredData = combinedMissing;
            string finalStatus = requestedStatus == "completed" && !completion.DataComplete
                ? "incomplete"
                : requestedStatus;
            RecordEvent("session_ended", record =>
            {
                record.Actor = "system";
                record.PayloadJson = JsonConvert.SerializeObject(new
                {
                    status = finalStatus,
                    data_complete = completion.DataComplete,
                    missing_required_data = combinedMissing
                });
            });
            Current.Status = finalStatus;
            Current.EndedAtUtc = UtcNow();
            Sink.WriteCompletionSummary(completion);
            Sink.WriteManifest(Current);
            Sink.Flush();
            try
            {
                ResearchCsvExporter.ExportSession(Current.SessionDirectory);
            }
            catch (Exception exception)
            {
                Sink.WriteTechnical(new ResearchTechnicalEvent
                {
                    SchemaVersion = ResearchSessionPaths.SchemaVersion,
                    ParticipantId = Current.ParticipantId,
                    SessionId = Current.SessionId,
                    SceneId = Current.CurrentSceneId,
                    EventTimestampUtc = UtcNow(),
                    EventType = "csv_export_failed",
                    ErrorCode = exception.GetType().Name,
                    ErrorMessage = exception.Message,
                    Recovered = false
                });
                Debug.LogError("Research CSV export failed: " + exception.Message);
            }
            _hasActiveSession = false;
        }

        public void Flush()
        {
            if (Sink == null) return;
            if (Current != null) Sink.WriteManifest(Current);
            Sink.Flush();
        }

        private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (!_hasActiveSession) return;
            TryEnterScene(scene.name);
        }

        private void HandleSceneUnloaded(Scene scene)
        {
            if (!_hasActiveSession) return;
            RecordEvent("scene_unloaded", record =>
            {
                record.Actor = "system";
                record.PayloadJson = JsonConvert.SerializeObject(new { scene_name = scene.name });
            });
            Flush();
        }

        private void TryEnterCurrentScene()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (scene.IsValid()) TryEnterScene(scene.name);
        }

        private void TryEnterScene(string sceneName)
        {
            if (ResearchSceneContract.TryResolve(
                    sceneName,
                    out string sceneId,
                    out string studyStage,
                    out string topicId,
                    out string topicText))
                EnterStage(sceneId, studyStage, topicId, topicText);
        }

        private void EnsureActiveSession()
        {
            if (!_hasActiveSession || Current == null || Sink == null)
                throw new InvalidOperationException("No active research session is available.");
        }

        private static string UtcNow()
        {
            return DateTimeOffset.UtcNow.ToString("o", CultureInfo.InvariantCulture);
        }

        private static string BuildMissingRequiredData(ResearchCompletionSummary summary)
        {
            if (summary == null) return string.Empty;
            string[] missingAudio = summary.MissingAudioAttemptIds
                .Select(value => "audio_for_attempt:" + value)
                .ToArray();
            string[] missingTranscript = summary.OrphanAudioAttemptIds
                .Select(value => "transcript_for_audio:" + value)
                .ToArray();
            return string.Join(";", missingAudio.Concat(missingTranscript));
        }

        private static string MergeMissingRequiredData(params string[] values)
        {
            return string.Join(";", values
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .SelectMany(value => value.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                .Select(value => value.Trim())
                .Where(value => value.Length > 0)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(value => value, StringComparer.Ordinal));
        }

        private static string HashDeviceId(string deviceId)
        {
            if (string.IsNullOrWhiteSpace(deviceId) ||
                string.Equals(deviceId, SystemInfo.unsupportedIdentifier,
                    StringComparison.OrdinalIgnoreCase))
                return string.Empty;

            using SHA256 sha = SHA256.Create();
            byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(deviceId));
            return BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant();
        }
    }
}
