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
    public sealed class OpponentChallengeRequest
    {
        public string Topic = string.Empty;
        public string LearnerStance = string.Empty;
        public string OpponentStance = string.Empty;
        public string LearnerStatement = string.Empty;
        public string PreviousRevision = string.Empty;
        public CreeiArgumentSnapshot CurrentCreeiSnapshot;
        public CreeiComponent ConfirmedFocus = CreeiComponent.Claim;
        public int CycleIndex = 1;
        public OpponentChallengeDifficulty Difficulty = OpponentChallengeDifficulty.Standard;
    }

    [Serializable]
    public sealed class OpponentChallengeResult
    {
        public bool Success;
        public string SpeechText = string.Empty;
        public string AttackFocus = string.Empty;
        public string RawJson = string.Empty;
        public string GenerationVersion = string.Empty;
        public string Error = string.Empty;
    }

    public sealed class OpponentChallengeGenerator
    {
        private readonly string _model;
        private readonly int _timeoutSeconds;
        private readonly string _baseUrl;
        private readonly string _apiKeyOverride;
        private UnityWebRequest _activeRequest;
        private int _requestVersion;

        public OpponentChallengeGenerator(
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

        public IEnumerator Generate(
            OpponentChallengeRequest requestData,
            Action<OpponentChallengeResult> onComplete)
        {
            int requestVersion = ++_requestVersion;
            OpponentChallengeRequest safeRequest = requestData ?? new OpponentChallengeRequest();
            string apiKey = DebateCommandParser.ResolveApiKey(_apiKeyOverride);
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                onComplete?.Invoke(Failure("Challenge API key is missing."));
                yield break;
            }

            string endpoint = DebateCommandParser.ResolveChatCompletionsEndpoint(
                DebateCommandParser.ResolveBaseUrl(_baseUrl));
            JObject body = new()
            {
                ["model"] = _model,
                ["messages"] = new JArray
                {
                    new JObject
                    {
                        ["role"] = "system",
                        ["content"] = "Return strict JSON with exactly speech_text and attack_focus."
                    },
                    new JObject
                    {
                        ["role"] = "user",
                        ["content"] = BuildPrompt(safeRequest)
                    }
                },
                ["response_format"] = new JObject { ["type"] = "json_object" }
            };
            byte[] bytes = Encoding.UTF8.GetBytes(body.ToString(Formatting.None));
            using UnityWebRequest request = new(endpoint, UnityWebRequest.kHttpVerbPOST)
            {
                uploadHandler = new UploadHandlerRaw(bytes),
                downloadHandler = new DownloadHandlerBuffer(),
                timeout = _timeoutSeconds
            };
            _activeRequest = request;
            request.SetRequestHeader("Authorization", "Bearer " + apiKey.Trim());
            request.SetRequestHeader("Content-Type", "application/json");
            yield return request.SendWebRequest();
            if (requestVersion != _requestVersion) yield break;
            _activeRequest = null;
            if (request.result != UnityWebRequest.Result.Success)
            {
                onComplete?.Invoke(Failure(
                    $"Challenge generation failed. http_status={request.responseCode}; error={request.error}",
                    request.downloadHandler?.text));
                yield break;
            }

            onComplete?.Invoke(ParseResponse(request.downloadHandler.text));
        }

        public void Cancel()
        {
            _requestVersion++;
            if (_activeRequest == null) return;
            _activeRequest.Abort();
            _activeRequest.Dispose();
            _activeRequest = null;
        }

        public static string BuildPrompt(OpponentChallengeRequest request)
        {
            request ??= new OpponentChallengeRequest();
            string difficulty = request.Difficulty == OpponentChallengeDifficulty.Hard
                ? "Hard: challenge the strongest premise with one plausible counterexample or trade-off. Use 20 to 35 words."
                : "Standard: identify one vulnerable point and ask one direct challenge question. Use 20 to 35 words.";
            string learnerArgument = request.CurrentCreeiSnapshot == null
                ? "learner_statement_to_challenge:\n" + request.LearnerStatement
                : BuildStructuredArgument(request.CurrentCreeiSnapshot);
            return
                "You are Leo, a concise debate opponent for a CEFR B1-B2 English learner.\n" +
                "Generate one targeted attack that the learner must answer independently before any coaching.\n" +
                "Do not infer or mention the experimental condition.\n" +
                "Do not coach the learner, provide a model answer, mention CREEI, or use insulting language.\n" +
                "Use one or two short English sentences and end with one direct challenge question.\n" +
                difficulty + "\n" +
                "Attack only the confirmed component below; do not select a different focus.\n" +
                "Return one JSON object only: {\"speech_text\":\"...\",\"attack_focus\":\"...\"}.\n\n" +
                "topic: " + request.Topic + "\n" +
                "learner_stance: " + request.LearnerStance + "\n" +
                "opponent_stance: " + request.OpponentStance + "\n" +
                "challenge_cycle: " + Mathf.Clamp(request.CycleIndex, 1, 2) + "\n" +
                "primary_challenge_focus: " + request.ConfirmedFocus + "\n" +
                learnerArgument + "\n\n" +
                "previous_revision_if_any:\n" + request.PreviousRevision;
        }

        private static OpponentChallengeResult ParseResponse(string responseJson)
        {
            try
            {
                JObject response = JObject.Parse(responseJson ?? string.Empty);
                string content = response["choices"]?[0]?["message"]?["content"]?.ToString();
                if (string.IsNullOrWhiteSpace(content))
                    return Failure("Challenge response did not contain choices[0].message.content.", responseJson);
                string cleaned = StripFence(content);
                JObject payload = JObject.Parse(cleaned);
                string speech = payload["speech_text"]?.ToString()?.Trim() ?? string.Empty;
                string focus = payload["attack_focus"]?.ToString()?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(speech) ||
                    !CoachFocusCatalog.TryNormalize(focus, out string normalizedFocus))
                    return Failure("Challenge JSON contained an empty speech or invalid attack focus.", content);
                return new OpponentChallengeResult
                {
                    Success = true,
                    SpeechText = speech,
                    AttackFocus = normalizedFocus,
                    RawJson = content,
                    GenerationVersion = "opponent-challenge-v2"
                };
            }
            catch (Exception exception)
            {
                return Failure("Challenge response parse failed: " + exception.Message, responseJson);
            }
        }

        private static string StripFence(string content)
        {
            string cleaned = content.Trim();
            if (!cleaned.StartsWith("```", StringComparison.Ordinal)) return cleaned;
            int firstLineEnd = cleaned.IndexOf('\n');
            int lastFence = cleaned.LastIndexOf("```", StringComparison.Ordinal);
            return firstLineEnd >= 0 && lastFence > firstLineEnd
                ? cleaned.Substring(firstLineEnd + 1, lastFence - firstLineEnd - 1).Trim()
                : cleaned;
        }

        private static string BuildStructuredArgument(CreeiArgumentSnapshot snapshot)
        {
            return "current_creei_argument:\n" +
                   "claim: " + snapshot.Claim + "\n" +
                   "reason: " + snapshot.Reason + "\n" +
                   "evidence: " + snapshot.Evidence + "\n" +
                   "explanation: " + snapshot.Explanation + "\n" +
                   "impact: " + snapshot.Impact;
        }

        private static OpponentChallengeResult Failure(string error, string rawJson = "") => new()
        {
            Success = false,
            Error = error ?? "Challenge generation failed.",
            RawJson = rawJson ?? string.Empty
        };
    }
}
