using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace Game.Debate
{
    [Serializable]
    public sealed class MicroCreeiSnapshotRecord
    {
        public string ParticipantId = string.Empty;
        public string SessionId = string.Empty;
        public CoachOrchestrationMode OrchestrationMode;
        public string TopicId = string.Empty;
        public CreeiArgumentSnapshot Snapshot;
        public CreeiComponent[] SelectedComponents = Array.Empty<CreeiComponent>();
        public CreeiComponent? ChallengeFocus;
        public CreeiComponent? CoachFocus;
        public string DiagnosisModelVersion = string.Empty;
        public string PolicyVersion = string.Empty;
    }

    public sealed class MicroCreeiSnapshotLogger
    {
        public const string FileName = "micro_creei_snapshot_log.csv";
        public const string Header =
            "participant_id,session_id,orchestration_mode,topic_id,round_index,snapshot_id,parent_snapshot_id,revision_kind," +
            "claim_text,reason_text,evidence_text,explanation_text,impact_text,changed_components,selected_components," +
            "input_modalities_by_component,challenge_focus,coach_focus,submitted_at,stage_elapsed_seconds," +
            "timeout_committed,diagnosis_model_version,policy_version";

        private readonly List<string> _errors = new();

        public MicroCreeiSnapshotLogger(string directory = null)
        {
            DirectoryPath = string.IsNullOrWhiteSpace(directory)
                ? ResearchSessionPaths.GetDefaultLegacyLogDirectory()
                : Path.GetFullPath(directory);
            FilePath = Path.Combine(DirectoryPath, FileName);
        }

        public string DirectoryPath { get; }
        public string FilePath { get; }
        public IReadOnlyList<string> Errors => _errors;

        public bool LogSnapshot(MicroCreeiSnapshotRecord record)
        {
            if (record?.Snapshot == null) return false;
            CreeiArgumentSnapshot snapshot = record.Snapshot;
            string[] values =
            {
                record.ParticipantId,
                record.SessionId,
                record.OrchestrationMode.ToString(),
                record.TopicId,
                snapshot.RoundIndex.ToString(CultureInfo.InvariantCulture),
                snapshot.SnapshotId,
                snapshot.ParentSnapshotId,
                snapshot.RevisionKind.ToString(),
                snapshot.Claim,
                snapshot.Reason,
                snapshot.Evidence,
                snapshot.Explanation,
                snapshot.Impact,
                string.Join("|", snapshot.ChangedComponents.Select(value => value.ToString())),
                string.Join("|", (record.SelectedComponents ?? Array.Empty<CreeiComponent>())
                    .Select(value => value.ToString())),
                string.Join("|", Enum.GetValues(typeof(CreeiComponent)).Cast<CreeiComponent>()
                    .Select(component => component + ":" + snapshot.GetInputModality(component))),
                record.ChallengeFocus?.ToString() ?? string.Empty,
                record.CoachFocus?.ToString() ?? string.Empty,
                snapshot.SubmittedAt.ToString("o"),
                snapshot.StageElapsedSeconds.ToString("0.###", CultureInfo.InvariantCulture),
                snapshot.TimeoutCommitted ? "true" : "false",
                record.DiagnosisModelVersion,
                record.PolicyVersion
            };
            try
            {
                Directory.CreateDirectory(DirectoryPath);
                if (!File.Exists(FilePath) || new FileInfo(FilePath).Length == 0)
                    File.WriteAllText(FilePath, Header + Environment.NewLine, Encoding.UTF8);
                File.AppendAllText(FilePath,
                    string.Join(",", values.Select(DebateCommandLogger.EscapeCsv)) +
                    Environment.NewLine,
                    Encoding.UTF8);
                return true;
            }
            catch (Exception exception)
            {
                _errors.Add(exception.Message);
                Debug.LogError("Micro CREEI snapshot log write failed: " + exception.Message);
                return false;
            }
        }
    }
}
