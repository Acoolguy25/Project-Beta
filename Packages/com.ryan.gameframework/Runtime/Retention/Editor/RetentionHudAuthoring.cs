using System.IO;
using System.Linq;
using RyanAssets.Retention.Client;
using RyanAssets.UI;
using RyanAssets.UI.Editor;
using RyanAssets.UI.GameHud;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using static RyanAssets.UI.Editor.UIKit;

// Client-only: this assembly has the !UNITY_SERVER define constraint, because RyanAssets.UI.Editor
// (and the client HUD it builds) do not exist in the server Editor.
namespace RyanAssets.Retention.Editor {
    /// <summary>
    /// Generates <c>Generated/RetentionHUD.prefab</c> (level chip, challenges panel, toasts, summary
    /// card) from the shared <see cref="UITheme"/> through <see cref="UIKit"/> and <see cref="HudKit"/>.
    /// Edit this script, not the prefab, then run Ryan &gt; Retention &gt; Rebuild HUD (batch:
    /// <c>-executeMethod RyanAssets.Retention.Editor.RetentionHudAuthoring.BuildBatch</c>).
    /// Games put the prefab in their own HUD with <see cref="AddTo"/> and move parts with
    /// <see cref="RetentionHud.SetPlacement"/>.
    /// </summary>
    public static class RetentionHudAuthoring {
        public const string Root = "Packages/com.ryan.gameframework/Runtime/Retention";
        public const string PrefabPath = Root + "/Generated/RetentionHUD.prefab";
        /// <summary>Above game HUDs (about 20), below the shared top bar (50) and menus.</summary>
        public const int SortingOrder = 30;

        const float ChipHeight = 44f;
        const float PanelWidth = 420f;
        const float SummaryWidth = 480f;
        const float ToastWidth = 440f;
        const float ToastRowHeight = 40f;
        const float ToastRadius = 10f;
        /// <summary>
        /// Room the shared HUD's player list (top right, under the top bar) takes with four players:
        /// padding 6+6, header 30, gap 4, four 34-px rows 2 apart, plus a 12-px gap below it.
        /// </summary>
        const float PlayerListClearance = 6f + 30f + 4f + 4f * 34f + 3f * 2f + 6f + 12f;

        // Theme icon names (Material / game-icons.net sets, Tools/UIIcons/icons.json).
        const string LevelIcon = "military_tech";
        const string ChallengeIcon = "emoji_events";
        const string OpenChallengeIcon = "sports_score";
        const string DoneIcon = "check_circle";
        const string StreakIcon = "local_fire_department";
        const string GoldIcon = "paid";
        const string TimerIcon = "timer";
        const string UnlockIcon = "star";
        const string InfoIcon = "trending_up";
        const string WarningIcon = "warning";
        const string CloseIcon = "close";

        [MenuItem("Ryan/Retention/Rebuild HUD")]
        public static void BuildMenu() {
            GameObject prefab = Build();
            EditorGUIUtility.PingObject(prefab);
        }

        /// <summary>Batch entry point (-executeMethod); throws on failure so the batch run fails loudly.</summary>
        public static void BuildBatch() {
            Build();
            Debug.Log($"[Retention] Built {PrefabPath}.");
        }

        /// <summary>Builds the prefab over the old one (its GUID, and so every game's nested instance, is kept).</summary>
        public static GameObject Build() {
            if (Application.isPlaying) throw new System.InvalidOperationException("Stop play mode before rebuilding the retention HUD.");
            UIThemeAssets.Ensure();
            RetentionHudStyle style = StyleFromTheme(Theme);

            RectTransform root = Canvas("RetentionHUD", SortingOrder);
            // Fills its parent when a game nests it inside its own HUD canvas.
            Stretch(root);
            root.gameObject.AddComponent<CanvasGroup>();
            var hud = root.gameObject.AddComponent<RetentionHud>();
            var icons = root.gameObject.AddComponent<RetentionIcons>();
            FillIcons(icons);

            RetentionChallengesPanel panel = ChallengesPanel(root, style);
            RetentionLevelChip chip = LevelChip(root, style);
            HudToastFeed toasts = HudKit.ToastFeed(root, "Toasts", new Vector2(1f, 1f), Vector2.zero, ToastWidth, ToastRowHeight);
            WrapToasts(toasts);
            RetentionSummaryCard summary = SummaryCard(root, style);

            SerializedFields.Set(hud, "levelChip", chip);
            SerializedFields.Set(hud, "challengesPanel", panel);
            SerializedFields.Set(hud, "toasts", toasts);
            SerializedFields.Set(hud, "summaryCard", summary);
            SerializedFields.Set(hud, "icons", icons);
            SerializedFields.Set(hud, "levelUpIcon", HudKit.Icon(LevelIcon));
            SerializedFields.Set(hud, "challengeIcon", HudKit.Icon(ChallengeIcon));
            SerializedFields.Set(hud, "streakIcon", HudKit.Icon(StreakIcon));
            SerializedFields.Set(hud, "warningIcon", HudKit.Icon(WarningIcon));
            WriteStyle(hud, style);
            // The placement fields' initialisers are the default layout. The toasts sit top right, under
            // the theme's top bar (margin, bar, 8 px gap) and below the shared player list there, which
            // any game may turn on, with room for four players.
            hud.SetPlacement(RetentionHudPart.Toasts, new RetentionPlacement(new Vector2(1f, 1f),
                new Vector2(-Theme.screenMargin, -(Theme.screenMargin + Theme.topbarHeight + 8f + PlayerListClearance))));
            hud.ApplyPlacement();

            // A root Screen Space canvas has its scale driven by the CanvasScaler, which is 0 with no
            // screen (batch mode). Saved that way, a copy nested under a game's canvas (AddTo) is a
            // sub-canvas that keeps the 0 and draws nothing, so save the root at scale 1.
            root.localScale = Vector3.one;
            GameObject prefab = SavePrefab(root.gameObject, PrefabPath);
            if (prefab.transform.localScale != Vector3.one) {
                GameObject contents = PrefabUtility.LoadPrefabContents(PrefabPath);
                try {
                    contents.transform.localScale = Vector3.one;
                    PrefabUtility.SaveAsPrefabAsset(contents, PrefabPath);
                } finally {
                    PrefabUtility.UnloadPrefabContents(contents);
                }
                prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            }
            return prefab;
        }

        /// <summary>
        /// Nests the retention HUD prefab under <paramref name="parent"/> (a game's HUD canvas or root),
        /// building it first if it does not exist yet. Call from a game's HUD authoring, then use
        /// <see cref="RetentionHud.SetPlacement"/> on the result to move parts for that game.
        /// </summary>
        public static RetentionHud AddTo(Transform parent) {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null) prefab = Build();
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            Stretch((RectTransform)instance.transform);
            // Nested, the root is a sub-canvas: its scale is no longer driven, so it must be 1.
            instance.transform.localScale = Vector3.one;
            return instance.GetComponent<RetentionHud>();
        }

        // ---------------------------------------------------------------- level chip

        static RetentionLevelChip LevelChip(RectTransform root, RetentionHudStyle style) {
            RectTransform rect = Pinned(root, "LevelChip", Vector2.zero, Vector2.zero, new Vector2(300f, ChipHeight));
            HudKit.Glass(rect, ChipHeight * 0.5f, raycast: true);
            Row(rect.gameObject, 10f, new RectOffset(5, 10, 0, 0), TextAnchor.MiddleLeft);
            FitContent(rect.gameObject, true, false);
            var chip = rect.gameObject.AddComponent<RetentionLevelChip>();
            chip.punchTarget = rect;

            RectTransform disc = Node(rect, "Level");
            HudKit.Fixed(disc, ChipHeight - 10f, ChipHeight - 10f);
            chip.levelDisc = Tinted(Rounded(disc, UIColorRole.Accent, (ChipHeight - 10f) * 0.5f, raycast: false), style.xp);
            chip.levelLabel = Text(disc, "Value", "1", UIText.Strong, UIColorRole.OnAccent, TextAlignmentOptions.Center);
            Stretch(chip.levelLabel.rectTransform);

            RectTransform progress = Node(rect, "Progress");
            HudKit.Fixed(progress, 92f, 28f);
            Column(progress.gameObject, 3f, null, TextAnchor.MiddleLeft);
            Text(progress, "Caption", "LEVEL", UIText.Caption, UIColorRole.TextMuted);
            chip.bar = HudKit.Bar(progress, "Bar", 6f, style.xp);

            RectTransform streak = Node(rect, "Streak");
            Row(streak.gameObject, 3f, null, TextAnchor.MiddleLeft);
            chip.streakGroup = streak.gameObject;
            chip.streakIcon = HudKit.Icon(streak, StreakIcon, 20f, style.streak);
            chip.streakLabel = Text(streak, "Days", "0", UIText.Strong);
            streak.gameObject.SetActive(false);

            RectTransform challenges = Node(rect, "Challenges");
            Row(challenges.gameObject, 3f, null, TextAnchor.MiddleLeft);
            chip.challengesIcon = HudKit.Icon(challenges, ChallengeIcon, 20f, style.challenge);
            chip.challengesLabel = Text(challenges, "Count", "0/3", UIText.Strong);

            RectTransform cap = HudKit.KeyCap(rect, "J");
            chip.keyCap = cap.gameObject;
            chip.keyLabel = cap.GetComponentInChildren<TMP_Text>();

            chip.button = Clickable(rect, ChipHeight * 0.5f);

            // "+XP" floats to the right of the chip, outside its layout.
            RectTransform gain = Node(rect, "Gain");
            gain.anchorMin = gain.anchorMax = new Vector2(1f, 0.5f);
            gain.pivot = new Vector2(0f, 0.5f);
            gain.anchoredPosition = new Vector2(10f, 0f);
            gain.sizeDelta = new Vector2(160f, ChipHeight);
            gain.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            chip.gainLabel = Text(gain, "Value", "+0 XP", UIText.Title, UIColorRole.TextPrimary);
            Stretch(chip.gainLabel.rectTransform);
            Untheme(chip.gainLabel);
            chip.gainLabel.fontSharedMaterial = UIThemeAssets.ShadowMaterial(chip.gainLabel.font);
            chip.gainPopup = HudKit.Popup(gain, false);
            return chip;
        }

        // ---------------------------------------------------------------- challenges panel

        static RetentionChallengesPanel ChallengesPanel(RectTransform root, RetentionHudStyle style) {
            RectTransform card = HudKit.Card(root, "ChallengesPanel", Vector2.zero, Vector2.zero, new Vector2(PanelWidth, 420f));
            Column(card.gameObject, 10f, new RectOffset(20, 20, 18, 20), TextAnchor.UpperLeft);
            FitContent(card.gameObject, false, true);
            HudKit.AccentStripe(card, style.challenge);
            var panel = card.gameObject.AddComponent<RetentionChallengesPanel>();

            RectTransform header = Node(card, "Header");
            Row(header.gameObject, 8f, null, TextAnchor.MiddleLeft);
            HudKit.Icon(header, ChallengeIcon, 24f, style.challenge);
            TextMeshProUGUI title = Text(header, "Title", "Daily challenges", UIText.Title);
            Size(title, -1f, -1f, 1f);
            RectTransform cap = HudKit.KeyCap(header, "J");
            panel.keyLabel = cap.GetComponentInChildren<TMP_Text>();
            panel.closeButton = IconButton(header, "Close", CloseIcon, 28f, UIColorRole.SurfaceInset, 8f, out _, out _);

            RectTransform level = Node(card, "Level");
            Row(level.gameObject, 12f, null, TextAnchor.MiddleLeft);
            HudKit.Emblem(level, "Emblem", LevelIcon, 44f, style.xp, out panel.levelDisc, out _);
            RectTransform levelInfo = Node(level, "Info");
            Column(levelInfo.gameObject, 6f, null, TextAnchor.MiddleLeft);
            Size(levelInfo, -1f, -1f, 1f);
            RectTransform levelLine = Node(levelInfo, "Line");
            Row(levelLine.gameObject, 8f, null, TextAnchor.MiddleLeft);
            panel.levelLabel = Text(levelLine, "Level", "Level 1", UIText.Strong);
            Size(panel.levelLabel, -1f, -1f, 1f);
            panel.levelXpLabel = Text(levelLine, "Xp", "0 / 100 XP", UIText.Small, UIColorRole.TextSecondary, TextAlignmentOptions.MidlineRight);
            panel.levelBar = HudKit.Bar(levelInfo, "Bar", 8f, style.xp);

            Divider(card);

            RectTransform today = Node(card, "Today");
            Row(today.gameObject, 6f, null, TextAnchor.MiddleLeft);
            TextMeshProUGUI caption = Text(today, "Caption", "Today", UIText.Caption, UIColorRole.TextMuted);
            Size(caption, -1f, -1f, 1f);
            Icon(today, TimerIcon, 16f, UIColorRole.TextMuted);
            panel.resetLabel = Text(today, "Reset", "New in 0h 00m", UIText.Small, UIColorRole.TextMuted, TextAlignmentOptions.MidlineRight);

            panel.rowTemplate = ChallengeRow(card, style);

            Divider(card);

            RectTransform streak = Node(card, "Streak");
            Row(streak.gameObject, 8f, null, TextAnchor.MiddleLeft);
            panel.streakIcon = HudKit.Icon(streak, StreakIcon, 20f, style.streak);
            panel.streakLabel = Wrapping(Text(streak, "Text", "Play today to start a streak", UIText.Small, UIColorRole.TextSecondary));
            Size(panel.streakLabel, -1f, -1f, 1f);

            RectTransform next = Node(card, "NextUnlock");
            Row(next.gameObject, 12f, null, TextAnchor.MiddleLeft);
            panel.nextUnlockGroup = next.gameObject;
            HudKit.Emblem(next, "Emblem", UnlockIcon, 44f, style.unlock, out panel.nextUnlockDisc, out panel.nextUnlockIcon);
            RectTransform nextInfo = Node(next, "Info");
            Column(nextInfo.gameObject, 2f, null, TextAnchor.MiddleLeft);
            Size(nextInfo, -1f, -1f, 1f);
            Text(nextInfo, "Caption", "Next unlock", UIText.Caption, UIColorRole.TextMuted);
            panel.nextUnlockTitle = Text(nextInfo, "Title", "Level 2: Something new", UIText.Strong);
            panel.nextUnlockDescription = Wrapping(Text(nextInfo, "Description", string.Empty, UIText.Small, UIColorRole.TextSecondary));

            RectTransform notSaved = Node(card, "NotSaved");
            Row(notSaved.gameObject, 8f, null, TextAnchor.MiddleLeft);
            HudKit.Icon(notSaved, WarningIcon, 18f, style.warning);
            TextMeshProUGUI warning = Wrapping(Text(notSaved, "Text", "Progress is not being saved this session.", UIText.Small));
            Untheme(warning).color = style.warning;
            Size(warning, -1f, -1f, 1f);
            panel.notSavedGroup = notSaved.gameObject;
            notSaved.gameObject.SetActive(false);
            return panel;
        }

        static RetentionChallengeRow ChallengeRow(RectTransform card, RetentionHudStyle style) {
            RectTransform rect = Node(card, "ChallengeTemplate");
            Rounded(rect, UIColorRole.SurfaceInset, 10f, raycast: false);
            Column(rect.gameObject, 6f, new RectOffset(12, 12, 10, 10), TextAnchor.UpperLeft);
            var row = rect.gameObject.AddComponent<RetentionChallengeRow>();
            row.body = rect.gameObject.AddComponent<CanvasGroup>();
            row.body.interactable = row.body.blocksRaycasts = false;

            RectTransform top = Node(rect, "Top");
            Row(top.gameObject, 8f, null, TextAnchor.MiddleLeft);
            row.openIcon = HudKit.Icon(OpenChallengeIcon);
            row.doneIcon = HudKit.Icon(DoneIcon);
            row.icon = HudKit.Icon(top, OpenChallengeIcon, 20f, style.muted);
            row.title = Wrapping(Text(top, "Text", "Challenge", UIText.Strong));
            // The title takes only the width the rewards leave (and wraps there); without this a long
            // title's preferred width squeezes the reward cells to "+...".
            Size(row.title, -1f, -1f, 1f).preferredWidth = 0f;
            row.xpReward = Text(top, "Xp", "+100 XP", UIText.Small);
            Untheme(row.xpReward).color = style.xp;
            RectTransform gold = Node(top, "Gold");
            Row(gold.gameObject, 2f, null, TextAnchor.MiddleLeft);
            row.goldGroup = gold.gameObject;
            HudKit.Icon(gold, GoldIcon, 16f, style.gold);
            row.goldReward = Text(gold, "Amount", "+10", UIText.Small);
            Untheme(row.goldReward).color = style.gold;

            RectTransform bottom = Node(rect, "Bottom");
            Row(bottom.gameObject, 10f, null, TextAnchor.MiddleLeft);
            row.bar = HudKit.Bar(bottom, "Bar", 6f, style.challenge);
            row.count = Text(bottom, "Count", "0/1", UIText.Small, UIColorRole.TextSecondary, TextAlignmentOptions.MidlineRight);
            Untheme(row.count);
            HudKit.Fixed(row.count.rectTransform, 64f, 18f);

            rect.gameObject.SetActive(false);
            return row;
        }

        // ---------------------------------------------------------------- summary card

        static RetentionSummaryCard SummaryCard(RectTransform root, RetentionHudStyle style) {
            RectTransform card = HudKit.Card(root, "SummaryCard", new Vector2(0f, 0.5f), Vector2.zero, new Vector2(SummaryWidth, 480f));
            Column(card.gameObject, 12f, new RectOffset(24, 24, 22, 22), TextAnchor.UpperCenter);
            FitContent(card.gameObject, false, true);
            HudKit.AccentStripe(card, style.xp);
            var summary = card.gameObject.AddComponent<RetentionSummaryCard>();

            summary.title = Text(card, "Title", "Match complete", UIText.Title, UIColorRole.TextPrimary, TextAlignmentOptions.Center);

            RectTransform level = Node(card, "Level");
            Row(level.gameObject, 16f, null, TextAnchor.MiddleLeft);
            const float emblemSize = 72f;
            RectTransform emblem = Node(level, "Emblem");
            HudKit.Fixed(emblem, emblemSize, emblemSize);
            summary.levelEmblem = emblem;
            summary.levelDisc = Tinted(Rounded(emblem, UIColorRole.Accent, emblemSize * 0.5f, raycast: false), style.xp);
            summary.levelLabel = Text(emblem, "Value", "1", UIText.Display, UIColorRole.OnAccent, TextAlignmentOptions.Center);
            Stretch(summary.levelLabel.rectTransform);

            RectTransform info = Node(level, "Info");
            Column(info.gameObject, 6f, null, TextAnchor.MiddleLeft);
            Size(info, -1f, -1f, 1f);
            summary.totalLabel = Text(info, "Total", "+0 XP", UIText.Title);
            Untheme(summary.totalLabel).color = Color.Lerp(style.xp, Color.white, 0.25f);
            summary.bar = HudKit.Bar(info, "Bar", 10f, style.xp);
            // The card drives the fill itself, frame by frame.
            summary.bar.smoothing = 0f;
            RectTransform under = Node(info, "Under");
            Row(under.gameObject, 6f, null, TextAnchor.MiddleLeft);
            summary.barLabel = Text(under, "Xp", "0 / 100 XP", UIText.Small, UIColorRole.TextSecondary);
            Size(summary.barLabel, -1f, -1f, 1f);
            RectTransform gold = Node(under, "Gold");
            Row(gold.gameObject, 2f, null, TextAnchor.MiddleLeft);
            summary.totalGoldGroup = gold.gameObject;
            HudKit.Icon(gold, GoldIcon, 16f, style.gold);
            summary.totalGoldLabel = Text(gold, "Amount", "+0", UIText.Strong);
            Untheme(summary.totalGoldLabel).color = style.gold;

            summary.lines = HudKit.Table(card, "Lines", new[] {
                new HudKit.Column("Reward"),
                new HudKit.Column("Gold", 56f, TextAlignmentOptions.MidlineRight),
                new HudKit.Column("XP", 92f, TextAlignmentOptions.MidlineRight),
            }, 30f);
            summary.lines.template.icon.sprite = HudKit.Icon(UnlockIcon);
            summary.lines.template.icon.color = style.levelUp;
            summary.rewardIcon = HudKit.Icon(UnlockIcon);
            summary.infoIcon = HudKit.Icon(InfoIcon);
            // Lines are revealed by fading, so the card keeps its size while it animates.
            summary.lines.template.gameObject.AddComponent<CanvasGroup>();

            RectTransform unlock = Node(card, "Unlock");
            Rounded(unlock, UIColorRole.SurfaceInset, 12f, raycast: false);
            Row(unlock.gameObject, 14f, new RectOffset(14, 14, 12, 12), TextAnchor.MiddleLeft);
            // A faint outline holds the unlock's place until it is revealed (it ignores the block's fade).
            RectTransform placeholder = Fill(unlock, "Placeholder");
            placeholder.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            Rounded(placeholder, UIColorRole.TextPrimary, 12f, 0.05f, raycast: false);
            placeholder.gameObject.AddComponent<CanvasGroup>().ignoreParentGroups = true;
            summary.unlockPlaceholder = placeholder.gameObject;
            HudKit.Emblem(unlock, "Emblem", UnlockIcon, 64f, style.unlock, out summary.unlockDisc, out summary.unlockIcon);
            RectTransform unlockInfo = Node(unlock, "Info");
            Column(unlockInfo.gameObject, 2f, null, TextAnchor.MiddleLeft);
            Size(unlockInfo, -1f, -1f, 1f);
            summary.unlockKicker = Text(unlockInfo, "Kicker", "UNLOCKED", UIText.Caption);
            Untheme(summary.unlockKicker).color = style.unlock;
            summary.unlockTitle = Text(unlockInfo, "Title", "Something new", UIText.Title);
            summary.unlockDescription = Wrapping(Text(unlockInfo, "Description", string.Empty, UIText.Small, UIColorRole.TextSecondary));
            summary.unlockPopup = HudKit.Popup(unlock, false);
            // Kept in the layout while hidden, so revealing it does not resize the card.
            summary.unlockPopup.deactivateWhenHidden = false;

            RectTransform streak = Node(card, "Streak");
            Row(streak.gameObject, 8f, null, TextAnchor.MiddleLeft);
            summary.streakGroup = streak.gameObject;
            summary.streakIcon = HudKit.Icon(streak, StreakIcon, 18f, style.streak);
            summary.streakLabel = Text(streak, "Text", "Come back tomorrow for day 2 of your streak!", UIText.Small, UIColorRole.TextSecondary);
            Size(summary.streakLabel, -1f, -1f, 1f);

            RectTransform next = Node(card, "NextUnlock");
            Row(next.gameObject, 8f, null, TextAnchor.MiddleLeft);
            summary.nextUnlockGroup = next.gameObject;
            summary.nextUnlockIcon = HudKit.Icon(next, UnlockIcon, 18f, style.unlock);
            summary.nextUnlockLabel = Text(next, "Text", "Next: Level 2", UIText.Small, UIColorRole.TextSecondary);
            Size(summary.nextUnlockLabel, -1f, -1f, 1f);

            summary.closeButton = TextButton(card, "Continue", "Continue", DoneIcon, UIColorRole.Accent, UIColorRole.OnAccent, 40f);
            return summary;
        }

        // ---------------------------------------------------------------- helpers

        /// <summary>Every icon of the shared theme (both sets), so any unlock icon name resolves at runtime.</summary>
        static void FillIcons(RetentionIcons icons) {
            string[] names = new[] { UIThemeAssets.IconFolder, UIThemeAssets.GameIconFolder }
                .Where(Directory.Exists)
                .SelectMany(folder => Directory.GetFiles(folder, "icon_*.png"))
                .Select(path => Path.GetFileNameWithoutExtension(path).Substring("icon_".Length))
                .Distinct()
                .OrderBy(name => name, System.StringComparer.Ordinal)
                .ToArray();
            icons.Set(names, names.Select(name => HudKit.Icon(name)).ToArray(), HudKit.Icon(UnlockIcon));
            EditorUtility.SetDirty(icons);
        }

        static RetentionHudStyle StyleFromTheme(UITheme theme) => new() {
            xp = theme.accent,
            challenge = theme.success,
            levelUp = theme.currency,
            streak = theme.warning,
            gold = theme.currency,
            warning = theme.danger,
            muted = theme.textMuted,
            // The theme has no unlock colour; keep the style's own (violet).
        };

        static void WriteStyle(RetentionHud hud, RetentionHudStyle style) {
            var serialized = new SerializedObject(hud);
            void Set(string field, Color color) {
                SerializedProperty property = serialized.FindProperty("style." + field)
                    ?? throw new System.ArgumentException($"RetentionHudStyle has no field '{field}'.");
                property.colorValue = color;
            }
            Set(nameof(RetentionHudStyle.xp), style.xp);
            Set(nameof(RetentionHudStyle.challenge), style.challenge);
            Set(nameof(RetentionHudStyle.levelUp), style.levelUp);
            Set(nameof(RetentionHudStyle.unlock), style.unlock);
            Set(nameof(RetentionHudStyle.streak), style.streak);
            Set(nameof(RetentionHudStyle.gold), style.gold);
            Set(nameof(RetentionHudStyle.warning), style.warning);
            Set(nameof(RetentionHudStyle.muted), style.muted);
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// Challenge names can be long: a toast wider than the feed wraps onto more lines and grows
        /// taller (at least one row high) instead of running off the right edge of the screen.
        /// </summary>
        static void WrapToasts(HudToastFeed toasts) {
            HudToast template = toasts.template;
            template.label.textWrappingMode = TextWrappingModes.Normal;
            template.label.overflowMode = TextOverflowModes.Overflow;
            if (template.TryGetComponent(out LayoutElement size)) {
                size.minHeight = ToastRowHeight;
                size.preferredHeight = size.maxHeight = -1f;
            }
            if (template.TryGetComponent(out HorizontalLayoutGroup row)) {
                row.padding = new RectOffset(row.padding.left, row.padding.right, 8, 8);
                row.childControlHeight = true;
            }
            CardBehind(template);
        }

        /// <summary>
        /// The shared toast row is translucent glass, which reads see-through over bright scenes (sky,
        /// snow). Kit toasts get an opaque themed card instead: a soft shadow, a hairline edge and a
        /// raised-surface body, drawn behind the accent bar, icon and text (and fading with the toast).
        /// </summary>
        static void CardBehind(HudToast template) {
            Transform row = template.transform;
            if (row.TryGetComponent(out Image glass)) {
                if (glass.TryGetComponent(out UIThemeColor painter)) Object.DestroyImmediate(painter);
                Object.DestroyImmediate(glass);
            }
            RectTransform shade = Fill(row, "Shadow", -12f, -8f, -12f, -16f);
            shade.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            var shadow = shade.gameObject.AddComponent<Image>();
            shadow.sprite = Theme.shadowSprite;
            shadow.type = Image.Type.Sliced;
            shadow.raycastTarget = false;
            shadow.color = new Color(0f, 0f, 0f, 0.4f);
            RectTransform edge = Fill(row, "Edge");
            edge.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            Rounded(edge, UIColorRole.Stroke, ToastRadius, raycast: false);
            RectTransform body = Fill(row, "Body", 1f, 1f, 1f, 1f);
            body.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            Rounded(body, UIColorRole.SurfaceRaised, ToastRadius - 1f, raycast: false);
            shade.SetSiblingIndex(0);
            edge.SetSiblingIndex(1);
            body.SetSiblingIndex(2);
        }

        /// <summary>Removes the theme painter so a script- or style-set colour sticks.</summary>
        static T Untheme<T>(T graphic) where T : Graphic {
            if (graphic.TryGetComponent(out UIThemeColor painter)) Object.DestroyImmediate(painter);
            return graphic;
        }

        static Image Tinted(Image image, Color color) {
            Untheme(image).color = color;
            return image;
        }

        static TextMeshProUGUI Wrapping(TextMeshProUGUI text) {
            text.textWrappingMode = TextWrappingModes.Normal;
            text.overflowMode = TextOverflowModes.Overflow;
            return text;
        }
    }
}
