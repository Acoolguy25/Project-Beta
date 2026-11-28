using System.Collections.Generic;
using System.Linq;
using RyanAssets.UI;
using RyanAssets.UI.Editor;
using RyanAssets.UI.Feel;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using static RyanAssets.UI.Editor.UIKit;

namespace RyanAssets.Client.ClientUI.Editor {
    /// <summary>
    /// Restyles the shared spectate bar (the bar a dead player watches others through) from the active
    /// <see cref="UITheme"/>: a glass pill at the bottom centre with "Spectating", the name, the level
    /// and two round arrow buttons. Menu: Ryan/UI/Rebuild Spectate Bar.
    /// <para>
    /// Rebuilt <b>in place</b>: <c>ClientSpectate</c> lives on the camera rig in <c>Client.prefab</c> and
    /// references the name, health and level labels, the two arrows and the fader inside this prefab by
    /// file id. Those six objects are kept and re-parented; everything else is regenerated, so the
    /// references in <c>Client.prefab</c> stay valid and that prefab does not change.
    /// </para>
    /// </summary>
    public static class SpectateBarAuthoring {
        public const string PrefabPath = "Packages/com.ryan.hud/Runtime/Client/ClientUI/Spectate/SpectateCanvas.prefab";

        // Objects ClientSpectate references (kept so their file ids survive).
        const string PlayerNameName = "PlayerName";
        const string HealthName = "CharacterHealth";
        const string LevelName = "PlayerLevel";
        const string LeftArrowName = "LeftArrow";
        const string RightArrowName = "RightArrow";

        const float BarWidth = 420f;
        const float BarHeight = 68f;
        const float BarRadius = 20f;
        const float ArrowSize = 48f;
        const float ArrowRadius = 14f;
        const float BottomMargin = 24f;

        [MenuItem("Ryan/UI/Rebuild Spectate Bar")]
        public static void Build() {
            UIThemeAssets.Ensure();
            GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
            try {
                Restyle(root);
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            } finally {
                PrefabUtility.UnloadPrefabContents(root);
            }
            AssetDatabase.SaveAssets();
            Debug.Log("Spectate bar rebuilt.");
        }

        static void Restyle(GameObject root) {
            Transform Find(string name) => root.GetComponentsInChildren<Transform>(true).First(t => t.name == name);
            var playerName = Find(PlayerNameName).GetComponent<TextMeshProUGUI>();
            var health = Find(HealthName).GetComponent<TextMeshProUGUI>();
            var level = Find(LevelName).GetComponent<TextMeshProUGUI>();
            var left = Find(LeftArrowName).GetComponent<Button>();
            var right = Find(RightArrowName).GetComponent<Button>();
            var kept = new HashSet<Transform> { root.transform, playerName.transform, health.transform, level.transform, left.transform, right.transform };

            // Park the kept objects on the root, then drop everything else (old window, dropdown, arrow labels,
            // or the previous run's generated layout).
            foreach (Transform keep in kept.Where(t => t != root.transform))
                keep.SetParent(root.transform, false);
            foreach (Transform child in root.transform.Cast<Transform>().Where(t => !kept.Contains(t)).ToList())
                Object.DestroyImmediate(child.gameObject);
            foreach (Button arrow in new[] { left, right })
                foreach (Transform child in arrow.transform.Cast<Transform>().ToList())
                    Object.DestroyImmediate(child.gameObject);

            // Bar: a glass pill pinned to the bottom centre.
            RectTransform bar = Pinned(root.transform, "Bar", new Vector2(0.5f, 0f), new Vector2(0f, BottomMargin), new Vector2(BarWidth, BarHeight));
            Shadow(bar, 24f, 6f, 0.4f);
            Rounded(bar, UIColorRole.Surface, BarRadius, raycast: true);
            Row(bar.gameObject, 12f, Pad(10, 10), TextAnchor.MiddleCenter, controlWidth: true, controlHeight: false);

            StyleArrow(left, bar, "arrow_back");

            RectTransform middle = Node(bar, "Middle");
            Size(middle, -1f, BarHeight - 16f, 1f);
            Column(middle.gameObject, 0f, null, TextAnchor.MiddleCenter);

            RectTransform kicker = Node(middle, "Kicker");
            Size(kicker, -1f, 18f);
            Row(kicker.gameObject, 6f, null, TextAnchor.MiddleCenter, controlWidth: true, controlHeight: true);
            Icon(kicker, "visibility", 14f, UIColorRole.TextMuted);
            Text(kicker, "Label", "Spectating", UIText.Caption, UIColorRole.TextMuted, TextAlignmentOptions.Center);
            level.transform.SetParent(kicker, false);
            StyleText(level, UIText.Caption, UIColorRole.TextSecondary, "Level 1");
            level.fontStyle = FontStyles.Normal;
            level.characterSpacing = 0f;

            RectTransform nameRow = Node(middle, "NameRow");
            Size(nameRow, -1f, 30f);
            Row(nameRow.gameObject, 10f, null, TextAnchor.MiddleCenter, controlWidth: true, controlHeight: true);
            playerName.transform.SetParent(nameRow, false);
            StyleText(playerName, UIText.Title, UIColorRole.TextPrimary, "Player");
            playerName.fontSize = Theme.titleFontSize - 2f;
            // The name takes its team colour at runtime, so it is not theme-painted.
            Object.DestroyImmediate(playerName.GetComponent<UIThemeColor>());
            health.transform.SetParent(nameRow, false);
            StyleText(health, UIText.Small, UIColorRole.Danger, "100/100");

            StyleArrow(right, bar, "arrow_forward");
            right.transform.SetAsLastSibling();

            // Root: the fader stays on the canvas root (ClientSpectate fades it), and the old prefab's
            // scaler is matched to the theme so the bar sizes like the rest of the HUD.
            if (root.TryGetComponent(out CanvasScaler scaler)) {
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = Theme.referenceResolution;
                scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
                scaler.matchWidthOrHeight = 0.5f;
            }
        }

        /// <summary>Restyles an existing label in place (font, size, colour slot), as <see cref="UIKit.Text"/> would make it.</summary>
        static void StyleText(TextMeshProUGUI label, UIText style, UIColorRole role, string placeholder) {
            UITheme theme = Theme;
            (TMP_FontAsset font, float size) = style switch {
                UIText.Strong => (theme.semiBoldFont, theme.bodyFontSize),
                UIText.Small => (theme.regularFont, theme.smallFontSize),
                UIText.Caption => (theme.semiBoldFont, theme.smallFontSize - 1f),
                UIText.Title => (theme.boldFont, theme.titleFontSize),
                _ => (theme.regularFont, theme.bodyFontSize),
            };
            label.font = font;
            label.fontSharedMaterial = font.material;
            label.fontSize = size;
            label.enableAutoSizing = false;
            label.fontStyle = style == UIText.Caption ? FontStyles.UpperCase : FontStyles.Normal;
            label.characterSpacing = style == UIText.Caption ? 4f : 0f;
            label.text = placeholder;
            label.alignment = TextAlignmentOptions.Center;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Ellipsis;
            label.raycastTarget = false;
            var rect = label.rectTransform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            if (rect.TryGetComponent(out LayoutElement element))
                Object.DestroyImmediate(element);
            if (rect.TryGetComponent(out ContentSizeFitter fitter))
                Object.DestroyImmediate(fitter);
            Paint(label, role);
        }

        /// <summary>A round icon button made from an existing <see cref="Button"/> (kept for its file id).</summary>
        static void StyleArrow(Button button, RectTransform parent, string icon) {
            var rect = (RectTransform)button.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(ArrowSize, ArrowSize);
            Size(rect, ArrowSize, ArrowSize);

            var background = button.GetComponent<Image>();
            background.sprite = Theme.roundedSprite;
            background.type = Image.Type.Sliced;
            background.pixelsPerUnitMultiplier = UIThemeAssets.RoundedSpriteRadius / ArrowRadius;
            background.raycastTarget = true;
            Paint(background, UIColorRole.SurfaceHover);

            Image glyph = Icon(rect, icon, 24f);
            Center(glyph.rectTransform);

            // Hover and press as a light layer over the theme colour (as UIKit.Clickable does), plus the
            // theme's press scale and sounds.
            RectTransform layer = Fill(rect, "StateLayer");
            var overlay = layer.gameObject.AddComponent<Image>();
            overlay.sprite = Theme.roundedSprite;
            overlay.type = Image.Type.Sliced;
            overlay.pixelsPerUnitMultiplier = UIThemeAssets.RoundedSpriteRadius / ArrowRadius;
            overlay.color = Color.white;
            overlay.raycastTarget = false;
            button.targetGraphic = overlay;
            button.transition = Selectable.Transition.ColorTint;
            button.colors = new ColorBlock {
                normalColor = new Color(1f, 1f, 1f, 0f),
                highlightedColor = new Color(1f, 1f, 1f, 0.1f),
                pressedColor = new Color(1f, 1f, 1f, 0.2f),
                selectedColor = new Color(1f, 1f, 1f, 0f),
                disabledColor = new Color(0f, 0f, 0f, 0.3f),
                colorMultiplier = 1f,
                fadeDuration = 0.08f,
            };
            NoNavigation(button);
            if (!button.TryGetComponent(out UIButtonFeel feel))
                feel = button.gameObject.AddComponent<UIButtonFeel>();
            SerializedFields.Set(feel, "selectable", button);
        }
    }
}
