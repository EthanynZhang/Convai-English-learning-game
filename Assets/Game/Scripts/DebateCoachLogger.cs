using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace Game.Debate
{
    public sealed class DebateCoachLogger
    {
        public const string FileName = "debate_coach_logs.csv";

        private const string Header =
            "participant_id,condition,stage,topic_id,turn_id,player_side,opponent_utterance_text,player_utterance_text,selected_strategy,previous_command_count,previous_command_types,coach_triggered,coach_feedback_level,coach_feedback_type,coach_strong_component,coach_weak_component,coach_dominant_strategy,coach_recommended_strategy,coach_next_action,coach_feedback_text,example_requested,timestamp_feedback_shown,timestamp_next_player_turn_started";

        private readonly string _path;

        public DebateCoachLogger(string path = null)
        {
            _path = string.IsNullOrWhiteSpace(path)
                ? System.IO.Path.Combine(Application.persistentDataPath, FileName)
                : path;
        }

        public string Path => _path;

        public void LogFeedback(CoachFeedbackLogRow row)
        {
            EnsureHeader();
            CoachFeedbackLogRow safe = row ?? new CoachFeedbackLogRow();
            string[] values =
            {
                safe.ParticipantId,
                safe.Condition,
                safe.Stage,
                safe.TopicId,
                safe.TurnId.ToString(CultureInfo.InvariantCulture),
                safe.PlayerSide,
                safe.OpponentUtteranceText,
                safe.PlayerUtteranceText,
                safe.SelectedStrategy,
                safe.PreviousCommandCount.ToString(CultureInfo.InvariantCulture),
                safe.PreviousCommandTypes,
                safe.CoachTriggered ? "true" : "false",
                safe.CoachFeedbackLevel,
                safe.CoachFeedbackType,
                safe.CoachStrongComponent,
                safe.CoachWeakComponent,
                safe.CoachDominantStrategy,
                safe.CoachRecommendedStrategy,
                safe.CoachNextAction,
                safe.CoachFeedbackText,
                safe.ExampleRequested ? "true" : "false",
                safe.TimestampFeedbackShown,
                safe.TimestampNextPlayerTurnStarted
            };

            StringBuilder builder = new();
            for (int i = 0; i < values.Length; i++)
            {
                if (i > 0)
                {
                    builder.Append(',');
                }

                builder.Append(DebateCommandLogger.EscapeCsv(values[i]));
            }

            builder.AppendLine();
            File.AppendAllText(_path, builder.ToString(), Encoding.UTF8);
        }

        private void EnsureHeader()
        {
            string directory = System.IO.Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            if (!File.Exists(_path) || new FileInfo(_path).Length == 0)
            {
                File.WriteAllText(_path, Header + Environment.NewLine, Encoding.UTF8);
            }
        }
    }
}
