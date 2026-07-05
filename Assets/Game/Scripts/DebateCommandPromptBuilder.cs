using System.Text;

namespace Game.Debate
{
    public static class DebateCommandPromptBuilder
    {
        public static string BuildPrompt(
            DebateCommandParseResult parsed,
            string debateTopic,
            string originalResponse)
        {
            DebateCommandParseResult safeParsed = parsed ?? DebateCommandParser.ParseWithLocalRules(string.Empty);
            StringBuilder builder = new();

            builder.AppendLine(GetTaskInstruction(safeParsed));
            string strategyInstruction = GetStrategyInstruction(safeParsed);
            if (!string.IsNullOrWhiteSpace(strategyInstruction))
            {
                builder.AppendLine(strategyInstruction);
            }

            string operationInstruction = GetOperationInstruction(safeParsed);
            if (!string.IsNullOrWhiteSpace(operationInstruction))
            {
                builder.AppendLine(operationInstruction);
            }

            builder.AppendLine();
            builder.AppendLine("Structured instruction:");
            builder.AppendLine("Operation: " + safeParsed.OperationLabel);
            builder.AppendLine("Target move: " + safeParsed.TargetMoveLabel);
            builder.AppendLine("Strategy: " + safeParsed.StrategyDimensionLabel);

            if (!string.IsNullOrWhiteSpace(safeParsed.SubStrategy))
            {
                builder.AppendLine("Sub-strategy: " + safeParsed.SubStrategy);
            }

            if (!string.IsNullOrWhiteSpace(safeParsed.Tone))
            {
                builder.AppendLine("Tone: " + safeParsed.Tone);
            }

            if (!string.IsNullOrWhiteSpace(safeParsed.TargetSide) && safeParsed.TargetSide != "Any")
            {
                builder.AppendLine("Target side: " + safeParsed.TargetSide);
            }

            builder.AppendLine();
            builder.AppendLine("Debate topic: " + (debateTopic ?? string.Empty));
            builder.AppendLine("Original response: \"" + (originalResponse ?? string.Empty) + "\"");
            builder.AppendLine();
            builder.AppendLine(GetOutputConstraint(safeParsed));

            return builder.ToString();
        }

        private static string GetTaskInstruction(DebateCommandParseResult parsed)
        {
            return parsed.Operation switch
            {
                DebateCommandOperation.Challenge =>
                    "Generate a concise opponent follow-up question based on the original response.",
                DebateCommandOperation.Compare =>
                    "Generate two concise improved versions of the original response and briefly explain the difference.",
                DebateCommandOperation.Diagnose =>
                    "Diagnose the main debate weakness in the original response, then provide one concise improved version.",
                DebateCommandOperation.PlanNextMove =>
                    "Plan the learner's next debate move and provide one concise response they could say next.",
                DebateCommandOperation.Reflect =>
                    "Reflect on the debate strategy used in the original response and provide one concise improved version.",
                DebateCommandOperation.Clarify =>
                    "Clarify the original response while preserving its meaning.",
                DebateCommandOperation.Strengthen =>
                    "Strengthen the original response according to the structured debate instruction.",
                DebateCommandOperation.GenerateAlternative =>
                    "Generate a concise alternative version of the original response.",
                DebateCommandOperation.CalibrateTone =>
                    "Rewrite the original response with the requested tone while preserving the debate meaning.",
                _ =>
                    "Rewrite the original response according to the structured debate instruction."
            };
        }

        private static string GetStrategyInstruction(DebateCommandParseResult parsed)
        {
            return parsed.StrategyDimension switch
            {
                DebateStrategyDimension.Logos =>
                    "Use Logos, a logical appeal: prioritize clear reasons, evidence, cause-and-effect relationships, tradeoffs, and practical solutions. Keep the tone rational, specific, and well structured. Avoid vague emotional claims.",
                DebateStrategyDimension.Ethos =>
                    "Use Ethos, a credibility and ethical appeal: emphasize fairness, responsibility, honesty, professional credibility, and a balanced position. Acknowledge reasonable concerns from different stakeholders. Keep the tone principled, trustworthy, and steady.",
                DebateStrategyDimension.Pathos =>
                    "Use Pathos, an emotional appeal: emphasize empathy, real pressure, a sense of fairness, and concern about future consequences. Make the response more humane and emotionally resonant, but do not exaggerate or manipulate emotions.",
                DebateStrategyDimension.Mixed =>
                    "Blend Logos, Ethos, and Pathos carefully: use clear reasoning, responsible credibility, and humane emotional resonance without becoming vague or manipulative.",
                _ =>
                    "If no strategy is specified, choose the most helpful debate strategy for the learner's command and keep the response concise, clear, and classroom appropriate."
            };
        }

        private static string GetOperationInstruction(DebateCommandParseResult parsed)
        {
            StringBuilder builder = new();

            switch (parsed.TargetMove)
            {
                case DebateTargetMove.Evidence:
                    builder.Append("Focus especially on adding or sharpening one concrete reason, example, or evidence point. ");
                    break;
                case DebateTargetMove.Claim:
                    builder.Append("Focus especially on making the main claim clearer and easier to defend. ");
                    break;
                case DebateTargetMove.Warrant:
                    builder.Append("Focus especially on explaining why the evidence supports the claim. ");
                    break;
                case DebateTargetMove.Rebuttal:
                    builder.Append("Focus especially on answering the opposing side respectfully and directly. ");
                    break;
                case DebateTargetMove.CounterQuestion:
                    builder.Append("Focus especially on producing a useful follow-up question for the opponent. ");
                    break;
                case DebateTargetMove.Impact:
                    builder.Append("Focus especially on why the point matters for learners, teachers, or real communication. ");
                    break;
                case DebateTargetMove.SummaryClosing:
                    builder.Append("Focus especially on a concise closing or summary move. ");
                    break;
            }

            if (!string.IsNullOrWhiteSpace(parsed.Tone))
            {
                builder.Append("Use a ").Append(parsed.Tone).Append(" tone. ");
            }

            return builder.ToString().Trim();
        }

        private static string GetOutputConstraint(DebateCommandParseResult parsed)
        {
            return parsed.Operation switch
            {
                DebateCommandOperation.Challenge =>
                    "Output only the follow-up question. Do not add labels, analysis, or extra commentary.",
                DebateCommandOperation.Compare =>
                    "Output only the two versions and one short difference note. Do not add unrelated commentary.",
                DebateCommandOperation.Diagnose =>
                    "Output only a short diagnosis and one revised response. Do not add unrelated commentary.",
                DebateCommandOperation.PlanNextMove =>
                    "Output only the next-move plan and the response. Do not add unrelated commentary.",
                DebateCommandOperation.Reflect =>
                    "Output only a short reflection and one revised response. Do not add unrelated commentary.",
                _ =>
                    "Output only the revised response. Do not add labels, analysis, or extra commentary."
            };
        }
    }
}
