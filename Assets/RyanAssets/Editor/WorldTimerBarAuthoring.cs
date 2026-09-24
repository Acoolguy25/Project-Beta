using RyanAssets.Shared.WorldUI;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace RyanAssets.Editor {
    /// <summary>
    /// Builds the shared world-space countdown bar prefab.
    /// <para>
    /// It is generated as a prefab asset rather than assembled at runtime, so the canvas, the
    /// backing, the fill, the owner strip, and the label all exist as authored, inspectable objects
    /// that <see cref="WorldTimerBar"/> only writes values into. Re-running the build saves over the
    /// existing asset, which keeps its GUID and therefore every prefab already pointing at it.
    /// </para>
    /// </summary>
    public static class WorldTimerBarAuthoring {
        const string PrefabPath = "Assets/RyanAssets/Shared/WorldUI/WorldTimerBar.prefab";
        const string FontPath = "Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset";

        /// <summary>
        /// Canvas pixels. Scaled down to world units below, so the bar is authored at a comfortable
        /// UI size and ends up about two metres wide in the world.
        /// </summary>
        static readonly Vector2 BarPixels = new(260f, 72f);
        const float PixelsPerWorldUnit = 100f;

        static readonly Color Backing = new(0.04f, 0.05f, 0.07f, 0.85f);
        static readonly Color Track = new(0.10f, 0.12f, 0.15f, 1f);
        static readonly Color FillColor = new(0.45f, 0.85f, 1f, 1f);
        static readonly Color Ink = new(0.96f, 0.98f, 1f, 1f);

        [MenuItem("Ryan/Shared/Rebuild World Timer Bar")]
        public static GameObject Rebuild() {
            var root = new GameObject("WorldTimerBar", typeof(RectTransform));
            try {
                var canvas = root.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.WorldSpace;
                root.AddComponent<CanvasScaler>().dynamicPixelsPerUnit = 4f;

                RectTransform rootRect = root.GetComponent<RectTransform>();
                rootRect.sizeDelta = BarPixels;
                // The canvas is authored in pixels and shrunk into world space here, so every label
                // and border keeps a sane pixel size instead of being authored in fractions of a metre.
                rootRect.localScale = Vector3.one / PixelsPerWorldUnit;

                var bar = root.AddComponent<WorldTimerBar>();

                // Everything visible hangs off one child, so the bar can be shown and hidden without
                // disabling the component that is driving it.
                GameObject content = Panel(root.transform, "Content", Backing);
                Stretch(content, 0f);

                GameObject accent = Panel(content.transform, "OwnerAccent", Color.grey);
                RectTransform accentRect = accent.GetComponent<RectTransform>();
                accentRect.anchorMin = new Vector2(0f, 1f);
                accentRect.anchorMax = Vector2.one;
                accentRect.pivot = new Vector2(0.5f, 1f);
                accentRect.sizeDelta = new Vector2(0f, 6f);
                accentRect.anchoredPosition = Vector2.zero;

                TextMeshProUGUI label = Label(content.transform, "Label", "Building 0s", 22f, Ink);
                RectTransform labelRect = label.GetComponent<RectTransform>();
                labelRect.anchorMin = new Vector2(0f, 1f);
                labelRect.anchorMax = Vector2.one;
                labelRect.pivot = new Vector2(0.5f, 1f);
                labelRect.sizeDelta = new Vector2(-16f, 32f);
                labelRect.anchoredPosition = new Vector2(0f, -8f);

                GameObject track = Panel(content.transform, "FillTrack", Track);
                RectTransform trackRect = track.GetComponent<RectTransform>();
                trackRect.anchorMin = Vector2.zero;
                trackRect.anchorMax = new Vector2(1f, 0f);
                trackRect.pivot = new Vector2(0.5f, 0f);
                trackRect.sizeDelta = new Vector2(-16f, 18f);
                trackRect.anchoredPosition = new Vector2(0f, 8f);

                GameObject fillObject = Panel(track.transform, "Fill", FillColor);
                Stretch(fillObject, 2f);
                var fill = fillObject.GetComponent<Image>();
                // Filled rather than a stretched rect, so the component sets one float per frame
                // instead of rewriting anchors and forcing a layout rebuild.
                fill.type = Image.Type.Filled;
                fill.fillMethod = Image.FillMethod.Horizontal;
                fill.fillOrigin = (int)Image.OriginHorizontal.Left;
                fill.fillAmount = 0f;

                var serialized = new SerializedObject(bar);
                Bind(serialized, "fill", fill);
                Bind(serialized, "label", label);
                Bind(serialized, "accent", accent.GetComponent<Image>());
                Bind(serialized, "content", content);
                serialized.FindProperty("labelPrefix").stringValue = "Building";
                serialized.ApplyModifiedPropertiesWithoutUndo();

                GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                AssetDatabase.SaveAssets();
                Debug.Log($"Rebuilt the shared world timer bar at {PrefabPath}.");
                return saved;
            } finally {
                Object.DestroyImmediate(root);
            }
        }

        static GameObject Panel(Transform parent, string name, Color color) {
            var panel = new GameObject(name, typeof(RectTransform));
            panel.transform.SetParent(parent, false);
            var image = panel.AddComponent<Image>();
            image.color = color;
            // Nothing on a world-space status bar should ever swallow a click meant for the world.
            image.raycastTarget = false;
            return panel;
        }

        static TextMeshProUGUI Label(Transform parent, string name, string text, float size, Color color) {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var label = go.AddComponent<TextMeshProUGUI>();
            label.text = text;
            label.fontSize = size;
            label.color = color;
            label.alignment = TextAlignmentOptions.Center;
            label.raycastTarget = false;
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
            if (font != null)
                label.font = font;
            return label;
        }

        static void Stretch(GameObject target, float padding) {
            var rect = target.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = new Vector2(padding, padding);
            rect.offsetMax = new Vector2(-padding, -padding);
        }

        static void Bind(SerializedObject serialized, string fieldName, Object value) {
            SerializedProperty property = serialized.FindProperty(fieldName);
            if (property == null)
                throw new System.InvalidOperationException(
                    $"World timer bar authoring: no serialized field '{fieldName}' on " +
                    $"{serialized.targetObject.GetType().Name}.");
            property.objectReferenceValue = value;
        }
    }
}
