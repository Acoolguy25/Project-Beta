using RyanAssets.UI;
using RyanAssets.UI.Editor;
using RyanAssets.UI.GameHud;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Universes.UniverseData.dot_invaders.Client;

namespace Universes.UniverseData.dot_invaders.Editor {
    /// <summary>
    /// Generates the Dot Invaders screen HUD (DI_HUD prefab) from the shared HudKit and plugs it into
    /// the board prefab. Run from Ryan/Dot Invaders in a client (non-server) Editor; safe to re-run.
    /// </summary>
    public static class DI_Authoring {
        const string Root = "Assets/Universes/UniverseData/dot_invaders";
        const string HudPath = Root + "/Client/Prefabs/DI_HUD.prefab";
        const string BoardPath = Root + "/Client/Prefabs/DI_Board.prefab";
        static readonly Color Muted = new(0.71f, 0.74f, 0.78f);

        [MenuItem("Ryan/Dot Invaders/Rebuild HUD")]
        public static void BuildAll() {
            DI_HUD hud = BuildHud();
            GameObject board = PrefabUtility.LoadPrefabContents(BoardPath);
            try {
                Set(board.GetComponent<DI_ClientController>(), "hudPrefab", hud);
                PrefabUtility.SaveAsPrefabAsset(board, BoardPath);
            } finally {
                PrefabUtility.UnloadPrefabContents(board);
            }
            AssetDatabase.SaveAssets();
            Debug.Log("Dot Invaders: rebuilt the HUD.");
        }

        static DI_HUD BuildHud() {
            // Below the shared top bar (50); the board is the game, so the HUD stays thin.
            RectTransform root = HudKit.Canvas("DI_HUD", 15);
            DI_HUD hud = root.gameObject.AddComponent<DI_HUD>();

            // Status strip under the top bar: team, clock, bases, troops.
            RectTransform strip = UIKit.Pinned(root, "Status", new Vector2(0.5f, 1f), new Vector2(0f, -60f), new Vector2(560f, 44f));
            HudKit.Glass(strip, 22f);
            UIKit.Row(strip.gameObject, 4f, new RectOffset(6, 10, 0, 0), TextAnchor.MiddleLeft);
            UIKit.FitContent(strip.gameObject, true, false);
            HudKit.Emblem(strip, "Team", "hub", 34f, Color.cyan, out Image teamDisc, out _);
            TextMeshProUGUI teamName = UIKit.Text(strip, "TeamName", "Cyan team", UIText.Strong);
            UIKit.Size(teamName, 150f, -1f);
            Divider(strip);
            HudChip clock = Bare(HudKit.Chip(strip, "Clock", "timer", "3:00", Muted, 34f));
            Divider(strip);
            HudChip bases = Bare(HudKit.Chip(strip, "Bases", "castle", "0", Muted, 34f));
            HudChip troops = Bare(HudKit.Chip(strip, "Troops", "meeple_army", "0", Muted, 34f));

            DI_PowerBar power = BuildPowerBar(root);

            // Controls reminder, bottom centre, quiet.
            RectTransform controls = UIKit.Pinned(root, "Controls", new Vector2(0.5f, 0f), new Vector2(0f, 18f), new Vector2(600f, 36f));
            HudKit.Glass(controls, 18f, 0.8f);
            UIKit.Row(controls.gameObject, 14f, new RectOffset(12, 16, 0, 0), TextAnchor.MiddleLeft);
            UIKit.FitContent(controls.gameObject, true, false);
            Hint(controls, "Drag", "Route troops");
            Hint(controls, "Drag empty", "Select");
            Hint(controls, "RMB", "Stop");
            Hint(controls, "Esc", "Cancel");
            HudPopup controlsPopup = HudKit.Popup(controls, true);

            HudBanner banner = HudKit.Banner(root, "MatchEnd", new Vector2(0.5f, 0.5f), new Vector2(0f, 60f), 560f, "laurels_trophy", Color.cyan, 88f, "Match over");

            Set(hud, "teamDisc", teamDisc);
            Set(hud, "teamName", teamName);
            Set(hud, "clock", clock);
            Set(hud, "bases", bases);
            Set(hud, "troops", troops);
            Set(hud, "powerBar", power);
            Set(hud, "controls", controlsPopup);
            Set(hud, "banner", banner);
            Set(hud, "victoryIcon", HudKit.Icon("laurels_trophy"));
            Set(hud, "defeatIcon", HudKit.Icon("tombstone"));
            Set(hud, "drawIcon", HudKit.Icon("crossed_swords"));
            return UIKit.SavePrefab(root.gameObject, HudPath).GetComponent<DI_HUD>();
        }

        /// <summary>Power shares under the status strip, with the hover card under the bar.</summary>
        static DI_PowerBar BuildPowerBar(RectTransform root) {
            RectTransform frame = UIKit.Pinned(root, "Power", new Vector2(0.5f, 1f), new Vector2(0f, -112f), new Vector2(760f, 30f));
            DI_PowerBar power = frame.gameObject.AddComponent<DI_PowerBar>();
            Image icon = UIKit.Icon(frame, "bolt", 20f, UIColorRole.Currency);
            icon.rectTransform.anchorMin = icon.rectTransform.anchorMax = icon.rectTransform.pivot = new Vector2(0f, 0.5f);
            icon.rectTransform.anchoredPosition = new Vector2(-28f, 0f);

            RectTransform track = UIKit.Fill(frame, "Track");
            HudKit.Glass(track, 15f);
            RectTransform lanes = UIKit.Fill(track, "Segments", 3f, 3f, 3f, 3f);
            RectTransform template = UIKit.Fill(lanes, "SegmentTemplate");
            Image fill = UIKit.Rounded(template, UIColorRole.Accent, 12f, raycast: false);
            Object.DestroyImmediate(fill.GetComponent<UIThemeColor>());
            TextMeshProUGUI percent = UIKit.Text(template, "Percent", "50%", UIText.Caption, UIColorRole.OnAccent, TextAlignmentOptions.Center);
            Object.DestroyImmediate(percent.GetComponent<UIThemeColor>());
            percent.characterSpacing = 0f;
            percent.overflowMode = TextOverflowModes.Masking;
            UIKit.Stretch(percent.rectTransform);

            RectTransform card = HudKit.Card(root, "PowerDetails", new Vector2(0.5f, 1f), new Vector2(0f, -152f), new Vector2(380f, 170f), popup: false);
            UIKit.Column(card.gameObject, 8f, new RectOffset(18, 18, 14, 16));
            UIKit.FitContent(card.gameObject, false, true);
            RectTransform header = UIKit.Node(card, "Header");
            UIKit.Row(header.gameObject, 10f, null, TextAnchor.MiddleLeft);
            RectTransform swatch = UIKit.Node(header, "Swatch");
            UIKit.Size(swatch, 14f, 14f);
            Image swatchImage = UIKit.Rounded(swatch, UIColorRole.Accent, 7f, raycast: false);
            Object.DestroyImmediate(swatchImage.GetComponent<UIThemeColor>());
            TextMeshProUGUI title = UIKit.Text(header, "Team", "Cyan team", UIText.Strong);
            title.richText = true;
            TextMeshProUGUI body = UIKit.Text(card, "Details", string.Empty, UIText.Body);
            body.richText = true;
            body.textWrappingMode = TextWrappingModes.Normal;
            body.lineSpacing = 6f;

            Set(power, "bar", frame);
            Set(power, "segmentTemplate", fill);
            Set(power, "tooltip", card);
            Set(power, "tooltipSwatch", swatchImage);
            Set(power, "tooltipTitle", title);
            Set(power, "tooltipBody", body);
            return power;
        }

        static void Hint(Transform parent, string key, string action) {
            RectTransform hint = UIKit.Node(parent, key);
            UIKit.Row(hint.gameObject, 6f, null, TextAnchor.MiddleLeft);
            HudKit.KeyCap(hint, key);
            UIKit.Text(hint, "Action", action, UIText.Small, UIColorRole.TextSecondary);
        }

        static void Divider(Transform parent) {
            RectTransform line = UIKit.Node(parent, "Divider");
            UIKit.Size(line, 1f, 24f);
            UIKit.Paint(line.gameObject.AddComponent<Image>(), UIColorRole.Stroke).raycastTarget = false;
        }

        /// <summary>A chip without its own background, for use inside a strip that already has one.</summary>
        static HudChip Bare(HudChip chip) {
            chip.background.enabled = false;
            return chip;
        }

        static void Set(Object target, string field, Object value) {
            var serialized = new SerializedObject(target);
            SerializedProperty property = serialized.FindProperty(field);
            if (property == null)
                throw new System.MissingFieldException(target.GetType().Name, field);
            property.objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
