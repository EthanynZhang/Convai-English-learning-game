using UnityEngine;

namespace Game.Debate
{
    /// <summary>
    /// Explicit scene contract for the transfer stage. The experimental Coach is
    /// intentionally disabled here and the learner applies prior skills to a new topic.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TransferDebateStageGuard : MonoBehaviour
    {
        public const string StageName = "Transfer";
        public const string TransferTopic =
            "Classroom instruction or real-life context: which is more beneficial for English speaking learning?";
        public const CoachOrchestrationMode Mode = CoachOrchestrationMode.Disabled;

        public bool IsPilotSameTopicTransfer =>
            CoachStudySessionContext.Current?.PilotUsesSameTransferTopic ?? true;

        private void Awake()
        {
            if (HasCoachContractViolation())
            {
                Debug.LogError("Transfer scene contract violation: Coach components must not be present in scene 05.");
            }
        }

        public static bool HasCoachContractViolation()
        {
            return FindAnyObjectByType<CoachEpisodeController>(FindObjectsInactive.Include) != null ||
                   FindAnyObjectByType<CoachExperimentView>(FindObjectsInactive.Include) != null ||
                   FindAnyObjectByType<SharedInitiativeOrchestrationController>(FindObjectsInactive.Include) != null ||
                   FindAnyObjectByType<GuidedCreeiStudyController>(FindObjectsInactive.Include) != null ||
                   FindAnyObjectByType<GuidedCreeiStudyView>(FindObjectsInactive.Include) != null ||
                   FindAnyObjectByType<ThreeStageDebatePracticeController>(FindObjectsInactive.Include) != null ||
                   FindAnyObjectByType<ThreeStageDebatePracticeView>(FindObjectsInactive.Include) != null;
        }

        private void OnDestroy()
        {
            CoachStudySessionContext.Clear();
        }
    }
}
