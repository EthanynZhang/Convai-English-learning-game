using System;
using UnityEngine;

namespace Game.Debate
{
    /// <summary>
    /// One persisted Convai/local-flow choice shared by every scene.
    /// </summary>
    public static class ConversationFlowSettings
    {
        public const string PlayerPrefsKey = "DISABLE_CONVAI_FLOW";
        private const string LegacyScene3PlayerPrefsKey = "SCENE03_DISABLE_CONVAI_FLOW";

        public static event Action<bool> DisableConvaiChanged;

        public static bool DisableConvai
        {
            get
            {
                if (PlayerPrefs.HasKey(PlayerPrefsKey))
                {
                    return PlayerPrefs.GetInt(PlayerPrefsKey, 1) != 0;
                }

                return PlayerPrefs.GetInt(LegacyScene3PlayerPrefsKey, 1) != 0;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void InitializeDefault()
        {
            // Convai is opt-in for each application run.  Do not carry an earlier
            // "enabled" debug choice into a new experiment session.
            PlayerPrefs.SetInt(PlayerPrefsKey, 1);
            PlayerPrefs.Save();
        }

        public static bool Toggle()
        {
            bool next = !DisableConvai;
            SetDisableConvai(next);
            return next;
        }

        public static void SetDisableConvai(bool disableConvai)
        {
            bool changed = !PlayerPrefs.HasKey(PlayerPrefsKey) ||
                           DisableConvai != disableConvai;
            PlayerPrefs.SetInt(PlayerPrefsKey, disableConvai ? 1 : 0);
            PlayerPrefs.Save();

            if (changed)
            {
                DisableConvaiChanged?.Invoke(disableConvai);
            }
        }
    }

    /// <summary>
    /// Central runtime policy used by every Convai transport before it opens or
    /// sends a network request. Keeping this check below the scene controllers
    /// also protects dormant and legacy Convai components that are enabled by
    /// mistake.
    /// </summary>
    public static class ConvaiNetworkPolicy
    {
        public static bool RequestsAllowed => !ConversationFlowSettings.DisableConvai;
    }
}
