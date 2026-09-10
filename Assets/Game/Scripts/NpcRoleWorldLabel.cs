using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Debate
{
    public static class NpcRoleWorldLabel
    {
        private const float WorldScale = 0.0025f;

        public static GameObject Ensure(
            Transform target,
            string roleText,
            Color accentColor,
            Vector3 localOffset)
        {
            if (target == null) return null;
            string objectName = "Role Label - " + (roleText ?? string.Empty);
            Transform existing = target.Find(objectName);
            if (existing != null)
            {
                TMP_Text existingText = existing.GetComponentInChildren<TMP_Text>(true);
                if (existingText != null) existingText.text = roleText ?? string.Empty;
                return existing.gameObject;
            }

            GameObject root = new(objectName, typeof(RectTransform), typeof(Canvas),
                typeof(CanvasScaler), typeof(RefereeLabelBillboard));
            root.transform.SetParent(target, false);
            root.transform.localPosition = localOffset;
            root.transform.localScale = Vector3.one * WorldScale;
            RectTransform rootRect = root.GetComponent<RectTransform>();
            rootRect.sizeDelta = new Vector2(360f, 76f);

            Canvas canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.overrideSorting = true;
            canvas.sortingOrder = 110;
            CanvasScaler scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.dynamicPixelsPerUnit = 10f;

            GameObject background = new("Label Background", typeof(RectTransform), typeof(Image));
            background.transform.SetParent(root.transform, false);
            Stretch(background.GetComponent<RectTransform>(), 0f);
            Image backgroundImage = background.GetComponent<Image>();
            backgroundImage.color = new Color(0.025f, 0.04f, 0.075f, 0.9f);
            backgroundImage.raycastTarget = false;

            GameObject accent = new("Accent", typeof(RectTransform), typeof(Image));
            accent.transform.SetParent(background.transform, false);
            RectTransform accentRect = accent.GetComponent<RectTransform>();
            accentRect.anchorMin = new Vector2(0f, 0f);
            accentRect.anchorMax = new Vector2(0f, 1f);
            accentRect.pivot = new Vector2(0f, 0.5f);
            accentRect.anchoredPosition = Vector2.zero;
            accentRect.sizeDelta = new Vector2(12f, 0f);
            Image accentImage = accent.GetComponent<Image>();
            accentImage.color = accentColor;
            accentImage.raycastTarget = false;

            GameObject textObject = new("Role Text", typeof(RectTransform),
                typeof(TextMeshProUGUI));
            textObject.transform.SetParent(background.transform, false);
            RectTransform textRect = textObject.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(20f, 4f);
            textRect.offsetMax = new Vector2(-10f, -4f);
            TMP_Text text = textObject.GetComponent<TMP_Text>();
            text.text = roleText ?? string.Empty;
            text.fontSize = 30f;
            text.fontStyle = FontStyles.Bold;
            text.alignment = TextAlignmentOptions.Center;
            text.color = Color.white;
            text.raycastTarget = false;

            root.GetComponent<RefereeLabelBillboard>().Follow(target, localOffset);
            return root;
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
