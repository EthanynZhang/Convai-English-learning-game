using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace Game.Debate
{
    public sealed class DebateCommandLogger
    {
        public const string FileName = "debate_command_logs.csv";

        private const string Header =
            "participant_id,condition,topic_id,stage,timestamp,command_text,parsed_operation,parsed_target_move,parsed_strategy_dimension,parsed_tone,target_side,modification_count,npc_version_viewed,practice_turn_id,player_utterance_text";

        private readonly string _path;

        public DebateCommandLogger(string path = null)
        {
            _path = string.IsNullOrWhiteSpace(path)
                ? System.IO.Path.Combine(Application.persistentDataPath, FileName)
                : path;
        }

        public string Path => _path;

        public void LogCommand(
            string participantId,
            string condition,
            string topicId,
            string stage,
            string commandText,
            DebateCommandParseResult parsed,
            int modificationCount,
            string npcVersionViewed,
            int practiceTurnId,
            string playerUtteranceText)
        {
            EnsureHeader();

            DebateCommandParseResult safeParsed = parsed ?? DebateCommandParser.ParseWithLocalRules(string.Empty);
            string[] values =
            {
                participantId,
                condition,
                topicId,
                stage,
                DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                commandText,
                safeParsed.OperationLabel,
                safeParsed.TargetMoveLabel,
                safeParsed.StrategyDimensionLabel,
                safeParsed.Tone,
                safeParsed.TargetSide,
                modificationCount.ToString(CultureInfo.InvariantCulture),
                npcVersionViewed,
                practiceTurnId.ToString(CultureInfo.InvariantCulture),
                playerUtteranceText
            };

            StringBuilder builder = new();
            for (int i = 0; i < values.Length; i++)
            {
                if (i > 0)
                {
                    builder.Append(',');
                }

                builder.Append(EscapeCsv(values[i]));
            }

            builder.AppendLine();
            File.AppendAllText(_path, builder.ToString(), Encoding.UTF8);
        }

        public static string EscapeCsv(string value)
        {
            if (value == null)
            {
                return string.Empty;
            }

            bool needsQuotes = value.Contains(",") || value.Contains("\"") || value.Contains("\n") || value.Contains("\r");
            if (!needsQuotes)
            {
                return value;
            }

            return "\"" + value.Replace("\"", "\"\"") + "\"";
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
