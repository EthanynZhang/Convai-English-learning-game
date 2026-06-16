using System.Collections;
using System.Collections.Generic;
using Convai.Scripts.Runtime.Core;
using Convai.Scripts.Runtime.Features;
using TMPro;
using UnityEngine;

namespace Game.Debate
{
    public class InteractiveNpcDebateController : MonoBehaviour
    {
        private sealed class SavedAudio
        {
            public AudioClip Clip;
            public string Transcript;
        }

        [Header("Conversation")]
        [SerializeField] private NPC2NPCConversationManager conversationManager;
        [SerializeField] private ConvaiGroupNPCController firstNPC;
        [SerializeField] private ConvaiGroupNPCController secondNPC;
        [SerializeField]
        private string debateTopic =
            "Reading and speaking, which is more important in learning English?";

        [Header("UI")]
        [SerializeField] private TMP_Text statusText;
        [SerializeField] private string waitingText = "Waiting for an NPC response...";
        [SerializeField] private bool unlockCursorWhilePaused = true;

        private readonly List<SavedAudio> _firstAudioBuffer = new();
        private readonly List<SavedAudio> _secondAudioBuffer = new();
        private readonly List<SavedAudio> _pendingAudio = new();

        private ConvaiGroupNPCController _pendingSpeaker;
        private string _pendingTranscript;
        private bool _isReplaying;
        private bool _isRequestingVariant;
        private bool _firstNpcSubscribed;
        private bool _secondNpcSubscribed;

        public bool IsPaused { get; private set; }

        private IEnumerator Start()
        {
            if (conversationManager != null)
            {
                conversationManager.RelayInterceptor = InterceptRelay;
            }

            SetStatus(waitingText);

            while (!_firstNpcSubscribed || !_secondNpcSubscribed)
            {
                _firstNpcSubscribed = TrySubscribeToAudio(
                    firstNPC,
                    CaptureFirstNpcAudio,
                    HandleFirstNpcTalkingChanged,
                    _firstNpcSubscribed);
                _secondNpcSubscribed = TrySubscribeToAudio(
                    secondNPC,
                    CaptureSecondNpcAudio,
                    HandleSecondNpcTalkingChanged,
                    _secondNpcSubscribed);

                if (!_firstNpcSubscribed || !_secondNpcSubscribed)
                {
                    yield return null;
                }
            }
        }

        private void OnDestroy()
        {
            if (conversationManager != null && conversationManager.RelayInterceptor == InterceptRelay)
            {
                conversationManager.RelayInterceptor = null;
            }

            UnsubscribeFromAudio(
                firstNPC,
                CaptureFirstNpcAudio,
                HandleFirstNpcTalkingChanged,
                _firstNpcSubscribed);
            UnsubscribeFromAudio(
                secondNPC,
                CaptureSecondNpcAudio,
                HandleSecondNpcTalkingChanged,
                _secondNpcSubscribed);
        }

        public void ReplayLastResponse()
        {
            if (!CanUsePendingResponse() || _isReplaying)
            {
                SetStatus("No completed response is available to replay.");
                return;
            }

            TryFinalizePendingAudio(_pendingSpeaker);
            if (_pendingAudio.Count == 0)
            {
                string status = _pendingSpeaker.ConvaiNPC.IsCharacterTalking
                    ? "Wait for the response audio to finish before replaying it."
                    : "No completed response audio is available to replay.";
                SetStatus(status);
                return;
            }

            StartCoroutine(ReplayPendingAudio());
        }

        public void MakeMorePolite()
        {
            RequestVariant(
                "Rewrite the response below as a more polite and less direct disagreement. " +
                "Preserve the original meaning, keep it concise, and output only the revised response.");
        }

        public void ShowBetterArgument()
        {
            RequestVariant(
                "Rewrite the response below as a stronger and clearer debate argument. " +
                "Add one specific reason or example, keep it concise, and output only the improved response.");
        }

        public void ShowBadExample()
        {
            RequestVariant(
                "Create a clearly inappropriate classroom debate example based on the response below. " +
                "Make it unnecessarily rude and too direct, but do not use profanity, slurs, threats, or hateful language. " +
                "Output only the bad example.");
        }

        public void ContinueDebate()
        {
            if (!CanUsePendingResponse() || conversationManager == null)
            {
                SetStatus("There is no paused response to continue from.");
                return;
            }

            ConvaiGroupNPCController speaker = _pendingSpeaker;
            string transcript = _pendingTranscript;

            IsPaused = false;
            _pendingSpeaker = null;
            _pendingTranscript = string.Empty;
            _pendingAudio.Clear();
            SetActionCursor(false);
            SetStatus("Debate continuing...");

            conversationManager.RelayMessageWithoutInterception(transcript, speaker);
        }

        private bool InterceptRelay(string message, ConvaiGroupNPCController sender)
        {
            _pendingSpeaker = sender;
            _pendingTranscript = message?.Trim() ?? string.Empty;
            IsPaused = true;
            SetActionCursor(false);

            _pendingAudio.Clear();

            string actionResult = _isRequestingVariant ? "Teaching version ready" : "Debate paused";
            _isRequestingVariant = false;
            if (!TryFinalizePendingAudio(sender))
            {
                SetStatus(
                    $"{actionResult} after {GetSpeakerName(sender)}. " +
                    "Waiting for the response audio to finish...");
            }

            return true;
        }

        private void RequestVariant(string instruction)
        {
            if (!CanUsePendingResponse() || _isRequestingVariant)
            {
                SetStatus("Wait for the current response to finish.");
                return;
            }

            _isRequestingVariant = true;
            SetActionCursor(false);
            _pendingAudio.Clear();
            GetAudioBuffer(_pendingSpeaker).Clear();

            string prompt =
                $"{instruction}\n\nDebate topic: {debateTopic}\n" +
                $"Original response: \"{_pendingTranscript}\"";

            SetStatus($"{GetSpeakerName(_pendingSpeaker)} is preparing a teaching version...");
            _pendingSpeaker.SendTextDataNPC2NPC(prompt);
        }

        private IEnumerator ReplayPendingAudio()
        {
            _isReplaying = true;
            SetStatus($"Replaying {GetSpeakerName(_pendingSpeaker)}'s last response...");

            ConvaiNPCAudioManager audioManager = _pendingSpeaker.ConvaiNPC.AudioManager;
            float duration = 0f;

            // Cached audio has no new streamed lip-sync frames, so release Convai's
            // post-response lip-sync wait before putting replay clips on its queue.
            audioManager.SetWaitForCharacterLipSync(false);

            foreach (SavedAudio saved in _pendingAudio)
            {
                if (saved.Clip == null)
                {
                    continue;
                }

                duration += saved.Clip.length;
                audioManager.AddResponseAudio(new ConvaiNPCAudioManager.ResponseAudio
                {
                    AudioClip = saved.Clip,
                    AudioTranscript = saved.Transcript,
                    IsFinal = false
                });
            }

            audioManager.AddResponseAudio(new ConvaiNPCAudioManager.ResponseAudio { IsFinal = true });

            float startTimeout = 3f;
            while (!_pendingSpeaker.ConvaiNPC.IsCharacterTalking && startTimeout > 0f)
            {
                startTimeout -= Time.deltaTime;
                yield return null;
            }

            if (!_pendingSpeaker.ConvaiNPC.IsCharacterTalking)
            {
                _isReplaying = false;
                SetStatus("Replay could not start. Try Replay Last Response again.");
                yield break;
            }

            float finishTimeout = duration + 3f;
            while (_pendingSpeaker.ConvaiNPC.IsCharacterTalking && finishTimeout > 0f)
            {
                finishTimeout -= Time.deltaTime;
                yield return null;
            }

            _isReplaying = false;
            SetActionCursor(true);
            SetStatus("Replay finished. Choose another action or continue.");
        }

        private void CaptureFirstNpcAudio(ConvaiNPCAudioManager.ResponseAudio response)
        {
            CaptureAudio(_firstAudioBuffer, response);
        }

        private void CaptureSecondNpcAudio(ConvaiNPCAudioManager.ResponseAudio response)
        {
            CaptureAudio(_secondAudioBuffer, response);
        }

        private void HandleFirstNpcTalkingChanged(bool isTalking)
        {
            HandleNpcTalkingChanged(firstNPC, isTalking);
        }

        private void HandleSecondNpcTalkingChanged(bool isTalking)
        {
            HandleNpcTalkingChanged(secondNPC, isTalking);
        }

        private void HandleNpcTalkingChanged(ConvaiGroupNPCController speaker, bool isTalking)
        {
            if (!isTalking && !_isReplaying && IsPaused && _pendingSpeaker == speaker)
            {
                TryFinalizePendingAudio(speaker);
            }
        }

        private void CaptureAudio(List<SavedAudio> buffer, ConvaiNPCAudioManager.ResponseAudio response)
        {
            if (_isReplaying || response == null || response.IsFinal || response.AudioClip == null)
            {
                return;
            }

            buffer.Add(new SavedAudio
            {
                Clip = response.AudioClip,
                Transcript = response.AudioTranscript
            });
        }

        private bool TryFinalizePendingAudio(ConvaiGroupNPCController speaker)
        {
            if (speaker == null || speaker.ConvaiNPC == null || speaker.ConvaiNPC.IsCharacterTalking)
            {
                return false;
            }

            List<SavedAudio> source = GetAudioBuffer(speaker);
            if (source.Count == 0)
            {
                return false;
            }

            _pendingAudio.Clear();
            _pendingAudio.AddRange(source);
            source.Clear();
            SetActionCursor(true);
            SetStatus(
                $"Debate paused after {GetSpeakerName(speaker)}. " +
                "Choose an action or continue.");
            return true;
        }

        private void SetActionCursor(bool canUseUi)
        {
            if (!unlockCursorWhilePaused)
            {
                return;
            }

            Cursor.lockState = canUseUi ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = canUseUi;
        }

        private bool TrySubscribeToAudio(
            ConvaiGroupNPCController controller,
            System.Action<ConvaiNPCAudioManager.ResponseAudio> audioHandler,
            System.Action<bool> talkingHandler,
            bool alreadySubscribed)
        {
            if (alreadySubscribed)
            {
                return true;
            }

            if (controller == null || controller.ConvaiNPC == null || controller.ConvaiNPC.AudioManager == null)
            {
                return false;
            }

            controller.ConvaiNPC.AudioManager.OnResponseAudioStarted += audioHandler;
            controller.ConvaiNPC.AudioManager.OnCharacterTalkingChanged += talkingHandler;
            return true;
        }

        private void UnsubscribeFromAudio(
            ConvaiGroupNPCController controller,
            System.Action<ConvaiNPCAudioManager.ResponseAudio> audioHandler,
            System.Action<bool> talkingHandler,
            bool wasSubscribed)
        {
            if (wasSubscribed &&
                controller != null &&
                controller.ConvaiNPC != null &&
                controller.ConvaiNPC.AudioManager != null)
            {
                controller.ConvaiNPC.AudioManager.OnResponseAudioStarted -= audioHandler;
                controller.ConvaiNPC.AudioManager.OnCharacterTalkingChanged -= talkingHandler;
            }
        }

        private List<SavedAudio> GetAudioBuffer(ConvaiGroupNPCController speaker)
        {
            return speaker == firstNPC ? _firstAudioBuffer : _secondAudioBuffer;
        }

        private bool CanUsePendingResponse()
        {
            return IsPaused && _pendingSpeaker != null && !string.IsNullOrWhiteSpace(_pendingTranscript);
        }

        private string GetSpeakerName(ConvaiGroupNPCController speaker)
        {
            if (speaker == null)
            {
                return "NPC";
            }

            return !string.IsNullOrWhiteSpace(speaker.CharacterName)
                ? speaker.CharacterName
                : speaker.gameObject.name;
        }

        private void SetStatus(string message)
        {
            if (statusText != null)
            {
                statusText.text = message;
            }
        }
    }
}
