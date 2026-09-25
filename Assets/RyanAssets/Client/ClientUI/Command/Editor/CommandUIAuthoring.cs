using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using static RyanAssets.Client.ClientUI.Command.Editor.UIAuthoringKit;

namespace RyanAssets.Client.ClientUI.Command.Editor {
    /// <summary>
    /// Authors the shared command UI prefabs: the option card and the build/research panel built
    /// from it, the queue slot, the stat row, the action button, the selection panel, and the funds
    /// transfer panel with its recipient row.
    /// <para>
    /// These are the reusable half of an RTS interface. A universe's HUD nests the panels as
    /// connected prefab instances and binds its own rules to them, so a second strategy mode gets
    /// the same build menu without a second implementation. Re-running a rebuild saves over the
    /// existing assets, which keeps their GUIDs and every nested instance pointing at them.
    /// </para>
    /// </summary>
    public static class CommandUIAuthoring {
        public const string Folder = "Assets/RyanAssets/Client/ClientUI/Command/Prefabs";
        public const string OptionCardPath = Folder + "/CommandOptionCard.prefab";
        public const string OptionPanelPath = Folder + "/CommandOptionPanel.prefab";
        public const string QueueSlotPath = Folder + "/CommandQueueSlot.prefab";
        public const string StatRowPath = Folder + "/CommandStatRow.prefab";
        public const string ActionButtonPath = Folder + "/CommandActionButton.prefab";
        public const string SelectionPanelPath = Folder + "/SelectionInfoPanel.prefab";
        public const string RecipientRowPath = Folder + "/FundsRecipientRow.prefab";
        public const string TransferPanelPath = Folder + "/FundsTransferPanel.prefab";

        /// <summary>Card size and column count of the option panel, shared so the panel is sized to fit whole cards.</summary>
        public static readonly Vector2 CardSize = new(132f, 164f);
        public const int CardColumns = 3;
        public const float CardSpacing = 8f;
        public const int PanelPadding = 12;

        /// <summary>Width of the option panel: three cards, their gaps, and the panel's own padding.</summary>
        public static float OptionPanelWidth =>
            CardColumns * CardSize.x + (CardColumns - 1) * CardSpacing + PanelPadding * 2 + 8f;

        public const float OptionPanelHeight = 420f;

        public const float SelectionPanelWidth = 440f;

        public static readonly Vector2 TransferPanelSize = new(380f, 420f);

        [MenuItem("Ryan/UI/Rebuild Command UI Prefabs")]
        public static void RebuildAll() {
            EnsureFolder(Folder);
            BuildOptionCard();
            BuildQueueSlot();
            BuildStatRow();
            BuildActionButton();
            BuildOptionPanel();
            BuildSelectionPanel();
            BuildRecipientRow();
            BuildTransferPanel();
            AssetDatabase.SaveAssets();
            Debug.Log($"Rebuilt the shared command UI prefabs in {Folder}.");
        }

        /// <summary>True when every command UI prefab exists, so a dependent HUD can nest them.</summary>
        public static bool PrefabsExist() =>
            AssetDatabase.LoadAssetAtPath<GameObject>(OptionCardPath) != null
            && AssetDatabase.LoadAssetAtPath<GameObject>(OptionPanelPath) != null
            && AssetDatabase.LoadAssetAtPath<GameObject>(QueueSlotPath) != null
            && AssetDatabase.LoadAssetAtPath<GameObject>(StatRowPath) != null
            && AssetDatabase.LoadAssetAtPath<GameObject>(ActionButtonPath) != null
            && AssetDatabase.LoadAssetAtPath<GameObject>(SelectionPanelPath) != null
            && AssetDatabase.LoadAssetAtPath<GameObject>(RecipientRowPath) != null
            && AssetDatabase.LoadAssetAtPath<GameObject>(TransferPanelPath) != null;

        /// <summary>
        /// True when the existing prefabs carry every part the current code binds, so a dependent HUD
        /// can tell a stale copy - one generated before the selection panel had its X - from a
        /// current one and rebuild it rather than nest it.
        /// </summary>
        public static bool PrefabsCurrent() {
            if (!PrefabsExist())
                return false;
            var panel = AssetDatabase.LoadAssetAtPath<SelectionInfoPanel>(SelectionPanelPath);
            return panel != null
                && new SerializedObject(panel).FindProperty("closeButton").objectReferenceValue != null;
        }

        static T Load<T>(string path) where T : Component {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            T component = prefab != null ? prefab.GetComponent<T>() : null;
            if (component == null)
                throw new System.InvalidOperationException($"Command UI authoring: missing {typeof(T).Name} prefab at {path}.");
            return component;
        }

        // --- Option card ------------------------------------------------------

        static void BuildOptionCard() => RebuildPrefab(OptionCardPath, "CommandOptionCard", root => {
            Rect(root.transform).sizeDelta = CardSize;

            Image background = Ensure<Image>(root);
            background.sprite = RoundedSprite;
            background.type = Image.Type.Sliced;
            background.color = CardFill;
            Border(background, PanelBorder, 1f);

            var button = Ensure<Button>(root);
            button.targetGraphic = background;
            ColorBlock colors = ColorBlock.defaultColorBlock;
            colors.highlightedColor = new Color(1.25f, 1.25f, 1.25f, 1f);
            colors.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
            colors.disabledColor = new Color(0.8f, 0.8f, 0.8f, 1f);
            colors.fadeDuration = 0.06f;
            button.colors = colors;
            button.navigation = new Navigation { mode = Navigation.Mode.None };

            Image iconFrame = Panel(root.transform, "IconFrame", Track);
            iconFrame.raycastTarget = false;
            Anchor(iconFrame, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -10f), new Vector2(68f, 68f));
            Image icon = Panel(iconFrame.transform, "Icon", Color.white, rounded: false);
            icon.preserveAspect = true;
            icon.raycastTarget = false;
            Stretch(icon, 4f);

            TextMeshProUGUI title = Label(root.transform, "Title", "Option", 16f, Ink, TextAlignmentOptions.Center, bold: true);
            TopBand(title, 82f, 22f, 6f, 6f);
            TextMeshProUGUI subtitle = Label(root.transform, "Subtitle", "Category", 12f, Muted, TextAlignmentOptions.Center);
            TopBand(subtitle, 104f, 16f, 6f, 6f);

            TextMeshProUGUI cost = Label(root.transform, "Cost", "0", 15f, Gold, TextAlignmentOptions.MidlineLeft, bold: true);
            Anchor(cost, Vector2.zero, new Vector2(0.5f, 0f), Vector2.zero, new Vector2(10f, 12f), new Vector2(-10f, 22f));
            TextMeshProUGUI time = Label(root.transform, "Time", "0s", 13f, Muted, TextAlignmentOptions.MidlineRight);
            Anchor(time, new Vector2(0.5f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-10f, 12f), new Vector2(-10f, 22f));

            // Laid over the icon rather than under the text, so the reason something is locked is
            // the first thing read on the card.
            Image stateBand = Panel(root.transform, "StateBand", Scrim, rounded: false);
            stateBand.raycastTarget = false;
            TopBand(stateBand, 30f, 44f, 4f, 4f);
            TextMeshProUGUI stateLabel = Label(stateBand.transform, "StateLabel", "Locked", 12f, Warning,
                TextAlignmentOptions.Center, bold: true);
            stateLabel.textWrappingMode = TextWrappingModes.Normal;
            Stretch(stateLabel, 4f);

            Image progressTrack = Panel(root.transform, "ProgressTrack", Track, rounded: false);
            progressTrack.raycastTarget = false;
            BottomBand(progressTrack, 4f, 5f, 8f, 8f);
            Image progress = Fill(progressTrack.transform, "ProgressFill", Accent);
            Stretch(progress, 0f);

            Image badge = Panel(root.transform, "CountBadge", Accent, rounded: false);
            badge.sprite = CircleSprite;
            badge.raycastTarget = false;
            Anchor(badge, Vector2.one, Vector2.one, Vector2.one, new Vector2(-6f, -6f), new Vector2(26f, 26f));
            TextMeshProUGUI badgeLabel = Label(badge.transform, "CountLabel", "1", 13f, Ink, TextAlignmentOptions.Center, bold: true);
            Stretch(badgeLabel, 0f);

            var hover = Hover(root.transform, string.Empty);
            var card = Ensure<CommandOptionCard>(root);
            Wire(card,
                ("button", button), ("background", background), ("icon", icon),
                ("titleLabel", title), ("subtitleLabel", subtitle), ("costLabel", cost), ("timeLabel", time),
                ("stateBand", stateBand.gameObject), ("stateLabel", stateLabel), ("progressFill", progress),
                ("progressRoot", progressTrack.gameObject), ("countBadge", badge.gameObject),
                ("countLabel", badgeLabel), ("hover", hover));

            // Idle cards are clean: the progress track, lock band, and badge appear only when bound data needs them.
            progressTrack.gameObject.SetActive(false);
            stateBand.gameObject.SetActive(false);
            badge.gameObject.SetActive(false);
        });

        // --- Option panel -----------------------------------------------------

        static void BuildOptionPanel() => RebuildPrefab(OptionPanelPath, "CommandOptionPanel", root => {
            Rect(root.transform).sizeDelta = new Vector2(OptionPanelWidth, OptionPanelHeight);
            Image background = Ensure<Image>(root);
            background.sprite = RoundedSprite;
            background.type = Image.Type.Sliced;
            background.color = PanelFill;
            Border(background, PanelBorder);

            TextMeshProUGUI title = Label(root.transform, "Title", "Build", 22f, Header, TextAlignmentOptions.MidlineLeft, bold: true);
            TopBand(title, 10f, 28f, PanelPadding + 2f, 48f);
            TextMeshProUGUI subtitle = Label(root.transform, "Subtitle", string.Empty, 13f, Muted);
            TopBand(subtitle, 38f, 18f, PanelPadding + 2f, 48f);

            Button close = Button(root.transform, "CloseButton", "X", new Color(0.2f, 0.22f, 0.26f, 1f), 14f, out _);
            Anchor(close, Vector2.one, Vector2.one, Vector2.one, new Vector2(-10f, -10f), new Vector2(28f, 28f));
            Hover(close, "Close", gameHelp: true);

            Image divider = Panel(root.transform, "Divider", PanelBorder, rounded: false);
            divider.raycastTarget = false;
            TopBand(divider, 62f, 1f, PanelPadding, PanelPadding);

            // Scrolls once a building offers more than three rows; most fit without it.
            GameObject scrollView = Node(root.transform, "Scroll");
            Stretch(scrollView.transform, PanelPadding, PanelPadding, 70f, PanelPadding);
            var scroll = scrollView.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 30f;

            GameObject viewport = Node(scrollView.transform, "Viewport");
            Stretch(viewport.transform, 0f);
            viewport.AddComponent<RectMask2D>();
            // A transparent raycast target inside the viewport lets the wheel scroll between cards.
            Image viewportHit = viewport.AddComponent<Image>();
            viewportHit.color = new Color(0f, 0f, 0f, 0f);
            viewportHit.raycastTarget = true;

            GameObject content = Node(viewport.transform, "Cards");
            RectTransform contentRect = Rect(content.transform);
            contentRect.anchorMin = new Vector2(0f, 1f);
            contentRect.anchorMax = Vector2.one;
            contentRect.pivot = new Vector2(0.5f, 1f);
            contentRect.sizeDelta = Vector2.zero;
            var grid = content.AddComponent<GridLayoutGroup>();
            grid.cellSize = CardSize;
            grid.spacing = new Vector2(CardSpacing, CardSpacing);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = CardColumns;
            grid.childAlignment = TextAnchor.UpperLeft;
            grid.padding = new RectOffset(4, 4, 4, 4);
            FitHeight(content.transform);

            scroll.viewport = Rect(viewport.transform);
            scroll.content = contentRect;

            TextMeshProUGUI empty = Label(root.transform, "EmptyLabel", "Nothing to build here", 15f, Muted,
                TextAlignmentOptions.Center);
            Stretch(empty, PanelPadding, PanelPadding, 70f, PanelPadding);
            empty.gameObject.SetActive(false);

            var gridComponent = Ensure<CommandOptionGrid>(root);
            Wire(gridComponent,
                ("cardPrefab", Load<CommandOptionCard>(OptionCardPath)), ("cardRoot", contentRect),
                ("titleLabel", title), ("subtitleLabel", subtitle), ("emptyLabel", empty), ("closeButton", close));
        });

        // --- Queue ------------------------------------------------------------

        static void BuildQueueSlot() => RebuildPrefab(QueueSlotPath, "CommandQueueSlot", root => {
            Rect(root.transform).sizeDelta = new Vector2(60f, 60f);
            Image background = Ensure<Image>(root);
            background.sprite = RoundedSprite;
            background.type = Image.Type.Sliced;
            background.color = CardFill;
            Border(background, PanelBorder, 1f);
            Layout(background, 60f, 60f);

            Image icon = Panel(root.transform, "Icon", Color.white, rounded: false);
            icon.preserveAspect = true;
            icon.raycastTarget = false;
            Stretch(icon, 6f, 6f, 4f, 12f);

            // Rises from the bottom of the slot as the entry trains, over the icon like a cooldown.
            Image progress = Fill(root.transform, "ProgressFill", new Color(Accent.r, Accent.g, Accent.b, 0.45f),
                Image.FillMethod.Vertical);
            Stretch(progress, 2f);

            Image owner = Panel(root.transform, "OwnerAccent", Color.white, rounded: false);
            owner.raycastTarget = false;
            BottomBand(owner, 2f, 4f, 4f, 4f);

            TextMeshProUGUI time = Label(root.transform, "TimeLabel", "0s", 12f, Ink, TextAlignmentOptions.Bottom, bold: true);
            BottomBand(time, 6f, 16f, 2f, 2f);

            Button cancel = Button(root.transform, "CancelButton", "X", Danger, 11f, out _);
            Anchor(cancel, Vector2.one, Vector2.one, new Vector2(0.5f, 0.5f), new Vector2(-4f, -4f), new Vector2(20f, 20f));
            Hover(cancel, "Cancel and refund", gameHelp: true);

            var hover = Hover(background, string.Empty);
            var slot = Ensure<CommandQueueSlot>(root);
            Wire(slot,
                ("icon", icon), ("progressFill", progress), ("timeLabel", time), ("ownerAccent", owner),
                ("cancelButton", cancel), ("hover", hover));
        });

        /// <summary>The queue strip lives inside the selection panel rather than as its own prefab.</summary>
        static CommandQueueStrip BuildQueueStrip(Transform parent) {
            GameObject root = Node(parent, "Queue");
            Column(root.transform, 0, 4f);
            Layout(root.transform, preferredHeight: 86f);

            TextMeshProUGUI header = Label(root.transform, "Header", "Queue", 13f, Header, bold: true);
            Layout(header, preferredHeight: 18f);

            GameObject slots = Node(root.transform, "Slots");
            Layout(slots.transform, preferredHeight: 60f);
            var row = slots.AddComponent<HorizontalLayoutGroup>();
            row.spacing = 6f;
            row.childAlignment = TextAnchor.MiddleLeft;
            row.childControlWidth = true;
            row.childControlHeight = true;
            row.childForceExpandWidth = false;
            row.childForceExpandHeight = false;

            TextMeshProUGUI empty = Label(slots.transform, "EmptyLabel", "Queue empty", 13f, Muted);
            Layout(empty, preferredHeight: 60f).ignoreLayout = true;
            Stretch(empty, 0f);

            var strip = root.AddComponent<CommandQueueStrip>();
            Wire(strip,
                ("slotPrefab", Load<CommandQueueSlot>(QueueSlotPath)), ("slotRoot", Rect(slots.transform)),
                ("headerLabel", header), ("emptyLabel", empty));
            return strip;
        }

        // --- Stats and actions ------------------------------------------------

        static void BuildStatRow() => RebuildPrefab(StatRowPath, "CommandStatRow", root => {
            Rect(root.transform).sizeDelta = new Vector2(400f, 20f);
            Layout(root.transform, preferredHeight: 20f);

            TextMeshProUGUI label = Label(root.transform, "Label", "Stat", 14f, Muted);
            Anchor(label, Vector2.zero, new Vector2(0.55f, 1f), new Vector2(0f, 0.5f), Vector2.zero, Vector2.zero);
            TextMeshProUGUI value = Label(root.transform, "Value", "0", 14f, Ink, TextAlignmentOptions.MidlineRight, bold: true);
            Anchor(value, new Vector2(0.45f, 0f), Vector2.one, new Vector2(1f, 0.5f), Vector2.zero, Vector2.zero);

            var row = Ensure<CommandStatRow>(root);
            Wire(row, ("label", label), ("value", value));
        });

        static void BuildActionButton() => RebuildPrefab(ActionButtonPath, "CommandActionButton", root => {
            Rect(root.transform).sizeDelta = new Vector2(120f, 34f);
            Image background = Ensure<Image>(root);
            background.sprite = RoundedSprite;
            background.type = Image.Type.Sliced;
            background.color = Accent;
            Layout(background, preferredHeight: 34f, flexibleWidth: 1f);

            var button = Ensure<Button>(root);
            button.targetGraphic = background;
            ColorBlock colors = ColorBlock.defaultColorBlock;
            colors.highlightedColor = new Color(1.2f, 1.2f, 1.2f, 1f);
            colors.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
            colors.disabledColor = new Color(0.5f, 0.5f, 0.5f, 0.55f);
            colors.fadeDuration = 0.06f;
            button.colors = colors;
            button.navigation = new Navigation { mode = Navigation.Mode.None };

            TextMeshProUGUI label = Label(root.transform, "Label", "Action", 14f, Ink, TextAlignmentOptions.Center, bold: true);
            Stretch(label, 6f, 6f, 2f, 2f);

            var hover = Hover(background, string.Empty);
            var action = Ensure<CommandActionButton>(root);
            Wire(action, ("button", button), ("background", background), ("label", label), ("hover", hover));
        });

        // --- Selection panel --------------------------------------------------

        static void BuildSelectionPanel() => RebuildPrefab(SelectionPanelPath, "SelectionInfoPanel", root => {
            // Pivoted on its bottom edge so it grows upward from wherever a HUD docks it.
            RectTransform rootRect = Rect(root.transform);
            rootRect.pivot = new Vector2(0.5f, 0f);
            rootRect.sizeDelta = new Vector2(SelectionPanelWidth, 300f);
            Image background = Ensure<Image>(root);
            background.sprite = RoundedSprite;
            background.type = Image.Type.Sliced;
            background.color = PanelFill;
            Border(background, PanelBorder);
            VerticalLayoutGroup column = Column(background, 14, 8f);
            column.padding.left = 20;
            FitHeight(background);

            Image owner = Panel(root.transform, "OwnerAccent", Color.white, rounded: false);
            owner.raycastTarget = false;
            Layout(owner).ignoreLayout = true;
            Anchor(owner, Vector2.zero, new Vector2(0f, 1f), new Vector2(0f, 0.5f), new Vector2(4f, 0f), new Vector2(5f, -16f));

            // Header: icon, name, owner.
            GameObject header = Node(root.transform, "Header");
            Layout(header.transform, preferredHeight: 60f);
            Image iconFrame = Panel(header.transform, "IconFrame", Track);
            iconFrame.raycastTarget = false;
            Anchor(iconFrame, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(60f, 60f));
            Image icon = Panel(iconFrame.transform, "Icon", Color.white, rounded: false);
            icon.preserveAspect = true;
            icon.raycastTarget = false;
            Stretch(icon, 4f);
            // Both lines stop short of the corner the X sits in.
            TextMeshProUGUI title = Label(header.transform, "Title", "Selection", 22f, Header, bold: true);
            Stretch(title, 72f, 30f, 2f, 30f);
            TextMeshProUGUI subtitle = Label(header.transform, "Subtitle", string.Empty, 14f, Muted);
            subtitle.richText = true;
            Stretch(subtitle, 72f, 30f, 32f, 4f);

            // Health bar with its figure written across it.
            Image healthTrack = Panel(root.transform, "Health", Track);
            healthTrack.raycastTarget = false;
            Layout(healthTrack, preferredHeight: 18f);
            Image healthFill = Fill(healthTrack.transform, "HealthFill", new Color(0.35f, 0.85f, 0.4f, 1f));
            Stretch(healthFill, 2f);
            TextMeshProUGUI healthLabel = Label(healthTrack.transform, "HealthLabel", "0 / 0", 12f, Ink,
                TextAlignmentOptions.Center, bold: true);
            Stretch(healthLabel, 0f);

            // Current activity: construction, training, research.
            GameObject status = Node(root.transform, "Status");
            Layout(status.transform, preferredHeight: 26f);
            TextMeshProUGUI statusLabel = Label(status.transform, "StatusLabel", string.Empty, 14f, Warning, bold: true);
            Stretch(statusLabel, 0f, 0f, 0f, 6f);
            Image statusTrack = Panel(status.transform, "StatusTrack", Track, rounded: false);
            statusTrack.raycastTarget = false;
            BottomBand(statusTrack, 0f, 4f);
            Image statusFill = Fill(statusTrack.transform, "StatusFill", Warning);
            Stretch(statusFill, 0f);

            GameObject stats = Node(root.transform, "Stats");
            Column(stats.transform, 0, 2f);

            CommandQueueStrip queue = BuildQueueStrip(root.transform);

            GameObject actions = Node(root.transform, "Actions");
            Layout(actions.transform, preferredHeight: 34f);
            HorizontalLayoutGroup actionRow = Row(actions.transform, 0, 6f);
            actionRow.childForceExpandHeight = true;

            // The X closes whatever the panel describes. It sits in the top-right corner, outside
            // the column, so it stays put however tall the panel grows.
            Button close = Button(root.transform, "CloseButton", "X", new Color(0.2f, 0.22f, 0.26f, 1f), 14f, out _);
            Layout(close).ignoreLayout = true;
            Anchor(close, Vector2.one, Vector2.one, Vector2.one, new Vector2(-10f, -10f), new Vector2(28f, 28f));
            Hover(close, "Close [Esc]", gameHelp: true);

            var panel = Ensure<SelectionInfoPanel>(root);
            Wire(panel,
                ("titleLabel", title), ("subtitleLabel", subtitle), ("icon", icon), ("ownerAccent", owner),
                ("healthRoot", healthTrack.gameObject), ("healthFill", healthFill), ("healthLabel", healthLabel),
                ("statusRoot", status), ("statusLabel", statusLabel), ("statusFill", statusFill),
                ("statusBar", statusTrack.gameObject),
                ("statRowPrefab", Load<CommandStatRow>(StatRowPath)), ("statRoot", Rect(stats.transform)),
                ("actionButtonPrefab", Load<CommandActionButton>(ActionButtonPath)), ("actionRoot", Rect(actions.transform)),
                ("queue", queue), ("closeButton", close));
        });

        // --- Funds transfer -----------------------------------------------------

        static void BuildRecipientRow() => RebuildPrefab(RecipientRowPath, "FundsRecipientRow", root => {
            Rect(root.transform).sizeDelta = new Vector2(TransferPanelSize.x - PanelPadding * 2, 40f);
            Image background = Ensure<Image>(root);
            background.sprite = RoundedSprite;
            background.type = Image.Type.Sliced;
            background.color = CardFill;
            Layout(background, preferredHeight: 40f);

            var button = Ensure<Button>(root);
            button.targetGraphic = background;
            ColorBlock colors = ColorBlock.defaultColorBlock;
            colors.highlightedColor = new Color(1.2f, 1.2f, 1.2f, 1f);
            colors.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
            colors.fadeDuration = 0.06f;
            button.colors = colors;
            button.navigation = new Navigation { mode = Navigation.Mode.None };

            Image accent = Panel(root.transform, "Accent", Color.white, rounded: false);
            accent.sprite = CircleSprite;
            accent.raycastTarget = false;
            Anchor(accent, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(10f, 0f), new Vector2(16f, 16f));

            TextMeshProUGUI name = Label(root.transform, "Name", "Player", 16f, Ink, bold: true);
            Stretch(name, 36f, 120f, 0f, 0f);
            TextMeshProUGUI detail = Label(root.transform, "Detail", string.Empty, 13f, Muted, TextAlignmentOptions.MidlineRight);
            Anchor(detail, new Vector2(1f, 0f), Vector2.one, new Vector2(1f, 0.5f), new Vector2(-10f, 0f), new Vector2(110f, 0f));

            var row = Ensure<FundsRecipientRow>(root);
            Wire(row, ("button", button), ("background", background), ("accent", accent),
                ("nameLabel", name), ("detailLabel", detail));
        });

        static void BuildTransferPanel() => RebuildPrefab(TransferPanelPath, "FundsTransferPanel", root => {
            Rect(root.transform).sizeDelta = TransferPanelSize;
            Image background = Ensure<Image>(root);
            background.sprite = RoundedSprite;
            background.type = Image.Type.Sliced;
            background.color = PanelFill;
            Border(background, PanelBorder);

            TextMeshProUGUI title = Label(root.transform, "Title", "Donate", 22f, Header, bold: true);
            TopBand(title, 10f, 28f, PanelPadding + 2f, 48f);
            TextMeshProUGUI balanceCaption = Label(root.transform, "BalanceCaption", "You have", 13f, Muted);
            TopBand(balanceCaption, 40f, 18f, PanelPadding + 2f, 150f);
            TextMeshProUGUI balance = Label(root.transform, "Balance", "0", 15f, Gold, TextAlignmentOptions.MidlineRight, bold: true);
            TopBand(balance, 40f, 18f, 150f, PanelPadding + 2f);

            Button close = Button(root.transform, "CloseButton", "X", new Color(0.2f, 0.22f, 0.26f, 1f), 14f, out _);
            Anchor(close, Vector2.one, Vector2.one, Vector2.one, new Vector2(-10f, -10f), new Vector2(28f, 28f));
            Hover(close, "Close", gameHelp: true);

            // Recipients: a scrolling list, since a full server has more allies than fit.
            TextMeshProUGUI toCaption = Label(root.transform, "ToCaption", "TO", 13f, Header, bold: true);
            TopBand(toCaption, 66f, 18f, PanelPadding + 2f, PanelPadding);
            GameObject scrollView = Node(root.transform, "Recipients");
            TopBand(scrollView.transform, 88f, 150f, PanelPadding, PanelPadding);
            var scroll = scrollView.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 30f;
            GameObject viewport = Node(scrollView.transform, "Viewport");
            Stretch(viewport.transform, 0f);
            viewport.AddComponent<RectMask2D>();
            Image viewportHit = viewport.AddComponent<Image>();
            viewportHit.color = new Color(0f, 0f, 0f, 0f);
            viewportHit.raycastTarget = true;
            GameObject list = Node(viewport.transform, "List");
            RectTransform listRect = Rect(list.transform);
            listRect.anchorMin = new Vector2(0f, 1f);
            listRect.anchorMax = Vector2.one;
            listRect.pivot = new Vector2(0.5f, 1f);
            listRect.sizeDelta = Vector2.zero;
            Column(list.transform, 0, 4f);
            FitHeight(list.transform);
            scroll.viewport = Rect(viewport.transform);
            scroll.content = listRect;
            TextMeshProUGUI empty = Label(scrollView.transform, "EmptyLabel", "No one to donate to", 14f, Muted,
                TextAlignmentOptions.Center);
            Stretch(empty, 0f);
            empty.gameObject.SetActive(false);

            // Amount: presets in a row, then a typed amount beside Max.
            TextMeshProUGUI amountCaption = Label(root.transform, "AmountCaption", "AMOUNT", 13f, Header, bold: true);
            TopBand(amountCaption, 246f, 18f, PanelPadding + 2f, PanelPadding);
            GameObject presets = Node(root.transform, "Presets");
            TopBand(presets.transform, 268f, 32f, PanelPadding, PanelPadding);
            Row(presets.transform, 0, 6f);
            var presetButtons = new Button[4];
            for (int i = 0; i < presetButtons.Length; i++)
                presetButtons[i] = Button(presets.transform, $"Preset{i}", "0", CardFill, 14f, out _);

            TMP_InputField amount = InputField(root.transform, "AmountInput", "Type an amount", 16f,
                TMP_InputField.ContentType.IntegerNumber);
            amount.characterLimit = 12;
            TopBand(amount, 306f, 34f, PanelPadding, PanelPadding + 76f);
            Button max = Button(root.transform, "MaxButton", "Max", CardFill, 14f, out _);
            Anchor(max, Vector2.one, Vector2.one, Vector2.one, new Vector2(-PanelPadding, -306f), new Vector2(70f, 34f));
            Hover(max, "Everything you have", gameHelp: true);

            Button send = Button(root.transform, "SendButton", "Send", Accent, 16f, out TextMeshProUGUI sendLabel);
            BottomBand(send, 30f, 38f, PanelPadding, PanelPadding);
            TextMeshProUGUI status = Label(root.transform, "Status", string.Empty, 13f, Warning, TextAlignmentOptions.Center);
            BottomBand(status, 8f, 18f, PanelPadding, PanelPadding);
            status.gameObject.SetActive(false);

            var panel = Ensure<FundsTransferPanel>(root);
            Wire(panel,
                ("titleLabel", title), ("balanceLabel", balance),
                ("recipientPrefab", Load<FundsRecipientRow>(RecipientRowPath)), ("recipientRoot", listRect),
                ("emptyLabel", empty), ("maxButton", max), ("amountInput", amount),
                ("sendButton", send), ("sendLabel", sendLabel), ("closeButton", close), ("statusLabel", status));
            WireArray(panel, "presetButtons", presetButtons);
        });
    }
}
