using System;
using System.Collections;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;

namespace Game.Debate
{
    [Serializable]
    public sealed class CreeiOpponentStatementRequest
    {
        public string Topic = string.Empty;
        public string OpponentStance = string.Empty;
        public CreeiStage Stage = CreeiStage.Claim;
        public string PreviousOpponentCreeiStages = string.Empty;
    }

    [Serializable]
    public sealed class CreeiOpponentStatementResult
    {
        public string SpeechText = string.Empty;
        public string RawJson = string.Empty;
        public string DebugInfo = string.Empty;
        public bool IsValid;
    }

    public sealed class CreeiOpponentStatementGenerator
    {
        private readonly string _model;
        private readonly int _timeoutSeconds;
        private readonly string _baseUrl;
        private readonly string _apiKeyOverride;

        public CreeiOpponentStatementGenerator(
            string model,
            float timeoutSeconds,
            string baseUrl,
            string apiKeyOverride)
        {
            _model = string.IsNullOrWhiteSpace(model) ? "gpt-4o-mini" : model.Trim();
            _timeoutSeconds = Mathf.Max(3, Mathf.RoundToInt(timeoutSeconds));
            _baseUrl = baseUrl?.Trim() ?? string.Empty;
            _apiKeyOverride = apiKeyOverride?.Trim() ?? string.Empty;
        }

        public IEnumerator GenerateStatement(
            CreeiOpponentStatementRequest requestData,
            Action<CreeiOpponentStatementResult> onComplete)
        {
            CreeiOpponentStatementRequest safeRequest = requestData ?? new CreeiOpponentStatementRequest();
            string apiKey = DebateCommandParser.ResolveApiKey(_apiKeyOverride);
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                onComplete?.Invoke(new CreeiOpponentStatementResult
                {
                    DebugInfo = "API key missing: Leo GPT was not called."
                });
                yield break;
            }

            string endpoint = DebateCommandParser.ResolveChatCompletionsEndpoint(
                DebateCommandParser.ResolveBaseUrl(_baseUrl));
            byte[] body = Encoding.UTF8.GetBytes(BuildOpenAIRequestJson(safeRequest).ToString(Formatting.None));
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
                onComplete?.Invoke(new CreeiOpponentStatementResult
                {
                    RawJson = request.downloadHandler?.text ?? string.Empty,
                    DebugInfo = $"Leo GPT request failed. http_status={request.responseCode}; error={request.error}"
                });
                yield break;
            }

            onComplete?.Invoke(ParseResponse(request.downloadHandler.text));
        }

        public static string BuildPrompt(CreeiOpponentStatementRequest request)
        {
            CreeiOpponentStatementRequest safeRequest = request ?? new CreeiOpponentStatementRequest();
            string priorStages = string.IsNullOrWhiteSpace(safeRequest.PreviousOpponentCreeiStages)
                ? "(none; this is the first stage)"
                : safeRequest.PreviousOpponentCreeiStages.Trim();

            return
                "You are Leo in an English debate.\n" +
                "Topic: " + safeRequest.Topic + "\n" +
                "Your position: " + safeRequest.OpponentStance + "\n" +
                "Current CREEI stage: " + safeRequest.Stage + "\n\n" +
                "Method for this stage:\n" + CreeiStageGuidance.GetMethod(safeRequest.Stage) + "\n\n" +
                "Your own completed CREEI stages:\n" + priorStages + "\n\n" +
                "Produce only the new " + safeRequest.Stage + " component. Make it coherent with your own completed stages.\n" +
                "Do not respond to the learner and do not ask the learner a question.\n" +
                "Do not mention the CREEI framework or stage label in the spoken content.\n" +
                "Use one or two short sentences. Write in English only.\n" +
                "Return one JSON object only: {\"speech_text\":\"...\"}";
        }

        private JObject BuildOpenAIRequestJson(CreeiOpponentStatementRequest request)
        {
            return new JObject
            {
                ["model"] = _model,
                ["messages"] = new JArray
                {
                    new JObject
                    {
                        ["role"] = "system",
                        ["content"] = "Return strict JSON with exactly one English field named speech_text."
                    },
                    new JObject
                    {
                        ["role"] = "user",
                        ["content"] = BuildPrompt(request)
                    }
                },
                ["response_format"] = new JObject { ["type"] = "json_object" }
            };
        }

        private static CreeiOpponentStatementResult ParseResponse(string responseJson)
        {
            try
            {
                JObject response = JObject.Parse(responseJson);
                string content = response["choices"]?[0]?["message"]?["content"]?.ToString();
                if (string.IsNullOrWhiteSpace(content))
                {
                    return new CreeiOpponentStatementResult
                    {
                        RawJson = responseJson ?? string.Empty,
                        DebugInfo = "Leo GPT response did not contain choices[0].message.content."
                    };
                }

                string cleaned = content.Trim();
                if (cleaned.StartsWith("```", StringComparison.Ordinal))
                {
                    int firstLineEnd = cleaned.IndexOf('\n');
                    int lastFence = cleaned.LastIndexOf("```", StringComparison.Ordinal);
                    cleaned = firstLineEnd >= 0 && lastFence > firstLineEnd
                        ? cleaned.Substring(firstLineEnd + 1, lastFence - firstLineEnd - 1).Trim()
                        : cleaned;
                }

                JObject payload = JObject.Parse(cleaned);
                string speechText = payload["speech_text"]?.ToString()?.Trim() ?? string.Empty;
                return new CreeiOpponentStatementResult
                {
                    SpeechText = speechText,
                    RawJson = content,
                    DebugInfo = string.IsNullOrWhiteSpace(speechText)
                        ? "Leo GPT returned JSON, but speech_text was empty."
                        : "Leo GPT returned a valid stage statement.",
                    IsValid = !string.IsNullOrWhiteSpace(speechText)
                };
            }
            catch (Exception ex)
            {
                return new CreeiOpponentStatementResult
                {
                    RawJson = responseJson ?? string.Empty,
                    DebugInfo = "Leo GPT response parse failed: " + ex.Message
                };
            }
        }
    }
}
