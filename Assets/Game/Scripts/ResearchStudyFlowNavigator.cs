using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Game.Debate
{
    /// <summary>
    /// Owns the formal research-study scene order so completion logging and navigation cannot drift.
    /// </summary>
    public static class ResearchStudyFlowNavigator
    {
        public static bool TryGetNextScene(
            string currentSceneId,
            out string nextSceneId,
            out string nextSceneName)
        {
            switch (currentSceneId?.Trim())
            {
                case "01":
                    nextSceneId = "03";
                    nextSceneName = "03Level_PlayerVsNPCDebate";
                    return true;
                case "03":
                    nextSceneId = "04";
                    nextSceneName = "04 coach Agent";
                    return true;
                case "04":
                    nextSceneId = "05";
                    nextSceneName = "05Level_PlayerVsNPCDebate 1";
                    return true;
                default:
                    nextSceneId = string.Empty;
                    nextSceneName = string.Empty;
                    return false;
            }
        }

        public static bool TryLoadNextScene(string currentSceneId, bool dataComplete = true)
        {
            if (!dataComplete ||
                !TryGetNextScene(currentSceneId, out string nextSceneId, out string nextSceneName))
            {
                return false;
            }

            if (!Application.isPlaying)
            {
                return false;
            }

            if (!TryGetNextBuildIndex(currentSceneId, out int nextBuildIndex) ||
                !Application.CanStreamedLevelBeLoaded(nextBuildIndex))
            {
                ResearchCapture.RecordTechnicalFailure(
                    "scene_transition_failed",
                    "SCENE_NOT_IN_BUILD",
                    $"Formal study scene '{nextSceneName}' ({nextSceneId}) is not loadable.");
                Debug.LogError($"Formal study scene is not loadable: {nextSceneName}");
                return false;
            }

            ResearchCapture.RecordEvent(
                "scene_transition_requested",
                "system",
                payload: new
                {
                    from_scene_id = currentSceneId?.Trim() ?? string.Empty,
                    to_scene_id = nextSceneId,
                    to_scene_name = nextSceneName,
                    data_complete = true,
                    requested_at_utc = DateTimeOffset.UtcNow.ToString("O")
                });
            SceneManager.LoadScene(nextBuildIndex);
            return true;
        }

        public static bool TryGetNextBuildIndex(string currentSceneId, out int nextBuildIndex)
        {
            nextBuildIndex = -1;
            if (!TryGetNextScene(currentSceneId, out _, out string nextSceneName))
            {
                return false;
            }

            string scenePath = $"Assets/Game/Scenes/{nextSceneName}.unity";
            nextBuildIndex = SceneUtility.GetBuildIndexByScenePath(scenePath);
            return nextBuildIndex >= 0;
        }
    }
}
