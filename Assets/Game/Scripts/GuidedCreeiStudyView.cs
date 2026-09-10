using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Debate
{
    public sealed class GuidedCreeiStudyView : MonoBehaviour
    {
        public event Action<string, CoachOrchestrationMode> SessionStartRequested;
        public event Action ToggleRecordingRequested;
        public event Action<string> ConfirmTranscriptRequested;
        public event Action RerecordRequested;
        public event Action<string> LearnerRequestSubmitted;
        public event Action<string, string> SharedSuggestionSelected;
        public event Action ContinueRequested;
        public event Action BeginRevisionRequested;
        public event Action SafetySkipRequested;
        public event Action RetryRequested;
        public event Action TechnicalSkipRequested;

        public bool IsBuilt { get; private set; }
        public string TranscriptText => _transcriptInput?.text?.Trim() ?? string.Empty;
        public string LearnerRequestText => _requestInput?.text?.Trim() ?? string.Empty;
        public string CoachVoiceStatusText => _coachVoiceStatus?.text ?? string.Empty;
        public RectTransform RootRect => _root != null ? _root.GetComponent<RectTransform>() : null;
        public ScrollRect StudyScrollRect => _studyScrollRect;

        private GameObject _root;
        private GameObject _setupPanel;
        private GameObject _studyPanel;
        private ScrollRect _studyScrollRect;
        private TMP_InputField _participantInput;
        private TMP_Dropdown _modeDropdown;
        private TMP_Text _setupError;
        private TMP_Text _stageTitle;
        private TMP_Text _instruction;
        private TMP_Text _timer;
        private TMP_Text _status;
        private TMP_Text _feedback;
        private TMP_Text _coachVoiceStatus;
        private TMP_InputField _transcriptInput;
        private TMP_InputField _requestInput;
        private readonly Button[] _suggestionButtons = new Button[3];
        private readonly TMP_Text[] _suggestionLabels = new TMP_Text[3];
        private CoachSuggestion[] _suggestions = Array.Empty<CoachSuggestion>();
        private Button _recordButton;
        private Button _confirmButton;
        private Button _rerecordButton;
        private Button _askButton;
        private Button _continueButton;
        private Button _revisionButton;
        private Button _safetySkipButton;
        private Button _retryButton;
        private Button _technicalSkipButton;

        public void Build(Canvas canvas)
        {
            if (IsBuilt || canvas == null) return;

            _root = CreatePanel("Guided CREEI Study", canvas.transform, new Color(0.035f, 0.055f, 0.09f, 0.97f));
            _root.GetComponent<VerticalLayoutGroup>().enabled = false;
            ApplySetupLayout();

            _setupPanel = CreatePanel("Researcher Setup", _root.transform, Color.clear);
            Stretch(_setupPanel.GetComponent<RectTransform>(), 32f);
            AddText(_setupPanel.transform, "Guided CREEI Coach Study", 34, FontStyles.Bold);
            AddText(_setupPanel.transform, "Enter an anonymous participant ID and assign one orchestration mode.", 19);
            _participantInput = AddInput(_setupPanel.transform, "Anonymous participant ID", false);
            _modeDropdown = AddDropdown(_setupPanel.transform, new[] { "Learner Led", "Shared Control", "AI Led" });
            _setupError = AddText(_setupPanel.transform, string.Empty, 17);
            _setupError.color = new Color(1f, 0.5f, 0.45f);
            AddButton(_setupPanel.transform, "Start Study", BeginSession);

            _studyScrollRect = CreateStudyScroll(_root.transform);
            _studyPanel = CreatePanel("Study", _studyScrollRect.viewport, Color.clear);
            RectTransform studyRect = _studyPanel.GetComponent<RectTransform>();
            studyRect.anchorMin = new Vector2(0f, 1f);
            studyRect.anchorMax = new Vector2(1f, 1f);
            studyRect.pivot = new Vector2(0.5f, 1f);
            studyRect.anchoredPosition = Vector2.zero;
            studyRect.sizeDelta = Vector2.zero;
            _studyPanel.GetComponent<VerticalLayoutGroup>().padding = new RectOffset(28, 28, 28, 28);
            ContentSizeFitter studyFitter = _studyPanel.AddComponent<ContentSizeFitter>();
            studyFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            studyFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            _studyScrollRect.content = studyRect;
            _stageTitle = AddText(_studyPanel.transform, "Claim", 30, FontStyles.Bold);
            _instruction = AddText(_studyPanel.transform, string.Empty, 18);
            _timer = AddText(_studyPanel.transform, string.Empty, 24, FontStyles.Bold);
            _status = AddText(_studyPanel.transform, string.Empty, 18);
            _transcriptInput = AddInput(_studyPanel.transform, "Confirmed transcript", true);
            _transcriptInput.readOnly = true;
            _requestInput = AddInput(_studyPanel.transform, "Describe what you want Coach to check", false);
            for (int index = 0; index < _suggestionButtons.Length; index++)
            {
                int captured = index;
                _suggestionButtons[index] = AddButton(_studyPanel.transform, "Suggestion " + (index + 1), () => SelectSuggestion(captured));
                _suggestionButtons[index].name = "Suggestion " + (index + 1);
                _suggestionLabels[index] = _suggestionButtons[index].GetComponentInChildren<TMP_Text>();
            }
            _feedback = AddText(_studyPanel.transform, string.Empty, 19);
            _feedback.gameObject.name = "Coach Feedback";
            _coachVoiceStatus = AddText(_studyPanel.transform, string.Empty, 16);
            _coachVoiceStatus.gameObject.name = "Coach Voice Status";
            _coachVoiceStatus.gameObject.SetActive(false);
            _recordButton = AddButton(_studyPanel.transform, "Start / Stop Recording (T)", () => ToggleRecordingRequested?.Invoke());
            _confirmButton = AddButton(_studyPanel.transform, "Confirm Transcript", () => ConfirmTranscriptRequested?.Invoke(TranscriptText));
            _rerecordButton = AddButton(_studyPanel.transform, "Re-record", () => RerecordRequested?.Invoke());
            _askButton = AddButton(_studyPanel.transform, "Ask Coach", () => LearnerRequestSubmitted?.Invoke(LearnerRequestText));
            _continueButton = AddButton(_studyPanel.transform, "Next Stage", () => ContinueRequested?.Invoke());
            _revisionButton = AddButton(_studyPanel.transform, "Revise Response", () => BeginRevisionRequested?.Invoke());
            _safetySkipButton = AddButton(_studyPanel.transform, "Safety Skip", () => SafetySkipRequested?.Invoke());
            _retryButton = AddButton(_studyPanel.transform, "Retry", () => RetryRequested?.Invoke());
            _technicalSkipButton = AddButton(_studyPanel.transform, "Technical Skip", () => TechnicalSkipRequested?.Invoke());

            IsBuilt = true;
            ShowSetup();
        }

        public void ShowSetup(string error = "")
        {
            SetPanels(true, false);
            if (_setupError != null) _setupError.text = error ?? string.Empty;
        }

        public void ShowStage(GuidedPracticeStageKind stage, string instruction, string status, bool integrated)
        {
            SetPanels(false, true);
            ResetStudyScrollToTop();
            if (_stageTitle != null) _stageTitle.text = StageTitle(stage);
            if (_instruction != null) _instruction.text = instruction ?? string.Empty;
            if (_status != null) _status.text = status ?? string.Empty;
            if (_timer != null) _timer.gameObject.SetActive(integrated);
            SetTranscriptEditable(false);
            ShowControls(record: true);
        }

        public void ShowRecording(string transcript, float elapsedSeconds, bool integrated)
        {
            SetPanels(false, true);
            SetTranscript(transcript);
            SetTimer(elapsedSeconds, integrated);
            if (_status != null) _status.text = integrated
                ? "Speak for 60–90 seconds. Press T after 01:00 to submit."
                : "Recording. Press T when this CREEI stage is complete.";
            ShowControls(record: true);
        }

        public void ShowTranscriptConfirmation(string transcript, string status, bool editableFallback = false)
        {
            SetPanels(false, true);
            SetTranscript(transcript);
            SetTranscriptEditable(editableFallback);
            if (_status != null) _status.text = status ?? string.Empty;
            ShowControls(confirm: true, rerecord: true);
        }

        public void ShowLearnerChoice(string feedback, bool canAsk, bool canRevise, bool canContinue)
        {
            SetPanels(false, true);
            if (_feedback != null) _feedback.text = feedback ?? string.Empty;
            ShowControls(ask: canAsk, request: canAsk, revise: canRevise, next: canContinue, safety: true);
        }

        public void ShowSharedSuggestions(CoachSuggestion[] suggestions, string status, bool canContinue)
        {
            SetPanels(false, true);
            _suggestions = suggestions ?? Array.Empty<CoachSuggestion>();
            if (_status != null) _status.text = status ?? string.Empty;
            ShowControls(ask: true, request: true, next: canContinue, safety: true);
            for (int index = 0; index < _suggestionButtons.Length; index++)
            {
                bool active = index < _suggestions.Length;
                SetActive(_suggestionButtons[index], active);
                if (active && _suggestionLabels[index] != null)
                {
                    CoachSuggestion suggestion = _suggestions[index];
                    _suggestionLabels[index].text = $"{index + 1}. {suggestion.Focus}: {suggestion.ProblemDescription}\nGoal: {suggestion.ImprovementGoal}";
                }
            }
        }

        public void ShowWorking(string status)
        {
            SetPanels(false, true);
            if (_status != null) _status.text = status ?? string.Empty;
            ShowControls(safety: true);
        }

        public void ShowFeedback(string feedback, bool revisionRequired, bool canContinue)
        {
            SetPanels(false, true);
            if (_feedback != null) _feedback.text = feedback ?? string.Empty;
            if (_status != null) _status.text = revisionRequired
                ? "Coach feedback is ready. Revise this stage before continuing."
                : "Coach feedback is ready.";
            ShowControls(revise: true, next: canContinue, safety: true);
        }

        public void ShowTechnicalError(string error, bool allowFallbackTranscript)
        {
            SetPanels(false, true);
            if (_status != null) _status.text = error ?? "A technical error occurred.";
            SetTranscriptEditable(allowFallbackTranscript);
            ShowControls(confirm: allowFallbackTranscript, retry: true, technicalSkip: true, safety: true);
        }

        public void SetTimer(float seconds, bool integrated)
        {
            if (_timer == null) return;
            _timer.gameObject.SetActive(integrated);
            int shown = Mathf.Clamp(Mathf.FloorToInt(seconds), 0, 90);
            _timer.text = $"{shown / 60:00}:{shown % 60:00} / 01:30";
        }

        public void SetTranscript(string text)
        {
            if (_transcriptInput != null) _transcriptInput.SetTextWithoutNotify(text ?? string.Empty);
        }

        public void SetCoachSpeaking(bool speaking)
        {
            if (_coachVoiceStatus == null) return;
            _coachVoiceStatus.text = speaking ? "Coach speaking..." : string.Empty;
            _coachVoiceStatus.gameObject.SetActive(speaking);
        }

        public void HideAll()
        {
            if (_root != null) _root.SetActive(false);
        }

        private void BeginSession()
        {
            string participant = _participantInput?.text?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(participant))
            {
                ShowSetup("Participant ID is required.");
                return;
            }
            CoachOrchestrationMode mode = _modeDropdown?.value switch
            {
                1 => CoachOrchestrationMode.SharedControl,
                2 => CoachOrchestrationMode.AiLed,
                _ => CoachOrchestrationMode.LearnerLed
            };
            SessionStartRequested?.Invoke(participant, mode);
        }

        private void SelectSuggestion(int index)
        {
            if (index < 0 || index >= _suggestions.Length) return;
            SharedSuggestionSelected?.Invoke(_suggestions[index].SuggestionId, LearnerRequestText);
        }

        private void SetTranscriptEditable(bool editable)
        {
            if (_transcriptInput != null) _transcriptInput.readOnly = !editable;
        }

        private void ShowControls(bool record = false, bool confirm = false, bool rerecord = false,
            bool ask = false, bool request = false, bool next = false, bool revise = false,
            bool safety = false, bool retry = false, bool technicalSkip = false)
        {
            SetActive(_recordButton, record);
            SetActive(_confirmButton, confirm);
            SetActive(_rerecordButton, rerecord);
            SetActive(_askButton, ask);
            SetActive(_requestInput, request);
            SetActive(_continueButton, next);
            SetActive(_revisionButton, revise);
            SetActive(_safetySkipButton, safety);
            SetActive(_retryButton, retry);
            SetActive(_technicalSkipButton, technicalSkip);
            if (!request && _requestInput != null) _requestInput.SetTextWithoutNotify(string.Empty);
            if (!next)
            {
                for (int index = 0; index < _suggestionButtons.Length; index++) SetActive(_suggestionButtons[index], false);
            }
        }

        private void SetPanels(bool setup, bool study)
        {
            if (_root != null) _root.SetActive(true);
            if (setup) ApplySetupLayout();
            else if (study) ApplyStudyLayout();
            if (_setupPanel != null) _setupPanel.SetActive(setup);
            if (_studyScrollRect != null) _studyScrollRect.gameObject.SetActive(study);
            if (_studyPanel != null) _studyPanel.SetActive(study);
        }

        private void ApplySetupLayout()
        {
            if (RootRect == null) return;
            RootRect.anchorMin = new Vector2(0.5f, 0.5f);
            RootRect.anchorMax = new Vector2(0.5f, 0.5f);
            RootRect.pivot = new Vector2(0.5f, 0.5f);
            RootRect.anchoredPosition = Vector2.zero;
            RootRect.sizeDelta = new Vector2(940f, 700f);
        }

        private void ApplyStudyLayout()
        {
            if (RootRect == null) return;
            RectTransform parentRect = RootRect.parent as RectTransform;
            float parentHeight = parentRect != null ? parentRect.rect.height : 0f;
            RootRect.anchorMin = Vector2.up;
            RootRect.anchorMax = Vector2.up;
            RootRect.pivot = Vector2.up;
            RootRect.anchoredPosition = new Vector2(16f, -16f);
            RootRect.sizeDelta = new Vector2(560f, Mathf.Min(760f, Mathf.Max(0f, parentHeight - 32f)));
        }

        private void ResetStudyScrollToTop()
        {
            if (_studyScrollRect == null) return;
            _studyScrollRect.StopMovement();
            _studyScrollRect.verticalNormalizedPosition = 1f;
            if (_studyScrollRect.content == null) return;
            Vector2 contentPosition = _studyScrollRect.content.anchoredPosition;
            contentPosition.y = 0f;
            _studyScrollRect.content.anchoredPosition = contentPosition;
        }

        private static string StageTitle(GuidedPracticeStageKind stage) => stage switch
        {
            GuidedPracticeStageKind.IntegratedPracticeOne => "Integrated Practice 1",
            GuidedPracticeStageKind.IntegratedPracticeTwo => "Integrated Practice 2",
            _ => $"Guided CREEI: {stage}"
        };

        private static GameObject CreatePanel(string name, Transform parent, Color color)
        {
            GameObject panel = new(name, typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup));
            panel.transform.SetParent(parent, false);
            panel.GetComponent<Image>().color = color;
            VerticalLayoutGroup layout = panel.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(18, 18, 18, 18);
            layout.spacing = 8f;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            return panel;
        }

        private static ScrollRect CreateStudyScroll(Transform parent)
        {
            GameObject scrollObject = new("Study Scroll", typeof(RectTransform), typeof(ScrollRect));
            scrollObject.transform.SetParent(parent, false);
            Stretch(scrollObject.GetComponent<RectTransform>(), 0f);

            GameObject viewportObject = new("Viewport", typeof(RectTransform), typeof(RectMask2D));
            viewportObject.transform.SetParent(scrollObject.transform, false);
            RectTransform viewportRect = viewportObject.GetComponent<RectTransform>();
            Stretch(viewportRect, 0f);

            ScrollRect scrollRect = scrollObject.GetComponent<ScrollRect>();
            scrollRect.viewport = viewportRect;
            scrollRect.horizontal = false;
            scrollRect.vertical = true;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;
            return scrollRect;
        }

        private static TMP_Text AddText(Transform parent, string value, int size, FontStyles style = FontStyles.Normal)
        {
            GameObject go = new("Text", typeof(RectTransform), typeof(TextMeshProUGUI), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            TMP_Text text = go.GetComponent<TMP_Text>();
            text.text = value;
            text.fontSize = size;
            text.fontStyle = style;
            text.color = Color.white;
            text.enableWordWrapping = true;
            go.GetComponent<LayoutElement>().minHeight = size + 10f;
            return text;
        }

        private static Button AddButton(Transform parent, string label, Action action)
        {
            GameObject go = new(label, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = new Color(0.14f, 0.34f, 0.55f, 0.95f);
            go.GetComponent<LayoutElement>().minHeight = 44f;
            Button button = go.GetComponent<Button>();
            button.onClick.AddListener(() => action?.Invoke());
            TMP_Text text = AddText(go.transform, label, 17, FontStyles.Bold);
            Stretch(text.rectTransform, 8f);
            text.alignment = TextAlignmentOptions.Center;
            return button;
        }

        private static TMP_InputField AddInput(Transform parent, string placeholder, bool multiline)
        {
            GameObject root = new("Input", typeof(RectTransform), typeof(Image), typeof(TMP_InputField), typeof(LayoutElement));
            root.transform.SetParent(parent, false);
            root.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.1f);
            root.GetComponent<LayoutElement>().minHeight = multiline ? 140f : 46f;
            GameObject viewport = new("Text Area", typeof(RectTransform), typeof(RectMask2D));
            viewport.transform.SetParent(root.transform, false);
            RectTransform viewportRect = viewport.GetComponent<RectTransform>();
            viewportRect.anchorMin = Vector2.zero;
            viewportRect.anchorMax = Vector2.one;
            viewportRect.offsetMin = new Vector2(10f, 8f);
            viewportRect.offsetMax = new Vector2(multiline ? -30f : -10f, -8f);
            TMP_Text text = AddText(viewport.transform, string.Empty, 17);
            Stretch(text.rectTransform, 0f);
            text.alignment = multiline ? TextAlignmentOptions.TopLeft : TextAlignmentOptions.MidlineLeft;
            TMP_Text hint = AddText(viewport.transform, placeholder, 17);
            hint.color = new Color(1f, 1f, 1f, 0.45f);
            Stretch(hint.rectTransform, 0f);
            TMP_InputField input = root.GetComponent<TMP_InputField>();
            input.textComponent = text;
            input.textViewport = viewportRect;
            input.placeholder = hint;
            input.lineType = multiline ? TMP_InputField.LineType.MultiLineNewline : TMP_InputField.LineType.SingleLine;
            if (multiline) input.verticalScrollbar = CreateScrollbar(root.transform);
            return input;
        }

        private static Scrollbar CreateScrollbar(Transform parent)
        {
            GameObject root = new("Scrollbar Vertical", typeof(RectTransform), typeof(Image), typeof(Scrollbar));
            root.transform.SetParent(parent, false);
            RectTransform rect = root.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(1f, 0f);
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(1f, 0.5f);
            rect.offsetMin = new Vector2(-20f, 4f);
            rect.offsetMax = new Vector2(-4f, -4f);
            GameObject area = new("Sliding Area", typeof(RectTransform));
            area.transform.SetParent(root.transform, false);
            Stretch(area.GetComponent<RectTransform>(), 2f);
            GameObject handle = new("Handle", typeof(RectTransform), typeof(Image));
            handle.transform.SetParent(area.transform, false);
            Stretch(handle.GetComponent<RectTransform>(), 0f);
            handle.GetComponent<Image>().color = new Color(0.35f, 0.75f, 1f, 0.85f);
            Scrollbar scrollbar = root.GetComponent<Scrollbar>();
            scrollbar.handleRect = handle.GetComponent<RectTransform>();
            scrollbar.targetGraphic = handle.GetComponent<Image>();
            scrollbar.direction = Scrollbar.Direction.BottomToTop;
            scrollbar.size = 0.35f;
            return scrollbar;
        }

        private static TMP_Dropdown AddDropdown(Transform parent, string[] options)
        {
            GameObject go = new("Dropdown", typeof(RectTransform), typeof(Image), typeof(TMP_Dropdown), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.12f);
            go.GetComponent<LayoutElement>().minHeight = 48f;
            TMP_Dropdown dropdown = go.GetComponent<TMP_Dropdown>();
            dropdown.ClearOptions();
            dropdown.AddOptions(new System.Collections.Generic.List<string>(options));
            TMP_Text label = AddText(go.transform, options.Length > 0 ? options[0] : string.Empty, 18);
            Stretch(label.rectTransform, 12f);
            dropdown.captionText = label;

            GameObject template = new("Template", typeof(RectTransform), typeof(Image), typeof(ScrollRect));
            template.transform.SetParent(go.transform, false);
            RectTransform templateRect = template.GetComponent<RectTransform>();
            templateRect.anchorMin = new Vector2(0f, 0f);
            templateRect.anchorMax = new Vector2(1f, 0f);
            templateRect.pivot = new Vector2(0.5f, 1f);
            templateRect.anchoredPosition = new Vector2(0f, -4f);
            templateRect.sizeDelta = new Vector2(0f, 180f);
            template.GetComponent<Image>().color = new Color(0.05f, 0.07f, 0.11f, 0.99f);

            GameObject viewport = new("Viewport", typeof(RectTransform), typeof(Image), typeof(Mask));
            viewport.transform.SetParent(template.transform, false);
            RectTransform viewportRect = viewport.GetComponent<RectTransform>();
            Stretch(viewportRect, 2f);
            viewport.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.03f);
            viewport.GetComponent<Mask>().showMaskGraphic = false;

            GameObject content = new("Content", typeof(RectTransform));
            content.transform.SetParent(viewport.transform, false);
            RectTransform contentRect = content.GetComponent<RectTransform>();
            contentRect.anchorMin = new Vector2(0f, 1f);
            contentRect.anchorMax = new Vector2(1f, 1f);
            contentRect.pivot = new Vector2(0.5f, 1f);
            contentRect.anchoredPosition = Vector2.zero;
            contentRect.sizeDelta = new Vector2(0f, 42f);

            GameObject item = new("Item", typeof(RectTransform), typeof(Toggle));
            item.transform.SetParent(content.transform, false);
            RectTransform itemRect = item.GetComponent<RectTransform>();
            itemRect.anchorMin = new Vector2(0f, 0.5f);
            itemRect.anchorMax = new Vector2(1f, 0.5f);
            itemRect.sizeDelta = new Vector2(0f, 42f);

            GameObject itemBackground = new("Item Background", typeof(RectTransform), typeof(Image));
            itemBackground.transform.SetParent(item.transform, false);
            Stretch(itemBackground.GetComponent<RectTransform>(), 0f);
            Image itemBackgroundImage = itemBackground.GetComponent<Image>();
            itemBackgroundImage.color = new Color(0.16f, 0.28f, 0.42f, 0.9f);

            GameObject checkmark = new("Item Checkmark", typeof(RectTransform), typeof(Image));
            checkmark.transform.SetParent(item.transform, false);
            RectTransform checkRect = checkmark.GetComponent<RectTransform>();
            checkRect.anchorMin = new Vector2(0f, 0.5f);
            checkRect.anchorMax = new Vector2(0f, 0.5f);
            checkRect.sizeDelta = new Vector2(18f, 18f);
            checkRect.anchoredPosition = new Vector2(14f, 0f);
            Image checkImage = checkmark.GetComponent<Image>();
            checkImage.color = new Color(0.35f, 0.75f, 1f, 1f);

            TMP_Text itemLabel = AddText(item.transform, options.Length > 0 ? options[0] : string.Empty, 17);
            RectTransform itemLabelRect = itemLabel.rectTransform;
            itemLabelRect.anchorMin = Vector2.zero;
            itemLabelRect.anchorMax = Vector2.one;
            itemLabelRect.offsetMin = new Vector2(32f, 2f);
            itemLabelRect.offsetMax = new Vector2(-8f, -2f);
            itemLabel.alignment = TextAlignmentOptions.MidlineLeft;

            Toggle toggle = item.GetComponent<Toggle>();
            toggle.targetGraphic = itemBackgroundImage;
            toggle.graphic = checkImage;

            ScrollRect scrollRect = template.GetComponent<ScrollRect>();
            scrollRect.content = contentRect;
            scrollRect.viewport = viewportRect;
            scrollRect.horizontal = false;
            scrollRect.vertical = true;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;

            dropdown.template = templateRect;
            dropdown.itemText = itemLabel;
            template.SetActive(false);
            return dropdown;
        }

        private static void SetActive(Component component, bool active)
        {
            if (component != null) component.gameObject.SetActive(active);
        }

        private static void Stretch(RectTransform rect, float padding)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(padding, padding);
            rect.offsetMax = new Vector2(-padding, -padding);
        }
    }
}
