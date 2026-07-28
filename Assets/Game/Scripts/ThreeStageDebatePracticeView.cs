using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Debate
{
    public sealed class ThreeStageDebatePracticeView : MonoBehaviour
    {
        private const float HistoryAutoExpandedMinViewportWidth = 1440f;

        public event Action<string, CoachOrchestrationMode> SessionStartRequested;
        public event Action IntroductionCompleted;
        public event Action ContinueRequested;
        public event Action RerecordRequested;
        public event Action RetryDiagnosisRequested;
        public event Action SkipRequested;
        public event Action<CoachLearnerAction, string> LearnerActionRequested;

        public bool IsBuilt => _root != null;
        public RectTransform RootRect => _root != null ? _root.GetComponent<RectTransform>() : null;
        public string TranscriptText => _transcript != null ? _transcript.text : string.Empty;
        public string SelectedFocus => _selectedFocus;
        public string LearnerRequestText => _learnerRequest != null
            ? _learnerRequest.text.Trim()
            : string.Empty;
        public bool IsLearnerRequestVisible =>
            _learnerRequestSection != null && _learnerRequestSection.activeSelf;
        public bool IsLearnerRequestFocused =>
            _learnerRequest != null && _learnerRequest.isFocused;
        public bool IsCoachAgendaVisible =>
            _coachAgendaBubble != null && _coachAgendaBubble.activeSelf;
        public string CoachAgendaText => _coachAgendaText != null
            ? _coachAgendaText.text
            : string.Empty;
        public CoachAgendaSource CurrentCoachAgendaSource { get; private set; }

        private GameObject _root;
        private GameObject _setupPanel;
        private GameObject _introductionPanel;
        private GameObject _practicePanel;
        private GameObject _coachPanel;
        private GameObject _completionPanel;
        private GameObject _feedbackHistoryPanel;
        private GameObject _coachAgendaBubble;
        private GameObject _conditionAuthorityBanner;
        private GameObject _focusSelector;
        private GameObject _learnerRequestSection;
        private TMP_InputField _learnerRequest;
        private TMP_InputField _transcript;
        private TMP_Text _setupError;
        private TMP_Text _introductionTopic;
        private TMP_Text _introductionOverview;
        private TMP_Text _title;
        private TMP_Text _timer;
        private TMP_Text _status;
        private TMP_Text _practiceContext;
        private TMP_Text _coachStatus;
        private TMP_Text _coachFeedback;
        private TMP_Text _conditionAuthorityText;
        private TMP_Text _completionStatus;
        private TMP_Text _feedbackHistory;
        private TMP_Text _coachAgendaTitle;
        private TMP_Text _coachAgendaText;
        private TMP_Text _historyToggleLabel;
        private TMP_Text _learnerRequestVoiceHint;
        private Button _rerecordButton;
        private Button _retryButton;
        private Button _skipButton;
        private Button _historyToggleButton;
        private readonly Dictionary<CoachLearnerAction, Button> _coachButtons = new();
        private readonly Dictionary<CoachLearnerAction, TMP_Text> _coachButtonLabels = new();
        private readonly Dictionary<CoachOrchestrationMode, Button> _modeButtons = new();
        private readonly Dictionary<string, Button> _focusButtons = new(StringComparer.OrdinalIgnoreCase);
        private CoachOrchestrationMode _selectedMode = CoachOrchestrationMode.LearnerLed;
        private string _assignedParticipantId = string.Empty;
        private string _selectedFocus = "Explanation";
        private int _feedbackHistoryIndex;
        private RectTransform _canvasRect;
        private Vector2 _lastCanvasSize;
        private Vector2 _lastViewportPixelSize;
        private bool _historyContextVisible;
        private bool _historyExpandedByUser;

        public void Build(Canvas canvas)
        {
            if (_root != null || canvas == null) return;
            _canvasRect = canvas.transform as RectTransform;

            _root = CreatePanel("Three Stage Debate Practice", canvas.transform,
                new Color(0.025f, 0.035f, 0.065f, 0.94f));
            ApplyStudyLayout();

            _setupPanel = CreatePanel("Researcher Setup", _root.transform, Color.clear);
            Stretch(_setupPanel.GetComponent<RectTransform>(), 20f);
            AddText(_setupPanel.transform, "Coach Agent Study Setup", 30, FontStyles.Bold);
            AddText(_setupPanel.transform,
                "Build one argument in five connected CREEI cards, then work one-to-one with Anna according to your assigned condition before revising it.", 18);
            AddText(_setupPanel.transform, "Experimental condition", 17, FontStyles.Bold);
            GameObject modeSelector = AddButtonRow(_setupPanel.transform, "Mode Selector", 52f);
            AddModeButton(modeSelector.transform, "Learner Led", CoachOrchestrationMode.LearnerLed);
            AddModeButton(modeSelector.transform, "Shared Control", CoachOrchestrationMode.SharedControl);
            AddModeButton(modeSelector.transform, "AI Led", CoachOrchestrationMode.AiLed);
            SelectMode(CoachOrchestrationMode.LearnerLed);
            _setupError = AddText(_setupPanel.transform, string.Empty, 16);
            _setupError.color = new Color(1f, 0.5f, 0.45f);
            Button setupStart = AddButton(
                _setupPanel.transform, "Confirm & Start Scene 04", BeginSession);
            setupStart.gameObject.name = "Start Practice";

            _introductionPanel = CreatePanel("Study Introduction", _root.transform, Color.clear);
            Stretch(_introductionPanel.GetComponent<RectTransform>(), 20f);
            AddText(_introductionPanel.transform, "Welcome to Debate Practice", 29,
                FontStyles.Bold);
            _introductionTopic = AddText(_introductionPanel.transform, string.Empty, 17,
                FontStyles.Bold);
            _introductionTopic.gameObject.name = "Introduction Topic";
            _introductionTopic.gameObject.GetComponent<LayoutElement>().minHeight = 96f;
            _introductionOverview = AddText(_introductionPanel.transform, string.Empty, 14);
            _introductionOverview.gameObject.name = "Introduction Overview";
            _introductionOverview.gameObject.GetComponent<LayoutElement>().minHeight = 360f;
            AddButton(_introductionPanel.transform, "Begin Practice 1",
                () => IntroductionCompleted?.Invoke());

            _practicePanel = CreatePanel("Practice", _root.transform, Color.clear);
            Stretch(_practicePanel.GetComponent<RectTransform>(), 20f);
            _title = AddText(_practicePanel.transform, "Practice", 27, FontStyles.Bold);
            _timer = AddText(_practicePanel.transform, "00:00", 25, FontStyles.Bold);
            _status = AddText(_practicePanel.transform, string.Empty, 18);
            _practiceContext = AddText(_practicePanel.transform, string.Empty, 17);
            _practiceContext.gameObject.name = "Practice Context";
            _practiceContext.color = new Color(0.78f, 0.88f, 1f);
            _transcript = AddInput(_practicePanel.transform, "Live transcript", true);
            _transcript.readOnly = true;
            _rerecordButton = AddButton(_practicePanel.transform, "Restart Recording",
                () => RerecordRequested?.Invoke());
            _retryButton = AddButton(_practicePanel.transform, "Retry Diagnosis",
                () => RetryDiagnosisRequested?.Invoke());
            _skipButton = AddButton(_practicePanel.transform, "Technical Skip",
                () => SkipRequested?.Invoke());

            _coachPanel = CreatePanel("Coach", _root.transform, Color.clear);
            Stretch(_coachPanel.GetComponent<RectTransform>(), 20f);
            _conditionAuthorityBanner = CreatePanel(
                "Condition Authority Banner",
                _coachPanel.transform,
                new Color(0.08f, 0.35f, 0.55f, 0.96f));
            LayoutElement authorityLayout =
                _conditionAuthorityBanner.AddComponent<LayoutElement>();
            authorityLayout.minHeight = 74f;
            authorityLayout.preferredHeight = 74f;
            _conditionAuthorityText = AddText(
                _conditionAuthorityBanner.transform,
                "COACH CONTROL",
                19,
                FontStyles.Bold);
            _conditionAuthorityText.alignment = TextAlignmentOptions.Center;
            _coachStatus = AddText(_coachPanel.transform, "Coach", 24, FontStyles.Bold);
            _coachFeedback = AddScrollableText(
                _coachPanel.transform, "Coach Feedback Scroll", 170f, 220f);
            _focusSelector = AddFocusSelector(_coachPanel.transform);
            _learnerRequestSection = CreatePanel("Learner Request Section", _coachPanel.transform,
                new Color(1f, 1f, 1f, 0.035f));
            _learnerRequestSection.AddComponent<LayoutElement>().minHeight = 142f;
            AddText(_learnerRequestSection.transform,
                "Tell Anna what kind of help you want.", 16, FontStyles.Bold);
            _learnerRequestVoiceHint = AddText(_learnerRequestSection.transform,
                "Press T to start voice input, or click the field to type.", 14);
            _learnerRequestVoiceHint.gameObject.name = "Learner Request Voice Hint";
            _learnerRequest = AddInput(_learnerRequestSection.transform,
                "Type a natural-language coaching request", false);
            _learnerRequest.gameObject.name = "Learner Request";
            AddCoachButton("Ask Coach", CoachLearnerAction.RequestCoach);
            AddCoachButton("Confirm Focus", CoachLearnerAction.ConfirmFocus);
            AddCoachButton("Accept", CoachLearnerAction.Accept);
            AddCoachButton("Change Focus", CoachLearnerAction.ChangeFocus);
            AddCoachButton("Decline", CoachLearnerAction.Decline);
            AddCoachButton("Continue", CoachLearnerAction.NeedExample);
            AddCoachButton("Replay", CoachLearnerAction.Replay);
            AddCoachButton("End Coaching", CoachLearnerAction.ApplyNextCycle);
            AddCoachButton("Override", CoachLearnerAction.Override);
            AddCoachButton("Retry Feedback", CoachLearnerAction.RetryFeedback);

            _completionPanel = CreatePanel("Practice Completion", _root.transform, Color.clear);
            Stretch(_completionPanel.GetComponent<RectTransform>(), 20f);
            AddText(_completionPanel.transform, "Scene 04 Complete", 30, FontStyles.Bold);
            _completionStatus = AddText(_completionPanel.transform, string.Empty, 18);
            _completionStatus.gameObject.GetComponent<LayoutElement>().minHeight = 72f;
            AddButton(
                _completionPanel.transform,
                "Continue to Scene 05  |  Transfer Debate",
                () => ContinueRequested?.Invoke());

            _feedbackHistoryPanel = CreatePanel("Coach Feedback History Panel", canvas.transform,
                new Color(0.025f, 0.035f, 0.065f, 0.88f));
            ApplyFeedbackHistoryLayout(canvas);
            AddText(_feedbackHistoryPanel.transform, "Coach Feedback History", 22,
                FontStyles.Bold);
            _feedbackHistory = AddFeedbackHistory(_feedbackHistoryPanel.transform);
            _historyToggleButton = AddButton(canvas.transform, "History", ToggleHistoryPanel);
            _historyToggleButton.gameObject.name = "Coach History Toggle";
            _historyToggleLabel = _historyToggleButton.GetComponentInChildren<TMP_Text>(true);

            _coachAgendaBubble = CreatePanel(
                "Coach Agenda Bubble",
                canvas.transform,
                new Color(0.025f, 0.12f, 0.22f, 0.96f));
            Outline agendaOutline = _coachAgendaBubble.AddComponent<Outline>();
            agendaOutline.effectColor = new Color(0.24f, 0.68f, 1f, 0.95f);
            agendaOutline.effectDistance = new Vector2(2f, -2f);
            _coachAgendaTitle = AddText(
                _coachAgendaBubble.transform, "Coaching agenda", 17, FontStyles.Bold);
            _coachAgendaTitle.color = new Color(0.55f, 0.84f, 1f);
            _coachAgendaText = AddScrollableText(
                _coachAgendaBubble.transform, "Coach Agenda Text Scroll", 76f, 92f);
            _coachAgendaBubble.SetActive(false);
            RefreshResponsiveLayout();
            ShowSetup();
        }

        public void ShowSetup(string error = "")
        {
            HideCoachAgenda();
            SetPanels(true, false, false, false);
            if (_setupError != null) _setupError.text = error ?? string.Empty;
        }

        public void ConfigureAssignedSession(
            string participantId,
            CoachOrchestrationMode assignedMode)
        {
            _assignedParticipantId = participantId?.Trim() ?? string.Empty;
            if (assignedMode != CoachOrchestrationMode.Disabled)
                SelectMode(assignedMode);
            ShowSetup();
        }

        public void ShowIntroduction(string topic, string learnerStance)
        {
            HideCoachAgenda();
            SetPanels(false, true, false, false);
            if (_introductionTopic != null)
                _introductionTopic.text = "DEBATE TOPIC\n" + (topic?.Trim() ?? string.Empty) +
                                          "\n\nYOUR POSITION\n" +
                                          (learnerStance?.Trim() ?? string.Empty);
            if (_introductionOverview != null)
            {
                string conditionGuidance = _selectedMode switch
                {
                    CoachOrchestrationMode.AiLed =>
                        "AI-LED CONDITION: Anna automatically selects the feedback focus and gives advice. You review it before revising.",
                    CoachOrchestrationMode.SharedControl =>
                        "SHARED CONTROL CONDITION: Anna proposes one feedback focus. You can accept it, change the request by text or voice, or continue without Coach advice.",
                    _ =>
                        "LEARNER-LED CONDITION: Anna waits until you choose Ask Coach. You can request help by text or voice, or continue without Coach advice."
                };
                _introductionOverview.text =
                    "WHO YOU WILL WORK WITH\n" +
                    "• Coach (Anna): gives feedback and guidance. How proactively she helps depends on the assigned study condition.\n" +
                    "• Practice 1 is a one-to-one coaching workbench with Anna.\n\n" +
                    conditionGuidance + "\n\n" +
                    "WHAT YOU WILL DO\n" +
                    "Practice 1 — CREEI Workbench (up to 10 minutes): complete five connected CREEI cards, interact with Anna according to your condition, and revise the structure.\n" +
                    "Practice 2 — Full Speech + Coach Feedback: deliver one complete speech for at least 90 seconds, then receive feedback.\n" +
                    "Practice 3 — Revision Speech: deliver a second complete speech for at least 90 seconds. Anna stays silent while the system records the assessment.\n\n" +
                    "In Practice 1, select a card before pressing T so speech is added only to that card. Press T to start or stop the complete speech in Practices 2 and 3.";
            }
        }

        public void ShowPractice(
            DebatePracticeStage stage,
            float stageElapsed,
            float recordingElapsed,
            string status,
            string transcript,
            bool canRestartRecording,
            bool diagnosisFailed = false,
            string context = "")
        {
            HideCoachAgenda();
            SetPanels(false, false, true, false);
            if (_title != null)
                _title.text = $"Practice {ThreeStageDebatePracticeRules.GetStageNumber(stage)}/3: " +
                              ThreeStageDebatePracticeRules.GetTitle(stage);
            if (_timer != null)
            {
                if (stage == DebatePracticeStage.MicroPractice)
                {
                    float remaining = Mathf.Max(0f,
                        ThreeStageDebatePracticeRules.MicroPracticeSeconds - stageElapsed);
                    _timer.text = "Time remaining  " + FormatTime(remaining);
                }
                else
                {
                    _timer.text = $"Speech  {FormatTime(recordingElapsed)} / minimum 01:30  (max 03:00)";
                }
            }
            if (_status != null) _status.text = status ?? string.Empty;
            if (_practiceContext != null)
            {
                _practiceContext.text = context ?? string.Empty;
                _practiceContext.gameObject.SetActive(!string.IsNullOrWhiteSpace(context));
            }
            if (_transcript != null) _transcript.text = transcript ?? string.Empty;
            SetActive(_rerecordButton, canRestartRecording);
            SetActive(_retryButton, diagnosisFailed);
            SetActive(_skipButton, diagnosisFailed);
        }

        public void ShowCoach(
            string status,
            string feedback,
            bool showFocus,
            params CoachLearnerAction[] actions)
        {
            SetPanels(false, false, false, true);
            ResetCoachActionLabels();
            if (_coachStatus != null) _coachStatus.text = status ?? string.Empty;
            if (_coachFeedback != null) _coachFeedback.text = feedback ?? string.Empty;
            if (_focusSelector != null) _focusSelector.SetActive(showFocus);
            SetLearnerRequestVisible(false);
            HashSet<CoachLearnerAction> visible = new(actions ?? Array.Empty<CoachLearnerAction>());
            foreach (KeyValuePair<CoachLearnerAction, Button> pair in _coachButtons)
                if (pair.Value != null) pair.Value.gameObject.SetActive(visible.Contains(pair.Key));
        }

        public void ShowAutomatedCoachFeedback(string status, string feedback)
        {
            ShowCoach(status, feedback, false);
            ApplyConditionIdentity(CoachOrchestrationMode.AiLed);
        }

        public void ShowConditionOpportunity(
            CoachOrchestrationMode mode,
            CoachDiagnosisResult diagnosis)
        {
            switch (mode)
            {
                case CoachOrchestrationMode.AiLed:
                    ShowCoachAgenda(
                        CoachAgendaSource.AiDiagnosis,
                        "AI-selected coaching agenda",
                        CoachConditionExperience.BuildAiDiagnosisAgenda(diagnosis));
                    ShowCoach(
                        "Anna is deciding what feedback you need.",
                        "Coach Agent is analysing your response. No learner selection is required.",
                        false);
                    break;
                case CoachOrchestrationMode.SharedControl:
                    ShowCoachAgenda(
                        CoachAgendaSource.SharedAiProposal,
                        "AI-proposed coaching agenda",
                        CoachConditionExperience.BuildSharedSuggestion(diagnosis));
                    ShowCoach(
                        "Anna proposes a direction; you retain the final choice.",
                        string.Empty,
                        false,
                        CoachLearnerAction.Accept,
                        CoachLearnerAction.ChangeFocus,
                        CoachLearnerAction.Decline);
                    SetCoachActionLabel(CoachLearnerAction.Accept, "Accept Suggestion");
                    SetCoachActionLabel(CoachLearnerAction.ChangeFocus, "Change Request");
                    SetCoachActionLabel(CoachLearnerAction.Decline, "Continue Without Coach");
                    break;
                default:
                    HideCoachAgenda();
                    ShowCoach(
                        "Anna is available only if you decide to ask.",
                        "No diagnosis or advice is shown unless you request coaching.",
                        false,
                        CoachLearnerAction.RequestCoach,
                        CoachLearnerAction.ApplyNextCycle);
                    SetCoachActionLabel(CoachLearnerAction.RequestCoach, "Ask Coach");
                    SetCoachActionLabel(CoachLearnerAction.ApplyNextCycle, "Continue Without Coach");
                    break;
            }

            ApplyConditionIdentity(mode);
        }

        public void ShowConditionRequestEntry(CoachOrchestrationMode mode)
        {
            bool shared = mode == CoachOrchestrationMode.SharedControl;
            ShowCoach(
                shared
                    ? "Change Anna's proposed direction."
                    : "Ask Anna for the kind of help you want.",
                shared
                    ? "Enter an alternative coaching request by text or voice."
                    : "Describe the feedback you want by text or voice.",
                false,
                CoachLearnerAction.RequestCoach,
                shared ? CoachLearnerAction.Decline : CoachLearnerAction.ApplyNextCycle);
            SetLearnerRequestVisible(true);
            SetCoachActionLabel(
                CoachLearnerAction.RequestCoach,
                shared ? "Send Alternative Request" : "Send Request");
            SetCoachActionLabel(
                shared ? CoachLearnerAction.Decline : CoachLearnerAction.ApplyNextCycle,
                "Continue Without Coach");
            ApplyConditionIdentity(mode);
        }

        public void ShowConditionLoading(CoachOrchestrationMode mode, string status)
        {
            ShowCoach(status, "Anna is preparing concise feedback for your next revision.", false);
            ApplyConditionIdentity(mode);
        }

        public void ShowConditionFeedback(CoachOrchestrationMode mode, string feedback)
        {
            switch (mode)
            {
                case CoachOrchestrationMode.AiLed:
                    ShowCoach(
                        "AI-led feedback — Anna controls when coaching continues or practice resumes.",
                        feedback,
                        false,
                        CoachLearnerAction.ApplyNextCycle);
                    SetCoachActionLabel(
                        CoachLearnerAction.ApplyNextCycle,
                        "Continue to Revision");
                    break;
                case CoachOrchestrationMode.SharedControl:
                    ShowCoach(
                        "Feedback based on the agreed direction",
                        feedback,
                        false,
                        CoachLearnerAction.ChangeFocus,
                        CoachLearnerAction.Replay,
                        CoachLearnerAction.ApplyNextCycle);
                    SetCoachActionLabel(CoachLearnerAction.ChangeFocus, "Ask Something Else");
                    SetCoachActionLabel(CoachLearnerAction.Replay, "Replay");
                    SetCoachActionLabel(CoachLearnerAction.ApplyNextCycle, "Use Advice & Revise");
                    break;
                default:
                    ShowCoach(
                        "Feedback you requested",
                        feedback,
                        false,
                        CoachLearnerAction.RequestCoach,
                        CoachLearnerAction.Replay,
                        CoachLearnerAction.ApplyNextCycle);
                    SetLearnerRequestVisible(true);
                    SetCoachActionLabel(CoachLearnerAction.RequestCoach, "Ask Follow-up");
                    SetCoachActionLabel(CoachLearnerAction.Replay, "Replay");
                    SetCoachActionLabel(CoachLearnerAction.ApplyNextCycle, "Use Advice & Revise");
                    break;
            }

            ApplyConditionIdentity(mode);
        }

        public void ShowCompletion(bool dataComplete, string missingRequiredData)
        {
            HideCoachAgenda();
            SetPanels(false, false, false, false);
            if (_completionPanel != null) _completionPanel.SetActive(true);
            if (_completionStatus != null)
            {
                _completionStatus.text = dataComplete
                    ? "All three practices are complete. Continue to the independent transfer debate."
                    : "The scene ended, but required research data is missing: " +
                      (missingRequiredData?.Trim() ?? string.Empty);
            }

            Button continueButton = _completionPanel != null
                ? _completionPanel.GetComponentInChildren<Button>(true)
                : null;
            if (continueButton != null) continueButton.interactable = dataComplete;
        }

        public void SetSelectedFocus(string focus)
        {
            string normalized = CoachFocusCatalog.Normalize(focus, _selectedFocus);
            _selectedFocus = normalized;
            foreach (KeyValuePair<string, Button> pair in _focusButtons)
                SetButtonSelected(pair.Value,
                    string.Equals(pair.Key, normalized, StringComparison.OrdinalIgnoreCase));
        }

        public void SetLearnerRequestVisible(bool visible)
        {
            if (_learnerRequestSection != null) _learnerRequestSection.SetActive(visible);
            if (visible && _learnerRequest != null) _learnerRequest.DeactivateInputField();
        }

        public void SetLearnerRequestText(string text)
        {
            if (_learnerRequest != null) _learnerRequest.text = text ?? string.Empty;
        }

        public void SetLearnerRequestVoiceState(bool active, string status = "")
        {
            if (_learnerRequest != null)
            {
                _learnerRequest.interactable = !active;
                if (active) _learnerRequest.DeactivateInputField();
            }
            if (_learnerRequestVoiceHint != null)
            {
                _learnerRequestVoiceHint.text = string.IsNullOrWhiteSpace(status)
                    ? "Press T to start voice input, or click the field to type."
                    : status.Trim();
            }
        }

        public void ClearLearnerRequest()
        {
            if (_learnerRequest != null) _learnerRequest.text = string.Empty;
        }

        public void AppendFeedbackHistory(string learnerRequest, string feedback)
        {
            if (_feedbackHistory == null || string.IsNullOrWhiteSpace(feedback)) return;
            _feedbackHistoryIndex++;
            string request = string.IsNullOrWhiteSpace(learnerRequest)
                ? string.Empty
                : "You: " + learnerRequest.Trim() + "\n";
            string entry = _feedbackHistoryIndex + ".\n" + request +
                           "Anna: " + feedback.Trim();
            _feedbackHistory.text = _feedbackHistoryIndex == 1
                ? entry
                : _feedbackHistory.text + "\n\n" + entry;
        }

        public void ClearFeedbackHistory()
        {
            _feedbackHistoryIndex = 0;
            if (_feedbackHistory != null)
                _feedbackHistory.text = "No Coach feedback yet.";
        }

        public void ShowCoachAgenda(
            CoachAgendaSource source,
            string title,
            string text)
        {
            CurrentCoachAgendaSource = source;
            if (_coachAgendaTitle != null) _coachAgendaTitle.text = title?.Trim() ?? string.Empty;
            if (_coachAgendaText != null) _coachAgendaText.text = text?.Trim() ?? string.Empty;
            if (_coachAgendaBubble != null)
                _coachAgendaBubble.SetActive(!string.IsNullOrWhiteSpace(text));
            RefreshResponsiveLayout();
        }

        public void UpdateCoachAgenda(
            CoachAgendaSource source,
            string title,
            string text)
        {
            ShowCoachAgenda(source, title, text);
        }

        public void HideCoachAgenda()
        {
            CurrentCoachAgendaSource = CoachAgendaSource.None;
            if (_coachAgendaBubble != null) _coachAgendaBubble.SetActive(false);
            if (_coachAgendaTitle != null) _coachAgendaTitle.text = string.Empty;
            if (_coachAgendaText != null) _coachAgendaText.text = string.Empty;
        }

        public void SetTranscriptEditable(bool editable)
        {
            if (_transcript != null) _transcript.readOnly = !editable;
        }

        public void HideAll()
        {
            HideCoachAgenda();
            if (_root != null) _root.SetActive(false);
            if (_feedbackHistoryPanel != null) _feedbackHistoryPanel.SetActive(false);
            if (_historyToggleButton != null) _historyToggleButton.gameObject.SetActive(false);
        }

        public void SetRootVisible(bool visible)
        {
            if (_root != null) _root.SetActive(visible);
            if (!visible)
            {
                _historyContextVisible = false;
                _historyExpandedByUser = false;
                HideCoachAgenda();
                if (_feedbackHistoryPanel != null) _feedbackHistoryPanel.SetActive(false);
                if (_historyToggleButton != null)
                    _historyToggleButton.gameObject.SetActive(false);
                return;
            }
            RefreshResponsiveLayout();
        }

        private void LateUpdate()
        {
            if (_canvasRect == null) return;
            Vector2 size = _canvasRect.rect.size;
            Vector2 viewport = GetViewportPixelSize();
            if ((size - _lastCanvasSize).sqrMagnitude > 1f ||
                (viewport - _lastViewportPixelSize).sqrMagnitude > 1f)
                RefreshResponsiveLayout();
        }

        public void RefreshResponsiveLayout()
        {
            RefreshResponsiveLayout(GetViewportPixelSize());
        }

        private void RefreshResponsiveLayout(Vector2 viewportPixelSize)
        {
            Vector2 canvasSize = GetCanvasSize();
            _lastCanvasSize = canvasSize;
            _lastViewportPixelSize = viewportPixelSize;
            float canvasUnitsPerPixel = GetCanvasUnitsPerPixel(canvasSize, viewportPixelSize);
            ApplyStudyLayout(canvasSize, viewportPixelSize, canvasUnitsPerPixel);
            ApplyFeedbackHistoryLayout(canvasSize, viewportPixelSize, canvasUnitsPerPixel);
            ApplyAgendaLayout(canvasSize, viewportPixelSize, canvasUnitsPerPixel);
            ApplyHistoryToggleLayout(canvasUnitsPerPixel, viewportPixelSize);
            UpdateHistoryVisibility(viewportPixelSize.x);
        }

        private void ApplyStudyLayout()
        {
            Vector2 canvasSize = GetCanvasSize();
            Vector2 viewportPixelSize = GetViewportPixelSize();
            ApplyStudyLayout(canvasSize, viewportPixelSize,
                GetCanvasUnitsPerPixel(canvasSize, viewportPixelSize));
        }

        private void ApplyStudyLayout(
            Vector2 canvasSize,
            Vector2 viewportPixelSize,
            float canvasUnitsPerPixel)
        {
            RectTransform rect = RootRect;
            if (rect == null) return;
            rect.anchorMin = Vector2.up;
            rect.anchorMax = Vector2.up;
            rect.pivot = Vector2.up;
            rect.anchoredPosition = new Vector2(16f, -16f) * canvasUnitsPerPixel;
            float width = Mathf.Clamp(viewportPixelSize.x * 0.58f, 500f, 640f) *
                          canvasUnitsPerPixel;
            float height = Mathf.Clamp(viewportPixelSize.y - 32f, 600f, 760f) *
                           canvasUnitsPerPixel;
            rect.sizeDelta = new Vector2(width, height);
        }

        private void ApplyFeedbackHistoryLayout(Canvas canvas)
        {
            if (canvas != null) _canvasRect = canvas.transform as RectTransform;
            Vector2 canvasSize = GetCanvasSize();
            Vector2 viewportPixelSize = GetViewportPixelSize();
            ApplyFeedbackHistoryLayout(canvasSize, viewportPixelSize,
                GetCanvasUnitsPerPixel(canvasSize, viewportPixelSize));
        }

        private void ApplyFeedbackHistoryLayout(
            Vector2 canvasSize,
            Vector2 viewportPixelSize,
            float canvasUnitsPerPixel)
        {
            if (_feedbackHistoryPanel == null) return;
            RectTransform rect = _feedbackHistoryPanel.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.one;
            rect.anchorMax = Vector2.one;
            rect.pivot = Vector2.one;
            rect.anchoredPosition = new Vector2(-16f, -16f) * canvasUnitsPerPixel;
            float width = CalculateFeedbackHistoryWidth(viewportPixelSize.x);
            rect.sizeDelta = new Vector2(
                width * canvasUnitsPerPixel,
                Mathf.Clamp(viewportPixelSize.y - 160f, 360f, 720f) * canvasUnitsPerPixel);
        }

        private void ApplyAgendaLayout(
            Vector2 canvasSize,
            Vector2 viewportPixelSize,
            float canvasUnitsPerPixel)
        {
            if (_coachAgendaBubble == null || RootRect == null) return;
            RectTransform rect = _coachAgendaBubble.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.up;
            rect.anchorMax = Vector2.up;
            rect.pivot = Vector2.up;
            float margin = 16f * canvasUnitsPerPixel;
            float left = RootRect.anchoredPosition.x + RootRect.sizeDelta.x + margin;
            bool historyDocked = viewportPixelSize.x >= HistoryAutoExpandedMinViewportWidth;
            float right = canvasSize.x - margin -
                          (historyDocked
                              ? CalculateFeedbackHistoryWidth(viewportPixelSize.x) *
                                canvasUnitsPerPixel
                              : 0f);
            float availablePixelWidth = (right - left - margin) / canvasUnitsPerPixel;
            float width = Mathf.Clamp(availablePixelWidth, 300f, 620f) * canvasUnitsPerPixel;
            rect.anchoredPosition = new Vector2(left, -margin);
            rect.sizeDelta = new Vector2(width, 150f * canvasUnitsPerPixel);
        }

        private void ApplyHistoryToggleLayout(
            float canvasUnitsPerPixel,
            Vector2 viewportPixelSize)
        {
            if (_historyToggleButton == null) return;
            RectTransform rect = _historyToggleButton.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.one;
            rect.anchorMax = Vector2.one;
            rect.pivot = Vector2.one;
            bool belowAgenda = viewportPixelSize.x < HistoryAutoExpandedMinViewportWidth &&
                               IsCoachAgendaVisible;
            float top = belowAgenda ? 174f : 16f;
            rect.anchoredPosition = new Vector2(-16f, -top) * canvasUnitsPerPixel;
            rect.sizeDelta = new Vector2(132f, 44f) * canvasUnitsPerPixel;
        }

        private Vector2 GetCanvasSize()
        {
            float width = _canvasRect != null && _canvasRect.rect.width > 0f
                ? _canvasRect.rect.width
                : Screen.width > 0 ? Screen.width : 1600f;
            float height = _canvasRect != null && _canvasRect.rect.height > 0f
                ? _canvasRect.rect.height
                : Screen.height > 0 ? Screen.height : 900f;
            return new Vector2(width, height);
        }

        private Vector2 GetViewportPixelSize()
        {
            if (!Application.isPlaying) return GetCanvasSize();
            float width = Screen.width > 0 ? Screen.width : GetCanvasSize().x;
            float height = Screen.height > 0 ? Screen.height : GetCanvasSize().y;
            return new Vector2(width, height);
        }

        private static float GetCanvasUnitsPerPixel(Vector2 canvasSize, Vector2 viewportPixelSize)
        {
            return canvasSize.x > 0f && viewportPixelSize.x > 0f
                ? canvasSize.x / viewportPixelSize.x
                : 1f;
        }

        private void ToggleHistoryPanel()
        {
            _historyExpandedByUser = !_historyExpandedByUser;
            UpdateHistoryVisibility(GetViewportPixelSize().x);
        }

        private void UpdateHistoryVisibility(float canvasWidth)
        {
            bool narrow = canvasWidth < HistoryAutoExpandedMinViewportWidth;
            bool showPanel = _historyContextVisible && (!narrow || _historyExpandedByUser);
            if (_feedbackHistoryPanel != null) _feedbackHistoryPanel.SetActive(showPanel);
            if (_historyToggleButton != null)
                _historyToggleButton.gameObject.SetActive(_historyContextVisible && narrow);
            if (_historyToggleLabel != null)
                _historyToggleLabel.text = _historyExpandedByUser ? "Close History" : "History";
        }

        private static float CalculateFeedbackHistoryWidth(float viewportPixelWidth)
        {
            float safeViewportWidth = Mathf.Max(320f, viewportPixelWidth);
            float studyWidth = Mathf.Clamp(safeViewportWidth * 0.58f, 500f, 640f);
            if (safeViewportWidth < HistoryAutoExpandedMinViewportWidth)
            {
                return Mathf.Min(studyWidth, Mathf.Max(320f, safeViewportWidth - 32f));
            }

            const float minimumAgendaWidth = 300f;
            const float fixedMarginsAndGaps = 64f;
            float maximumWidthWithAgenda = safeViewportWidth - studyWidth -
                                           minimumAgendaWidth - fixedMarginsAndGaps;
            return Mathf.Clamp(
                Mathf.Min(studyWidth, maximumWidthWithAgenda),
                360f,
                640f);
        }

        private void BeginSession()
        {
            if (string.IsNullOrWhiteSpace(_assignedParticipantId))
            {
                ShowSetup("No active participant session. Start the study from Scene 01.");
                return;
            }
            SessionStartRequested?.Invoke(_assignedParticipantId, _selectedMode);
        }

        private void AddCoachButton(string label, CoachLearnerAction action)
        {
            Button button = AddButton(_coachPanel.transform, label,
                () => LearnerActionRequested?.Invoke(action,
                    action is CoachLearnerAction.RequestCoach or CoachLearnerAction.ChangeFocus
                        ? LearnerRequestText
                        : SelectedFocus));
            _coachButtons[action] = button;
            _coachButtonLabels[action] = button.GetComponentInChildren<TMP_Text>(true);
        }

        private void AddModeButton(Transform parent, string label, CoachOrchestrationMode mode)
        {
            _modeButtons[mode] = AddButton(parent, label, () => SelectMode(mode));
        }

        private void SelectMode(CoachOrchestrationMode mode)
        {
            _selectedMode = mode;
            foreach (KeyValuePair<CoachOrchestrationMode, Button> pair in _modeButtons)
                SetButtonSelected(pair.Value, pair.Key == mode);
        }

        private void ApplyConditionIdentity(CoachOrchestrationMode mode)
        {
            string label;
            Color color;
            switch (mode)
            {
                case CoachOrchestrationMode.AiLed:
                    label = "AI-LED  |  COACH DECIDES";
                    color = new Color(0.68f, 0.24f, 0.16f, 0.98f);
                    break;
                case CoachOrchestrationMode.SharedControl:
                    label = "SHARED CONTROL  |  AI SUGGESTS, YOU DECIDE";
                    color = new Color(0.08f, 0.35f, 0.62f, 0.98f);
                    break;
                default:
                    label = "LEARNER-LED  |  YOU DECIDE WHETHER TO ASK";
                    color = new Color(0.08f, 0.48f, 0.30f, 0.98f);
                    break;
            }

            if (_conditionAuthorityText != null) _conditionAuthorityText.text = label;
            if (_conditionAuthorityBanner != null &&
                _conditionAuthorityBanner.TryGetComponent(out Image image))
                image.color = color;
        }

        private void SetCoachActionLabel(CoachLearnerAction action, string label)
        {
            if (_coachButtonLabels.TryGetValue(action, out TMP_Text text) && text != null)
                text.text = label ?? string.Empty;
        }

        private void ResetCoachActionLabels()
        {
            SetCoachActionLabel(CoachLearnerAction.RequestCoach, "Ask Coach");
            SetCoachActionLabel(CoachLearnerAction.ConfirmFocus, "Confirm Focus");
            SetCoachActionLabel(CoachLearnerAction.Accept, "Accept");
            SetCoachActionLabel(CoachLearnerAction.ChangeFocus, "Change Focus");
            SetCoachActionLabel(CoachLearnerAction.Decline, "Decline");
            SetCoachActionLabel(CoachLearnerAction.NeedExample, "Continue");
            SetCoachActionLabel(CoachLearnerAction.Replay, "Replay");
            SetCoachActionLabel(CoachLearnerAction.ApplyNextCycle, "End Coaching");
            SetCoachActionLabel(CoachLearnerAction.Override, "Override");
            SetCoachActionLabel(CoachLearnerAction.RetryFeedback, "Retry Feedback");
        }

        private GameObject AddFocusSelector(Transform parent)
        {
            GameObject selector = new("Focus Selector", typeof(RectTransform), typeof(GridLayoutGroup),
                typeof(LayoutElement));
            selector.transform.SetParent(parent, false);
            selector.GetComponent<LayoutElement>().minHeight = 126f;
            GridLayoutGroup grid = selector.GetComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(158f, 36f);
            grid.spacing = new Vector2(8f, 7f);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 3;
            foreach (string focus in CoachFocusCatalog.Values)
            {
                string captured = focus;
                Button button = AddButton(selector.transform, captured,
                    () => SetSelectedFocus(captured));
                button.gameObject.name = "Focus " + captured;
                _focusButtons[captured] = button;
            }
            SetSelectedFocus(_selectedFocus);
            return selector;
        }

        private void SetPanels(bool setup, bool introduction, bool practice, bool coach)
        {
            if (_root != null) _root.SetActive(true);
            if (_setupPanel != null) _setupPanel.SetActive(setup);
            if (_introductionPanel != null) _introductionPanel.SetActive(introduction);
            if (_practicePanel != null) _practicePanel.SetActive(practice);
            if (_coachPanel != null) _coachPanel.SetActive(coach);
            if (_completionPanel != null) _completionPanel.SetActive(false);
            _historyContextVisible = practice || coach;
            if (!_historyContextVisible) _historyExpandedByUser = false;
            Vector2 canvasSize = GetCanvasSize();
            Vector2 viewportPixelSize = GetViewportPixelSize();
            ApplyHistoryToggleLayout(
                GetCanvasUnitsPerPixel(canvasSize, viewportPixelSize),
                viewportPixelSize);
            UpdateHistoryVisibility(GetViewportPixelSize().x);
        }

        private static string FormatTime(float seconds)
        {
            int value = Mathf.Max(0, Mathf.FloorToInt(seconds));
            return $"{value / 60:00}:{value % 60:00}";
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
            layout.padding = new RectOffset(16, 16, 16, 16);
            layout.spacing = 9f;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            return panel;
        }

        private static TMP_Text AddText(Transform parent, string value, int size,
            FontStyles style = FontStyles.Normal)
        {
            GameObject go = new("Text", typeof(RectTransform), typeof(TextMeshProUGUI), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            TMP_Text text = go.GetComponent<TMP_Text>();
            text.text = value;
            text.fontSize = size;
            text.fontStyle = style;
            text.color = Color.white;
            text.textWrappingMode = TextWrappingModes.Normal;
            go.GetComponent<LayoutElement>().minHeight = size + 14f;
            return text;
        }

        private static TMP_Text AddScrollableText(
            Transform parent,
            string name,
            float minHeight,
            float preferredHeight)
        {
            GameObject root = new(name, typeof(RectTransform), typeof(Image),
                typeof(ScrollRect), typeof(LayoutElement));
            root.transform.SetParent(parent, false);
            root.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.18f);
            LayoutElement element = root.GetComponent<LayoutElement>();
            element.minHeight = minHeight;
            element.preferredHeight = preferredHeight;
            element.flexibleHeight = 1f;

            GameObject viewport = new("Viewport", typeof(RectTransform),
                typeof(Image), typeof(RectMask2D));
            viewport.transform.SetParent(root.transform, false);
            viewport.GetComponent<Image>().color = Color.clear;
            Stretch(viewport.GetComponent<RectTransform>(), 8f);

            GameObject content = new("Scrollable Text", typeof(RectTransform),
                typeof(TextMeshProUGUI), typeof(ContentSizeFitter));
            content.transform.SetParent(viewport.transform, false);
            RectTransform contentRect = content.GetComponent<RectTransform>();
            contentRect.anchorMin = new Vector2(0f, 1f);
            contentRect.anchorMax = new Vector2(1f, 1f);
            contentRect.pivot = new Vector2(0.5f, 1f);
            contentRect.anchoredPosition = Vector2.zero;
            contentRect.sizeDelta = Vector2.zero;
            TMP_Text text = content.GetComponent<TMP_Text>();
            text.text = string.Empty;
            text.fontSize = 17f;
            text.color = Color.white;
            text.textWrappingMode = TextWrappingModes.Normal;
            ContentSizeFitter fitter = content.GetComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            ScrollRect scroll = root.GetComponent<ScrollRect>();
            scroll.viewport = viewport.GetComponent<RectTransform>();
            scroll.content = contentRect;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            return text;
        }

        private static TMP_InputField AddInput(Transform parent, string placeholder, bool multiline)
        {
            GameObject root = new("Input", typeof(RectTransform), typeof(Image), typeof(TMP_InputField),
                typeof(LayoutElement));
            root.transform.SetParent(parent, false);
            root.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.1f);
            root.GetComponent<LayoutElement>().minHeight = multiline ? 170f : 48f;
            GameObject viewport = new("Text Area", typeof(RectTransform), typeof(RectMask2D));
            viewport.transform.SetParent(root.transform, false);
            Stretch(viewport.GetComponent<RectTransform>(), 8f);
            TMP_Text text = AddText(viewport.transform, string.Empty, 17);
            Stretch(text.rectTransform, 0f);
            TMP_Text hint = AddText(viewport.transform, placeholder, 17);
            hint.color = new Color(1f, 1f, 1f, 0.45f);
            Stretch(hint.rectTransform, 0f);
            TMP_InputField input = root.GetComponent<TMP_InputField>();
            input.textViewport = viewport.GetComponent<RectTransform>();
            input.textComponent = text;
            input.placeholder = hint;
            input.lineType = multiline
                ? TMP_InputField.LineType.MultiLineNewline
                : TMP_InputField.LineType.SingleLine;
            return input;
        }

        private static GameObject AddButtonRow(Transform parent, string name, float minHeight)
        {
            GameObject go = new(name, typeof(RectTransform), typeof(HorizontalLayoutGroup),
                typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            LayoutElement rowElement = go.GetComponent<LayoutElement>();
            rowElement.minHeight = minHeight;
            rowElement.preferredHeight = minHeight;
            rowElement.flexibleHeight = 0f;
            HorizontalLayoutGroup layout = go.GetComponent<HorizontalLayoutGroup>();
            layout.spacing = 8f;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandWidth = true;
            return go;
        }

        private static TMP_Text AddFeedbackHistory(Transform parent)
        {
            GameObject root = new("Feedback History", typeof(RectTransform), typeof(Image),
                typeof(ScrollRect), typeof(LayoutElement));
            root.transform.SetParent(parent, false);
            root.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.22f);
            root.GetComponent<LayoutElement>().minHeight = 190f;
            root.GetComponent<LayoutElement>().flexibleHeight = 1f;
            GameObject viewport = new("Viewport", typeof(RectTransform), typeof(Image), typeof(RectMask2D));
            viewport.transform.SetParent(root.transform, false);
            viewport.GetComponent<Image>().color = Color.clear;
            Stretch(viewport.GetComponent<RectTransform>(), 8f);
            GameObject content = new("Feedback History Text", typeof(RectTransform),
                typeof(TextMeshProUGUI), typeof(ContentSizeFitter));
            content.transform.SetParent(viewport.transform, false);
            RectTransform contentRect = content.GetComponent<RectTransform>();
            contentRect.anchorMin = new Vector2(0f, 1f);
            contentRect.anchorMax = new Vector2(1f, 1f);
            contentRect.pivot = new Vector2(0.5f, 1f);
            contentRect.anchoredPosition = Vector2.zero;
            contentRect.sizeDelta = Vector2.zero;
            TMP_Text text = content.GetComponent<TMP_Text>();
            text.text = string.Empty;
            text.fontSize = 16f;
            text.color = Color.white;
            text.textWrappingMode = TextWrappingModes.Normal;
            ContentSizeFitter fitter = content.GetComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            ScrollRect scroll = root.GetComponent<ScrollRect>();
            scroll.viewport = viewport.GetComponent<RectTransform>();
            scroll.content = contentRect;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            return text;
        }

        private static void SetButtonSelected(Button button, bool selected)
        {
            if (button == null) return;
            Image image = button.GetComponent<Image>();
            if (image != null)
                image.color = selected
                    ? new Color(0.12f, 0.62f, 0.42f, 1f)
                    : new Color(0.12f, 0.38f, 0.67f, 1f);
        }

        private static Button AddButton(Transform parent, string label, Action action)
        {
            GameObject go = new(label, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = new Color(0.12f, 0.38f, 0.67f, 1f);
            go.GetComponent<LayoutElement>().minHeight = 42f;
            TMP_Text text = AddText(go.transform, label, 16, FontStyles.Bold);
            text.alignment = TextAlignmentOptions.Center;
            Stretch(text.rectTransform, 0f);
            Button button = go.GetComponent<Button>();
            button.onClick.AddListener(() => action?.Invoke());
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
