using System;
using System.Collections;
using Convai.Scripts.Runtime.Features;
using Convai.Scripts.Runtime.Features.LipSync;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Debate
{
    public static class StructuredNpcDebatePlan
    {
        public const int TurnCount = 6;

        private static readonly string[] Strategies =
        {
            "Logos", "Logos", "Ethos", "Ethos", "Pathos", "Pathos"
        };

        public static string GetStrategyForTurn(int zeroBasedTurnIndex)
        {
            if (zeroBasedTurnIndex < 0 || zeroBasedTurnIndex >= TurnCount)
            {
                throw new ArgumentOutOfRangeException(nameof(zeroBasedTurnIndex));
            }

            return Strategies[zeroBasedTurnIndex];
        }

        public static string BuildTurnPrompt(
            string topic,
            string speakerName,
            string stance,
            string strategy,
            int turnNumber,
            string previousResponse)
        {
            string responseContext = string.IsNullOrWhiteSpace(previousResponse)
                ? "This is the opening argument."
                : $"The other speaker just said: \"{previousResponse.Trim()}\" Respond directly to that argument.";

            return
                $"Debate topic: {topic} " +
                $"You are {speakerName}, speaking on turn {turnNumber} of {TurnCount}. " +
                $"Your position is that {stance}. Use {strategy} as the main persuasive strategy. " +
                $"{responseContext} Give one concise CREEI response in this exact functional order: " +
                "Claim, Reason, Evidence, Explanation, Impact. " +
                "Speak at a calm, measured pace with a brief natural pause between each CREEI part. " +
                "Use clear English suitable for an English learner. Output only the debate response, " +
                "without labels, stage directions, or meta commentary.";
        }
    }

    public class NpcDebateRoundManager : MonoBehaviour
    {
        public const bool DefaultUseRoundTimer = true;

        [Header("Round")]
        [SerializeField] private bool startOnPlay = true;
        [SerializeField] private float roundDurationSeconds = 180f;
        [SerializeField] private bool useRoundTimer = DefaultUseRoundTimer;
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
        [SerializeField] private RefereePresentationController refereePresentation;

        [Header("Start Control")]
        [SerializeField] private Button startButton;
        [SerializeField] private bool unlockCursorBeforeStart = true;
        [SerializeField] private bool waitForExternalStart;

        [Header("Timer")]
        [SerializeField] private TMP_Text timerText;
        [SerializeField] private string timerPrefix = "Time Left ";

        [Header("Speech Bubbles")]
        [SerializeField] private Color speechBubbleBackgroundColor = new(0.08f, 0.08f, 0.08f, 0.72f);
        [SerializeField] private Color speechBubbleTextColor = new(0.96f, 0.97f, 0.98f, 1f);
        [SerializeField] private float speechBubbleLocalHeight = 2f;
        [SerializeField] private Vector2 speechBubbleSize = new(40f, 16f);

        [Header("NPC Debate")]
        [SerializeField] private NPC2NPCConversationManager conversationManager;
        [SerializeField] private ConvaiGroupNPCController firstSpeaker;
        [SerializeField] private float firstSpeakerDelaySeconds = 0.5f;
        [TextArea(2, 6)]
        [SerializeField]
        private string firstSpeakerPrompt =
            "The debate topic is: {0} You are the first speaker. Argue that reading is more important in learning English. Present your opening argument in under 20 seconds, then wait for the other NPC to respond.";

        [Header("Structured Six-Turn Debate")]
        [SerializeField] private bool useStructuredSixTurnDebate;
        [SerializeField] private bool useTutorialLipSyncDuringRound;
        [SerializeField] private float interTurnDelaySeconds = 1.5f;
        [SerializeField] private float speakerAudioStartGraceSeconds = 2f;
        [TextArea(2, 3)]
        [SerializeField]
        private string firstSpeakerStance = "interaction with others is more beneficial for developing English speaking skills";
        [TextArea(2, 3)]
        [SerializeField]
        private string secondSpeakerStance = "individual practice is more beneficial for developing English speaking skills";

        private Coroutine _roundRoutine;
        private Coroutine _speechBubbleStyleRoutine;
        private Coroutine _structuredTransitionRoutine;
        private float _remainingSeconds;
        private int _completedStructuredTurns;
        private Func<string, ConvaiGroupNPCController, bool> _previousRelayInterceptor;
        private bool _roundCompletionRaised;

        public bool IsRoundRunning { get; private set; }
        public bool HasRoundEnded { get; private set; }
        public bool WaitForExternalStart => waitForExternalStart;
        public bool UseRoundTimer => useRoundTimer;
        public event Action RoundCompleted;

        private void Start()
        {
            _speechBubbleStyleRoutine = StartCoroutine(ConfigureSpeechBubbleBackgrounds());
            HideRefereeCaption();
            SetTimerVisible(useRoundTimer);
            if (useRoundTimer)
            {
                UpdateTimer(roundDurationSeconds);
            }

            if (waitForExternalStart)
            {
                SetStartButtonVisible(false);
                SetWaitingForStartCursor(false);
                return;
            }

            SetStartButtonVisible(!startOnPlay);
            SetWaitingForStartCursor(!startOnPlay);

            if (startOnPlay)
            {
                BeginRound();
            }
        }

        public void SetWaitForExternalStart(bool wait)
        {
            waitForExternalStart = wait;
            if (waitForExternalStart && !IsRoundRunning)
            {
                SetStartButtonVisible(false);
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
            if (_speechBubbleStyleRoutine != null)
            {
                StopCoroutine(_speechBubbleStyleRoutine);
            }
            _speechBubbleStyleRoutine = StartCoroutine(ConfigureSpeechBubbleBackgrounds());
            if (_roundRoutine != null)
            {
                StopCoroutine(_roundRoutine);
            }

            _completedStructuredTurns = 0;
            _roundCompletionRaised = false;
            ActivateStructuredSubtitlePresentation();
            ConfigureRoundLipSync();
            _roundRoutine = StartCoroutine(RunRound());
        }

        private void ActivateStructuredSubtitlePresentation()
        {
            if (!useStructuredSixTurnDebate || conversationManager == null)
            {
                return;
            }

            ScreenDebateSubtitleController subtitleController =
                GetComponent<ScreenDebateSubtitleController>();
            if (subtitleController == null)
            {
                subtitleController = gameObject.AddComponent<ScreenDebateSubtitleController>();
            }

            subtitleController.ConfigureForStructuredDebate(conversationManager);
        }

        private void OnDestroy()
        {
            RestoreRelayInterceptor();
        }

        private IEnumerator RunRound()
        {
            IsRoundRunning = true;
            HasRoundEnded = false;
            _remainingSeconds = roundDurationSeconds;
            ConfigureStructuredRelay();

            ShowRefereeCaption(FormatDebateText(refereeOpeningLine));
            PlayRefereeClip(refereeOpeningClip);
            SetTimerVisible(useRoundTimer);
            if (useRoundTimer)
            {
                UpdateTimer(_remainingSeconds);
            }

            yield return new WaitForSeconds(GetCaptionDelay(openingCaptionSeconds, refereeOpeningClip));

            StartNpcOpening();

            if (!useRoundTimer)
            {
                if (!useStructuredSixTurnDebate)
                {
                    HideRefereeCaption();
                }

                while (IsRoundRunning)
                {
                    yield return null;
                }

                yield break;
            }

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
            RaiseRoundCompleted();
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

            if (useStructuredSixTurnDebate)
            {
                ShowStructuredTurnStatus(1, firstSpeaker);
                StartCoroutine(SendStructuredTurnPrompt(firstSpeaker, 0, string.Empty));
                return;
            }

            StartCoroutine(SendFirstSpeakerPrompt());
        }

        private void ConfigureStructuredRelay()
        {
            if (!useStructuredSixTurnDebate || conversationManager == null)
            {
                return;
            }

            _previousRelayInterceptor = conversationManager.RelayInterceptor;
            conversationManager.RelayInterceptor = InterceptStructuredRelay;
        }

        private void RestoreRelayInterceptor()
        {
            if (conversationManager != null && conversationManager.RelayInterceptor == InterceptStructuredRelay)
            {
                conversationManager.RelayInterceptor = _previousRelayInterceptor;
            }

            _previousRelayInterceptor = null;
        }

        private bool InterceptStructuredRelay(string message, ConvaiGroupNPCController sender)
        {
            if (!useStructuredSixTurnDebate || !IsRoundRunning || sender == null)
            {
                return false;
            }

            _completedStructuredTurns++;
            if (_structuredTransitionRoutine != null)
            {
                StopCoroutine(_structuredTransitionRoutine);
            }

            if (_completedStructuredTurns >= StructuredNpcDebatePlan.TurnCount)
            {
                _structuredTransitionRoutine = StartCoroutine(FinishStructuredDebateAfterSpeaker(sender));
            }
            else
            {
                _structuredTransitionRoutine = StartCoroutine(
                    ContinueStructuredDebateAfterSpeaker(sender, message));
            }

            return true;
        }

        private IEnumerator ContinueStructuredDebateAfterSpeaker(
            ConvaiGroupNPCController sender,
            string previousResponse)
        {
            yield return WaitForSpeakerToFinish(sender);
            yield return new WaitForSeconds(interTurnDelaySeconds);

            NPCGroup group = FindGroup(sender);
            if (group == null || !IsRoundRunning)
            {
                _structuredTransitionRoutine = null;
                yield break;
            }

            ConvaiGroupNPCController nextSpeaker = group.GroupNPC1 == sender
                ? group.GroupNPC2
                : group.GroupNPC1;
            group.CurrentSpeaker = nextSpeaker;
            int nextTurnIndex = _completedStructuredTurns;
            ShowStructuredTurnStatus(nextTurnIndex + 1, nextSpeaker);
            yield return SendStructuredTurnPrompt(nextSpeaker, nextTurnIndex, previousResponse);
            _structuredTransitionRoutine = null;
        }

        private IEnumerator FinishStructuredDebateAfterSpeaker(ConvaiGroupNPCController sender)
        {
            yield return WaitForSpeakerToFinish(sender);

            IsRoundRunning = false;
            HasRoundEnded = true;
            RestoreRelayInterceptor();

            if (conversationManager != null && firstSpeaker != null)
            {
                conversationManager.EndConversation(firstSpeaker);
            }

            ShowRefereeCaption(FormatDebateText(refereeClosingLine));
            PlayRefereeClip(refereeClosingClip);
            yield return new WaitForSeconds(GetCaptionDelay(closingCaptionSeconds, refereeClosingClip));
            HideRefereeCaption();
            _structuredTransitionRoutine = null;
            RaiseRoundCompleted();
        }

        private void RaiseRoundCompleted()
        {
            if (_roundCompletionRaised)
            {
                return;
            }

            _roundCompletionRaised = true;
            RoundCompleted?.Invoke();
        }

        private IEnumerator WaitForSpeakerToFinish(ConvaiGroupNPCController speaker)
        {
            if (speaker == null || speaker.ConvaiNPC == null)
            {
                yield break;
            }

            AudioSource source = speaker.GetComponent<AudioSource>();
            float graceRemaining = speakerAudioStartGraceSeconds;
            while (graceRemaining > 0f && !IsSpeakerPlaying(speaker, source))
            {
                graceRemaining -= Time.deltaTime;
                yield return null;
            }

            while (IsSpeakerPlaying(speaker, source))
            {
                yield return null;
            }
        }

        private static bool IsSpeakerPlaying(ConvaiGroupNPCController speaker, AudioSource source)
        {
            return (speaker.ConvaiNPC != null && speaker.ConvaiNPC.IsCharacterTalking) ||
                   (source != null && source.isPlaying);
        }

        private IEnumerator SendStructuredTurnPrompt(
            ConvaiGroupNPCController speaker,
            int turnIndex,
            string previousResponse)
        {
            yield return new WaitForSeconds(firstSpeakerDelaySeconds);
            if (!IsRoundRunning || speaker == null)
            {
                yield break;
            }

            string stance = speaker == firstSpeaker ? firstSpeakerStance : secondSpeakerStance;
            string strategy = StructuredNpcDebatePlan.GetStrategyForTurn(turnIndex);
            string prompt = StructuredNpcDebatePlan.BuildTurnPrompt(
                debateTopic,
                speaker.CharacterName,
                stance,
                strategy,
                turnIndex + 1,
                previousResponse);
            speaker.SendTextDataNPC2NPC(prompt);
        }

        private NPCGroup FindGroup(ConvaiGroupNPCController npc)
        {
            if (conversationManager == null || npc == null)
            {
                return null;
            }

            foreach (NPCGroup group in conversationManager.npcGroups)
            {
                if (group != null && group.BelongToGroup(npc))
                {
                    return group;
                }
            }

            return null;
        }

        private void ShowStructuredTurnStatus(int turnNumber, ConvaiGroupNPCController speaker)
        {
            string strategy = StructuredNpcDebatePlan.GetStrategyForTurn(turnNumber - 1);
            string speakerName = speaker != null ? speaker.CharacterName : "NPC";
            ShowRefereeCaption(
                $"Turn {turnNumber} / {StructuredNpcDebatePlan.TurnCount} | {speakerName}\n" +
                $"Strategy: {strategy}");
        }

        private void ConfigureRoundLipSync()
        {
            if (!useTutorialLipSyncDuringRound || conversationManager == null)
            {
                return;
            }

            foreach (NPCGroup group in conversationManager.npcGroups)
            {
                if (group == null)
                {
                    continue;
                }

                ConfigureRoundLipSync(group.GroupNPC1);
                ConfigureRoundLipSync(group.GroupNPC2);
            }
        }

        private static void ConfigureRoundLipSync(ConvaiGroupNPCController groupNpc)
        {
            if (groupNpc == null || groupNpc.ConvaiNPC == null)
            {
                return;
            }

            ConvaiLipSync convaiLipSync = groupNpc.GetComponent<ConvaiLipSync>();
            if (convaiLipSync != null)
            {
                convaiLipSync.StopLipSync();
                convaiLipSync.enabled = false;
            }

            foreach (ConvaiLipSyncApplicationBase lipSyncApplication in
                     groupNpc.GetComponents<ConvaiLipSyncApplicationBase>())
            {
                lipSyncApplication.ClearQueue();
                lipSyncApplication.enabled = false;
            }

            AudioSource source = groupNpc.GetComponent<AudioSource>();
            AudioDrivenNpcLipSync tutorialLipSync = groupNpc.GetComponent<AudioDrivenNpcLipSync>();
            if (tutorialLipSync == null)
            {
                tutorialLipSync = groupNpc.gameObject.AddComponent<AudioDrivenNpcLipSync>();
            }

            tutorialLipSync.Configure(source);
            tutorialLipSync.enabled = true;
        }

        private IEnumerator ConfigureSpeechBubbleBackgrounds()
        {
            const int maximumFrames = 60;
            for (int frame = 0; frame < maximumFrames; frame++)
            {
                int expected = 0;
                int styled = 0;
                if (conversationManager != null)
                {
                    foreach (NPCGroup group in conversationManager.npcGroups)
                    {
                        if (group == null)
                        {
                            continue;
                        }

                        expected += CountAssignedNpc(group.GroupNPC1) + CountAssignedNpc(group.GroupNPC2);
                        styled += ConfigureSpeechBubbleBackground(group.GroupNPC1);
                        styled += ConfigureSpeechBubbleBackground(group.GroupNPC2);
                    }
                }

                if (expected > 0 && styled >= expected)
                {
                    _speechBubbleStyleRoutine = null;
                    yield break;
                }

                yield return null;
            }

            _speechBubbleStyleRoutine = null;
        }

        private static int CountAssignedNpc(ConvaiGroupNPCController npc)
        {
            return npc == null ? 0 : 1;
        }

        private int ConfigureSpeechBubbleBackground(ConvaiGroupNPCController npc)
        {
            if (npc == null)
            {
                return 0;
            }

            NPCSpeechBubble bubble = npc.GetComponentInChildren<NPCSpeechBubble>(true);
            if (bubble == null)
            {
                return 0;
            }

            UnityEngine.UI.Image background = bubble.GetComponent<UnityEngine.UI.Image>();
            if (background != null)
            {
                background.color = speechBubbleBackgroundColor;
            }

            RectTransform bubbleRect = bubble.GetComponent<RectTransform>();
            if (bubbleRect != null)
            {
                bubbleRect.sizeDelta = speechBubbleSize;
                Vector3 localPosition = bubbleRect.localPosition;
                localPosition.y = speechBubbleLocalHeight;
                bubbleRect.localPosition = localPosition;
            }

            TMP_Text text = bubble.GetComponentInChildren<TMP_Text>(true);
            if (text != null)
            {
                text.color = speechBubbleTextColor;
            }

            return 1;
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
            if (refereePresentation != null)
            {
                refereePresentation.PlayClip(clip);
                return;
            }

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

        private void SetTimerVisible(bool visible)
        {
            if (timerText == null)
            {
                return;
            }

            timerText.text = visible ? timerText.text : string.Empty;
            timerText.gameObject.SetActive(visible);
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
