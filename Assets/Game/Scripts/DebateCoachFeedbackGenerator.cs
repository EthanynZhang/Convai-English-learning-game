using System;
using System.Collections;
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
            yield return RequestOpenAIFeedback(
                safeRequest,
                apiKey,
                DebateCommandParser.ResolveChatCompletionsEndpoint(DebateCommandParser.ResolveBaseUrl(_baseUrl)),
                result =>
                {
                    parsed = result;
                    completed = true;
                });

            if (requestVersion != _requestVersion)
            {
                yield break;
            }

            if (completed && parsed != null && parsed.Source == CoachFeedbackSource.OpenAI)
            {
                parsed.FeedbackLevel = safeRequest.FeedbackLevel;
                if (safeRequest.FeedbackLevel == CoachFeedbackLevel.Level3 &&
                    !IsConcreteExampleText(parsed.FeedbackText))
                {
                    parsed.Source = CoachFeedbackSource.Rules;
                    parsed.DebugInfo =
                        "GPT returned advice or meta-commentary instead of a concrete example sentence. The result was rejected and was not sent to Convai.";
                    onComplete?.Invoke(parsed);
                    yield break;
                }
                if (safeRequest.FeedbackFormat == CoachFeedbackFormat.Scene04CreeiDetailed &&
                    safeRequest.FeedbackLevel == CoachFeedbackLevel.Level2 &&
                    !IsDetailedCreeiFeedbackText(parsed.FeedbackText))
                {
                    parsed.Source = CoachFeedbackSource.Rules;
                    parsed.DebugInfo =
                        "GPT did not return the required CREEI gaps and Specific advice sections in 4 to 6 sentences. The result was rejected and was not sent to Convai.";
                    onComplete?.Invoke(parsed);
                    yield break;
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
            string stageInstruction = detailedCreei
                ? "The condition-blind diagnosis is already complete. Do not rediagnose the learner.\n" +
                  "Use only creei_gap_summary and creei_missing_or_weak_components for the whole-argument diagnosis. " +
                  "Use the confirmed focus, target success criterion, learner request, and learner words for the specific advice."
                : "The condition-blind diagnosis is already complete. Do not rediagnose the learner.\n" +
                  "Respond only to the confirmed focus and target success criterion.";
            string levelInstruction = detailedCreei
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
                "You only provide short post-turn feedback during Practice Debate.\n" +
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
            return new JObject
            {
                ["model"] = _model,
                ["messages"] = new JArray
                {
                    new JObject
                    {
                        ["role"] = "system",
                        ["content"] = safeRequest.FeedbackLevel == CoachFeedbackLevel.Level3
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
            Action<CoachFeedbackResult> onComplete)
        {
            byte[] body = Encoding.UTF8.GetBytes(BuildOpenAIRequestJson(requestData).ToString(Formatting.None));
            using UnityWebRequest request = new(endpoint, UnityWebRequest.kHttpVerbPOST)
            {
                uploadHandler = new UploadHandlerRaw(body),
                downloadHandler = new DownloadHandlerBuffer(),
                timeout = _timeoutSeconds
            };

            request.SetRequestHeader("Authorization", "Bearer " + apiKey.Trim());
            request.SetRequestHeader("Content-Type", "application/json");

            _activeRequest = request;
            yield return request.SendWebRequest();
            if (ReferenceEquals(_activeRequest, request))
            {
                _activeRequest = null;
            }

            if (request.result != UnityWebRequest.Result.Success)
            {
                string rawBody = TrimForLog(request.downloadHandler?.text);
                CoachFeedbackResult fallback = BuildLocalFallback(requestData);
                fallback.DebugInfo = BuildRequestFailureDebugInfo(endpoint, request.result, request.responseCode, request.error);
                fallback.RawJson = string.IsNullOrWhiteSpace(rawBody)
                    ? "(empty response body)"
                    : rawBody;
                Debug.LogWarning(
                    $"Debate coach feedback request failed. endpoint={endpoint}, result={request.result}, status={request.responseCode}, error={request.error}, body={rawBody}");
                onComplete?.Invoke(fallback);
                yield break;
            }

            CoachFeedbackResult parsed = TryParseOpenAIResponse(request.downloadHandler.text, out string parseFailure);
            if (parsed != null)
            {
                onComplete?.Invoke(parsed);
            }
            else
            {
                CoachFeedbackResult fallback = BuildLocalFallback(requestData);
                fallback.DebugInfo = "HTTP request succeeded, but GPT response could not be parsed. " + parseFailure;
                fallback.RawJson = TrimForLog(request.downloadHandler.text);
                Debug.LogWarning("Debate coach feedback response could not be parsed. " + parseFailure + " body=" + fallback.RawJson);
                onComplete?.Invoke(fallback);
            }
        }

        private static string BuildRequestFailureDebugInfo(
            string endpoint,
            UnityWebRequest.Result result,
            long statusCode,
            string error)
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
                   $"result={result}; http_status={statusCode}; error={error}; endpoint={endpoint}. " +
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
                TargetSuccessCriterion = ReadString(json, "target_success_criterion", string.Empty)
            };
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
