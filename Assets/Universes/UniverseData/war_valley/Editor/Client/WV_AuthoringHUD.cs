using System.Linq;
using RyanAssets.Client.ClientUI.Command;
using RyanAssets.Client.ClientUI.Command.Editor;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;
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
    /// RyanAssets, nested as connected prefab instances.
    /// </para>
    /// <para>
    /// The look is deliberately quiet: dark glass panels without outlines, one accent colour for
    /// the order waiting for a click, red only for selling, and an icon on every control with its
    /// shortcut printed in the corner, so the bar teaches its own keys without paragraphs of help.
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

        static readonly Color BoxFill = new(0.35f, 1f, 0.55f, 0.12f);
        static readonly Color BoxEdge = new(0.35f, 1f, 0.55f, 0.8f);
        static readonly Color Glass = new(0.055f, 0.065f, 0.08f, 0.86f);
        static readonly Color ButtonFill = new(1f, 1f, 1f, 0.06f);
        static readonly Color Divider = new(1f, 1f, 1f, 0.08f);
        static readonly Color SellFill = new(0.42f, 0.13f, 0.12f, 1f);
        static readonly Color SellConfirmFill = new(0.72f, 0.3f, 0.1f, 1f);
        static readonly Color HealthFill = new(0.36f, 0.85f, 0.5f, 1f);
        static readonly Color FreezeIcon = new(0.6f, 0.85f, 1f, 1f);

        const float Margin = 16f;
        const float CardWidth = 300f;
        const float EconomyCardHeight = 124f;
        const float SelectionPanelHeight = 124f;
        const float BarHeight = 84f;
        const float ButtonSize = 64f;
        const float GroupWidth = 46f;
        const float Gap = 6f;
        const float Pad = 10f;
        const float SectionGap = 17f;

        static WV_AuthoringHUD() {
            // Deferred: asset operations are not allowed while the domain is still loading.
            EditorApplication.delayCall += RebuildIfStale;
        }

        [MenuItem("Ryan/War Valley/Rebuild HUD")]
        public static void RebuildHud() {
            // The HUD nests the shared command panels, so stale copies of those are rebuilt first.
            if (!CommandUIAuthoring.PrefabsCurrent())
                CommandUIAuthoring.RebuildAll();
            BuildHud();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("War Valley: rebuilt the commander HUD prefab.");
        }

        /// <summary>
        /// Rebuilds the HUD once per Editor session when the prefab predates the code that binds it.
        /// Skipped in batch mode and play mode, where assets must not change underneath a build or a
        /// running game, and never retried in a session that already tried.
        /// </summary>
        static void RebuildIfStale() {
            if (Application.isBatchMode
                || EditorApplication.isPlayingOrWillChangePlaymode
                || SessionState.GetBool(AutoRebuildSessionKey, false))
                return;

            var hud = AssetDatabase.LoadAssetAtPath<WV_HUD>(HudPath);
            if (hud != null && IsCurrent(hud) && CommandUIAuthoring.PrefabsCurrent())
                return;

            SessionState.SetBool(AutoRebuildSessionKey, true);
            Debug.Log("War Valley: the commander HUD prefab predates the current HUD code; rebuilding it.");
            RebuildHud();
        }

        /// <summary>True when every panel the current <see cref="WV_HUD"/> binds is present in the prefab.</summary>
        static bool IsCurrent(WV_HUD hud) {
            if (!HasReferences(hud, "structurePanel", "optionMenu", "commandMenu", "researchLabel",
                    "selectionHealthFill", "donateButton", "donatePanel", "researchButton", "sellButton",
                    "sellLabel", "troopRangeIndicator"))
                return false;
            SerializedProperty forces = new SerializedObject(hud).FindProperty("forceCounts");
            return forces != null && forces.arraySize == 4;
        }

        static bool HasReferences(Object target, params string[] fields) {
            var serialized = new SerializedObject(target);
            foreach (string field in fields) {
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
            BuildEconomy(root.transform, hud);
            BuildSelectionSummary(root.transform, hud);
            BuildCommandBar(root.transform, hud);

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

            // Donations open beside the funds card they spend from.
            FundsTransferPanel donatePanel = Nest(
                AssetDatabase.LoadAssetAtPath<FundsTransferPanel>(CommandUIAuthoring.TransferPanelPath),
                root.transform, "DonatePanel");
            Anchor(donatePanel, Vector2.zero, Vector2.zero, Vector2.zero,
                new Vector2(Margin + CardWidth + 12f, Margin), CommandUIAuthoring.TransferPanelSize);
            donatePanel.gameObject.SetActive(false);

            // One line for alerts and notices, just above the order prompt. Long messages end in an
            // ellipsis rather than wrapping over the battlefield.
            TextMeshProUGUI hint = Label(root.transform, "HintLabel", string.Empty, 17f, Ink, TextAlignmentOptions.Bottom);
            hint.richText = true;
            hint.textWrappingMode = TextWrappingModes.NoWrap;
            Anchor(hint, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, Margin + BarHeight + 30f), new Vector2(1100f, 24f));

            Wire(hud,
                ("donatePanel", donatePanel), ("selectionBox", selectionBox),
                ("structurePanel", structurePanel), ("optionMenu", optionMenu), ("hintLabel", hint),
                ("troopRangeIndicator", AssetDatabase.LoadAssetAtPath<WV_RangeIndicator>(
                    Root + "/Presentation/WV_AttackRange.prefab")));
            WireTroopIcons(hud);
        });

        static void WireTroopIcons(WV_HUD hud) {
            var icon = AssetDatabase.LoadAssetAtPath<Sprite>(TroopIconPath);
            if (icon == null)
                Debug.LogWarning($"War Valley: no troop icon at {TroopIconPath}; troop cards will draw an empty frame.");

            var serialized = new SerializedObject(hud);
            SerializedProperty icons = serialized.FindProperty("troopIcons");
            WV_TroopKind[] kinds = WV_TroopCatalog.All.Select(profile => profile.Kind).ToArray();
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
            Border(box, BoxEdge, 1.5f);
            box.gameObject.SetActive(false);
            return rect;
        }

        /// <summary>
        /// Funds in the bottom-left corner: the balance beside a coin, income under it, the four force
        /// limits as icon-and-count pairs, and Donate and Research as icon buttons in the corner.
        /// </summary>
        static void BuildEconomy(Transform parent, WV_HUD hud) {
            Image panel = Panel(parent, "EconomyPanel", Glass);
            Anchor(panel, Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(Margin, Margin),
                new Vector2(CardWidth, EconomyCardHeight));

            Image coin = Icon(panel.transform, "Coin", "paid", Gold);
            PlaceTopLeft(coin, 14f, 14f, 26f, 26f);
            TextMeshProUGUI funds = Label(panel.transform, "FundsLabel", "0", 28f, Gold, bold: true);
            PlaceTopLeft(funds, 46f, 10f, 160f, 34f);

            Image trend = Icon(panel.transform, "IncomeIcon", "trending_up", Muted);
            PlaceTopLeft(trend, 16f, 50f, 16f, 16f);
            TextMeshProUGUI income = Label(panel.transform, "IncomeLabel", "No income", 14f, Muted);
            PlaceTopLeft(income, 38f, 48f, 160f, 20f);

            Button donate = IconButton(panel.transform, "DonateButton", "volunteer_activism", null, null, ButtonFill, out _);
            PlaceTopLeft(donate, CardWidth - 84f, 12f, 34f, 34f);
            Hover(donate, "Donate funds to an ally");
            Button research = IconButton(panel.transform, "ResearchButton", "science", null, null, ButtonFill, out _);
            PlaceTopLeft(research, CardWidth - 46f, 12f, 34f, 34f);
            Hover(research, "Research  ·  needs a Research Station");

            Image divider = Panel(panel.transform, "Divider", Divider, rounded: false);
            divider.raycastTarget = false;
            PlaceTopLeft(divider, 14f, 76f, CardWidth - 28f, 1f);

            string[] forceIcons = { "person", "local_shipping", "flight", "domain" };
            string[] forceTips = { "Troops", "Vehicles", "Aircraft", "Buildings" };
            var counts = new TextMeshProUGUI[forceIcons.Length];
            float slot = (CardWidth - 28f) / forceIcons.Length;
            for (int i = 0; i < forceIcons.Length; i++) {
                float x = 14f + i * slot;
                Image icon = Icon(panel.transform, $"Force{i}Icon", forceIcons[i], Muted);
                PlaceTopLeft(icon, x, 88f, 16f, 16f);
                counts[i] = Label(panel.transform, $"Force{i}Count", "0/0", 13f, Ink);
                PlaceTopLeft(counts[i], x + 20f, 86f, slot - 22f, 20f);
            }

            // Research in progress sits just above the card, so the card itself never changes size.
            TextMeshProUGUI researchLabel = Label(parent, "ResearchLabel", string.Empty, 13f, Warning);
            Anchor(researchLabel, Vector2.zero, Vector2.zero, Vector2.zero,
                new Vector2(Margin + 4f, Margin + EconomyCardHeight + 4f), new Vector2(CardWidth, 18f));

            Wire(hud, ("fundsLabel", funds), ("incomeLabel", income), ("researchLabel", researchLabel),
                ("donateButton", donate), ("researchButton", research));
            WireArray(hud, "forceCounts", counts);
            for (int i = 0; i < forceTips.Length; i++)
                Hover(counts[i], forceTips[i]);
        }

        /// <summary>
        /// The unit and troop selection summary, stacked above the funds card: what is selected, its
        /// combined health as a bar, and Sell. It only shows while something is selected.
        /// </summary>
        static void BuildSelectionSummary(Transform parent, WV_HUD hud) {
            Image panel = Panel(parent, "SelectionPanel", Glass);
            Anchor(panel, Vector2.zero, Vector2.zero, Vector2.zero,
                new Vector2(Margin, Margin + EconomyCardHeight + 28f), new Vector2(CardWidth, SelectionPanelHeight));

            TextMeshProUGUI label = Label(panel.transform, "SelectionLabel", string.Empty, 15f, Ink, TextAlignmentOptions.TopLeft);
            label.richText = true;
            label.textWrappingMode = TextWrappingModes.Normal;
            Stretch(label, 14f, 14f, 12f, 60f);

            Image track = Panel(panel.transform, "HealthTrack", Track, rounded: false);
            track.raycastTarget = false;
            BottomBand(track, 50f, 4f, 14f, 14f);
            Image health = Fill(track.transform, "HealthFill", HealthFill);
            Stretch(health, 0f);

            Button sell = Button(panel.transform, "Sell", "Sell", SellFill, 14f, out TextMeshProUGUI sellLabel);
            BottomBand(sell, 12f, 30f, 14f, 14f);
            Stretch(sellLabel, 30f, 8f, 0f, 0f);
            Image sellIcon = Icon(sell.transform, "Icon", "sell", Ink);
            Anchor(sellIcon, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(10f, 0f), new Vector2(16f, 16f));
            Hover(sell, "Sell the selection  ·  Delete  ·  click twice to confirm");

            panel.gameObject.SetActive(false);
            Wire(hud, ("selectionLabel", label), ("selectionHealthFill", health), ("selectionPanel", panel.gameObject),
                ("sellButton", sell), ("sellLabel", sellLabel));
            // The HUD repaints Sell from these at runtime, so they must match the authored fill.
            var tints = new SerializedObject(hud);
            tints.FindProperty("sellTint").colorValue = SellFill;
            tints.FindProperty("sellConfirmTint").colorValue = SellConfirmFill;
            tints.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// One bar of icon buttons along the bottom: orders, then whole-army selection, then the four
        /// control groups. Every button shows its key, and the order prompt sits just above the bar.
        /// </summary>
        static void BuildCommandBar(Transform parent, WV_HUD hud) {
            (string icon, string caption, string key, string tip)[] orders = {
                ("near_me", "Advance", "V", "Advance  ·  move and fight on the way"),
                ("gps_fixed", "Attack", "T", "Attack one target"),
                ("shield", "Hold", "H", "Hold position and fight anything in range"),
                ("block", "Stop", "X", "Stop where you are"),
                ("ac_unit", "Freeze", "F", "Freeze  ·  stop moving and shooting")
            };
            (string icon, string caption, string key, string tip)[] selects = {
                ("groups", "Army", "Z", "Select your whole army"),
                ("local_shipping", "Vehicles", "U", "Select every vehicle and aircraft"),
                ("person", "Troops", "I", "Select every foot soldier"),
                ("deselect", "Clear", "C", "Clear the selection")
            };
            const int groupCount = 4;

            float width = Pad * 2f
                + orders.Length * ButtonSize + (orders.Length - 1) * Gap + SectionGap
                + selects.Length * ButtonSize + (selects.Length - 1) * Gap + SectionGap
                + groupCount * GroupWidth + (groupCount - 1) * Gap;

            Image bar = Panel(parent, "CommandPanel", Glass);
            Anchor(bar, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, Margin), new Vector2(width, BarHeight));

            float x = Pad;
            float y = (BarHeight - ButtonSize) * 0.5f;
            var orderButtons = new Button[orders.Length];
            for (int i = 0; i < orders.Length; i++) {
                orderButtons[i] = IconButton(bar.transform, $"Order{i}", orders[i].icon, orders[i].caption,
                    orders[i].key, ButtonFill, out _, i == 4 ? FreezeIcon : (Color?)null);
                PlaceTopLeft(orderButtons[i], x, y, ButtonSize, ButtonSize);
                Hover(orderButtons[i], orders[i].tip);
                x += ButtonSize + Gap;
            }
            x = SectionDivider(bar.transform, "Divider0", x - Gap);

            var selectButtons = new Button[selects.Length];
            for (int i = 0; i < selects.Length; i++) {
                selectButtons[i] = IconButton(bar.transform, $"Select{i}", selects[i].icon, selects[i].caption,
                    selects[i].key, ButtonFill, out _);
                PlaceTopLeft(selectButtons[i], x, y, ButtonSize, ButtonSize);
                Hover(selectButtons[i], selects[i].tip);
                x += ButtonSize + Gap;
            }
            x = SectionDivider(bar.transform, "Divider1", x - Gap);

            // Each group is a recall tile with a thin Set strip under it.
            var recalls = new Button[groupCount];
            var sets = new Button[groupCount];
            for (int i = 0; i < groupCount; i++) {
                recalls[i] = Button(bar.transform, $"Group{i + 1}", (i + 1).ToString(), ButtonFill, 18f, out _);
                PlaceTopLeft(recalls[i], x, y, GroupWidth, ButtonSize - 22f);
                TextMeshProUGUI badge = Label(recalls[i].transform, "Key", $"F{i + 1}", 10f, Muted, TextAlignmentOptions.TopRight);
                Stretch(badge, 0f, 4f, 2f, 0f);
                Hover(recalls[i], $"Recall group {i + 1}  ·  F{i + 1}");
                sets[i] = Button(bar.transform, $"SetGroup{i + 1}", "Set", ButtonFill, 10f, out TextMeshProUGUI setLabel);
                setLabel.fontStyle = FontStyles.Normal;
                setLabel.color = Muted;
                Stretch(setLabel, 0f);
                PlaceTopLeft(sets[i], x, y + ButtonSize - 18f, GroupWidth, 18f);
                Hover(sets[i], $"Save the selection as group {i + 1}  ·  Ctrl+F{i + 1}  ·  Alt+F{i + 1} adds");
                x += GroupWidth + Gap;
            }

            // What the armed order is waiting for, centred just above the bar.
            TextMeshProUGUI status = Label(parent, "Status", string.Empty, 14f, Muted, TextAlignmentOptions.Bottom);
            Anchor(status, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, Margin + BarHeight + 6f), new Vector2(width, 20f));

            WV_CommandMenu menu = Ensure<WV_CommandMenu>(bar.gameObject);
            Wire(menu,
                ("moveButton", orderButtons[0]), ("attackButton", orderButtons[1]),
                ("holdButton", orderButtons[2]), ("stopButton", orderButtons[3]), ("freezeButton", orderButtons[4]),
                ("selectAllButton", selectButtons[0]), ("selectUnitsButton", selectButtons[1]),
                ("selectTroopsButton", selectButtons[2]), ("clearButton", selectButtons[3]),
                ("statusText", status));
            WireArray(menu, "groupButtons", recalls);
            WireArray(menu, "setGroupButtons", sets);
            Wire(hud, ("commandPanel", bar.gameObject), ("commandMenu", menu));
        }

        /// <summary>A hairline between two groups of buttons; returns where the next group starts.</summary>
        static float SectionDivider(Transform bar, string name, float afterX) {
            Image line = Panel(bar, name, Divider, rounded: false);
            line.raycastTarget = false;
            float lineX = afterX + (SectionGap - 1f) * 0.5f;
            PlaceTopLeft(line, lineX, 16f, 1f, BarHeight - 32f);
            return afterX + SectionGap;
        }

        /// <summary>Places a child by its top-left corner, in pixels from the parent's top-left.</summary>
        static void PlaceTopLeft(Component target, float x, float y, float width, float height) =>
            Anchor(target, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(x, -y),
                new Vector2(width, height));
    }
}
