using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game.Debate
{
    [DisallowMultipleComponent]
    public sealed class DebateSpeakerStation : MonoBehaviour
    {
        private readonly List<Material> _runtimeMaterials = new();
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

            transform.SetParent(camera.transform, false);
            transform.localPosition = Vector3.zero;
            transform.localRotation = Quaternion.identity;
            BuildStation();
            _built = true;
        }

        private void OnDestroy()
        {
            foreach (Material material in _runtimeMaterials)
                if (material != null) Destroy(material);
            _runtimeMaterials.Clear();
        }

        private void BuildStation()
        {
            Material podiumMaterial = CreateMaterial(
                "Podium Navy", new Color(0.045f, 0.09f, 0.16f, 1f), 0.35f, 0.55f);
            Material trimMaterial = CreateMaterial(
                "Podium Trim", new Color(0.08f, 0.62f, 0.82f, 1f), 0.25f, 0.7f);
            Material microphoneMaterial = CreateMaterial(
                "Microphone", new Color(0.055f, 0.06f, 0.07f, 1f), 0.7f, 0.4f);

            CreatePart(
                PrimitiveType.Cube,
                "Debate Podium",
                new Vector3(0f, -0.94f, 1.40f),
                new Vector3(0.66f, 0.50f, 0.18f),
                Quaternion.identity,
                podiumMaterial);
            CreatePart(
                PrimitiveType.Cube,
                "Podium Top",
                new Vector3(0f, -0.65f, 1.34f),
                new Vector3(0.74f, 0.07f, 0.28f),
                Quaternion.Euler(-6f, 0f, 0f),
                trimMaterial);
            CreatePart(
                PrimitiveType.Cylinder,
                "Microphone Stand",
                new Vector3(0.16f, -0.46f, 1.24f),
                new Vector3(0.014f, 0.15f, 0.014f),
                Quaternion.Euler(0f, 0f, -13f),
                microphoneMaterial);
            CreatePart(
                PrimitiveType.Capsule,
                "Debate Microphone",
                new Vector3(0.12f, -0.27f, 1.20f),
                new Vector3(0.04f, 0.075f, 0.04f),
                Quaternion.Euler(0f, 0f, -13f),
                microphoneMaterial);
        }

        private void CreatePart(
            PrimitiveType primitiveType,
            string objectName,
            Vector3 localPosition,
            Vector3 localScale,
            Quaternion localRotation,
            Material material)
        {
            GameObject part = GameObject.CreatePrimitive(primitiveType);
            part.name = objectName;
            part.transform.SetParent(transform, false);
            part.transform.localPosition = localPosition;
            part.transform.localScale = localScale;
            part.transform.localRotation = localRotation;
            Collider collider = part.GetComponent<Collider>();
            if (collider != null) Destroy(collider);
            Renderer renderer = part.GetComponent<Renderer>();
            if (renderer != null) renderer.sharedMaterial = material;
        }

        private Material CreateMaterial(
            string materialName,
            Color color,
            float metallic,
            float smoothness)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ??
                            Shader.Find("Standard");
            Material material = new(shader)
            {
                name = materialName,
                color = color
            };
            if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", metallic);
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", smoothness);
            _runtimeMaterials.Add(material);
            return material;
        }
    }
}
