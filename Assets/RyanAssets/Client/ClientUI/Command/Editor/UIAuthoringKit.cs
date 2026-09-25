using System;
using RyanAssets.UI.Hover;
using RyanAssets.UI.Textbox;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace RyanAssets.Client.ClientUI.Command.Editor {
    /// <summary>
    /// Shared building blocks for editor scripts that author UI prefabs.
    /// <para>
    /// Every universe's HUD is generated as a prefab from code, and each authoring script used to
    /// carry its own copy of the same panel, label, and anchor helpers with its own colours. This
    /// kit is the one copy: the command UI prefabs are built with it, and a universe HUD that nests
    /// them uses the same palette and primitives so the pieces read as one interface.
    /// </para>
    /// </summary>
    public static class UIAuthoringKit {
        public const string FontPath = "Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset";

        // --- Palette ----------------------------------------------------------
        // Derived from the shared settings menu so every generated HUD matches the pause menu.
        public static readonly Color PanelFill = new(0.075f, 0.085f, 0.1f, 0.94f);
        public static readonly Color PanelBorder = new(0.22f, 0.26f, 0.3f, 1f);
        public static readonly Color CardFill = new(0.13f, 0.16f, 0.2f, 1f);
        public static readonly Color Track = new(0.04f, 0.05f, 0.065f, 1f);
        public static readonly Color Header = new(0.72f, 0.86f, 1f, 1f);
        public static readonly Color Ink = new(0.93f, 0.96f, 1f, 1f);
        public static readonly Color Muted = new(0.62f, 0.67f, 0.74f, 1f);
        public static readonly Color Accent = new(0.1f, 0.49f, 0.75f, 1f);
        public static readonly Color Gold = new(1f, 0.86f, 0.45f, 1f);
        public static readonly Color Danger = new(0.66f, 0.2f, 0.18f, 1f);
        public static readonly Color Warning = new(1f, 0.72f, 0.35f, 1f);
        public static readonly Color Scrim = new(0f, 0f, 0f, 0.72f);

        // --- Built-in art -----------------------------------------------------

        /// <summary>Unity's rounded, nine-sliced UI sprite, the same one the default controls use.</summary>
        public static Sprite RoundedSprite => AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");

        /// <summary>Unity's built-in circle, for badges.</summary>
        public static Sprite CircleSprite => AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");

        static TMP_FontAsset font;

        static TMP_FontAsset Font {
            get {
                if (font == null)
                    font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
                return font;
            }
        }

        // --- Primitives -------------------------------------------------------

        public static GameObject Node(Transform parent, string name) {
            var node = new GameObject(name, typeof(RectTransform));
            node.transform.SetParent(parent, false);
            return node;
        }

        /// <summary>
        /// A filled rectangle. Rounded panels use the built-in sliced sprite; a transparent colour
        /// never blocks clicks, so an invisible layout parent cannot swallow input meant for the world.
        /// </summary>
        public static Image Panel(Transform parent, string name, Color color, bool rounded = true) {
            GameObject node = Node(parent, name);
            var image = node.AddComponent<Image>();
            image.color = color;
            if (rounded) {
                image.sprite = RoundedSprite;
                image.type = Image.Type.Sliced;
            }
            image.raycastTarget = color.a > 0.01f;
            return image;
        }

        /// <summary>A bar that fills by <see cref="Image.fillAmount"/>. A sprite is required: Unity ignores fill on a sprite-less Image.</summary>
        public static Image Fill(Transform parent, string name, Color color, Image.FillMethod method = Image.FillMethod.Horizontal) {
            Image image = Panel(parent, name, color, rounded: false);
            image.sprite = RoundedSprite;
            image.type = Image.Type.Filled;
            image.fillMethod = method;
            image.fillOrigin = method == Image.FillMethod.Vertical
                ? (int)Image.OriginVertical.Bottom
                : (int)Image.OriginHorizontal.Left;
            image.fillAmount = 1f;
            image.raycastTarget = false;
            return image;
        }

        /// <summary>
        /// The component already on <paramref name="target"/>, or a new one. Builders use this for
        /// everything they put on a prefab's root, because an in-place rebuild keeps the root's
        /// existing components (and the references into them) rather than starting from nothing.
        /// </summary>
        public static T Ensure<T>(GameObject target) where T : Component {
            // Deliberately not ??: a destroyed component is only null through Unity's == operator.
            T existing = target.GetComponent<T>();
            return existing != null ? existing : target.AddComponent<T>();
        }

        public static void Border(Component target, Color color, float width = 1.5f) {
            var outline = Ensure<Outline>(target.gameObject);
            outline.effectColor = color;
            outline.effectDistance = new Vector2(width, -width);
        }

        public static TextMeshProUGUI Label(
            Transform parent, string name, string text, float size, Color color,
            TextAlignmentOptions alignment = TextAlignmentOptions.MidlineLeft, bool bold = false) {
            GameObject node = Node(parent, name);
            var label = node.AddComponent<TextMeshProUGUI>();
            label.text = text;
            label.fontSize = size;
            label.color = color;
            label.alignment = alignment;
            label.raycastTarget = false;
            label.overflowMode = TextOverflowModes.Ellipsis;
            if (bold)
                label.fontStyle = FontStyles.Bold;
            if (Font != null)
                label.font = Font;
            return label;
        }

        /// <summary>
        /// A button with a rounded background and a centred caption. Colour transitions are tinted
        /// from the authored background so hover and press read on any accent.
        /// </summary>
        public static Button Button(
            Transform parent, string name, string caption, Color color, float fontSize, out TextMeshProUGUI label) {
            Image background = Panel(parent, name, color);
            var button = background.gameObject.AddComponent<Button>();
            button.targetGraphic = background;
            ColorBlock colors = ColorBlock.defaultColorBlock;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.18f, 1.18f, 1.18f, 1f);
            colors.pressedColor = new Color(0.78f, 0.78f, 0.78f, 1f);
            colors.selectedColor = Color.white;
            colors.disabledColor = new Color(0.55f, 0.55f, 0.55f, 0.6f);
            colors.colorMultiplier = 1f;
            colors.fadeDuration = 0.08f;
            button.colors = colors;
            // Keyboard navigation would steal focus from the world input on every click.
            var navigation = new Navigation { mode = Navigation.Mode.None };
            button.navigation = navigation;

            label = Label(background.transform, "Label", caption, fontSize, Ink, TextAlignmentOptions.Center, bold: true);
            Stretch(label, 4f);
            return button;
        }

        /// <summary>
        /// A single-line TextMeshPro input field with a placeholder. It carries the shared
        /// <see cref="TextboxHelper"/>, so while it has focus the keyboard types into it instead of
        /// also moving the player's character and firing hotkeys.
        /// </summary>
        public static TMP_InputField InputField(
            Transform parent, string name, string placeholder, float fontSize,
            TMP_InputField.ContentType contentType = TMP_InputField.ContentType.Standard) {
            Image background = Panel(parent, name, Track);
            Border(background, PanelBorder, 1f);

            GameObject viewport = Node(background.transform, "TextArea");
            Stretch(viewport.transform, 10f, 10f, 4f, 4f);
            viewport.AddComponent<RectMask2D>();

            TextMeshProUGUI hint = Label(viewport.transform, "Placeholder", placeholder, fontSize, Muted);
            hint.fontStyle = FontStyles.Italic;
            Stretch(hint, 0f);
            TextMeshProUGUI text = Label(viewport.transform, "Text", string.Empty, fontSize, Ink);
            text.overflowMode = TextOverflowModes.Overflow;
            Stretch(text, 0f);

            var field = background.gameObject.AddComponent<TMP_InputField>();
            field.targetGraphic = background;
            field.textViewport = Rect(viewport.transform);
            field.textComponent = text;
            field.placeholder = hint;
            field.contentType = contentType;
            field.lineType = TMP_InputField.LineType.SingleLine;
            if (Font != null)
                field.fontAsset = Font;
            field.pointSize = fontSize;
            field.navigation = new Navigation { mode = Navigation.Mode.None };
            background.gameObject.AddComponent<TextboxHelper>();
            return field;
        }

        /// <param name="gameHelp">
        /// True when the text teaches a game mode's controls rather than stating a fact, so the
        /// player's Game Help setting (<see cref="GameHelp"/>) can hide it.
        /// </param>
        public static HoverItem Hover(Component target, string text, bool gameHelp = false) {
            var hover = Ensure<HoverItem>(target.gameObject);
            hover.SetText(text);
            var serialized = new SerializedObject(hover);
            serialized.FindProperty("isGameHelp").boolValue = gameHelp;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return hover;
        }

        public static LayoutElement Layout(Component target, float preferredWidth = -1f, float preferredHeight = -1f,
            float flexibleWidth = -1f, float flexibleHeight = -1f) {
            var element = Ensure<LayoutElement>(target.gameObject);
            element.preferredWidth = preferredWidth;
            element.preferredHeight = preferredHeight;
            element.flexibleWidth = flexibleWidth;
            element.flexibleHeight = flexibleHeight;
            return element;
        }

        public static VerticalLayoutGroup Column(Component target, int padding, float spacing) {
            var layout = Ensure<VerticalLayoutGroup>(target.gameObject);
            layout.padding = new RectOffset(padding, padding, padding, padding);
            layout.spacing = spacing;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            return layout;
        }

        public static HorizontalLayoutGroup Row(Component target, int padding, float spacing) {
            var layout = Ensure<HorizontalLayoutGroup>(target.gameObject);
            layout.padding = new RectOffset(padding, padding, padding, padding);
            layout.spacing = spacing;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = true;
            return layout;
        }

        /// <summary>Grows the target to fit its layout vertically, keeping its authored width.</summary>
        public static void FitHeight(Component target) {
            var fitter = Ensure<ContentSizeFitter>(target.gameObject);
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        }

        // --- Rect helpers -----------------------------------------------------

        public static RectTransform Rect(Component target) => target.GetComponent<RectTransform>();

        public static void Anchor(Component target, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot,
            Vector2 position, Vector2 size) {
            RectTransform rect = Rect(target);
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }

        public static void Stretch(Component target, float padding) =>
            Stretch(target, padding, padding, padding, padding);

        public static void Stretch(Component target, float left, float right, float top, float bottom) {
            RectTransform rect = Rect(target);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = new Vector2(left, bottom);
            rect.offsetMax = new Vector2(-right, -top);
        }

        /// <summary>Spans the parent's width at a fixed height, measured down from its top edge.</summary>
        public static void TopBand(Component target, float top, float height, float left = 0f, float right = 0f) {
            RectTransform rect = Rect(target);
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 1f);
            rect.offsetMin = new Vector2(left, -top - height);
            rect.offsetMax = new Vector2(-right, -top);
        }

        /// <summary>Spans the parent's width at a fixed height, measured up from its bottom edge.</summary>
        public static void BottomBand(Component target, float bottom, float height, float left = 0f, float right = 0f) {
            RectTransform rect = Rect(target);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.offsetMin = new Vector2(left, bottom);
            rect.offsetMax = new Vector2(-right, bottom + height);
        }

        // --- Serialization ----------------------------------------------------

        /// <summary>
        /// Writes serialized fields through <see cref="SerializedObject"/>, so references are recorded
        /// exactly as the Inspector would record them. An unknown field name is a bug in the authoring
        /// script and fails loudly rather than leaving a silently unwired prefab.
        /// </summary>
        public static void Wire(Object target, params (string field, Object value)[] bindings) {
            var serialized = new SerializedObject(target);
            foreach ((string field, Object value) in bindings) {
                SerializedProperty property = serialized.FindProperty(field);
                if (property == null)
                    throw new InvalidOperationException(
                        $"UI authoring: {target.GetType().Name} has no serialized field '{field}'.");
                property.objectReferenceValue = value;
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        public static void WireArray(Object target, string field, Object[] values) {
            var serialized = new SerializedObject(target);
            SerializedProperty property = serialized.FindProperty(field);
            if (property == null || !property.isArray)
                throw new InvalidOperationException(
                    $"UI authoring: {target.GetType().Name} has no serialized array '{field}'.");
            property.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++)
                property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        public static void EnsureFolder(string path) {
            if (AssetDatabase.IsValidFolder(path))
                return;
            string parent = System.IO.Path.GetDirectoryName(path)?.Replace('\\', '/');
            if (!string.IsNullOrEmpty(parent))
                EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
        }

        /// <summary>
        /// Rebuilds a code-authored prefab in place. An existing asset is opened with its root
        /// GameObject and root components intact - so scene fields and nested prefab instances that
        /// point into it survive - its children are cleared, <paramref name="build"/> repopulates it,
        /// and it is saved back over itself. A missing asset is created from a fresh root.
        /// </summary>
        public static GameObject RebuildPrefab(string path, string rootName, Action<GameObject> build) {
            bool exists = AssetDatabase.LoadAssetAtPath<GameObject>(path) != null;
            GameObject root = exists
                ? PrefabUtility.LoadPrefabContents(path)
                : new GameObject(rootName, typeof(RectTransform));
            try {
                for (int i = root.transform.childCount - 1; i >= 0; i--)
                    Object.DestroyImmediate(root.transform.GetChild(i).gameObject);
                build(root);
                return PrefabUtility.SaveAsPrefabAsset(root, path);
            } finally {
                if (exists)
                    PrefabUtility.UnloadPrefabContents(root);
                else
                    Object.DestroyImmediate(root);
            }
        }

        /// <summary>Nests an authored prefab as a connected instance, so later changes to it still flow through.</summary>
        public static T Nest<T>(T prefab, Transform parent, string name) where T : Component {
            if (prefab == null)
                throw new InvalidOperationException($"UI authoring: cannot nest a missing prefab for '{name}'.");
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab.gameObject, parent);
            instance.name = name;
            return instance.GetComponent<T>();
        }
    }
}
