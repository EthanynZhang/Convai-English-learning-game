using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace Game.Debate
{
    public sealed class GuidedCoachResearchLogger
    {
        public const string EventFileName = "coach_guided_event_log.csv";
        public const string StageSummaryFileName = "coach_guided_stage_summary.csv";

        private const string EventHeader =
            "participant_id,session_id,orchestration_mode,stage_kind,stage_index,attempt_index,revision_index,event_type,event_timestamp,learner_request_text,confirmed_learner_text,ranked_suggestions_json,selected_suggestion_id,suggestion_modification,revision_improvement_status,revision_improvement_summary,evaluation_performed,assessment_status,criterion_met,evaluation_confidence,evaluation_evidence,issue_code,next_action,feedback_text,visible_feedback_turn,skip_kind,termination_reason,start_authority,agenda_owner,pacing_owner,termination_owner,diagnosis_model_version,feedback_model_version,policy_version,rubric_version";

        private const string SummaryHeader =
            "participant_id,session_id,orchestration_mode,stage_kind,stage_index,attempt_count,revision_count,visible_feedback_count,initial_confirmed_text,final_confirmed_text,evaluation_performed,assessment_status,criterion_met,evaluation_confidence,skip_kind,termination_reason,start_timestamp,end_timestamp,diagnosis_model_version,feedback_model_version,policy_version,rubric_version";

        private readonly List<PendingWrite> _pendingWrites = new();
        private readonly List<string> _errors = new();

        public GuidedCoachResearchLogger(string directory = null)
        {
            DirectoryPath = string.IsNullOrWhiteSpace(directory)
                ? ResearchSessionPaths.GetDefaultLegacyLogDirectory()
                : directory;
            EventLogPath = Path.Combine(DirectoryPath, EventFileName);
            StageSummaryPath = Path.Combine(DirectoryPath, StageSummaryFileName);
        }

        public string DirectoryPath { get; }
        public string EventLogPath { get; }
        public string StageSummaryPath { get; }
        public int PendingWriteCount => _pendingWrites.Count;
        public IReadOnlyList<string> Errors => _errors;

        public void LogEvent(GuidedCoachEventRecord record)
        {
            record ??= new GuidedCoachEventRecord();
            if (string.IsNullOrWhiteSpace(record.EventTimestamp))
            {
                record.EventTimestamp = DateTimeOffset.UtcNow.ToString("o");
            }

            Append(EventLogPath, EventHeader, new[]
            {
                record.ParticipantId, record.SessionId, record.Mode.ToString(), record.StageKind.ToString(),
                I(record.StageIndex), I(record.AttemptIndex), I(record.RevisionIndex), record.EventType,
                record.EventTimestamp, record.LearnerRequestText, record.ConfirmedLearnerText,
                record.RankedSuggestionsJson, record.SelectedSuggestionId, record.SuggestionModification,
                record.RevisionImprovementStatus, record.RevisionImprovementSummary,
                B(record.EvaluationPerformed), record.AssessmentStatus,
                B(record.CriterionMet), F(record.EvaluationConfidence), record.EvaluationEvidence,
                record.IssueCode, record.NextAction, record.FeedbackText, I(record.VisibleFeedbackTurn),
                record.SkipKind.ToString(), record.TerminationReason.ToString(),
                record.StartAuthority.ToString(), record.AgendaOwner.ToString(),
                record.PacingOwner.ToString(), record.TerminationOwner.ToString(),
                record.DiagnosisModelVersion, record.FeedbackModelVersion, record.PolicyVersion,
                record.RubricVersion
            });
        }

        public void CompleteStage(GuidedCoachStageSummary summary)
        {
            summary ??= new GuidedCoachStageSummary();
            Append(StageSummaryPath, SummaryHeader, new[]
            {
                summary.ParticipantId, summary.SessionId, summary.Mode.ToString(), summary.StageKind.ToString(),
                I(summary.StageIndex), I(summary.AttemptCount), I(summary.RevisionCount),
                I(summary.VisibleFeedbackCount), summary.InitialConfirmedText, summary.FinalConfirmedText,
                B(summary.EvaluationPerformed), summary.AssessmentStatus, B(summary.CriterionMet),
                F(summary.EvaluationConfidence), summary.SkipKind.ToString(), summary.TerminationReason.ToString(),
                summary.StartTimestamp, summary.EndTimestamp, summary.DiagnosisModelVersion,
                summary.FeedbackModelVersion, summary.PolicyVersion, summary.RubricVersion
            });
        }

        public void Flush()
        {
            while (_pendingWrites.Count > 0)
            {
                if (!TryAppend(_pendingWrites[0])) break;
                _pendingWrites.RemoveAt(0);
            }
        }

        private void Append(string path, string header, IReadOnlyList<string> values)
        {
            StringBuilder line = new();
            for (int index = 0; index < values.Count; index++)
            {
                if (index > 0) line.Append(',');
                line.Append(DebateCommandLogger.EscapeCsv(values[index] ?? string.Empty));
            }
            line.AppendLine();

            PendingWrite write = new(path, header, line.ToString());
            if (!TryAppend(write)) _pendingWrites.Add(write);
        }

        private bool TryAppend(PendingWrite write)
        {
            try
            {
                Directory.CreateDirectory(DirectoryPath);
                if (!File.Exists(write.Path) || new FileInfo(write.Path).Length == 0)
                {
                    File.WriteAllText(write.Path, write.Header + Environment.NewLine, Encoding.UTF8);
                }
                File.AppendAllText(write.Path, write.Line, Encoding.UTF8);
                return true;
            }
            catch (Exception exception)
            {
                _errors.Add(DateTimeOffset.UtcNow.ToString("o") + " " + exception.Message);
                Debug.LogError("Guided Coach research log write failed: " + exception.Message);
                return false;
            }
        }

        private sealed class PendingWrite
        {
            public PendingWrite(string path, string header, string line)
            {
                Path = path;
                Header = header;
                Line = line;
            }

            public string Path { get; }
            public string Header { get; }
            public string Line { get; }
        }

        private static string I(int value) => value.ToString(CultureInfo.InvariantCulture);
        private static string F(float value) => value.ToString("0.###", CultureInfo.InvariantCulture);
        private static string B(bool value) => value ? "true" : "false";
    }
}
