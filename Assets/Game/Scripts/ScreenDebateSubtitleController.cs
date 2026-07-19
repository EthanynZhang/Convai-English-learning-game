using System;
using System.Collections;
using System.Collections.Generic;
using Convai.Scripts.Runtime.Features;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Game.Debate
{
    [DisallowMultipleComponent]
    public sealed class ScreenDebateSubtitleController : MonoBehaviour
    {
        private sealed class TranscriptSubscription
        {
            public ConvaiGroupNPCController Npc;
            public Action<string> ShowHandler;
            public Action HideHandler;
        }

        private readonly List<TranscriptSubscription> _subscriptions = new();
        private GameObject _canvasRoot;
        private GameObject _captionPanel;
        private TMP_Text _captionText;
        private Coroutine _hideRoutine;

        public static bool TargetsScene(string sceneName)
        {
            return string.Equals(
                sceneName?.Trim(),
                "01Level_NPCVsNPCDebate",
                StringComparison.Ordinal);
        }

        private void Awake()
        {
            if (!TargetsScene(SceneManager.GetActiveScene().name))
            {
                enabled = false;
                return;
            }

            BuildUi();
        }

        private void OnDestroy()
        {
            foreach (TranscriptSubscription subscription in _subscriptions)
            {
                if (subscription.Npc == null) continue;
                subscription.Npc.ShowSpeechBubble -= subscription.ShowHandler;
                subscription.Npc.HideSpeechBubble -= subscription.HideHandler;
            }
            _subscriptions.Clear();
        }

        public void ConfigureForStructuredDebate(NPC2NPCConversationManager conversationManager)
        {
            if (!enabled || conversationManager == null || _subscriptions.Count > 0)
            {
                return;
            }

            foreach (NPCGroup group in conversationManager.npcGroups)
            {
                if (group == null) continue;
                SubscribeToDebater(group.GroupNPC1);
                SubscribeToDebater(group.GroupNPC2);
            }
        }

        public void ShowSubtitle(string speakerName, string transcript)
        {
            string text = transcript?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(text) || _captionText == null)
            {
                return;
            }

            string speaker = string.IsNullOrWhiteSpace(speakerName)
                ? "Speaker"
                : speakerName.Trim();
            _captionText.text = $"<b>{speaker}</b>   {text}";
            _captionPanel.SetActive(true);
            if (_hideRoutine != null) StopCoroutine(_hideRoutine);
            float duration = Mathf.Clamp(3.5f + text.Length * 0.035f, 5f, 14f);
            _hideRoutine = StartCoroutine(HideAfter(duration));
        }

        private IEnumerator HideAfter(float seconds)
        {
            yield return new WaitForSecondsRealtime(seconds);
            _hideRoutine = null;
            if (_captionText != null) _captionText.text = string.Empty;
            if (_captionPanel != null) _captionPanel.SetActive(false);
        }

        private void HideSubtitle()
        {
            if (_hideRoutine != null) StopCoroutine(_hideRoutine);
            _hideRoutine = null;
            if (_captionText != null) _captionText.text = string.Empty;
            if (_captionPanel != null) _captionPanel.SetActive(false);
        }

        private void SubscribeToDebater(ConvaiGroupNPCController npc)
        {
            if (npc == null || _subscriptions.Exists(item => item.Npc == npc))
            {
                return;
            }

            string speaker = string.IsNullOrWhiteSpace(npc.CharacterName)
                ? npc.gameObject.name
                : npc.CharacterName;
            Action<string> showHandler = transcript => ShowSubtitle(speaker, transcript);
            Action hideHandler = HideSubtitle;
            npc.ShowSpeechBubble += showHandler;
            npc.HideSpeechBubble += hideHandler;
            _subscriptions.Add(new TranscriptSubscription
            {
                Npc = npc,
                ShowHandler = showHandler,
                HideHandler = hideHandler
            });

            foreach (NPCSpeechBubble bubble in npc.GetComponentsInChildren<NPCSpeechBubble>(true))
            {
                if (bubble == null) continue;
                bubble.HideSpeechBubble();
                bubble.gameObject.SetActive(false);
            }
            npc.DetachSpeechBubble();
        }

        private void BuildUi()
        {
            _canvasRoot = new GameObject(
                "Scene 01 Six-Turn Debate Subtitle Canvas",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(GraphicRaycaster));
            Canvas canvas = _canvasRoot.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 220;
            CanvasScaler scaler = _canvasRoot.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            _captionPanel = new GameObject(
                "Unified Debate Subtitle",
                typeof(RectTransform),
                typeof(Image));
            _captionPanel.transform.SetParent(_canvasRoot.transform, false);
            RectTransform panelRect = _captionPanel.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0.5f, 0f);
            panelRect.anchorMax = new Vector2(0.5f, 0f);
            panelRect.pivot = new Vector2(0.5f, 0f);
            panelRect.anchoredPosition = new Vector2(0f, 64f);
            panelRect.sizeDelta = new Vector2(1320f, 156f);
            _captionPanel.GetComponent<Image>().color =
                new Color(0.015f, 0.025f, 0.045f, 0.92f);

            GameObject textObject = new(
                "Subtitle Text",
                typeof(RectTransform),
                typeof(TextMeshProUGUI));
            textObject.transform.SetParent(_captionPanel.transform, false);
            _captionText = textObject.GetComponent<TextMeshProUGUI>();
            _captionText.fontSize = 29f;
            _captionText.color = Color.white;
            _captionText.alignment = TextAlignmentOptions.Center;
            _captionText.enableWordWrapping = true;
            _captionText.raycastTarget = false;
            RectTransform textRect = _captionText.rectTransform;
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(34f, 18f);
            textRect.offsetMax = new Vector2(-34f, -18f);

            _captionPanel.SetActive(false);
        }
    }
}
