using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Game.Debate
{
    public sealed class DebatePauseMenu : MonoBehaviour
    {
        private const string PlayerVsNpcScene = "Level_PlayerVsNPCDebate";
        private const string NpcVsNpcScene = "Level_NPCVsNPCDebate";
        private const string InteractiveNpcScene = "Level_InteractiveNPCDebate";

        private Canvas _canvas;
        private GameObject _menuRoot;
        private CursorLockMode _previousCursorLockMode;
        private bool _previousCursorVisible;
        private bool _isOpen;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Create()
        {
            if (FindFirstObjectByType<DebatePauseMenu>() != null)
            {
                return;
            }

            var menuObject = new GameObject("Debate Pause Menu");
            DontDestroyOnLoad(menuObject);
            menuObject.AddComponent<DebatePauseMenu>();
        }

        private void Awake()
        {
            EnsureEventSystem();
            BuildUi();
            SceneManager.sceneLoaded += HandleSceneLoaded;
        }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            Time.timeScale = 1f;
        }

        private void Update()
        {
            if (WasTogglePressed())
            {
                SetMenuOpen(!_isOpen);
            }
        }

        private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            BuildUi();
            SetMenuOpen(false, false);
        }

        private static bool WasTogglePressed()
        {
#if ENABLE_INPUT_SYSTEM
            return Keyboard.current != null && Keyboard.current.f8Key.wasPressedThisFrame;
#elif ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetKeyDown(KeyCode.F8);
#else
            return false;
#endif
        }

        private void SetMenuOpen(bool open, bool restoreCursor = true)
        {
            if (_isOpen == open)
            {
                return;
            }

            _isOpen = open;
            if (_menuRoot == null)
            {
                BuildUi();
            }

            _menuRoot.SetActive(open);
            Time.timeScale = open ? 0f : 1f;

            if (open)
            {
                _previousCursorLockMode = Cursor.lockState;
                _previousCursorVisible = Cursor.visible;
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
                return;
            }

            if (restoreCursor)
            {
                Cursor.lockState = _previousCursorLockMode;
                Cursor.visible = _previousCursorVisible;
            }
        }

        private void LoadLevel(string sceneName)
        {
            SetMenuOpen(false, false);
            Time.timeScale = 1f;
            SceneManager.LoadScene(sceneName);
        }

        private void RestartCurrentLevel()
        {
            LoadLevel(SceneManager.GetActiveScene().name);
        }

        private void QuitGame()
        {
            Time.timeScale = 1f;

#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private void BuildUi()
        {
            if (_menuRoot != null)
            {
                Destroy(_menuRoot);
            }

            Transform parent = FindSceneCanvasParent();
            if (parent == null)
            {
                parent = GetFallbackCanvas().transform;
            }

            _menuRoot = CreateRect("Menu Root", parent);
            _menuRoot.transform.SetAsLastSibling();
            Stretch(_menuRoot.GetComponent<RectTransform>());

            var backdrop = _menuRoot.AddComponent<Image>();
            backdrop.color = new Color(0.02f, 0.04f, 0.05f, 0.72f);

            GameObject panel = CreateRect("Panel", _menuRoot.transform);
            var panelRect = panel.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0.5f, 0.5f);
            panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.pivot = new Vector2(0.5f, 0.5f);
            panelRect.sizeDelta = new Vector2(520f, 500f);
            panelRect.anchoredPosition = Vector2.zero;

            var panelImage = panel.AddComponent<Image>();
            panelImage.color = new Color(0.08f, 0.12f, 0.14f, 0.96f);

            var layout = panel.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(42, 42, 34, 34);
            layout.spacing = 16f;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;

            CreateTitle(panel.transform, "DEBATE MENU");
            CreateButton(panel.transform, "Player vs NPC Debate", () => LoadLevel(PlayerVsNpcScene), new Color(0.10f, 0.42f, 0.67f));
            CreateButton(panel.transform, "NPC vs NPC Debate", () => LoadLevel(NpcVsNpcScene), new Color(0.14f, 0.50f, 0.42f));
            CreateButton(panel.transform, "Interactive NPC Debate", () => LoadLevel(InteractiveNpcScene), new Color(0.33f, 0.45f, 0.70f));
            CreateButton(panel.transform, "Restart Current Level", RestartCurrentLevel, new Color(0.70f, 0.48f, 0.13f));
            CreateButton(panel.transform, "Quit Game", QuitGame, new Color(0.62f, 0.22f, 0.20f));

            _menuRoot.SetActive(false);
        }

        private Transform FindSceneCanvasParent()
        {
            Canvas[] canvases = FindObjectsByType<Canvas>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            for (int i = 0; i < canvases.Length; i++)
            {
                if (canvases[i].name == "Debate Round UI")
                {
                    return canvases[i].transform;
                }
            }

            return null;
        }

        private Canvas GetFallbackCanvas()
        {
            if (_canvas != null)
            {
                return _canvas;
            }

            _canvas = gameObject.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 32000;

            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280f, 720f);
            scaler.matchWidthOrHeight = 0.5f;

            gameObject.AddComponent<GraphicRaycaster>();
            return _canvas;
        }

        private static void EnsureEventSystem()
        {
            if (FindFirstObjectByType<EventSystem>() != null)
            {
                return;
            }

            var eventSystemObject = new GameObject("EventSystem");
            DontDestroyOnLoad(eventSystemObject);
            eventSystemObject.AddComponent<EventSystem>();
#if ENABLE_INPUT_SYSTEM
            eventSystemObject.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
#else
            eventSystemObject.AddComponent<StandaloneInputModule>();
#endif
        }

        private static GameObject CreateRect(string name, Transform parent)
        {
            var rectObject = new GameObject(name, typeof(RectTransform));
            rectObject.transform.SetParent(parent, false);
            return rectObject;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static void CreateTitle(Transform parent, string text)
        {
            GameObject titleObject = CreateRect("Title", parent);
            var layoutElement = titleObject.AddComponent<LayoutElement>();
            layoutElement.preferredHeight = 64f;

            var title = titleObject.AddComponent<TextMeshProUGUI>();
            title.text = text;
            title.alignment = TextAlignmentOptions.Center;
            title.fontSize = 34f;
            title.fontStyle = FontStyles.Bold;
            title.color = new Color(1f, 0.84f, 0.28f);
            title.raycastTarget = false;
        }

        private static void CreateButton(
            Transform parent,
            string label,
            UnityEngine.Events.UnityAction action,
            Color color)
        {
            GameObject buttonObject = CreateRect(label + " Button", parent);
            var layoutElement = buttonObject.AddComponent<LayoutElement>();
            layoutElement.preferredHeight = 64f;

            var image = buttonObject.AddComponent<Image>();
            image.color = color;

            var button = buttonObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(action);

            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(0.92f, 0.98f, 1f, 1f);
            colors.pressedColor = new Color(0.78f, 0.88f, 0.94f, 1f);
            colors.selectedColor = colors.highlightedColor;
            button.colors = colors;

            GameObject labelObject = CreateRect("Label", buttonObject.transform);
            Stretch(labelObject.GetComponent<RectTransform>());

            var text = labelObject.AddComponent<TextMeshProUGUI>();
            text.text = label;
            text.alignment = TextAlignmentOptions.Center;
            text.fontSize = 22f;
            text.fontStyle = FontStyles.Bold;
            text.color = Color.white;
            text.raycastTarget = false;
        }
    }
}
