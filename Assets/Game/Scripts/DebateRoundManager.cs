using System;
using System.Collections;
using Convai.Scripts.Runtime.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Debate
{
    public class DebateRoundManager : MonoBehaviour
    {
        [Header("Round")]
        [SerializeField] private bool startOnPlay = true;
        [SerializeField] private float roundDurationSeconds = 180f;
        [SerializeField] private float openingCaptionSeconds = 2.5f;
        [SerializeField] private float closingCaptionSeconds = 5f;

        [Header("Debate Topic")]
        [SerializeField]
        private string debateTopic =
            "Reading and speaking, which is more important in learning English?";

        [Header("Referee")]
        [SerializeField] private TMP_Text refereeCaptionText;
        [TextArea(2, 4)]
        [SerializeField]
        private string refereeOpeningLine =
            "Welcome, everyone. Today's debate topic is: {0}";

        [SerializeField] private string refereeClosingLine = "Thank you, everyone. The debate is over.";

        [Header("Referee Audio")]
        [SerializeField] private AudioSource refereeAudioSource;
        [SerializeField] private AudioClip refereeOpeningClip;
        [SerializeField] private AudioClip refereeClosingClip;
        [SerializeField] private RefereePresentationController refereePresentation;

        [Header("Referee TTS Fallback")]
        [SerializeField] private bool useMiniMaxRefereeNarration;
        [SerializeField] private string refereeMiniMaxVoiceId = MiniMaxTtsClient.MaleVoiceId;

        [Header("Start Control")]
        [SerializeField] private Button startButton;
        [SerializeField] private bool unlockCursorBeforeStart = true;

        [Header("Timer")]
        [SerializeField] private TMP_Text timerText;
        [SerializeField] private string timerPrefix = "Time Left ";
        [SerializeField] private bool waitForPlayerSpeechBeforeTimer;

        [Header("Opponent Opening")]
        [SerializeField] private ConvaiNPC opponentNPC;
        [SerializeField] private bool sendOpponentOpeningPrompt = true;
        [SerializeField] private bool disableOpponentConversation;
        [SerializeField] private bool waitForOpponentOpeningBeforePlayerPractice;
        [SerializeField] private float opponentOpeningTimeoutSeconds = 45f;
        [SerializeField] private float opponentOpeningDelaySeconds = 0.5f;
        [TextArea(2, 6)]
        [SerializeField]
        private string opponentOpeningPrompt =
            "The debate topic is: {0} You are the opposing side. Argue that reading is more important in learning English. Present your opening argument in under 20 seconds, then wait for the player to respond.";

        private Coroutine _roundRoutine;
        private float _remainingSeconds;
        private MiniMaxTtsClient _miniMaxTtsClient;
        private int _refereeSpeechGeneration;
        private bool _playerSpeechStarted;
        private bool _preparationInputSuppressed;
        private bool _playerPracticeInputSuppressed;

        public bool IsRoundRunning { get; private set; }
        public bool HasRoundEnded { get; private set; }
        public bool HasPlayerSpeechStarted => _playerSpeechStarted;
        public bool IsRoundTimerActive => ShouldAdvanceRoundTimer(
            waitForPlayerSpeechBeforeTimer,
            _playerSpeechStarted);
        public float RoundDurationSeconds => roundDurationSeconds;
        public string DebateTopic => debateTopic?.Trim() ?? string.Empty;
        public event Action OpeningCompleted;
        public event Action PlayerPracticeReady;

        public void SetRefereeCaptionTarget(TMP_Text captionText)
        {
            refereeCaptionText = captionText;
            HideRefereeCaption();
        }

        private void Awake()
        {
            if (useMiniMaxRefereeNarration)
            {
                _miniMaxTtsClient = GetComponent<MiniMaxTtsClient>();
                if (_miniMaxTtsClient == null)
                {
                    _miniMaxTtsClient = gameObject.AddComponent<MiniMaxTtsClient>();
                }
            }
        }

        private void OnEnable()
        {
            ConfigureOpponentConversationSuppression();
        }

        private void OnDisable()
        {
            _refereeSpeechGeneration++;
            ClearOpponentConversationSuppression();
        }

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

            DisableOpponentConversation();
            _playerSpeechStarted = false;
            _roundRoutine = StartCoroutine(RunRound());
        }

        private IEnumerator RunRound()
        {
            IsRoundRunning = true;
            HasRoundEnded = false;
            _remainingSeconds = roundDurationSeconds;

            string openingLine = FormatDebateText(refereeOpeningLine);
            AudioClip openingClip = refereeOpeningClip;
            ShowRefereeCaption(openingLine);
            yield return PlayRefereeOpeningLine(openingLine, clip => openingClip = clip);
            UpdateTimer(_remainingSeconds);

            yield return new WaitForSeconds(GetCaptionDelay(openingCaptionSeconds, openingClip));

            OpeningCompleted?.Invoke();

            bool sendOpeningPrompt = ShouldSendOpponentOpeningPrompt(
                    sendOpponentOpeningPrompt,
                    opponentNPC != null,
                    disableOpponentConversation);
            if (sendOpeningPrompt)
            {
                ConvaiNPCManager.Instance?.SetActiveConvaiNPC(opponentNPC);
                yield return new WaitForSeconds(opponentOpeningDelaySeconds);
                ConvaiNPCAudioManager audioManager = opponentNPC.GetComponent<ConvaiNPCAudioManager>();
                bool speechStarted = false;
                bool speechFinished = false;
                Action<bool> talkingChanged = talking =>
                {
                    if (talking)
                    {
                        speechStarted = true;
                    }
                    else if (speechStarted)
                    {
                        speechFinished = true;
                    }
                };

                if (waitForOpponentOpeningBeforePlayerPractice && audioManager != null)
                {
                    audioManager.OnCharacterTalkingChanged += talkingChanged;
                }

                opponentNPC.SendTextDataAsync(FormatDebateText(opponentOpeningPrompt));
                if (waitForOpponentOpeningBeforePlayerPractice && audioManager != null)
                {
                    float elapsed = 0f;
                    while (!speechFinished && elapsed < Mathf.Max(1f, opponentOpeningTimeoutSeconds))
                    {
                        elapsed += Time.unscaledDeltaTime;
                        yield return null;
                    }

                    audioManager.OnCharacterTalkingChanged -= talkingChanged;
                }
            }

            PlayerPracticeReady?.Invoke();

            while (!ShouldAdvanceRoundTimer(
                       waitForPlayerSpeechBeforeTimer,
                       _playerSpeechStarted))
            {
                yield return null;
            }

            while (_remainingSeconds > 0f)
            {
                _remainingSeconds -= Time.deltaTime;
                UpdateTimer(Mathf.Max(0f, _remainingSeconds));
                yield return null;
            }

            IsRoundRunning = false;
            HasRoundEnded = true;
            ShowRefereeCaption(FormatDebateText(refereeClosingLine));
            PlayRefereeClip(refereeClosingClip);
            UpdateTimer(0f);

            yield return new WaitForSeconds(GetCaptionDelay(closingCaptionSeconds, refereeClosingClip));
            HideRefereeCaption();
        }

        private void PlayRefereeClip(AudioClip clip)
        {
            if (clip == null)
            {
                return;
            }

            if (refereePresentation != null)
            {
                refereePresentation.PlayClip(clip);
                return;
            }

            if (refereeAudioSource == null)
            {
                return;
            }

            refereeAudioSource.Stop();
            refereeAudioSource.PlayOneShot(clip);
        }

        private IEnumerator PlayRefereeOpeningLine(string line, Action<AudioClip> onClipReady)
        {
            AudioClip clip = refereeOpeningClip;
            if (ShouldUseMiniMaxRefereeSpeech(useMiniMaxRefereeNarration, clip, line))
            {
                if (_miniMaxTtsClient == null)
                {
                    _miniMaxTtsClient = GetComponent<MiniMaxTtsClient>();
                }

                if (_miniMaxTtsClient != null)
                {
                    int generation = ++_refereeSpeechGeneration;
                    string failure = string.Empty;
                    yield return _miniMaxTtsClient.RequestClip(
                        line,
                        refereeMiniMaxVoiceId,
                        generation,
                        currentGeneration => currentGeneration == _refereeSpeechGeneration,
                        generatedClip => clip = generatedClip,
                        error => failure = error);

                    if (clip == null && !string.IsNullOrWhiteSpace(failure))
                    {
                        Debug.LogWarning("Referee MiniMax TTS did not play: " + failure);
                    }
                }
            }

            PlayRefereeClip(clip);
            onClipReady?.Invoke(clip);
        }

        public static bool ShouldUseMiniMaxRefereeSpeech(
            bool useMiniMaxNarration,
            AudioClip assignedClip,
            string line)
        {
            return useMiniMaxNarration && assignedClip == null && !string.IsNullOrWhiteSpace(line);
        }

        public static bool ShouldSendOpponentOpeningPrompt(
            bool openingPromptEnabled,
            bool hasOpponent,
            bool opponentConversationDisabled)
        {
            return openingPromptEnabled && hasOpponent && !opponentConversationDisabled;
        }

        public static bool ShouldAdvanceRoundTimer(
            bool waitForPlayerSpeech,
            bool playerSpeechStarted)
        {
            return !waitForPlayerSpeech || playerSpeechStarted;
        }

        public void NotifyPlayerSpeechStarted()
        {
            _playerSpeechStarted = true;
        }

        public void SetPreparationInputSuppressed(bool suppressed)
        {
            _preparationInputSuppressed = suppressed;
        }

        public void SetPlayerPracticeInputSuppressed(bool suppressed)
        {
            _playerPracticeInputSuppressed = suppressed;
        }

        private void ConfigureOpponentConversationSuppression()
        {
            ConvaiInputManager.ShouldSuppressTalkInput = ShouldSuppressOpponentConversation;
            ConvaiPlayerInteractionManager.TryHandleTextSubmission = SuppressOpponentTextSubmission;
            ConvaiPlayerInteractionManager.ShouldSuppressChatToggle = ShouldSuppressOpponentConversation;
            ConvaiPlayerInteractionManager.ShouldSuppressTalkInput = ShouldSuppressOpponentConversation;
            ConvaiPlayerInteractionManager.ShouldSuppressNpcInteraction = ShouldSuppressOpponentConversation;
        }

        private void ClearOpponentConversationSuppression()
        {
            if (ConvaiInputManager.ShouldSuppressTalkInput ==
                (Func<bool>)ShouldSuppressOpponentConversation)
            {
                ConvaiInputManager.ShouldSuppressTalkInput = null;
            }

            if (ConvaiPlayerInteractionManager.TryHandleTextSubmission ==
                (Func<string, bool>)SuppressOpponentTextSubmission)
            {
                ConvaiPlayerInteractionManager.TryHandleTextSubmission = null;
            }

            if (ConvaiPlayerInteractionManager.ShouldSuppressChatToggle ==
                (Func<bool>)ShouldSuppressOpponentConversation)
            {
                ConvaiPlayerInteractionManager.ShouldSuppressChatToggle = null;
            }

            if (ConvaiPlayerInteractionManager.ShouldSuppressTalkInput ==
                (Func<bool>)ShouldSuppressOpponentConversation)
            {
                ConvaiPlayerInteractionManager.ShouldSuppressTalkInput = null;
            }

            if (ConvaiPlayerInteractionManager.ShouldSuppressNpcInteraction ==
                (Func<bool>)ShouldSuppressOpponentConversation)
            {
                ConvaiPlayerInteractionManager.ShouldSuppressNpcInteraction = null;
            }
        }

        private bool ShouldSuppressOpponentConversation()
        {
            return disableOpponentConversation ||
                   _preparationInputSuppressed ||
                   _playerPracticeInputSuppressed;
        }

        private bool SuppressOpponentTextSubmission(string input)
        {
            return disableOpponentConversation ||
                   _preparationInputSuppressed ||
                   _playerPracticeInputSuppressed;
        }

        private void DisableOpponentConversation()
        {
            if (!disableOpponentConversation || opponentNPC == null)
            {
                return;
            }

            if (opponentNPC.IsCharacterTalking)
            {
                opponentNPC.InterruptCharacterSpeech();
            }

            opponentNPC.isCharacterActive = false;
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
