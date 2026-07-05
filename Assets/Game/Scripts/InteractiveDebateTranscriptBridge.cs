using Convai.Scripts.Runtime.Features;
using Convai.Scripts.Runtime.UI;
using UnityEngine;

namespace Game.Debate
{
    public class InteractiveDebateTranscriptBridge : MonoBehaviour
    {
        [SerializeField] private ConvaiChatUIHandler chatUIHandler;
        [SerializeField] private string learnerDisplayName = "Learner";
        [SerializeField] private string coachDisplayName = "Coach";
        [SerializeField] private Color normalNpcTextColor = new(0.32f, 0.68f, 1f, 1f);
        [SerializeField] private Color revisedNpcTextColor = new(1f, 0.72f, 0.25f, 1f);
        [SerializeField] private Color learnerCommandTextColor = new(0.35f, 0.92f, 0.42f, 1f);
        [SerializeField] private Color coachTextColor = new(1f, 0.86f, 0.32f, 1f);

        public void PublishNpcLine(ConvaiGroupNPCController speaker, string transcript, bool isRevised)
        {
            string safeTranscript = transcript?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(safeTranscript))
            {
                return;
            }

            IChatUI currentUI = GetCurrentUI();
            if (currentUI == null)
            {
                Debug.LogWarning("InteractiveDebateTranscriptBridge could not find an active Convai chat UI for an NPC line.");
                return;
            }

            currentUI.ActivateUI();
            currentUI.SendCharacterText(
                GetSpeakerDisplayName(GetSpeakerName(speaker), isRevised),
                safeTranscript,
                isRevised ? revisedNpcTextColor : normalNpcTextColor);
        }

        public void PublishLearnerCommand(string commandText)
        {
            string formattedCommand = FormatLearnerCommand(commandText);
            if (string.IsNullOrWhiteSpace(formattedCommand))
            {
                return;
            }

            IChatUI currentUI = GetCurrentUI();
            if (currentUI == null)
            {
                Debug.LogWarning("InteractiveDebateTranscriptBridge could not find an active Convai chat UI for a learner command.");
                return;
            }

            currentUI.ActivateUI();
            currentUI.SendPlayerText(learnerDisplayName, formattedCommand, learnerCommandTextColor);
        }

        public void PublishPlayerUtterance(string utteranceText)
        {
            string safeText = utteranceText?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(safeText))
            {
                return;
            }

            IChatUI currentUI = GetCurrentUI();
            if (currentUI == null)
            {
                Debug.LogWarning("InteractiveDebateTranscriptBridge could not find an active Convai chat UI for a learner utterance.");
                return;
            }

            currentUI.ActivateUI();
            currentUI.SendPlayerText(learnerDisplayName, safeText, learnerCommandTextColor);
        }

        public void PublishNpcLine(string speakerName, string transcript)
        {
            string safeTranscript = transcript?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(safeTranscript))
            {
                return;
            }

            IChatUI currentUI = GetCurrentUI();
            if (currentUI == null)
            {
                Debug.LogWarning("InteractiveDebateTranscriptBridge could not find an active Convai chat UI for an NPC line.");
                return;
            }

            currentUI.ActivateUI();
            currentUI.SendCharacterText(
                GetSpeakerDisplayName(speakerName, false),
                safeTranscript,
                normalNpcTextColor);
        }

        public void PublishCoachLine(string feedbackText)
        {
            string safeText = feedbackText?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(safeText))
            {
                return;
            }

            IChatUI currentUI = GetCurrentUI();
            if (currentUI == null)
            {
                Debug.LogWarning("InteractiveDebateTranscriptBridge could not find an active Convai chat UI for Coach feedback.");
                return;
            }

            currentUI.ActivateUI();
            currentUI.SendCharacterText(coachDisplayName, safeText, coachTextColor);
        }

        public static string GetSpeakerDisplayName(string speakerName, bool isRevised)
        {
            string safeName = string.IsNullOrWhiteSpace(speakerName) ? "NPC" : speakerName.Trim();
            return isRevised ? safeName + " (revised)" : safeName;
        }

        public static string FormatLearnerCommand(string commandText)
        {
            string safeCommand = commandText?.Trim() ?? string.Empty;
            return string.IsNullOrWhiteSpace(safeCommand) ? string.Empty : "Strategy: " + safeCommand;
        }

        private IChatUI GetCurrentUI()
        {
            ConvaiChatUIHandler handler = chatUIHandler != null ? chatUIHandler : ConvaiChatUIHandler.Instance;
            return handler == null ? null : handler.GetCurrentUI();
        }

        private static string GetSpeakerName(ConvaiGroupNPCController speaker)
        {
            if (speaker == null)
            {
                return "NPC";
            }

            return !string.IsNullOrWhiteSpace(speaker.CharacterName)
                ? speaker.CharacterName
                : speaker.gameObject.name;
        }
    }
}
