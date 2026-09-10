using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;

namespace Game.Debate
{
    public sealed class DebateCoachFeedbackGenerator
    {
        private const string DefaultModel = "gpt-4o-mini";

        private readonly string _model;
        private readonly int _timeoutSeconds;
        private readonly string _baseUrl;
        private readonly string _apiKeyOverride;
        private int _requestVersion;
        private UnityWebRequest _activeRequest;

        public DebateCoachFeedbackGenerator(
            string model = DefaultModel,
            float timeoutSeconds = 10f,
            string baseUrl = "",
            string apiKeyOverride = "")
        {
            _model = string.IsNullOrWhiteSpace(model) ? DefaultModel : model.Trim();
            _timeoutSeconds = Mathf.Max(3, Mathf.RoundToInt(timeoutSeconds));
            _baseUrl = baseUrl?.Trim() ?? string.Empty;
            _apiKeyOverride = apiKeyOverride?.Trim() ?? string.Empty;
        }

        public IEnumerator GenerateFeedback(CoachFeedbackRequest request, Action<CoachFeedbackResult> onComplete)
        {
            int requestVersion = ++_requestVersion;
            CoachFeedbackRequest safeRequest = request ?? new CoachFeedbackRequest();
            string apiKey = DebateCommandParser.ResolveApiKey(_apiKeyOverride);
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                CoachFeedbackResult fallback = BuildLocalFallback(safeRequest);
                fallback.DebugInfo = "API key missing: GPT was not called. Check the OpenAI API key source used by this scene.";
                fallback.RawJson = "No HTTP request was made because the API key resolved to an empty value.";
                onComplete?.Invoke(fallback);
                yield break;
            }

            CoachFeedbackResult parsed = null;
            bool completed = false;
            int maxContractAttempts = safeRequest.Purpose.HasValue ? 1 : 2;
            string contractRepairInstruction = string.Empty;
            string endpoint = DebateCommandParser.ResolveChatCompletionsEndpoint(
                DebateCommandParser.ResolveBaseUrl(_baseUrl));
            for (int contractAttempt = 1;
                 contractAttempt <= maxContractAttempts;
                 contractAttempt++)
            {
                parsed = null;
                completed = false;
                yield return RequestOpenAIFeedback(
                    safeRequest,
                    apiKey,
                    endpoint,
                    requestVersion,
                    contractRepairInstruction,
                    result =>
                    {
                        parsed = result;
                        completed = true;
                    });

                if (requestVersion != _requestVersion)
                {
                    yield break;
                }

                bool purposeContractFailed = completed &&
                                             parsed != null &&
                                             parsed.Source == CoachFeedbackSource.OpenAI &&
                                             safeRequest.Purpose.HasValue &&
                                             !IsPurposeFeedbackResult(
                                                 safeRequest.Purpose.Value,
                                                 parsed);
                if (purposeContractFailed && contractAttempt < maxContractAttempts)
                {
                    contractRepairInstruction = BuildPurposeContractRepairInstruction(
                        safeRequest.Purpose.Value,
                        parsed.FeedbackText);
                    Debug.LogWarning(
                        $"Debate coach feedback contract repair requested. " +
                        $"purpose={safeRequest.Purpose.Value}, " +
                        $"sentence_count={CountSentences(parsed.FeedbackText)}, " +
                        $"contract_attempt={contractAttempt}/{maxContractAttempts}.");
                    continue;
                }

                break;
            }

            if (completed && parsed != null && parsed.Source == CoachFeedbackSource.OpenAI)
            {
                parsed.FeedbackLevel = safeRequest.FeedbackLevel;
                if (safeRequest.Purpose.HasValue &&
                    !IsPurposeFeedbackResult(safeRequest.Purpose.Value, parsed))
                {
                    parsed.Source = CoachFeedbackSource.Rules;
                    parsed.DebugInfo =
                        $"GPT did not satisfy the {safeRequest.Purpose.Value} feedback contract. The result was rejected and was not sent to Anna.";
                    onComplete?.Invoke(parsed);
                    yield break;
                }
                if (!safeRequest.Purpose.HasValue &&
                    safeRequest.FeedbackLevel == CoachFeedbackLevel.Level3 &&
                    !IsConcreteExampleText(parsed.FeedbackText))
                {
                    parsed.Source = CoachFeedbackSource.Rules;
                    parsed.DebugInfo =
                        "GPT returned advice or meta-commentary instead of a concrete example sentence. The result was rejected and was not sent to Convai.";
                    onComplete?.Invoke(parsed);
                    yield break;
                }
                if (!safeRequest.Purpose.HasValue &&
                    safeRequest.FeedbackFormat == CoachFeedbackFormat.Scene04CreeiDetailed &&
                    safeRequest.FeedbackLevel == CoachFeedbackLevel.Level2 &&
                    !IsDetailedCreeiFeedbackText(parsed.FeedbackText))
                {
                    parsed.Source = CoachFeedbackSource.Rules;
                    parsed.DebugInfo =
                        "GPT did not return the required CREEI gaps and Specific advice sections in 4 to 6 sentences. The result was rejected and was not sent to Convai.";
                    onComplete?.Invoke(parsed);
                    yield break;
                }
                if (!safeRequest.Purpose.HasValue && RequiresCompactWorkbenchFeedback(safeRequest))
                {
                    parsed.FeedbackText = NormalizeWorkbenchFeedbackText(parsed.FeedbackText);
                    if (!IsWorkbenchFeedbackText(parsed.FeedbackText))
                    {
                        parsed.Source = CoachFeedbackSource.Rules;
                        parsed.DebugInfo =
                            "GPT did not return usable focused workbench feedback. The result was rejected and was not sent to Convai.";
                        onComplete?.Invoke(parsed);
                        yield break;
                    }
                }

                parsed.DebugInfo = string.IsNullOrWhiteSpace(parsed.FeedbackText)
                    ? "GPT HTTP request succeeded and the JSON parsed, but feedback_text was empty. This is a response-format/content problem, not a network or API-key problem."
                    : "GPT was called successfully and returned a parseable structured response.";
                onComplete?.Invoke(parsed);
                yield break;
            }

            if (completed && parsed != null)
            {
                onComplete?.Invoke(parsed);
                yield break;
            }

            CoachFeedbackResult requestFallback = BuildLocalFallback(safeRequest);
            requestFallback.DebugInfo = "GPT was attempted, but no usable result reached the app. This usually means a network timeout, server error, or response parsing failure.";
            requestFallback.RawJson = "No response details were captured.";
            onComplete?.Invoke(requestFallback);
        }

        public void Cancel()
        {
            _requestVersion++;
            _activeRequest?.Abort();
            _activeRequest = null;
        }

        public static JObject BuildStructuredOutputSchema()
        {
            JArray levels = new("Level1", "Level2", "Level3", "Summary");
            JArray nextActions = new("clarify", "add evidence", "explain", "show impact", "qualify", "balance", "empathize");

            return new JObject
            {
                ["type"] = "object",
                ["additionalProperties"] = false,
                ["properties"] = new JObject
                {
                    ["feedback_type"] = new JObject { ["type"] = "string" },
                    ["feedback_level"] = new JObject { ["type"] = "string", ["enum"] = levels },
                    ["feedback_text"] = new JObject { ["type"] = "string" },
                    ["next_action"] = new JObject { ["type"] = "string", ["enum"] = nextActions },
                    ["target_success_criterion"] = new JObject { ["type"] = "string" }
                },
                ["required"] = new JArray
                {
                    "feedback_type",
                    "feedback_level",
                    "feedback_text",
                    "next_action",
                    "target_success_criterion"
                }
            };
        }

        public static string BuildPrompt(CoachFeedbackRequest request)
        {
            CoachFeedbackRequest safeRequest = request ?? new CoachFeedbackRequest();
            if (safeRequest.Purpose.HasValue)
                return BuildPurposePrompt(safeRequest, safeRequest.Purpose.Value);
            string currentStage = string.IsNullOrWhiteSpace(safeRequest.CurrentCreeiStage)
                ? "Claim"
                : safeRequest.CurrentCreeiStage.Trim();
            CreeiStage creeiStage = CreeiStageGuidance.Parse(currentStage);
            if (safeRequest.FeedbackLevel == CoachFeedbackLevel.Level3)
            {
                return BuildExamplePrompt(safeRequest, creeiStage);
            }

            string confirmedFocus = string.IsNullOrWhiteSpace(safeRequest.ConfirmedFocus)
                ? currentStage
                : safeRequest.ConfirmedFocus.Trim();
            bool detailedCreei = safeRequest.FeedbackFormat ==
                                 CoachFeedbackFormat.Scene04CreeiDetailed &&
                                 safeRequest.FeedbackLevel == CoachFeedbackLevel.Level2;
            bool workbenchCreei = safeRequest.FeedbackFormat ==
                                  CoachFeedbackFormat.Scene04CreeiWorkbench &&
                                  safeRequest.FeedbackLevel == CoachFeedbackLevel.Level2;
            bool learnerRequestAgenda = workbenchCreei &&
                                        safeRequest.LearnerRequestIsPrimaryAgenda &&
                                        !string.IsNullOrWhiteSpace(safeRequest.LearnerRequest);
            string stageInstruction = learnerRequestAgenda
                ? "The learner's natural-language request is the primary coaching agenda. " +
                  "Answer every part of learner_request directly, even when it asks about multiple CREEI components or differs from confirmed_focus. " +
                  "Use confirmed_focus, the component diagnosis, and the full committed CREEI snapshot as supporting context, not as restrictions on the answer. " +
                  "When the learner asks for examples, evidence, or a named context such as China, address those requests explicitly. " +
                  "Do not invent statistics, studies, or sources; clearly label hypothetical examples."
                : workbenchCreei
                ? "The structured diagnosis is already complete. Do not rediagnose or change the confirmed focus. " +
                  "Use the component diagnosis, evidence span, learner request, and current committed snapshot only."
                : detailedCreei
                ? "The condition-blind diagnosis is already complete. Do not rediagnose the learner.\n" +
                  "Use only creei_gap_summary and creei_missing_or_weak_components for the whole-argument diagnosis. " +
                  "Use the confirmed focus, target success criterion, learner request, and learner words for the specific advice."
                : "The condition-blind diagnosis is already complete. Do not rediagnose the learner.\n" +
                  "Respond only to the confirmed focus and target success criterion.";
            string levelInstruction = learnerRequestAgenda
                ? "Feedback level: detailed learner-request response. Do not impose a sentence or word limit. " +
                  "Be thorough, specific, and useful without unnecessary repetition. " +
                  "First answer the learner's question directly. Then explain what is weak or missing in each relevant card, quote short exact phrases from the learner where useful, and show how Evidence and Explanation connect to the Claim. " +
                  "Provide several concrete examples or evidence options the learner could adapt, including the requested cultural or local context. " +
                  "Explain why each option works and distinguish a hypothetical illustration from verifiable factual evidence. " +
                  "Do not silently replace the learner's position and do not write an entire five-card answer unless the learner explicitly asks for one."
                : workbenchCreei
                ? "Feedback level: Level2. Write feedback_text in at most two sentences. " +
                  "Sentence one identifies one specific problem by quoting no more than 12 exact learner words or naming the exact card content. " +
                  "Sentence two gives one next-step action for the confirmed CREEI focus. " +
                  "Do not provide a full five-card answer or introduce a second focus."
                : detailedCreei
                ? "Feedback level: Level2. Write feedback_text in exactly two labelled sections and 4 to 6 short sentences total. " +
                  "Section 1 must begin 'CREEI gaps:' and use 1 to 2 short sentences to state every missing or weak CREEI component and its structural consequence. " +
                  "Section 2 must begin 'Specific advice:' and use 3 to 4 short sentences. Quote one short exact phrase from the learner (no more than 12 words), explain the concrete problem, give an actionable revision strategy, and give one adaptable revision example. " +
                  "Do not provide a complete debate answer. Keep the language appropriate for a CEFR B1-B2 learner."
                : safeRequest.FeedbackLevel switch
            {
                CoachFeedbackLevel.Summary =>
                    "Feedback level: Summary. Summarize the practice debate in no more than four short sentences. Do not give a Transfer Debate answer.",
                CoachFeedbackLevel.Level1 =>
                    "Feedback level: Level1. Name only the already-confirmed issue in one short sentence.",
                _ =>
                    "Feedback level: Level2. Quote one short exact phrase from the learner (no more than 12 words), explain the confirmed issue, and give one concrete revision example the learner could adapt. Keep feedback within three short sentences."
            };

            string outputInstruction = safeRequest.DetailedJson
                ? "JSON mode: detail. Return exactly these fields: feedback_type, feedback_level, feedback_text, next_action, target_success_criterion. " +
                  "Use Level1/Level2/Level3/Summary for feedback_level; and clarify/add evidence/explain/show impact/qualify/balance/empathize for next_action. " +
                  "Do not return diagnostic component or strategy labels; those were fixed by the diagnosis stage."
                : "JSON mode: minimal. Return exactly one field named feedback_text. Do not include any other fields.";

            return
                "You are a debate strategy coach for an L2 English learner.\n" +
                (learnerRequestAgenda
                    ? "You provide responsive, detailed coaching after the learner asks a question during Practice Debate.\n"
                    : "You only provide short post-turn feedback during Practice Debate.\n") +
                "Do not write a full answer for the learner.\n" +
                "Do not interrupt or coach before the learner speaks.\n" +
                "Use encouraging but academically focused language.\n" +
                "Write feedback_text in English only. Do not use Chinese or translate the feedback into another language.\n" +
                stageInstruction + "\n" +
                levelInstruction + "\n" +
                outputInstruction + "\n\n" +
                "Return one JSON object only. Do not use Markdown fences or add text outside the JSON object.\n\n" +
                "Context:\n" +
                "stage: " + safeRequest.Stage + "\n" +
                "topic_id: " + safeRequest.TopicId + "\n" +
                "topic: " + safeRequest.Topic + "\n" +
                "learner_side: " + safeRequest.PlayerSide + "\n" +
                "current_creei_stage: " + currentStage + "\n" +
                "confirmed_focus: " + confirmedFocus + "\n" +
                "learner_request: " + safeRequest.LearnerRequest + "\n" +
                "diagnosis_issue_code: " + safeRequest.DiagnosisIssueCode + "\n" +
                "recommended_strategy: " + safeRequest.RecommendedStrategy + "\n" +
                "target_success_criterion: " + safeRequest.TargetSuccessCriterion + "\n" +
                "creei_missing_or_weak_components: " +
                string.Join(" | ", safeRequest.CreeiMissingOrWeakComponents ?? Array.Empty<string>()) + "\n" +
                "creei_gap_summary: " + safeRequest.CreeiGapSummary + "\n" +
                BuildWorkbenchDiagnosisContext(safeRequest) +
                "turn_id: " + safeRequest.TurnId + "\n" +
                "selected_strategy: " + safeRequest.SelectedStrategy + "\n" +
                "previous_commands: " + string.Join(" | ", safeRequest.PreviousCommands ?? Array.Empty<string>()) + "\n" +
                "previous_npc_versions_viewed: " + string.Join(" | ", safeRequest.PreviousNpcVersionsViewed ?? Array.Empty<string>()) + "\n\n" +
                "learner_completed_creei_stages:\n" + (safeRequest.PreviousLearnerCreeiStages ?? string.Empty) + "\n\n" +
                "opponent_last_turn:\n" + safeRequest.OpponentUtteranceText + "\n\n" +
                "previous_coach_advice:\n" + safeRequest.PreviousCoachFeedbackText + "\n\n" +
                "learner_current_turn:\n" + safeRequest.PlayerUtteranceText;
        }

        public static bool IsDetailedCreeiFeedbackText(string text)
        {
            if (string.IsNullOrWhiteSpace(text) ||
                !text.Contains("CREEI gaps:", StringComparison.OrdinalIgnoreCase) ||
                !text.Contains("Specific advice:", StringComparison.OrdinalIgnoreCase))
                return false;
            int sentenceCount = text.Count(character => character is '.' or '!' or '?');
            return sentenceCount is >= 4 and <= 6;
        }

        public static bool IsWorkbenchFeedbackText(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            int sentenceCount = text.Count(character => character is '.' or '!' or '?');
            return sentenceCount is >= 1 and <= 2;
        }

        public static bool IsPurposeFeedbackText(CoachFeedbackPurpose purpose, string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            if (purpose == CoachFeedbackPurpose.CreeiModelAnswer)
                return IsValidCreeiModelExampleSet(ParseLabelledCreeiModelExamples(text));
            int count = CountSentences(text);
            return purpose switch
            {
                CoachFeedbackPurpose.LearnerSocratic => count is >= 3 and <= 6,
                CoachFeedbackPurpose.CriticalIssue => count is >= 1 and <= 3,
                CoachFeedbackPurpose.TargetedAdvice => count is >= 4 and <= 6,
                CoachFeedbackPurpose.DirectAdvice => count is >= 4 and <= 6,
                CoachFeedbackPurpose.ConversationalFollowUp => count is >= 3 and <= 6,
                CoachFeedbackPurpose.Example => count == 1 && IsConcreteExampleText(text),
                CoachFeedbackPurpose.AdditionalSuggestion => count is >= 2 and <= 4,
                _ => false
            };
        }

        private static bool IsPurposeFeedbackResult(
            CoachFeedbackPurpose purpose,
            CoachFeedbackResult result) =>
            result != null &&
            (purpose == CoachFeedbackPurpose.CreeiModelAnswer
                ? IsValidCreeiModelExampleSet(result.CreeiModelExamples)
                : IsPurposeFeedbackText(purpose, result.FeedbackText));

        public static bool IsValidCreeiModelExampleSet(CreeiModelExampleSet examples)
        {
            if (examples == null) return false;
            foreach (CreeiComponent component in Enum.GetValues(typeof(CreeiComponent)))
            {
                string text = examples.GetText(component)?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(text) ||
                    !text.Any(character =>
                        character is >= 'A' and <= 'Z' or >= 'a' and <= 'z') ||
                    !text.EndsWith(".", StringComparison.Ordinal) &&
                    !text.EndsWith("!", StringComparison.Ordinal) &&
                    !text.EndsWith("?", StringComparison.Ordinal) ||
                    CountSentences(text) != 1 ||
                    !IsConcreteExampleText(text) ||
                    !string.Equals(text, Clean(text), StringComparison.Ordinal))
                    return false;
            }
            return true;
        }

        public static string BuildCreeiModelHistoryText(CreeiModelExampleSet examples)
        {
            if (examples == null) return string.Empty;
            return "Claim: " + examples.Claim.Trim() + "\n" +
                   "Reason: " + examples.Reason.Trim() + "\n" +
                   "Evidence: " + examples.Evidence.Trim() + "\n" +
                   "Explanation: " + examples.Explanation.Trim() + "\n" +
                   "Impact: " + examples.Impact.Trim();
        }

        public static string BuildCreeiModelSpeechText(CreeiModelExampleSet examples)
        {
            if (examples == null) return string.Empty;
            return "Claim. " + examples.Claim.Trim() + " " +
                   "Reason. " + examples.Reason.Trim() + " " +
                   "Evidence. " + examples.Evidence.Trim() + " " +
                   "Explanation. " + examples.Explanation.Trim() + " " +
                   "Impact. " + examples.Impact.Trim();
        }

        private static int CountSentences(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return 0;

            int count = 0;
            for (int index = 0; index < text.Length; index++)
            {
                char current = text[index];
                if (current is not ('.' or '!' or '?')) continue;
                if (current == '.' && index > 0 && index + 1 < text.Length &&
                    char.IsDigit(text[index - 1]) && char.IsDigit(text[index + 1]))
                    continue;
                if (current == '.' && IsCommonAbbreviationPeriod(text, index))
                    continue;
                if (current == '.' && index + 1 < text.Length &&
                    char.IsLetterOrDigit(text[index + 1]))
                    continue;

                int next = index + 1;
                while (next < text.Length && text[next] is '.' or '!' or '?') next++;
                while (next < text.Length && text[next] is '"' or '\'' or ')' or ']' or '\u2019' or '\u201d') next++;
                while (next < text.Length && char.IsWhiteSpace(text[next])) next++;

                if (next >= text.Length || char.IsUpper(text[next]) ||
                    char.IsDigit(text[next]) || text[next] is '"' or '\'' or '(' or '[' or '-')
                    count++;
                index = Math.Max(index, next - 1);
            }

            return count == 0 ? 1 : count;
        }

        private static bool IsCommonAbbreviationPeriod(string text, int periodIndex)
        {
            int start = periodIndex;
            while (start > 0 &&
                   (char.IsLetter(text[start - 1]) || text[start - 1] == '.'))
                start--;
            string token = text.Substring(start, periodIndex - start + 1)
                .ToLowerInvariant();
            return token is "e.g." or "i.e." or "etc." or "mr." or "mrs." or
                "ms." or "dr." or "vs." or "u.s." or "u.k." or "no.";
        }

        private static string BuildPurposeContractRepairInstruction(
            CoachFeedbackPurpose purpose,
            string rejectedFeedback)
        {
            string requirement = purpose switch
            {
                CoachFeedbackPurpose.LearnerSocratic => "Use 3 to 6 complete English sentences.",
                CoachFeedbackPurpose.CriticalIssue => "Use 1 to 3 complete English sentences and give no solution.",
                CoachFeedbackPurpose.TargetedAdvice => "Use 4 to 6 complete English sentences of targeted advice.",
                CoachFeedbackPurpose.DirectAdvice => "Use 4 to 6 complete English sentences of direct, detailed advice.",
                CoachFeedbackPurpose.ConversationalFollowUp => "Use 3 to 6 complete English sentences that directly answer the learner.",
                CoachFeedbackPurpose.CreeiModelAnswer =>
                    "Return all five valid creei_examples fields, with exactly one complete English sentence in each field.",
                CoachFeedbackPurpose.Example => "Use exactly one adaptable example sentence.",
                CoachFeedbackPurpose.AdditionalSuggestion => "Use 2 to 4 complete English sentences.",
                _ => "Follow the requested feedback contract exactly."
            };
            return
                "The previous JSON was valid, but feedback_text failed the required format. " +
                requirement + " Rewrite it now without discussing this correction. " +
                "Avoid abbreviations and numbered fragments so sentence boundaries are unambiguous.\n" +
                "Rejected feedback_text:\n" + TrimForLog(rejectedFeedback);
        }

        private static string BuildPurposePrompt(
            CoachFeedbackRequest request,
            CoachFeedbackPurpose purpose)
        {
            if (purpose == CoachFeedbackPurpose.CreeiModelAnswer)
                return BuildCreeiModelAnswerPrompt(request);

            string task = purpose switch
            {
                CoachFeedbackPurpose.LearnerSocratic =>
                    "Write 3 to 6 English sentences. Begin with one brief observation, then mainly use broad, open-ended guiding questions that help the learner decide what to improve step by step. Do not solve every weakness at once. If the learner explicitly asks for an example, evidence, or a named context, answer that request with one concrete adaptable illustration before returning to an open-ended question. Never invent statistics or sources.",
                CoachFeedbackPurpose.CriticalIssue =>
                    "Write 1 to 3 English sentences identifying exactly one important weakness or unclear connection in the learner's current CREEI argument. If learner_request is present, use it to choose the issue to examine while still grounding the observation in the learner's actual CREEI text; replace rather than merely repeat a previously rejected issue. Do not give advice, a solution, an example, or a focus-selection instruction. The text will be read silently before the learner decides whether to accept the issue.",
                CoachFeedbackPurpose.TargetedAdvice =>
                    "Write 4 to 6 English sentences of detailed, actionable advice about the accepted critical feedback only. Explain why the issue matters, give a practical revision strategy, and describe an adaptable approach without writing the learner's complete answer.",
                CoachFeedbackPurpose.DirectAdvice =>
                    "Write 4 to 6 English sentences that directly give detailed, actionable feedback on the most important problem in the learner's complete CREEI argument. Explain the problem, why it matters, and a practical revision strategy. Include an adaptable approach, but do not write the learner's complete answer. Do not ask the learner to confirm an issue or select a focus.",
                CoachFeedbackPurpose.ConversationalFollowUp =>
                    "Write 3 to 6 English sentences that directly answer the learner's current question. Use the latest committed CREEI argument and conversation_history, address every explicit request, and avoid repeating advice already given. Give a concrete example when requested, but do not write the learner's complete five-part answer.",
                CoachFeedbackPurpose.Example =>
                    "Write exactly one adaptable example sentence that addresses the accepted critical feedback and fits the learner's position. Return only the example sentence and do not repeat an example from conversation_history.",
                CoachFeedbackPurpose.AdditionalSuggestion =>
                    "Write 2 to 4 English sentences giving one new actionable suggestion about the accepted critical feedback. Do not repeat advice or examples from conversation_history, and do not write the learner's complete answer.",
                _ => throw new ArgumentOutOfRangeException(nameof(purpose), purpose, null)
            };
            string acceptedIssue = purpose is CoachFeedbackPurpose.TargetedAdvice or
                CoachFeedbackPurpose.Example or CoachFeedbackPurpose.AdditionalSuggestion
                ? "accepted critical feedback: " + Clean(request.AcceptedCriticalFeedback) + "\n"
                : string.Empty;
            return
                "You are Anna, a debate coach for a CEFR B1-B2 English learner.\n" +
                "The structured diagnosis is condition-blind. Never mention an experiment condition, control mode, hidden focus, or policy.\n" +
                "Use encouraging but academically focused language and preserve the learner's position.\n" +
                task + "\n" +
                "Return one JSON object only with the fields feedback_type, feedback_level, feedback_text, next_action, and target_success_criterion.\n\n" +
                "Context:\n" +
                "topic: " + Clean(request.Topic) + "\n" +
                "learner_side: " + Clean(request.PlayerSide) + "\n" +
                "learner_request: " + Clean(request.LearnerRequest) + "\n" +
                acceptedIssue +
                BuildWorkbenchDiagnosisContext(
                    request, purpose != CoachFeedbackPurpose.CriticalIssue) +
                BuildConversationHistory(request.ConversationHistory) +
                "learner_current_turn:\n" + Clean(request.PlayerUtteranceText);
        }

        private static string BuildCreeiModelAnswerPrompt(CoachFeedbackRequest request)
        {
            return
                "You are Anna, creating a worked example for a CEFR B1-B2 English debate learner.\n" +
                "Generate one entirely new, coherent argument that supports the specified learner side.\n" +
                "Do not quote, preserve, rewrite, evaluate, or refer to the learner's current answer.\n" +
                "The five fields must form one coherent argument in this exact order: claim, reason, evidence, explanation, impact.\n" +
                "Write exactly one complete English sentence in each field.\n" +
                "claim: clearly support the learner side.\n" +
                "reason: directly explain why the claim is true or preferable.\n" +
                "evidence: give one concrete classroom or real-communication scenario; never invent statistics, studies, sources, or citations.\n" +
                "explanation: explicitly connect the evidence to the reason and claim.\n" +
                "impact: identify who is affected and state an important consequence.\n" +
                "Do not use advice or meta-language such as 'you should', 'try to', 'your answer', or 'your claim'.\n" +
                "Return one JSON object only, with feedback_type, feedback_level, creei_examples, next_action, and target_success_criterion.\n" +
                "creei_examples must be an object with exactly these string fields: claim, reason, evidence, explanation, impact.\n" +
                "Do not add feedback_text, Markdown, or any text outside the JSON object.\n\n" +
                "Context:\n" +
                "topic: " + Clean(request.Topic) + "\n" +
                "learner_side: " + Clean(request.PlayerSide);
        }

        private static string BuildConversationHistory(CoachConversationTurn[] turns)
        {
            if (turns == null || turns.Length == 0)
                return "conversation_history: none\n";
            StringBuilder builder = new("conversation_history:\n");
            foreach (CoachConversationTurn turn in turns.Skip(Math.Max(0, turns.Length - 6)))
            {
                if (turn == null) continue;
                builder.Append("turn ").Append(turn.TurnIndex)
                    .Append(" [").Append(turn.Purpose).Append("]\n")
                    .Append("learner: ").Append(Clean(turn.LearnerRequest)).Append('\n')
                    .Append("anna: ").Append(Clean(turn.CoachResponse)).Append('\n');
            }
            return builder.ToString();
        }

        private static string Clean(string value) => EnglishLlmInputSanitizer.Sanitize(value);

        public static bool RequiresCompactWorkbenchFeedback(CoachFeedbackRequest request) =>
            request != null &&
            request.FeedbackFormat == CoachFeedbackFormat.Scene04CreeiWorkbench &&
            request.FeedbackLevel == CoachFeedbackLevel.Level2 &&
            !request.LearnerRequestIsPrimaryAgenda;

        public static string NormalizeWorkbenchFeedbackText(string text)
        {
            string trimmed = text?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(trimmed)) return string.Empty;
            int terminalCount = trimmed.Count(character => character is '.' or '!' or '?');
            if (terminalCount <= 2) return trimmed;

            int firstTerminal = trimmed.IndexOfAny(new[] { '.', '!', '?' });
            if (firstTerminal < 0 || firstTerminal >= trimmed.Length - 1) return trimmed;
            string firstSentence = trimmed.Substring(0, firstTerminal + 1).Trim();
            string remainder = trimmed.Substring(firstTerminal + 1).Trim();
            StringBuilder repaired = new(remainder.Length);
            for (int index = 0; index < remainder.Length; index++)
            {
                char current = remainder[index];
                if (current is '.' or '!' or '?')
                {
                    bool hasFollowingText = remainder
                        .Skip(index + 1)
                        .Any(character => !char.IsWhiteSpace(character) && character != '"' && character != '\'');
                    repaired.Append(hasFollowingText ? ';' : current);
                    continue;
                }
                repaired.Append(current);
            }
            return firstSentence + " " + repaired.ToString().Trim();
        }

        private static string BuildWorkbenchDiagnosisContext(
            CoachFeedbackRequest request,
            bool includeRecommendedNextAction = true)
        {
            if (request.FeedbackFormat != CoachFeedbackFormat.Scene04CreeiWorkbench)
                return string.Empty;
            CreeiComponentDiagnosis diagnosis = request.ComponentDiagnosis;
            string diagnosisContext = diagnosis == null
                ? string.Empty
                : "component_diagnosis: " + diagnosis.Component + "\n" +
                  "criterion_met: " + diagnosis.CriterionMet + "\n" +
                  "severity: " + diagnosis.Severity + "\n" +
                  "confidence: " + diagnosis.Confidence + "\n" +
                  "issue_code: " + Clean(diagnosis.IssueCode) + "\n" +
                  "evidence_span: " + Clean(diagnosis.EvidenceSpan) + "\n" +
                  (includeRecommendedNextAction
                      ? "recommended_next_action: " + Clean(diagnosis.RecommendedNextAction) + "\n"
                      : string.Empty);
            return diagnosisContext + BuildStructuredSnapshot(
                "current_creei_argument", request.CurrentCreeiSnapshot) +
                BuildStructuredSnapshot("previous_creei_argument", request.PreviousCreeiSnapshot);
        }

        private static string BuildStructuredSnapshot(string label, CreeiArgumentSnapshot snapshot)
        {
            if (snapshot == null) return string.Empty;
            return label + ":\n" +
                   "claim: " + Clean(snapshot.Claim) + "\n" +
                   "reason: " + Clean(snapshot.Reason) + "\n" +
                   "evidence: " + Clean(snapshot.Evidence) + "\n" +
                   "explanation: " + Clean(snapshot.Explanation) + "\n" +
                   "impact: " + Clean(snapshot.Impact) + "\n";
        }

        private static string BuildExamplePrompt(CoachFeedbackRequest request, CreeiStage stage)
        {
            string currentStage = stage.ToString();
            string outputInstruction = request.DetailedJson
                ? "JSON mode: detail. Return exactly these fields: feedback_type, feedback_level, feedback_text, next_action, target_success_criterion. The feedback_text field must still contain only the example sentence. Do not return diagnostic component or strategy labels."
                : "JSON mode: minimal. Return exactly one field named feedback_text. Do not include any other fields.";

            string confirmedFocus = CoachFocusCatalog.Normalize(request.ConfirmedFocus, currentStage);
            bool rhetoricalFocus = confirmedFocus is "Ethos" or "Pathos" or "Logos";
            string focusInstruction = rhetoricalFocus
                ? "Confirmed rhetorical focus: " + confirmedFocus + "\n" +
                  "Example task: " + GetRhetoricalExampleTask(confirmedFocus) + "\n"
                : "Current CREEI stage: " + currentStage + "\n" +
                  "Method for this stage: " + CreeiStageGuidance.GetMethod(stage) + "\n" +
                  "Example task: " + CreeiStageGuidance.GetExampleTask(stage) + "\n";

            return
                "You are generating a model sentence that an L2 English learner can say aloud in a debate.\n" +
                "This is an EXAMPLE task, not a feedback or diagnosis task.\n" +
                "Do not evaluate the learner. Do not give advice. Do not explain how to improve.\n" +
                "Never write phrases such as 'you should', 'try to', 'consider', 'add', 'make sure', or 'your response'.\n" +
                "Write in English only.\n\n" +
                focusInstruction + "\n" +
                "Rewrite the learner's current stage utterance so it applies the previous Coach advice while preserving the learner's intended position.\n" +
                "The output must be one natural, speakable model sentence that could replace the learner's original utterance.\n" +
                "Output only that model sentence in feedback_text. Do not add a label, quotation marks, diagnosis, advice, explanation, or second sentence.\n" +
                outputInstruction + "\n" +
                "Return one JSON object only. Do not use Markdown fences or add text outside the JSON object.\n\n" +
                "Context:\n" +
                "topic: " + request.Topic + "\n" +
                "learner_side: " + request.PlayerSide + "\n" +
                "current_creei_stage: " + currentStage + "\n" +
                "confirmed_focus: " + request.ConfirmedFocus + "\n" +
                "learner_request: " + request.LearnerRequest + "\n" +
                "diagnosis_issue_code: " + request.DiagnosisIssueCode + "\n" +
                "recommended_strategy: " + request.RecommendedStrategy + "\n" +
                "target_success_criterion: " + request.TargetSuccessCriterion + "\n" +
                "learner_completed_creei_stages:\n" + (request.PreviousLearnerCreeiStages ?? string.Empty) + "\n\n" +
                "previous_coach_advice:\n" + request.PreviousCoachFeedbackText + "\n\n" +
                "learner_current_stage_utterance:\n" + request.PlayerUtteranceText;
        }

        private static string GetRhetoricalExampleTask(string focus)
        {
            return focus switch
            {
                "Ethos" => "Write one sentence that builds relevant credibility or trust without inventing credentials.",
                "Pathos" => "Write one sentence that makes a relevant audience consequence vivid and empathetic without manipulation.",
                "Logos" => "Write one sentence that makes the logical link between reason, evidence, and claim explicit without inventing facts.",
                _ => "Write one focused sentence that applies the confirmed Coach advice."
            };
        }

        public static bool IsConcreteExampleText(string value)
        {
            string text = value?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            string lower = text.ToLowerInvariant();
            string[] adviceMarkers =
            {
                "you should", "you could", "try to", "consider ", "make sure", "your response",
                "your claim", "your reason", "your evidence", "needs to", "could be improved",
                "add more", "be more specific", "a stronger"
            };
            return !adviceMarkers.Any(lower.Contains);
        }

        public JObject BuildOpenAIRequestJson(CoachFeedbackRequest request)
        {
            CoachFeedbackRequest safeRequest = request ?? new CoachFeedbackRequest();
            bool creeiModelAnswer =
                safeRequest.Purpose == CoachFeedbackPurpose.CreeiModelAnswer;
            return new JObject
            {
                ["model"] = _model,
                ["temperature"] = 0.2f,
                ["max_tokens"] = GetMaxTokens(safeRequest.Purpose),
                ["messages"] = new JArray
                {
                    new JObject
                    {
                        ["role"] = "system",
                        ["content"] = creeiModelAnswer
                            ? "Return strict JSON with a creei_examples object containing exactly one English sentence for claim, reason, evidence, explanation, and impact."
                            : safeRequest.FeedbackLevel == CoachFeedbackLevel.Level3
                            ? "Return strict JSON. feedback_text must be one speakable example sentence, never advice, evaluation, or meta-commentary. Write in English only."
                            : safeRequest.DetailedJson
                                ? "Return only strict JSON for a detailed debate Coach Agent feedback result. Write feedback_text in English only."
                                : "Return only strict JSON with exactly one field named feedback_text. Write its value in English only."
                    },
                    new JObject
                    {
                        ["role"] = "user",
                        ["content"] = BuildPrompt(safeRequest)
                    }
                },
                ["response_format"] = new JObject
                {
                    ["type"] = "json_object"
                }
            };
        }

        public static CoachFeedbackResult BuildLocalFallback(CoachFeedbackRequest request)
        {
            CoachFeedbackRequest safeRequest = request ?? new CoachFeedbackRequest();
            string utterance = safeRequest.PlayerUtteranceText?.Trim() ?? string.Empty;
            string lower = utterance.ToLowerInvariant();
            string selectedStrategy = string.IsNullOrWhiteSpace(safeRequest.SelectedStrategy)
                ? "Logos"
                : NormalizeStrategy(safeRequest.SelectedStrategy);

            if (safeRequest.FeedbackLevel == CoachFeedbackLevel.Level3)
            {
                return new CoachFeedbackResult
                {
                    StrongComponent = "Claim",
                    WeakComponent = "Evidence",
                    DominantStrategy = selectedStrategy,
                    RecommendedStrategy = selectedStrategy,
                    FeedbackType = "Example Frame",
                    FeedbackLevel = CoachFeedbackLevel.Level3,
                    FeedbackText = string.Empty,
                    NextAction = "add evidence",
                    Source = CoachFeedbackSource.Rules,
                    DebugInfo = "Local default example feedback is disabled for this scene."
                };
            }

            if (safeRequest.FeedbackLevel == CoachFeedbackLevel.Summary)
            {
                return new CoachFeedbackResult
                {
                    StrongComponent = "Claim",
                    WeakComponent = "Explanation",
                    DominantStrategy = selectedStrategy,
                    RecommendedStrategy = "Logos",
                    FeedbackType = "Practice Summary",
                    FeedbackLevel = CoachFeedbackLevel.Summary,
                    FeedbackText = string.Empty,
                    NextAction = "explain",
                    Source = CoachFeedbackSource.Rules,
                    DebugInfo = "Local default summary feedback is disabled for this scene."
                };
            }

            bool hasEvidence = ContainsAny(lower, "for example", "example", "data", "research", "study", "survey", "in class", "students can");
            bool hasReason = ContainsAny(lower, "because", "so", "therefore", "reason");
            bool hasImpact = ContainsAny(lower, "important", "matter", "affect", "help", "future", "confidence");
            string weak = hasEvidence ? (hasReason ? (hasImpact ? "Explanation" : "Impact") : "Reason") : "Evidence";
            string nextAction = weak switch
            {
                "Evidence" => "add evidence",
                "Impact" => "show impact",
                "Reason" => "clarify",
                _ => "explain"
            };

            return new CoachFeedbackResult
            {
                StrongComponent = string.IsNullOrWhiteSpace(utterance) ? "Claim" : "Claim",
                WeakComponent = weak,
                DominantStrategy = InferDominantStrategy(lower, selectedStrategy),
                RecommendedStrategy = weak == "Impact" ? "Pathos" : selectedStrategy,
                FeedbackType = weak + " Support",
                FeedbackLevel = safeRequest.FeedbackLevel,
                FeedbackText = string.Empty,
                NextAction = nextAction,
                Source = CoachFeedbackSource.Rules,
                DebugInfo = "Local default Coach feedback is disabled for this scene."
            };
        }

        private IEnumerator RequestOpenAIFeedback(
            CoachFeedbackRequest requestData,
            string apiKey,
            string endpoint,
            int requestVersion,
            string contractRepairInstruction,
            Action<CoachFeedbackResult> onComplete)
        {
            JObject requestJson = BuildOpenAIRequestJson(requestData);
            if (!string.IsNullOrWhiteSpace(contractRepairInstruction) &&
                requestJson["messages"] is JArray messages)
            {
                messages.Add(new JObject
                {
                    ["role"] = "user",
                    ["content"] = contractRepairInstruction
                });
            }
            byte[] body = Encoding.UTF8.GetBytes(requestJson.ToString(Formatting.None));
            bool useScene04Budget = requestData.Purpose.HasValue;
            int maxAttempts = useScene04Budget ? 1 : 2;
            float operationStartedAt = Time.realtimeSinceStartup;
            float operationDeadline = operationStartedAt + (useScene04Budget ? 18f :
                Mathf.Max(_timeoutSeconds, 3) * maxAttempts + 1f);
            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                float remainingBudget = Mathf.Max(0f, operationDeadline - Time.realtimeSinceStartup);
                if (remainingBudget < 1f)
                {
                    CoachFeedbackResult budgetFailure = BuildLocalFallback(requestData);
                    budgetFailure.DebugInfo =
                        "Coach request exceeded the Scene 04 18-second request budget.";
                    budgetFailure.RequestAttemptCount = attempt - 1;
                    budgetFailure.RequestByteCount = body.Length;
                    budgetFailure.RequestElapsedMilliseconds = Mathf.RoundToInt(
                        (Time.realtimeSinceStartup - operationStartedAt) * 1000f);
                    onComplete?.Invoke(budgetFailure);
                    yield break;
                }

                int attemptTimeout = useScene04Budget
                    ? Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(17f, remainingBudget)), 1, 17)
                    : _timeoutSeconds;
                using UnityWebRequest request = new(endpoint, UnityWebRequest.kHttpVerbPOST)
                {
                    uploadHandler = new UploadHandlerRaw(body),
                    downloadHandler = new DownloadHandlerBuffer(),
                    timeout = attemptTimeout
                };

                request.SetRequestHeader("Authorization", "Bearer " + apiKey.Trim());
                request.SetRequestHeader("Content-Type", "application/json");

                float startedAt = Time.realtimeSinceStartup;
                _activeRequest = request;
                yield return request.SendWebRequest();
                if (ReferenceEquals(_activeRequest, request))
                {
                    _activeRequest = null;
                }

                if (requestVersion != _requestVersion)
                {
                    yield break;
                }

                float elapsedSeconds = Mathf.Max(0f, Time.realtimeSinceStartup - startedAt);
                if (request.result != UnityWebRequest.Result.Success)
                {
                    string rawBody = TrimForLog(request.downloadHandler?.text);
                    bool retryable = IsRetryableFeedbackFailure(request.result, request.responseCode);
                    Debug.LogWarning(
                        $"Debate coach feedback request failed. endpoint={endpoint}, attempt={attempt}/{maxAttempts}, " +
                        $"elapsed_seconds={elapsedSeconds:F1}, request_bytes={body.Length}, result={request.result}, " +
                        $"status={request.responseCode}, error={request.error}, body={rawBody}");
                    if (retryable && attempt < maxAttempts)
                    {
                        yield return new WaitForSecondsRealtime(1f);
                        continue;
                    }

                    CoachFeedbackResult fallback = BuildLocalFallback(requestData);
                    fallback.DebugInfo = BuildRequestFailureDebugInfo(
                        endpoint,
                        request.result,
                        request.responseCode,
                        request.error,
                        attempt,
                        maxAttempts);
                    fallback.RawJson = string.IsNullOrWhiteSpace(rawBody)
                        ? "(empty response body)"
                        : rawBody;
                    ApplyTelemetry(fallback, attempt, body.Length, operationStartedAt);
                    onComplete?.Invoke(fallback);
                    yield break;
                }

                CoachFeedbackResult parsed = TryParseOpenAIResponse(
                    request.downloadHandler.text, out string parseFailure);
                if (parsed != null)
                {
                    Debug.Log(
                        $"Debate coach feedback request succeeded. endpoint={endpoint}, attempt={attempt}/{maxAttempts}, " +
                        $"elapsed_seconds={elapsedSeconds:F1}, request_bytes={body.Length}");
                    ApplyTelemetry(parsed, attempt, body.Length, operationStartedAt);
                    onComplete?.Invoke(parsed);
                }
                else
                {
                    CoachFeedbackResult fallback = BuildLocalFallback(requestData);
                    fallback.DebugInfo = "HTTP request succeeded, but GPT response could not be parsed. " + parseFailure;
                    fallback.RawJson = TrimForLog(request.downloadHandler.text);
                    ApplyTelemetry(fallback, attempt, body.Length, operationStartedAt);
                    Debug.LogWarning("Debate coach feedback response could not be parsed. " + parseFailure + " body=" + fallback.RawJson);
                    onComplete?.Invoke(fallback);
                }
                yield break;
            }
        }

        private static void ApplyTelemetry(
            CoachFeedbackResult result,
            int attempts,
            int requestBytes,
            float startedAt)
        {
            if (result == null) return;
            result.RequestAttemptCount = Mathf.Max(0, attempts);
            result.RequestByteCount = Mathf.Max(0, requestBytes);
            result.RequestElapsedMilliseconds = Mathf.Max(0,
                Mathf.RoundToInt((Time.realtimeSinceStartup - startedAt) * 1000f));
        }

        public static int GetMaxTokens(CoachFeedbackPurpose? purpose) => purpose switch
        {
            CoachFeedbackPurpose.CriticalIssue => 180,
            CoachFeedbackPurpose.Example => 120,
            CoachFeedbackPurpose.AdditionalSuggestion => 240,
            CoachFeedbackPurpose.LearnerSocratic => 400,
            CoachFeedbackPurpose.TargetedAdvice => 400,
            CoachFeedbackPurpose.DirectAdvice => 400,
            CoachFeedbackPurpose.ConversationalFollowUp => 400,
            CoachFeedbackPurpose.CreeiModelAnswer => 400,
            _ => 500
        };

        public static bool IsRetryableFeedbackFailure(
            UnityWebRequest.Result result,
            long statusCode)
        {
            if (result == UnityWebRequest.Result.ConnectionError)
            {
                return true;
            }

            return statusCode == 408 || statusCode == 429 || statusCode >= 500;
        }

        private static string BuildRequestFailureDebugInfo(
            string endpoint,
            UnityWebRequest.Result result,
            long statusCode,
            string error,
            int attempts,
            int maxAttempts)
        {
            string likelyCause = statusCode switch
            {
                401 or 403 => "Likely API key or permission problem.",
                429 => "Likely quota or rate-limit problem.",
                >= 500 => "Likely upstream server problem.",
                0 => "Likely network, DNS, proxy, certificate, or timeout problem.",
                _ => "Check the HTTP status and response body."
            };

            return "Network/API request failed. " +
                   $"result={result}; http_status={statusCode}; error={error}; endpoint={endpoint}; " +
                   $"attempts={attempts}/{maxAttempts}. " +
                   likelyCause;
        }

        private static string TrimForLog(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            string compact = value.Replace("\r", " ").Replace("\n", " ").Trim();
            return compact.Length <= 600 ? compact : compact.Substring(0, 600) + "...";
        }

        private static CoachFeedbackResult TryParseOpenAIResponse(string responseJson, out string failureReason)
        {
            failureReason = string.Empty;
            try
            {
                JObject response = JObject.Parse(responseJson);
                string outputText = ExtractOutputText(response);
                if (string.IsNullOrWhiteSpace(outputText))
                {
                    failureReason = "The response did not contain choices[0].message.content, output_text, or output[].content[].text.";
                    return null;
                }

                JObject parsedJson = JObject.Parse(StripJsonCodeFence(outputText));
                CoachFeedbackResult result = ParseResultJson(parsedJson);
                result.Source = CoachFeedbackSource.OpenAI;
                result.RawJson = outputText;
                return result;
            }
            catch (Exception ex)
            {
                failureReason = "Parser error: " + ex.Message;
                Debug.LogWarning("Debate coach feedback parse failed: " + failureReason);
                return null;
            }
        }

        private static CoachFeedbackResult ParseResultJson(JObject json)
        {
            string feedbackText = ReadString(json, "feedback_text", string.Empty);
            if (string.IsNullOrWhiteSpace(feedbackText))
            {
                feedbackText = CombineAlternativeFeedbackFields(json);
            }

            CreeiModelExampleSet examples = ParseNestedCreeiModelExamples(json);
            if (!IsValidCreeiModelExampleSet(examples))
                examples = ParseLabelledCreeiModelExamples(feedbackText);
            if (IsValidCreeiModelExampleSet(examples))
                feedbackText = BuildCreeiModelHistoryText(examples);

            return new CoachFeedbackResult
            {
                StrongComponent = ReadString(json, "strong_component", "Claim"),
                WeakComponent = ReadString(json, "weak_component", "Evidence"),
                DominantStrategy = ReadString(json, "dominant_strategy", "Logos"),
                RecommendedStrategy = ReadString(json, "recommended_strategy", "Logos"),
                FeedbackType = ReadString(json, "feedback_type", "Evidence Support"),
                FeedbackLevel = Enum.TryParse(ReadString(json, "feedback_level", "Level2"), true, out CoachFeedbackLevel level)
                    ? level
                    : CoachFeedbackLevel.Level2,
                FeedbackText = feedbackText,
                NextAction = ReadString(json, "next_action", "add evidence"),
                TargetSuccessCriterion = ReadString(json, "target_success_criterion", string.Empty),
                CreeiModelExamples = examples
            };
        }

        private static CreeiModelExampleSet ParseNestedCreeiModelExamples(JObject json)
        {
            if (json?["creei_examples"] is not JObject nested)
                return new CreeiModelExampleSet();
            return new CreeiModelExampleSet
            {
                Claim = ReadString(nested, "claim", string.Empty).Trim(),
                Reason = ReadString(nested, "reason", string.Empty).Trim(),
                Evidence = ReadString(nested, "evidence", string.Empty).Trim(),
                Explanation = ReadString(nested, "explanation", string.Empty).Trim(),
                Impact = ReadString(nested, "impact", string.Empty).Trim()
            };
        }

        private static CreeiModelExampleSet ParseLabelledCreeiModelExamples(string text)
        {
            CreeiModelExampleSet result = new();
            if (string.IsNullOrWhiteSpace(text)) return result;
            HashSet<string> seenLabels = new(StringComparer.OrdinalIgnoreCase);
            foreach (string rawLine in text.Replace("\r", string.Empty).Split('\n'))
            {
                string line = rawLine.Trim();
                AssignLabelledValue(line, "Claim:", seenLabels,
                    value => result.Claim = value);
                AssignLabelledValue(line, "Reason:", seenLabels,
                    value => result.Reason = value);
                AssignLabelledValue(line, "Evidence:", seenLabels,
                    value => result.Evidence = value);
                AssignLabelledValue(line, "Explanation:", seenLabels,
                    value => result.Explanation = value);
                AssignLabelledValue(line, "Impact:", seenLabels,
                    value => result.Impact = value);
            }
            return result;
        }

        private static void AssignLabelledValue(
            string line,
            string label,
            ISet<string> seenLabels,
            Action<string> assign)
        {
            if (!line.StartsWith(label, StringComparison.OrdinalIgnoreCase)) return;
            assign(seenLabels.Add(label)
                ? line.Substring(label.Length).Trim()
                : "\0");
        }

        private static string CombineAlternativeFeedbackFields(JObject json)
        {
            string diagnosis = ReadString(json, "diagnosis", string.Empty);
            string nextStep = ReadString(json, "next_step_strategy", string.Empty);
            if (string.IsNullOrWhiteSpace(diagnosis))
            {
                return nextStep;
            }

            return string.IsNullOrWhiteSpace(nextStep)
                ? diagnosis
                : diagnosis + " " + nextStep;
        }

        private static string ExtractOutputText(JObject response)
        {
            JToken chatContent = response.SelectToken("choices[0].message.content");
            if (chatContent?.Type == JTokenType.String)
            {
                string chatText = chatContent.Value<string>();
                if (!string.IsNullOrWhiteSpace(chatText))
                {
                    return chatText;
                }
            }

            string direct = (string)response["output_text"];
            if (!string.IsNullOrWhiteSpace(direct))
            {
                return direct;
            }

            JToken output = response["output"];
            if (output == null)
            {
                return string.Empty;
            }

            foreach (JToken item in output.Children())
            {
                JToken content = item["content"];
                if (content == null)
                {
                    continue;
                }

                foreach (JToken contentItem in content.Children())
                {
                    string text = (string)contentItem["text"];
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        return text;
                    }
                }
            }

            return string.Empty;
        }

        private static string StripJsonCodeFence(string value)
        {
            string trimmed = value?.Trim() ?? string.Empty;
            if (!trimmed.StartsWith("```", StringComparison.Ordinal))
            {
                return trimmed;
            }

            int firstLineEnd = trimmed.IndexOf('\n');
            int closingFence = trimmed.LastIndexOf("```", StringComparison.Ordinal);
            if (firstLineEnd < 0 || closingFence <= firstLineEnd)
            {
                return trimmed;
            }

            return trimmed.Substring(firstLineEnd + 1, closingFence - firstLineEnd - 1).Trim();
        }

        private static string ReadString(JObject json, string key, string fallback)
        {
            string value = (string)json[key];
            return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        }

        private static bool ContainsAny(string value, params string[] needles)
        {
            return needles.Any(needle => value.Contains(needle));
        }

        private static string NormalizeStrategy(string value)
        {
            string lower = value.ToLowerInvariant();
            if (lower.Contains("ethos")) return "Ethos";
            if (lower.Contains("pathos")) return "Pathos";
            if (lower.Contains("logos")) return "Logos";
            return "Logos";
        }

        private static string InferDominantStrategy(string lower, string fallback)
        {
            if (ContainsAny(lower, "feel", "care", "pressure", "students worry", "confidence")) return "Pathos";
            if (ContainsAny(lower, "fair", "responsible", "balanced", "honest")) return "Ethos";
            if (ContainsAny(lower, "because", "example", "data", "research", "reason")) return "Logos";
            return fallback;
        }
    }
}
