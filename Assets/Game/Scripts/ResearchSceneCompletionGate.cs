using System;
using System.Collections.Generic;

namespace Game.Debate
{
    public sealed class ResearchSceneCompletionStatus
    {
        public bool DataComplete;
        public string MissingRequiredData = string.Empty;
    }

    public static class ResearchSceneCompletionGate
    {
        public static ResearchSceneCompletionStatus EvaluateOral(
            string sceneId,
            int attemptCount,
            int confirmedTranscriptCount,
            int audioArtifactCount,
            bool noCoachVerified)
        {
            List<string> missing = new();
            if (attemptCount < 1) missing.Add("recording_attempt");
            if (confirmedTranscriptCount < 1) missing.Add("confirmed_transcript");
            if (audioArtifactCount < 1) missing.Add("audio_artifact");
            if (string.Equals(sceneId?.Trim(), "05", StringComparison.Ordinal) && !noCoachVerified)
                missing.Add("no_coach_verified");
            return new ResearchSceneCompletionStatus
            {
                DataComplete = missing.Count == 0,
                MissingRequiredData = string.Join(";", missing)
            };
        }

        public static ResearchSceneCompletionStatus EvaluatePractice(
            int microIndependentResponseCount,
            int microRevisionResponseCount,
            bool fullSpeechConfirmed,
            bool revisionSpeechConfirmed,
            int confirmedTranscriptCount,
            int audioArtifactCount,
            int successfulDiagnosisCount,
            int coachEpisodeCount)
        {
            List<string> missing = new();
            if (microIndependentResponseCount < 1) missing.Add("micro_independent_cycle_1");
            if (microIndependentResponseCount < 2) missing.Add("micro_independent_cycle_2");
            if (microRevisionResponseCount < 1) missing.Add("micro_revision_cycle_1");
            if (microRevisionResponseCount < 2) missing.Add("micro_revision_cycle_2");
            if (!fullSpeechConfirmed) missing.Add("full_speech");
            if (!revisionSpeechConfirmed) missing.Add("revision_speech");
            if (confirmedTranscriptCount < 5) missing.Add("scene04_confirmed_transcripts");
            if (audioArtifactCount < 5) missing.Add("scene04_audio_artifacts");
            if (successfulDiagnosisCount < 6) missing.Add("scene04_diagnoses");
            if (coachEpisodeCount < 3) missing.Add("coach_episodes");
            return new ResearchSceneCompletionStatus
            {
                DataComplete = missing.Count == 0,
                MissingRequiredData = string.Join(";", missing)
            };
        }

        public static ResearchSceneCompletionStatus EvaluateCreeiWorkbenchPractice(
            int committedSnapshotCount,
            int completedWorkbenchRoundCount,
            bool fullSpeechConfirmed,
            bool revisionSpeechConfirmed,
            int confirmedTranscriptCount,
            int audioArtifactCount,
            int successfulDiagnosisCount)
        {
            List<string> missing = new();
            if (committedSnapshotCount < 2) missing.Add("micro_creei_snapshots");
            if (completedWorkbenchRoundCount < 1) missing.Add("micro_creei_round");
            if (!fullSpeechConfirmed) missing.Add("full_speech");
            if (!revisionSpeechConfirmed) missing.Add("revision_speech");
            if (confirmedTranscriptCount < 2) missing.Add("scene04_confirmed_transcripts");
            if (audioArtifactCount < 2) missing.Add("scene04_audio_artifacts");
            if (successfulDiagnosisCount < 2) missing.Add("scene04_diagnoses");
            return new ResearchSceneCompletionStatus
            {
                DataComplete = missing.Count == 0,
                MissingRequiredData = string.Join(";", missing)
            };
        }
    }
}
