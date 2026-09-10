using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;

namespace Game.Debate
{
    public sealed class ResearchRatingImportResult
    {
        public bool Success => Errors.Count == 0;
        public int ImportedRowCount { get; internal set; }
        public string OutputPath { get; internal set; } = string.Empty;
        public List<string> Errors { get; } = new();
    }

    public static class ResearchRatingImportService
    {
        private static readonly string[] RequiredColumns =
        {
            "response_id", "rater_id", "rubric_version",
            "claim_score", "reason_score", "evidence_score",
            "explanation_score", "impact_score", "language_delivery_score"
        };

        private static readonly HashSet<string> ForbiddenBlindColumns =
            new(StringComparer.OrdinalIgnoreCase)
            {
                "condition", "group", "orchestration_mode", "participant_id",
                "participant_name", "session_id"
            };

        public static ResearchRatingImportResult ValidateAndImport(
            string ratingsCsvPath,
            string transcriptJsonlPath,
            string outputCsvPath,
            float minimumScore = 1f,
            float maximumScore = 5f)
        {
            ResearchRatingImportResult result = new();
            if (!File.Exists(ratingsCsvPath))
                result.Errors.Add("Ratings CSV was not found: " + ratingsCsvPath);
            if (!File.Exists(transcriptJsonlPath))
                result.Errors.Add("Transcript JSONL was not found: " + transcriptJsonlPath);
            if (minimumScore > maximumScore)
                result.Errors.Add("minimumScore cannot exceed maximumScore.");
            if (result.Errors.Count > 0) return result;

            List<List<string>> table;
            try
            {
                table = ParseCsv(File.ReadAllText(ratingsCsvPath, Encoding.UTF8));
            }
            catch (FormatException exception)
            {
                result.Errors.Add("Ratings CSV is malformed: " + exception.Message);
                return result;
            }

            if (table.Count == 0)
            {
                result.Errors.Add("Ratings CSV is empty.");
                return result;
            }

            List<string> headers = table[0]
                .Select(value => value.Trim().ToLowerInvariant())
                .ToList();
            foreach (string forbidden in headers.Where(ForbiddenBlindColumns.Contains))
                result.Errors.Add("Forbidden condition-blinding column '" + forbidden + "' is present.");
            foreach (string required in RequiredColumns.Where(column => !headers.Contains(column)))
                result.Errors.Add("Required column '" + required + "' is missing.");

            HashSet<string> knownResponses = ReadKnownResponseIds(transcriptJsonlPath, result);
            Dictionary<string, int> indexes = headers
                .Select((header, index) => new { header, index })
                .GroupBy(pair => pair.header)
                .ToDictionary(group => group.Key, group => group.First().index);

            List<string[]> validRows = new();
            HashSet<string> ratingKeys = new(StringComparer.OrdinalIgnoreCase);
            for (int rowIndex = 1; rowIndex < table.Count; rowIndex++)
            {
                List<string> source = table[rowIndex];
                if (source.All(string.IsNullOrWhiteSpace)) continue;
                int displayRow = rowIndex + 1;
                string responseId = Value(source, indexes, "response_id").Trim();
                string raterId = Value(source, indexes, "rater_id").Trim();
                string rubricVersion = Value(source, indexes, "rubric_version").Trim();

                if (string.IsNullOrWhiteSpace(responseId))
                    result.Errors.Add("Row " + displayRow + " has no response_id.");
                else if (!knownResponses.Contains(responseId))
                    result.Errors.Add("Row " + displayRow + " references unknown response_id " + responseId + ".");
                if (string.IsNullOrWhiteSpace(raterId))
                    result.Errors.Add("Row " + displayRow + " has no anonymous rater_id.");
                if (string.IsNullOrWhiteSpace(rubricVersion))
                    result.Errors.Add("Row " + displayRow + " has no rubric_version.");

                string ratingKey = responseId + "\u001f" + raterId;
                if (!string.IsNullOrWhiteSpace(responseId) && !string.IsNullOrWhiteSpace(raterId) &&
                    !ratingKeys.Add(ratingKey))
                    result.Errors.Add("Row " + displayRow + " duplicates response_id/rater_id " +
                                      responseId + "/" + raterId + ".");

                List<string> normalized = new() { responseId, raterId, rubricVersion };
                foreach (string scoreColumn in RequiredColumns.Skip(3))
                {
                    string raw = Value(source, indexes, scoreColumn).Trim();
                    if (!float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out float score))
                    {
                        result.Errors.Add("Row " + displayRow + " has a non-numeric " + scoreColumn + ".");
                        normalized.Add(raw);
                    }
                    else
                    {
                        if (score < minimumScore || score > maximumScore)
                            result.Errors.Add("Row " + displayRow + " " + scoreColumn +
                                              " is outside " + minimumScore.ToString(CultureInfo.InvariantCulture) +
                                              "-" + maximumScore.ToString(CultureInfo.InvariantCulture) + ".");
                        normalized.Add(score.ToString("0.###", CultureInfo.InvariantCulture));
                    }
                }

                normalized.Add(Value(source, indexes, "rater_comment").Trim());
                validRows.Add(normalized.ToArray());
            }

            if (result.Errors.Count > 0) return result;

            string fullOutputPath = Path.GetFullPath(outputCsvPath);
            string outputDirectory = Path.GetDirectoryName(fullOutputPath);
            if (!string.IsNullOrWhiteSpace(outputDirectory)) Directory.CreateDirectory(outputDirectory);
            WriteCsv(fullOutputPath,
                RequiredColumns.Concat(new[] { "rater_comment" }).ToArray(),
                validRows);
            result.ImportedRowCount = validRows.Count;
            result.OutputPath = fullOutputPath;
            return result;
        }

        private static HashSet<string> ReadKnownResponseIds(
            string transcriptJsonlPath,
            ResearchRatingImportResult result)
        {
            HashSet<string> responseIds = new(StringComparer.OrdinalIgnoreCase);
            int lineNumber = 0;
            foreach (string line in File.ReadLines(transcriptJsonlPath))
            {
                lineNumber++;
                if (string.IsNullOrWhiteSpace(line)) continue;
                try
                {
                    ResearchTranscriptRecord record = JsonConvert.DeserializeObject<ResearchTranscriptRecord>(line);
                    if (!string.IsNullOrWhiteSpace(record?.ResponseId))
                        responseIds.Add(record.ResponseId.Trim());
                }
                catch (JsonException exception)
                {
                    result.Errors.Add("Transcript JSONL line " + lineNumber + " is invalid: " + exception.Message);
                }
            }
            return responseIds;
        }

        private static string Value(
            IReadOnlyList<string> row,
            IReadOnlyDictionary<string, int> indexes,
            string column)
        {
            return indexes.TryGetValue(column, out int index) && index < row.Count
                ? row[index] ?? string.Empty
                : string.Empty;
        }

        private static List<List<string>> ParseCsv(string text)
        {
            List<List<string>> rows = new();
            List<string> row = new();
            StringBuilder field = new();
            bool quoted = false;
            for (int index = 0; index < text.Length; index++)
            {
                char character = text[index];
                if (quoted)
                {
                    if (character == '"')
                    {
                        if (index + 1 < text.Length && text[index + 1] == '"')
                        {
                            field.Append('"');
                            index++;
                        }
                        else quoted = false;
                    }
                    else field.Append(character);
                    continue;
                }

                if (character == '"' && field.Length == 0) quoted = true;
                else if (character == ',')
                {
                    row.Add(field.ToString());
                    field.Clear();
                }
                else if (character == '\r' || character == '\n')
                {
                    if (character == '\r' && index + 1 < text.Length && text[index + 1] == '\n') index++;
                    row.Add(field.ToString());
                    field.Clear();
                    rows.Add(row);
                    row = new List<string>();
                }
                else field.Append(character);
            }
            if (quoted) throw new FormatException("unterminated quoted field");
            if (field.Length > 0 || row.Count > 0)
            {
                row.Add(field.ToString());
                rows.Add(row);
            }
            return rows;
        }

        private static void WriteCsv(string path, IReadOnlyList<string> headers, IEnumerable<string[]> rows)
        {
            StringBuilder builder = new();
            builder.AppendLine(string.Join(",", headers.Select(EscapeCsv)));
            foreach (string[] row in rows)
                builder.AppendLine(string.Join(",", row.Select(EscapeCsv)));
            File.WriteAllText(path, builder.ToString(), new UTF8Encoding(false));
        }

        private static string EscapeCsv(string value)
        {
            string safe = value ?? string.Empty;
            return safe.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0
                ? "\"" + safe.Replace("\"", "\"\"") + "\""
                : safe;
        }
    }
}
