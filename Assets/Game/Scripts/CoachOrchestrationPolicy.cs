using System;

namespace Game.Debate
{
    public sealed class CoachOrchestrationPolicy
    {
        private readonly CoachPolicyConfig _config;

        public CoachOrchestrationPolicy(CoachPolicyConfig config)
        {
            _config = config ?? CoachPolicyConfig.CreateDefault();
        }

        public CoachPolicyDecision Evaluate(CoachPolicyInput input)
        {
            input ??= new CoachPolicyInput();
            CoachPolicyDecision decision = CreateDecision(input.Mode);

            if (input.Mode == CoachOrchestrationMode.Disabled)
            {
                return End(decision, CoachEpisodeState.Inactive, CoachTerminationReason.NoEligibleIssue,
                    "CoachDisabledForStage", ControlOwner.SystemSafety);
            }

            if (input.ElapsedSeconds >= _config.MaxEpisodeSeconds &&
                input.EpisodeState is CoachEpisodeState.Active or
                    CoachEpisodeState.AwaitingLearnerAction or
                    CoachEpisodeState.Overridden)
            {
                return End(decision, CoachEpisodeState.TimedOut, CoachTerminationReason.TimeLimit,
                    "EpisodeTimeLimitReached", ControlOwner.SystemSafety);
            }

            if (input.LearnerAction == CoachLearnerAction.ExitCoaching)
            {
                return End(decision, CoachEpisodeState.Completed, CoachTerminationReason.SafetyOverride,
                    "LearnerSafetyExit", ControlOwner.SystemSafety);
            }

            if (input.EpisodeState == CoachEpisodeState.Diagnosing)
            {
                return EvaluateDiagnosis(input, decision);
            }

            if (input.LearnerAction == CoachLearnerAction.Decline)
            {
                return End(decision, CoachEpisodeState.Declined, CoachTerminationReason.LearnerDeclined,
                    "LearnerDeclinedInvitation", ControlOwner.Learner);
            }

            if (input.LearnerAction == CoachLearnerAction.ApplyNextCycle)
            {
                bool noEligibleIssue = input.Diagnosis == null || !input.Diagnosis.HasEligibleIssue;
                return End(decision, CoachEpisodeState.Completed,
                    noEligibleIssue ? CoachTerminationReason.NoEligibleIssue : CoachTerminationReason.LearnerEnded,
                    noEligibleIssue ? "LearnerContinuedAfterNeutralResult" : "LearnerAppliedFeedbackToNextCycle",
                    ControlOwner.Learner);
            }

            if (input.Mode == CoachOrchestrationMode.AiLed &&
                input.EpisodeState == CoachEpisodeState.AwaitingLearnerAction &&
                input.ActionWindowExpired)
            {
                if (input.Diagnosis?.Severity >= 3 &&
                    input.CoachTurnIndex < _config.MaxCoachTurnsPerEpisode)
                {
                    decision.Action = CoachPolicyAction.Continue;
                    decision.NextState = CoachEpisodeState.Active;
                    decision.ProposedFocus = ChooseFocus(input);
                    decision.PolicyReason = "AiHighSeverityUsesSecondFeedbackTurn";
                    return decision;
                }

                return End(decision, CoachEpisodeState.Completed,
                    CoachTerminationReason.LearnerPracticeDeferred,
                    "AiActionWindowElapsedPracticeDeferred", ControlOwner.Coach);
            }

            if (input.LearnerAction == CoachLearnerAction.Replay)
            {
                decision.Action = CoachPolicyAction.Replay;
                decision.NextState = input.EpisodeState;
                decision.PolicyReason = "LearnerRequestedReplay";
                return decision;
            }

            if (input.Mode == CoachOrchestrationMode.SharedControl &&
                input.LearnerAction == CoachLearnerAction.RequestCoach)
            {
                if (input.Diagnosis == null || !input.Diagnosis.HasEligibleIssue)
                {
                    return End(decision, CoachEpisodeState.Completed,
                        CoachTerminationReason.NoEligibleIssue,
                        "NoEligibleIssueAfterSharedAlternativeRequest", ControlOwner.Learner);
                }

                decision.Action = input.EpisodeState is CoachEpisodeState.Available or
                    CoachEpisodeState.Invited
                    ? CoachPolicyAction.Start
                    : CoachPolicyAction.Continue;
                decision.NextState = CoachEpisodeState.Active;
                decision.ProposedFocus = ChooseFocus(input);
                decision.PolicyReason = "SharedLearnerChangedRequest";
                decision.AgendaOwner = ControlOwner.Learner;
                return decision;
            }

            if (input.Mode == CoachOrchestrationMode.LearnerLed &&
                input.EpisodeState == CoachEpisodeState.AwaitingLearnerAction &&
                input.LearnerAction == CoachLearnerAction.RequestCoach)
            {
                if (input.CoachTurnIndex >= _config.MaxCoachTurnsPerEpisode)
                {
                    return End(decision, CoachEpisodeState.Completed,
                        CoachTerminationReason.TurnLimit,
                        "LearnerLedNaturalLanguageTurnLimitReached", ControlOwner.Learner);
                }

                decision.Action = CoachPolicyAction.Continue;
                decision.NextState = CoachEpisodeState.Active;
                decision.ProposedFocus = ChooseFocus(input);
                decision.PolicyReason = "LearnerRequestedMoreNaturalLanguageFeedback";
                decision.AgendaOwner = ControlOwner.Learner;
                return decision;
            }

            if (input.LearnerAction is CoachLearnerAction.RequestCoach or
                CoachLearnerAction.Accept or CoachLearnerAction.ConfirmFocus)
            {
                if (input.Diagnosis == null || !input.Diagnosis.HasEligibleIssue)
                {
                    return End(decision, CoachEpisodeState.Completed,
                        CoachTerminationReason.NoEligibleIssue,
                        "NoEligibleIssueAfterLearnerRequest", ControlOwner.Learner);
                }

                decision.Action = CoachPolicyAction.Start;
                decision.NextState = CoachEpisodeState.Active;
                decision.PolicyReason = "LearnerAuthorizedCoachEpisode";
                decision.ProposedFocus = ChooseFocus(input);
                if (input.LearnerAction == CoachLearnerAction.ConfirmFocus ||
                    !string.IsNullOrWhiteSpace(input.LearnerSelectedFocus))
                {
                    decision.AgendaOwner = ControlOwner.Learner;
                }

                return decision;
            }

            if (input.LearnerAction is CoachLearnerAction.NeedExample or
                CoachLearnerAction.ChangeFocus or CoachLearnerAction.Override)
            {
                if (input.CoachTurnIndex >= _config.MaxCoachTurnsPerEpisode)
                {
                    return End(decision, CoachEpisodeState.Completed,
                        CoachTerminationReason.TurnLimit,
                        "CoachTurnLimitReached", decision.TerminationOwner);
                }

                decision.Action = CoachPolicyAction.Continue;
                decision.NextState = input.LearnerAction == CoachLearnerAction.Override
                    ? CoachEpisodeState.Overridden
                    : CoachEpisodeState.Active;
                decision.PolicyReason = input.LearnerAction.ToString();
                decision.ProposedFocus = ChooseFocus(input);
                if (input.LearnerAction is CoachLearnerAction.ChangeFocus or CoachLearnerAction.Override)
                {
                    decision.AgendaOwner = ControlOwner.Learner;
                }

                return decision;
            }

            decision.Action = CoachPolicyAction.None;
            decision.NextState = input.EpisodeState;
            decision.PolicyReason = "NoPolicyTransition";
            return decision;
        }

        private CoachPolicyDecision EvaluateDiagnosis(
            CoachPolicyInput input,
            CoachPolicyDecision decision)
        {
            CoachDiagnosisResult diagnosis = input.Diagnosis;
            if (diagnosis == null || !diagnosis.Success)
            {
                return End(decision, CoachEpisodeState.Completed,
                    CoachTerminationReason.TechnicalFailure,
                    "DiagnosisUnavailable", ControlOwner.SystemSafety);
            }

            decision.ProposedFocus = CoachFocusCatalog.Normalize(diagnosis.RecommendedFocus);
            switch (input.Mode)
            {
                case CoachOrchestrationMode.LearnerLed:
                    decision.Action = CoachPolicyAction.MakeAvailable;
                    decision.NextState = CoachEpisodeState.Available;
                    decision.PolicyReason = diagnosis.HasEligibleIssue
                        ? "LearnerMayRequestCoach"
                        : "LearnerMayRequestNeutralNoIssueResult";
                    return decision;

                case CoachOrchestrationMode.SharedControl:
                    bool invite = diagnosis.HasEligibleIssue &&
                                  diagnosis.Severity >= _config.TriggerSeverityThreshold;
                    decision.Action = invite
                        ? CoachPolicyAction.Invite
                        : CoachPolicyAction.MakeAvailable;
                    decision.NextState = invite
                        ? CoachEpisodeState.Invited
                        : CoachEpisodeState.Available;
                    decision.PolicyReason = invite
                        ? "SeverityThresholdMetLearnerConfirmationRequired"
                        : "SeverityBelowInvitationThresholdLearnerMayRequest";
                    return decision;

                case CoachOrchestrationMode.AiLed:
                    bool autoStart = diagnosis.HasEligibleIssue &&
                                     diagnosis.Severity >= _config.TriggerSeverityThreshold &&
                                     diagnosis.Confidence >= _config.TriggerConfidenceThreshold;
                    if (!autoStart)
                    {
                        return End(decision, CoachEpisodeState.Completed,
                            CoachTerminationReason.NoEligibleIssue,
                            "AiLedThresholdsNotMet", ControlOwner.Coach);
                    }

                    decision.Action = CoachPolicyAction.AutoStart;
                    decision.NextState = CoachEpisodeState.Active;
                    decision.PolicyReason = "SeverityAndConfidenceThresholdsMet";
                    return decision;

                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        private static string ChooseFocus(CoachPolicyInput input)
        {
            if (!string.IsNullOrWhiteSpace(input.LearnerSelectedFocus))
            {
                return CoachFocusCatalog.Normalize(input.LearnerSelectedFocus);
            }

            return !string.IsNullOrWhiteSpace(input.ConfirmedFocus)
                ? CoachFocusCatalog.Normalize(input.ConfirmedFocus)
                : CoachFocusCatalog.Normalize(input.Diagnosis?.RecommendedFocus);
        }

        private static CoachPolicyDecision CreateDecision(CoachOrchestrationMode mode)
        {
            return mode switch
            {
                CoachOrchestrationMode.LearnerLed => new CoachPolicyDecision
                {
                    StartAuthority = ControlOwner.Learner,
                    AgendaOwner = ControlOwner.Learner,
                    PacingOwner = ControlOwner.Learner,
                    TerminationOwner = ControlOwner.Learner
                },
                CoachOrchestrationMode.SharedControl => new CoachPolicyDecision
                {
                    StartAuthority = ControlOwner.Shared,
                    AgendaOwner = ControlOwner.Shared,
                    PacingOwner = ControlOwner.Shared,
                    TerminationOwner = ControlOwner.Learner
                },
                CoachOrchestrationMode.AiLed => new CoachPolicyDecision
                {
                    StartAuthority = ControlOwner.Coach,
                    AgendaOwner = ControlOwner.Coach,
                    PacingOwner = ControlOwner.Coach,
                    TerminationOwner = ControlOwner.Coach
                },
                _ => new CoachPolicyDecision
                {
                    StartAuthority = ControlOwner.SystemSafety,
                    AgendaOwner = ControlOwner.SystemSafety,
                    PacingOwner = ControlOwner.SystemSafety,
                    TerminationOwner = ControlOwner.SystemSafety
                }
            };
        }

        private static CoachPolicyDecision End(
            CoachPolicyDecision decision,
            CoachEpisodeState state,
            CoachTerminationReason reason,
            string policyReason,
            ControlOwner terminationOwner)
        {
            decision.Action = CoachPolicyAction.End;
            decision.NextState = state;
            decision.TerminationReason = reason;
            decision.PolicyReason = policyReason;
            decision.TerminationOwner = terminationOwner;
            return decision;
        }
    }

    public static class CoachSpeechWindow
    {
        public static bool CanSubmit(float elapsedSeconds, CoachPolicyConfig config)
        {
            config ??= CoachPolicyConfig.CreateDefault();
            return elapsedSeconds >= config.LearnerSpeechMinimumSeconds;
        }

        public static bool ShouldAutoStop(float elapsedSeconds, CoachPolicyConfig config)
        {
            config ??= CoachPolicyConfig.CreateDefault();
            return elapsedSeconds >= config.LearnerSpeechMaximumSeconds;
        }
    }
}
