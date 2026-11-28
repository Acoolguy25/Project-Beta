using RyanAssets.Retention.Client;
using RyanAssets.Retention.Editor;
using RyanAssets.UI;
using RyanAssets.UI.Editor;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Universes.UniverseData.war_valley.Client;
using Universes.UniverseData.war_valley.Shared;
using static RyanAssets.Client.ClientUI.Command.Editor.UIAuthoringKit;

namespace Universes.UniverseData.war_valley.Editor.Client {
    /// <summary>
    /// Authors the campaign half of the commander HUD for <see cref="WV_AuthoringHUD"/>: the shared
    /// retention HUD (level chip, daily challenges, toasts, round summary) nested and placed around
    /// War Valley's own panels, the doctrine hand at the bottom centre, and the campaign card on the
    /// left edge.
    /// </summary>
    public static partial class WV_AuthoringCampaignHUD {
        /// <summary>The kit's level chip height (RetentionHudAuthoring), so the selection panel can stack above it.</summary>
        public const float LevelChipHeight = 44f;

        const float CardWidth = 236f;
        const float CardHeight = 172f;
        const float CardGap = 12f;
        const int MaxCards = 4;
        const float PanelPad = 14f;
        const float PanelTitleHeight = 28f;
        const float RerollHeight = 34f;
        const float RerollWidth = 180f;
        const float FoldWidth = 92f;
        const float PillWidth = 290f;
        const float PillHeight = 46f;
        const float RibbonWidth = 132f;
        const float CampaignWidth = 330f;
        const float CampaignHeight = 246f;
        const float CampaignOffsetY = 70f;
        const int DoctrineSlots = 8;
        const float DoctrineSlotSize = 32f;
        const float StarSize = 20f;
        const float PanelRadius = 14f;
        const float ArrowClearance = 24f;

        static Color Glass => new(UIKit.Theme.surface.r, UIKit.Theme.surface.g, UIKit.Theme.surface.b, 0.9f);
        static readonly Color CardFillColor = new(0.11f, 0.12f, 0.15f, 0.96f);
        static readonly Color CardAccent = new(0.95f, 0.72f, 0.32f, 1f);
        static readonly Color RerollFill = new(1f, 1f, 1f, 0.08f);
        static readonly Color StarOff = new(1f, 1f, 1f, 0.18f);

        public static float DoctrinePanelWidth => MaxCards * CardWidth + (MaxCards - 1) * CardGap + PanelPad * 2f;
        public static float DoctrinePanelHeight => PanelPad + PanelTitleHeight + 10f + CardHeight + 10f + RerollHeight + PanelPad;

        /// <summary>
        /// Builds every campaign piece under <paramref name="root"/>. The doctrine hand sits on top of
        /// <paramref name="doctrineBottom"/> (canvas units from the bottom edge, above the hint pill),
        /// and the level chip at <paramref name="levelChipBottom"/>, above the funds card.
        /// </summary>
        public static void Build(Transform root, WV_HUD hud, float margin, float doctrineBottom, float levelChipBottom) {
            // Nested first so its icon table can be shared; moved last so its toasts and summary
            // draw over War Valley's panels.
            RetentionHud retention = RetentionHudAuthoring.AddTo(root);
            RetentionIcons icons = retention.GetComponent<RetentionIcons>();
            PlaceRetention(retention, margin, levelChipBottom);

            WV_DoctrineOfferPanel panel = BuildDoctrinePanel(root, icons, doctrineBottom);
            WV_CampaignCard card = BuildCampaignCard(root, icons, margin);
            RetentionSummaryCard summary = retention.GetComponentInChildren<RetentionSummaryCard>(true);
            WV_CampaignReveal reveal = BuildCampaignReveal(root, icons, summary);
            retention.transform.SetAsLastSibling();
            // The tier choice draws over everything, the retention HUD included (it hides for the vote).
            WV_TierChoicePanel tierChoice = BuildTierChoice(root, icons);
            tierChoice.transform.SetAsLastSibling();

            // The wave arrows keep clear of the doctrine hand, which can be open while they show.
            if (hud.WaveDrama != null) {
                var drama = new SerializedObject(hud.WaveDrama);
                drama.FindProperty("bottomMargin").floatValue = doctrineBottom + DoctrinePanelHeight + ArrowClearance;
                drama.ApplyModifiedPropertiesWithoutUndo();
                Wire(hud.WaveDrama, ("icons", icons));
            }

            Wire(hud, ("doctrinePanel", panel), ("campaignCard", card), ("retentionHud", retention),
                ("retentionSummary", summary), ("tierChoice", tierChoice), ("campaignReveal", reveal));
        }

        /// <summary>
        /// The level chip sits above the funds card; the challenges panel opens on the left edge,
        /// where the campaign card steps aside for it (and for the summary card) while it is open.
        /// </summary>
        static void PlaceRetention(RetentionHud retention, float margin, float levelChipBottom) {
            retention.SetPlacement(RetentionHudPart.LevelChip,
                new RetentionPlacement(Vector2.zero, new Vector2(margin, levelChipBottom)));
            retention.SetPlacement(RetentionHudPart.ChallengesPanel,
                new RetentionPlacement(new Vector2(0f, 0.5f), new Vector2(margin, 0f)));
            EditorUtility.SetDirty(retention);
        }

        /// <summary>
        /// A plain node with a soft shadow and a glass body as its first two children, both stretched
        /// over it: the shadow hides, fades and resizes with the node (UIKit.Shadow alone adds it as a
        /// sibling, which stays on screen when the panel is hidden).
        /// </summary>
        public static RectTransform ShadowedPanel(Transform parent, string name, Color body, float radius, float blur = 24f,
            float offsetY = 6f, float opacity = 0.35f) {
            RectTransform node = Rect(Node(parent, name).transform);
            node.sizeDelta = new Vector2(100f, 100f);
            Image shade = UIKit.Shadow(node, blur, offsetY, opacity);
            shade.rectTransform.SetParent(node, false);
            shade.rectTransform.SetAsFirstSibling();
            if (shade.TryGetComponent(out UIShadowFollower follower))
                Object.DestroyImmediate(follower);
            shade.rectTransform.anchorMin = Vector2.zero;
            shade.rectTransform.anchorMax = Vector2.one;
            shade.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            shade.rectTransform.offsetMin = new Vector2(-blur, -blur - offsetY);
            shade.rectTransform.offsetMax = new Vector2(blur, blur - offsetY);
            shade.name = "Shadow";
            Image glass = Panel(node, "Body", body);
            glass.pixelsPerUnitMultiplier = UIThemeAssets.RoundedSpriteRadius / radius;
            Stretch(glass, 0f);
            return node;
        }

        static WV_DoctrineOfferPanel BuildDoctrinePanel(Transform root, RetentionIcons icons, float bottom) {
            // An always-active root holds the hand and its folded pill; each hides as a whole.
            RectTransform dock = Rect(Node(root, "DoctrinePanel").transform);
            Anchor(dock, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, bottom), new Vector2(DoctrinePanelWidth, DoctrinePanelHeight));

            RectTransform hand = ShadowedPanel(dock, "Hand", Glass, PanelRadius);
            Anchor(hand, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                Vector2.zero, new Vector2(DoctrinePanelWidth, DoctrinePanelHeight));
            var handGroup = hand.gameObject.AddComponent<CanvasGroup>();

            Image titleIcon = HudKit.Icon(hand, "military_tech", 24f, CardAccent);
            PlaceTopLeft(titleIcon, PanelPad, PanelPad + 2f, 24f, 24f);
            TextMeshProUGUI title = Label(hand, "Title", "Choose a doctrine", 19f, Ink, bold: true);
            PlaceTopLeft(title, PanelPad + 32f, PanelPad, DoctrinePanelWidth - PanelPad * 2f - 32f - FoldWidth - 8f, PanelTitleHeight);

            Button fold = Button(hand, "Fold", string.Empty, RerollFill, 14f, out TextMeshProUGUI foldLabel);
            Object.DestroyImmediate(foldLabel.gameObject);
            Anchor(fold, Vector2.one, Vector2.one, Vector2.one, new Vector2(-PanelPad, -PanelPad + 1f), new Vector2(FoldWidth, 28f));
            Image foldIcon = HudKit.Icon(fold.transform, "expand_more", 20f, Ink);
            Anchor(foldIcon, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(8f, 0f), new Vector2(20f, 20f));
            TextMeshProUGUI foldKey = Label(fold.transform, "Key", "Fold  N", 14f, Muted, TextAlignmentOptions.MidlineRight, bold: true);
            Stretch(foldKey, 28f, 10f, 0f, 0f);
            Hover(fold, "Fold the hand to a small button while you fight (N); pick later");

            var cards = new Object[MaxCards];
            float top = PanelPad + PanelTitleHeight + 10f;
            for (int i = 0; i < MaxCards; i++)
                cards[i] = BuildCard(hand, i, PanelPad + i * (CardWidth + CardGap), top);

            Button reroll = Button(hand, "Reroll", "Redraw", RerollFill, 15f, out TextMeshProUGUI rerollLabel);
            Anchor(reroll, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, PanelPad), new Vector2(RerollWidth, RerollHeight));
            Stretch(rerollLabel, 30f, 8f, 0f, 0f);
            Image rerollIcon = HudKit.Icon(reroll.transform, "refresh", 16f, Ink);
            Anchor(rerollIcon, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(10f, 0f), new Vector2(16f, 16f));
            Hover(reroll, "Redraw this hand  ·  once a round (level 4 unlock)");

            // The folded hand: one pill where the hand's bottom edge was.
            RectTransform pill = ShadowedPanel(dock, "Pill", Glass, PillHeight * 0.5f, 18f, 4f, 0.35f);
            Anchor(pill, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), Vector2.zero,
                new Vector2(PillWidth, PillHeight));
            var pillButton = pill.gameObject.AddComponent<Button>();
            pillButton.targetGraphic = pill.Find("Body").GetComponent<Image>();
            pillButton.navigation = new Navigation { mode = Navigation.Mode.None };
            Image pillAccent = Panel(pill, "Accent", CardAccent, rounded: false);
            pillAccent.raycastTarget = false;
            Anchor(pillAccent, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f), new Vector2(PillHeight * 0.5f - 2f, 0f),
                new Vector2(3f, -16f));
            Image pillIcon = HudKit.Icon(pill, "military_tech", 24f, CardAccent);
            Anchor(pillIcon, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(PillHeight * 0.5f + 8f, 0f),
                new Vector2(24f, 24f));
            TextMeshProUGUI pillLabel = Label(pill, "Label", "Doctrine ready  (N)", 17f, Ink, bold: true);
            Stretch(pillLabel, PillHeight * 0.5f + 40f, 18f, 0f, 0f);
            Image pillArrow = HudKit.Icon(pill, "keyboard_arrow_up", 20f, Muted);
            Anchor(pillArrow, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-16f, 0f), new Vector2(20f, 20f));
            Hover(pillButton, "Open the doctrine hand (N)");

            var offer = dock.gameObject.AddComponent<WV_DoctrineOfferPanel>();
            WireArray(offer, "cards", cards);
            Wire(offer, ("hand", hand), ("handGroup", handGroup), ("titleLabel", title), ("rerollButton", reroll),
                ("rerollLabel", rerollLabel), ("foldButton", fold), ("pill", pill), ("pillButton", pillButton),
                ("pillLabel", pillLabel), ("icons", icons));
            hand.gameObject.SetActive(false);
            pill.gameObject.SetActive(false);
            return offer;
        }

        static WV_DoctrineCard BuildCard(Transform parent, int index, float x, float y) {
            Button button = Button(parent, $"Card{index}", string.Empty, CardFillColor, 12f, out TextMeshProUGUI unused);
            Object.DestroyImmediate(unused.gameObject);
            var background = button.GetComponent<Image>();
            PlaceTopLeft(button, x, y, CardWidth, CardHeight);

            Image accent = Panel(button.transform, "Accent", CardAccent, rounded: false);
            accent.raycastTarget = false;
            TopBand(accent, 0f, 4f, 12f, 12f);

            Image icon = HudKit.Icon(button.transform, "star", 34f, CardAccent);
            icon.raycastTarget = false;
            PlaceTopLeft(icon, 12f, 16f, 34f, 34f);
            TextMeshProUGUI title = Label(button.transform, "Title", "Doctrine", 18f, Ink, bold: true);
            PlaceTopLeft(title, 54f, 12f, CardWidth - 64f, 24f);
            TextMeshProUGUI rank = Label(button.transform, "Rank", "New", 14f, Muted);
            PlaceTopLeft(rank, 54f, 36f, CardWidth - 64f, 18f);
            TextMeshProUGUI description = Label(button.transform, "Description", string.Empty, 15f, Ink, TextAlignmentOptions.TopLeft);
            description.textWrappingMode = TextWrappingModes.Normal;
            description.overflowMode = TextOverflowModes.Truncate;
            description.lineSpacing = -4f;
            PlaceTopLeft(description, 12f, 62f, CardWidth - 24f, CardHeight - 62f - 30f);
            TextMeshProUGUI family = Label(button.transform, "Family", "ECONOMY", 14f, CardAccent, bold: true);
            family.characterSpacing = 4f;
            Anchor(family, Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(12f, 8f), new Vector2(CardWidth - 24f, 18f));

            // The day's featured card wears a ribbon over its top edge.
            Image ribbon = Panel(button.transform, "Featured", Warning);
            ribbon.raycastTarget = false;
            Anchor(ribbon, Vector2.one, Vector2.one, new Vector2(1f, 0.5f), new Vector2(-10f, 0f), new Vector2(RibbonWidth, 22f));
            TextMeshProUGUI ribbonLabel = Label(ribbon.transform, "Label", "TODAY'S FRONT", 14f, new Color(0.1f, 0.08f, 0.04f, 1f),
                TextAlignmentOptions.Center, bold: true);
            Stretch(ribbonLabel, 4f, 4f, 0f, 0f);
            ribbon.gameObject.SetActive(false);

            var card = button.gameObject.AddComponent<WV_DoctrineCard>();
            Wire(card, ("button", button), ("background", background), ("accent", accent), ("icon", icon),
                ("titleLabel", title), ("rankLabel", rank), ("descriptionLabel", description), ("familyLabel", family),
                ("featuredRibbon", ribbon.gameObject));
            return card;
        }

        static WV_CampaignCard BuildCampaignCard(Transform root, RetentionIcons icons, float margin) {
            RectTransform panel = ShadowedPanel(root, "CampaignCard", Glass, PanelRadius, 20f, 4f, 0.3f);
            Anchor(panel, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(margin, CampaignOffsetY), new Vector2(CampaignWidth, CampaignHeight));
            panel.Find("Body").GetComponent<Image>().raycastTarget = false;
            var group = panel.gameObject.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            group.interactable = false;

            Image badge = HudKit.Icon(panel, "military_tech", 22f, CardAccent);
            PlaceTopLeft(badge, 14f, 14f, 22f, 22f);
            TextMeshProUGUI tier = Label(panel, "Tier", "RECRUIT", 19f, Ink, bold: true);
            PlaceTopLeft(tier, 42f, 10f, 170f, 28f);

            var stars = new Object[WV_Campaign.MaxStars];
            for (int i = 0; i < stars.Length; i++) {
                Image star = HudKit.Icon(panel, "star", StarSize, StarOff);
                PlaceTopLeft(star, CampaignWidth - 14f - (stars.Length - i) * (StarSize + 2f), 13f, StarSize, StarSize);
                stars[i] = star;
            }

            TextMeshProUGUI record = Label(panel, "Record", string.Empty, 14f, Muted);
            PlaceTopLeft(record, 14f, 42f, CampaignWidth - 28f, 20f);
            // The day's front: what makes today's war differ from yesterday's.
            Image frontIcon = HudKit.Icon(panel, "compass", 18f, CardAccent);
            PlaceTopLeft(frontIcon, 14f, 68f, 18f, 18f);
            TextMeshProUGUI frontText = Label(panel, "Front", string.Empty, 14f, Ink, TextAlignmentOptions.TopLeft);
            frontText.textWrappingMode = TextWrappingModes.Normal;
            frontText.lineSpacing = -4f;
            PlaceTopLeft(frontText, 38f, 66f, CampaignWidth - 52f, 58f);

            TextMeshProUGUI goal = Label(panel, "Goal", string.Empty, 15f, Warning, TextAlignmentOptions.TopLeft);
            goal.textWrappingMode = TextWrappingModes.Normal;
            goal.lineSpacing = -4f;
            PlaceTopLeft(goal, 14f, 128f, CampaignWidth - 28f, 40f);

            TextMeshProUGUI doctrinesTitle = Label(panel, "DoctrinesTitle", "DOCTRINES", 14f, Muted, bold: true);
            PlaceTopLeft(doctrinesTitle, 14f, 174f, 140f, 18f);
            var slots = new Object[DoctrineSlots];
            var ranks = new Object[DoctrineSlots];
            float slotStep = (CampaignWidth - 28f) / DoctrineSlots;
            for (int i = 0; i < DoctrineSlots; i++) {
                Image icon = HudKit.Icon(panel, "star", DoctrineSlotSize, CardAccent);
                PlaceTopLeft(icon, 14f + i * slotStep, 198f, DoctrineSlotSize, DoctrineSlotSize);
                TextMeshProUGUI rank = Label(icon.transform, "Rank", string.Empty, 14f, Ink, TextAlignmentOptions.BottomRight, bold: true);
                Anchor(rank, Vector2.one, Vector2.one, Vector2.one, new Vector2(4f, -16f), new Vector2(20f, 18f));
                icon.gameObject.SetActive(false);
                slots[i] = icon;
                ranks[i] = rank;
            }
            TextMeshProUGUI empty = Label(panel, "DoctrinesEmpty", "Hold the line: doctrines are dealt between waves",
                14f, Muted, TextAlignmentOptions.TopLeft);
            empty.textWrappingMode = TextWrappingModes.Normal;
            empty.lineSpacing = -4f;
            PlaceTopLeft(empty, 14f, 198f, CampaignWidth - 28f, 40f);

            var card = panel.gameObject.AddComponent<WV_CampaignCard>();
            WireArray(card, "stars", stars);
            WireArray(card, "doctrineIcons", slots);
            WireArray(card, "doctrineRanks", ranks);
            Wire(card, ("tierLabel", tier), ("recordLabel", record), ("goalLabel", goal), ("frontLabel", frontText), ("frontIcon", frontIcon),
                ("doctrinesEmptyLabel", empty), ("icons", icons), ("group", group));
            var colors = new SerializedObject(card);
            colors.FindProperty("starOff").colorValue = StarOff;
            colors.ApplyModifiedPropertiesWithoutUndo();
            panel.gameObject.SetActive(false);
            return card;
        }

        static void PlaceTopLeft(Component target, float x, float y, float width, float height) =>
            Anchor(target, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(x, -y),
                new Vector2(width, height));
    }
}
