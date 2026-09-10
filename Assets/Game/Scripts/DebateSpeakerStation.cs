using UnityEngine;

namespace Game.Debate
{
    [DisallowMultipleComponent]
    public sealed class DebateSpeakerStation : MonoBehaviour
    {
        private const string WoodenPodiumResourcePath = "WoodenPodium/WoodenPodium";
        private const string BuiltInMicrophoneMeshName = "desirefx.me_003";
        private const float StationDistanceFromPlayer = 1.45f;
        private const float PodiumVerticalOffset = 0.05f;
        private static readonly Vector3 WoodenPodiumScale =
            new Vector3(1.2f, 1.35f, 1.2f);
        private bool _built;

        public static bool TargetsScene(string sceneName)
        {
            string normalized = sceneName?.Trim() ?? string.Empty;
            return normalized == "03Level_PlayerVsNPCDebate" ||
                   normalized == "05Level_PlayerVsNPCDebate 1";
        }

        public static DebateSpeakerStation EnsureForScene(string sceneName)
        {
            if (!TargetsScene(sceneName)) return null;
            DebateSpeakerStation existing = FindFirstObjectByType<DebateSpeakerStation>();
            if (existing != null) return existing;
            GameObject host = new("First Person Debate Speaker Station");
            return host.AddComponent<DebateSpeakerStation>();
        }

        public static Vector3 CalculateWorldPosition(
            Vector3 playerPosition,
            Vector3 playerForward,
            float floorY)
        {
            Vector3 forward = Vector3.ProjectOnPlane(playerForward, Vector3.up).normalized;
            if (forward.sqrMagnitude < 0.001f) forward = Vector3.forward;
            Vector3 position = playerPosition + forward * StationDistanceFromPlayer;
            position.y = floorY;
            return position;
        }

        private void OnEnable()
        {
            TryBuildStation();
        }

        private void Start()
        {
            TryBuildStation();
        }

        private void Update()
        {
            if (!_built) TryBuildStation();
        }

        private void TryBuildStation()
        {
            if (_built) return;
            Camera camera = Camera.main != null
                ? Camera.main
                : FindFirstObjectByType<Camera>();
            if (camera == null)
            {
                return;
            }

            GameObject player = GameObject.FindGameObjectWithTag("Player");
            Transform playerTransform = player != null
                ? player.transform
                : camera.transform.parent != null
                    ? camera.transform.parent
                    : camera.transform;
            Vector3 forward = Vector3.ProjectOnPlane(playerTransform.forward, Vector3.up).normalized;
            if (forward.sqrMagnitude < 0.001f) forward = Vector3.forward;
            transform.SetParent(null, true);
            transform.SetPositionAndRotation(
                CalculateWorldPosition(playerTransform.position, forward, 0f),
                Quaternion.LookRotation(forward, Vector3.up));
            BuildStation();
            _built = true;
        }

        private void BuildStation()
        {
            GameObject podiumPrefab = Resources.Load<GameObject>(WoodenPodiumResourcePath);
            if (podiumPrefab == null)
            {
                Debug.LogError(
                    "The wooden podium prefab is missing from Resources/" +
                    WoodenPodiumResourcePath + ".");
                return;
            }

            GameObject podium = Instantiate(podiumPrefab, transform, false);
            podium.name = "Wooden Debate Podium";
            podium.transform.localPosition =
                new Vector3(0f, PodiumVerticalOffset, 0f);
            podium.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            podium.transform.localScale = WoodenPodiumScale;

            Transform[] descendants = podium.GetComponentsInChildren<Transform>(true);
            for (int index = 0; index < descendants.Length; index++)
            {
                Transform builtInMicrophone = descendants[index];
                if (builtInMicrophone.name != BuiltInMicrophoneMeshName) continue;
                builtInMicrophone.gameObject.SetActive(false);
                break;
            }
        }
    }
}
