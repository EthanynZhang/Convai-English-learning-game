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
    public sealed class MockDebateEvaluationRequest
    {
        public string Topic = string.Empty;
        public string LearnerStance = string.Empty;
        public string Transcript = string.Empty;
        public float DurationSeconds;
        public float TargetDurationSeconds = 180f;
    }

    [Serializable]
    public sealed class MockDebateEvaluationResult
    {
        public bool IsValid;
        public string FeedbackText = string.Empty;
        public string RawJson = string.Empty;
        public string DebugInfo = string.Empty;
    }

    public sealed class MockDebateEvaluationGenerator
    {
        private readonly string _model;
        private readonly int _timeoutSeconds;
        private readonly string _baseUrl;
        private readonly string _apiKeyOverride;

        public MockDebateEvaluationGenerator(string model, float timeoutSeconds, string baseUrl, string apiKeyOverride)
        {
            _model = string.IsNullOrWhiteSpace(model) ? "gpt-4o-mini" : model.Trim();
            _timeoutSeconds = Mathf.Max(3, Mathf.RoundToInt(timeoutSeconds));
            _baseUrl = baseUrl?.Trim() ?? string.Empty;
            _apiKeyOverride = apiKeyOverride?.Trim() ?? string.Empty;
        }

        public IEnumerator Evaluate(MockDebateEvaluationRequest requestData, Action<MockDebateEvaluationResult> onComplete)
        {
            MockDebateEvaluationRequest safeRequest = requestData ?? new MockDebateEvaluationRequest();
            string apiKey = DebateCommandParser.ResolveApiKey(_apiKeyOverride);
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                onComplete?.Invoke(new MockDebateEvaluationResult { DebugInfo = "API key missing: Mock Debate GPT was not called." });
                yield break;
            }

            string endpoint = DebateCommandParser.ResolveChatCompletionsEndpoint(DebateCommandParser.ResolveBaseUrl(_baseUrl));
            byte[] body = Encoding.UTF8.GetBytes(BuildOpenAIRequestJson(safeRequest).ToString(Formatting.None));
            const int maxAttempts = 2;
            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                using UnityWebRequest request = new(endpoint, UnityWebRequest.kHttpVerbPOST)
                {
                    uploadHandler = new UploadHandlerRaw(body),
                    downloadHandler = new DownloadHandlerBuffer(),
                    timeout = _timeoutSeconds
                };
                request.SetRequestHeader("Authorization", "Bearer " + apiKey.Trim());
                request.SetRequestHeader("Content-Type", "application/json");
                yield return request.SendWebRequest();

                if (request.result == UnityWebRequest.Result.Success)
                {
                    MockDebateEvaluationResult parsed = ParseResponse(request.downloadHandler.text);
                    parsed.DebugInfo += $" Request completed on attempt {attempt} of {maxAttempts}.";
                    onComplete?.Invoke(parsed);
                    yield break;
                }

                bool retryable = IsRetryableFailure(request);
                if (!retryable || attempt == maxAttempts)
                {
                    onComplete?.Invoke(new MockDebateEvaluationResult
                    {
                        RawJson = request.downloadHandler?.text ?? string.Empty,
                        DebugInfo =
                            $"Mock Debate GPT request failed after {attempt} attempt(s). " +
                            $"http_status={request.responseCode}; error={request.error}; " +
                            $"timeout_seconds={_timeoutSeconds}"
                    });
                    yield break;
                }

                yield return new WaitForSecondsRealtime(1f);
            }
        }

        private static bool IsRetryableFailure(UnityWebRequest request)
        {
            if (request == null || request.result == UnityWebRequest.Result.ConnectionError)
            {
                return true;
            }

            long status = request.responseCode;
            return status == 408 || status == 429 || status >= 500;
        }

        public static string BuildPrompt(MockDebateEvaluationRequest request)
        {
            MockDebateEvaluationRequest safeRequest = request ?? new MockDebateEvaluationRequest();
            StringBuilder methods = new();
            foreach (CreeiStage stage in Enum.GetValues(typeof(CreeiStage)))
            {
                methods.Append(stage).Append(": ").Append(CreeiStageGuidance.GetMethod(stage)).Append('\n');
            }

            return
                "You are Anna, evaluating an L2 English learner's complete Mock Debate speech.\n" +
                "Write all feedback in English only.\n\n" +
                "Evaluate the speech using these CREEI methods:\n" + methods + "\n" +
                "Analyze the complete speech after the learner has finished. Do not decide whether the learner is allowed to finish or pass the stage.\n" +
                "In feedback_text, write a concise final Coach summary and actionable advice covering the complete CREEI structure. " +
                "Mention what worked across Claim, Reason, Evidence, Explanation, and Impact, identify the most important weak or missing connection, and give one priority for the next debate.\n" +
                "Use four to six clear, supportive sentences.\n" +
                "Return one JSON object only with exactly one field named feedback_text.\n\n" +
                "Context:\n" +
                "topic: " + safeRequest.Topic + "\n" +
                "learner_stance: " + safeRequest.LearnerStance + "\n" +
                "speech_duration_seconds: " + safeRequest.DurationSeconds.ToString("0.0") + "\n" +
                "target_duration_seconds: " + safeRequest.TargetDurationSeconds.ToString("0.0") + "\n\n" +
                "mock_debate_transcript:\n" + safeRequest.Transcript;
        }

        private JObject BuildOpenAIRequestJson(MockDebateEvaluationRequest request)
        {
            return new JObject
            {
                ["model"] = _model,
                ["messages"] = new JArray
                {
                    new JObject
                    {
                        ["role"] = "system",
                        ["content"] = "Return strict JSON for a CREEI Mock Debate evaluation. Write feedback_text in English only."
                    },
                    new JObject { ["role"] = "user", ["content"] = BuildPrompt(request) }
                },
                ["response_format"] = new JObject { ["type"] = "json_object" },
                ["temperature"] = 0.3f,
                ["max_tokens"] = 500
            };
        }

        private static MockDebateEvaluationResult ParseResponse(string responseJson)
        {
            try
            {
                JObject response = JObject.Parse(responseJson);
                string content = response["choices"]?[0]?["message"]?["content"]?.ToString();
                if (string.IsNullOrWhiteSpace(content))
                {
                    return new MockDebateEvaluationResult
                    {
                        RawJson = responseJson ?? string.Empty,
                        DebugInfo = "Mock Debate GPT response did not contain choices[0].message.content."
                    };
                }

                JObject payload = JObject.Parse(StripCodeFence(content));
                string feedback = payload["feedback_text"]?.ToString()?.Trim() ?? string.Empty;
                return new MockDebateEvaluationResult
                {
                    IsValid = !string.IsNullOrWhiteSpace(feedback),
                    FeedbackText = feedback,
                    RawJson = content,
                    DebugInfo = string.IsNullOrWhiteSpace(feedback)
                        ? "Mock Debate GPT returned JSON, but feedback_text was empty."
                        : "Mock Debate GPT returned a valid CREEI evaluation."
                };
            }
            catch (Exception ex)
            {
                return new MockDebateEvaluationResult
                {
                    RawJson = responseJson ?? string.Empty,
                    DebugInfo = "Mock Debate GPT response parse failed: " + ex.Message
                };
            }
        }

        private static string StripCodeFence(string value)
        {
            string cleaned = value?.Trim() ?? string.Empty;
            if (!cleaned.StartsWith("```", StringComparison.Ordinal))
            {
                return cleaned;
            }

            int firstLineEnd = cleaned.IndexOf('\n');
            int lastFence = cleaned.LastIndexOf("```", StringComparison.Ordinal);
            return firstLineEnd >= 0 && lastFence > firstLineEnd
                ? cleaned.Substring(firstLineEnd + 1, lastFence - firstLineEnd - 1).Trim()
                : cleaned;
        }
    }
}
