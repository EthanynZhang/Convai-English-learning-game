using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Game.Debate
{
    [DisallowMultipleComponent]
    public sealed class DebugSceneJumpMenu : MonoBehaviour
    {
        private static readonly string[] TargetSceneNames =
        {
            "01Level_NPCVsNPCDebate",
            "03Level_PlayerVsNPCDebate",
            "04 coach Agent",
            "05Level_PlayerVsNPCDebate 1"
        };

        private static readonly string[] TargetSceneLabels =
        {
            "Scene 01  |  NPC vs NPC Tutorial",
            "Scene 03  |  Individual Baseline Practice",
            "Scene 04  |  Coach Agent Practice",
            "Scene 05  |  Transfer Debate"
        };

        private readonly Dictionary<string, Button> _sceneButtons = new();
        private readonly Dictionary<string, Text> _sceneButtonLabels = new();
        private GameObject _canvasRoot;
        private Button _conversationFlowButton;
        private Text _conversationFlowButtonLabel;
        private bool _isVisible;
        private float _lastConversationToggleTime = -10f;
        private CursorLockMode _previousCursorLockMode;
        private bool _previousCursorVisible;
        private Font _font;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            if (FindFirstObjectByType<DebugSceneJumpMenu>() != null) return;
            GameObject host = new("F5 Study Scene Menu");
            DontDestroyOnLoad(host);
            host.AddComponent<DebugSceneJumpMenu>();
        }

        public static string[] GetTargetSceneNames() =>
            (string[])TargetSceneNames.Clone();

        private void Awake()
        {
            ConversationFlowSettings.DisableConvaiChanged += HandleConversationFlowChanged;
            EnsureUi();
            _canvasRoot.SetActive(false);
        }

        private void Update()
        {
            if (!_isVisible && WasRightMousePressed())
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }

            if (WasF5Pressed())
            {
                SetVisible(!_isVisible);
                return;
            }

            if (_isVisible && WasEscapePressed()) SetVisible(false);
        }

        private void OnDestroy()
        {
            ConversationFlowSettings.DisableConvaiChanged -= HandleConversationFlowChanged;
            if (_isVisible) RestoreCursor();
        }

        private void EnsureUi()
        {
            if (_canvasRoot != null) return;

            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _canvasRoot = new GameObject(
                "F5 Scene Menu Canvas",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(GraphicRaycaster));
            _canvasRoot.transform.SetParent(transform, false);

            Canvas canvas = _canvasRoot.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.overrideSorting = true;
            canvas.sortingOrder = 32000;
            CanvasScaler scaler = _canvasRoot.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            Image blocker = CreateImage(
                _canvasRoot.transform,
                "Input Blocker",
                new Color(0f, 0f, 0f, 0.78f));
            Stretch(blocker.rectTransform);
            blocker.raycastTarget = true;

            Image panel = CreateImage(
                blocker.transform,
                "Scene Menu Panel",
                new Color(0.035f, 0.05f, 0.07f, 0.99f));
            RectTransform panelRect = panel.rectTransform;
            panelRect.anchorMin = panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.pivot = new Vector2(0.5f, 0.5f);
            panelRect.sizeDelta = new Vector2(720f, 820f);
            panelRect.anchoredPosition = Vector2.zero;

            CreateText(panel.transform, "Title", "STUDY SCENE MENU", 34,
                FontStyle.Bold, new Vector2(0f, 350f), new Vector2(650f, 55f));
            CreateText(panel.transform, "Subtitle", "Press F5 or Esc to close this menu", 18,
                FontStyle.Normal, new Vector2(0f, 305f), new Vector2(650f, 36f),
                new Color(0.8f, 0.84f, 0.9f));

            for (int index = 0; index < TargetSceneNames.Length; index++)
            {
                string sceneName = TargetSceneNames[index];
                string label = TargetSceneLabels[index];
                string capturedSceneName = sceneName;
                Button button = CreateButton(
                    panel.transform,
                    "Load " + sceneName,
                    label,
                    new Vector2(0f, 225f - index * 78f),
                    new Vector2(620f, 62f),
                    new Color(0.12f, 0.19f, 0.27f, 1f));
                button.onClick.AddListener(() => LoadDebugScene(capturedSceneName));
                _sceneButtons[sceneName] = button;
                _sceneButtonLabels[sceneName] = button.GetComponentInChildren<Text>(true);
            }

            _conversationFlowButton = CreateButton(
                panel.transform,
                "Toggle Convai Flow",
                string.Empty,
                new Vector2(0f, -105f),
                new Vector2(620f, 62f),
                new Color(0.06f, 0.3f, 0.24f, 1f));
            _conversationFlowButton.onClick.AddListener(ToggleConversationFlow);
            _conversationFlowButtonLabel =
                _conversationFlowButton.GetComponentInChildren<Text>(true);
            RefreshConversationFlowButton();

            Button exitButton = CreateButton(
                panel.transform,
                "Exit Application",
                "Exit Application",
                new Vector2(0f, -185f),
                new Vector2(620f, 62f),
                new Color(0.45f, 0.08f, 0.08f, 1f));
            exitButton.onClick.AddListener(ExitApplication);
            CreateText(panel.transform, "Footer",
                "The conversation mode is shared by every scene.", 17,
                FontStyle.Normal, new Vector2(0f, -275f), new Vector2(650f, 40f),
                new Color(0.8f, 0.84f, 0.9f));
        }

        private void RefreshSceneButtons()
        {
            string activeScene = SceneManager.GetActiveScene().name;
            for (int index = 0; index < TargetSceneNames.Length; index++)
            {
                string sceneName = TargetSceneNames[index];
                bool isCurrent = activeScene == sceneName;
                bool isLoadable = Application.CanStreamedLevelBeLoaded(sceneName);
                _sceneButtons[sceneName].interactable = !isCurrent && isLoadable;
                string suffix = isCurrent
                    ? "  (CURRENT)"
                    : isLoadable
                        ? string.Empty
                        : "  (NOT IN BUILD)";
                _sceneButtonLabels[sceneName].text = TargetSceneLabels[index] + suffix;
            }

            RefreshConversationFlowButton();
        }

        private void ToggleConversationFlow()
        {
            if (Time.unscaledTime - _lastConversationToggleTime < 0.25f)
            {
                return;
            }

            _lastConversationToggleTime = Time.unscaledTime;
            bool disableConvai = ConversationFlowSettings.Toggle();
            Debug.Log(disableConvai
                ? "[StudySceneMenu] Convai disabled globally; using local conversation flow."
                : "[StudySceneMenu] Convai enabled globally; using original Convai flow.");
        }

        private void HandleConversationFlowChanged(bool disableConvai)
        {
            RefreshConversationFlowButton();
        }

        private void RefreshConversationFlowButton()
        {
            if (_conversationFlowButton == null || _conversationFlowButtonLabel == null)
            {
                return;
            }

            bool disableConvai = ConversationFlowSettings.DisableConvai;
            _conversationFlowButtonLabel.text = disableConvai
                ? "Convai: OFF  |  DeepSeek + local Kokoro  (click to enable)"
                : "Convai: ON  |  Original Convai flow  (click to disable)";
            Image image = _conversationFlowButton.targetGraphic as Image;
            if (image != null)
            {
                image.color = disableConvai
                    ? new Color(0.06f, 0.3f, 0.24f, 1f)
                    : new Color(0.48f, 0.18f, 0.06f, 1f);
            }
        }

        private void LoadDebugScene(string sceneName)
        {
            if (!Application.CanStreamedLevelBeLoaded(sceneName))
            {
                Debug.LogError(
                    $"Debug scene jump failed because '{sceneName}' is not in Build Settings.");
                return;
            }

            Debug.Log($"[StudySceneMenu] Loading '{sceneName}'.");
            SetVisible(false);
            SceneManager.LoadScene(sceneName, LoadSceneMode.Single);
        }

        private static void ExitApplication()
        {
            Application.Quit();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#endif
        }

        private void SetVisible(bool visible)
        {
            if (_isVisible == visible) return;
            EnsureUi();
            _isVisible = visible;
            if (visible)
            {
                _previousCursorLockMode = Cursor.lockState;
                _previousCursorVisible = Cursor.visible;
                RefreshSceneButtons();
                _canvasRoot.SetActive(true);
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
                if (EventSystem.current != null)
                    EventSystem.current.SetSelectedGameObject(null);
            }
            else
            {
                _canvasRoot.SetActive(false);
                RestoreCursor();
            }
        }

        private void RestoreCursor()
        {
            Cursor.lockState = _previousCursorLockMode;
            Cursor.visible = _previousCursorVisible;
        }

        private Button CreateButton(
            Transform parent,
            string objectName,
            string label,
            Vector2 position,
            Vector2 size,
            Color color)
        {
            Image image = CreateImage(parent, objectName, color);
            RectTransform rect = image.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            Button button = image.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(0.78f, 0.9f, 1f, 1f);
            colors.pressedColor = new Color(0.55f, 0.75f, 0.95f, 1f);
            colors.disabledColor = new Color(0.38f, 0.4f, 0.44f, 0.65f);
            button.colors = colors;
            Text text = CreateText(button.transform, "Label", label, 21,
                FontStyle.Bold, Vector2.zero, size - new Vector2(28f, 12f));
            Stretch(text.rectTransform);
            return button;
        }

        private Text CreateText(
            Transform parent,
            string objectName,
            string value,
            int fontSize,
            FontStyle fontStyle,
            Vector2 position,
            Vector2 size,
            Color? color = null)
        {
            GameObject textObject = new(objectName, typeof(RectTransform), typeof(Text));
            textObject.transform.SetParent(parent, false);
            Text text = textObject.GetComponent<Text>();
            text.font = _font;
            text.text = value;
            text.fontSize = fontSize;
            text.fontStyle = fontStyle;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = color ?? Color.white;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            RectTransform rect = text.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            return text;
        }

        private static Image CreateImage(Transform parent, string name, Color color)
        {
            GameObject imageObject = new(name, typeof(RectTransform), typeof(Image));
            imageObject.transform.SetParent(parent, false);
            Image image = imageObject.GetComponent<Image>();
            image.color = color;
            return image;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static bool WasF5Pressed()
        {
#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current != null && Keyboard.current.f5Key.wasPressedThisFrame)
                return true;
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            if (Input.GetKeyDown(KeyCode.F5)) return true;
#endif
            return false;
        }

        private static bool WasEscapePressed()
        {
#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
                return true;
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            if (Input.GetKeyDown(KeyCode.Escape)) return true;
#endif
            return false;
        }

        private static bool WasRightMousePressed()
        {
#if ENABLE_INPUT_SYSTEM
            if (Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame)
                return true;
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            if (Input.GetMouseButtonDown(1)) return true;
#endif
            return false;
        }
    }
}
