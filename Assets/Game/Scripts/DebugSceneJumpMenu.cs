using UnityEngine;
using UnityEngine.SceneManagement;
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

        private bool _isVisible;
        private CursorLockMode _previousCursorLockMode;
        private bool _previousCursorVisible;
        private GUIStyle _titleStyle;
        private GUIStyle _subtitleStyle;
        private GUIStyle _buttonStyle;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            if (FindFirstObjectByType<DebugSceneJumpMenu>() != null)
            {
                return;
            }

            GameObject host = new("[Debug] F5 Scene Jump Menu");
            DontDestroyOnLoad(host);
            host.AddComponent<DebugSceneJumpMenu>();
        }

        public static string[] GetTargetSceneNames()
        {
            return (string[])TargetSceneNames.Clone();
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
            }
        }

        private void OnDestroy()
        {
            if (_isVisible)
            {
                RestoreCursor();
            }
        }

        private void OnGUI()
        {
            if (!_isVisible)
            {
                return;
            }

            EnsureStyles();
            int previousDepth = GUI.depth;
            GUI.depth = -10000;

            Color previousColor = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.72f);
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = previousColor;

            float panelWidth = Mathf.Min(560f, Mathf.Max(320f, Screen.width - 32f));
            float panelHeight = Mathf.Min(470f, Mathf.Max(360f, Screen.height - 32f));
            Rect panel = new(
                (Screen.width - panelWidth) * 0.5f,
                (Screen.height - panelHeight) * 0.5f,
                panelWidth,
                panelHeight);

            GUILayout.BeginArea(panel, GUI.skin.window);
            GUILayout.Space(10f);
            GUILayout.Label("DEBUG SCENE JUMP", _titleStyle);
            GUILayout.Label("Press F5 again to close", _subtitleStyle);
            GUILayout.Space(14f);

            string activeScene = SceneManager.GetActiveScene().name;
            for (int index = 0; index < TargetSceneNames.Length; index++)
            {
                string sceneName = TargetSceneNames[index];
                bool isCurrent = activeScene == sceneName;
                bool isLoadable = Application.CanStreamedLevelBeLoaded(sceneName);
                GUI.enabled = !isCurrent && isLoadable;

                string suffix = isCurrent ? "  (CURRENT)" : isLoadable ? string.Empty : "  (NOT IN BUILD)";
                if (GUILayout.Button(TargetSceneLabels[index] + suffix, _buttonStyle))
                {
                    LoadDebugScene(sceneName);
                }

                GUILayout.Space(8f);
            }

            GUI.enabled = true;
            Color previousBackgroundColor = GUI.backgroundColor;
            GUI.backgroundColor = new Color(0.72f, 0.18f, 0.18f, 1f);
            if (GUILayout.Button("Exit Application", _buttonStyle))
            {
                ExitApplication();
            }
            GUI.backgroundColor = previousBackgroundColor;
            GUILayout.FlexibleSpace();
            GUILayout.Label(
                "Development shortcut: scene jumps bypass formal research completion checks.",
                _subtitleStyle);
            GUILayout.Space(8f);
            GUILayout.EndArea();
            GUI.depth = previousDepth;
        }

        private void LoadDebugScene(string sceneName)
        {
            if (!Application.CanStreamedLevelBeLoaded(sceneName))
            {
                Debug.LogError($"Debug scene jump failed because '{sceneName}' is not in Build Settings.");
                return;
            }

            Debug.LogWarning(
                $"[DebugSceneJumpMenu] Loading '{sceneName}' and bypassing research completion gates.");
            SetVisible(false);
            SceneManager.LoadScene(sceneName);
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
            if (_isVisible == visible)
            {
                return;
            }

            _isVisible = visible;
            if (visible)
            {
                _previousCursorLockMode = Cursor.lockState;
                _previousCursorVisible = Cursor.visible;
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
            else
            {
                RestoreCursor();
            }
        }

        private void RestoreCursor()
        {
            Cursor.lockState = _previousCursorLockMode;
            Cursor.visible = _previousCursorVisible;
        }

        private void EnsureStyles()
        {
            if (_titleStyle != null)
            {
                return;
            }

            _titleStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 28,
                fontStyle = FontStyle.Bold,
                normal = { textColor = Color.white }
            };
            _subtitleStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 15,
                wordWrap = true,
                normal = { textColor = new Color(0.8f, 0.84f, 0.9f) }
            };
            _buttonStyle = new GUIStyle(GUI.skin.button)
            {
                fixedHeight = 58f,
                fontSize = 18,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };
        }

        private static bool WasF5Pressed()
        {
#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current != null && Keyboard.current.f5Key.wasPressedThisFrame)
            {
                return true;
            }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            if (Input.GetKeyDown(KeyCode.F5))
            {
                return true;
            }
#endif
            return false;
        }

        private static bool WasRightMousePressed()
        {
#if ENABLE_INPUT_SYSTEM
            if (Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame)
            {
                return true;
            }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            if (Input.GetMouseButtonDown(1))
            {
                return true;
            }
#endif
            return false;
        }
    }
}
