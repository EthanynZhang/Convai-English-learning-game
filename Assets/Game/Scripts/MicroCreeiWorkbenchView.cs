using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Game.Debate
{
    public sealed class MicroCreeiWorkbenchView : MonoBehaviour
    {
        public event Action<CreeiComponent> ComponentSelected;
        public event Action<WorkbenchVoiceTarget> VoiceTargetSelected;
        public event Action<CreeiComponent, string> ComponentTextChanged;
        public event Action RestartRecordingRequested;
        public event Action SubmitRequested;
        public event Action<CoachWorkbenchAction> CoachActionRequested;

        private readonly Dictionary<CreeiComponent, TMP_InputField> _inputs = new();
        private readonly Dictionary<CreeiComponent, TMP_Text> _cardStates = new();
        private readonly Dictionary<CreeiComponent, Outline> _cardOutlines = new();
        private readonly Dictionary<CreeiComponent, TMP_Text> _modelExamples = new();
        private readonly Dictionary<CreeiComponent, LayoutElement> _cardLayouts = new();
        private readonly Dictionary<string, Button> _buttons = new(StringComparer.Ordinal);
        private readonly Dictionary<CoachWorkbenchAction, string> _actionButtonNames = new();
        private RectTransform _root;
        private RectTransform _canvasRect;
        private TMP_Text _topic;
        private TMP_Text _timer;
        private TMP_Text _status;
        private TMP_Text _activeHint;
        private TMP_Text _dialogueSpeaker;
        private TMP_Text _dialogueText;
        private RectTransform _dialoguePanel;
        private TMP_Text _historyText;
        private GameObject _historyPanel;
        private Button _historyToggle;
        private TMP_InputField _learnerRequest;
        private Outline _learnerRequestOutline;
        private CreeiComponent? _activeComponent;
        private WorkbenchVoiceTarget _voiceTarget;
        private bool _suppressInputEvents;
        private float _lastViewportWidth = -1f;

        public RectTransform RootRect => _root;
        public CreeiComponent? ActiveComponent => _activeComponent;
        public WorkbenchVoiceTarget VoiceTarget => _voiceTarget;
        public bool IsAnyTextInputFocused =>
            _inputs.Values.Any(input => input != null && input.isFocused) ||
            (_learnerRequest != null && _learnerRequest.isFocused);
        public string LearnerRequestText => _learnerRequest != null
            ? _learnerRequest.text.Trim()
            : string.Empty;
        public bool IsLearnerRequestVisible => _learnerRequest != null &&
                                               _learnerRequest.gameObject.activeInHierarchy;
        public bool IsLearnerRequestFocused => _learnerRequest != null &&
                                               _learnerRequest.isFocused;

        private void Update()
        {
            if (_root == null) return;
            float viewportWidth = Screen.width > 0 ? Screen.width : GetCanvasWidth();
            if (Mathf.Abs(viewportWidth - _lastViewportWidth) > 0.5f)
                ApplyResponsiveLayout(viewportWidth);
        }

        public void Build(Canvas canvas)
        {
            if (_root != null || canvas == null) return;
            _canvasRect = canvas.transform as RectTransform;
            GameObject root = new("Micro CREEI Workbench", typeof(RectTransform), typeof(Image),
                typeof(VerticalLayoutGroup));
            root.transform.SetParent(canvas.transform, false);
            _root = root.GetComponent<RectTransform>();
            root.GetComponent<Image>().color = new Color(0.018f, 0.03f, 0.06f, 0.96f);
            VerticalLayoutGroup layout = root.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(14, 14, 12, 12);
            layout.spacing = 7f;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;

            AddText(root.transform, "PRACTICE 1 - CREEI WORKBENCH", 23, FontStyles.Bold);
            _timer = AddText(root.transform, "Time remaining  12:00", 21, FontStyles.Bold);
            _topic = AddText(root.transform, string.Empty, 14, FontStyles.Bold);
            _topic.color = new Color(0.35f, 0.78f, 1f);
            _activeHint = AddText(root.transform,
                "Select a card. T: Speak into the selected box.", 13, FontStyles.Bold);
            _activeHint.color = new Color(1f, 0.78f, 0.25f);

            GameObject cardScroll = CreateScroll(root.transform, "CREEI Card Scroll", 420f);
            Transform cardContent = cardScroll.GetComponent<ScrollRect>().content;
            foreach (CreeiComponent component in Enum.GetValues(typeof(CreeiComponent)))
                BuildCard(cardContent, component);

            _status = AddText(root.transform, "Complete all five cards to submit.", 14,
                FontStyles.Bold);
            BuildPrimaryControls(root.transform);
            BuildDecisionControls(root.transform);
            _learnerRequest = AddInput(root.transform, "Learner Coach Request",
                "Tell Anna what help you want", false);
            _learnerRequestOutline = _learnerRequest.gameObject.AddComponent<Outline>();
            _learnerRequestOutline.effectColor = new Color(0.2f, 0.55f, 0.82f, 0.72f);
            _learnerRequestOutline.effectDistance = new Vector2(1f, -1f);
            _learnerRequest.onSelect.AddListener(_ =>
                SelectVoiceTarget(WorkbenchVoiceTarget.CoachRequest));
            _learnerRequest.gameObject.SetActive(false);

            BuildDialogueBubble(canvas.transform);
            BuildHistory(canvas.transform);
            ApplyResponsiveLayout(Screen.width > 0 ? Screen.width : GetCanvasWidth());
            root.SetActive(false);
        }

        public static float CalculateWorkspaceWidth(float canvasWidth) =>
            Mathf.Clamp(Mathf.Max(320f, canvasWidth) * 0.58f, 500f, 640f);

        public static Rect CalculateDialogueRect(float viewportWidth)
        {
            float safeWidth = Mathf.Max(320f, viewportWidth);
            float workbenchRight = 4f + CalculateWorkspaceWidth(safeWidth);
            bool narrow = safeWidth < 1280f;
            float historyLeft = narrow
                ? safeWidth - 4f
                : safeWidth - 4f - CalculateWorkspaceWidth(safeWidth);
            float left = workbenchRight + 6f;
            float available = Mathf.Max(0f, historyLeft - left - 6f);
            float width = Mathf.Clamp(available, 300f, 620f);
            return new Rect(left, 0f, width, 148f);
        }

        public void Show(string topic, string stance, float remainingSeconds)
        {
            if (_root == null) return;
            _root.gameObject.SetActive(true);
            if (_topic != null) _topic.text = "TOPIC  " + (topic ?? string.Empty);
            SetRemainingSeconds(remainingSeconds);
            ApplyResponsiveLayout(Screen.width > 0 ? Screen.width : GetCanvasWidth());
        }

        public void Hide()
        {
            ClearCreeiModelExamples();
            if (_root != null) _root.gameObject.SetActive(false);
            if (_historyPanel != null) _historyPanel.SetActive(false);
            if (_historyToggle != null) _historyToggle.gameObject.SetActive(false);
            SetDialogue(string.Empty, string.Empty);
        }

        public void SetRemainingSeconds(float seconds)
        {
            int value = Mathf.Max(0, Mathf.CeilToInt(seconds));
            if (_timer != null) _timer.text = $"Time remaining  {value / 60:00}:{value % 60:00}";
        }

        public string GetComponentText(CreeiComponent component) =>
            _inputs.TryGetValue(component, out TMP_InputField input) && input != null
                ? input.text
                : string.Empty;

        public void SetComponentText(CreeiComponent component, string text)
        {
            if (!_inputs.TryGetValue(component, out TMP_InputField input) || input == null) return;
            _suppressInputEvents = true;
            input.text = text ?? string.Empty;
            _suppressInputEvents = false;
        }

        public void ShowCreeiModelExamples(CreeiModelExampleSet examples)
        {
            foreach (CreeiComponent component in Enum.GetValues(typeof(CreeiComponent)))
            {
                if (!_modelExamples.TryGetValue(component, out TMP_Text label) ||
                    label == null)
                    continue;
                string example = examples?.GetText(component)?.Trim() ?? string.Empty;
                label.text = string.IsNullOrWhiteSpace(example)
                    ? string.Empty
                    : "Anna's example: " + example;
                label.gameObject.SetActive(!string.IsNullOrWhiteSpace(example));
                if (_cardLayouts.TryGetValue(component, out LayoutElement cardLayout) &&
                    cardLayout != null)
                {
                    cardLayout.minHeight = string.IsNullOrWhiteSpace(example) ? 88f : 148f;
                    cardLayout.preferredHeight =
                        string.IsNullOrWhiteSpace(example) ? 88f : 148f;
                }
            }
            Canvas.ForceUpdateCanvases();
        }

        public void ClearCreeiModelExamples()
        {
            foreach (KeyValuePair<CreeiComponent, TMP_Text> pair in _modelExamples)
            {
                if (pair.Value == null) continue;
                pair.Value.text = string.Empty;
                pair.Value.gameObject.SetActive(false);
                if (_cardLayouts.TryGetValue(pair.Key, out LayoutElement cardLayout) &&
                    cardLayout != null)
                {
                    cardLayout.minHeight = 88f;
                    cardLayout.preferredHeight = 88f;
                }
            }
        }

        public void SetActiveComponent(CreeiComponent? component)
        {
            _activeComponent = component;
            if (component.HasValue) _voiceTarget = ToVoiceTarget(component.Value);
            foreach (KeyValuePair<CreeiComponent, Outline> pair in _cardOutlines)
            {
                if (pair.Value == null) continue;
                pair.Value.effectColor = component.HasValue && pair.Key == component.Value
                    ? new Color(1f, 0.72f, 0.15f, 1f)
                    : new Color(0.2f, 0.55f, 0.82f, 0.72f);
                pair.Value.effectDistance = component.HasValue && pair.Key == component.Value
                    ? new Vector2(3f, -3f)
                    : new Vector2(1f, -1f);
            }
            if (_activeHint != null)
                _activeHint.text = component.HasValue
                    ? $"ACTIVE: {component.Value} - T: Speak into this box"
                    : "Select a card. T: Speak into the selected box.";
            UpdateLearnerRequestOutline();
        }

        private void SelectComponentFromUser(CreeiComponent component)
        {
            SetActiveComponent(component);
            ComponentSelected?.Invoke(component);
            VoiceTargetSelected?.Invoke(_voiceTarget);
        }

        private void SelectVoiceTarget(WorkbenchVoiceTarget target)
        {
            _voiceTarget = target;
            if (target == WorkbenchVoiceTarget.CoachRequest)
            {
                _activeComponent = null;
                foreach (Outline outline in _cardOutlines.Values)
                {
                    if (outline == null) continue;
                    outline.effectColor = new Color(0.2f, 0.55f, 0.82f, 0.72f);
                    outline.effectDistance = new Vector2(1f, -1f);
                }
                if (_activeHint != null)
                    _activeHint.text = "ACTIVE: ASK COACH - T: Speak into this box";
            }
            UpdateLearnerRequestOutline();
            VoiceTargetSelected?.Invoke(target);
        }

        private void UpdateLearnerRequestOutline()
        {
            if (_learnerRequestOutline == null) return;
            bool active = _voiceTarget == WorkbenchVoiceTarget.CoachRequest;
            _learnerRequestOutline.effectColor = active
                ? new Color(1f, 0.72f, 0.15f, 1f)
                : new Color(0.2f, 0.55f, 0.82f, 0.72f);
            _learnerRequestOutline.effectDistance = active
                ? new Vector2(3f, -3f)
                : new Vector2(1f, -1f);
        }

        private static WorkbenchVoiceTarget ToVoiceTarget(CreeiComponent component) =>
            Enum.TryParse(component.ToString(), out WorkbenchVoiceTarget target)
                ? target
                : WorkbenchVoiceTarget.None;

        public void SetCardState(CreeiComponent component, string state)
        {
            if (_cardStates.TryGetValue(component, out TMP_Text text) && text != null)
                text.text = string.IsNullOrWhiteSpace(state) ? "Draft" : state;
        }

        public void ReleaseTextFocusForVoice()
        {
            foreach (TMP_InputField input in _inputs.Values)
                if (input != null && input.isFocused) input.DeactivateInputField();
            if (_learnerRequest != null && _learnerRequest.isFocused)
                _learnerRequest.DeactivateInputField();
            EventSystem.current?.SetSelectedGameObject(null);
        }

        public void SetStatus(string status)
        {
            if (_status != null) _status.text = status ?? string.Empty;
        }

        public void SetInputsInteractable(bool interactable)
        {
            foreach (TMP_InputField input in _inputs.Values)
                if (input != null) input.interactable = interactable;
            if (_learnerRequest != null) _learnerRequest.interactable = interactable;
        }

        public void SetLearnerRequestInteractable(bool interactable)
        {
            if (_learnerRequest != null) _learnerRequest.interactable = interactable;
        }

        public void SetButtonVisible(string label, bool visible, bool interactable = true)
        {
            if (!_buttons.TryGetValue(label, out Button button) || button == null) return;
            button.gameObject.SetActive(visible);
            button.interactable = interactable;
        }

        public void SetButtonVisible(
            CoachWorkbenchAction action,
            bool visible,
            bool interactable = true)
        {
            if (_actionButtonNames.TryGetValue(action, out string label))
                SetButtonVisible(label, visible, interactable);
        }

        public void SetActionLabel(CoachWorkbenchAction action, string label)
        {
            if (!_actionButtonNames.TryGetValue(action, out string name) ||
                !_buttons.TryGetValue(name, out Button button) || button == null) return;
            TMP_Text text = button.GetComponentInChildren<TMP_Text>(true);
            if (text != null) text.text = label ?? name;
        }

        public void HideAllActionButtons()
        {
            foreach (string name in _actionButtonNames.Values)
                SetButtonVisible(name, false);
            SetSubmitVisible(false);
            SetRestartRecordingVisible(false);
        }

        public void SetSubmitVisible(bool visible, bool interactable = true) =>
            SetButtonVisible("Submit Structure", visible, interactable);

        public void SetRestartRecordingVisible(bool visible) =>
            SetButtonVisible("Restart Recording", visible);

        public void SetLearnerRequestVisible(bool visible)
        {
            if (_learnerRequest != null) _learnerRequest.gameObject.SetActive(visible);
            if (!visible && _voiceTarget == WorkbenchVoiceTarget.CoachRequest)
            {
                _voiceTarget = WorkbenchVoiceTarget.None;
                if (_activeHint != null)
                    _activeHint.text = "Select a card. T: Speak into the selected box.";
                UpdateLearnerRequestOutline();
            }
        }

        public void ClearLearnerRequest()
        {
            if (_learnerRequest != null) _learnerRequest.text = string.Empty;
        }

        public void SetLearnerRequestText(string text)
        {
            if (_learnerRequest != null) _learnerRequest.text = text ?? string.Empty;
        }

        public void SetDialogue(string speaker, string text)
        {
            if (_dialogueSpeaker == null || _dialogueText == null) return;
            bool visible = !string.IsNullOrWhiteSpace(text);
            _dialogueSpeaker.transform.parent.gameObject.SetActive(visible);
            _dialogueSpeaker.text = speaker ?? string.Empty;
            _dialogueText.text = text ?? string.Empty;
        }

        public void AppendFeedbackHistory(
            int roundIndex,
            CoachFeedbackPurpose purpose,
            string learnerRequest,
            string feedback,
            bool spoken)
        {
            if (_historyText == null || string.IsNullOrWhiteSpace(feedback)) return;
            string agendaLabel = purpose == CoachFeedbackPurpose.LearnerSocratic
                ? "Learner question"
                : purpose switch
                {
                    CoachFeedbackPurpose.CriticalIssue => "Critical issue",
                    CoachFeedbackPurpose.TargetedAdvice => "Detailed advice",
                    CoachFeedbackPurpose.DirectAdvice => "Direct advice",
                    CoachFeedbackPurpose.ConversationalFollowUp => "Follow-up answer",
                    CoachFeedbackPurpose.Example => "Example",
                    CoachFeedbackPurpose.AdditionalSuggestion => "More suggestions",
                    _ => "Coach feedback"
                };
            string entry = $"Round {roundIndex} - {agendaLabel}\n";
            if (!string.IsNullOrWhiteSpace(learnerRequest))
                entry += "You asked: \"" + learnerRequest.Trim() + "\"\n";
            entry += "Anna: \"" + feedback.Trim() + "\"\nVoice played: " +
                     (spoken ? "yes" : "no");
            _historyText.text = string.IsNullOrWhiteSpace(_historyText.text)
                ? entry
                : _historyText.text + "\n\n" + entry;
        }

        private void BuildCard(Transform parent, CreeiComponent component)
        {
            GameObject card = new("CREEI Card " + component, typeof(RectTransform), typeof(Image),
                typeof(VerticalLayoutGroup), typeof(LayoutElement), typeof(Outline), typeof(Button));
            card.transform.SetParent(parent, false);
            card.GetComponent<Image>().color = new Color(0.045f, 0.085f, 0.14f, 0.98f);
            LayoutElement cardElement = card.GetComponent<LayoutElement>();
            cardElement.minHeight = 88f;
            cardElement.preferredHeight = 88f;
            _cardLayouts[component] = cardElement;
            VerticalLayoutGroup layout = card.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(9, 9, 4, 4);
            layout.spacing = 3f;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            _cardOutlines[component] = card.GetComponent<Outline>();
            Button cardButton = card.GetComponent<Button>();
            cardButton.transition = Selectable.Transition.None;
            cardButton.onClick.AddListener(() => SelectComponentFromUser(component));

            GameObject header = new("Card Header", typeof(RectTransform),
                typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            header.transform.SetParent(card.transform, false);
            LayoutElement headerElement = header.GetComponent<LayoutElement>();
            headerElement.minHeight = 27f;
            headerElement.preferredHeight = 27f;
            HorizontalLayoutGroup headerLayout = header.GetComponent<HorizontalLayoutGroup>();
            headerLayout.spacing = 6f;
            headerLayout.childControlWidth = true;
            headerLayout.childForceExpandWidth = false;
            headerLayout.childForceExpandHeight = false;
            TMP_Text title = AddText(header.transform, component.ToString().ToUpperInvariant(), 15,
                FontStyles.Bold);
            title.color = new Color(0.46f, 0.83f, 1f);
            title.textWrappingMode = TextWrappingModes.NoWrap;
            LayoutElement titleLayout = title.GetComponent<LayoutElement>();
            titleLayout.preferredHeight = 27f;
            titleLayout.flexibleWidth = 1f;
            TMP_Text state = AddText(header.transform, "Empty", 12, FontStyles.Bold);
            state.alignment = TextAlignmentOptions.Right;
            state.textWrappingMode = TextWrappingModes.NoWrap;
            LayoutElement stateLayout = state.GetComponent<LayoutElement>();
            stateLayout.preferredWidth = 86f;
            stateLayout.preferredHeight = 27f;
            _cardStates[component] = state;
            TMP_Text modelExample = AddText(
                card.transform,
                string.Empty,
                12,
                FontStyles.Italic);
            modelExample.gameObject.name = "CREEI Model Example " + component;
            modelExample.color = new Color(0.65f, 0.9f, 1f);
            modelExample.textWrappingMode = TextWrappingModes.Normal;
            LayoutElement modelLayout = modelExample.GetComponent<LayoutElement>();
            modelLayout.minHeight = 44f;
            modelLayout.preferredHeight = 54f;
            modelExample.gameObject.SetActive(false);
            _modelExamples[component] = modelExample;
            TMP_InputField input = AddInput(card.transform, "CREEI Input " + component,
                GetPlaceholder(component), true);
            input.onSelect.AddListener(_ => SelectComponentFromUser(component));
            input.onValueChanged.AddListener(value =>
            {
                if (!_suppressInputEvents)
                    ComponentTextChanged?.Invoke(component, value ?? string.Empty);
            });
            _inputs[component] = input;
        }

        private void BuildPrimaryControls(Transform parent)
        {
            GameObject row = AddButtonRow(parent, "Primary Controls");
            AddButton(row.transform, "Submit Structure", () => SubmitRequested?.Invoke());
            AddButton(row.transform, "Restart Recording", () => RestartRecordingRequested?.Invoke());
        }

        private void BuildDecisionControls(Transform parent)
        {
            GameObject first = AddButtonRow(parent, "Coach Decision Controls");
            AddActionButton(first.transform, "Ask Coach", CoachWorkbenchAction.AskCoach);
            AddActionButton(first.transform, "Accept Issue", CoachWorkbenchAction.AcceptIssue);
            AddActionButton(first.transform, "Change Request", CoachWorkbenchAction.ChangeRequest);
            AddActionButton(first.transform, "Continue Without Coach",
                CoachWorkbenchAction.ContinueWithoutCoach);

            GameObject second = AddButtonRow(parent, "Coach Follow-up Controls");
            AddActionButton(second.transform, "Need an Example?", CoachWorkbenchAction.NeedExample);
            AddActionButton(second.transform, "Need More Suggestions?",
                CoachWorkbenchAction.NeedMoreSuggestions);
            AddActionButton(second.transform, "Use Advice & Revise",
                CoachWorkbenchAction.UseAdviceAndRevise);

            GameObject third = AddButtonRow(parent, "Practice Controls");
            AddActionButton(third.transform, "Finish Practice 1",
                CoachWorkbenchAction.FinishPractice);
            AddActionButton(third.transform, "Retry", CoachWorkbenchAction.Retry);
            AddActionButton(third.transform, "Technical Skip", CoachWorkbenchAction.TechnicalSkip);
        }

        private void AddActionButton(
            Transform parent,
            string label,
            CoachWorkbenchAction action)
        {
            AddButton(parent, label, () => CoachActionRequested?.Invoke(action));
            _actionButtonNames[action] = label;
        }

        private void BuildDialogueBubble(Transform parent)
        {
            GameObject panel = new("CREEI Dialogue Bubble", typeof(RectTransform), typeof(Image),
                typeof(VerticalLayoutGroup));
            panel.transform.SetParent(parent, false);
            RectTransform rect = panel.GetComponent<RectTransform>();
            _dialoguePanel = rect;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(0f, -20f);
            rect.sizeDelta = new Vector2(480f, 148f);
            panel.GetComponent<Image>().color = new Color(0.02f, 0.08f, 0.14f, 0.94f);
            VerticalLayoutGroup layout = panel.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(14, 14, 10, 10);
            layout.spacing = 6f;
            _dialogueSpeaker = AddText(panel.transform, string.Empty, 16, FontStyles.Bold);
            _dialogueSpeaker.color = new Color(0.4f, 0.82f, 1f);
            GameObject feedbackScroll = CreateScroll(panel.transform,
                "CREEI Dialogue Feedback Scroll", 92f);
            _dialogueText = AddText(feedbackScroll.GetComponent<ScrollRect>().content,
                string.Empty, 16);
            ContentSizeFitter dialogueFitter =
                _dialogueText.gameObject.AddComponent<ContentSizeFitter>();
            dialogueFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            panel.SetActive(false);
        }

        private void BuildHistory(Transform parent)
        {
            _historyPanel = new GameObject("Micro CREEI Feedback History", typeof(RectTransform),
                typeof(Image), typeof(VerticalLayoutGroup));
            _historyPanel.transform.SetParent(parent, false);
            RectTransform rect = _historyPanel.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.one;
            rect.anchorMax = Vector2.one;
            rect.pivot = Vector2.one;
            rect.anchoredPosition = new Vector2(-16f, -16f);
            rect.sizeDelta = new Vector2(640f, 430f);
            _historyPanel.GetComponent<Image>().color = new Color(0.018f, 0.03f, 0.06f, 0.94f);
            VerticalLayoutGroup layout = _historyPanel.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(14, 14, 12, 12);
            layout.spacing = 8f;
            AddText(_historyPanel.transform, "Coach Feedback History", 20, FontStyles.Bold);
            GameObject scroll = CreateScroll(_historyPanel.transform, "Workbench History Scroll", 330f);
            _historyText = AddText(scroll.GetComponent<ScrollRect>().content, string.Empty, 14);
            ContentSizeFitter fitter = _historyText.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            _historyToggle = AddButton(parent, "History", ToggleHistory);
            RectTransform toggleRect = _historyToggle.GetComponent<RectTransform>();
            toggleRect.anchorMin = Vector2.one;
            toggleRect.anchorMax = Vector2.one;
            toggleRect.pivot = Vector2.one;
            toggleRect.anchoredPosition = new Vector2(-16f, -16f);
            toggleRect.sizeDelta = new Vector2(120f, 44f);
        }

        private void ApplyResponsiveLayout(float viewportWidth)
        {
            _lastViewportWidth = viewportWidth;
            if (_root != null)
            {
                float width = CalculateWorkspaceWidth(viewportWidth);
                _root.anchorMin = Vector2.zero;
                _root.anchorMax = new Vector2(0f, 1f);
                _root.pivot = new Vector2(0f, 0.5f);
                _root.anchoredPosition = new Vector2(4f, 0f);
                _root.sizeDelta = new Vector2(width, -16f);
            }
            if (_dialoguePanel != null)
            {
                Rect dialogue = CalculateDialogueRect(viewportWidth);
                _dialoguePanel.anchoredPosition = new Vector2(dialogue.x, -20f);
                _dialoguePanel.sizeDelta = new Vector2(dialogue.width, dialogue.height);
            }
            bool narrow = viewportWidth < 1280f;
            if (_historyPanel != null)
            {
                RectTransform historyRect = _historyPanel.GetComponent<RectTransform>();
                historyRect.anchoredPosition = new Vector2(-4f, -16f);
                historyRect.sizeDelta = new Vector2(
                    CalculateWorkspaceWidth(viewportWidth), historyRect.sizeDelta.y);
            }
            if (_historyPanel != null) _historyPanel.SetActive(!narrow && _root.gameObject.activeSelf);
            if (_historyToggle != null) _historyToggle.gameObject.SetActive(narrow && _root.gameObject.activeSelf);
        }

        private void ToggleHistory()
        {
            if (_historyPanel != null) _historyPanel.SetActive(!_historyPanel.activeSelf);
        }

        private float GetCanvasWidth() => _canvasRect != null && _canvasRect.rect.width > 0f
            ? _canvasRect.rect.width
            : 1600f;

        private Button AddButton(Transform parent, string label, Action action, string shownLabel = null)
        {
            GameObject go = new(label, typeof(RectTransform), typeof(Image), typeof(Button),
                typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = new Color(0.08f, 0.4f, 0.68f, 1f);
            go.GetComponent<LayoutElement>().minHeight = 38f;
            TMP_Text text = AddText(go.transform, shownLabel ?? label, 13, FontStyles.Bold);
            text.alignment = TextAlignmentOptions.Center;
            Stretch(text.rectTransform, 0f);
            Button button = go.GetComponent<Button>();
            button.onClick.AddListener(() => action?.Invoke());
            _buttons[label] = button;
            return button;
        }

        private static GameObject AddButtonRow(Transform parent, string name)
        {
            GameObject row = new(name, typeof(RectTransform), typeof(HorizontalLayoutGroup),
                typeof(LayoutElement));
            row.transform.SetParent(parent, false);
            row.GetComponent<LayoutElement>().minHeight = 40f;
            HorizontalLayoutGroup layout = row.GetComponent<HorizontalLayoutGroup>();
            layout.spacing = 6f;
            layout.childControlHeight = true;
            layout.childForceExpandHeight = false;
            layout.childControlWidth = true;
            layout.childForceExpandWidth = true;
            return row;
        }

        private static TMP_Text AddText(Transform parent, string value, int size,
            FontStyles style = FontStyles.Normal)
        {
            GameObject go = new("Text", typeof(RectTransform), typeof(TextMeshProUGUI),
                typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            TMP_Text text = go.GetComponent<TMP_Text>();
            text.text = value;
            text.fontSize = size;
            text.fontStyle = style;
            text.color = Color.white;
            text.textWrappingMode = TextWrappingModes.Normal;
            go.GetComponent<LayoutElement>().minHeight = size + 8f;
            return text;
        }

        private static TMP_InputField AddInput(
            Transform parent,
            string name,
            string placeholder,
            bool multiline)
        {
            GameObject root = new(name, typeof(RectTransform), typeof(Image),
                typeof(TMP_InputField), typeof(LayoutElement));
            root.transform.SetParent(parent, false);
            root.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.08f);
            root.GetComponent<LayoutElement>().minHeight = multiline ? 48f : 44f;
            GameObject viewport = new("Text Area", typeof(RectTransform), typeof(RectMask2D));
            viewport.transform.SetParent(root.transform, false);
            Stretch(viewport.GetComponent<RectTransform>(), 7f);
            TMP_Text text = AddText(viewport.transform, string.Empty, 14);
            Stretch(text.rectTransform, 0f);
            TMP_Text hint = AddText(viewport.transform, placeholder, 13);
            hint.color = new Color(1f, 1f, 1f, 0.42f);
            Stretch(hint.rectTransform, 0f);
            TMP_InputField input = root.GetComponent<TMP_InputField>();
            input.textViewport = viewport.GetComponent<RectTransform>();
            input.textComponent = text;
            input.placeholder = hint;
            input.lineType = multiline
                ? TMP_InputField.LineType.MultiLineNewline
                : TMP_InputField.LineType.SingleLine;
            input.onValidateInput = (_, _, character) =>
                EnglishLlmInputSanitizer.FilterInputCharacter(character);
            return input;
        }

        private static GameObject CreateScroll(Transform parent, string name, float minHeight)
        {
            GameObject root = new(name, typeof(RectTransform), typeof(Image), typeof(ScrollRect),
                typeof(LayoutElement));
            root.transform.SetParent(parent, false);
            root.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.18f);
            LayoutElement element = root.GetComponent<LayoutElement>();
            element.minHeight = minHeight;
            element.flexibleHeight = 1f;
            GameObject viewport = new("Viewport", typeof(RectTransform), typeof(Image),
                typeof(RectMask2D));
            viewport.transform.SetParent(root.transform, false);
            viewport.GetComponent<Image>().color = Color.clear;
            Stretch(viewport.GetComponent<RectTransform>(), 4f);
            GameObject content = new("Content", typeof(RectTransform), typeof(VerticalLayoutGroup),
                typeof(ContentSizeFitter));
            content.transform.SetParent(viewport.transform, false);
            RectTransform contentRect = content.GetComponent<RectTransform>();
            contentRect.anchorMin = new Vector2(0f, 1f);
            contentRect.anchorMax = new Vector2(1f, 1f);
            contentRect.pivot = new Vector2(0.5f, 1f);
            contentRect.anchoredPosition = Vector2.zero;
            contentRect.sizeDelta = Vector2.zero;
            VerticalLayoutGroup layout = content.GetComponent<VerticalLayoutGroup>();
            layout.spacing = 7f;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            content.GetComponent<ContentSizeFitter>().verticalFit =
                ContentSizeFitter.FitMode.PreferredSize;
            ScrollRect scroll = root.GetComponent<ScrollRect>();
            scroll.viewport = viewport.GetComponent<RectTransform>();
            scroll.content = contentRect;
            scroll.horizontal = false;
            scroll.vertical = true;
            return root;
        }

        private static string GetPlaceholder(CreeiComponent component) => component switch
        {
            CreeiComponent.Claim => "State the side you support.",
            CreeiComponent.Reason => "Explain why your claim is valid.",
            CreeiComponent.Evidence => "Give one specific example, fact, or experience.",
            CreeiComponent.Explanation => "Connect the evidence to your reason and claim.",
            CreeiComponent.Impact => "Show who is affected and why it matters.",
            _ => string.Empty
        };

        private static void Stretch(RectTransform rect, float inset)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(inset, inset);
            rect.offsetMax = new Vector2(-inset, -inset);
        }
    }
}
