using System;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Game.Debate
{
    /// <summary>
    /// The Scene 03 oral-practice surface records only through iFlytek. Learner speech is never
    /// sent to a Convai NPC from this controller.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerOralPracticeController : MonoBehaviour
    {
        private const float DefaultPracticeDurationSeconds = 180f;

        private DebateRoundManager _roundManager;
        private XfyunRealtimeTranscriber _realtimeTranscriber;
        private GameObject _practiceRoot;
        private TMP_Text _countdownText;
        private TMP_Text _statusText;
        private Button _continueButton;
        private bool _eventsSubscribed;
        private bool _practiceActive;
        private bool _voiceSessionActive;
        private bool _voiceFinalizing;
        private string _latestTranscript = string.Empty;
        private string _completedTranscript = string.Empty;
        private string _researchAttemptId = string.Empty;
        private float _researchRecordingStartedAt;
        private int _researchAttemptCount;
        private int _researchConfirmedTranscriptCount;
        private int _researchAudioArtifactCount;
        private bool _completionDataComplete;
        private bool _localAttemptStarted;
        private bool _researchSessionActiveForAttempt;
        private bool _researchAttemptLogged;
        private bool _hasValidSavedAttempt;
        private bool _timeoutReached;
        private bool _finishWhenCurrentAttemptEnds;

        public float RemainingSeconds { get; private set; }
        public bool IsPracticeActive => _practiceActive;

        private void Awake()
        {
            _roundManager = FindFirstObjectByType<DebateRoundManager>();
            _realtimeTranscriber = GetComponent<XfyunRealtimeTranscriber>();
            BuildPracticeUi();
        }

        private void OnEnable()
        {
            _roundManager ??= FindFirstObjectByType<DebateRoundManager>();
            _realtimeTranscriber ??= GetComponent<XfyunRealtimeTranscriber>();
            if (_roundManager != null)
            {
                _roundManager.PlayerPracticeReady += BeginPractice;
            }

            SubscribeToTranscriber();
        }

        private void OnDisable()
        {
            if (_roundManager != null)
            {
                _roundManager.PlayerPracticeReady -= BeginPractice;
            }

            UnsubscribeFromTranscriber();
            StopAndPreserveCurrentTranscript();
            _roundManager?.SetPlayerPracticeInputSuppressed(false);
        }

        private void Update()
        {
            if (!_practiceActive)
            {
                return;
            }

            if (_roundManager != null && _roundManager.HasRoundEnded)
            {
                RequestPracticeCompletion();
                return;
            }

            if (WasVoiceHotkeyPressed() &&
                CanToggleRecordingFromKeyboard(_practiceActive, _voiceFinalizing))
            {
                ToggleVoiceRecording();
            }

            AdvancePractice(Time.deltaTime);
        }

        public void BeginPractice()
        {
            if (_practiceActive)
            {
                return;
            }

            _practiceActive = true;
            _voiceSessionActive = false;
            _voiceFinalizing = false;
            _latestTranscript = string.Empty;
            _completedTranscript = string.Empty;
            _researchAttemptCount = 0;
            _researchConfirmedTranscriptCount = 0;
            _researchAudioArtifactCount = 0;
            _completionDataComplete = false;
            _localAttemptStarted = false;
            _researchSessionActiveForAttempt = false;
            _researchAttemptLogged = false;
            _hasValidSavedAttempt = false;
            _timeoutReached = false;
            _finishWhenCurrentAttemptEnds = false;
            SetActive(_continueButton != null ? _continueButton.gameObject : null, false);
            RemainingSeconds = _roundManager != null
                ? Mathf.Max(0f, _roundManager.RoundDurationSeconds)
                : DefaultPracticeDurationSeconds;
            _roundManager?.SetPlayerPracticeInputSuppressed(true);
            SetActive(_practiceRoot, true);
            SetStatus("Press T to begin speaking.");
            UpdateCountdownText();
            ResearchCapture.RecordEvent("speech_task_started", "system", string.Empty, new
            {
                response_role = GetResearchResponseRole(),
                time_limit_seconds = RemainingSeconds
            });
        }

        public void ToggleVoiceRecording()
        {
            if (!_practiceActive || _realtimeTranscriber == null || _voiceFinalizing)
            {
                return;
            }

            if (_voiceSessionActive || _realtimeTranscriber.IsRecording || _realtimeTranscriber.IsConnecting)
            {
                if (_realtimeTranscriber.IsConnecting && !_realtimeTranscriber.IsRecording)
                {
                    _realtimeTranscriber.CancelSessionAndGetLatestTranscript();
                    _voiceSessionActive = false;
                    SetStatus("Voice connection cancelled. Press T to try again.");
                    return;
                }

                _voiceFinalizing = true;
                SetStatus("Finalizing your spoken argument...");
                _realtimeTranscriber.StopSession();
                return;
            }

            _voiceSessionActive = true;
            _voiceFinalizing = false;
            _latestTranscript = string.Empty;
            SetStatus("Connecting to iFlytek speech recognition...");
            _realtimeTranscriber.StartSession(string.Empty);
        }

        public static string FormatPracticeCountdown(float seconds)
        {
            int totalSeconds = Mathf.Max(0, Mathf.CeilToInt(seconds));
            return $"{totalSeconds / 60:00}:{totalSeconds % 60:00}";
        }

        public static bool CanToggleRecordingFromKeyboard(bool practiceActive, bool isFinalizing)
        {
            return practiceActive && !isFinalizing;
        }

        public static bool HasValidSavedAttempt(
            bool recordingStarted,
            string transcript,
            int pcmByteCount,
            bool requireResearchPersistence,
            bool attemptLogged,
            bool transcriptLogged,
            bool audioLogged)
        {
            bool localCaptureComplete = recordingStarted &&
                                        !string.IsNullOrWhiteSpace(transcript) &&
                                        pcmByteCount > 0;
            return localCaptureComplete &&
                   (!requireResearchPersistence ||
                    (attemptLogged && transcriptLogged && audioLogged));
        }

        public static bool CanFinishEarly(
            bool hasValidSavedAttempt,
            bool voiceSessionActive,
            bool voiceFinalizing)
        {
            return hasValidSavedAttempt && !voiceSessionActive && !voiceFinalizing;
        }

        public static bool ShouldRemainOpenAtTimeout(bool hasValidSavedAttempt)
        {
            return !hasValidSavedAttempt;
        }

        public static string AppendTranscriptEntry(string existingTranscript, string transcript)
        {
            string existing = existingTranscript?.TrimEnd() ?? string.Empty;
            string entry = transcript?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(entry))
            {
                return existing;
            }

            return string.IsNullOrWhiteSpace(existing)
                ? entry
                : existing + "\n\u2022 " + entry;
        }

        private void RequestPracticeCompletion()
        {
            if (!_practiceActive) return;
            if (_voiceSessionActive || _voiceFinalizing ||
                (_realtimeTranscriber != null &&
                 (_realtimeTranscriber.IsRecording || _realtimeTranscriber.IsConnecting)))
            {
                _finishWhenCurrentAttemptEnds = true;
                if (!_voiceFinalizing && _realtimeTranscriber != null &&
                    _realtimeTranscriber.IsRecording)
                {
                    _voiceFinalizing = true;
                    SetStatus("Time is complete. Finalizing your current recording...");
                    _realtimeTranscriber.StopSession();
                }
                return;
            }

            if (ShouldRemainOpenAtTimeout(_hasValidSavedAttempt))
            {
                _timeoutReached = true;
                SetStatus("Time is complete. Make and save one valid recording before continuing.");
                return;
            }

            CompletePractice("time_limit");
        }

        private void CompletePractice(string completionReason)
        {
            if (!_practiceActive)
            {
                return;
            }

            _practiceActive = false;
            RemainingSeconds = 0f;
            UpdateCountdownText();
            SetStatus("Three-minute speaking practice complete.");
            _roundManager?.SetPlayerPracticeInputSuppressed(false);
            string sceneId = GetResearchSceneId();
            bool noCoachVerified = sceneId != "05" ||
                                   !TransferDebateStageGuard.HasCoachContractViolation();
            bool requiresResearchPersistence = HasActiveResearchSession();
            int effectiveAttemptCount = requiresResearchPersistence
                ? _researchAttemptCount
                : (_hasValidSavedAttempt ? 1 : 0);
            int effectiveTranscriptCount = requiresResearchPersistence
                ? _researchConfirmedTranscriptCount
                : (_hasValidSavedAttempt ? 1 : 0);
            int effectiveAudioCount = requiresResearchPersistence
                ? _researchAudioArtifactCount
                : (_hasValidSavedAttempt ? 1 : 0);
            ResearchSceneCompletionStatus completion = ResearchSceneCompletionGate.EvaluateOral(
                sceneId,
                effectiveAttemptCount,
                effectiveTranscriptCount,
                effectiveAudioCount,
                noCoachVerified);
            ResearchCapture.RecordEvent(
                sceneId == "05" ? "transfer_completed" : "baseline_completed",
                "system",
                payload: new
                {
                    attempt_count = _researchAttemptCount,
                    confirmed_response_count = _researchConfirmedTranscriptCount,
                    audio_artifact_count = _researchAudioArtifactCount,
                    transcript_available = _researchConfirmedTranscriptCount > 0,
                    audio_available = _researchAudioArtifactCount > 0,
                    local_valid_saved_attempt = _hasValidSavedAttempt,
                    research_session_active = requiresResearchPersistence,
                    completion_reason = completionReason,
                    no_coach_verified = noCoachVerified,
                    completion_status = completion.DataComplete ? "completed" : "incomplete",
                    missing_required_data = completion.MissingRequiredData,
                    completed_at_utc = DateTimeOffset.UtcNow.ToString("O")
                });
            ResearchCapture.CompleteScene(
                completion.DataComplete ? "completed" : "incomplete",
                completion.MissingRequiredData);
            if (sceneId == "03")
            {
                _completionDataComplete = completion.DataComplete;
                ShowContinueButton(completion);
            }
        }

        public void ContinueToNextScene()
        {
            if (!_completionDataComplete)
            {
                return;
            }

            ResearchStudyFlowNavigator.TryLoadNextScene("03", true);
        }

        public void FinishEarlyAndContinue()
        {
            if (GetResearchSceneId() != "03" ||
                !CanFinishEarly(_hasValidSavedAttempt, _voiceSessionActive, _voiceFinalizing))
                return;

            ResearchCapture.RecordEvent("baseline_early_finish_selected", "learner", payload: new
            {
                remaining_seconds = RemainingSeconds,
                attempt_count = _researchAttemptCount,
                research_session_active = HasActiveResearchSession()
            });
            CompletePractice("early_finish");
            ContinueToNextScene();
        }

        private void HandlePrimaryAction()
        {
            if (_practiceActive)
            {
                FinishEarlyAndContinue();
                return;
            }
            ContinueToNextScene();
        }

        private void ShowContinueButton(ResearchSceneCompletionStatus completion)
        {
            if (_continueButton == null || completion == null)
            {
                return;
            }

            _continueButton.gameObject.SetActive(true);
            _continueButton.interactable = completion.DataComplete;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            TMP_Text label = _continueButton.GetComponentInChildren<TMP_Text>(true);
            if (completion.DataComplete)
            {
                SetStatus("Baseline speaking complete. Continue to the Coach practice scene.");
                if (label != null) label.text = "Continue to Scene 04  |  Coach Practice";
            }
            else
            {
                SetStatus("A valid recording, transcript, and audio file are required before continuing. Missing: " +
                          completion.MissingRequiredData);
                if (label != null) label.text = "Complete one valid recording to continue";
            }
        }

        private void AdvancePractice(float deltaTime)
        {
            if (!_practiceActive || (_roundManager != null && !_roundManager.IsRoundTimerActive))
            {
                return;
            }

            RemainingSeconds = Mathf.Max(0f, RemainingSeconds - Mathf.Max(0f, deltaTime));
            UpdateCountdownText();
            if (RemainingSeconds <= 0f)
            {
                RemainingSeconds = 0f;
                if (!_timeoutReached)
                {
                    _timeoutReached = true;
                    RequestPracticeCompletion();
                }
            }
        }

        private void StopAndPreserveCurrentTranscript()
        {
            if (_realtimeTranscriber == null)
            {
                return;
            }

            if (_voiceSessionActive || _voiceFinalizing || _realtimeTranscriber.IsRecording ||
                _realtimeTranscriber.IsConnecting)
            {
                string transcript = _realtimeTranscriber.CancelSessionAndGetLatestTranscript();
                CommitTranscript(transcript);
                CompleteResearchAttempt(transcript, "practice_ended");
            }

            _voiceSessionActive = false;
            _voiceFinalizing = false;
        }

        private void SubscribeToTranscriber()
        {
            if (_eventsSubscribed || _realtimeTranscriber == null)
            {
                return;
            }

            _realtimeTranscriber.SessionStarted += HandleTranscriptionStarted;
            _realtimeTranscriber.TranscriptUpdated += HandleTranscriptUpdated;
            _realtimeTranscriber.SessionCompleted += HandleTranscriptionCompleted;
            _realtimeTranscriber.SessionFailed += HandleTranscriptionFailed;
            _eventsSubscribed = true;
        }

        private void UnsubscribeFromTranscriber()
        {
            if (!_eventsSubscribed || _realtimeTranscriber == null)
            {
                return;
            }

            _realtimeTranscriber.SessionStarted -= HandleTranscriptionStarted;
            _realtimeTranscriber.TranscriptUpdated -= HandleTranscriptUpdated;
            _realtimeTranscriber.SessionCompleted -= HandleTranscriptionCompleted;
            _realtimeTranscriber.SessionFailed -= HandleTranscriptionFailed;
            _eventsSubscribed = false;
        }

        private void HandleTranscriptionStarted()
        {
            _roundManager?.NotifyPlayerSpeechStarted();
            _researchRecordingStartedAt = Time.realtimeSinceStartup;
            _localAttemptStarted = true;
            _researchSessionActiveForAttempt = HasActiveResearchSession();
            _researchAttemptId = ResearchCapture.BeginAttempt(GetResearchResponseRole());
            _researchAttemptLogged = !string.IsNullOrWhiteSpace(_researchAttemptId);
            if (_researchAttemptLogged) _researchAttemptCount++;
            if (_practiceActive)
            {
                SetStatus("Listening. Speak your argument, then press T to stop.");
            }
        }

        private void HandleTranscriptUpdated(string transcript)
        {
            if (!_practiceActive)
            {
                return;
            }

            _latestTranscript = transcript?.Trim() ?? string.Empty;
        }

        private void HandleTranscriptionCompleted(string transcript)
        {
            if (!_practiceActive)
            {
                return;
            }

            _voiceSessionActive = false;
            _voiceFinalizing = false;
            string confirmed = string.IsNullOrWhiteSpace(transcript) ? _latestTranscript : transcript;
            CommitTranscript(confirmed);
            _hasValidSavedAttempt |= CompleteResearchAttempt(confirmed, "transcription_completed");
            if (_finishWhenCurrentAttemptEnds)
            {
                _finishWhenCurrentAttemptEnds = false;
                RequestPracticeCompletion();
                return;
            }
            ShowSavedAttemptActions();
        }

        private void HandleTranscriptionFailed(string error)
        {
            if (!_practiceActive)
            {
                return;
            }

            _voiceSessionActive = false;
            _voiceFinalizing = false;
            ResearchCapture.StopAttempt(
                _researchAttemptId,
                CurrentResearchRecordingDuration(),
                "transcription_failed");
            ResearchCapture.SaveAudio(
                _researchAttemptId,
                GetResearchResponseRole(),
                _realtimeTranscriber?.LastAudioCapture);
            ResearchCapture.RecordTechnicalFailure(
                "asr_failure",
                "XFYUN_TRANSCRIPTION_FAILED",
                error ?? "Speech recognition failed.");
            _researchAttemptId = string.Empty;
            _finishWhenCurrentAttemptEnds = false;
            SetStatus("Speech recognition failed: " + error + " Press T to try again.");
        }

        private void CommitTranscript(string transcript)
        {
            string updated = AppendTranscriptEntry(_completedTranscript, transcript);
            if (string.Equals(updated, _completedTranscript, StringComparison.Ordinal))
            {
                return;
            }

            _completedTranscript = updated;
        }

        private bool CompleteResearchAttempt(string transcript, string stopReason)
        {
            float duration = CurrentResearchRecordingDuration();
            ResearchCapture.StopAttempt(_researchAttemptId, duration, stopReason);
            ResearchAudioArtifactRecord audio = ResearchCapture.SaveAudio(
                _researchAttemptId,
                GetResearchResponseRole(),
                _realtimeTranscriber?.LastAudioCapture);
            if (audio != null) _researchAudioArtifactCount++;
            ResearchTranscriptRecord transcriptRecord = ResearchCapture.ConfirmTranscript(
                _researchAttemptId,
                GetResearchResponseRole(),
                transcript,
                duration);
            if (transcriptRecord != null) _researchConfirmedTranscriptCount++;
            int pcmByteCount = _realtimeTranscriber?.LastAudioCapture?.Pcm16Bytes?.Length ?? 0;
            bool valid = HasValidSavedAttempt(
                _localAttemptStarted,
                transcript,
                pcmByteCount,
                _researchSessionActiveForAttempt,
                _researchAttemptLogged,
                transcriptRecord != null,
                audio != null);
            _researchAttemptId = string.Empty;
            _researchRecordingStartedAt = 0f;
            _localAttemptStarted = false;
            _researchSessionActiveForAttempt = false;
            _researchAttemptLogged = false;
            return valid;
        }

        private float CurrentResearchRecordingDuration()
        {
            return _researchRecordingStartedAt <= 0f
                ? 0f
                : Mathf.Max(0f, Time.realtimeSinceStartup - _researchRecordingStartedAt);
        }

        private static string GetResearchResponseRole()
        {
            if (GetResearchSceneId() == "05")
                return "transfer_speech";
            return "same_topic_baseline";
        }

        private static string GetResearchSceneId()
        {
            string sceneName = SceneManager.GetActiveScene().name;
            return ResearchSceneContract.TryResolve(
                sceneName,
                out string sceneId,
                out _,
                out _,
                out _)
                ? sceneId
                : string.Empty;
        }

        private static bool HasActiveResearchSession()
        {
            ResearchSessionManager manager = ResearchSessionManager.Instance != null
                ? ResearchSessionManager.Instance
                : FindAnyObjectByType<ResearchSessionManager>(FindObjectsInactive.Include);
            return manager != null && manager.HasActiveSession;
        }

        private static string GetDebateTopic()
        {
            return GetResearchSceneId() == "05"
                ? ResearchSceneContract.TransferTopic
                : ResearchSceneContract.PracticeTopic;
        }

        private void ShowSavedAttemptActions()
        {
            bool canFinish = GetResearchSceneId() == "03" &&
                             CanFinishEarly(_hasValidSavedAttempt,
                                 _voiceSessionActive, _voiceFinalizing);
            if (_continueButton != null)
            {
                _continueButton.gameObject.SetActive(canFinish);
                _continueButton.interactable = canFinish;
                TMP_Text label = _continueButton.GetComponentInChildren<TMP_Text>(true);
                if (label != null) label.text = "Finish Early & Continue to Scene 04";
            }

            if (_hasValidSavedAttempt && !HasActiveResearchSession())
                SetStatus("Saved for this debug run. Finish early or press T to add another part.");
            else if (_hasValidSavedAttempt)
                SetStatus("Saved. Finish early or press T to add another part of your argument.");
            else
                SetStatus("The response was captured but could not be saved completely. Press T to retry.");
        }

        private void UpdateCountdownText()
        {
            SetText(_countdownText, "Time Left " + FormatPracticeCountdown(RemainingSeconds));
        }

        private void BuildPracticeUi()
        {
            if (_practiceRoot != null)
            {
                return;
            }

            _practiceRoot = new GameObject(
                "Player Oral Practice Overlay",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(GraphicRaycaster));
            Canvas canvas = _practiceRoot.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 90;
            CanvasScaler scaler = _practiceRoot.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            GameObject panel = CreatePanel(_practiceRoot.transform);
            VerticalLayoutGroup layout = panel.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(36, 36, 30, 30);
            layout.spacing = 12f;
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            AddText(panel.transform, "Your 3-Minute Speaking Practice", 30, FontStyles.Bold, 46f);
            CreateTopicPanel(panel.transform, GetDebateTopic());
            _countdownText = AddText(panel.transform, string.Empty, 23, FontStyles.Bold, 32f);
            AddText(
                panel.transform,
                "Speak in English about the debate topic. Press T to start recording, then press T again to stop. Your speech is transcribed by iFlytek and is not sent to any NPC.",
                18,
                FontStyles.Normal,
                66f);
            _statusText = AddText(panel.transform, string.Empty, 18, FontStyles.Bold, 34f);
            _continueButton = AddButton(
                panel.transform,
                "Continue to Scene 04  |  Coach Practice",
                HandlePrimaryAction);
            _continueButton.gameObject.SetActive(false);

            _practiceRoot.SetActive(false);
        }

        private static GameObject CreatePanel(Transform parent)
        {
            GameObject panel = new("Practice Panel", typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(parent, false);
            RectTransform rect = panel.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(30f, -30f);
            rect.sizeDelta = new Vector2(700f, 480f);
            panel.GetComponent<Image>().color = new Color(0.035f, 0.055f, 0.08f, 0.96f);
            return panel;
        }

        private static void CreateTopicPanel(Transform parent, string topic)
        {
            GameObject panel = new("Debate Topic Panel", typeof(RectTransform), typeof(Image),
                typeof(LayoutElement), typeof(VerticalLayoutGroup));
            panel.transform.SetParent(parent, false);
            panel.GetComponent<Image>().color = new Color(0.035f, 0.20f, 0.34f, 0.96f);
            LayoutElement size = panel.GetComponent<LayoutElement>();
            size.minHeight = 92f;
            size.preferredHeight = 92f;
            VerticalLayoutGroup layout = panel.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(16, 16, 10, 10);
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            TMP_Text topicText = AddText(
                panel.transform,
                "DEBATE TOPIC\n" + (topic?.Trim() ?? string.Empty),
                18,
                FontStyles.Bold,
                72f);
            topicText.color = new Color(0.42f, 0.84f, 1f);
        }

        private static TMP_Text AddText(
            Transform parent,
            string value,
            int fontSize,
            FontStyles fontStyle,
            float minHeight)
        {
            GameObject textObject = new("Text", typeof(RectTransform), typeof(TextMeshProUGUI), typeof(LayoutElement));
            textObject.transform.SetParent(parent, false);
            TMP_Text text = textObject.GetComponent<TMP_Text>();
            text.text = value;
            text.fontSize = fontSize;
            text.fontStyle = fontStyle;
            text.color = Color.white;
            text.enableWordWrapping = true;
            text.alignment = TextAlignmentOptions.TopLeft;
            text.raycastTarget = false;
            textObject.GetComponent<LayoutElement>().minHeight = minHeight;
            return text;
        }

        private static Button AddButton(Transform parent, string label, UnityEngine.Events.UnityAction action)
        {
            GameObject buttonObject = new(
                label,
                typeof(RectTransform),
                typeof(Image),
                typeof(Button),
                typeof(LayoutElement));
            buttonObject.transform.SetParent(parent, false);
            buttonObject.GetComponent<Image>().color = new Color(0.08f, 0.52f, 0.70f, 1f);
            buttonObject.GetComponent<LayoutElement>().minHeight = 58f;
            Button button = buttonObject.GetComponent<Button>();
            if (action != null) button.onClick.AddListener(action);

            TMP_Text text = AddText(
                buttonObject.transform,
                label,
                20,
                FontStyles.Bold,
                0f);
            text.alignment = TextAlignmentOptions.Center;
            RectTransform textRect = text.rectTransform;
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(14f, 8f);
            textRect.offsetMax = new Vector2(-14f, -8f);
            return button;
        }

        private static void SetText(TMP_Text text, string value)
        {
            if (text != null)
            {
                text.text = value ?? string.Empty;
            }
        }

        private void SetStatus(string value)
        {
            SetText(_statusText, value);
        }

        private static void SetActive(GameObject target, bool active)
        {
            if (target != null)
            {
                target.SetActive(active);
            }
        }

        private static bool WasVoiceHotkeyPressed()
        {
#if ENABLE_INPUT_SYSTEM
            return Keyboard.current != null && Keyboard.current.tKey.wasPressedThisFrame;
#else
            return Input.GetKeyDown(KeyCode.T);
#endif
        }
    }
}
