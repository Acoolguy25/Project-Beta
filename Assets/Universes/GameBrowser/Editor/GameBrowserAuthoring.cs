using RyanAssets.UI;
using RyanAssets.UI.Editor;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using static RyanAssets.UI.Editor.UIKit;

namespace Universes.GameBrowser.Editor {
    /// <summary>
    /// Generates the main menu's game browser (game grid + game page) and its card prefab from the
    /// active <see cref="UITheme"/>. Menu: Ryan/UI/Rebuild Game Browser.
    /// <para>
    /// The canvas is rebuilt inside its existing root, so the root's fader keeps its identity and the
    /// login screen in MainMenu.unity, which fades it in, stays wired.
    /// </para>
    /// </summary>
    public static class GameBrowserAuthoring {
        const string Folder = "Assets/Universes/GameBrowser";
        const string CanvasPath = Folder + "/GameBrowserCanvas.prefab";
        const string CardPath = Folder + "/GameCard.prefab";
        const string LevelBadgePath = "Packages/com.ryan.networking/Runtime/Levels/Client/LobbyLevelContainer.prefab";
        const string MuteButtonPath = "Packages/com.ryan.ui/Runtime/Client/ClientAudio/MuteMusicIcon.prefab";

        const float PageMargin = 56f;
        const float HeaderHeight = 112f;
        static readonly Vector2 CardSize = new(300f, 258f);
        static readonly Vector2 CardSpacing = new(24f, 24f);
        const float CardRadius = 14f;
        static readonly Vector2 GamePageSize = new(1080f, 560f);

        [MenuItem("Ryan/UI/Rebuild Game Browser")]
        public static void Build() {
            UIThemeAssets.Ensure();
            GameObject card = BuildCard();

            GameObject root = PrefabUtility.LoadPrefabContents(CanvasPath);
            try {
                ClearToRoot(root);
                BuildBrowser((RectTransform)root.transform, card);
                PrefabUtility.SaveAsPrefabAsset(root, CanvasPath);
            } finally {
                PrefabUtility.UnloadPrefabContents(root);
            }
            AssetDatabase.SaveAssets();
        }

        /// <summary>Removes everything but the canvas and fader components of the root.</summary>
        static void ClearToRoot(GameObject root) {
            for (int i = root.transform.childCount - 1; i >= 0; i--)
                Object.DestroyImmediate(root.transform.GetChild(i).gameObject);
            foreach (Component component in root.GetComponents<Component>()) {
                if (component is RectTransform or UnityEngine.Canvas or CanvasScaler or GraphicRaycaster or CanvasGroup or CanvasGroupController)
                    continue;
                Object.DestroyImmediate(component);
            }
            var scaler = root.GetComponent<CanvasScaler>();
            scaler.referenceResolution = Theme.referenceResolution;
            scaler.matchWidthOrHeight = 0.5f;
        }

        // --- Card -------------------------------------------------------------------

        static GameObject BuildCard() {
            RectTransform card = Node(null, "GameCard");
            card.sizeDelta = CardSize;
            Rounded(card, UIColorRole.SurfaceInset, CardRadius);

            float thumbWidth = CardSize.x - 16f;
            RectTransform frame = Pinned(card, "ThumbnailFrame", new Vector2(0.5f, 1f), new Vector2(0f, -8f), new Vector2(thumbWidth, thumbWidth * 9f / 16f));
            Rounded(frame, UIColorRole.SurfaceHover, CardRadius - 4f, raycast: false);
            frame.gameObject.AddComponent<Mask>().showMaskGraphic = true;
            RectTransform thumbRect = Fill(frame, "Thumbnail");
            var thumbnail = thumbRect.gameObject.AddComponent<Image>();
            thumbnail.preserveAspect = false;
            thumbnail.raycastTarget = false;

            RectTransform info = Fill(card, "Info", 16f, 8f + thumbWidth * 9f / 16f + 10f, 16f, 12f);
            Column(info.gameObject, 4f);
            TextMeshProUGUI title = Text(info, "Title", "Game", UIText.Strong);
            title.fontSize = Theme.bodyFontSize + 1f;
            Size(title, -1f, 24f);
            RectTransform meta = Node(info, "Players");
            Size(meta, -1f, 20f);
            Row(meta.gameObject, 6f, null, TextAnchor.MiddleLeft);
            RectTransform dot = Node(meta, "LiveDot");
            Size(dot, 8f, 8f);
            Rounded(dot, UIColorRole.Success, 4f, raycast: false);
            TextMeshProUGUI players = Text(meta, "Count", "0 playing", UIText.Small, UIColorRole.TextSecondary);
            Size(players, -1f, -1f, 1f);

            Clickable(card, CardRadius);
            var view = card.gameObject.AddComponent<GameCardView>();
            SerializedFields.Set(view, "thumbnail", thumbnail);
            SerializedFields.Set(view, "title", title);
            SerializedFields.Set(view, "playerCount", players);
            return SavePrefab(card.gameObject, CardPath);
        }

        // --- Browser ------------------------------------------------------------------

        static void BuildBrowser(RectTransform root, GameObject card) {
            var background = Fill(root, "Background").gameObject.AddComponent<Image>();
            Paint(background, UIColorRole.SurfaceRaised);

            var list = root.gameObject.AddComponent<GameListUI>();

            // Header: title, refresh, level badge, account chip
            RectTransform header = Node(root, "Header");
            header.anchorMin = new Vector2(0f, 1f);
            header.anchorMax = new Vector2(1f, 1f);
            header.pivot = new Vector2(0.5f, 1f);
            header.sizeDelta = new Vector2(0f, HeaderHeight);
            Row(header.gameObject, 12f, new RectOffset((int)PageMargin, (int)PageMargin, 16, 0), TextAnchor.MiddleLeft);

            RectTransform heading = Node(header, "Heading");
            Size(heading, -1f, 64f, 1f);
            Column(heading.gameObject, 2f, null, TextAnchor.MiddleLeft);
            Text(heading, "Title", "Discover", UIText.Display);
            Text(heading, "Subtitle", "Pick a game and jump in", UIText.Body, UIColorRole.TextSecondary);

            Button refresh = IconButton(header, "RefreshButton", "refresh", 44f, UIColorRole.SurfaceInset, 12f, out _, out Image refreshIcon);
            UnityEventTools.AddPersistentListener(refresh.onClick, new UnityAction(list.Refresh_ButtonClicked));

            PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(LevelBadgePath), header);

            RectTransform account = Node(header, "AccountButton");
            Size(account, -1f, 44f);
            Rounded(account, UIColorRole.SurfaceInset, 22f);
            Row(account.gameObject, 10f, new RectOffset(8, 18, 0, 0), TextAnchor.MiddleLeft);
            RectTransform avatar = Node(account, "Avatar");
            Size(avatar, 30f, 30f);
            Rounded(avatar, UIColorRole.Accent, 15f, raycast: false);
            Image person = Icon(avatar, "person", 20f, UIColorRole.OnAccent);
            Center(person.rectTransform);
            TextMeshProUGUI username = Text(account, "Username", "Player", UIText.Strong);
            Button accountButton = Clickable(account, 22f);
            UnityEventTools.AddPersistentListener(accountButton.onClick, new UnityAction(list.NavigateToLoginPage_ButtonClicked));

            // Game grid
            RectTransform gridArea = Fill(root, "Games", PageMargin, HeaderHeight + 24f, PageMargin, 0f);
            ScrollRect scroll = GridList(gridArea);

            // Bottom-left music toggle
            var mute = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(MuteButtonPath), root);
            var muteRect = (RectTransform)mute.transform;
            muteRect.anchorMin = muteRect.anchorMax = muteRect.pivot = Vector2.zero;
            muteRect.anchoredPosition = new Vector2(24f, 24f);

            SelectedGameUI page = BuildGamePage(root);

            SerializedFields.Set(list, "modelPrefab", card);
            SerializedFields.Set(list, "scrollRect", scroll);
            SerializedFields.Set(list, "usernameText", username);
            SerializedFields.Set(list, "selectedGameUI", page);
            SerializedFields.Set(list, "refreshIcon", refreshIcon);
        }

        static ScrollRect GridList(RectTransform area) {
            var scroll = area.gameObject.AddComponent<ScrollRect>();
            RectTransform viewport = Fill(area, "Viewport");
            viewport.gameObject.AddComponent<RectMask2D>().padding = new Vector4(-8f, 0f, -8f, 0f);
            viewport.gameObject.AddComponent<Image>().color = Color.clear;
            RectTransform content = Node(viewport, "Content");
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.sizeDelta = Vector2.zero;
            var grid = content.gameObject.AddComponent<GridLayoutGroup>();
            grid.cellSize = CardSize;
            grid.spacing = CardSpacing;
            grid.padding = new RectOffset(0, 0, 4, 32);
            grid.startCorner = GridLayoutGroup.Corner.UpperLeft;
            grid.childAlignment = TextAnchor.UpperLeft;
            scroll.viewport = viewport;
            scroll.content = content;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 32f;
            return scroll;
        }

        static SelectedGameUI BuildGamePage(RectTransform root) {
            RectTransform pageRect = Fill(root, "GamePage");
            CanvasGroupController fader = Fader(pageRect, visible: false);

            RectTransform scrim = Fill(pageRect, "Scrim");
            Paint(scrim.gameObject.AddComponent<Image>(), UIColorRole.Scrim);
            var page = pageRect.gameObject.AddComponent<SelectedGameUI>();
            var dismiss = scrim.gameObject.AddComponent<Button>();
            dismiss.transition = Selectable.Transition.None;
            UnityEventTools.AddPersistentListener(dismiss.onClick, new UnityAction(page.CloseGame_ButtonClicked));

            RectTransform window = Pinned(pageRect, "Window", new Vector2(0.5f, 0.5f), Vector2.zero, GamePageSize);
            Shadow(window, 48f, 16f, 0.55f);
            Rounded(window, UIColorRole.SurfaceRaised, 20f);
            Row(window.gameObject, 36f, Pad(28), TextAnchor.UpperLeft).childForceExpandHeight = true;

            float thumbWidth = 600f;
            RectTransform frame = Node(window, "ThumbnailFrame");
            Size(frame, thumbWidth, thumbWidth * 9f / 16f);
            Rounded(frame, UIColorRole.SurfaceHover, 14f, raycast: false);
            frame.gameObject.AddComponent<Mask>().showMaskGraphic = true;
            var thumbnail = Fill(frame, "Thumbnail").gameObject.AddComponent<Image>();
            thumbnail.raycastTarget = false;

            RectTransform details = Node(window, "Details");
            Size(details, -1f, -1f, 1f);
            VerticalLayoutGroup column = Column(details.gameObject, 12f, new RectOffset(0, 0, 8, 0));
            column.childForceExpandHeight = false;

            TextMeshProUGUI title = Text(details, "Title", "Game", UIText.Display);
            title.textWrappingMode = TextWrappingModes.Normal;
            title.overflowMode = TextOverflowModes.Ellipsis;
            title.margin = new Vector4(0f, 0f, 44f, 0f); // clear the close button

            RectTransform meta = Node(details, "Players");
            Size(meta, -1f, 22f);
            Row(meta.gameObject, 8f, null, TextAnchor.MiddleLeft);
            RectTransform dot = Node(meta, "LiveDot");
            Size(dot, 10f, 10f);
            Rounded(dot, UIColorRole.Success, 5f, raycast: false);
            TextMeshProUGUI players = Text(meta, "Count", "0 playing", UIText.Strong, UIColorRole.TextSecondary);

            TextMeshProUGUI description = Text(details, "Description", "Description", UIText.Body, UIColorRole.TextSecondary, TextAlignmentOptions.TopLeft);
            description.textWrappingMode = TextWrappingModes.Normal;
            description.overflowMode = TextOverflowModes.Ellipsis;
            description.lineSpacing = 6f;
            Size(description, -1f, -1f).flexibleHeight = 1f;

            Button play = TextButton(details, "PlayButton", "Play", "play_arrow", UIColorRole.Accent, UIColorRole.OnAccent, 56f);
            TextMeshProUGUI playLabel = play.GetComponentInChildren<TextMeshProUGUI>();
            playLabel.font = Theme.boldFont;
            playLabel.fontSize = Theme.titleFontSize;
            UnityEventTools.AddPersistentListener(play.onClick, new UnityAction(page.PlayGame_ButtonClicked));

            RectTransform report = Node(details, "ReportButton");
            Size(report, -1f, 28f);
            Row(report.gameObject, 6f, null, TextAnchor.MiddleCenter);
            Icon(report, "flag", 16f, UIColorRole.TextMuted);
            Text(report, "Label", "Report", UIText.Small, UIColorRole.TextMuted);
            report.gameObject.AddComponent<Image>().color = Color.clear;
            Button reportButton = Clickable(report, 8f);
            UnityEventTools.AddPersistentListener(reportButton.onClick, new UnityAction(page.ReportGame_ButtonClicked));

            Button close = IconButton(window, "CloseButton", "close", 40f, UIColorRole.SurfaceInset, 12f, out _, out _);
            close.GetComponent<LayoutElement>().ignoreLayout = true;
            var closeRect = (RectTransform)close.transform;
            closeRect.anchorMin = closeRect.anchorMax = closeRect.pivot = Vector2.one;
            closeRect.anchoredPosition = new Vector2(-20f, -20f);
            UnityEventTools.AddPersistentListener(close.onClick, new UnityAction(page.CloseGame_ButtonClicked));

            SerializedFields.Set(page, "page", fader);
            SerializedFields.Set(page, "thumbnailImage", thumbnail);
            SerializedFields.Set(page, "title", title);
            SerializedFields.Set(page, "description", description);
            SerializedFields.Set(page, "activePlayers", players);
            return page;
        }
    }
}
