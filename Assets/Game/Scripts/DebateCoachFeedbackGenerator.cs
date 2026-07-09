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
            CoachFeedbackRequest safeRequest = request ?? new CoachFeedbackRequest();
            string apiKey = DebateCommandParser.ResolveApiKey(_apiKeyOverride);
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                onComplete?.Invoke(BuildLocalFallback(safeRequest));
                yield break;
            }

            CoachFeedbackResult parsed = null;
            bool completed = false;
            yield return RequestOpenAIFeedback(
                safeRequest,
                apiKey,
                DebateCommandParser.ResolveResponsesEndpoint(DebateCommandParser.ResolveBaseUrl(_baseUrl)),
                result =>
                {
                    parsed = result;
                    completed = true;
                });

            onComplete?.Invoke(completed && parsed != null ? parsed : BuildLocalFallback(safeRequest));
        }

        public static JObject BuildStructuredOutputSchema()
        {
            JArray creei = new("Claim", "Reason", "Evidence", "Explanation", "Impact");
            JArray strategies = new("Logos", "Ethos", "Pathos", "Mixed", "Any");
            JArray levels = new("Level1", "Level2", "Level3", "Summary");
            JArray nextActions = new("clarify", "add evidence", "explain", "show impact", "qualify", "balance", "empathize");

            return new JObject
            {
                ["type"] = "object",
                ["additionalProperties"] = false,
                ["properties"] = new JObject
                {
                    ["strong_component"] = new JObject { ["type"] = "string", ["enum"] = creei },
                    ["weak_component"] = new JObject { ["type"] = "string", ["enum"] = creei },
                    ["dominant_strategy"] = new JObject { ["type"] = "string", ["enum"] = strategies },
                    ["recommended_strategy"] = new JObject { ["type"] = "string", ["enum"] = strategies },
                    ["feedback_type"] = new JObject { ["type"] = "string" },
                    ["feedback_level"] = new JObject { ["type"] = "string", ["enum"] = levels },
                    ["feedback_text"] = new JObject { ["type"] = "string" },
                    ["next_action"] = new JObject { ["type"] = "string", ["enum"] = nextActions }
                },
                ["required"] = new JArray
                {
                    "strong_component",
                    "weak_component",
                    "dominant_strategy",
                    "recommended_strategy",
                    "feedback_type",
                    "feedback_level",
                    "feedback_text",
                    "next_action"
                }
            };
        }

        public static string BuildPrompt(CoachFeedbackRequest request)
        {
            CoachFeedbackRequest safeRequest = request ?? new CoachFeedbackRequest();
            string levelInstruction = safeRequest.FeedbackLevel switch
            {
                CoachFeedbackLevel.Level3 =>
                    "Feedback level: Level3. Give only a sentence frame or short example fragment. Do not write a full answer for the learner.",
                CoachFeedbackLevel.Summary =>
                    "Feedback level: Summary. Summarize the practice debate in no more than four short sentences. Do not give a Transfer Debate answer.",
                CoachFeedbackLevel.Level1 =>
                    "Feedback level: Level1. Diagnose only the main problem type in one short sentence.",
                _ =>
                    "Feedback level: Level2. Give one diagnosis sentence and one next-step strategy sentence. Keep feedback within two short sentences."
            };

            return
                "You are a debate strategy coach for an L2 English learner.\n" +
                "You only provide short post-turn feedback during Practice Debate.\n" +
                "Do not write a full answer for the learner.\n" +
                "Do not interrupt or coach before the learner speaks.\n" +
                "Diagnose the learner's utterance using CREEI and Ethos/Pathos/Logos.\n" +
                "Use encouraging but academically focused language.\n" +
                levelInstruction + "\n\n" +
                "Return JSON only with the requested schema.\n\n" +
                "Context:\n" +
                "condition: " + safeRequest.Condition + "\n" +
                "stage: " + safeRequest.Stage + "\n" +
                "topic_id: " + safeRequest.TopicId + "\n" +
                "topic: " + safeRequest.Topic + "\n" +
                "learner_side: " + safeRequest.PlayerSide + "\n" +
                "turn_id: " + safeRequest.TurnId + "\n" +
                "selected_strategy: " + safeRequest.SelectedStrategy + "\n" +
                "previous_commands: " + string.Join(" | ", safeRequest.PreviousCommands ?? Array.Empty<string>()) + "\n" +
                "previous_npc_versions_viewed: " + string.Join(" | ", safeRequest.PreviousNpcVersionsViewed ?? Array.Empty<string>()) + "\n\n" +
                "opponent_last_turn:\n" + safeRequest.OpponentUtteranceText + "\n\n" +
                "learner_current_turn:\n" + safeRequest.PlayerUtteranceText;
        }

        public JObject BuildOpenAIRequestJson(CoachFeedbackRequest request)
        {
            return new JObject
            {
                ["model"] = _model,
                ["input"] = new JArray
                {
                    new JObject
                    {
                        ["role"] = "system",
                        ["content"] = "Return only strict JSON for a debate Coach Agent feedback result."
                    },
                    new JObject
                    {
                        ["role"] = "user",
                        ["content"] = BuildPrompt(request)
                    }
                },
                ["store"] = false,
                ["text"] = new JObject
                {
                    ["format"] = new JObject
                    {
                        ["type"] = "json_schema",
                        ["name"] = "debate_coach_feedback",
                        ["strict"] = true,
                        ["schema"] = BuildStructuredOutputSchema()
                    }
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
                    FeedbackText = "You could start with: For example, in a real English class... Then add one short detail that supports your point.",
                    NextAction = "add evidence",
                    Source = CoachFeedbackSource.Rules
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
                    FeedbackText = "You mainly practiced giving a clear position. The part to improve is Explanation: connect each example back to your claim. In the next debate, try to make the evidence-to-impact link more explicit.",
                    NextAction = "explain",
                    Source = CoachFeedbackSource.Rules
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

            string feedback = weak switch
            {
                "Evidence" => "Your claim is understandable, but the evidence is still weak. Next, try using Logos by adding one concrete classroom example.",
                "Impact" => "Your reason is clear, but the impact is not explicit yet. Next, try using Pathos by explaining why this issue matters to students or teachers.",
                "Reason" => "Your position is visible, but the reason needs to be clearer. Next, clarify the main reason before adding details.",
                _ => "Your claim is clear, but the explanation is still weak. Next, try using Logos to explain why your example supports your point."
            };

            return new CoachFeedbackResult
            {
                StrongComponent = string.IsNullOrWhiteSpace(utterance) ? "Claim" : "Claim",
                WeakComponent = weak,
                DominantStrategy = InferDominantStrategy(lower, selectedStrategy),
                RecommendedStrategy = weak == "Impact" ? "Pathos" : selectedStrategy,
                FeedbackType = weak + " Support",
                FeedbackLevel = safeRequest.FeedbackLevel,
                FeedbackText = feedback,
                NextAction = nextAction,
                Source = CoachFeedbackSource.Rules
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

            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning(
                    $"Debate coach feedback request failed. endpoint={endpoint}, result={request.result}, status={request.responseCode}, error={request.error}, body={TrimForLog(request.downloadHandler?.text)}");
                yield break;
            }

            CoachFeedbackResult parsed = TryParseOpenAIResponse(request.downloadHandler.text);
            if (parsed != null)
            {
                onComplete?.Invoke(parsed);
            }
            else
            {
                Debug.LogWarning("Debate coach feedback response could not be parsed. body=" + TrimForLog(request.downloadHandler.text));
            }
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

        private static CoachFeedbackResult TryParseOpenAIResponse(string responseJson)
        {
            try
            {
                JObject response = JObject.Parse(responseJson);
                string outputText = ExtractOutputText(response);
                if (string.IsNullOrWhiteSpace(outputText))
                {
                    return null;
                }

                JObject parsedJson = JObject.Parse(outputText);
                CoachFeedbackResult result = ParseResultJson(parsedJson);
                result.Source = CoachFeedbackSource.OpenAI;
                result.RawJson = outputText;
                return result;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("Debate coach feedback parse failed: " + ex.Message);
                return null;
            }
        }

        private static CoachFeedbackResult ParseResultJson(JObject json)
        {
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
                FeedbackText = ReadString(json, "feedback_text", string.Empty),
                NextAction = ReadString(json, "next_action", "add evidence")
            };
        }

        private static string ExtractOutputText(JObject response)
        {
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
