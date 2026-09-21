using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Universes.UniverseData.war_valley.Client;
using Universes.UniverseData.war_valley.Shared;

namespace Universes.UniverseData.war_valley.Editor.Client {
    /// <summary>
    /// Authors the commander HUD prefab and drops its controller into the start scene.
    /// <para>
    /// The HUD is generated as a prefab asset rather than assembled at runtime, so the canvas, its
    /// panels, and every label exist as authored, inspectable objects that <c>WV_HUD</c> only writes
    /// text into. The production list reuses the project's existing <c>StructureItemCard</c> as its row
    /// prefab, so unit buttons match the build menu instead of introducing a second card style.
    /// </para>
    /// <para>
    /// This half lives in its own client-only Editor assembly because it references the war_valley
    /// Client assembly, which does not exist in the dedicated-server Editor.
    /// </para>
    /// </summary>
    public static class WV_AuthoringHUD {
        const string Root = "Assets/Universes/UniverseData/war_valley";
        const string HudPath = Root + "/Client/WV_HUD.prefab";
        const string StartScenePath = Root + "/war_valley_start.unity";
        const string ItemCardPath = "Assets/RyanAssets/Client/ClientUI/Structure/StructureItemCard.prefab";
        const string FontPath = "Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset";

        static readonly Color PanelFill = new(0.05f, 0.07f, 0.09f, 0.78f);
        static readonly Color Ink = new(0.88f, 0.93f, 0.98f);
        static readonly Color Accent = new(0.45f, 0.85f, 1f);
        static readonly Color Money = new(0.55f, 0.95f, 0.6f);
        static readonly Color BoxFill = new(0.35f, 1f, 0.55f, 0.15f);

        [MenuItem("Ryan/War Valley/Rebuild HUD")]
        public static void RebuildHud() {
            BuildHud();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("War Valley: rebuilt the commander HUD prefab.");
        }

        /// <summary>Adds the commander HUD controller to the start scene if it is not already there.</summary>
        [MenuItem("Ryan/War Valley/Wire Start Scene")]
        public static void WireStartScene() {
            var scene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(
                StartScenePath, UnityEditor.SceneManagement.OpenSceneMode.Additive);
            bool opened = true;
            try {
                GameObject host = null;
                foreach (GameObject rootObject in scene.GetRootGameObjects()) {
                    if (rootObject.name == "War Valley Command") {
                        host = rootObject;
                        break;
                    }
                }

                if (host == null) {
                    host = new GameObject("War Valley Command");
                    UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(host, scene);
                }

                WV_ClientController controller = host.GetComponent<WV_ClientController>();
                if (controller == null)
                    controller = host.AddComponent<WV_ClientController>();

                var serialized = new SerializedObject(controller);
                serialized.FindProperty("hudPrefab").objectReferenceValue =
                    AssetDatabase.LoadAssetAtPath<WV_HUD>(HudPath);
                serialized.ApplyModifiedPropertiesWithoutUndo();

                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
                UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);
                Debug.Log("War Valley: start scene now hosts the commander HUD controller.");
            } finally {
                if (opened)
                    UnityEditor.SceneManagement.EditorSceneManager.CloseScene(scene, true);
            }
        }

        static GameObject BuildHud() {
            var root = new GameObject("WV_HUD", typeof(RectTransform));
            try {
                var canvas = root.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                // Below the shared topbar and menus so War Valley never covers chat or settings.
                canvas.sortingOrder = 2;
                var scaler = root.AddComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920f, 1080f);
                scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
                scaler.matchWidthOrHeight = 0.5f;
                root.AddComponent<GraphicRaycaster>();

                var hud = root.AddComponent<WV_HUD>();

                // Selection rectangle: anchored to the bottom-left with a bottom-left pivot, so the
                // controller can drive it straight from screen pixels.
                GameObject selectionBox = Panel(root.transform, "SelectionBox", BoxFill);
                RectTransform boxRect = selectionBox.GetComponent<RectTransform>();
                boxRect.anchorMin = boxRect.anchorMax = boxRect.pivot = Vector2.zero;
                boxRect.sizeDelta = Vector2.zero;
                Outline(selectionBox, new Color(0.35f, 1f, 0.55f, 0.85f));
                selectionBox.SetActive(false);

                GameObject economyPanel = Panel(root.transform, "EconomyPanel", PanelFill);
                Anchor(economyPanel, Vector2.one, Vector2.one, new Vector2(-24f, -96f),
                    new Vector2(300f, 92f), Vector2.one);
                TextMeshProUGUI caption = Label(economyPanel.transform, "Caption", "FUNDS", 16f,
                    new Color(0.6f, 0.68f, 0.75f), TextAlignmentOptions.MidlineLeft);
                Anchor(caption.gameObject, Vector2.zero, Vector2.one, new Vector2(14f, 16f), Vector2.zero,
                    new Vector2(0.5f, 0.5f));
                TextMeshProUGUI funds = Label(economyPanel.transform, "FundsLabel", "0", 34f, Money,
                    TextAlignmentOptions.MidlineRight);
                Anchor(funds.gameObject, Vector2.zero, Vector2.one, new Vector2(-12f, 16f), Vector2.zero,
                    new Vector2(0.5f, 0.5f));
                TextMeshProUGUI income = Label(economyPanel.transform, "IncomeLabel", "No income", 20f, Accent,
                    TextAlignmentOptions.MidlineRight);
                Anchor(income.gameObject, Vector2.zero, Vector2.one, new Vector2(-12f, -22f), Vector2.zero,
                    new Vector2(0.5f, 0.5f));

                GameObject selectionPanel = Panel(root.transform, "SelectionPanel", PanelFill);
                Anchor(selectionPanel, Vector2.zero, Vector2.zero, new Vector2(24f, 24f),
                    new Vector2(280f, 180f), Vector2.zero);
                TextMeshProUGUI selectionLabel = Label(selectionPanel.transform, "SelectionLabel", string.Empty,
                    20f, Ink, TextAlignmentOptions.TopLeft);
                Stretch(selectionLabel.gameObject, 14f);
                selectionPanel.SetActive(false);

                GameObject productionPanel = Panel(root.transform, "ProductionPanel", PanelFill);
                Anchor(productionPanel, Vector2.right, Vector2.right, new Vector2(-24f, 24f),
                    new Vector2(420f, 440f), Vector2.right);
                TextMeshProUGUI productionTitle = Label(productionPanel.transform, "ProductionTitle", "Production",
                    24f, Accent, TextAlignmentOptions.MidlineLeft);
                Anchor(productionTitle.gameObject, Vector2.up, Vector2.one, new Vector2(0f, -26f),
                    new Vector2(-28f, 34f), new Vector2(0.5f, 1f));
                TextMeshProUGUI queueLabel = Label(productionPanel.transform, "ProductionQueueLabel", "Queue empty",
                    18f, Ink, TextAlignmentOptions.MidlineLeft);
                Anchor(queueLabel.gameObject, Vector2.up, Vector2.one, new Vector2(0f, -66f),
                    new Vector2(-28f, 42f), new Vector2(0.5f, 1f));

                WV_ProductionMenu menu = BuildProductionMenu(productionPanel.transform);
                productionPanel.SetActive(false);

                TextMeshProUGUI hint = Label(root.transform, "HintLabel", string.Empty, 20f,
                    new Color(0.75f, 0.82f, 0.9f), TextAlignmentOptions.Center);
                Anchor(hint.gameObject, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 96f),
                    new Vector2(900f, 30f), new Vector2(0.5f, 0f));

                var serialized = new SerializedObject(hud);
                Bind(serialized, "fundsLabel", funds);
                Bind(serialized, "incomeLabel", income);
                Bind(serialized, "selectionBox", boxRect);
                Bind(serialized, "selectionLabel", selectionLabel);
                Bind(serialized, "selectionPanel", selectionPanel);
                Bind(serialized, "productionPanel", productionPanel);
                Bind(serialized, "productionTitle", productionTitle);
                Bind(serialized, "productionQueueLabel", queueLabel);
                Bind(serialized, "productionMenu", menu);
                Bind(serialized, "hintLabel", hint);
                serialized.ApplyModifiedPropertiesWithoutUndo();

                return PrefabUtility.SaveAsPrefabAsset(root, HudPath);
            } finally {
                Object.DestroyImmediate(root);
            }
        }

        static WV_ProductionMenu BuildProductionMenu(Transform parent) {
            GameObject scrollView = Panel(parent, "UnitScrollView", new Color(0f, 0f, 0f, 0.25f));
            Anchor(scrollView, Vector2.zero, Vector2.one, new Vector2(0f, -56f), new Vector2(-28f, -124f),
                new Vector2(0.5f, 0.5f));

            var scrollRect = scrollView.AddComponent<ScrollRect>();
            scrollRect.horizontal = false;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;
            scrollRect.scrollSensitivity = 24f;

            GameObject viewport = Panel(scrollView.transform, "Viewport", new Color(0f, 0f, 0f, 0f));
            Stretch(viewport, 0f);
            viewport.AddComponent<Mask>().showMaskGraphic = false;

            var content = new GameObject("Content", typeof(RectTransform));
            content.transform.SetParent(viewport.transform, false);
            RectTransform contentRect = content.GetComponent<RectTransform>();
            contentRect.anchorMin = Vector2.up;
            contentRect.anchorMax = Vector2.one;
            contentRect.pivot = new Vector2(0.5f, 1f);
            contentRect.sizeDelta = Vector2.zero;
            var layout = content.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(6, 6, 6, 6);
            layout.spacing = 6f;
            layout.childForceExpandHeight = false;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = true;
            layout.childControlWidth = true;

            scrollRect.viewport = viewport.GetComponent<RectTransform>();
            scrollRect.content = contentRect;

            var menu = scrollView.AddComponent<WV_ProductionMenu>();
            var serialized = new SerializedObject(menu);
            Bind(serialized, "modelPrefab", AssetDatabase.LoadAssetAtPath<GameObject>(ItemCardPath));
            Bind(serialized, "scrollRect", scrollRect);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return menu;
        }

        static void Bind(SerializedObject serialized, string fieldName, Object value) {
            SerializedProperty property = serialized.FindProperty(fieldName);
            if (property == null)
                throw new System.InvalidOperationException(
                    $"War Valley HUD authoring: no serialized field '{fieldName}' on {serialized.targetObject.GetType().Name}.");
            property.objectReferenceValue = value;
        }

        // --- Small UI helpers -------------------------------------------------

        static GameObject Panel(Transform parent, string name, Color color) {
            var panel = new GameObject(name, typeof(RectTransform));
            panel.transform.SetParent(parent, false);
            var image = panel.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = color.a > 0.01f;
            return panel;
        }

        static void Outline(GameObject target, Color color) {
            var outline = target.AddComponent<Outline>();
            outline.effectColor = color;
            outline.effectDistance = new Vector2(2f, 2f);
        }

        static TextMeshProUGUI Label(
            Transform parent, string name, string text, float size, Color color, TextAlignmentOptions alignment) {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var label = go.AddComponent<TextMeshProUGUI>();
            label.text = text;
            label.fontSize = size;
            label.color = color;
            label.alignment = alignment;
            label.raycastTarget = false;
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
            if (font != null)
                label.font = font;
            return label;
        }

        static void Anchor(
            GameObject target, Vector2 anchorMin, Vector2 anchorMax, Vector2 position, Vector2 size, Vector2 pivot) {
            var rect = target.GetComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }

        static void Stretch(GameObject target, float padding) {
            var rect = target.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = new Vector2(padding, padding);
            rect.offsetMax = new Vector2(-padding, -padding);
        }
    }
}
