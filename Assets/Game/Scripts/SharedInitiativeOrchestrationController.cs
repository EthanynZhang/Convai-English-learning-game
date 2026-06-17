using System;
using System.Collections;
using System.Linq;
using System.Text;
using Convai.Scripts.Runtime.Core;
using Convai.Scripts.Runtime.Features;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Debate
{
    public enum OrchestrationPhase
    {
        Intro,
        ConversationTurn,
        CoachPaused,
        CoachSuggestionReady,
        Complete
    }

    [Serializable]
    public sealed class ConditionCSessionRecord
    {
        public string topic;
        public string learnerStance;
        public string taskGoal;
        public int completedTurns;
        public string latestLearnerNote;
        public string latestCoachSuggestion;
    }

    public sealed class SharedInitiativeOrchestrationController : MonoBehaviour
    {
        [Header("Agents")]
        [SerializeField] private NPC2NPCConversationManager conversationManager;
        [SerializeField] private ConvaiNPC conversationNPC;
        [SerializeField] private ConvaiNPC coachNPC;

        [Header("Task")]
        [SerializeField]
        private string debateTopic =
            "Reading and speaking, which is more important in learning English?";
        [SerializeField] private string learnerStance = "Speaking is more important for learning English.";
        [SerializeField] private string taskGoal = "Discuss with the NPC, pause for coach advice, then continue.";
        [SerializeField] private int maxConversationTurns = 3;
        [TextArea(2, 6)]
        [SerializeField]
        private string npcOpeningPrompt =
            "The debate topic is: {0} You are the opposing side. Argue that reading is more important in learning English. Present one short opening argument, then wait for the learner to respond.";
        [TextArea(2, 6)]
        [SerializeField]
        private string npcFollowUpPrompt =
            "The learner has responded. Ask one concise follow-up question or rebuttal that challenges the claim that speaking is more important. Keep it under 20 seconds, then wait.";
        [TextArea(2, 4)]
        [SerializeField]
        private string closingText =
            "Good work. The guided conversation is complete.";

        [Header("UI")]
        [SerializeField] private Canvas uiCanvas;
        [SerializeField] private GameObject legacyInteractiveControls;
        [SerializeField] private GameObject legacyStartButton;
        [SerializeField] private bool unlockCursorForControlUi = true;

        [Header("Timing")]
        [SerializeField] private float coachResponseTimeoutSeconds = 18f;

        public ConditionCSessionRecord SessionRecord = new();
        public OrchestrationPhase Phase { get; private set; } = OrchestrationPhase.Intro;

        private readonly StringBuilder _coachTranscript = new();

        private GameObject _root;
        private TMP_Text _titleText;
        private TMP_Text _taskText;
        private TMP_Text _statusText;
        private TMP_Text _coachText;
        private TMP_InputField _learnerNoteInput;
        private Button _startButton;
        private Button _pauseForCoachButton;
        private Button _continueButton;
        private Button _askAgainButton;
        private Button _endButton;
        private Coroutine _coachRoutine;
        private int _turnIndex;

        private void Awake()
        {
            BuildUi();
            HideLegacyUi();
        }

        private IEnumerator Start()
        {
            SessionRecord.topic = debateTopic;
            SessionRecord.learnerStance = learnerStance;
            SessionRecord.taskGoal = taskGoal;

            if (conversationManager != null)
            {
                conversationManager.RelayInterceptor = null;
            }

            SubscribeToCoachAudio();

            yield return null;
            EnterIntro();
        }

        private void OnDestroy()
        {
            if (_coachRoutine != null)
            {
                StopCoroutine(_coachRoutine);
                _coachRoutine = null;
            }

            UnsubscribeFromCoachAudio();
            SetControlCursor(false);
        }

        public void BeginConditionC()
        {
            if (Phase != OrchestrationPhase.Intro && Phase != OrchestrationPhase.Complete)
            {
                return;
            }

            if (conversationNPC == null)
            {
                SetStatus("Assign a conversation NPC before starting.");
                return;
            }

            _turnIndex = 0;
            SessionRecord.completedTurns = 0;
            SessionRecord.latestLearnerNote = string.Empty;
            SessionRecord.latestCoachSuggestion = string.Empty;
            _learnerNoteInput.text = string.Empty;
            _coachText.text = "Coach advice will appear here after you pause.";

            StartNpcTurn(FormatTaskText(npcOpeningPrompt));
        }

        public void PauseForCoach()
        {
            if (Phase != OrchestrationPhase.ConversationTurn)
            {
                return;
            }

            Phase = OrchestrationPhase.CoachPaused;
            SessionRecord.completedTurns = Mathf.Max(SessionRecord.completedTurns, _turnIndex);
            SessionRecord.latestLearnerNote = _learnerNoteInput.text.Trim();
            ConvaiNPCManager.Instance?.SetActiveConvaiNPC(coachNPC);
            SetControlCursor(true);
            SetStatus("Paused. Coach is preparing advice...");
            SetButtonsForPhase();

            if (_coachRoutine != null)
            {
                StopCoroutine(_coachRoutine);
            }

            _coachRoutine = StartCoroutine(RequestCoachAdvice());
        }

        public void ContinueConversation()
        {
            if (Phase != OrchestrationPhase.CoachSuggestionReady)
            {
                return;
            }

            if (_turnIndex >= maxConversationTurns)
            {
                EndConditionC();
                return;
            }

            _learnerNoteInput.text = string.Empty;
            StartNpcTurn(FormatTaskText(npcFollowUpPrompt));
        }

        public void AskCoachAgain()
        {
            if (Phase != OrchestrationPhase.CoachSuggestionReady && Phase != OrchestrationPhase.CoachPaused)
            {
                return;
            }

            Phase = OrchestrationPhase.CoachPaused;
            SessionRecord.latestLearnerNote = _learnerNoteInput.text.Trim();
            ConvaiNPCManager.Instance?.SetActiveConvaiNPC(coachNPC);
            SetStatus("Coach is revising the suggestion...");
            SetButtonsForPhase();

            if (_coachRoutine != null)
            {
                StopCoroutine(_coachRoutine);
            }

            _coachRoutine = StartCoroutine(RequestCoachAdvice());
        }

        public void EndConditionC()
        {
            Phase = OrchestrationPhase.Complete;
            ConvaiNPCManager.Instance?.SetActiveConvaiNPC(coachNPC != null ? coachNPC : conversationNPC);
            SetControlCursor(true);
            SetStatus(closingText);
            _coachText.text = "Session complete.";
            if (coachNPC != null)
            {
                coachNPC.SendTextDataAsync(closingText);
            }
            SetButtonsForPhase();
        }

        public void AdvanceFormalDebate()
        {
            ContinueConversation();
        }

        private void StartNpcTurn(string prompt)
        {
            Phase = OrchestrationPhase.ConversationTurn;
            _turnIndex++;
            ConvaiNPCManager.Instance?.SetActiveConvaiNPC(conversationNPC);
            SetControlCursor(false);
            SetStatus($"Turn {_turnIndex}: listen to the NPC, respond by voice, then press Pause For Coach.");
            SetButtonsForPhase();
            conversationNPC.SendTextDataAsync(prompt);
        }

        private IEnumerator RequestCoachAdvice()
        {
            if (coachNPC == null)
            {
                ApplyFallbackAdvice();
                yield break;
            }

            _coachTranscript.Clear();
            _coachText.text = "Coach is thinking...";
            coachNPC.SendTextDataAsync(BuildCoachPrompt());

            float timeout = coachResponseTimeoutSeconds;
            while (timeout > 0f)
            {
                timeout -= Time.deltaTime;
                if (_coachTranscript.Length > 0 && !coachNPC.IsCharacterTalking)
                {
                    break;
                }

                yield return null;
            }

            string advice = ExtractAdvice(_coachTranscript.ToString());
            if (string.IsNullOrWhiteSpace(advice))
            {
                ApplyFallbackAdvice();
                yield break;
            }

            SessionRecord.latestCoachSuggestion = advice;
            _coachText.text = advice;
            Phase = OrchestrationPhase.CoachSuggestionReady;
            SetStatus("Coach advice is ready. Press Continue when you want the NPC conversation to resume.");
            SetButtonsForPhase();
        }

        private string BuildCoachPrompt()
        {
            string learnerNote = string.IsNullOrWhiteSpace(SessionRecord.latestLearnerNote)
                ? "No learner transcript was typed. Infer from the task context and give one general next-step suggestion."
                : SessionRecord.latestLearnerNote;

            return
                "You are a debate coach for an English learning task.\n" +
                $"Debate topic: {debateTopic}\n" +
                $"Learner stance: {learnerStance}\n" +
                $"Task goal: {taskGoal}\n" +
                $"Current turn: {_turnIndex}\n\n" +
                "Learner's latest response or note:\n" +
                $"{learnerNote}\n\n" +
                "Give one short, actionable suggestion before the learner continues. " +
                "Use one of Logos, Ethos, or Pathos if helpful. " +
                "Output exactly:\n" +
                "COACH_ADVICE: one or two short sentences";
        }

        private void ApplyFallbackAdvice()
        {
            string advice = "Logos: add one clear reason and one example before you answer the next challenge.";
            SessionRecord.latestCoachSuggestion = advice;
            _coachText.text = advice;
            Phase = OrchestrationPhase.CoachSuggestionReady;
            SetStatus("Coach advice is ready. Press Continue when you want the NPC conversation to resume.");
            SetButtonsForPhase();
        }

        private static string ExtractAdvice(string response)
        {
            if (string.IsNullOrWhiteSpace(response))
            {
                return string.Empty;
            }

            string trimmed = response.Trim();
            string marker = "COACH_ADVICE:";
            int markerIndex = trimmed.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (markerIndex >= 0)
            {
                return trimmed[(markerIndex + marker.Length)..].Trim();
            }

            return trimmed;
        }

        private void CaptureCoachAudio(ConvaiNPCAudioManager.ResponseAudio response)
        {
            if (response == null || response.IsFinal || string.IsNullOrWhiteSpace(response.AudioTranscript))
            {
                return;
            }

            if (_coachTranscript.Length > 0)
            {
                _coachTranscript.Append(' ');
            }

            _coachTranscript.Append(response.AudioTranscript.Trim());
        }

        private void SubscribeToCoachAudio()
        {
            if (coachNPC != null && coachNPC.AudioManager != null)
            {
                coachNPC.AudioManager.OnResponseAudioStarted += CaptureCoachAudio;
            }
        }

        private void UnsubscribeFromCoachAudio()
        {
            if (coachNPC != null && coachNPC.AudioManager != null)
            {
                coachNPC.AudioManager.OnResponseAudioStarted -= CaptureCoachAudio;
            }
        }

        private void EnterIntro()
        {
            Phase = OrchestrationPhase.Intro;
            SetControlCursor(true);
            SetStatus("Press Start Conversation when you are ready.");
            _coachText.text = "Coach advice will appear here after you pause.";
            SetButtonsForPhase();
        }

        private void BuildUi()
        {
            if (uiCanvas == null)
            {
                uiCanvas = FindObjectsByType<Canvas>(FindObjectsInactive.Exclude)
                    .FirstOrDefault(canvas => canvas.name == "Debate Round UI");
            }

            if (uiCanvas == null)
            {
                SetStatus("Condition C UI needs a canvas.");
                return;
            }

            _root = CreateRect("Shared Initiative Controls", uiCanvas.transform);
            RectTransform rootRect = _root.GetComponent<RectTransform>();
            rootRect.anchorMin = new Vector2(0f, 1f);
            rootRect.anchorMax = new Vector2(0f, 1f);
            rootRect.pivot = new Vector2(0f, 1f);
            rootRect.anchoredPosition = new Vector2(24f, -24f);
            rootRect.sizeDelta = new Vector2(520f, 620f);

            Image background = _root.AddComponent<Image>();
            background.color = new Color(0.05f, 0.07f, 0.09f, 0.72f);

            VerticalLayoutGroup layout = _root.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(18, 18, 16, 16);
            layout.spacing = 10f;
            layout.childControlWidth = true;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            _titleText = CreateText(_root.transform, "Condition C: Shared Initiative", 24, FontStyles.Bold, 34f);
            _taskText = CreateText(_root.transform, string.Empty, 15, FontStyles.Normal, 92f);
            _statusText = CreateText(_root.transform, string.Empty, 15, FontStyles.Bold, 54f);
            _coachText = CreateText(_root.transform, string.Empty, 16, FontStyles.Normal, 112f);
            _learnerNoteInput = CreateInput(_root.transform, "Optional: type what you just said before asking the coach.", 82f);

            _startButton = CreateButton(_root.transform, "Start Conversation", BeginConditionC, new Color(0.20f, 0.48f, 0.36f));
            _pauseForCoachButton = CreateButton(_root.transform, "Pause For Coach", PauseForCoach, new Color(0.72f, 0.50f, 0.18f));
            _continueButton = CreateButton(_root.transform, "Continue Conversation", ContinueConversation, new Color(0.24f, 0.43f, 0.70f));
            _askAgainButton = CreateButton(_root.transform, "Ask Coach Again", AskCoachAgain, new Color(0.42f, 0.36f, 0.64f));
            _endButton = CreateButton(_root.transform, "End Session", EndConditionC, new Color(0.62f, 0.22f, 0.20f));

            _taskText.text =
                $"Topic: {debateTopic}\n" +
                $"Your stance: {learnerStance}\n" +
                $"Goal: {taskGoal}";
        }

        private void HideLegacyUi()
        {
            if (legacyInteractiveControls == null)
            {
                legacyInteractiveControls = GameObject.Find("Interactive Controls");
            }

            if (legacyStartButton == null)
            {
                legacyStartButton = GameObject.Find("Start Debate Button");
            }

            legacyInteractiveControls?.SetActive(false);
            legacyStartButton?.SetActive(false);
        }

        private void SetButtonsForPhase()
        {
            bool intro = Phase == OrchestrationPhase.Intro || Phase == OrchestrationPhase.Complete;
            bool conversation = Phase == OrchestrationPhase.ConversationTurn;
            bool waitingCoach = Phase == OrchestrationPhase.CoachPaused;
            bool coachReady = Phase == OrchestrationPhase.CoachSuggestionReady;

            SetActive(_startButton, intro);
            SetActive(_pauseForCoachButton, conversation);
            SetActive(_continueButton, coachReady);
            SetActive(_askAgainButton, coachReady);
            SetActive(_endButton, conversation || waitingCoach || coachReady);

            SetButtonInteractable(_pauseForCoachButton, conversation);
            SetButtonInteractable(_continueButton, coachReady);
            SetButtonInteractable(_askAgainButton, coachReady);

            if (_continueButton != null)
            {
                TMP_Text label = _continueButton.GetComponentInChildren<TMP_Text>();
                if (label != null)
                {
                    label.text = _turnIndex >= maxConversationTurns ? "Finish" : "Continue Conversation";
                }
            }
        }

        private void SetStatus(string text)
        {
            if (_statusText != null)
            {
                _statusText.text = text;
            }
        }

        private void SetControlCursor(bool visible)
        {
            if (!unlockCursorForControlUi)
            {
                return;
            }

            Cursor.lockState = visible ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = visible;
        }

        private string FormatTaskText(string template)
        {
            if (string.IsNullOrWhiteSpace(template))
            {
                return string.Empty;
            }

            return template.Contains("{0}", StringComparison.Ordinal)
                ? string.Format(template, debateTopic)
                : template;
        }

        private static GameObject CreateRect(string name, Transform parent)
        {
            GameObject gameObject = new(name, typeof(RectTransform));
            gameObject.transform.SetParent(parent, false);
            return gameObject;
        }

        private static TMP_Text CreateText(Transform parent, string text, int fontSize, FontStyles style, float height)
        {
            GameObject textObject = CreateRect("Text", parent);
            TMP_Text textComponent = textObject.AddComponent<TextMeshProUGUI>();
            textComponent.text = text;
            textComponent.fontSize = fontSize;
            textComponent.fontStyle = style;
            textComponent.color = Color.white;
            textComponent.alignment = TextAlignmentOptions.Left;
            textComponent.textWrappingMode = TextWrappingModes.Normal;
            textComponent.raycastTarget = false;

            LayoutElement layout = textObject.AddComponent<LayoutElement>();
            layout.preferredHeight = height;
            layout.minHeight = height;
            return textComponent;
        }

        private static Button CreateButton(Transform parent, string label, UnityEngine.Events.UnityAction action, Color color)
        {
            GameObject buttonObject = CreateRect(label, parent);
            Image image = buttonObject.AddComponent<Image>();
            image.color = color;

            Button button = buttonObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(action);

            LayoutElement layout = buttonObject.AddComponent<LayoutElement>();
            layout.preferredHeight = 42f;
            layout.minHeight = 42f;

            GameObject labelObject = CreateRect("Label", buttonObject.transform);
            RectTransform labelRect = labelObject.GetComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;

            TMP_Text text = labelObject.AddComponent<TextMeshProUGUI>();
            text.text = label;
            text.fontSize = 16;
            text.fontStyle = FontStyles.Bold;
            text.color = Color.white;
            text.alignment = TextAlignmentOptions.Center;
            text.raycastTarget = false;
            return button;
        }

        private static TMP_InputField CreateInput(Transform parent, string placeholder, float height)
        {
            GameObject inputObject = CreateRect("Learner Note Input", parent);
            Image image = inputObject.AddComponent<Image>();
            image.color = new Color(1f, 1f, 1f, 0.92f);

            LayoutElement layout = inputObject.AddComponent<LayoutElement>();
            layout.preferredHeight = height;
            layout.minHeight = height;

            TMP_InputField inputField = inputObject.AddComponent<TMP_InputField>();
            inputField.lineType = TMP_InputField.LineType.MultiLineNewline;

            GameObject textObject = CreateRect("Text", inputObject.transform);
            RectTransform textRect = textObject.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(10f, 8f);
            textRect.offsetMax = new Vector2(-10f, -8f);

            TMP_Text textComponent = textObject.AddComponent<TextMeshProUGUI>();
            textComponent.fontSize = 14;
            textComponent.color = Color.black;
            textComponent.textWrappingMode = TextWrappingModes.Normal;

            GameObject placeholderObject = CreateRect("Placeholder", inputObject.transform);
            RectTransform placeholderRect = placeholderObject.GetComponent<RectTransform>();
            placeholderRect.anchorMin = Vector2.zero;
            placeholderRect.anchorMax = Vector2.one;
            placeholderRect.offsetMin = new Vector2(10f, 8f);
            placeholderRect.offsetMax = new Vector2(-10f, -8f);

            TMP_Text placeholderText = placeholderObject.AddComponent<TextMeshProUGUI>();
            placeholderText.text = placeholder;
            placeholderText.fontSize = 14;
            placeholderText.color = new Color(0f, 0f, 0f, 0.48f);
            placeholderText.textWrappingMode = TextWrappingModes.Normal;

            inputField.textComponent = textComponent;
            inputField.placeholder = placeholderText;
            return inputField;
        }

        private static void SetActive(Selectable selectable, bool active)
        {
            if (selectable != null)
            {
                selectable.gameObject.SetActive(active);
            }
        }

        private static void SetButtonInteractable(Button button, bool interactable)
        {
            if (button != null)
            {
                button.interactable = interactable;
            }
        }
    }
}
