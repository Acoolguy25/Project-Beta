using RyanAssets.UI;
using RyanAssets.UI.Editor;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using static RyanAssets.UI.Editor.UIKit;

namespace RyanAssets.Levels.Client.Editor {
    /// <summary>
    /// Generates the level badges (main menu and in-game) from the active <see cref="UITheme"/>:
    /// a pill with a trophy, "Lv N" and a thin progress bar to the next level.
    /// Menu: Ryan/UI/Rebuild Level Badges.
    /// </summary>
    public static class LevelBadgeAuthoring {
        const string Folder = "Packages/com.ryan.networking/Runtime/Levels/Client";
        public const string LobbyBadgePath = Folder + "/LobbyLevelContainer.prefab";
        public const string GameBadgePath = Folder + "/GameLevelContainer.prefab";

        const float Height = 44f;
        const float BarHeight = 3f;
        const float BarInset = 16f;

        [MenuItem("Ryan/UI/Rebuild Level Badges")]
        public static void Build() {
            UIThemeAssets.Ensure();
            BuildBadge<LobbyLevelsClient>("LobbyLevelContainer", LobbyBadgePath);
            BuildBadge<GameLevelsClient>("GameLevelContainer", GameBadgePath);
            AssetDatabase.SaveAssets();
        }

        static void BuildBadge<T>(string name, string path) where T : LevelsClient {
            RectTransform badge = Node(null, name);
            badge.sizeDelta = new Vector2(96f, Height);
            Size(badge, -1f, Height, 0f);
            Rounded(badge, UIColorRole.SurfaceInset, Height / 2f);
            Row(badge.gameObject, 8f, new RectOffset(12, 16, 0, 0), TextAnchor.MiddleLeft);
            FitContent(badge.gameObject, true, false);

            Icon(badge, "emoji_events", 20f, UIColorRole.Currency);
            TextMeshProUGUI level = Text(badge, "Level", "Lv 1", UIText.Strong);

            // Progress to the next level, along the bottom edge of the pill.
            RectTransform track = Node(badge, "Progress");
            track.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            track.anchorMin = new Vector2(0f, 0f);
            track.anchorMax = new Vector2(1f, 0f);
            track.pivot = new Vector2(0.5f, 0f);
            track.offsetMin = new Vector2(BarInset, 6f);
            track.offsetMax = new Vector2(-BarInset, 6f + BarHeight);
            Rounded(track, UIColorRole.SurfaceHover, BarHeight / 2f, raycast: false);

            RectTransform fill = Node(track, "Fill");
            fill.anchorMin = Vector2.zero;
            fill.anchorMax = new Vector2(0.4f, 1f);
            fill.offsetMin = fill.offsetMax = Vector2.zero;
            Rounded(fill, UIColorRole.Accent, BarHeight / 2f, raycast: false);

            var client = badge.gameObject.AddComponent<T>();
            SerializedFields.Set(client, "levelText", level);
            SerializedFields.Set(client, "progressFill", fill);
            SerializedFields.Set(client, "levelFormat", "Lv {0}");
            SavePrefab(badge.gameObject, path);
        }
    }
}
