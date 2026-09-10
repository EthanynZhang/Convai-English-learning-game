using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Game.Debate
{
    public sealed class DebatePreparationController : MonoBehaviour
    {
        private const string EmptyNotesMessage = "No preparation notes.";

        [Header("Preparation Flow")]
        [SerializeField] private float preparationDurationSeconds = 180f;
        [SerializeField] private DebateRoundManager roundManager;
        [SerializeField] private XfyunRealtimeTranscriber realtimeTranscriber;
        [SerializeField] private string preferredMicrophoneDevice = string.Empty;
        [SerializeField] private Behaviour[] controlsToDisableDuringPreparation = Array.Empty<Behaviour>();

        [Header("Preparation UI")]
        [SerializeField] private GameObject preparationRoot;
        [SerializeField] private TMP_Text countdownText;
        [SerializeField] private TMP_InputField notesInput;
        [SerializeField] private TMP_Text voicePreviewText;
        [SerializeField] private TMP_Text voiceStatusText;
        [SerializeField] private Button microphoneButton;
        [SerializeField] private TMP_Text microphoneButtonLabel;
        [SerializeField] private Button skipPreparationButton;

        [Header("Debate UI")]
        [SerializeField] private GameObject roundTimerRoot;
        [SerializeField] private GameObject debateNotesTab;
        [SerializeField] private GameObject debateNotesPanel;
        [SerializeField] private TMP_Text debateNotesText;
        [SerializeField] private ScrollRect debateNotesScrollRect;

        private readonly Dictionary<Behaviour, bool> _controlEnabledStates = new();
        private bool _controlsCaptured;
        private bool _cursorCaptured;
        private CursorLockMode _cursorLockBeforePreparation;
        private bool _cursorVisibleBeforePreparation;
        private bool _eventsSubscribed;
        private bool _hasCompletedPreparation;
        private bool _resumePreparationOnEnable;
        private bool _voiceWasInterruptedBySuspend;
        private bool _voiceSessionActive;
        private bool _voiceFinalizing;
        private bool _voiceEntryCommitted;
        private string _latestVoiceTranscript = string.Empty;
        private string _notesText = string.Empty;
        private float _preparationElapsedSeconds;
        private GameObject _runtimeUiCanvas;
        private GameObject _sceneIntroductionRoot;
        private Button _confirmSceneIntroductionButton;
        private TMP_Text _preparationTopicText;

        public event Action PreparationCompleted;

        public bool IsPreparing { get; private set; }
        public float RemainingSeconds { get; private set; }
        public string NotesText => _notesText;

        private void Awake()
        {
            if (roundManager == null)
            {
                roundManager = FindFirstObjectByType<DebateRoundManager>();
            }

            if (realtimeTranscriber == null)
            {
                realtimeTranscriber = GetComponent<XfyunRealtimeTranscriber>();
            }

            BuildRuntimeUiIfNeeded();

            string sceneName = SceneManager.GetActiveScene().name;
            DebateSpeakerStation.EnsureForScene(sceneName);

            if (GetComponent<PlayerOralPracticeController>() == null)
            {
                gameObject.AddComponent<PlayerOralPracticeController>();
            }
        }

        private void OnEnable()
        {
            SubscribeToTranscriber();
            ConfigureUiListeners();
            if (_resumePreparationOnEnable && Application.isPlaying)
            {
                ResumePreparation();
            }
        }

        private void Start()
        {
            ShowSceneIntroduction();
        }

        private void Update()
        {
            if (IsPreparing)
            {
                HandlePreparationInput();
                AdvancePreparation(Time.unscaledDeltaTime);
                return;
            }

            if (!_hasCompletedPreparation)
            {
                return;
            }

            UpdateDebateNotesAvailability();
            if (roundManager != null && roundManager.HasRoundEnded)
            {
                return;
            }

            if (WasNotesHotkeyPressed())
            {
                ToggleDebateNotes();
            }

            HandleDebateNotesScrolling();
        }

        private void OnDisable()
        {
            bool introductionWasOpen = _sceneIntroductionRoot != null &&
                                       _sceneIntroductionRoot.activeSelf;
            if (IsPreparing)
            {
                SuspendPreparation();
            }

            if (introductionWasOpen)
            {
                _sceneIntroductionRoot.SetActive(false);
                roundManager?.SetPreparationInputSuppressed(false);
                RestorePlayerControls();
                RestoreCursorState();
            }

            RemoveUiListeners();
            UnsubscribeFromTranscriber();
            CancelVoiceSession();
            roundManager?.SetPreparationInputSuppressed(false);
            RestorePlayerControls();
            RestoreCursorState();
        }

        public void BeginPreparation()
        {
            if (IsPreparing)
            {
                return;
            }

            SubscribeToTranscriber();
            ConfigureUiListeners();
            SetActive(_sceneIntroductionRoot, false);
            CancelVoiceSession();
            roundManager?.SetPreparationInputSuppressed(true);

            _hasCompletedPreparation = false;
            _resumePreparationOnEnable = false;
            _voiceSessionActive = false;
            _voiceFinalizing = false;
            _voiceEntryCommitted = false;
            _latestVoiceTranscript = string.Empty;
            _notesText = string.Empty;
            _preparationElapsedSeconds = 0f;
            IsPreparing = true;
            RemainingSeconds = Mathf.Max(0f, preparationDurationSeconds);

            CaptureAndDisablePlayerControls();
            CaptureAndUnlockCursor();

            SetActive(preparationRoot, true);
            SetActive(roundTimerRoot, false);
            SetActive(debateNotesTab, false);
            SetActive(debateNotesPanel, false);

            if (notesInput != null)
            {
                notesInput.readOnly = false;
                notesInput.interactable = true;
                notesInput.SetTextWithoutNotify(string.Empty);
            }

            SetText(voicePreviewText, string.Empty);
            SetVoiceStatus("Type your notes, or press T to start a voice note.");
            UpdateCountdownText();

            if (RemainingSeconds <= 0f)
            {
                CompletePreparation();
            }
        }

        public static string GetScenePurpose(string sceneName)
        {
            string normalized = sceneName?.Trim() ?? string.Empty;
            if (normalized == "03Level_PlayerVsNPCDebate")
            {
                return "Give an individual baseline argument about whether individual practice or interaction with others is more beneficial for English speaking. No Coach support is provided in this scene.";
            }

            if (normalized == "05Level_PlayerVsNPCDebate 1")
            {
                return "Transfer what you learned to a new debate: classroom instruction versus real-life context for English speaking learning. This is an independent transfer assessment without Coach feedback.";
            }

            return string.Empty;
        }

        public static string FormatTopicLine(string topic)
        {
            string value = topic?.Trim() ?? string.Empty;
            return string.IsNullOrWhiteSpace(value)
                ? "DEBATE TOPIC\nNot available"
                : "DEBATE TOPIC\n" + value;
        }

        private void ShowSceneIntroduction()
        {
            if (_sceneIntroductionRoot == null)
            {
                BeginPreparation();
                return;
            }

            roundManager?.SetPreparationInputSuppressed(true);
            CaptureAndDisablePlayerControls();
            CaptureAndUnlockCursor();
            SetActive(preparationRoot, false);
            SetActive(_sceneIntroductionRoot, true);
        }

        private void ConfirmSceneIntroduction()
        {
            SetActive(_sceneIntroductionRoot, false);
            BeginPreparation();
        }

        public void ToggleVoiceRecording()
        {
            if (!IsPreparing || realtimeTranscriber == null || _voiceFinalizing)
            {
                return;
            }

            if (_voiceSessionActive || realtimeTranscriber.IsRecording || realtimeTranscriber.IsConnecting)
            {
                if (realtimeTranscriber.IsConnecting && !realtimeTranscriber.IsRecording)
                {
                    CancelVoiceSession();
                    SetVoiceStatus("Voice connection cancelled. Press T to try again.");
                    return;
                }

                _voiceFinalizing = true;
                SetVoiceStatus("Finalizing voice note...");
                SetMicrophoneButtonState(false, "Finalizing...");
                realtimeTranscriber.StopSession();
                return;
            }

            _voiceSessionActive = true;
            _voiceFinalizing = false;
            _voiceEntryCommitted = false;
            _latestVoiceTranscript = string.Empty;
            SetText(voicePreviewText, string.Empty);
            SetVoiceStatus("Connecting to English speech recognition...");
            SetMicrophoneButtonState(true, "Cancel voice note");
            realtimeTranscriber.StartSession(preferredMicrophoneDevice);
        }

        public void SkipPreparation()
        {
            if (!IsPreparing)
            {
                return;
            }

            CompletePreparation();
        }

        public void ToggleDebateNotes()
        {
            if (IsPreparing || !_hasCompletedPreparation || debateNotesPanel == null ||
                (roundManager != null && roundManager.HasRoundEnded))
            {
                return;
            }

            bool showPanel = !debateNotesPanel.activeSelf;
            debateNotesPanel.SetActive(showPanel);
            if (showPanel)
            {
                UpdateDebateNotesText();
                if (debateNotesScrollRect != null)
                {
                    Canvas.ForceUpdateCanvases();
                    debateNotesScrollRect.verticalNormalizedPosition = 1f;
                }
            }
        }

        public static string FormatCountdown(float seconds)
        {
            int totalSeconds = Mathf.Max(0, Mathf.CeilToInt(seconds));
            return $"{totalSeconds / 60:00}:{totalSeconds % 60:00}";
        }

        public static string AppendVoiceEntry(string existingNotes, string transcript)
        {
            string safeNotes = existingNotes?.TrimEnd() ?? string.Empty;
            string safeTranscript = transcript?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(safeTranscript))
            {
                return safeNotes;
            }

            string voiceEntry = "\u2022 " + safeTranscript;
            return string.IsNullOrWhiteSpace(safeNotes)
                ? voiceEntry
                : safeNotes + "\n" + voiceEntry;
        }

        public static bool CanToggleVoiceFromKeyboard(
            bool isPreparing,
            bool inputFieldFocused,
            bool isFinalizing)
        {
            return isPreparing && !inputFieldFocused && !isFinalizing;
        }

        private void HandlePreparationInput()
        {
            bool inputFocused = notesInput != null && notesInput.isFocused;
            if (inputFocused && WasEscapePressed())
            {
                notesInput.DeactivateInputField();
                EventSystem.current?.SetSelectedGameObject(null);
                return;
            }

            if (!inputFocused && WasSkipPreparationPressed())
            {
                SkipPreparation();
                return;
            }

            if (WasVoiceHotkeyPressed() &&
                CanToggleVoiceFromKeyboard(IsPreparing, inputFocused, _voiceFinalizing))
            {
                ToggleVoiceRecording();
            }
        }

        private void AdvancePreparation(float unscaledDeltaTime)
        {
            if (!IsPreparing)
            {
                return;
            }

            RemainingSeconds = Mathf.Max(0f, RemainingSeconds - Mathf.Max(0f, unscaledDeltaTime));
            _preparationElapsedSeconds += Mathf.Max(0f, unscaledDeltaTime);
            UpdateCountdownText();
            if (RemainingSeconds <= 0f)
            {
                CompletePreparation();
            }
        }

        private void CompletePreparation()
        {
            if (_hasCompletedPreparation)
            {
                return;
            }

            bool skipped = RemainingSeconds > 0f;
            _hasCompletedPreparation = true;
            _resumePreparationOnEnable = false;
            IsPreparing = false;
            RemainingSeconds = 0f;
            UpdateCountdownText();
            SyncNotesFromInput();

            if (_voiceSessionActive || _voiceFinalizing ||
                (realtimeTranscriber != null &&
                 (realtimeTranscriber.IsRecording || realtimeTranscriber.IsConnecting)))
            {
                CommitCurrentVoiceEntry(CancelVoiceSessionAndGetLatestTranscript());
            }

            _voiceSessionActive = false;
            _voiceFinalizing = false;
            SetMicrophoneButtonState(false, "Voice note ended");

            if (notesInput != null)
            {
                notesInput.DeactivateInputField();
                notesInput.readOnly = true;
                notesInput.interactable = false;
            }

            UpdateDebateNotesText();
            SetActive(preparationRoot, false);
            SetActive(roundTimerRoot, true);
            SetActive(debateNotesTab, true);
            SetActive(debateNotesPanel, false);
            RestorePlayerControls();
            RestoreCursorState();
            roundManager?.SetPreparationInputSuppressed(false);

            ResearchCapture.RecordEvent(
                "baseline_preparation_completed",
                "learner",
                payload: new
                {
                    notes_text = _notesText,
                    voice_preview_text = _latestVoiceTranscript,
                    preparation_duration_seconds = Mathf.Max(0f, _preparationElapsedSeconds),
                    completion_reason = skipped ? "learner_skipped" : "timer_elapsed"
                });

            PreparationCompleted?.Invoke();
            roundManager?.BeginRound();
        }

        private void CommitCurrentVoiceEntry(string transcript)
        {
            if (_voiceEntryCommitted)
            {
                return;
            }

            _voiceEntryCommitted = true;
            SyncNotesFromInput();
            _notesText = AppendVoiceEntry(_notesText, transcript);
            if (notesInput != null)
            {
                notesInput.SetTextWithoutNotify(_notesText);
            }

            UpdateDebateNotesText();
        }

        private void HandleTranscriptionStarted()
        {
            if (!IsPreparing)
            {
                return;
            }

            _voiceSessionActive = true;
            SetVoiceStatus("Recording English voice note. Press T again to stop.");
            SetMicrophoneButtonState(true, "Stop voice note");
        }

        private void HandleTranscriptUpdated(string transcript)
        {
            if (!IsPreparing || (!_voiceSessionActive && !_voiceFinalizing))
            {
                return;
            }

            _latestVoiceTranscript = transcript?.Trim() ?? string.Empty;
            SetText(
                voicePreviewText,
                string.IsNullOrWhiteSpace(_latestVoiceTranscript)
                    ? "Listening..."
                    : "Live: " + _latestVoiceTranscript);
        }

        private void HandleTranscriptionCompleted(string transcript)
        {
            if (!IsPreparing || (!_voiceSessionActive && !_voiceFinalizing))
            {
                return;
            }

            string finalTranscript = string.IsNullOrWhiteSpace(transcript)
                ? _latestVoiceTranscript
                : transcript;
            CommitCurrentVoiceEntry(finalTranscript);
            _voiceSessionActive = false;
            _voiceFinalizing = false;
            SetText(voicePreviewText, string.Empty);
            SetVoiceStatus(
                string.IsNullOrWhiteSpace(finalTranscript)
                    ? "No English speech was recognized. You can type or press T to retry."
                    : "Voice note added. Press T to record another one.");
            SetMicrophoneButtonState(false, "Start voice note");
        }

        private void HandleTranscriptionFailed(string error)
        {
            if (!_voiceSessionActive && !_voiceFinalizing)
            {
                return;
            }

            if (IsPreparing)
            {
                CommitCurrentVoiceEntry(_latestVoiceTranscript);
            }

            _voiceSessionActive = false;
            _voiceFinalizing = false;
            SetText(voicePreviewText, string.Empty);
            string safeError = string.IsNullOrWhiteSpace(error)
                ? "Voice transcription failed."
                : error.Trim();
            SetVoiceStatus(safeError + " Continue typing, or press T to retry.");
            SetMicrophoneButtonState(false, "Start voice note");
        }

        private void SuspendPreparation()
        {
            if (!IsPreparing)
            {
                return;
            }

            _voiceWasInterruptedBySuspend = _voiceSessionActive || _voiceFinalizing ||
                (realtimeTranscriber != null &&
                 (realtimeTranscriber.IsRecording || realtimeTranscriber.IsConnecting));
            if (_voiceWasInterruptedBySuspend)
            {
                CommitCurrentVoiceEntry(CancelVoiceSessionAndGetLatestTranscript());
            }

            _resumePreparationOnEnable = true;
            CancelVoiceSession();
            SetActive(preparationRoot, false);
            RestorePlayerControls();
            RestoreCursorState();
        }

        private void ResumePreparation()
        {
            if (!IsPreparing)
            {
                return;
            }

            _resumePreparationOnEnable = false;
            roundManager?.SetPreparationInputSuppressed(true);
            CaptureAndDisablePlayerControls();
            CaptureAndUnlockCursor();
            SetActive(preparationRoot, true);
            SetActive(roundTimerRoot, false);
            SetActive(debateNotesTab, false);
            SetActive(debateNotesPanel, false);
            SetText(voicePreviewText, string.Empty);
            SetVoiceStatus(
                _voiceWasInterruptedBySuspend
                    ? "Voice note preserved. Press T to start another one."
                    : "Type your notes, or press T to start a voice note.");
            SetMicrophoneButtonState(false, "Start voice note");
            _voiceWasInterruptedBySuspend = false;
        }

        private void HandleNotesChanged(string notes)
        {
            if (IsPreparing)
            {
                _notesText = notes ?? string.Empty;
            }
        }

        private void SubscribeToTranscriber()
        {
            if (_eventsSubscribed || realtimeTranscriber == null)
            {
                return;
            }

            realtimeTranscriber.SessionStarted += HandleTranscriptionStarted;
            realtimeTranscriber.TranscriptUpdated += HandleTranscriptUpdated;
            realtimeTranscriber.SessionCompleted += HandleTranscriptionCompleted;
            realtimeTranscriber.SessionFailed += HandleTranscriptionFailed;
            _eventsSubscribed = true;
        }

        private void UnsubscribeFromTranscriber()
        {
            if (!_eventsSubscribed || realtimeTranscriber == null)
            {
                return;
            }

            realtimeTranscriber.SessionStarted -= HandleTranscriptionStarted;
            realtimeTranscriber.TranscriptUpdated -= HandleTranscriptUpdated;
            realtimeTranscriber.SessionCompleted -= HandleTranscriptionCompleted;
            realtimeTranscriber.SessionFailed -= HandleTranscriptionFailed;
            _eventsSubscribed = false;
        }

        private void ConfigureUiListeners()
        {
            if (_confirmSceneIntroductionButton != null)
            {
                _confirmSceneIntroductionButton.onClick.RemoveListener(ConfirmSceneIntroduction);
                _confirmSceneIntroductionButton.onClick.AddListener(ConfirmSceneIntroduction);
            }

            if (microphoneButton != null)
            {
                microphoneButton.onClick.RemoveListener(ToggleVoiceRecording);
                microphoneButton.onClick.AddListener(ToggleVoiceRecording);
            }

            if (skipPreparationButton != null)
            {
                skipPreparationButton.onClick.RemoveListener(SkipPreparation);
                skipPreparationButton.onClick.AddListener(SkipPreparation);
            }

            if (notesInput != null)
            {
                notesInput.onValueChanged.RemoveListener(HandleNotesChanged);
                notesInput.onValueChanged.AddListener(HandleNotesChanged);
            }
        }

        private void RemoveUiListeners()
        {
            _confirmSceneIntroductionButton?.onClick.RemoveListener(ConfirmSceneIntroduction);
            microphoneButton?.onClick.RemoveListener(ToggleVoiceRecording);
            skipPreparationButton?.onClick.RemoveListener(SkipPreparation);
            notesInput?.onValueChanged.RemoveListener(HandleNotesChanged);
        }

        private void CaptureAndDisablePlayerControls()
        {
            if (_controlsCaptured)
            {
                return;
            }

            _controlEnabledStates.Clear();
            foreach (Behaviour control in controlsToDisableDuringPreparation)
            {
                if (control == null || _controlEnabledStates.ContainsKey(control))
                {
                    continue;
                }

                _controlEnabledStates.Add(control, control.enabled);
                control.enabled = false;
            }

            _controlsCaptured = true;
        }

        private void RestorePlayerControls()
        {
            if (!_controlsCaptured)
            {
                return;
            }

            foreach (KeyValuePair<Behaviour, bool> state in _controlEnabledStates)
            {
                if (state.Key != null)
                {
                    state.Key.enabled = state.Value;
                }
            }

            _controlEnabledStates.Clear();
            _controlsCaptured = false;
        }

        private void CaptureAndUnlockCursor()
        {
            if (!_cursorCaptured)
            {
                _cursorLockBeforePreparation = Cursor.lockState;
                _cursorVisibleBeforePreparation = Cursor.visible;
                _cursorCaptured = true;
            }

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        private void RestoreCursorState()
        {
            if (!_cursorCaptured)
            {
                return;
            }

            Cursor.lockState = _cursorLockBeforePreparation;
            Cursor.visible = _cursorVisibleBeforePreparation;
            _cursorCaptured = false;
        }

        private void CancelVoiceSession()
        {
            if (realtimeTranscriber != null &&
                (realtimeTranscriber.IsRecording || realtimeTranscriber.IsConnecting ||
                 _voiceSessionActive || _voiceFinalizing))
            {
                realtimeTranscriber.CancelSessionAndGetLatestTranscript();
            }

            _voiceSessionActive = false;
            _voiceFinalizing = false;
        }

        private string CancelVoiceSessionAndGetLatestTranscript()
        {
            string fallbackTranscript = GetLatestVoiceTranscriptSnapshot();
            string transcriberTranscript = realtimeTranscriber != null
                ? realtimeTranscriber.CancelSessionAndGetLatestTranscript()
                : string.Empty;
            _voiceSessionActive = false;
            _voiceFinalizing = false;
            return string.IsNullOrWhiteSpace(transcriberTranscript)
                ? fallbackTranscript
                : transcriberTranscript.Trim();
        }

        private void SyncNotesFromInput()
        {
            if (notesInput != null)
            {
                _notesText = notesInput.text ?? string.Empty;
            }
        }

        private string GetLatestVoiceTranscriptSnapshot()
        {
            string transcriberSnapshot = realtimeTranscriber?.LatestTranscriptSnapshot;
            return string.IsNullOrWhiteSpace(transcriberSnapshot)
                ? _latestVoiceTranscript
                : transcriberSnapshot.Trim();
        }

        private void UpdateDebateNotesAvailability()
        {
            if (!_hasCompletedPreparation || roundManager == null || !roundManager.HasRoundEnded)
            {
                return;
            }

            SetActive(debateNotesTab, false);
            SetActive(debateNotesPanel, false);
        }

        private void UpdateCountdownText()
        {
            SetText(countdownText, "Preparation " + FormatCountdown(RemainingSeconds));
        }

        private void UpdateDebateNotesText()
        {
            SetText(
                debateNotesText,
                string.IsNullOrWhiteSpace(_notesText) ? EmptyNotesMessage : _notesText);
        }

        private void SetVoiceStatus(string status)
        {
            SetText(voiceStatusText, status);
            SetMicrophoneButtonState(_voiceSessionActive, _voiceSessionActive ? "Stop voice note" : "Start voice note");
        }

        private void SetMicrophoneButtonState(bool isActive, string label)
        {
            if (microphoneButton != null)
            {
                microphoneButton.interactable = IsPreparing && !_voiceFinalizing;
                ColorBlock colors = microphoneButton.colors;
                colors.normalColor = isActive
                    ? new Color(0.72f, 0.22f, 0.24f, 1f)
                    : new Color(0.18f, 0.48f, 0.78f, 1f);
                colors.highlightedColor = isActive
                    ? new Color(0.84f, 0.28f, 0.30f, 1f)
                    : new Color(0.24f, 0.58f, 0.90f, 1f);
                microphoneButton.colors = colors;
            }

            SetText(microphoneButtonLabel, label);
        }

        private void HandleDebateNotesScrolling()
        {
            if (debateNotesPanel == null || !debateNotesPanel.activeSelf || debateNotesScrollRect == null)
            {
                return;
            }

            float scrollDelta = ReadScrollDelta();
            if (WasPageUpPressed())
            {
                scrollDelta = 1f;
            }
            else if (WasPageDownPressed())
            {
                scrollDelta = -1f;
            }

            if (Mathf.Abs(scrollDelta) > 0.001f)
            {
                debateNotesScrollRect.verticalNormalizedPosition = Mathf.Clamp01(
                    debateNotesScrollRect.verticalNormalizedPosition + Mathf.Sign(scrollDelta) * 0.16f);
            }
        }

        private void BuildRuntimeUiIfNeeded()
        {
            bool uiAlreadyAssigned = preparationRoot != null &&
                                     countdownText != null &&
                                     notesInput != null &&
                                     voicePreviewText != null &&
                                     voiceStatusText != null &&
                                     microphoneButton != null &&
                                     skipPreparationButton != null &&
                                     debateNotesTab != null &&
                                     debateNotesPanel != null &&
                                     debateNotesText != null;
            if (_runtimeUiCanvas != null)
            {
                return;
            }

            _runtimeUiCanvas = new GameObject(
                "Debate Preparation Runtime Canvas",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(GraphicRaycaster));
            Canvas canvas = _runtimeUiCanvas.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 80;
            CanvasScaler scaler = _runtimeUiCanvas.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            _sceneIntroductionRoot = CreateRuntimeRect(
                "Scene Task Introduction",
                _runtimeUiCanvas.transform,
                typeof(Image));
            StretchRuntimeRect(_sceneIntroductionRoot.GetComponent<RectTransform>(), 0f);
            _sceneIntroductionRoot.GetComponent<Image>().color =
                new Color(0.012f, 0.022f, 0.04f, 0.96f);

            GameObject introductionCard = CreateRuntimeRect(
                "Scene Task Card",
                _sceneIntroductionRoot.transform,
                typeof(Image),
                typeof(VerticalLayoutGroup));
            RectTransform introductionRect = introductionCard.GetComponent<RectTransform>();
            introductionRect.anchorMin = new Vector2(0.5f, 0.5f);
            introductionRect.anchorMax = new Vector2(0.5f, 0.5f);
            introductionRect.pivot = new Vector2(0.5f, 0.5f);
            introductionRect.anchoredPosition = Vector2.zero;
            introductionRect.sizeDelta = new Vector2(1120f, 760f);
            introductionCard.GetComponent<Image>().color =
                new Color(0.035f, 0.06f, 0.10f, 0.99f);
            VerticalLayoutGroup introductionLayout =
                introductionCard.GetComponent<VerticalLayoutGroup>();
            introductionLayout.padding = new RectOffset(56, 56, 44, 44);
            introductionLayout.spacing = 18f;
            introductionLayout.childControlWidth = true;
            introductionLayout.childControlHeight = true;
            introductionLayout.childForceExpandWidth = true;
            introductionLayout.childForceExpandHeight = false;

            string activeSceneName = SceneManager.GetActiveScene().name;
            string sceneNumber = activeSceneName == "05Level_PlayerVsNPCDebate 1"
                ? "SCENE 05  |  TRANSFER DEBATE"
                : "SCENE 03  |  BASELINE SPEAKING";
            TMP_Text introTitle = CreateRuntimeText(
                introductionCard.transform,
                sceneNumber,
                38,
                FontStyles.Bold,
                62f);
            introTitle.alignment = TextAlignmentOptions.Center;

            TMP_Text introTopic = CreateRuntimeText(
                introductionCard.transform,
                FormatTopicLine(roundManager?.DebateTopic),
                25,
                FontStyles.Bold,
                138f);
            introTopic.alignment = TextAlignmentOptions.Center;
            introTopic.color = new Color(0.42f, 0.84f, 1f);

            TMP_Text purpose = CreateRuntimeText(
                introductionCard.transform,
                GetScenePurpose(activeSceneName),
                22,
                FontStyles.Normal,
                142f);
            purpose.alignment = TextAlignmentOptions.Center;

            TMP_Text steps = CreateRuntimeText(
                introductionCard.transform,
                "First prepare your notes. Then press T to start speaking and press T again to finish. Your transcript and audio will be saved for research.",
                20,
                FontStyles.Normal,
                105f);
            steps.alignment = TextAlignmentOptions.Center;
            steps.color = new Color(0.80f, 0.88f, 0.95f);

            _confirmSceneIntroductionButton = CreateRuntimeButton(
                introductionCard.transform,
                "Confirm & Start Preparation",
                out _);
            _confirmSceneIntroductionButton.gameObject.AddComponent<LayoutElement>().minHeight = 72f;

            if (uiAlreadyAssigned)
            {
                TMP_Text[] existingLabels = preparationRoot.GetComponentsInChildren<TMP_Text>(true);
                foreach (TMP_Text label in existingLabels)
                {
                    if (label.gameObject.name != "Topic")
                    {
                        continue;
                    }

                    _preparationTopicText = label;
                    _preparationTopicText.text = FormatTopicLine(roundManager?.DebateTopic);
                    break;
                }

                _sceneIntroductionRoot.SetActive(false);
                return;
            }

            preparationRoot = CreateRuntimeRect(
                "Debate Preparation Overlay",
                _runtimeUiCanvas.transform,
                typeof(Image));
            StretchRuntimeRect(preparationRoot.GetComponent<RectTransform>(), 0f);
            preparationRoot.GetComponent<Image>().color = new Color(0.015f, 0.025f, 0.04f, 0.88f);

            GameObject card = CreateRuntimeRect(
                "Preparation Card",
                preparationRoot.transform,
                typeof(Image),
                typeof(VerticalLayoutGroup));
            RectTransform cardRect = card.GetComponent<RectTransform>();
            cardRect.anchorMin = new Vector2(0.5f, 0.5f);
            cardRect.anchorMax = new Vector2(0.5f, 0.5f);
            cardRect.pivot = new Vector2(0.5f, 0.5f);
            cardRect.anchoredPosition = Vector2.zero;
            cardRect.sizeDelta = new Vector2(1040f, 900f);
            card.GetComponent<Image>().color = new Color(0.035f, 0.055f, 0.08f, 0.98f);
            VerticalLayoutGroup cardLayout = card.GetComponent<VerticalLayoutGroup>();
            cardLayout.padding = new RectOffset(42, 42, 34, 34);
            cardLayout.spacing = 12f;
            cardLayout.childControlWidth = true;
            cardLayout.childControlHeight = true;
            cardLayout.childForceExpandWidth = true;
            cardLayout.childForceExpandHeight = false;

            string preparationTitle = SceneManager.GetActiveScene().name ==
                                      "05Level_PlayerVsNPCDebate 1"
                ? "Prepare Your Transfer Argument"
                : "Prepare Your Baseline Argument";
            CreateRuntimeText(
                card.transform,
                preparationTitle,
                36,
                FontStyles.Bold,
                52f);
            _preparationTopicText = CreateRuntimeText(
                card.transform,
                FormatTopicLine(roundManager?.DebateTopic),
                22,
                FontStyles.Bold,
                92f);
            _preparationTopicText.gameObject.name = "Preparation Topic";
            _preparationTopicText.alignment = TextAlignmentOptions.Center;
            _preparationTopicText.color = new Color(0.42f, 0.84f, 1f);
            countdownText = CreateRuntimeText(
                card.transform,
                string.Empty,
                28,
                FontStyles.Bold,
                42f);
            CreateRuntimeText(
                card.transform,
                "Write notes for your argument. Press T to start or stop an iFlytek voice note. Press Enter or use Skip Preparation when you are ready.",
                20,
                FontStyles.Normal,
                64f);
            notesInput = CreateRuntimeInput(card.transform);
            voicePreviewText = CreateRuntimeText(
                card.transform,
                string.Empty,
                18,
                FontStyles.Italic,
                62f);
            voiceStatusText = CreateRuntimeText(
                card.transform,
                string.Empty,
                18,
                FontStyles.Bold,
                36f);

            GameObject buttonRow = CreateRuntimeRect(
                "Preparation Actions",
                card.transform,
                typeof(HorizontalLayoutGroup),
                typeof(LayoutElement));
            LayoutElement buttonRowLayout = buttonRow.GetComponent<LayoutElement>();
            buttonRowLayout.minHeight = 64f;
            HorizontalLayoutGroup row = buttonRow.GetComponent<HorizontalLayoutGroup>();
            row.spacing = 16f;
            row.childControlWidth = true;
            row.childControlHeight = true;
            row.childForceExpandWidth = true;
            row.childForceExpandHeight = true;
            microphoneButton = CreateRuntimeButton(
                buttonRow.transform,
                "Start voice note",
                out microphoneButtonLabel);
            skipPreparationButton = CreateRuntimeButton(
                buttonRow.transform,
                "Skip Preparation",
                out _);

            debateNotesTab = CreateRuntimeRect(
                "Debate Notes Tab",
                _runtimeUiCanvas.transform,
                typeof(Image));
            RectTransform tabRect = debateNotesTab.GetComponent<RectTransform>();
            tabRect.anchorMin = Vector2.zero;
            tabRect.anchorMax = Vector2.zero;
            tabRect.pivot = Vector2.zero;
            tabRect.anchoredPosition = new Vector2(24f, 24f);
            tabRect.sizeDelta = new Vector2(220f, 56f);
            debateNotesTab.GetComponent<Image>().color = new Color(0.03f, 0.05f, 0.08f, 0.94f);
            TMP_Text tabLabel = CreateRuntimeText(
                debateNotesTab.transform,
                "N  Notes",
                22,
                FontStyles.Bold,
                0f);
            StretchRuntimeRect(tabLabel.rectTransform, 10f);
            tabLabel.alignment = TextAlignmentOptions.Center;

            debateNotesPanel = CreateRuntimeRect(
                "Debate Notes Panel",
                _runtimeUiCanvas.transform,
                typeof(Image));
            RectTransform notesPanelRect = debateNotesPanel.GetComponent<RectTransform>();
            notesPanelRect.anchorMin = new Vector2(0.5f, 0f);
            notesPanelRect.anchorMax = new Vector2(0.5f, 0f);
            notesPanelRect.pivot = new Vector2(0.5f, 0f);
            notesPanelRect.anchoredPosition = new Vector2(0f, 88f);
            notesPanelRect.sizeDelta = new Vector2(1100f, 340f);
            debateNotesPanel.GetComponent<Image>().color = new Color(0.025f, 0.04f, 0.065f, 0.97f);

            TMP_Text notesHeader = CreateRuntimeText(
                debateNotesPanel.transform,
                "Preparation Notes     N to close",
                24,
                FontStyles.Bold,
                0f);
            RectTransform headerRect = notesHeader.rectTransform;
            headerRect.anchorMin = new Vector2(0f, 1f);
            headerRect.anchorMax = new Vector2(1f, 1f);
            headerRect.pivot = new Vector2(0.5f, 1f);
            headerRect.offsetMin = new Vector2(28f, -58f);
            headerRect.offsetMax = new Vector2(-28f, -18f);

            CreateRuntimeNotesScroll(debateNotesPanel.transform);
            preparationRoot.SetActive(false);
            debateNotesTab.SetActive(false);
            debateNotesPanel.SetActive(false);
        }

        private TMP_InputField CreateRuntimeInput(Transform parent)
        {
            GameObject root = CreateRuntimeRect(
                "Preparation Notes Input",
                parent,
                typeof(Image),
                typeof(TMP_InputField),
                typeof(LayoutElement));
            root.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.09f);
            root.GetComponent<LayoutElement>().minHeight = 280f;

            GameObject viewport = CreateRuntimeRect(
                "Text Area",
                root.transform,
                typeof(RectMask2D));
            RectTransform viewportRect = viewport.GetComponent<RectTransform>();
            StretchRuntimeRect(viewportRect, 14f);

            TMP_Text text = CreateRuntimeText(
                viewport.transform,
                string.Empty,
                21,
                FontStyles.Normal,
                0f);
            StretchRuntimeRect(text.rectTransform, 0f);
            text.alignment = TextAlignmentOptions.TopLeft;
            text.richText = false;

            TMP_Text placeholder = CreateRuntimeText(
                viewport.transform,
                "Write your claim, reason, evidence, explanation, and impact here...",
                21,
                FontStyles.Italic,
                0f);
            StretchRuntimeRect(placeholder.rectTransform, 0f);
            placeholder.color = new Color(1f, 1f, 1f, 0.42f);
            placeholder.alignment = TextAlignmentOptions.TopLeft;

            TMP_InputField input = root.GetComponent<TMP_InputField>();
            input.textViewport = viewportRect;
            input.textComponent = text;
            input.placeholder = placeholder;
            input.lineType = TMP_InputField.LineType.MultiLineNewline;
            return input;
        }

        private void CreateRuntimeNotesScroll(Transform parent)
        {
            GameObject scrollObject = CreateRuntimeRect(
                "Notes Scroll View",
                parent,
                typeof(ScrollRect));
            RectTransform scrollRect = scrollObject.GetComponent<RectTransform>();
            scrollRect.anchorMin = Vector2.zero;
            scrollRect.anchorMax = Vector2.one;
            scrollRect.offsetMin = new Vector2(28f, 24f);
            scrollRect.offsetMax = new Vector2(-28f, -72f);

            GameObject viewport = CreateRuntimeRect(
                "Viewport",
                scrollObject.transform,
                typeof(RectMask2D));
            RectTransform viewportRect = viewport.GetComponent<RectTransform>();
            StretchRuntimeRect(viewportRect, 0f);

            GameObject content = CreateRuntimeRect(
                "Content",
                viewport.transform,
                typeof(VerticalLayoutGroup),
                typeof(ContentSizeFitter));
            RectTransform contentRect = content.GetComponent<RectTransform>();
            contentRect.anchorMin = new Vector2(0f, 1f);
            contentRect.anchorMax = new Vector2(1f, 1f);
            contentRect.pivot = new Vector2(0.5f, 1f);
            contentRect.anchoredPosition = Vector2.zero;
            contentRect.sizeDelta = Vector2.zero;
            VerticalLayoutGroup contentLayout = content.GetComponent<VerticalLayoutGroup>();
            contentLayout.padding = new RectOffset(8, 8, 4, 4);
            contentLayout.childControlWidth = true;
            contentLayout.childControlHeight = true;
            contentLayout.childForceExpandWidth = true;
            contentLayout.childForceExpandHeight = false;
            content.GetComponent<ContentSizeFitter>().verticalFit =
                ContentSizeFitter.FitMode.PreferredSize;

            debateNotesText = CreateRuntimeText(
                content.transform,
                EmptyNotesMessage,
                22,
                FontStyles.Normal,
                0f);
            debateNotesText.richText = false;
            debateNotesText.enableWordWrapping = true;
            debateNotesText.rectTransform.anchorMin = new Vector2(0f, 1f);
            debateNotesText.rectTransform.anchorMax = new Vector2(1f, 1f);
            debateNotesText.rectTransform.pivot = new Vector2(0.5f, 1f);
            debateNotesText.rectTransform.offsetMin = new Vector2(8f, 0f);
            debateNotesText.rectTransform.offsetMax = new Vector2(-8f, 0f);

            debateNotesScrollRect = scrollObject.GetComponent<ScrollRect>();
            debateNotesScrollRect.viewport = viewportRect;
            debateNotesScrollRect.content = contentRect;
            debateNotesScrollRect.horizontal = false;
            debateNotesScrollRect.movementType = ScrollRect.MovementType.Clamped;
            _sceneIntroductionRoot.SetActive(false);
        }

        private static Button CreateRuntimeButton(
            Transform parent,
            string label,
            out TMP_Text labelText)
        {
            GameObject buttonObject = CreateRuntimeRect(
                label,
                parent,
                typeof(Image),
                typeof(Button));
            buttonObject.GetComponent<Image>().color = new Color(0.18f, 0.48f, 0.78f, 1f);
            Button button = buttonObject.GetComponent<Button>();
            labelText = CreateRuntimeText(
                buttonObject.transform,
                label,
                20,
                FontStyles.Bold,
                0f);
            StretchRuntimeRect(labelText.rectTransform, 8f);
            labelText.alignment = TextAlignmentOptions.Center;
            return button;
        }

        private static TMP_Text CreateRuntimeText(
            Transform parent,
            string value,
            int fontSize,
            FontStyles fontStyle,
            float minHeight)
        {
            GameObject textObject = CreateRuntimeRect(
                "Text",
                parent,
                typeof(TextMeshProUGUI),
                typeof(LayoutElement));
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

        private static GameObject CreateRuntimeRect(
            string name,
            Transform parent,
            params Type[] components)
        {
            Type[] types = new Type[components.Length + 1];
            types[0] = typeof(RectTransform);
            Array.Copy(components, 0, types, 1, components.Length);
            GameObject gameObject = new(name, types);
            gameObject.transform.SetParent(parent, false);
            return gameObject;
        }

        private static void StretchRuntimeRect(RectTransform rect, float padding)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(padding, padding);
            rect.offsetMax = new Vector2(-padding, -padding);
        }

        private static float ReadScrollDelta()
        {
#if ENABLE_INPUT_SYSTEM
            if (Mouse.current != null)
            {
                float delta = Mouse.current.scroll.ReadValue().y;
                if (Mathf.Abs(delta) > 0.001f)
                {
                    return delta;
                }
            }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            return Input.mouseScrollDelta.y;
#else
            return 0f;
#endif
        }

        private static bool WasVoiceHotkeyPressed()
        {
#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current != null && Keyboard.current.tKey.wasPressedThisFrame)
            {
                return true;
            }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetKeyDown(KeyCode.T);
#else
            return false;
#endif
        }

        private static bool WasSkipPreparationPressed()
        {
#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current != null &&
                (Keyboard.current.enterKey.wasPressedThisFrame ||
                 Keyboard.current.numpadEnterKey.wasPressedThisFrame))
            {
                return true;
            }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter);
#else
            return false;
#endif
        }

        private static bool WasNotesHotkeyPressed()
        {
#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current != null && Keyboard.current.nKey.wasPressedThisFrame)
            {
                return true;
            }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetKeyDown(KeyCode.N);
#else
            return false;
#endif
        }

        private static bool WasEscapePressed()
        {
#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                return true;
            }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetKeyDown(KeyCode.Escape);
#else
            return false;
#endif
        }

        private static bool WasPageUpPressed()
        {
#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current != null && Keyboard.current.pageUpKey.wasPressedThisFrame)
            {
                return true;
            }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetKeyDown(KeyCode.PageUp);
#else
            return false;
#endif
        }

        private static bool WasPageDownPressed()
        {
#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current != null && Keyboard.current.pageDownKey.wasPressedThisFrame)
            {
                return true;
            }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetKeyDown(KeyCode.PageDown);
#else
            return false;
#endif
        }

        private static void SetText(TMP_Text target, string value)
        {
            if (target != null)
            {
                target.text = value ?? string.Empty;
            }
        }

        private static void SetActive(GameObject target, bool active)
        {
            if (target != null)
            {
                target.SetActive(active);
            }
        }
    }
}
