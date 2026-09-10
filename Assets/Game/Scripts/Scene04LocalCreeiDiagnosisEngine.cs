using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Game.Debate
{
    public sealed class Scene04LocalCreeiDiagnosisEngine : ICreeiArgumentDiagnosisEngine
    {
        public const string ModelVersion = "coach-diagnosis-local-v3";

        public IEnumerator Diagnose(
            CreeiArgumentDiagnosisRequest request,
            Action<CreeiArgumentDiagnosisResult> onComplete)
        {
            onComplete?.Invoke(Evaluate(request));
            yield break;
        }

        public void Cancel() { }

        public static CreeiArgumentDiagnosisResult Evaluate(
            CreeiArgumentDiagnosisRequest request)
        {
            if (request?.CurrentSnapshot == null)
                return new CreeiArgumentDiagnosisResult
                {
                    Success = false,
                    Error = "A committed CREEI snapshot is required for local diagnosis.",
                    ModelVersion = ModelVersion
                };

            Dictionary<CreeiComponent, string> text = Enum
                .GetValues(typeof(CreeiComponent))
                .Cast<CreeiComponent>()
                .ToDictionary(
                    component => component,
                    component => EnglishLlmInputSanitizer.Sanitize(
                        request.CurrentSnapshot.GetText(component)));

            List<CreeiComponentDiagnosis> diagnoses = new();
            diagnoses.Add(EvaluateClaim(text[CreeiComponent.Claim], request.LearnerSide));
            diagnoses.Add(EvaluateReason(
                text[CreeiComponent.Reason], text[CreeiComponent.Claim]));
            diagnoses.Add(EvaluateEvidence(
                text[CreeiComponent.Evidence], text[CreeiComponent.Claim],
                text[CreeiComponent.Reason]));
            diagnoses.Add(EvaluateExplanation(
                text[CreeiComponent.Explanation], text[CreeiComponent.Reason],
                text[CreeiComponent.Evidence]));
            diagnoses.Add(EvaluateImpact(
                text[CreeiComponent.Impact], text[CreeiComponent.Claim],
                text[CreeiComponent.Explanation]));

            CreeiComponent[] ranked = diagnoses
                .Where(item => !item.CriterionMet)
                .OrderByDescending(item => item.Severity)
                .ThenBy(item => (int)item.Component)
                .Select(item => item.Component)
                .Take(3)
                .ToArray();

            return new CreeiArgumentDiagnosisResult
            {
                Success = true,
                Components = diagnoses.ToArray(),
                RankedIssues = ranked,
                PrimaryIssue = ranked.Length > 0 ? ranked[0] : null,
                ModelVersion = ModelVersion
            };
        }

        private static CreeiComponentDiagnosis EvaluateClaim(string claim, string learnerSide)
        {
            if (!HasEnglish(claim)) return Missing(CreeiComponent.Claim);
            if (ConflictsWithSide(claim, learnerSide))
                return Weak(CreeiComponent.Claim, 3, "CLAIM_STANCE_CONFLICT",
                    claim, "Align the claim with the assigned learner position.");
            if (WordCount(claim) < 4)
                return Weak(CreeiComponent.Claim, 2, "CLAIM_UNDERDEVELOPED",
                    claim, "State one complete and unambiguous position.");
            return Met(CreeiComponent.Claim, claim);
        }

        private static CreeiComponentDiagnosis EvaluateReason(string reason, string claim)
        {
            if (!HasEnglish(reason)) return Missing(CreeiComponent.Reason);
            if (IsNearDuplicate(reason, claim))
                return Weak(CreeiComponent.Reason, 3, "REASON_REPEATS_CLAIM",
                    reason, "Give a distinct reason that explains why the claim is true.");
            if (WordCount(reason) < 6 && !ContainsAny(reason, "because", "since", "reason"))
                return Weak(CreeiComponent.Reason, 2, "REASON_UNDERDEVELOPED",
                    reason, "Develop a causal reason rather than restating the position.");
            return Met(CreeiComponent.Reason, reason);
        }

        private static CreeiComponentDiagnosis EvaluateEvidence(
            string evidence, string claim, string reason)
        {
            if (!HasEnglish(evidence)) return Missing(CreeiComponent.Evidence);
            if (IsNearDuplicate(evidence, claim) || IsNearDuplicate(evidence, reason))
                return Weak(CreeiComponent.Evidence, 3, "EVIDENCE_REPEATS_PRIOR_POINT",
                    evidence, "Add a concrete example, observation, or credible source.");
            if (!ContainsAny(evidence, "for example", "for instance", "study", "research",
                    "survey", "data", "such as", "when", "in class") && WordCount(evidence) < 10)
                return Weak(CreeiComponent.Evidence, 2, "EVIDENCE_TOO_GENERAL",
                    evidence, "Add one specific example or verifiable piece of support.");
            return Met(CreeiComponent.Evidence, evidence);
        }

        private static CreeiComponentDiagnosis EvaluateExplanation(
            string explanation, string reason, string evidence)
        {
            if (!HasEnglish(explanation)) return Missing(CreeiComponent.Explanation);
            if (IsNearDuplicate(explanation, reason) || IsNearDuplicate(explanation, evidence))
                return Weak(CreeiComponent.Explanation, 3, "EXPLANATION_REPEATS_SUPPORT",
                    explanation, "Explain how the evidence proves the reason and claim.");
            if (!ContainsAny(explanation, "this means", "this shows", "therefore", "because",
                    "so", "which", "demonstrates", "supports") && WordCount(explanation) < 10)
                return Weak(CreeiComponent.Explanation, 2, "EXPLANATION_LINK_UNCLEAR",
                    explanation, "Make the connection from evidence to reason and claim explicit.");
            return Met(CreeiComponent.Explanation, explanation);
        }

        private static CreeiComponentDiagnosis EvaluateImpact(
            string impact, string claim, string explanation)
        {
            if (!HasEnglish(impact)) return Missing(CreeiComponent.Impact);
            if (IsNearDuplicate(impact, claim) || IsNearDuplicate(impact, explanation))
                return Weak(CreeiComponent.Impact, 3, "IMPACT_REPEATS_PRIOR_POINT",
                    impact, "State the final consequence and why it matters.");
            if (!ContainsAny(impact, "as a result", "therefore", "leads", "helps", "matters",
                    "important", "impact", "future", "confidence", "consequence") &&
                WordCount(impact) < 8)
                return Weak(CreeiComponent.Impact, 2, "IMPACT_UNDERDEVELOPED",
                    impact, "State who is affected and why the consequence matters.");
            return Met(CreeiComponent.Impact, impact);
        }

        private static CreeiComponentDiagnosis Missing(CreeiComponent component) =>
            Weak(component, 3, component.ToString().ToUpperInvariant() + "_MISSING",
                string.Empty, "Add a complete English " + component + " component.");

        private static CreeiComponentDiagnosis Weak(
            CreeiComponent component,
            int severity,
            string issueCode,
            string evidence,
            string nextAction) => new()
        {
            Component = component,
            CriterionMet = false,
            Severity = severity,
            Confidence = 0.9f,
            IssueCode = issueCode,
            EvidenceSpan = evidence ?? string.Empty,
            RecommendedNextAction = nextAction ?? string.Empty
        };

        private static CreeiComponentDiagnosis Met(CreeiComponent component, string evidence) =>
            new()
            {
                Component = component,
                CriterionMet = true,
                Severity = 0,
                Confidence = 0.8f,
                IssueCode = component.ToString().ToUpperInvariant() + "_ADEQUATE",
                EvidenceSpan = evidence ?? string.Empty,
                RecommendedNextAction = string.Empty
            };

        private static bool ConflictsWithSide(string claim, string learnerSide)
        {
            string cleanClaim = claim.ToLowerInvariant();
            string cleanSide = EnglishLlmInputSanitizer.Sanitize(learnerSide).ToLowerInvariant();
            bool sideInteraction = cleanSide.Contains("interaction") || cleanSide.Contains("with others");
            bool sideIndividual = cleanSide.Contains("individual practice");
            bool claimInteraction = cleanClaim.Contains("interaction") || cleanClaim.Contains("with others");
            bool claimIndividual = cleanClaim.Contains("individual practice");
            return sideInteraction && claimIndividual && !claimInteraction ||
                   sideIndividual && claimInteraction && !claimIndividual;
        }

        private static bool IsNearDuplicate(string left, string right)
        {
            HashSet<string> a = Tokens(left);
            HashSet<string> b = Tokens(right);
            if (a.Count == 0 || b.Count == 0) return false;
            int intersection = a.Count(token => b.Contains(token));
            int union = a.Union(b).Count();
            return union > 0 && (float)intersection / union >= 0.82f;
        }

        private static HashSet<string> Tokens(string value) => new(
            (value ?? string.Empty).ToLowerInvariant()
            .Split(new[] { ' ', '.', ',', '?', '!', ':', ';', '\'', '"', '-', '(', ')', '/' },
                StringSplitOptions.RemoveEmptyEntries));

        private static int WordCount(string value) => Tokens(value).Count;
        private static bool HasEnglish(string value) =>
            EnglishLlmInputSanitizer.ContainsEnglishLetter(value);

        private static bool ContainsAny(string value, params string[] markers)
        {
            string lower = value?.ToLowerInvariant() ?? string.Empty;
            return markers.Any(lower.Contains);
        }
    }
}
