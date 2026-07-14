using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace Game.Debate
{
    public sealed class DebateLearningLogger
    {
        public const string FileName = "debate_learning_logs.csv";

        private const string Header =
            "participant_id,condition,stage,timestamp,card_view_time,demo_view_time,natural_view_time,structure_view_time,strategy_version_viewed,micro_choice_strategy,optional_bad_example_viewed,rewatch_count,total_learning_phase_time,micro_practice_1_choice,micro_practice_1_correct,micro_practice_1_feedback_shown,micro_practice_2_template_choice,micro_practice_2_short_text,micro_practice_3_strategy,micro_practice_3_template_choice,micro_practice_3_short_text,micro_choice_rationale_type,micro_choice_rationale_text,micro_practice_total_time,micro_practice_2_claim,micro_practice_2_reason,micro_practice_2_evidence,micro_practice_2_explanation,micro_practice_2_impact,micro_practice_2_completed,micro_practice_2_rerecord_count";

        private readonly string _path;

        public DebateLearningLogger(string path = null)
        {
            _path = string.IsNullOrWhiteSpace(path)
                ? System.IO.Path.Combine(Application.persistentDataPath, FileName)
                : path;
        }

        public string Path => _path;

        public void LogStage(string participantId, string condition, string stage, DebateLearningMetrics metrics)
        {
            AppendRow(participantId, condition, stage, metrics);
        }

        public void LogFinalSummary(string participantId, string condition, DebateLearningMetrics metrics)
        {
            AppendRow(participantId, condition, "final_summary", metrics);
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

        private void AppendRow(string participantId, string condition, string stage, DebateLearningMetrics metrics)
        {
            EnsureHeader();

            DebateLearningMetrics safeMetrics = metrics ?? new DebateLearningMetrics();
            string[] values =
            {
                participantId,
                condition,
                stage,
                DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                FormatSeconds(safeMetrics.CardViewTime),
                FormatSeconds(safeMetrics.DemoViewTime),
                FormatSeconds(safeMetrics.NaturalViewTime),
                FormatSeconds(safeMetrics.StructureViewTime),
                safeMetrics.StrategyVersionViewed,
                safeMetrics.MicroChoiceStrategy,
                safeMetrics.OptionalBadExampleViewed ? "true" : "false",
                safeMetrics.RewatchCount.ToString(CultureInfo.InvariantCulture),
                FormatSeconds(safeMetrics.TotalLearningPhaseTime),
                safeMetrics.MicroPractice1Choice,
                safeMetrics.MicroPractice1Correct ? "true" : "false",
                safeMetrics.MicroPractice1FeedbackShown ? "true" : "false",
                safeMetrics.MicroPractice2TemplateChoice,
                safeMetrics.MicroPractice2ShortText,
                safeMetrics.MicroPractice3Strategy,
                safeMetrics.MicroPractice3TemplateChoice,
                safeMetrics.MicroPractice3ShortText,
                safeMetrics.MicroChoiceRationaleType,
                safeMetrics.MicroChoiceRationaleText,
                FormatSeconds(safeMetrics.MicroPracticeTotalTime),
                safeMetrics.MicroPractice2Claim,
                safeMetrics.MicroPractice2Reason,
                safeMetrics.MicroPractice2Evidence,
                safeMetrics.MicroPractice2Explanation,
                safeMetrics.MicroPractice2Impact,
                safeMetrics.MicroPractice2Completed ? "true" : "false",
                safeMetrics.MicroPractice2RerecordCount.ToString(CultureInfo.InvariantCulture)
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
                return;
            }

            string existing = File.ReadAllText(_path, Encoding.UTF8);
            int firstLineEnd = existing.IndexOf('\n');
            string existingHeader = firstLineEnd >= 0
                ? existing.Substring(0, firstLineEnd).TrimEnd('\r')
                : existing.TrimEnd('\r');
            if (!existingHeader.Contains("micro_practice_2_claim", StringComparison.Ordinal))
            {
                string existingRows = firstLineEnd >= 0 ? existing.Substring(firstLineEnd + 1) : string.Empty;
                File.WriteAllText(_path, Header + Environment.NewLine + existingRows, Encoding.UTF8);
            }
        }

        private static string FormatSeconds(float seconds)
        {
            return seconds.ToString("0.###", CultureInfo.InvariantCulture);
        }
    }
}
