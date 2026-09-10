using System;
using System.Collections;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;

namespace Game.Debate
{
    public sealed class CoachStageEvaluator : ICoachStageEvaluator
    {
        private const string DefaultModel = "gpt-4o-mini";
        private readonly string _model;
        private readonly int _timeoutSeconds;
        private readonly string _baseUrl;
        private readonly string _apiKeyOverride;
        private UnityWebRequest _activeRequest;
        private int _requestVersion;

        public CoachStageEvaluator(
            string model = DefaultModel,
            float timeoutSeconds = 30f,
            string baseUrl = "",
            string apiKeyOverride = "")
        {
            _model = string.IsNullOrWhiteSpace(model) ? DefaultModel : model.Trim();
            _timeoutSeconds = Mathf.Max(3, Mathf.RoundToInt(timeoutSeconds));
            _baseUrl = baseUrl?.Trim() ?? string.Empty;
            _apiKeyOverride = apiKeyOverride?.Trim() ?? string.Empty;
        }

        public static string BuildPrompt(CoachStageEvaluationContext context)
        {
            context ??= new CoachStageEvaluationContext();
            return
                "You are a condition-blind evaluator of one confirmed CREEI stage response from an L2 English learner.\n" +
                "Evaluate only the frozen criterion. Do not provide learner-facing feedback and do not infer an experimental mode.\n" +
                "Frozen criterion: " + GuidedCreeiRubric.GetCriterion(context.StageKind) + "\n" +
                "Return criterion_met, confidence, evidence_span, issue_code, and one next_action.\n\n" +
                "stage: " + context.StageKind + "\n" +
                "topic: " + context.Topic + "\n" +
                "learner_stance: " + context.LearnerStance + "\n" +
                "attempt_index: " + context.AttemptIndex + "\n" +
                "revision_index: " + context.RevisionIndex + "\n" +
                "previous_confirmed_stages:\n" + context.PreviousConfirmedStages + "\n\n" +
                "confirmed_transcript:\n" + context.ConfirmedTranscript;
        }

        public static JObject BuildStructuredOutputSchema()
        {
            return new JObject
            {
                ["type"] = "object",
                ["additionalProperties"] = false,
                ["properties"] = new JObject
                {
                    ["criterion_met"] = new JObject { ["type"] = "boolean" },
                    ["confidence"] = new JObject { ["type"] = "number", ["minimum"] = 0, ["maximum"] = 1 },
                    ["evidence_span"] = new JObject { ["type"] = "string" },
                    ["issue_code"] = new JObject { ["type"] = "string" },
                    ["next_action"] = new JObject { ["type"] = "string" }
                },
                ["required"] = new JArray(
                    "criterion_met", "confidence", "evidence_span", "issue_code", "next_action")
            };
        }

        public IEnumerator Evaluate(
            CoachStageEvaluationContext context,
            Action<CoachStageEvaluationResult> onComplete)
        {
            int version = ++_requestVersion;
            string apiKey = DebateCommandParser.ResolveApiKey(_apiKeyOverride);
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                onComplete?.Invoke(Failure("Stage evaluator API key is missing."));
                yield break;
            }

            CoachStageEvaluationResult result = null;
            for (int attempt = 0; attempt < 2; attempt++)
            {
                yield return RequestEvaluation(context, apiKey, value => result = value);
                if (version != _requestVersion) yield break;
                if (result != null && result.Success && result.Confidence >= 0.65f) break;
                if (attempt == 0) yield return new WaitForSecondsRealtime(0.5f);
            }

            if (version == _requestVersion)
            {
                onComplete?.Invoke(RequireMinimumConfidence(
                    result ?? Failure("Stage evaluation ended without a result.")));
            }
        }

        public static CoachStageEvaluationResult RequireMinimumConfidence(
            CoachStageEvaluationResult result)
        {
            if (result == null) return Failure("Stage evaluation ended without a result.");
            if (result.Success && result.Confidence < 0.65f)
            {
                return Failure(
                    "Stage evaluation confidence remained below 0.65 after the automatic retry.");
            }
            return result;
        }

        public void Cancel()
        {
            _requestVersion++;
            _activeRequest?.Abort();
            _activeRequest?.Dispose();
            _activeRequest = null;
        }

        public static CoachStageEvaluationResult ParseStructuredResult(string jsonText)
        {
            try
            {
                JObject json = JObject.Parse(jsonText ?? string.Empty);
                return new CoachStageEvaluationResult
                {
                    Success = true,
                    CriterionMet = (bool?)json["criterion_met"] ?? false,
                    Confidence = Mathf.Clamp01((float?)json["confidence"] ?? 0f),
                    EvidenceSpan = (string)json["evidence_span"] ?? string.Empty,
                    IssueCode = (string)json["issue_code"] ?? string.Empty,
                    NextAction = (string)json["next_action"] ?? string.Empty,
                    RawJson = json.ToString(Formatting.None),
                    RubricVersion = GuidedCreeiRubric.Version
                };
            }
            catch (Exception exception)
            {
                return Failure("Stage evaluation response was invalid: " + exception.Message);
            }
        }

        private IEnumerator RequestEvaluation(
            CoachStageEvaluationContext context,
            string apiKey,
            Action<CoachStageEvaluationResult> onComplete)
        {
            string endpoint = DebateCommandParser.ResolveChatCompletionsEndpoint(
                DebateCommandParser.ResolveBaseUrl(_baseUrl));
            JObject body = new()
            {
                ["model"] = _model,
                ["temperature"] = 0,
                ["messages"] = new JArray(new JObject
                {
                    ["role"] = "user",
                    ["content"] = BuildPrompt(context)
                }),
                ["response_format"] = new JObject
                {
                    ["type"] = "json_schema",
                    ["json_schema"] = new JObject
                    {
                        ["name"] = "guided_creei_stage_evaluation",
                        ["strict"] = true,
                        ["schema"] = BuildStructuredOutputSchema()
                    }
                }
            };

            using UnityWebRequest request = new(endpoint, UnityWebRequest.kHttpVerbPOST)
            {
                uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body.ToString(Formatting.None))),
                downloadHandler = new DownloadHandlerBuffer(),
                timeout = _timeoutSeconds
            };
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("Authorization", "Bearer " + apiKey);
            _activeRequest = request;
            yield return request.SendWebRequest();
            if (ReferenceEquals(_activeRequest, request)) _activeRequest = null;

            if (request.result != UnityWebRequest.Result.Success)
            {
                onComplete?.Invoke(Failure(
                    $"Stage evaluation request failed ({request.responseCode}): {request.error}"));
                yield break;
            }

            try
            {
                JObject envelope = JObject.Parse(request.downloadHandler.text);
                string content = (string)envelope["choices"]?[0]?["message"]?["content"];
                onComplete?.Invoke(ParseStructuredResult(content));
            }
            catch (Exception exception)
            {
                onComplete?.Invoke(Failure("Stage evaluation envelope was invalid: " + exception.Message));
            }
        }

        private static CoachStageEvaluationResult Failure(string error)
        {
            return new CoachStageEvaluationResult
            {
                Success = false,
                Error = error ?? "Stage evaluation failed.",
                RubricVersion = GuidedCreeiRubric.Version
            };
        }
    }
}
