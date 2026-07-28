using UnityEngine;

namespace Game.Debate
{
    public sealed class InternalTestApiCredentials : ScriptableObject
    {
        public const string ResourcePath = "InternalTestApiCredentials";

        [SerializeField] private string miniMaxApiKey = string.Empty;
        [SerializeField] private string debateApiKey = string.Empty;
        [SerializeField] private string debateBaseUrl = string.Empty;
        [SerializeField] private string xfyunAppId = string.Empty;
        [SerializeField] private string xfyunApiKey = string.Empty;

        public string MiniMaxApiKey => Clean(miniMaxApiKey);
        public string DebateApiKey => Clean(debateApiKey);
        public string DebateBaseUrl => Clean(debateBaseUrl);
        public string XfyunAppId => Clean(xfyunAppId);
        public string XfyunApiKey => Clean(xfyunApiKey);

        public bool IsComplete =>
            !string.IsNullOrWhiteSpace(MiniMaxApiKey) &&
            !string.IsNullOrWhiteSpace(DebateApiKey) &&
            !string.IsNullOrWhiteSpace(DebateBaseUrl) &&
            !string.IsNullOrWhiteSpace(XfyunAppId) &&
            !string.IsNullOrWhiteSpace(XfyunApiKey);

        public static InternalTestApiCredentials Load()
        {
            return Resources.Load<InternalTestApiCredentials>(ResourcePath);
        }

        private static string Clean(string value)
        {
            return value?.Trim() ?? string.Empty;
        }
    }
}
