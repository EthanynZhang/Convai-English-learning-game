using System;
using UnityEngine;

namespace Game.Debate
{
    public static class CoachConditionExperience
    {
        public static string BuildSharedSuggestion(CoachDiagnosisResult diagnosis)
        {
            string weakComponent = CleanSentencePart(diagnosis?.WeakComponent, "one part of your response");
            string focus = CleanSentencePart(
                CoachFocusCatalog.Normalize(diagnosis?.RecommendedFocus),
                "Explanation");
            string gap = CleanSentencePart(
                diagnosis?.CreeiGapSummary,
                $"The {weakComponent} component needs development");
            return $"{gap}. Anna suggests focusing on {focus}.";
        }

        public static string BuildAiDiagnosisAgenda(CoachDiagnosisResult diagnosis)
        {
            string weakComponent = CleanSentencePart(
                diagnosis?.WeakComponent, "one part of your response");
            string focus = CleanSentencePart(
                CoachFocusCatalog.Normalize(diagnosis?.RecommendedFocus), "Explanation");
            string gap = CleanSentencePart(
                diagnosis?.CreeiGapSummary,
                $"The {weakComponent} component needs development");
            return $"{gap}. Anna will focus on {focus}.";
        }

        private static string CleanSentencePart(string value, string fallback)
        {
            string clean = value?.Trim().Trim('.', '!', '?') ?? string.Empty;
            return string.IsNullOrWhiteSpace(clean) ? fallback : clean;
        }
    }

    public enum DebatePracticeStage
    {
        MicroPractice,
        FullSpeechWithFeedback,
        RevisionSpeech
    }

    public enum DebatePracticePhase
    {
        Setup,
        Introduction,
        ReadyToRecord,
        Recording,
        AwaitingTranscript,
        GeneratingChallenge,
        OpponentSpeaking,
        Diagnosing,
        CoachInteraction,
        EvaluatingRevision,
        TechnicalError,
        Complete
    }

    public enum MicroChallengeStep
    {
        InitialStatement,
        GeneratingChallenge,
        OpponentSpeaking,
        IndependentResponse,
        Diagnosing,
        CoachInteraction,
        RevisionResponse,
        EvaluatingRevision,
        Completed
    }

    public enum OpponentChallengeDifficulty
    {
        Standard,
        Hard
    }

    public enum DebateVoiceCaptureTarget
    {
        None,
        PracticeSpeech,
        LearnerRequest
    }

    public sealed class MicroChallengeLoop
    {
        public const int RequiredCycleCount = 2;

        public MicroChallengeStep Step { get; private set; } =
            MicroChallengeStep.InitialStatement;
        public int CycleIndex { get; private set; }
        public int CompletedCycleCount { get; private set; }
        public OpponentChallengeDifficulty Difficulty => CycleIndex >= 2
            ? OpponentChallengeDifficulty.Hard
            : OpponentChallengeDifficulty.Standard;

        public void ConfirmInitialStatement()
        {
            Require(MicroChallengeStep.InitialStatement);
            CycleIndex = 1;
            Step = MicroChallengeStep.GeneratingChallenge;
        }

        public void ChallengeReady()
        {
            Require(MicroChallengeStep.GeneratingChallenge);
            Step = MicroChallengeStep.OpponentSpeaking;
        }

        public void OpponentFinishedSpeaking()
        {
            Require(MicroChallengeStep.OpponentSpeaking);
            Step = MicroChallengeStep.Diagnosing;
        }

        public void ConfirmIndependentResponse()
        {
            Require(MicroChallengeStep.IndependentResponse);
            Step = MicroChallengeStep.Diagnosing;
        }

        public void DiagnosisCompleted()
        {
            Require(MicroChallengeStep.Diagnosing);
            Step = MicroChallengeStep.CoachInteraction;
        }

        public void CompleteCoaching()
        {
            Require(MicroChallengeStep.CoachInteraction);
            Step = MicroChallengeStep.RevisionResponse;
        }

        public void ConfirmRevision()
        {
            Require(MicroChallengeStep.RevisionResponse);
            Step = MicroChallengeStep.EvaluatingRevision;
        }

        public void RevisionEvaluationCompleted()
        {
            Require(MicroChallengeStep.EvaluatingRevision);
            CompletedCycleCount++;
            if (CompletedCycleCount >= RequiredCycleCount)
            {
                Step = MicroChallengeStep.Completed;
                return;
            }

            CycleIndex++;
            Step = MicroChallengeStep.GeneratingChallenge;
        }

        public void Expire()
        {
            Step = MicroChallengeStep.Completed;
        }

        private void Require(MicroChallengeStep expected)
        {
            if (Step != expected)
                throw new InvalidOperationException(
                    $"Micro challenge transition requires {expected}, but current step is {Step}.");
        }
    }

    public enum CoachDecisionCue
    {
        Silent,
        AskCoach,
        Invite,
        SpeakFeedback,
        AnnounceFocusAndSpeak
    }

    public static class ThreeStageDebatePracticeRules
    {
        public const string MockDebateTopic =
            "Individual practice and interaction with others, which is more beneficial for developing English speaking skills?";
        public const string LearnerStance =
            "Interaction with others is more beneficial for developing English speaking skills.";
        public const string OpponentStance =
            "Individual practice is more beneficial for developing English speaking skills.";
        public const float MicroPracticeSeconds = 600f;
        public const float SustainedSpeechMinimumSeconds = 90f;
        public const float SustainedSpeechMaximumSeconds = 180f;

        public static readonly DebatePracticeStage[] StageSequence =
        {
            DebatePracticeStage.MicroPractice,
            DebatePracticeStage.FullSpeechWithFeedback,
            DebatePracticeStage.RevisionSpeech
        };

        public static bool CanStopRecording(DebatePracticeStage stage, float elapsedSeconds)
        {
            return stage == DebatePracticeStage.MicroPractice ||
                   elapsedSeconds >= SustainedSpeechMinimumSeconds;
        }

        public static bool ShouldAutoStopRecording(DebatePracticeStage stage, float elapsedSeconds)
        {
            return stage != DebatePracticeStage.MicroPractice &&
                   elapsedSeconds >= SustainedSpeechMaximumSeconds;
        }

        public static bool ShouldAutomaticallyAcceptTranscript(string transcript)
        {
            return !string.IsNullOrWhiteSpace(transcript);
        }

        public static bool IsStageTimeExpired(DebatePracticeStage stage, float elapsedSeconds)
        {
            return stage == DebatePracticeStage.MicroPractice &&
                   elapsedSeconds >= MicroPracticeSeconds;
        }

        public static bool ShouldAdvanceAfterCoachEpisode(
            DebatePracticeStage stage,
            CoachTerminationReason reason,
            float stageElapsedSeconds)
        {
            if (stage != DebatePracticeStage.MicroPractice) return true;
            return stageElapsedSeconds >= MicroPracticeSeconds;
        }

        public static bool UsesOpponent(DebatePracticeStage stage) =>
            stage == DebatePracticeStage.MicroPractice;

        public static bool ShouldKeepCursorVisible(DebatePracticePhase phase) => true;

        public static bool CanDictateLearnerRequest(
            CoachOrchestrationMode mode,
            DebatePracticePhase phase,
            bool requestInputVisible)
        {
            return mode == CoachOrchestrationMode.LearnerLed &&
                   phase == DebatePracticePhase.CoachInteraction &&
                   requestInputVisible;
        }

        public static string MergeLearnerRequestText(string typedText, string voiceText)
        {
            string typed = typedText?.Trim() ?? string.Empty;
            string voice = voiceText?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(typed)) return voice;
            if (string.IsNullOrWhiteSpace(voice)) return typed;
            return typed + " " + voice;
        }

        public static bool UsesVisibleCoach(DebatePracticeStage stage)
        {
            return stage != DebatePracticeStage.RevisionSpeech;
        }

        public static DebatePracticeStage? Next(DebatePracticeStage stage)
        {
            return stage switch
            {
                DebatePracticeStage.MicroPractice => DebatePracticeStage.FullSpeechWithFeedback,
                DebatePracticeStage.FullSpeechWithFeedback => DebatePracticeStage.RevisionSpeech,
                _ => null
            };
        }

        public static string GetTitle(DebatePracticeStage stage)
        {
            return stage switch
            {
                DebatePracticeStage.MicroPractice => "Challenge & Revision Lab",
                DebatePracticeStage.FullSpeechWithFeedback => "Full Speech + Coach Feedback",
                DebatePracticeStage.RevisionSpeech => "Revision Speech",
                _ => throw new ArgumentOutOfRangeException(nameof(stage), stage, null)
            };
        }

        public static int GetStageNumber(DebatePracticeStage stage)
        {
            return Array.IndexOf(StageSequence, stage) + 1;
        }

        public static float GetDisplayLimitSeconds(DebatePracticeStage stage)
        {
            return stage == DebatePracticeStage.MicroPractice
                ? MicroPracticeSeconds
                : SustainedSpeechMaximumSeconds;
        }
    }

    public static class CoachDecisionMomentRules
    {
        public static CoachDecisionCue ResolveCue(
            CoachOrchestrationMode mode,
            CoachEpisodeState state)
        {
            return mode switch
            {
                CoachOrchestrationMode.LearnerLed when state == CoachEpisodeState.Available =>
                    CoachDecisionCue.AskCoach,
                CoachOrchestrationMode.LearnerLed when state is CoachEpisodeState.Active or
                    CoachEpisodeState.AwaitingLearnerAction => CoachDecisionCue.SpeakFeedback,
                CoachOrchestrationMode.SharedControl when state == CoachEpisodeState.Invited =>
                    CoachDecisionCue.Invite,
                CoachOrchestrationMode.SharedControl when state is CoachEpisodeState.Active or
                    CoachEpisodeState.AwaitingLearnerAction => CoachDecisionCue.SpeakFeedback,
                CoachOrchestrationMode.AiLed when state is CoachEpisodeState.Active or
                    CoachEpisodeState.AwaitingLearnerAction => CoachDecisionCue.AnnounceFocusAndSpeak,
                _ => CoachDecisionCue.Silent
            };
        }
    }

    public sealed class CoachFixedPoseAnchor
    {
        private readonly Transform _transform;
        private readonly Vector3 _position;
        private readonly Quaternion _rotation;

        public CoachFixedPoseAnchor(Transform transform)
        {
            _transform = transform;
            if (_transform == null) return;
            _position = _transform.position;
            _rotation = _transform.rotation;
        }

        public static Quaternion CalculateFacingRotation(
            Vector3 coachPosition,
            Vector3 learnerPosition)
        {
            Vector3 direction = learnerPosition - coachPosition;
            direction.y = 0f;
            return direction.sqrMagnitude < 0.0001f
                ? Quaternion.identity
                : Quaternion.LookRotation(direction.normalized, Vector3.up);
        }

        public void Apply()
        {
            if (_transform == null) return;
            _transform.SetPositionAndRotation(_position, _rotation);
        }
    }
}
