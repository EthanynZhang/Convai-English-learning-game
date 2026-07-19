using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Debate
{
    /// <summary>
    /// Presentation-only view for the researcher setup, solo practice, transcript
    /// confirmation and Coach episode controls. It reports intent through events;
    /// orchestration decisions stay in CoachEpisodeController.
    /// </summary>
    public sealed class CoachExperimentView : MonoBehaviour
    {
        public static readonly string[] FocusOptions = (string[])CoachFocusCatalog.Values.Clone();

        public event Action<string, CoachOrchestrationMode> SessionStartRequested;
        public event Action ConfirmTranscriptRequested;
        public event Action RerecordRequested;
        public event Action RetryDiagnosisRequested;
        public event Action SkipCycleRequested;
        public event Action<CoachLearnerAction, string> LearnerActionRequested;

        public bool IsBuilt => _root != null;
        public string SelectedFocus => _focusDropdown == null || _focusDropdown.value < 0 ||
                                       _focusDropdown.value >= FocusOptions.Length
            ? FocusOptions[0]
            : FocusOptions[_focusDropdown.value];
        public string TranscriptText => _transcript == null ? string.Empty : _transcript.text;

        private GameObject _root;
        private GameObject _setupPanel;
        private GameObject _practicePanel;
        private GameObject _coachPanel;
        private TMP_InputField _participantInput;
        private TMP_Dropdown _modeDropdown;
        private TMP_Dropdown _focusDropdown;
        private TMP_Text _setupError;
        private TMP_Text _practiceTitle;
        private TMP_Text _practiceTimer;
        private TMP_Text _practiceStatus;
        private TMP_InputField _transcript;
        private TMP_Text _coachStatus;
        private TMP_Text _coachFeedback;
        private Button _confirmButton;
        private Button _rerecordButton;
        private Button _retryButton;
        private Button _skipButton;
        private readonly System.Collections.Generic.Dictionary<CoachLearnerAction, Button> _coachButtons = new();

        public void Build(Canvas canvas)
        {
            if (_root != null || canvas == null)
            {
                return;
            }

            _root = CreatePanel("Coach Experiment", canvas.transform, new Color(0.035f, 0.055f, 0.09f, 0.96f));
            RectTransform rootRect = _root.GetComponent<RectTransform>();
            rootRect.anchorMin = new Vector2(0.5f, 0.5f);
            rootRect.anchorMax = new Vector2(0.5f, 0.5f);
            rootRect.pivot = new Vector2(0.5f, 0.5f);
            rootRect.sizeDelta = new Vector2(900f, 610f);
            rootRect.anchoredPosition = Vector2.zero;

            _setupPanel = CreatePanel("Researcher Setup", _root.transform, new Color(0f, 0f, 0f, 0f));
            Stretch(_setupPanel.GetComponent<RectTransform>(), 36f);
            AddText(_setupPanel.transform, "Coach Agent Study Setup", 34, FontStyles.Bold);
            AddText(_setupPanel.transform, "Enter an anonymous participant ID and assign one orchestration mode. The mode is hidden and locked after start.", 19);
            _participantInput = AddInput(_setupPanel.transform, "Anonymous participant ID", false);
            _modeDropdown = AddDropdown(_setupPanel.transform,
                new[] { "Learner Led", "Shared Control", "AI Led" });
            _setupError = AddText(_setupPanel.transform, string.Empty, 17);
            _setupError.color = new Color(1f, 0.5f, 0.45f);
            AddButton(_setupPanel.transform, "Start Session", BeginSession);

            _practicePanel = CreatePanel("Solo Practice", _root.transform, new Color(0f, 0f, 0f, 0f));
            Stretch(_practicePanel.GetComponent<RectTransform>(), 36f);
            _practiceTitle = AddText(_practicePanel.transform, "Practice Cycle", 30, FontStyles.Bold);
            _practiceTimer = AddText(_practicePanel.transform, "00:00 / 01:30", 28, FontStyles.Bold);
            _practiceStatus = AddText(_practicePanel.transform, string.Empty, 19);
            _transcript = AddInput(_practicePanel.transform, "Confirmed transcript", true);
            _transcript.readOnly = true;
            _confirmButton = AddButton(_practicePanel.transform, "Confirm Transcript", () => ConfirmTranscriptRequested?.Invoke());
            _rerecordButton = AddButton(_practicePanel.transform, "Re-record", () => RerecordRequested?.Invoke());
            _retryButton = AddButton(_practicePanel.transform, "Retry Diagnosis", () => RetryDiagnosisRequested?.Invoke());
            _skipButton = AddButton(_practicePanel.transform, "Skip Cycle", () => SkipCycleRequested?.Invoke());

            _coachPanel = CreatePanel("Coach Episode", _root.transform, new Color(0f, 0f, 0f, 0f));
            Stretch(_coachPanel.GetComponent<RectTransform>(), 36f);
            _coachStatus = AddText(_coachPanel.transform, "Coach", 24, FontStyles.Bold);
            _coachFeedback = AddText(_coachPanel.transform, string.Empty, 20);
            _focusDropdown = AddDropdown(_coachPanel.transform, FocusOptions);
            AddCoachButton("Ask Coach", CoachLearnerAction.RequestCoach);
            AddCoachButton("Confirm Focus", CoachLearnerAction.ConfirmFocus);
            AddCoachButton("Accept", CoachLearnerAction.Accept);
            AddCoachButton("Change Focus", CoachLearnerAction.ChangeFocus);
            AddCoachButton("Decline", CoachLearnerAction.Decline);
            AddCoachButton("Need Example", CoachLearnerAction.NeedExample);
            AddCoachButton("Replay", CoachLearnerAction.Replay);
            AddCoachButton("Apply Next Cycle", CoachLearnerAction.ApplyNextCycle);
            AddCoachButton("Override", CoachLearnerAction.Override);
            AddCoachButton("Exit Coaching", CoachLearnerAction.ExitCoaching);
            AddCoachButton("Retry Feedback", CoachLearnerAction.RetryFeedback);

            ShowSetup();
        }

        public void ShowSetup(string error = "")
        {
            SetPanels(true, false, false);
            if (_setupError != null) _setupError.text = error ?? string.Empty;
        }

        public void ShowPractice(int cycle, string status, float elapsedSeconds, string transcript,
            bool canConfirm, bool canRerecord, bool diagnosisFailed = false)
        {
            SetPanels(false, true, false);
            if (_practiceTitle != null) _practiceTitle.text = $"Solo Practice Cycle {cycle} of 3";
            if (_practiceTimer != null)
            {
                int seconds = Mathf.Clamp(Mathf.FloorToInt(elapsedSeconds), 0, 90);
                _practiceTimer.text = $"{seconds / 60:00}:{seconds % 60:00} / 01:30";
            }
            if (_practiceStatus != null) _practiceStatus.text = status ?? string.Empty;
            if (_transcript != null) _transcript.text = transcript ?? string.Empty;
            SetActive(_confirmButton, canConfirm);
            SetActive(_rerecordButton, canRerecord);
            SetActive(_retryButton, diagnosisFailed);
            SetActive(_skipButton, diagnosisFailed);
        }

        public void ShowCoach(string status, string feedback, bool showFocus,
            params CoachLearnerAction[] visibleActions)
        {
            SetPanels(false, false, true);
            if (_coachStatus != null) _coachStatus.text = status ?? string.Empty;
            if (_coachFeedback != null) _coachFeedback.text = feedback ?? string.Empty;
            if (_focusDropdown != null) _focusDropdown.gameObject.SetActive(showFocus);
            System.Collections.Generic.HashSet<CoachLearnerAction> visible =
                new(visibleActions ?? Array.Empty<CoachLearnerAction>());
            foreach (System.Collections.Generic.KeyValuePair<CoachLearnerAction, Button> pair in _coachButtons)
            {
                if (pair.Value != null) pair.Value.gameObject.SetActive(visible.Contains(pair.Key));
            }
        }

        public void SetTranscriptEditable(bool editable)
        {
            if (_transcript != null) _transcript.readOnly = !editable;
        }

        public void SetSelectedFocus(string focus)
        {
            if (_focusDropdown == null || string.IsNullOrWhiteSpace(focus))
            {
                return;
            }

            int index = Array.FindIndex(
                FocusOptions,
                option => string.Equals(option, focus.Trim(), StringComparison.OrdinalIgnoreCase));
            if (index >= 0)
            {
                _focusDropdown.SetValueWithoutNotify(index);
                _focusDropdown.RefreshShownValue();
            }
        }

        public void HideAll()
        {
            if (_root != null) _root.SetActive(false);
        }

        private void BeginSession()
        {
            string participant = _participantInput == null ? string.Empty : _participantInput.text.Trim();
            if (string.IsNullOrWhiteSpace(participant))
            {
                ShowSetup("Participant ID is required.");
                return;
            }

            CoachOrchestrationMode mode = _modeDropdown == null
                ? CoachOrchestrationMode.LearnerLed
                : _modeDropdown.value switch
                {
                    1 => CoachOrchestrationMode.SharedControl,
                    2 => CoachOrchestrationMode.AiLed,
                    _ => CoachOrchestrationMode.LearnerLed
                };
            SessionStartRequested?.Invoke(participant, mode);
        }

        private void AddCoachButton(string label, CoachLearnerAction action)
        {
            Button button = AddButton(_coachPanel.transform, label,
                () => LearnerActionRequested?.Invoke(action, SelectedFocus));
            _coachButtons[action] = button;
        }

        private void SetPanels(bool setup, bool practice, bool coach)
        {
            if (_root != null) _root.SetActive(true);
            if (_setupPanel != null) _setupPanel.SetActive(setup);
            if (_practicePanel != null) _practicePanel.SetActive(practice);
            if (_coachPanel != null) _coachPanel.SetActive(coach);
        }

        private static void SetActive(Component component, bool active)
        {
            if (component != null) component.gameObject.SetActive(active);
        }

        private static GameObject CreatePanel(string name, Transform parent, Color color)
        {
            GameObject panel = new(name, typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup));
            panel.transform.SetParent(parent, false);
            panel.GetComponent<Image>().color = color;
            VerticalLayoutGroup layout = panel.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(18, 18, 18, 18);
            layout.spacing = 10f;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            return panel;
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
            go.GetComponent<LayoutElement>().minHeight = size + 12f;
            return text;
        }

        private static TMP_InputField AddInput(Transform parent, string placeholder, bool multiline)
        {
            GameObject root = new("Input", typeof(RectTransform), typeof(Image), typeof(TMP_InputField), typeof(LayoutElement));
            root.transform.SetParent(parent, false);
            root.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.1f);
            root.GetComponent<LayoutElement>().minHeight = multiline ? 150f : 48f;

            GameObject viewport = new("Text Area", typeof(RectTransform), typeof(RectMask2D));
            viewport.transform.SetParent(root.transform, false);
            RectTransform viewportRect = viewport.GetComponent<RectTransform>();
            viewportRect.anchorMin = Vector2.zero;
            viewportRect.anchorMax = Vector2.one;
            viewportRect.offsetMin = new Vector2(10f, 8f);
            viewportRect.offsetMax = new Vector2(multiline ? -30f : -10f, -8f);

            TMP_Text text = AddText(viewport.transform, string.Empty, 18);
            RectTransform textRect = text.rectTransform;
            Stretch(textRect, 0f);
            text.alignment = multiline ? TextAlignmentOptions.TopLeft : TextAlignmentOptions.MidlineLeft;
            TMP_InputField input = root.GetComponent<TMP_InputField>();
            input.textComponent = text;
            input.textViewport = viewportRect;
            input.lineType = multiline ? TMP_InputField.LineType.MultiLineNewline : TMP_InputField.LineType.SingleLine;
            TMP_Text hint = AddText(viewport.transform, placeholder, 18);
            hint.color = new Color(1f, 1f, 1f, 0.45f);
            Stretch(hint.rectTransform, 0f);
            hint.alignment = multiline ? TextAlignmentOptions.TopLeft : TextAlignmentOptions.MidlineLeft;
            input.placeholder = hint;

            if (multiline)
            {
                input.verticalScrollbar = CreateVerticalScrollbar(root.transform);
                input.scrollSensitivity = 20f;
            }

            return input;
        }

        private static Scrollbar CreateVerticalScrollbar(Transform parent)
        {
            GameObject scrollbarObject = new("Scrollbar Vertical", typeof(RectTransform), typeof(Image), typeof(Scrollbar));
            scrollbarObject.transform.SetParent(parent, false);
            RectTransform scrollbarRect = scrollbarObject.GetComponent<RectTransform>();
            scrollbarRect.anchorMin = new Vector2(1f, 0f);
            scrollbarRect.anchorMax = new Vector2(1f, 1f);
            scrollbarRect.pivot = new Vector2(1f, 0.5f);
            scrollbarRect.offsetMin = new Vector2(-20f, 4f);
            scrollbarRect.offsetMax = new Vector2(-4f, -4f);
            scrollbarObject.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.08f);

            GameObject slidingArea = new("Sliding Area", typeof(RectTransform));
            slidingArea.transform.SetParent(scrollbarObject.transform, false);
            Stretch(slidingArea.GetComponent<RectTransform>(), 2f);

            GameObject handleObject = new("Handle", typeof(RectTransform), typeof(Image));
            handleObject.transform.SetParent(slidingArea.transform, false);
            RectTransform handleRect = handleObject.GetComponent<RectTransform>();
            handleRect.anchorMin = Vector2.zero;
            handleRect.anchorMax = Vector2.one;
            handleRect.offsetMin = Vector2.zero;
            handleRect.offsetMax = Vector2.zero;
            Image handleImage = handleObject.GetComponent<Image>();
            handleImage.color = new Color(0.35f, 0.75f, 1f, 0.85f);

            Scrollbar scrollbar = scrollbarObject.GetComponent<Scrollbar>();
            scrollbar.handleRect = handleRect;
            scrollbar.targetGraphic = handleImage;
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
            templateRect.sizeDelta = new Vector2(0f, 210f);
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

        private static Button AddButton(Transform parent, string label, UnityEngine.Events.UnityAction action)
        {
            GameObject go = new(label, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = new Color(0.14f, 0.42f, 0.72f, 0.95f);
            go.GetComponent<LayoutElement>().minHeight = 46f;
            Button button = go.GetComponent<Button>();
            button.onClick.AddListener(action);
            TMP_Text text = AddText(go.transform, label, 18, FontStyles.Bold);
            text.alignment = TextAlignmentOptions.Center;
            Stretch(text.rectTransform, 4f);
            return button;
        }

        private static void Stretch(RectTransform rect, float inset)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(inset, inset);
            rect.offsetMax = new Vector2(-inset, -inset);
        }
    }
}
