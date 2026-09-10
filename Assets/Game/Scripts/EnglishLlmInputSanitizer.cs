using System;
using System.Text;

namespace Game.Debate
{
    public static class EnglishLlmInputSanitizer
    {
        public static string Sanitize(string value) => Sanitize(value, out _);

        public static string Sanitize(string value, out int removedCharacterCount)
        {
            removedCharacterCount = 0;
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;

            string normalized = value.Normalize(NormalizationForm.FormKC);
            StringBuilder builder = new(normalized.Length);
            bool pendingSpace = false;
            foreach (char original in normalized)
            {
                char character = NormalizePunctuation(original);
                if (char.IsWhiteSpace(character))
                {
                    pendingSpace = builder.Length > 0;
                    continue;
                }

                if (!IsAllowed(character))
                {
                    removedCharacterCount++;
                    continue;
                }

                if (pendingSpace && builder.Length > 0 &&
                    !IsClosingPunctuation(character) && builder[builder.Length - 1] != ' ')
                    builder.Append(' ');
                pendingSpace = false;
                builder.Append(character);
            }

            return builder.ToString().Trim();
        }

        public static bool ContainsEnglishLetter(string value)
        {
            if (string.IsNullOrEmpty(value)) return false;
            foreach (char character in value)
                if (character is >= 'A' and <= 'Z' or >= 'a' and <= 'z')
                    return true;
            return false;
        }

        public static char FilterInputCharacter(char character)
        {
            char normalized = NormalizePunctuation(character);
            return char.IsWhiteSpace(normalized) || IsAllowed(normalized)
                ? normalized
                : '\0';
        }

        private static char NormalizePunctuation(char character) => character switch
        {
            '\u2018' or '\u2019' or '\u02BC' => '\'',
            '\u201C' or '\u201D' => '"',
            '\u2013' or '\u2014' or '\u2212' => '-',
            '\u3002' => '.',
            '\uFF0C' => ',',
            '\uFF01' => '!',
            '\uFF1F' => '?',
            '\uFF1A' => ':',
            '\uFF1B' => ';',
            '\uFF08' => '(',
            '\uFF09' => ')',
            _ => character
        };

        private static bool IsAllowed(char character) =>
            character is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' ||
            character is '.' or ',' or '?' or '!' or ':' or ';' or '\'' or '"' or '-' or
                '(' or ')' or '/';

        private static bool IsClosingPunctuation(char character) =>
            character is '.' or ',' or '?' or '!' or ':' or ';' or ')';
    }
}
