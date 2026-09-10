using System;

namespace Game.Debate
{
    [Serializable]
    public sealed class CoachStudySessionSnapshot
    {
        public string ParticipantId = string.Empty;
        public string SessionId = string.Empty;
        public CoachOrchestrationMode Mode;
        public string PracticeTopic = string.Empty;
        public string TransferTopic = string.Empty;
        public bool PilotUsesSameTransferTopic;
        public string StartedAt = string.Empty;
    }

    public static class CoachStudySessionContext
    {
        public static CoachStudySessionSnapshot Current { get; private set; }
        public static bool IsInitialized => Current != null;

        public static CoachStudySessionSnapshot Initialize(
            string participantId,
            CoachOrchestrationMode mode,
            string practiceTopic,
            string transferTopic)
        {
            string safeParticipantId = participantId?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(safeParticipantId))
            {
                throw new ArgumentException("An anonymous participant ID is required.", nameof(participantId));
            }

            string safePracticeTopic = practiceTopic?.Trim() ?? string.Empty;
            string safeTransferTopic = transferTopic?.Trim() ?? string.Empty;
            Current = new CoachStudySessionSnapshot
            {
                ParticipantId = safeParticipantId,
                SessionId = Guid.NewGuid().ToString("N"),
                Mode = mode,
                PracticeTopic = safePracticeTopic,
                TransferTopic = safeTransferTopic,
                PilotUsesSameTransferTopic = string.Equals(
                    safePracticeTopic,
                    safeTransferTopic,
                    StringComparison.OrdinalIgnoreCase),
                StartedAt = DateTimeOffset.UtcNow.ToString("o")
            };
            return Current;
        }

        public static void Clear()
        {
            Current = null;
        }
    }
}
