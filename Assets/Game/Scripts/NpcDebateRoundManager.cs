using System.Collections;
using Convai.Scripts.Runtime.Features;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Debate
{
    public class NpcDebateRoundManager : MonoBehaviour
    {
        [Header("Round")]
        [SerializeField] private bool startOnPlay = true;
        [SerializeField] private float roundDurationSeconds = 180f;
        [SerializeField] private float openingCaptionSeconds = 2.5f;
        [SerializeField] private float closingCaptionSeconds = 5f;
        [SerializeField] private bool endConversationWhenTimeExpires = true;

        [Header("Debate Topic")]
        [SerializeField]
        private string debateTopic =
            "Reading and speaking, which is more important in learning English?";

        [Header("Referee")]
        [SerializeField] private TMP_Text refereeCaptionText;
        [TextArea(2, 4)]
        [SerializeField]
        private string refereeOpeningLine =
            "Welcome, everyone. Today's debate topic is: {0} The NPC debate begins.";

        [SerializeField] private string refereeClosingLine = "Thank you, everyone. The debate is over.";

        [Header("Referee Audio")]
        [SerializeField] private AudioSource refereeAudioSource;
        [SerializeField] private AudioClip refereeOpeningClip;
        [SerializeField] private AudioClip refereeClosingClip;

        [Header("Start Control")]
        [SerializeField] private Button startButton;
        [SerializeField] private bool unlockCursorBeforeStart = true;

        [Header("Timer")]
        [SerializeField] private TMP_Text timerText;
        [SerializeField] private string timerPrefix = "Time Left ";

        [Header("NPC Debate")]
        [SerializeField] private NPC2NPCConversationManager conversationManager;
        [SerializeField] private ConvaiGroupNPCController firstSpeaker;
        [SerializeField] private float firstSpeakerDelaySeconds = 0.5f;
        [TextArea(2, 6)]
        [SerializeField]
        private string firstSpeakerPrompt =
            "The debate topic is: {0} You are the first speaker. Argue that reading is more important in learning English. Present your opening argument in under 20 seconds, then wait for the other NPC to respond.";

        private Coroutine _roundRoutine;
        private float _remainingSeconds;

        public bool IsRoundRunning { get; private set; }
        public bool HasRoundEnded { get; private set; }

        private void Start()
        {
            HideRefereeCaption();
            UpdateTimer(roundDurationSeconds);
            SetStartButtonVisible(!startOnPlay);
            SetWaitingForStartCursor(!startOnPlay);

            if (startOnPlay)
            {
                BeginRound();
            }
        }

        public void BeginRound()
        {
            if (IsRoundRunning)
            {
                return;
            }

            SetStartButtonVisible(false);
            SetWaitingForStartCursor(false);
            if (_roundRoutine != null)
            {
                StopCoroutine(_roundRoutine);
            }

            _roundRoutine = StartCoroutine(RunRound());
        }

        private IEnumerator RunRound()
        {
            IsRoundRunning = true;
            HasRoundEnded = false;
            _remainingSeconds = roundDurationSeconds;

            ShowRefereeCaption(FormatDebateText(refereeOpeningLine));
            PlayRefereeClip(refereeOpeningClip);
            UpdateTimer(_remainingSeconds);

            yield return new WaitForSeconds(GetCaptionDelay(openingCaptionSeconds, refereeOpeningClip));

            StartNpcOpening();

            while (_remainingSeconds > 0f)
            {
                _remainingSeconds -= Time.deltaTime;
                UpdateTimer(Mathf.Max(0f, _remainingSeconds));
                yield return null;
            }

            IsRoundRunning = false;
            HasRoundEnded = true;

            if (endConversationWhenTimeExpires && conversationManager != null && firstSpeaker != null)
            {
                conversationManager.EndConversation(firstSpeaker);
            }

            ShowRefereeCaption(FormatDebateText(refereeClosingLine));
            PlayRefereeClip(refereeClosingClip);
            UpdateTimer(0f);

            yield return new WaitForSeconds(GetCaptionDelay(closingCaptionSeconds, refereeClosingClip));
            HideRefereeCaption();
        }

        private void StartNpcOpening()
        {
            if (firstSpeaker == null)
            {
                return;
            }

            if (conversationManager != null)
            {
                foreach (NPCGroup group in conversationManager.npcGroups)
                {
                    if (group != null && group.BelongToGroup(firstSpeaker))
                    {
                        group.CurrentSpeaker = firstSpeaker;
                        group.topic = debateTopic;
                        break;
                    }
                }
            }

            StartCoroutine(SendFirstSpeakerPrompt());
        }

        private void SetStartButtonVisible(bool visible)
        {
            if (startButton != null)
            {
                startButton.gameObject.SetActive(visible);
            }
        }

        private void SetWaitingForStartCursor(bool isWaiting)
        {
            if (!unlockCursorBeforeStart)
            {
                return;
            }

            Cursor.lockState = isWaiting ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = isWaiting;
        }

        private IEnumerator SendFirstSpeakerPrompt()
        {
            yield return new WaitForSeconds(firstSpeakerDelaySeconds);
            firstSpeaker.SendTextDataNPC2NPC(FormatDebateText(firstSpeakerPrompt));
        }

        private void PlayRefereeClip(AudioClip clip)
        {
            if (refereeAudioSource == null || clip == null)
            {
                return;
            }

            refereeAudioSource.Stop();
            refereeAudioSource.PlayOneShot(clip);
        }

        private float GetCaptionDelay(float configuredSeconds, AudioClip clip)
        {
            if (clip == null)
            {
                return configuredSeconds;
            }

            return Mathf.Max(configuredSeconds, clip.length);
        }

        private void ShowRefereeCaption(string message)
        {
            if (refereeCaptionText == null)
            {
                return;
            }

            SetRefereeCaptionVisible(true);
            refereeCaptionText.gameObject.SetActive(true);
            refereeCaptionText.text = message;
        }

        private void HideRefereeCaption()
        {
            if (refereeCaptionText == null)
            {
                return;
            }

            refereeCaptionText.text = string.Empty;
            SetRefereeCaptionVisible(false);
        }

        private void SetRefereeCaptionVisible(bool visible)
        {
            GameObject captionRoot = refereeCaptionText.transform.parent != null
                ? refereeCaptionText.transform.parent.gameObject
                : refereeCaptionText.gameObject;

            captionRoot.SetActive(visible);
        }

        private void UpdateTimer(float seconds)
        {
            if (timerText == null)
            {
                return;
            }

            int totalSeconds = Mathf.CeilToInt(seconds);
            int minutes = totalSeconds / 60;
            int remainingSeconds = totalSeconds % 60;
            timerText.text = $"{timerPrefix}{minutes:00}:{remainingSeconds:00}";
        }

        private string FormatDebateText(string template)
        {
            if (string.IsNullOrEmpty(template))
            {
                return string.Empty;
            }

            return template.Contains("{0}")
                ? string.Format(template, debateTopic)
                : template;
        }
    }
}
