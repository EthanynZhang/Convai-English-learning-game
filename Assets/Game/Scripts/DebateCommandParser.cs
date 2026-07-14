using System;
using System.Collections;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;

namespace Game.Debate
{
    public sealed class DebateCommandParser
    {
        private const string DefaultResponsesEndpoint = "https://api.openai.com/v1/responses";
        private const string DefaultChatCompletionsEndpoint = "https://api.openai.com/v1/chat/completions";
        private const string ApiKeyEnvironmentVariable = "OPENAI_API_KEY";
        private const string RelayApiKeyEnvironmentVariable = "DEBATE_OPENAI_API_KEY";
        private const string BaseUrlEnvironmentVariable = "OPENAI_BASE_URL";
        private const string RelayBaseUrlEnvironmentVariable = "DEBATE_OPENAI_BASE_URL";
        private const string ApiKeyPlayerPrefsKey = "DEBATE_OPENAI_API_KEY";
        private const string BaseUrlPlayerPrefsKey = "DEBATE_OPENAI_BASE_URL";
        private const string ProjectRelayEndpoint = "https://api.meding.site/v1/chat/completions";
        private const string ProjectRelayApiKey = "sk-jp3lFBmZA8Jv7He2ulCkpJvQUsR2MkL1kCyvIopmNGGs40c8";
        private const string DefaultModel = "gpt-4o-mini";

        private readonly string _model;
        private readonly int _timeoutSeconds;
        private readonly string _baseUrl;
        private readonly string _apiKeyOverride;

        public DebateCommandParser(
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

        public IEnumerator ParseCommand(
            string commandText,
            string debateTopic,
            Action<DebateCommandParseResult> onComplete)
        {
            string safeCommand = commandText?.Trim() ?? string.Empty;
            string apiKey = ResolveApiKey(_apiKeyOverride);
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                onComplete?.Invoke(ParseWithLocalRules(safeCommand));
                yield break;
            }

            DebateCommandParseResult parsed = null;
            bool completed = false;
            yield return RequestOpenAIParse(
                safeCommand,
                debateTopic,
                apiKey,
                ResolveResponsesEndpoint(ResolveBaseUrl(_baseUrl)),
                result =>
                {
                    parsed = result;
                    completed = true;
                });

            if (!completed || parsed == null)
            {
                parsed = ParseWithLocalRules(safeCommand);
            }

            onComplete?.Invoke(parsed);
        }

        public static string ResolveResponsesEndpoint(string baseUrl)
        {
            string trimmed = baseUrl?.Trim().TrimEnd('/') ?? string.Empty;
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                return DefaultResponsesEndpoint;
            }

            if (trimmed.EndsWith("/v1/responses", StringComparison.OrdinalIgnoreCase))
            {
                return trimmed;
            }

            if (trimmed.EndsWith("/responses", StringComparison.OrdinalIgnoreCase))
            {
                return trimmed;
            }

            if (trimmed.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
            {
                return trimmed + "/responses";
            }

            return trimmed + "/v1/responses";
        }

        public static string ResolveChatCompletionsEndpoint(string baseUrl)
        {
            string trimmed = baseUrl?.Trim().TrimEnd('/') ?? string.Empty;
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                return DefaultChatCompletionsEndpoint;
            }

            if (trimmed.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
            {
                return trimmed;
            }

            if (trimmed.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
            {
                return trimmed + "/chat/completions";
            }

            return trimmed + "/v1/chat/completions";
        }

        public static string ResolveApiKey(string overrideValue = "")
        {
            if (!string.IsNullOrWhiteSpace(overrideValue))
            {
                return overrideValue.Trim();
            }

            string relayKey = Environment.GetEnvironmentVariable(RelayApiKeyEnvironmentVariable);
            if (!string.IsNullOrWhiteSpace(relayKey))
            {
                return relayKey.Trim();
            }

            string openAIKey = Environment.GetEnvironmentVariable(ApiKeyEnvironmentVariable);
            if (!string.IsNullOrWhiteSpace(openAIKey))
            {
                return openAIKey.Trim();
            }

            string playerPrefsKey = PlayerPrefs.GetString(ApiKeyPlayerPrefsKey, string.Empty).Trim();
            return !string.IsNullOrWhiteSpace(playerPrefsKey)
                ? playerPrefsKey
                : ProjectRelayApiKey;
        }

        public static string ResolveBaseUrl(string overrideValue = "")
        {
            if (!string.IsNullOrWhiteSpace(overrideValue))
            {
                return overrideValue.Trim();
            }

            string relayBaseUrl = Environment.GetEnvironmentVariable(RelayBaseUrlEnvironmentVariable);
            if (!string.IsNullOrWhiteSpace(relayBaseUrl))
            {
                return relayBaseUrl.Trim();
            }

            string openAIBaseUrl = Environment.GetEnvironmentVariable(BaseUrlEnvironmentVariable);
            if (!string.IsNullOrWhiteSpace(openAIBaseUrl))
            {
                return openAIBaseUrl.Trim();
            }

            string playerPrefsBaseUrl = PlayerPrefs.GetString(BaseUrlPlayerPrefsKey, string.Empty).Trim();
            return !string.IsNullOrWhiteSpace(playerPrefsBaseUrl)
                ? playerPrefsBaseUrl
                : ProjectRelayEndpoint;
        }

        public static DebateCommandParseResult ParseWithLocalRules(string commandText)
        {
            string command = (commandText ?? string.Empty).Trim();
            string lower = command.ToLowerInvariant();

            DebateCommandParseResult result = new()
            {
                Operation = InferOperation(lower),
                TargetMove = InferTargetMove(lower),
                StrategyDimension = InferStrategy(lower),
                Tone = InferTone(lower),
                TargetSide = InferTargetSide(lower),
                Confidence = string.IsNullOrWhiteSpace(command) ? 0.1f : 0.62f,
                Source = DebateCommandParseSource.Rules
            };

            result.SubStrategy = InferSubStrategy(result);
            return result;
        }

        public static JObject BuildOpenAIRequestJson(string model, string commandText, string debateTopic)
        {
            JObject schema = BuildStructuredOutputSchema();
            JArray input = new()
            {
                new JObject
                {
                    ["role"] = "system",
                    ["content"] =
                        "You parse learner debate-coaching commands into a fixed taxonomy. " +
                        "Return only the requested JSON. Use empty strings for unknown optional text fields."
                },
                new JObject
                {
                    ["role"] = "user",
                    ["content"] =
                        "Debate topic: " + (debateTopic ?? string.Empty) + "\n" +
                        "Learner command: " + (commandText ?? string.Empty) + "\n\n" +
                        "Map the command to operation + target_move + strategy_dimension + tone/side."
                }
            };

            return new JObject
            {
                ["model"] = string.IsNullOrWhiteSpace(model) ? DefaultModel : model,
                ["input"] = input,
                ["store"] = false,
                ["text"] = new JObject
                {
                    ["format"] = new JObject
                    {
                        ["type"] = "json_schema",
                        ["name"] = "debate_command_parse",
                        ["strict"] = true,
                        ["schema"] = schema
                    }
                }
            };
        }

        public static JObject BuildStructuredOutputSchema()
        {
            return new JObject
            {
                ["type"] = "object",
                ["additionalProperties"] = false,
                ["properties"] = new JObject
                {
                    ["operation"] = EnumStringProperty(DebateCommandTaxonomy.Operations),
                    ["target_move"] = EnumStringProperty(DebateCommandTaxonomy.TargetMoves),
                    ["strategy_dimension"] = EnumStringProperty(DebateCommandTaxonomy.StrategyDimensions),
                    ["sub_strategy"] = new JObject { ["type"] = "string" },
                    ["tone"] = new JObject { ["type"] = "string" },
                    ["target_side"] = new JObject { ["type"] = "string" },
                    ["confidence"] = new JObject
                    {
                        ["type"] = "number",
                        ["minimum"] = 0,
                        ["maximum"] = 1
                    }
                },
                ["required"] = new JArray
                {
                    "operation",
                    "target_move",
                    "strategy_dimension",
                    "sub_strategy",
                    "tone",
                    "target_side",
                    "confidence"
                }
            };
        }

        private IEnumerator RequestOpenAIParse(
            string commandText,
            string debateTopic,
            string apiKey,
            string responsesEndpoint,
            Action<DebateCommandParseResult> onComplete)
        {
            JObject requestJson = BuildOpenAIRequestJson(_model, commandText, debateTopic);
            byte[] body = Encoding.UTF8.GetBytes(requestJson.ToString(Formatting.None));

            using UnityWebRequest request = new(responsesEndpoint, UnityWebRequest.kHttpVerbPOST)
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
                yield break;
            }

            DebateCommandParseResult parsed = TryParseOpenAIResponse(request.downloadHandler.text);
            if (parsed != null)
            {
                onComplete?.Invoke(parsed);
            }
        }

        private static DebateCommandParseResult TryParseOpenAIResponse(string responseJson)
        {
            if (string.IsNullOrWhiteSpace(responseJson))
            {
                return null;
            }

            try
            {
                JObject response = JObject.Parse(responseJson);
                string outputText = ExtractOutputText(response);
                if (string.IsNullOrWhiteSpace(outputText))
                {
                    return null;
                }

                JObject parsedJson = JObject.Parse(outputText);
                DebateCommandParseResult result = ParseResultJson(parsedJson);
                result.Source = DebateCommandParseSource.OpenAI;
                result.RawJson = outputText;
                return result;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("Debate command OpenAI parse failed: " + ex.Message);
                return null;
            }
        }

        private static DebateCommandParseResult ParseResultJson(JObject parsedJson)
        {
            DebateCommandParseResult fallback = ParseWithLocalRules(string.Empty);
            fallback.Operation = ParseEnum(parsedJson, "operation", DebateCommandOperation.Transform);
            fallback.TargetMove = ParseEnum(parsedJson, "target_move", DebateTargetMove.Response);
            fallback.StrategyDimension = ParseEnum(parsedJson, "strategy_dimension", DebateStrategyDimension.Any);
            fallback.SubStrategy = (string)parsedJson["sub_strategy"] ?? string.Empty;
            fallback.Tone = (string)parsedJson["tone"] ?? string.Empty;
            fallback.TargetSide = string.IsNullOrWhiteSpace((string)parsedJson["target_side"])
                ? "Any"
                : (string)parsedJson["target_side"];
            fallback.Confidence = Mathf.Clamp01((float?)parsedJson["confidence"] ?? 0.7f);
            return fallback;
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

        private static JObject EnumStringProperty(string[] values)
        {
            return new JObject
            {
                ["type"] = "string",
                ["enum"] = new JArray(values)
            };
        }

        private static TEnum ParseEnum<TEnum>(JObject json, string key, TEnum fallback)
            where TEnum : struct
        {
            string value = (string)json[key];
            return Enum.TryParse(value, true, out TEnum parsed) ? parsed : fallback;
        }

        private static DebateCommandOperation InferOperation(string lower)
        {
            if (ContainsAny(lower, "challenge", "harder", "follow-up question", "follow up question", "ask a", "question"))
            {
                return DebateCommandOperation.Challenge;
            }

            if (ContainsAny(lower, "compare", "two versions", "difference", "contrast"))
            {
                return DebateCommandOperation.Compare;
            }

            if (ContainsAny(lower, "diagnose", "analyze", "what is wrong", "problem", "weakness"))
            {
                return DebateCommandOperation.Diagnose;
            }

            if (ContainsAny(lower, "alternative", "another version", "different version"))
            {
                return DebateCommandOperation.GenerateAlternative;
            }

            if (ContainsAny(lower, "tone", "polite", "less direct", "softer", "more formal"))
            {
                return DebateCommandOperation.CalibrateTone;
            }

            if (ContainsAny(lower, "next move", "plan", "what should i say next"))
            {
                return DebateCommandOperation.PlanNextMove;
            }

            if (ContainsAny(lower, "reflect", "why did", "what did i do"))
            {
                return DebateCommandOperation.Reflect;
            }

            if (ContainsAny(lower, "clarify", "clearer", "simpler", "explain"))
            {
                return DebateCommandOperation.Clarify;
            }

            if (ContainsAny(lower, "strengthen", "stronger", "add", "support", "improve"))
            {
                return DebateCommandOperation.Strengthen;
            }

            return DebateCommandOperation.Transform;
        }

        private static DebateTargetMove InferTargetMove(string lower)
        {
            if (ContainsAny(lower, "follow-up question", "follow up question", "counter-question", "counter question", "ask", "question"))
            {
                return DebateTargetMove.CounterQuestion;
            }

            if (ContainsAny(lower, "evidence", "example", "data", "proof", "source", "research"))
            {
                return DebateTargetMove.Evidence;
            }

            if (ContainsAny(lower, "claim", "main point", "position", "thesis"))
            {
                return DebateTargetMove.Claim;
            }

            if (ContainsAny(lower, "warrant", "reasoning link", "connect the evidence", "explain why"))
            {
                return DebateTargetMove.Warrant;
            }

            if (ContainsAny(lower, "backing", "background"))
            {
                return DebateTargetMove.Backing;
            }

            if (ContainsAny(lower, "qualifier", "probably", "sometimes", "limit"))
            {
                return DebateTargetMove.Qualifier;
            }

            if (ContainsAny(lower, "concession", "admit", "acknowledge"))
            {
                return DebateTargetMove.Concession;
            }

            if (ContainsAny(lower, "rebuttal", "refute", "counterargument", "counter argument"))
            {
                return DebateTargetMove.Rebuttal;
            }

            if (ContainsAny(lower, "impact", "consequence", "why it matters", "empathetic", "empathy", "humane", "emotion"))
            {
                return DebateTargetMove.Impact;
            }

            if (ContainsAny(lower, "summary", "closing", "conclusion"))
            {
                return DebateTargetMove.SummaryClosing;
            }

            return DebateTargetMove.Response;
        }

        private static DebateStrategyDimension InferStrategy(string lower)
        {
            int logos = ContainsAny(lower, "logos", "logic", "logical", "evidence", "data", "reason", "proof", "cause", "effect") ? 1 : 0;
            int ethos = ContainsAny(lower, "ethos", "fair", "responsible", "credible", "trust", "honest", "balanced", "ethical") ? 1 : 0;
            int pathos = ContainsAny(lower, "pathos", "empathetic", "empathy", "emotion", "feeling", "humane", "pressure", "heart") ? 1 : 0;
            int count = logos + ethos + pathos;

            if (count > 1)
            {
                return DebateStrategyDimension.Mixed;
            }

            if (logos == 1)
            {
                return DebateStrategyDimension.Logos;
            }

            if (ethos == 1)
            {
                return DebateStrategyDimension.Ethos;
            }

            if (pathos == 1)
            {
                return DebateStrategyDimension.Pathos;
            }

            return DebateStrategyDimension.Any;
        }

        private static string InferTone(string lower)
        {
            if (ContainsAny(lower, "polite", "less direct", "softer"))
            {
                return "polite";
            }

            if (ContainsAny(lower, "empathetic", "empathy", "humane"))
            {
                return "empathetic";
            }

            if (ContainsAny(lower, "fair", "responsible", "balanced"))
            {
                return "fair and responsible";
            }

            if (ContainsAny(lower, "formal", "academic"))
            {
                return "formal";
            }

            return string.Empty;
        }

        private static string InferTargetSide(string lower)
        {
            if (ContainsAny(lower, "anna"))
            {
                return "Anna";
            }

            if (ContainsAny(lower, "mike"))
            {
                return "Mike";
            }

            if (ContainsAny(lower, "pro side", "supporting side"))
            {
                return "Pro";
            }

            if (ContainsAny(lower, "con side", "opposing side"))
            {
                return "Con";
            }

            return "Any";
        }

        private static string InferSubStrategy(DebateCommandParseResult result)
        {
            return result.StrategyDimension switch
            {
                DebateStrategyDimension.Logos => "reasoning/evidence",
                DebateStrategyDimension.Ethos => "credibility/fairness",
                DebateStrategyDimension.Pathos => "empathy/impact",
                DebateStrategyDimension.Mixed => "combined appeals",
                _ => string.Empty
            };
        }

        private static bool ContainsAny(string value, params string[] needles)
        {
            if (string.IsNullOrEmpty(value))
            {
                return false;
            }

            for (int i = 0; i < needles.Length; i++)
            {
                if (value.Contains(needles[i]))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
