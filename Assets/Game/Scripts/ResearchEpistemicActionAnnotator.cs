using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Game.Debate
{
    public static class ResearchEpistemicActionCatalog
    {
        public const string SchemaVersion = "epistemic-labour-v1";
        public const string ProblemIdentification = "problem_identification";
        public const string StrategyGeneration = "strategy_generation";
        public const string Evaluation = "evaluation";
        public const string Decision = "decision";
        public const string Revision = "revision";
        public const string Termination = "termination";

        public static readonly string[] Values =
        {
            ProblemIdentification,
            StrategyGeneration,
            Evaluation,
            Decision,
            Revision,
            Termination
        };
    }

    public static class ResearchEpistemicActionAnnotator
    {
        private static readonly HashSet<string> ProblemIdentificationEvents = new()
        {
            "diagnosiscompleted",
            "criticalfeedbackpresented",
            "criticalfeedbackupdated"
        };

        private static readonly HashSet<string> StrategyGenerationEvents = new()
        {
            "coachadvicepresented",
            "coachfeedbackshown",
            "coachfeedbackpresented",
            "feedbackshown",
            "coachexamplepresented",
            "coachadditionalsuggestionpresented",
            "aicreeimodelpresented"
        };

        private static readonly HashSet<string> EvaluationEvents = new()
        {
            "microrevisionevaluated",
            "revisionevaluationcompleted",
            "silentrevisiondiagnosiscompleted"
        };

        private static readonly HashSet<string> DecisionEvents = new()
        {
            "criticalfeedbackaccepted",
            "criticalfeedbackchangerequested",
            "learnerfollowupsubmitted",
            "requestcoach",
            "learnercontrolaction",
            "suggestionaccepted",
            "suggestionchangerequested",
            "suggestiondeclined",
            "coachexamplerequested",
            "coachmoresuggestionsrequested",
            "coachautostarted",
            "coachopportunitypresented"
        };

        private static readonly HashSet<string> RevisionEvents = new()
        {
            "microrevisionconfirmed",
            "revisionspeechconfirmed",
            "independentrevisionsubmitted",
            "coachedrevisionsubmitted"
        };

        private static readonly HashSet<string> TerminationEvents = new()
        {
            "coachskipped",
            "coachepisodecompleted",
            "episodecompleted",
            "practiceonefinishedearly"
        };

        public static void Annotate(ResearchLogEvent row)
        {
            if (row == null) throw new ArgumentNullException(nameof(row));
            Clear(row);
            string eventKey = Key(row.EventType);
            if (eventKey.Length == 0) return;

            JObject payload = ParsePayload(row.PayloadJson);
            string action;
            string actor;
            string initiator;
            string decisionOwner;
            string outcome;

            if (eventKey == "diagnosiscompleted" && IsRevisionDiagnosis(payload))
            {
                action = ResearchEpistemicActionCatalog.Evaluation;
                actor = "coach";
                initiator = "coach";
                decisionOwner = "coach";
                outcome = "evaluated";
            }
            else if (ProblemIdentificationEvents.Contains(eventKey))
            {
                action = ResearchEpistemicActionCatalog.ProblemIdentification;
                actor = "coach";
                initiator = eventKey == "criticalfeedbackupdated" ? "learner" : "coach";
                decisionOwner = "coach";
                outcome = eventKey == "criticalfeedbackupdated" ? "updated" :
                    eventKey == "criticalfeedbackpresented" ? "presented" : "identified";
            }
            else if (StrategyGenerationEvents.Contains(eventKey))
            {
                action = ResearchEpistemicActionCatalog.StrategyGeneration;
                actor = "coach";
                initiator = IsLearnerInitiatedFeedback(payload) ? "learner" : "coach";
                decisionOwner = "coach";
                outcome = "presented";
            }
            else if (EvaluationEvents.Contains(eventKey))
            {
                action = ResearchEpistemicActionCatalog.Evaluation;
                actor = "coach";
                initiator = "coach";
                decisionOwner = "coach";
                outcome = "evaluated";
            }
            else if (DecisionEvents.Contains(eventKey))
            {
                action = ResearchEpistemicActionCatalog.Decision;
                bool coachDecision = eventKey is "coachautostarted" or "coachopportunitypresented";
                actor = coachDecision ? "coach" : "learner";
                initiator = actor;
                decisionOwner = actor;
                outcome = DecisionOutcome(eventKey);
            }
            else if (RevisionEvents.Contains(eventKey))
            {
                action = ResearchEpistemicActionCatalog.Revision;
                actor = "learner";
                initiator = "learner";
                decisionOwner = "learner";
                outcome = "submitted";
            }
            else if (TerminationEvents.Contains(eventKey))
            {
                action = ResearchEpistemicActionCatalog.Termination;
                string terminationOwner = NormalizeOwner(ReadPayload(payload,
                    "termination_owner", "TerminationOwner"));
                actor = terminationOwner.Length > 0
                    ? terminationOwner
                    : eventKey is "coachskipped" or "practiceonefinishedearly"
                        ? "learner"
                        : NormalizeOwner(row.Actor);
                if (actor.Length == 0) actor = "system";
                initiator = actor;
                decisionOwner = actor;
                outcome = eventKey == "coachskipped" ? "skipped" : "completed";
            }
            else
            {
                return;
            }

            row.EpistemicSchemaVersion = ResearchEpistemicActionCatalog.SchemaVersion;
            row.EpistemicAction = action;
            row.EpistemicActor = actor;
            row.EpistemicInitiator = initiator;
            row.EpistemicDecisionOwner = decisionOwner;
            row.EpistemicTarget = ReadTarget(payload, action);
            row.EpistemicOutcome = outcome;
        }

        private static void Clear(ResearchLogEvent row)
        {
            row.EpistemicSchemaVersion = string.Empty;
            row.EpistemicAction = string.Empty;
            row.EpistemicActor = string.Empty;
            row.EpistemicInitiator = string.Empty;
            row.EpistemicDecisionOwner = string.Empty;
            row.EpistemicTarget = string.Empty;
            row.EpistemicOutcome = string.Empty;
        }

        private static string ReadTarget(JObject payload, string action)
        {
            string target = ReadPayload(payload,
                "component", "focus", "confirmed_focus", "proposed_focus",
                "target_component", "active_component", "changed_components",
                "creei_missing_or_weak_components", "primary_issue");
            if (!string.IsNullOrWhiteSpace(target)) return NormalizeTarget(target);
            return action switch
            {
                ResearchEpistemicActionCatalog.Decision => "coach_support",
                ResearchEpistemicActionCatalog.Termination => "coach_episode",
                _ => "whole_argument"
            };
        }

        private static string DecisionOutcome(string eventKey)
        {
            if (eventKey.Contains("change")) return "change_requested";
            if (eventKey.Contains("declined")) return "declined";
            if (eventKey.Contains("accepted")) return "accepted";
            if (eventKey.Contains("submitted")) return "submitted";
            if (eventKey.Contains("requested") || eventKey == "requestcoach") return "requested";
            if (eventKey == "coachautostarted") return "auto_started";
            if (eventKey == "coachopportunitypresented") return "offered";
            return "selected";
        }

        private static bool IsRevisionDiagnosis(JObject payload)
        {
            string revisionKind = Key(ReadPayload(payload, "revision_kind", "RevisionKind", "stage"));
            return revisionKind is "independentrevision" or "coachedrevision" or
                "microrevision" or "revisionspeech";
        }

        private static string NormalizeTarget(string value)
        {
            string[] values = (value ?? string.Empty)
                .Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(TargetToken)
                .Where(token => token.Length > 0)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            return values.Length == 0 ? "whole_argument" : string.Join(";", values);
        }

        private static string TargetToken(string value)
        {
            string key = Key(value);
            if (key.Contains("claim")) return "claim";
            if (key.Contains("reason")) return "reason";
            if (key.Contains("evidence")) return "evidence";
            if (key.Contains("explanation")) return "explanation";
            if (key.Contains("impact")) return "impact";
            if (key is "overallstructure" or "wholeargument" or "argument")
                return "whole_argument";
            if (key is "coachsupport" or "coach") return "coach_support";
            if (key is "coachepisode" or "episode") return "coach_episode";
            return string.Empty;
        }

        private static bool IsLearnerInitiatedFeedback(JObject payload)
        {
            string purpose = Key(ReadPayload(payload, "feedback_purpose", "CoachFeedbackType"));
            return purpose is "learnersocratic" or "conversationalfollowup" or
                "example" or "additionalsuggestion";
        }

        private static JObject ParsePayload(string payloadJson)
        {
            if (string.IsNullOrWhiteSpace(payloadJson)) return new JObject();
            try
            {
                return JObject.Parse(payloadJson);
            }
            catch
            {
                return new JObject();
            }
        }

        private static string ReadPayload(JObject payload, params string[] names)
        {
            foreach (string name in names)
            {
                JToken token = payload.Properties()
                    .FirstOrDefault(property => string.Equals(
                        property.Name, name, StringComparison.OrdinalIgnoreCase))?.Value;
                if (token == null || token.Type == JTokenType.Null) continue;
                string value = token.Type == JTokenType.Array
                    ? string.Join(";", token.Values<string>())
                    : token.ToString();
                if (!string.IsNullOrWhiteSpace(value)) return value.Trim();
            }
            return string.Empty;
        }

        private static string NormalizeOwner(string value)
        {
            return Key(value) switch
            {
                "learner" => "learner",
                "coach" => "coach",
                "shared" => "shared",
                "systemsafety" => "system",
                "system" => "system",
                _ => string.Empty
            };
        }

        private static string Key(string value)
        {
            return new string((value ?? string.Empty)
                .Where(char.IsLetterOrDigit)
                .Select(char.ToLowerInvariant)
                .ToArray());
        }
    }
}
