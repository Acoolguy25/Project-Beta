using System.Linq;
using RyanAssets.Client.ClientUI.Chat;
using RyanAssets.Client.ClientUI.PlayerList;
using RyanAssets.Client.ClientUI.Topbar;
using RyanAssets.UI;
using RyanAssets.UI.Editor;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using static RyanAssets.UI.Editor.UIKit;

namespace RyanAssets.Client.ClientUI.Editor {
    /// <summary>
    /// Generates the shared in-game HUD prefabs (top bar, chat, player list) from the active
    /// <see cref="UITheme"/>, then re-places them, with the game menu, in the Client and Login
    /// prefabs that nest them. Menu: Ryan/UI/Rebuild Shared HUD.
    /// <para>
    /// The HUD floats over the game in small rounded "glass" pieces along the top edge instead of
    /// full-width bars, and the chat frame fades away while idle.
    /// </para>
    /// </summary>
    public static class HudAuthoring {
        const string HudRoot = "Packages/com.ryan.hud/Runtime/Client/ClientUI";
        const string TopbarPath = HudRoot + "/Topbar/TopbarCanvas.prefab";
        const string ChatPath = HudRoot + "/Chat/ChatCanvas.prefab";
        const string ChatMessagePath = HudRoot + "/Chat/ChatMessagePrefab.prefab";
        const string ChatValidatorPath = HudRoot + "/Chat/ChatValidatorInstance.asset";
        const string PlayerListPath = HudRoot + "/PlayerList/PlayerListCanvas.prefab";
        const string PlayerRowPath = HudRoot + "/PlayerList/PlayerListRowPrefab.prefab";
        const string ClientPrefabPath = "Packages/com.ryan.networking/Runtime/Client/ClientCore/Client.prefab";
        const string LoginPrefabPath = "Packages/com.ryan.networking/Runtime/Login/LoginCanvas.prefab";

        const int TopbarSortingOrder = 50;
        const int PanelSortingOrder = 10;
        const float ButtonRadius = 10f;
        const float PanelRadius = 12f;
        static readonly Vector2 ChatSize = new(420f, 260f);
        const float PlayerListWidth = 300f;
        const float PlayerListMaxRows = 360f;
        const float StatColumnWidth = 58f;

        static float Margin => Theme.screenMargin;
        static float BarHeight => Theme.topbarHeight;
        /// <summary>Top of the panels that hang under the top bar.</summary>
        static float BelowBar => -(Margin + BarHeight + 8f);

        [MenuItem("Ryan/UI/Rebuild Shared HUD")]
        public static void BuildAll() {
            UIThemeAssets.Ensure();
            GameMenuAuthoring.Build();
            PromptAuthoring.Build();
            BuildTopbar();
            BuildChat();
            BuildPlayerList();
            AssetDatabase.SaveAssets();
            ReseatInstances(ClientPrefabPath, TopbarPath, ChatPath, PlayerListPath, GameMenuAuthoring.CanvasPath, PromptAuthoring.CanvasPath);
            ReseatLoginSettings();
            AssetDatabase.SaveAssets();
            Debug.Log("Shared HUD rebuilt.");
        }

        // --- Top bar ----------------------------------------------------------------

        static void BuildTopbar() {
            RectTransform root = Canvas("TopbarCanvas", TopbarSortingOrder);
            var topbar = root.gameObject.AddComponent<ClientTopbar>();

            // Left: menu, chat, coins
            RectTransform left = Pinned(root, "Left", new Vector2(0f, 1f), new Vector2(Margin, -Margin), new Vector2(0f, BarHeight));
            Row(left.gameObject, 8f, null, TextAnchor.MiddleLeft, controlHeight: false);
            FitContent(left.gameObject, true, false);
            PanelButton(left, "MenuButton", "menu", HudPanelId.GameMenu);
            PanelButton(left, "ChatButton", "chat_bubble", HudPanelId.Chat);
            RectTransform coins = Pill(left, "Coins", out _);
            Icon(coins, "monetization_on", 20f, UIColorRole.Currency);
            TextMeshProUGUI coinsText = Text(coins, "CoinsText", "0", UIText.Strong);

            // Centre: game status
            RectTransform status = Pinned(root, "Status", new Vector2(0.5f, 1f), new Vector2(0f, -Margin), new Vector2(0f, BarHeight));
            Rounded(status, UIColorRole.Surface, BarHeight / 2f, raycast: false);
            Row(status.gameObject, 0f, Pad(20, 0), TextAnchor.MiddleCenter);
            FitContent(status.gameObject, true, false);
            TextMeshProUGUI statusText = Text(status, "StatusText", "Game status", UIText.Strong, UIColorRole.TextPrimary, TextAlignmentOptions.Center);

            // Right: player list
            RectTransform right = Pinned(root, "Right", new Vector2(1f, 1f), new Vector2(-Margin, -Margin), new Vector2(0f, BarHeight));
            Row(right.gameObject, 8f, null, TextAnchor.MiddleRight, controlHeight: false);
            FitContent(right.gameObject, true, false);
            RectTransform players = Pill(right, "PlayersButton", out Image playersBackground);
            Image playersIcon = Icon(players, "groups", 22f);
            TextMeshProUGUI playerCount = Text(players, "PlayerCount", "1", UIText.Strong);
            Clickable(players, BarHeight / 2f);
            var playersToggle = players.gameObject.AddComponent<TopbarPanelButton>();
            SerializedFields.SetEnum(playersToggle, "panel", (int)HudPanelId.PlayerList);
            SerializedFields.Set(playersToggle, "background", playersBackground);
            SerializedFields.Set(playersToggle, "icon", playersIcon);

            BuildHealthbar(root);

            SerializedFields.Set(topbar, "statusPill", status.gameObject);
            SerializedFields.Set(topbar, "statusText", statusText);
            SerializedFields.Set(topbar, "coinPill", coins.gameObject);
            SerializedFields.Set(topbar, "coinsText", coinsText);
            SerializedFields.Set(topbar, "playerCountText", playerCount);
            SavePrefab(root.gameObject, TopbarPath);
        }

        /// <summary>A bar-height pill that sizes to its contents.</summary>
        static RectTransform Pill(Transform parent, string name, out Image background) {
            RectTransform pill = Node(parent, name);
            pill.sizeDelta = new Vector2(0f, BarHeight);
            background = Rounded(pill, UIColorRole.Surface, BarHeight / 2f);
            Row(pill.gameObject, 8f, Pad(14, 0), TextAnchor.MiddleCenter);
            FitContent(pill.gameObject, true, false);
            return pill;
        }

        static void PanelButton(Transform parent, string name, string icon, HudPanelId panel) {
            Button button = IconButton(parent, name, icon, BarHeight, UIColorRole.Surface, ButtonRadius, out Image background, out Image iconImage);
            var toggle = button.gameObject.AddComponent<TopbarPanelButton>();
            SerializedFields.SetEnum(toggle, "panel", (int)panel);
            SerializedFields.Set(toggle, "background", background);
            SerializedFields.Set(toggle, "icon", iconImage);
        }

        /// <summary>Health pill under the status pill; only visible while the followed character is hurt.</summary>
        static void BuildHealthbar(RectTransform root) {
            RectTransform bar = Pinned(root, "HealthBar", new Vector2(0.5f, 1f), new Vector2(0f, BelowBar), new Vector2(220f, 32f));
            Rounded(bar, UIColorRole.Surface, 16f, raycast: false);
            Row(bar.gameObject, 10f, Pad(12, 0), TextAnchor.MiddleLeft);
            Icon(bar, "favorite", 18f, UIColorRole.Danger);

            RectTransform track = Node(bar, "Track");
            Size(track, -1f, 8f, 1f);
            Rounded(track, UIColorRole.SurfaceHover, 4f, raycast: false);
            RectTransform fill = Node(track, "Fill");
            fill.anchorMin = Vector2.zero;
            fill.anchorMax = new Vector2(0.7f, 1f);
            fill.offsetMin = fill.offsetMax = Vector2.zero;
            Image fillImage = Rounded(fill, UIColorRole.Success, 4f, raycast: false);
            Object.DestroyImmediate(fillImage.GetComponent<UIThemeColor>()); // coloured by health in code

            TextMeshProUGUI text = Text(bar, "HealthText", "70/100", UIText.Small, UIColorRole.TextPrimary, TextAlignmentOptions.MidlineRight);
            Size(text, 64f, -1f);

            Fader(bar, visible: false, alwaysActive: true);
            var health = bar.gameObject.AddComponent<ClientHealthbar>();
            SerializedFields.Set(health, "healthText", text);
            SerializedFields.Set(health, "fill", fillImage);
        }

        // --- Chat -------------------------------------------------------------------

        static void BuildChat() {
            GameObject message = BuildChatMessage();

            RectTransform root = Canvas("ChatCanvas", PanelSortingOrder);
            RectTransform window = Pinned(root, "Window", new Vector2(0f, 1f), new Vector2(Margin, BelowBar), ChatSize);
            CanvasGroupController fader = Fader(window, visible: true);

            RectTransform frame = Fill(window, "Frame");
            var frameGroup = frame.gameObject.AddComponent<CanvasGroup>();
            Rounded(Fill(frame, "Background"), UIColorRole.Surface, PanelRadius);
            var chatBox = InputBox(frame, "ChatInput", "Press / to chat", 36f, 9f);
            var inputRect = (RectTransform)chatBox.transform;
            inputRect.anchorMin = new Vector2(0f, 0f);
            inputRect.anchorMax = new Vector2(1f, 0f);
            inputRect.pivot = new Vector2(0.5f, 0f);
            inputRect.anchoredPosition = new Vector2(0f, 8f);
            inputRect.sizeDelta = new Vector2(-16f, 36f);
            chatBox.characterLimit = 200;
            chatBox.inputValidator = AssetDatabase.LoadAssetAtPath<TMP_InputValidator>(ChatValidatorPath);

            RectTransform messages = Fill(window, "Messages", 4f, 6f, 4f, 52f);
            var messagesGroup = messages.gameObject.AddComponent<CanvasGroup>();
            ScrollRect scroll = ScrollList(messages, 3f, new RectOffset(10, 10, 4, 4), fitContent: false);

            var panel = root.gameObject.AddComponent<HudPanel>();
            SerializedFields.SetEnum(panel, "id", (int)HudPanelId.Chat);
            SerializedFields.Set(panel, "content", fader);

            var chat = root.gameObject.AddComponent<ClientChat>();
            SerializedFields.Set(chat, "modelPrefab", message);
            SerializedFields.Set(chat, "scrollRect", scroll);
            SerializedFields.Set(chat, "AutoScroll", true);
            SerializedFields.Set(chat, "chatBox", chatBox);
            SerializedFields.Set(chat, "frame", frameGroup);
            SerializedFields.Set(chat, "messages", messagesGroup);
            SavePrefab(root.gameObject, ChatPath);
        }

        static GameObject BuildChatMessage() {
            TextMeshProUGUI text = Text(null, "ChatMessage", "<b>Player</b>  Hello!", UIText.Body);
            text.rectTransform.sizeDelta = new Vector2(380f, 22f);
            text.textWrappingMode = TextWrappingModes.Normal;
            text.overflowMode = TextOverflowModes.Overflow;
            text.richText = true;
            text.lineSpacing = -4f;
            text.fontSharedMaterial = UIThemeAssets.ShadowMaterial(Theme.regularFont);
            return SavePrefab(text.gameObject, ChatMessagePath);
        }

        // --- Player list --------------------------------------------------------------

        static void BuildPlayerList() {
            GameObject rowPrefab = BuildPlayerRow();

            RectTransform root = Canvas("PlayerListCanvas", PanelSortingOrder);
            RectTransform window = Pinned(root, "Window", new Vector2(1f, 1f), new Vector2(-Margin, BelowBar), new Vector2(PlayerListWidth, 120f));
            Rounded(window, UIColorRole.Surface, PanelRadius);
            Column(window.gameObject, 4f, new RectOffset(6, 6, 6, 6));
            FitContent(window.gameObject, false, true);
            CanvasGroupController fader = Fader(window, visible: true);

            RectTransform header = Node(window, "Header");
            Size(header, -1f, 30f);
            Row(header.gameObject, 8f, new RectOffset(10, 10, 0, 0), TextAnchor.MiddleLeft);
            TextMeshProUGUI title = Text(header, "Title", "Players", UIText.Caption, UIColorRole.TextSecondary);
            Size(title, -1f, -1f, 1f);
            var headerColumns = StatColumns(header, UIText.Caption, UIColorRole.TextMuted);
            var headerView = header.gameObject.AddComponent<PlayerListHeader>();
            SerializedFields.Set(headerView, "titleText", title);
            SerializedFields.Set(headerView, "columns", headerColumns);

            RectTransform rows = Node(window, "Rows");
            Size(rows, -1f, 36f);
            ScrollRect scroll = ScrollList(rows, 2f, new RectOffset(0, 0, 0, 0), fitContent: false);
            var fitter = rows.gameObject.AddComponent<ScrollHeightFitter>();
            SerializedFields.Set(fitter, "content", scroll.content);
            SerializedFields.Set(fitter, "maxHeight", PlayerListMaxRows);

            var panel = root.gameObject.AddComponent<HudPanel>();
            SerializedFields.SetEnum(panel, "id", (int)HudPanelId.PlayerList);
            SerializedFields.Set(panel, "content", fader);

            var list = root.gameObject.AddComponent<ClientPlayerList>();
            SerializedFields.Set(list, "modelPrefab", rowPrefab);
            SerializedFields.Set(list, "scrollRect", scroll);
            SavePrefab(root.gameObject, PlayerListPath);
        }

        static GameObject BuildPlayerRow() {
            RectTransform row = Node(null, "PlayerListRow");
            row.sizeDelta = new Vector2(PlayerListWidth - 12f, 34f);
            Size(row, -1f, 34f);
            Image background = Rounded(row, UIColorRole.SurfaceInset, 8f);
            Object.DestroyImmediate(background.GetComponent<UIThemeColor>()); // tinted per player in code
            Row(row.gameObject, 8f, new RectOffset(10, 10, 0, 0), TextAnchor.MiddleLeft);

            RectTransform marker = Node(row, "TeamMarker");
            Size(marker, 4f, 16f);
            Image markerImage = Rounded(marker, UIColorRole.Accent, 2f, raycast: false);
            Object.DestroyImmediate(markerImage.GetComponent<UIThemeColor>()); // team colour in code

            TextMeshProUGUI name = Text(row, "Name", "Player", UIText.Body);
            Size(name, -1f, -1f, 1f);
            PlayerListStatColumns stats = StatColumns(row, UIText.Strong, UIColorRole.TextPrimary);

            var view = row.gameObject.AddComponent<PlayerListRow>();
            SerializedFields.Set(view, "nameText", name);
            SerializedFields.Set(view, "stats", stats);
            SerializedFields.Set(view, "teamMarker", markerImage);
            SerializedFields.Set(view, "background", background);
            return SavePrefab(row.gameObject, PlayerRowPath);
        }

        /// <summary>Right-aligned, fixed-width stat cells, so the header and every row line up.</summary>
        static PlayerListStatColumns StatColumns(Transform parent, UIText style, UIColorRole role) {
            RectTransform columns = Node(parent, "Stats");
            Row(columns.gameObject, 0f, null, TextAnchor.MiddleRight, controlHeight: false);
            FitContent(columns.gameObject, true, false);
            TextMeshProUGUI template = Text(columns, "CellTemplate", "0", style, role, TextAlignmentOptions.MidlineRight);
            Size(template, StatColumnWidth, 24f);
            template.rectTransform.sizeDelta = new Vector2(StatColumnWidth, 24f);
            template.gameObject.SetActive(false);
            var view = columns.gameObject.AddComponent<PlayerListStatColumns>();
            SerializedFields.Set(view, "cellTemplate", template);
            return view;
        }

        // --- Nesting --------------------------------------------------------------------

        /// <summary>
        /// Replaces the nested instances of <paramref name="prefabPaths"/> inside the host prefab with
        /// fresh, override-free instances at the same place. Rebuilt prefabs get new internal ids, so
        /// overrides made on the old instances would otherwise point at objects that no longer exist.
        /// </summary>
        static void ReseatInstances(string hostPath, params string[] prefabPaths) {
            GameObject host = PrefabUtility.LoadPrefabContents(hostPath);
            try {
                foreach (string path in prefabPaths)
                    Reseat(host, path);
                PrefabUtility.SaveAsPrefabAsset(host, hostPath);
            } finally {
                PrefabUtility.UnloadPrefabContents(host);
            }
        }

        static GameObject Reseat(GameObject host, string prefabPath) {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            GameObject old = host.GetComponentsInChildren<Transform>(true)
                .Select(t => t.gameObject)
                .FirstOrDefault(go => PrefabUtility.IsOutermostPrefabInstanceRoot(go)
                    && AssetDatabase.GetAssetPath(PrefabUtility.GetCorrespondingObjectFromSource(go)) == prefabPath);
            if (old == null)
                throw new System.InvalidOperationException($"{host.name} does not nest {prefabPath}.");

            Transform parent = old.transform.parent;
            int index = old.transform.GetSiblingIndex();
            string name = old.name;
            Object.DestroyImmediate(old);

            var fresh = (GameObject)PrefabUtility.InstantiatePrefab(asset, parent);
            fresh.transform.SetSiblingIndex(index);
            fresh.name = name;
            return fresh;
        }

        /// <summary>
        /// The login screen nests its own copy of the game menu as a settings-only screen. It is
        /// private (not reachable by id), so Escape in the main menu still means the in-game menu.
        /// </summary>
        static void ReseatLoginSettings() {
            GameObject host = PrefabUtility.LoadPrefabContents(LoginPrefabPath);
            try {
                GameObject menu = Reseat(host, GameMenuAuthoring.CanvasPath);
                menu.SetActive(true);
                var panel = menu.GetComponent<HudPanel>();
                SerializedFields.Set(panel, "registerById", false);
                SerializedFields.Set(menu.GetComponent<Client.ClientUI.GameSettings.GameSettingsClient>(), "hideGameActionButtons", true);

                MonoBehaviour loginScreen = host.GetComponentsInChildren<MonoBehaviour>(true)
                    .First(component => component != null && component.GetType().Name == "LoginScreen");
                SerializedFields.Set(loginScreen, "settingsPanel", panel);
                PrefabUtility.SaveAsPrefabAsset(host, LoginPrefabPath);
            } finally {
                PrefabUtility.UnloadPrefabContents(host);
            }
        }
    }
}
