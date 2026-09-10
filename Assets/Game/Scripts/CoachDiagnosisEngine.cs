using System;
using System.Collections;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;

namespace Game.Debate
{
    public interface ICoachDiagnosisEngine
    {
        IEnumerator Diagnose(CoachDiagnosisRequest request, Action<CoachDiagnosisResult> onComplete);
        void Cancel();
    }

    public sealed class CoachDiagnosisEngine : ICoachDiagnosisEngine
    {
        private const string DefaultModel = "gpt-4o-mini";
        private readonly string _model;
        private readonly int _timeoutSeconds;
        private readonly string _baseUrl;
        private readonly string _apiKeyOverride;
        private int _requestVersion;
        private UnityWebRequest _activeRequest;

        public CoachDiagnosisEngine(
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

        public static string BuildPrompt(CoachDiagnosisRequest request)
        {
            request ??= new CoachDiagnosisRequest();
            string currentArgument = BuildStructuredArgument(
                "current_creei_argument", request.CurrentCreeiSnapshot,
                request.PlayerUtteranceText);
            string previousArgument = BuildStructuredArgument(
                "previous_creei_argument", request.PreviousCreeiSnapshot,
                request.PreviousConfirmedAttempt);
            return
                "You analyze one completed debate turn from a CEFR B1-B2 English learner.\n" +
                "Diagnose the turn using CREEI and Ethos/Pathos/Logos.\n" +
                "Review all five CREEI components: Claim, Reason, Evidence, Explanation, and Impact.\n" +
                "Return exactly one component_diagnoses item for each of the five components. " +
                "Check connections: whether Evidence supports Reason and whether Explanation links Evidence to Reason and Claim.\n" +
                "Return creei_missing_or_weak_components as every component that is absent, underdeveloped, or connected unclearly. " +
                "Return creei_gap_summary as one concise English sentence describing the whole-argument structural gap without advice or a model answer.\n" +
                "Return structured labels only. Do not generate learner-facing feedback or a model answer.\n" +
                "Identify up to three priority issues that can be improved in a short coaching episode, and mark one as highest priority.\n" +
                "Assign diagnosis_severity from 0 to 3 and diagnosis_confidence from 0.00 to 1.00.\n" +
                "Return diagnosis_issue_code, strong_component, weak_component, dominant_strategy, " +
                "recommended_strategy, evidence_quality, reasoning_connection, diagnosis_severity, " +
                "diagnosis_confidence, recommended_focus, recommended_next_action, revision_improvement_status, " +
                "revision_improvement_summary, creei_missing_or_weak_components, and creei_gap_summary. " +
                "Use NotApplicable when there is no previous confirmed attempt.\n" +
                "Also return ranked_suggestions with at most three distinct valid issues, ordered by priority. " +
                "Each suggestion needs an id, rank, issue code, focus, problem description, and improvement goal. " +
                "Do not invent extra suggestions to fill the list.\n" +
                "Use only the learning content supplied below.\n\n" +
                "stage: " + request.Stage + "\n" +
                "topic_id: " + request.TopicId + "\n" +
                "practice_cycle_id: " + request.PracticeCycleId + "\n" +
                "turn_id: " + request.TurnId + "\n" +
                "topic: " + request.Topic + "\n" +
                "learner_side: " + request.LearnerSide + "\n" +
                "previous_confirmed_creei_stages:\n" + request.PreviousConfirmedStages + "\n" +
                previousArgument + "\n" +
                "opponent_last_turn: " + request.OpponentUtteranceText + "\n" +
                currentArgument + "\n" +
                "selected_strategy: " + request.SelectedStrategy;
        }

        public static JObject BuildStructuredOutputSchema()
        {
            return new JObject
            {
                ["type"] = "object",
                ["additionalProperties"] = false,
                ["properties"] = new JObject
                {
                    ["diagnosis_issue_code"] = new JObject { ["type"] = "string" },
                    ["strong_component"] = EnumString("Claim", "Reason", "Evidence", "Explanation", "Impact"),
                    ["weak_component"] = EnumString("Claim", "Reason", "Evidence", "Explanation", "Impact"),
                    ["dominant_strategy"] = EnumString("Logos", "Ethos", "Pathos", "Mixed", "Any"),
                    ["recommended_strategy"] = EnumString("Logos", "Ethos", "Pathos", "Mixed", "Any"),
                    ["evidence_quality"] = EnumString("missing", "general", "specific", "credible"),
                    ["reasoning_connection"] = EnumString("missing", "unclear", "clear"),
                    ["diagnosis_severity"] = new JObject { ["type"] = "integer", ["minimum"] = 0, ["maximum"] = 3 },
                    ["diagnosis_confidence"] = new JObject { ["type"] = "number", ["minimum"] = 0, ["maximum"] = 1 },
                    ["recommended_focus"] = EnumString(CoachFocusCatalog.Values),
                    ["recommended_next_action"] = new JObject { ["type"] = "string" },
                    ["revision_improvement_status"] = EnumString(
                        "NotApplicable", "Improved", "Unchanged", "Regressed"),
                    ["revision_improvement_summary"] = new JObject { ["type"] = "string" },
                    ["creei_missing_or_weak_components"] = new JObject
                    {
                        ["type"] = "array",
                        ["maxItems"] = 5,
                        ["items"] = EnumString("Claim", "Reason", "Evidence", "Explanation", "Impact")
                    },
                    ["creei_gap_summary"] = new JObject { ["type"] = "string" },
                    ["component_diagnoses"] = new JObject
                    {
                        ["type"] = "array",
                        ["minItems"] = 5,
                        ["maxItems"] = 5,
                        ["items"] = new JObject
                        {
                            ["type"] = "object",
                            ["additionalProperties"] = false,
                            ["properties"] = new JObject
                            {
                                ["component"] = EnumString(
                                    "Claim", "Reason", "Evidence", "Explanation", "Impact"),
                                ["criterion_met"] = new JObject { ["type"] = "boolean" },
                                ["severity"] = new JObject
                                    { ["type"] = "integer", ["minimum"] = 0, ["maximum"] = 3 },
                                ["confidence"] = new JObject
                                    { ["type"] = "number", ["minimum"] = 0, ["maximum"] = 1 },
                                ["issue_code"] = new JObject { ["type"] = "string" },
                                ["evidence_span"] = new JObject { ["type"] = "string" },
                                ["recommended_next_action"] = new JObject { ["type"] = "string" }
                            },
                            ["required"] = new JArray(
                                "component", "criterion_met", "severity", "confidence",
                                "issue_code", "evidence_span", "recommended_next_action")
                        }
                    },
                    ["ranked_suggestions"] = new JObject
                    {
                        ["type"] = "array",
                        ["maxItems"] = 3,
                        ["items"] = new JObject
                        {
                            ["type"] = "object",
                            ["additionalProperties"] = false,
                            ["properties"] = new JObject
                            {
                                ["suggestion_id"] = new JObject { ["type"] = "string" },
                                ["rank"] = new JObject { ["type"] = "integer", ["minimum"] = 1 },
                                ["issue_code"] = new JObject { ["type"] = "string" },
                                ["focus"] = EnumString(CoachFocusCatalog.Values),
                                ["problem_description"] = new JObject { ["type"] = "string" },
                                ["improvement_goal"] = new JObject { ["type"] = "string" }
                            },
                            ["required"] = new JArray(
                                "suggestion_id", "rank", "issue_code", "focus",
                                "problem_description", "improvement_goal")
                        }
                    }
                },
                ["required"] = new JArray(
                    "diagnosis_issue_code", "strong_component", "weak_component",
                    "dominant_strategy", "recommended_strategy", "evidence_quality",
                    "reasoning_connection", "diagnosis_severity", "diagnosis_confidence",
                    "recommended_focus", "recommended_next_action", "revision_improvement_status",
                    "revision_improvement_summary", "creei_missing_or_weak_components",
                    "creei_gap_summary", "component_diagnoses", "ranked_suggestions")
            };
        }

        public IEnumerator Diagnose(CoachDiagnosisRequest request, Action<CoachDiagnosisResult> onComplete)
        {
            int requestVersion = ++_requestVersion;
            string apiKey = DebateCommandParser.ResolveApiKey(_apiKeyOverride);
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                onComplete?.Invoke(Failure("Diagnosis API key is missing."));
                yield break;
            }

            CoachDiagnosisResult result = null;
            for (int attempt = 0; attempt < 2 && result == null; attempt++)
            {
                yield return RequestDiagnosis(request, apiKey, value => result = value);
                if (result != null && !result.Success && attempt == 0)
                {
                    result = null;
                    yield return new WaitForSecondsRealtime(0.5f);
                }
            }

            if (requestVersion != _requestVersion)
            {
                yield break;
            }

            onComplete?.Invoke(result ?? Failure("Diagnosis request ended without a result."));
        }

        public void Cancel()
        {
            _requestVersion++;
            _activeRequest?.Abort();
            _activeRequest?.Dispose();
            _activeRequest = null;
        }

        private IEnumerator RequestDiagnosis(
            CoachDiagnosisRequest request,
            string apiKey,
            Action<CoachDiagnosisResult> onComplete)
        {
            string endpoint = DebateCommandParser.ResolveChatCompletionsEndpoint(
                DebateCommandParser.ResolveBaseUrl(_baseUrl));
            JObject body = new()
            {
                ["model"] = _model,
                ["temperature"] = 0,
                ["messages"] = new JArray
                {
                    new JObject { ["role"] = "user", ["content"] = BuildPrompt(request) }
                },
                ["response_format"] = new JObject
                {
                    ["type"] = "json_schema",
                    ["json_schema"] = new JObject
                    {
                        ["name"] = "coach_diagnosis",
                        ["strict"] = true,
                        ["schema"] = BuildStructuredOutputSchema()
                    }
                }
            };

            byte[] bytes = System.Text.Encoding.UTF8.GetBytes(body.ToString(Formatting.None));
            using UnityWebRequest webRequest = new(endpoint, UnityWebRequest.kHttpVerbPOST)
            {
                uploadHandler = new UploadHandlerRaw(bytes),
                downloadHandler = new DownloadHandlerBuffer(),
                timeout = _timeoutSeconds
            };
            webRequest.SetRequestHeader("Content-Type", "application/json");
            webRequest.SetRequestHeader("Authorization", "Bearer " + apiKey);
            _activeRequest = webRequest;
            yield return webRequest.SendWebRequest();
            _activeRequest = null;

            if (webRequest.result != UnityWebRequest.Result.Success)
            {
                onComplete?.Invoke(Failure(
                    $"Diagnosis request failed ({webRequest.responseCode}): {webRequest.error}"));
                yield break;
            }

            onComplete?.Invoke(ParseResponse(webRequest.downloadHandler.text));
        }

        private static CoachDiagnosisResult ParseResponse(string response)
        {
            try
            {
                JObject envelope = JObject.Parse(response);
                string content = (string)envelope["choices"]?[0]?["message"]?["content"];
                return ParseStructuredResult(content);
            }
            catch (Exception exception)
            {
                return Failure("Diagnosis response was invalid: " + exception.Message);
            }
        }

        public static CoachDiagnosisResult ParseStructuredResult(string jsonText)
        {
            try
            {
                JObject json = JObject.Parse(jsonText ?? string.Empty);
                System.Collections.Generic.List<CoachSuggestion> suggestions = new();
                if (json["ranked_suggestions"] is JArray suggestionArray)
                {
                    foreach (JToken token in suggestionArray)
                    {
                        suggestions.Add(new CoachSuggestion
                        {
                            SuggestionId = (string)token["suggestion_id"] ?? string.Empty,
                            Rank = Mathf.Max(1, (int?)token["rank"] ?? suggestions.Count + 1),
                            IssueCode = (string)token["issue_code"] ?? string.Empty,
                            Focus = CoachFocusCatalog.Normalize((string)token["focus"]),
                            ProblemDescription = (string)token["problem_description"] ?? string.Empty,
                            ImprovementGoal = (string)token["improvement_goal"] ?? string.Empty
                        });
                    }
                }

                CoachSuggestion[] ranked = suggestions
                    .OrderBy(suggestion => suggestion.Rank)
                    .Take(3)
                    .ToArray();
                string weakComponent = (string)json["weak_component"] ?? string.Empty;
                string[] missingOrWeak = json["creei_missing_or_weak_components"] is JArray creeiArray
                    ? creeiArray.Values<string>()
                        .Where(IsCreeiComponent)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .Take(5)
                        .ToArray()
                    : Array.Empty<string>();
                if (missingOrWeak.Length == 0 && IsCreeiComponent(weakComponent))
                    missingOrWeak = new[] { weakComponent };
                string gapSummary = ((string)json["creei_gap_summary"] ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(gapSummary) && !string.IsNullOrWhiteSpace(weakComponent))
                    gapSummary = $"The {weakComponent} component needs development.";
                CreeiComponentDiagnosis[] componentDiagnoses = ParseComponentDiagnoses(
                    json["component_diagnoses"] as JArray);
                return new CoachDiagnosisResult
                {
                    Success = true,
                    DiagnosisIssueCode = (string)json["diagnosis_issue_code"] ?? string.Empty,
                    StrongComponent = (string)json["strong_component"] ?? string.Empty,
                    WeakComponent = weakComponent,
                    DominantStrategy = (string)json["dominant_strategy"] ?? string.Empty,
                    RecommendedStrategy = (string)json["recommended_strategy"] ?? string.Empty,
                    EvidenceQuality = (string)json["evidence_quality"] ?? string.Empty,
                    ReasoningConnection = (string)json["reasoning_connection"] ?? string.Empty,
                    Severity = Mathf.Clamp((int?)json["diagnosis_severity"] ?? 0, 0, 3),
                    Confidence = Mathf.Clamp01((float?)json["diagnosis_confidence"] ?? 0f),
                    RecommendedFocus = CoachFocusCatalog.Normalize((string)json["recommended_focus"]),
                    RecommendedNextAction = (string)json["recommended_next_action"] ?? string.Empty,
                    RevisionImprovementStatus = (string)json["revision_improvement_status"] ?? "NotApplicable",
                    RevisionImprovementSummary = (string)json["revision_improvement_summary"] ?? string.Empty,
                    CreeiMissingOrWeakComponents = missingOrWeak,
                    CreeiGapSummary = gapSummary,
                    ComponentDiagnoses = componentDiagnoses,
                    RankedSuggestions = ranked,
                    RawJson = json.ToString(Formatting.None),
                    ModelVersion = "coach-diagnosis-v2"
                };
            }
            catch (Exception exception)
            {
                return Failure("Diagnosis response was invalid: " + exception.Message);
            }
        }

        private static CoachDiagnosisResult Failure(string error)
        {
            return new CoachDiagnosisResult { Success = false, Error = error ?? "Diagnosis failed." };
        }

        private static JObject EnumString(params string[] values)
        {
            return new JObject { ["type"] = "string", ["enum"] = new JArray(values) };
        }

        private static bool IsCreeiComponent(string value)
        {
            return value is "Claim" or "Reason" or "Evidence" or "Explanation" or "Impact";
        }

        private static string BuildStructuredArgument(
            string label,
            CreeiArgumentSnapshot snapshot,
            string fallback)
        {
            if (snapshot == null)
                return label + "_unstructured:\n" + (fallback ?? string.Empty);
            return label + ":\n" +
                   "claim: " + snapshot.Claim + "\n" +
                   "reason: " + snapshot.Reason + "\n" +
                   "evidence: " + snapshot.Evidence + "\n" +
                   "explanation: " + snapshot.Explanation + "\n" +
                   "impact: " + snapshot.Impact;
        }

        private static CreeiComponentDiagnosis[] ParseComponentDiagnoses(JArray array)
        {
            if (array == null) return Array.Empty<CreeiComponentDiagnosis>();
            return array
                .Select(token =>
                {
                    Enum.TryParse((string)token["component"], true,
                        out CreeiComponent component);
                    return new CreeiComponentDiagnosis
                    {
                        Component = component,
                        CriterionMet = (bool?)token["criterion_met"] ?? false,
                        Severity = Mathf.Clamp((int?)token["severity"] ?? 0, 0, 3),
                        Confidence = Mathf.Clamp01((float?)token["confidence"] ?? 0f),
                        IssueCode = (string)token["issue_code"] ?? string.Empty,
                        EvidenceSpan = (string)token["evidence_span"] ?? string.Empty,
                        RecommendedNextAction =
                            (string)token["recommended_next_action"] ?? string.Empty
                    };
                })
                .GroupBy(item => item.Component)
                .Select(group => group.First())
                .OrderBy(item => (int)item.Component)
                .Take(5)
                .ToArray();
        }
    }
}
