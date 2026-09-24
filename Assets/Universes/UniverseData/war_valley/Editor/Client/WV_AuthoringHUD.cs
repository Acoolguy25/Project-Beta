using RyanAssets.Client.ClientUI.Command;
using RyanAssets.Client.ClientUI.Command.Editor;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Universes.UniverseData.war_valley.Client;
using Universes.UniverseData.war_valley.Shared;
using static RyanAssets.Client.ClientUI.Command.Editor.UIAuthoringKit;

namespace Universes.UniverseData.war_valley.Editor.Client {
    /// <summary>
    /// Authors the commander HUD prefab and drops its controller into the start scene.
    /// <para>
    /// The HUD is generated as a prefab asset rather than assembled at runtime, so the canvas, its
    /// panels, and every label exist as authored, inspectable objects that <c>WV_HUD</c> only writes
    /// into. The building panel and the build/research menu are the shared command UI from
    /// RyanAssets, nested as connected prefab instances, and everything else is drawn with the same
    /// authoring kit and palette so the War Valley pieces read as one interface with them.
    /// </para>
    /// <para>
    /// The prefab is rebuilt in place, keeping its root and root components, so the start scene's
    /// reference to its <c>WV_HUD</c> survives every rebuild. When the prefab is older than this
    /// script - it lacks a panel the code now binds - it is rebuilt automatically the next time the
    /// Editor loads, so pulling new HUD code does not leave a stale HUD behind.
    /// </para>
    /// <para>
    /// This half lives in its own client-only Editor assembly because it references the war_valley
    /// Client assembly, which does not exist in the dedicated-server Editor.
    /// </para>
    /// </summary>
    [InitializeOnLoad]
    public static class WV_AuthoringHUD {
        const string Root = "Assets/Universes/UniverseData/war_valley";
        const string HudPath = Root + "/Client/WV_HUD.prefab";
        const string StartScenePath = Root + "/war_valley_start.unity";
        /// <summary>Foot soldiers have no model of their own; the infantry icon baked from the robot rig they use stands in.</summary>
        const string TroopIconPath = Root + "/Presentation/Icons/Infantry.png";
        const string AutoRebuildSessionKey = "WV_AuthoringHUD.AutoRebuildAttempted";

        static readonly Color BoxFill = new(0.35f, 1f, 0.55f, 0.15f);
        static readonly Color BoxEdge = new(0.35f, 1f, 0.55f, 0.85f);
        static readonly Color ButtonFill = new(0.16f, 0.2f, 0.25f, 1f);

        /// <summary>Command card size. The option menu and building panel dock to the right of it.</summary>
        static readonly Vector2 CommandCardSize = new(640f, 124f);
        const float Margin = 16f;

        static WV_AuthoringHUD() {
            // Deferred: asset operations are not allowed while the domain is still loading.
            EditorApplication.delayCall += RebuildIfStale;
        }

        [MenuItem("Ryan/War Valley/Rebuild HUD")]
        public static void RebuildHud() {
            if (!CommandUIAuthoring.PrefabsExist())
                CommandUIAuthoring.RebuildAll();
            BuildHud();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("War Valley: rebuilt the commander HUD prefab.");
        }

        /// <summary>
        /// Rebuilds the HUD once per Editor session when the prefab predates the code that binds it.
        /// Skipped in batch mode and play mode, where assets must not change underneath a build or a
        /// running game, and never retried in a session that already tried, so a failing rebuild
        /// reports once rather than on every domain reload.
        /// </summary>
        static void RebuildIfStale() {
            if (Application.isBatchMode
                || EditorApplication.isPlayingOrWillChangePlaymode
                || SessionState.GetBool(AutoRebuildSessionKey, false))
                return;

            var hud = AssetDatabase.LoadAssetAtPath<WV_HUD>(HudPath);
            if (hud != null && IsCurrent(hud) && CommandUIAuthoring.PrefabsExist())
                return;

            SessionState.SetBool(AutoRebuildSessionKey, true);
            Debug.Log("War Valley: the commander HUD prefab predates the current HUD code; rebuilding it.");
            if (!CommandUIAuthoring.PrefabsExist())
                CommandUIAuthoring.RebuildAll();
            RebuildHud();
        }

        /// <summary>True when every panel the current <see cref="WV_HUD"/> binds is present in the prefab.</summary>
        static bool IsCurrent(WV_HUD hud) {
            var serialized = new SerializedObject(hud);
            foreach (string field in new[] { "structurePanel", "optionMenu", "commandMenu", "researchLabel" }) {
                SerializedProperty property = serialized.FindProperty(field);
                if (property == null || property.objectReferenceValue == null)
                    return false;
            }
            return true;
        }

        /// <summary>Adds the commander HUD controller to the start scene if it is not already there.</summary>
        [MenuItem("Ryan/War Valley/Wire Start Scene")]
        public static void WireStartScene() {
            var scene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(
                StartScenePath, UnityEditor.SceneManagement.OpenSceneMode.Additive);
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

                WV_ClientController controller = Ensure<WV_ClientController>(host);
                Wire(controller, ("hudPrefab", AssetDatabase.LoadAssetAtPath<WV_HUD>(HudPath)));

                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
                UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);
                Debug.Log("War Valley: start scene now hosts the commander HUD controller.");
            } finally {
                UnityEditor.SceneManagement.EditorSceneManager.CloseScene(scene, true);
            }
        }

        static void BuildHud() => RebuildPrefab(HudPath, "WV_HUD", root => {
            var canvas = Ensure<Canvas>(root);
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            // Below the shared topbar and menus so War Valley never covers chat or settings.
            canvas.sortingOrder = 2;
            var scaler = Ensure<CanvasScaler>(root);
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            Ensure<GraphicRaycaster>(root);
            var hud = Ensure<WV_HUD>(root);

            RectTransform selectionBox = BuildSelectionBox(root.transform);
            BuildEconomy(root.transform, out TextMeshProUGUI funds, out TextMeshProUGUI income, out TextMeshProUGUI research);
            GameObject selectionPanel = BuildSelectionSummary(root.transform, out TextMeshProUGUI selectionLabel);
            GameObject commandPanel = BuildCommandCard(root.transform, out WV_CommandMenu commandMenu);

            // The building panel docks bottom-right; the build/research menu stacks above it (the
            // HUD keeps the two from overlapping as the panel's height changes with its contents).
            SelectionInfoPanel structurePanel = Nest(
                AssetDatabase.LoadAssetAtPath<SelectionInfoPanel>(CommandUIAuthoring.SelectionPanelPath),
                root.transform, "StructurePanel");
            Anchor(structurePanel, Vector2.right, Vector2.right, Vector2.right, new Vector2(-Margin, Margin),
                new Vector2(CommandUIAuthoring.SelectionPanelWidth, 300f));
            structurePanel.gameObject.SetActive(false);

            CommandOptionGrid optionMenu = Nest(
                AssetDatabase.LoadAssetAtPath<CommandOptionGrid>(CommandUIAuthoring.OptionPanelPath),
                root.transform, "BuildMenu");
            Anchor(optionMenu, Vector2.right, Vector2.right, Vector2.right, new Vector2(-Margin, Margin),
                new Vector2(CommandUIAuthoring.OptionPanelWidth, CommandUIAuthoring.OptionPanelHeight));
            optionMenu.gameObject.SetActive(false);

            TextMeshProUGUI hint = Label(root.transform, "HintLabel", string.Empty, 18f, Ink, TextAlignmentOptions.Bottom);
            hint.textWrappingMode = TextWrappingModes.Normal;
            Anchor(hint, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, Margin + CommandCardSize.y + 10f), new Vector2(CommandCardSize.x + 120f, 52f));

            Wire(hud,
                ("fundsLabel", funds), ("incomeLabel", income), ("researchLabel", research),
                ("selectionBox", selectionBox), ("selectionLabel", selectionLabel), ("selectionPanel", selectionPanel),
                ("structurePanel", structurePanel), ("optionMenu", optionMenu),
                ("commandPanel", commandPanel), ("commandMenu", commandMenu), ("hintLabel", hint));
            WireTroopIcons(hud);
        });

        static void WireTroopIcons(WV_HUD hud) {
            var icon = AssetDatabase.LoadAssetAtPath<Sprite>(TroopIconPath);
            if (icon == null)
                Debug.LogWarning($"War Valley: no troop icon at {TroopIconPath}; troop cards will draw an empty frame.");

            var serialized = new SerializedObject(hud);
            SerializedProperty icons = serialized.FindProperty("troopIcons");
            var kinds = new[] { WV_TroopKind.Knife, WV_TroopKind.Gunner };
            icons.arraySize = kinds.Length;
            for (int i = 0; i < kinds.Length; i++) {
                SerializedProperty entry = icons.GetArrayElementAtIndex(i);
                entry.FindPropertyRelative("kind").intValue = (int)kinds[i];
                entry.FindPropertyRelative("icon").objectReferenceValue = icon;
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        // --- Pieces -----------------------------------------------------------

        /// <summary>
        /// Anchored to the bottom-left with a bottom-left pivot, so the controller can drive it
        /// straight from screen pixels.
        /// </summary>
        static RectTransform BuildSelectionBox(Transform parent) {
            Image box = Panel(parent, "SelectionBox", BoxFill, rounded: false);
            box.raycastTarget = false;
            RectTransform rect = Rect(box);
            rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.zero;
            rect.sizeDelta = Vector2.zero;
            Border(box, BoxEdge, 2f);
            box.gameObject.SetActive(false);
            return rect;
        }

        /// <summary>
        /// Funds sit in the bottom-left corner, the one part of the screen no panel opens over, with
        /// the side's research in progress on the line beneath them.
        /// </summary>
        static void BuildEconomy(Transform parent, out TextMeshProUGUI funds, out TextMeshProUGUI income,
            out TextMeshProUGUI research) {
            Image panel = Panel(parent, "EconomyPanel", PanelFill);
            Anchor(panel, Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(Margin, Margin), new Vector2(300f, 112f));
            Border(panel, PanelBorder);

            TextMeshProUGUI caption = Label(panel.transform, "Caption", "FUNDS", 14f, Header, bold: true);
            TopBand(caption, 10f, 18f, 14f, 150f);
            income = Label(panel.transform, "IncomeLabel", "No income", 14f, Muted, TextAlignmentOptions.MidlineRight);
            TopBand(income, 10f, 18f, 140f, 14f);
            funds = Label(panel.transform, "FundsLabel", "0", 34f, Gold, bold: true);
            TopBand(funds, 30f, 42f, 14f, 14f);
            research = Label(panel.transform, "ResearchLabel", string.Empty, 13f, Warning);
            BottomBand(research, 10f, 18f, 14f, 14f);
        }

        /// <summary>The unit and troop selection summary, stacked above the funds card.</summary>
        static GameObject BuildSelectionSummary(Transform parent, out TextMeshProUGUI label) {
            Image panel = Panel(parent, "SelectionPanel", PanelFill);
            Anchor(panel, Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(Margin, Margin + 112f + 12f),
                new Vector2(300f, 170f));
            Border(panel, PanelBorder);
            label = Label(panel.transform, "SelectionLabel", string.Empty, 17f, Ink, TextAlignmentOptions.TopLeft);
            label.richText = true;
            label.textWrappingMode = TextWrappingModes.Normal;
            Stretch(label, 14f);
            panel.gameObject.SetActive(false);
            return panel.gameObject;
        }

        /// <summary>
        /// The command card: every order and every way of selecting a squad, as buttons, with the
        /// keyboard shortcut printed on each so the keys can be learned from the card.
        /// </summary>
        static GameObject BuildCommandCard(Transform parent, out WV_CommandMenu menu) {
            Image panel = Panel(parent, "CommandPanel", PanelFill);
            Anchor(panel, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, Margin), CommandCardSize);
            Border(panel, PanelBorder);

            const float pad = 12f;
            const float gap = 6f;
            float inner = CommandCardSize.x - pad * 2f;

            // Row 1: the five orders.
            string[] orderLabels = { "Move [M]", "Attack-move [V]", "Attack [T]", "Stop [X]", "Hold [H]" };
            string[] orderTips = {
                "Move the selection to a point. With a building selected, sets its rally point.",
                "Advance to a point, engaging anything hostile met on the way.",
                "Attack one target. Click an enemy after choosing this.",
                "Stop where you are.",
                "Hold this position and only fight what comes into range."
            };
            float orderWidth = (inner - gap * (orderLabels.Length - 1)) / orderLabels.Length;
            var orders = new Button[orderLabels.Length];
            for (int i = 0; i < orderLabels.Length; i++) {
                orders[i] = Button(panel.transform, $"Order{i}", orderLabels[i], Accent, 14f, out _);
                PlaceTopLeft(orders[i], pad + i * (orderWidth + gap), pad, orderWidth, 34f);
                Hover(orders[i], orderTips[i]);
            }

            // Row 2: whole-army selection on the left, control-group recall on the right.
            const float selectWidth = 112f;
            const float groupWidth = 52f;
            string[] selectLabels = { "All Units", "All Troops", "Everything" };
            var selects = new Button[selectLabels.Length];
            for (int i = 0; i < selectLabels.Length; i++) {
                selects[i] = Button(panel.transform, $"Select{i}", selectLabels[i], ButtonFill, 13f, out _);
                PlaceTopLeft(selects[i], pad + i * (selectWidth + gap), pad + 40f, selectWidth, 28f);
            }
            Hover(selects[0], "Select every vehicle and aircraft you command.");
            Hover(selects[1], "Select every foot soldier you command.");
            Hover(selects[2], "Select your whole army.");

            float groupsLeft = pad + inner - (4 * groupWidth + 3 * gap);
            var recalls = new Button[4];
            var sets = new Button[4];
            for (int i = 0; i < 4; i++) {
                float x = groupsLeft + i * (groupWidth + gap);
                recalls[i] = Button(panel.transform, $"Group{i + 1}", $"F{i + 1}", ButtonFill, 13f, out _);
                PlaceTopLeft(recalls[i], x, pad + 40f, groupWidth, 28f);
                Hover(recalls[i], $"Recall control group {i + 1}.");
                sets[i] = Button(panel.transform, $"SetGroup{i + 1}", "Set", new Color(0.12f, 0.15f, 0.19f, 1f), 11f, out _);
                PlaceTopLeft(sets[i], x, pad + 74f, groupWidth, 22f);
                Hover(sets[i], $"Store the current selection as group {i + 1} (Ctrl+F{i + 1}).");
            }

            // Row 3: what the armed order is waiting for, or the key reminder.
            TextMeshProUGUI status = Label(panel.transform, "Status", string.Empty, 13f, Muted);
            PlaceTopLeft(status, pad, pad + 74f, groupsLeft - pad - gap, 22f);

            menu = Ensure<WV_CommandMenu>(panel.gameObject);
            Wire(menu,
                ("moveButton", orders[0]), ("attackMoveButton", orders[1]), ("attackButton", orders[2]),
                ("stopButton", orders[3]), ("holdButton", orders[4]),
                ("selectUnitsButton", selects[0]), ("selectTroopsButton", selects[1]), ("selectAllButton", selects[2]),
                ("statusText", status));
            WireArray(menu, "groupButtons", recalls);
            WireArray(menu, "setGroupButtons", sets);
            return panel.gameObject;
        }

        /// <summary>Places a child by its top-left corner, in pixels from the parent's top-left.</summary>
        static void PlaceTopLeft(Component target, float x, float y, float width, float height) =>
            Anchor(target, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(x, -y),
                new Vector2(width, height));
    }
}
