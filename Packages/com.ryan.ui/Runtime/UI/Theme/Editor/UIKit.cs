using System.IO;
using RyanAssets.UI.Feel;
using RyanAssets.UI.Textbox;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace RyanAssets.UI.Editor {
    /// <summary>Text presets of the shared UI.</summary>
    public enum UIText {
        /// <summary>Regular reading text (chat lines, setting names).</summary>
        Body,
        /// <summary>Emphasised body text (names, values).</summary>
        Strong,
        /// <summary>Secondary small text.</summary>
        Small,
        /// <summary>Small uppercase labels (section and column headers).</summary>
        Caption,
        /// <summary>Panel titles.</summary>
        Title,
        /// <summary>Large display text (game page title).</summary>
        Display,
    }

    /// <summary>
    /// Building blocks for editor scripts that generate the shared UI prefabs from the active
    /// <see cref="UITheme"/>. Colours are applied through <see cref="UIThemeColor"/>, so they keep
    /// following the theme after the prefab is built.
    /// </summary>
    public static class UIKit {
        public static UITheme Theme => UIThemeAssets.Theme;

        // --- Layout -------------------------------------------------------------

        public static RectTransform Node(Transform parent, string name) {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = 5; // UI
            if (parent != null)
                go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        /// <summary>A rect of fixed <paramref name="size"/> pinned to <paramref name="anchor"/> (pivot matches the anchor).</summary>
        public static RectTransform Pinned(Transform parent, string name, Vector2 anchor, Vector2 position, Vector2 size) {
            RectTransform rect = Node(parent, name);
            rect.anchorMin = rect.anchorMax = rect.pivot = anchor;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            return rect;
        }

        /// <summary>A rect filling its parent, inset by the given margins.</summary>
        public static RectTransform Fill(Transform parent, string name, float left = 0f, float top = 0f, float right = 0f, float bottom = 0f) {
            RectTransform rect = Node(parent, name);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(left, bottom);
            rect.offsetMax = new Vector2(-right, -top);
            return rect;
        }

        public static HorizontalLayoutGroup Row(GameObject go, float spacing, RectOffset padding = null, TextAnchor align = TextAnchor.MiddleLeft, bool controlWidth = true, bool controlHeight = true, bool expandWidth = false) {
            var layout = go.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = spacing;
            layout.padding = padding ?? new RectOffset();
            layout.childAlignment = align;
            layout.childControlWidth = controlWidth;
            layout.childControlHeight = controlHeight;
            layout.childForceExpandWidth = expandWidth;
            layout.childForceExpandHeight = false;
            return layout;
        }

        public static VerticalLayoutGroup Column(GameObject go, float spacing, RectOffset padding = null, TextAnchor align = TextAnchor.UpperLeft) {
            var layout = go.AddComponent<VerticalLayoutGroup>();
            layout.spacing = spacing;
            layout.padding = padding ?? new RectOffset();
            layout.childAlignment = align;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            return layout;
        }

        public static LayoutElement Size(Component target, float width = -1f, float height = -1f, float flexibleWidth = -1f) {
            if (!target.TryGetComponent(out LayoutElement element))
                element = target.gameObject.AddComponent<LayoutElement>();
            element.preferredWidth = width;
            element.minWidth = width;
            element.preferredHeight = height;
            element.minHeight = height;
            // Unity 6.6 layout groups also report a max size (their children's), which would clamp a
            // fixed size set here, so a fixed size pins the max as well.
            element.maxWidth = width;
            element.maxHeight = height;
            // A fixed width must not grow either: a nested layout group would otherwise report its
            // children's flexibility and take a share of the parent's spare space.
            element.flexibleWidth = flexibleWidth < 0f && width >= 0f ? 0f : flexibleWidth;
            return element;
        }

        public static ContentSizeFitter FitContent(GameObject go, bool horizontal, bool vertical) {
            var fitter = go.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = horizontal ? ContentSizeFitter.FitMode.PreferredSize : ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = vertical ? ContentSizeFitter.FitMode.PreferredSize : ContentSizeFitter.FitMode.Unconstrained;
            return fitter;
        }

        public static RectOffset Pad(int all) => new(all, all, all, all);
        public static RectOffset Pad(int horizontal, int vertical) => new(horizontal, horizontal, vertical, vertical);

        // --- Canvas -------------------------------------------------------------

        /// <summary>A screen-space overlay canvas scaled from the theme's reference resolution.</summary>
        public static RectTransform Canvas(string name, int sortingOrder) {
            RectTransform root = Node(null, name);
            var canvas = root.gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;
            canvas.additionalShaderChannels = AdditionalCanvasShaderChannels.TexCoord1 | AdditionalCanvasShaderChannels.Normal | AdditionalCanvasShaderChannels.Tangent;
            var scaler = root.gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = Theme.referenceResolution;
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            root.gameObject.AddComponent<GraphicRaycaster>();
            return root;
        }

        /// <summary>CanvasGroup + <see cref="CanvasGroupController"/> on <paramref name="target"/>, so it can fade.</summary>
        public static CanvasGroupController Fader(Component target, bool visible, bool alwaysActive = false) {
            var group = target.gameObject.AddComponent<CanvasGroup>();
            group.alpha = visible ? 1f : 0f;
            group.interactable = visible;
            group.blocksRaycasts = visible;
            var controller = target.gameObject.AddComponent<CanvasGroupController>();
            controller.alwaysActive = alwaysActive;
            controller.targetAlpha = group.alpha;
            return controller;
        }

        // --- Graphics -----------------------------------------------------------

        /// <summary>Paints <paramref name="graphic"/> from a theme colour slot (and keeps it in sync).</summary>
        public static T Paint<T>(T graphic, UIColorRole role, float opacity = 1f) where T : Graphic {
            if (!graphic.TryGetComponent(out UIThemeColor themeColor))
                themeColor = graphic.gameObject.AddComponent<UIThemeColor>();
            themeColor.Role = role;
            themeColor.Opacity = opacity;
            themeColor.Apply();
            return graphic;
        }

        /// <summary>A rounded rectangle filling <paramref name="target"/>, with corner <paramref name="radius"/> in canvas units.</summary>
        public static Image Rounded(Component target, UIColorRole role, float radius, float opacity = 1f, bool raycast = true) {
            var image = target.gameObject.AddComponent<Image>();
            image.sprite = Theme.roundedSprite;
            image.type = Image.Type.Sliced;
            image.pixelsPerUnitMultiplier = UIThemeAssets.RoundedSpriteRadius / Mathf.Max(0.5f, radius);
            image.raycastTarget = raycast;
            return Paint(image, role, opacity);
        }

        /// <summary>A soft drop shadow behind <paramref name="panel"/> (added to its parent, just before it).</summary>
        public static Image Shadow(RectTransform panel, float blur = 28f, float offsetY = 8f, float opacity = 0.45f) {
            RectTransform rect = Node(panel.parent, panel.name + "Shadow");
            rect.SetSiblingIndex(panel.GetSiblingIndex());
            rect.anchorMin = panel.anchorMin;
            rect.anchorMax = panel.anchorMax;
            rect.pivot = panel.pivot;
            rect.anchoredPosition = panel.anchoredPosition + new Vector2(0f, -offsetY);
            rect.sizeDelta = panel.sizeDelta + Vector2.one * blur * 2f;
            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = Theme.shadowSprite;
            image.type = Image.Type.Sliced;
            image.pixelsPerUnitMultiplier = UIThemeAssets.ShadowSpriteMargin / blur;
            image.raycastTarget = false;
            image.color = new Color(0f, 0f, 0f, opacity);
            if (panel.anchorMin == panel.anchorMax) {
                // Follows the panel's real size, which may come from a ContentSizeFitter.
                var follower = rect.gameObject.AddComponent<UIShadowFollower>();
                SerializedFields.Set(follower, "target", panel);
                SerializedFields.Set(follower, "blur", blur);
                SerializedFields.Set(follower, "offsetY", offsetY);
            }
            return image;
        }

        public static Image Icon(Transform parent, string icon, float size, UIColorRole role = UIColorRole.TextPrimary) {
            RectTransform rect = Node(parent, "Icon");
            rect.sizeDelta = new Vector2(size, size);
            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = UIThemeAssets.Icon(icon);
            image.preserveAspect = true;
            image.raycastTarget = false;
            Size(image, size, size);
            return Paint(image, role);
        }

        public static TextMeshProUGUI Text(Transform parent, string name, string text, UIText style, UIColorRole role = UIColorRole.TextPrimary, TextAlignmentOptions align = TextAlignmentOptions.MidlineLeft) {
            RectTransform rect = Node(parent, name);
            var label = rect.gameObject.AddComponent<TextMeshProUGUI>();
            UITheme theme = Theme;
            (TMP_FontAsset font, float size) = style switch {
                UIText.Body => (theme.regularFont, theme.bodyFontSize),
                UIText.Strong => (theme.semiBoldFont, theme.bodyFontSize),
                UIText.Small => (theme.regularFont, theme.smallFontSize),
                UIText.Caption => (theme.semiBoldFont, theme.smallFontSize - 1f),
                UIText.Title => (theme.boldFont, theme.titleFontSize),
                UIText.Display => (theme.boldFont, theme.titleFontSize * 1.6f),
                _ => (theme.regularFont, theme.bodyFontSize),
            };
            label.font = font;
            label.fontSize = size;
            label.text = text;
            label.alignment = align;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Ellipsis;
            label.raycastTarget = false;
            if (style == UIText.Caption) {
                label.fontStyle = FontStyles.UpperCase;
                label.characterSpacing = 4f;
            }
            return Paint(label, role);
        }

        // --- Controls -----------------------------------------------------------

        /// <summary>
        /// Makes <paramref name="target"/> a button whose hover / press feedback is a light "state
        /// layer" over it, so the same feedback reads on any background colour, plus a press scale
        /// and the theme's hover / click sounds (<see cref="UIButtonFeel"/>).
        /// </summary>
        public static Button Clickable(RectTransform target, float radius) {
            RectTransform layer = Fill(target, "StateLayer");
            var overlay = layer.gameObject.AddComponent<Image>();
            overlay.sprite = Theme.roundedSprite;
            overlay.type = Image.Type.Sliced;
            overlay.pixelsPerUnitMultiplier = UIThemeAssets.RoundedSpriteRadius / Mathf.Max(0.5f, radius);
            overlay.color = Color.white;
            overlay.raycastTarget = false;
            layer.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;

            var button = target.gameObject.AddComponent<Button>();
            button.targetGraphic = overlay;
            button.transition = Selectable.Transition.ColorTint;
            button.colors = new ColorBlock {
                normalColor = new Color(1f, 1f, 1f, 0f),
                highlightedColor = new Color(1f, 1f, 1f, 0.08f),
                pressedColor = new Color(1f, 1f, 1f, 0.16f),
                selectedColor = new Color(1f, 1f, 1f, 0f),
                disabledColor = new Color(0f, 0f, 0f, 0.3f),
                colorMultiplier = 1f,
                fadeDuration = 0.08f,
            };
            NoNavigation(button);
            // Press scale plus the theme's hover / click sounds.
            var feel = target.gameObject.AddComponent<UIButtonFeel>();
            SerializedFields.Set(feel, "selectable", button);
            return button;
        }

        /// <summary>A square icon button on a rounded background.</summary>
        public static Button IconButton(Transform parent, string name, string icon, float size, UIColorRole background, float radius, out Image backgroundImage, out Image iconImage) {
            RectTransform rect = Node(parent, name);
            rect.sizeDelta = new Vector2(size, size);
            Size(rect, size, size);
            backgroundImage = Rounded(rect, background, radius);
            iconImage = Icon(rect, icon, Mathf.Round(size * 0.55f));
            Center(iconImage.rectTransform);
            return Clickable(rect, radius);
        }

        /// <summary>A text button with an optional leading icon and trailing key hint.</summary>
        public static Button TextButton(Transform parent, string name, string label, string icon, UIColorRole background, UIColorRole foreground, float height, string keyHint = null) {
            RectTransform rect = Node(parent, name);
            Size(rect, -1f, height, 1f);
            Rounded(rect, background, 10f);
            Row(rect.gameObject, 8f, Pad(14, 0), TextAnchor.MiddleCenter);
            if (!string.IsNullOrEmpty(icon))
                Icon(rect, icon, 20f, foreground);
            Text(rect, "Label", label, UIText.Strong, foreground, TextAlignmentOptions.Center);
            if (!string.IsNullOrEmpty(keyHint))
                KeyHint(rect, keyHint, UIColorRole.TextPrimary);
            return Clickable(rect, 10f);
        }

        /// <summary>A small keycap showing a keyboard shortcut.</summary>
        public static RectTransform KeyHint(Transform parent, string key, UIColorRole foreground = UIColorRole.TextSecondary) {
            RectTransform cap = Node(parent, "KeyHint");
            Rounded(cap, UIColorRole.TextPrimary, 5f, 0.14f, raycast: false);
            Row(cap.gameObject, 0f, Pad(6, 2), TextAnchor.MiddleCenter);
            Size(cap, -1f, 22f, 0f);
            TextMeshProUGUI text = Text(cap, "Key", key, UIText.Caption, foreground, TextAlignmentOptions.Center);
            text.characterSpacing = 0f;
            text.fontSize = Theme.smallFontSize - 2f;
            return cap;
        }

        /// <summary>A thin horizontal rule.</summary>
        public static Image Divider(Transform parent) {
            RectTransform rect = Node(parent, "Divider");
            Size(rect, -1f, 1f);
            var image = rect.gameObject.AddComponent<Image>();
            image.raycastTarget = false;
            return Paint(image, UIColorRole.Stroke);
        }

        /// <summary>
        /// A vertical scroll list: viewport with a soft mask and a content rect laid out top-down.
        /// Returns the ScrollRect; its content has a VerticalLayoutGroup.
        /// </summary>
        public static ScrollRect ScrollList(RectTransform area, float spacing, RectOffset padding, bool fitContent) {
            var scroll = area.gameObject.AddComponent<ScrollRect>();
            RectTransform viewport = Fill(area, "Viewport");
            viewport.gameObject.AddComponent<RectMask2D>();
            // Transparent image so the viewport catches drags and wheel scrolls between rows.
            var hit = viewport.gameObject.AddComponent<Image>();
            hit.color = Color.clear;

            RectTransform content = Node(viewport, "Content");
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.sizeDelta = Vector2.zero;
            Column(content.gameObject, spacing, padding);
            if (fitContent)
                FitContent(content.gameObject, false, true);

            scroll.viewport = viewport;
            scroll.content = content;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.inertia = true;
            scroll.decelerationRate = 0.12f;
            scroll.scrollSensitivity = 24f;
            return scroll;
        }

        /// <summary>
        /// A single-line text box (<see cref="CustomInputField"/>, the project's TMP input with
        /// autocomplete hooks) on a rounded inset background.
        /// </summary>
        public static CustomInputField InputBox(Transform parent, string name, string placeholder, float height, float radius = 10f) {
            RectTransform rect = Node(parent, name);
            Size(rect, -1f, height, 1f);
            Image background = Rounded(rect, UIColorRole.SurfaceInset, radius);

            RectTransform area = Fill(rect, "Text Area", 12f, 0f, 12f, 0f);
            area.gameObject.AddComponent<RectMask2D>().padding = new Vector4(-4f, 0f, -4f, 0f);

            TextMeshProUGUI hint = Text(area, "Placeholder", placeholder, UIText.Body, UIColorRole.TextMuted);
            Stretch(hint.rectTransform);
            TextMeshProUGUI text = Text(area, "Text", string.Empty, UIText.Body);
            Stretch(text.rectTransform);
            text.richText = false;
            text.overflowMode = TextOverflowModes.Overflow;

            var input = rect.gameObject.AddComponent<CustomInputField>();
            input.targetGraphic = background;
            input.textViewport = area;
            input.textComponent = text;
            input.placeholder = hint;
            input.fontAsset = text.font;
            input.pointSize = text.fontSize;
            input.customCaretColor = true;
            input.caretColor = Theme.accent;
            input.caretWidth = 2;
            input.selectionColor = Theme.accent * new Color(1f, 1f, 1f, 0.35f);
            input.lineType = CustomInputField.LineType.SingleLine;
            input.onFocusSelectAll = false;
            input.resetOnDeActivation = true;
            input.restoreOriginalTextOnEscape = true;
            NoNavigation(input);
            input.transition = Selectable.Transition.None;
            rect.gameObject.AddComponent<TextboxHelper>();
            return input;
        }

        /// <summary>Keyboard and gamepad keys drive the game, so shared controls never take UI focus by navigation.</summary>
        public static void NoNavigation(Selectable selectable) {
            UnityEngine.UI.Navigation navigation = selectable.navigation;
            navigation.mode = UnityEngine.UI.Navigation.Mode.None;
            selectable.navigation = navigation;
        }

        public static void Center(RectTransform rect) {
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
        }

        public static void Stretch(RectTransform rect) {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }

        // --- Prefabs ------------------------------------------------------------

        /// <summary>Saves <paramref name="root"/> over the prefab at <paramref name="path"/> (keeping its GUID) and destroys the scene copy.</summary>
        public static GameObject SavePrefab(GameObject root, string path) {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path, out bool success);
            Object.DestroyImmediate(root);
            if (!success)
                throw new IOException("Could not save prefab " + path);
            return prefab;
        }
    }
}
