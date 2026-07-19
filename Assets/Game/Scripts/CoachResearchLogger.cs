using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace Game.Debate
{
    public sealed class CoachResearchLogger
    {
        public const string EventFileName = "coach_event_log.csv";
        public const string EpisodeSummaryFileName = "coach_episode_summary.csv";

        private const string EventHeader =
            "participant_id,session_id,orchestration_mode,stage,topic_id,practice_cycle_id,turn_id,coach_episode_id,event_id,event_timestamp,episode_state_before,episode_state_after,event_type,diagnosis_issue_code,diagnosis_severity,diagnosis_confidence,creei_missing_or_weak_components,creei_gap_summary,agenda_source,agenda_text,policy_action,policy_reason,proposed_focus,confirmed_focus,start_authority,agenda_owner,pacing_owner,termination_owner,learner_control_action,request_input_modality,decision_latency_ms,confirmed_learner_text,opponent_utterance_text,challenge_cycle_index,challenge_difficulty,revision_improvement_status,revision_improvement_summary,focus_override_used,level3_requested,coach_feedback_level,coach_feedback_type,coach_feedback_text,coach_turn_index,elapsed_episode_seconds,target_resolved,termination_reason,diagnosis_model_version,feedback_model_version,policy_version";

        private const string SummaryHeader =
            "participant_id,session_id,orchestration_mode,topic_id,practice_cycle_id,turn_id,coach_episode_id,diagnosis_issue_code,diagnosis_severity,diagnosis_confidence,episode_start_timestamp,episode_end_timestamp,episode_duration_seconds,invitation_shown,episode_activated,start_authority,initial_proposed_focus,final_confirmed_focus,agenda_owner,pacing_owner,termination_owner,coach_feedback_turn_count,level3_request_count,learner_voice_response_count,focus_override_used,invitation_declined,target_resolved,termination_reason,diagnosis_model_version,feedback_model_version,policy_version";

        private readonly List<string> _errors = new();
        private readonly List<PendingWrite> _pendingWrites = new();

        public CoachResearchLogger(string directory = null)
        {
            DirectoryPath = string.IsNullOrWhiteSpace(directory)
                ? Path.Combine(Application.persistentDataPath, "ResearchLogs")
                : directory;
            EventLogPath = Path.Combine(DirectoryPath, EventFileName);
            EpisodeSummaryPath = Path.Combine(DirectoryPath, EpisodeSummaryFileName);
        }

        public string DirectoryPath { get; }
        public string EventLogPath { get; }
        public string EpisodeSummaryPath { get; }
        public IReadOnlyList<string> Errors => _errors;
        public int PendingWriteCount => _pendingWrites.Count;

        public void LogEvent(CoachEventRecord row)
        {
            row ??= new CoachEventRecord();
            if (string.IsNullOrWhiteSpace(row.EventId)) row.EventId = Guid.NewGuid().ToString("N");
            if (string.IsNullOrWhiteSpace(row.EventTimestamp)) row.EventTimestamp = DateTimeOffset.UtcNow.ToString("o");
            ResearchCapture.RecordCoachEvent(row);
            Append(EventLogPath, EventHeader, new[]
            {
                row.ParticipantId, row.SessionId, row.OrchestrationMode.ToString(), row.Stage,
                row.TopicId, I(row.PracticeCycleId), I(row.TurnId), row.CoachEpisodeId,
                row.EventId, row.EventTimestamp, row.EpisodeStateBefore.ToString(),
                row.EpisodeStateAfter.ToString(), row.EventType, row.DiagnosisIssueCode,
                I(row.DiagnosisSeverity), F(row.DiagnosisConfidence),
                row.CreeiMissingOrWeakComponents, row.CreeiGapSummary,
                row.AgendaSource, row.AgendaText, row.PolicyAction,
                row.PolicyReason, row.ProposedFocus, row.ConfirmedFocus,
                row.StartAuthority.ToString(), row.AgendaOwner.ToString(),
                row.PacingOwner.ToString(), row.TerminationOwner.ToString(),
                row.LearnerControlAction, row.RequestInputModality,
                I(row.DecisionLatencyMilliseconds), row.ConfirmedLearnerText, row.OpponentUtteranceText,
                I(row.ChallengeCycleIndex), row.ChallengeDifficulty,
                row.RevisionImprovementStatus, row.RevisionImprovementSummary,
                B(row.FocusOverrideUsed),
                B(row.Level3Requested), row.CoachFeedbackLevel, row.CoachFeedbackType,
                row.CoachFeedbackText, I(row.CoachTurnIndex), F(row.ElapsedEpisodeSeconds),
                B(row.TargetResolved), row.TerminationReason.ToString(),
                row.DiagnosisModelVersion, row.FeedbackModelVersion, row.PolicyVersion
            });
        }

        public void CompleteEpisode(CoachEpisodeSummary row)
        {
            row ??= new CoachEpisodeSummary();
            Append(EpisodeSummaryPath, SummaryHeader, new[]
            {
                row.ParticipantId, row.SessionId, row.OrchestrationMode.ToString(), row.TopicId,
                I(row.PracticeCycleId), I(row.TurnId), row.CoachEpisodeId,
                row.DiagnosisIssueCode, I(row.DiagnosisSeverity), F(row.DiagnosisConfidence),
                row.EpisodeStartTimestamp, row.EpisodeEndTimestamp, F(row.EpisodeDurationSeconds),
                B(row.InvitationShown), B(row.EpisodeActivated), row.StartAuthority.ToString(),
                row.InitialProposedFocus, row.FinalConfirmedFocus, row.AgendaOwner.ToString(),
                row.PacingOwner.ToString(), row.TerminationOwner.ToString(),
                I(row.CoachFeedbackTurnCount), I(row.Level3RequestCount),
                I(row.LearnerVoiceResponseCount), B(row.FocusOverrideUsed),
                B(row.InvitationDeclined), B(row.TargetResolved), row.TerminationReason.ToString(),
                row.DiagnosisModelVersion, row.FeedbackModelVersion, row.PolicyVersion
            });
        }

        public void Flush()
        {
            while (_pendingWrites.Count > 0)
            {
                if (!TryAppend(_pendingWrites[0]))
                {
                    break;
                }

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
            if (!TryAppend(write))
            {
                _pendingWrites.Add(write);
            }
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
                string message = DateTimeOffset.UtcNow.ToString("o") + " " + exception.Message;
                _errors.Add(message);
                Debug.LogError("Coach research log write failed: " + exception.Message);
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
