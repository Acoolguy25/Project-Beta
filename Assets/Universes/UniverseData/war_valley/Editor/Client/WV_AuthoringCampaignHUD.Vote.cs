using RyanAssets.Retention.Client;
using RyanAssets.UI;
using RyanAssets.UI.Editor;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Universes.UniverseData.war_valley.Client;
using Universes.UniverseData.war_valley.Shared;
using static RyanAssets.Client.ClientUI.Command.Editor.UIAuthoringKit;

namespace Universes.UniverseData.war_valley.Editor.Client {
    /// <summary>
    /// The campaign's two full moments: the tier choice that opens every round (War Valley's own panel
    /// over the shared vote) and the stars and tier-unlock tiles beside the round summary.
    /// </summary>
    public static partial class WV_AuthoringCampaignHUD {
        const int TierCards = WV_Campaign.MaxVoteOptions;
        const float TierCardWidth = 220f;
        const float TierCardHeight = 300f;
        const float TierGap = 16f;
        const float TierPad = 24f;
        const float TierTop = 100f;
        const float TierFooter = 44f;
        const float TierStar = 30f;
        const float RevealWidth = 380f;
        const float RevealStarsHeight = 112f;
        const float RevealUnlockHeight = 104f;
        const int RevealUnlocks = 2;

        static readonly Color TierCardFill = new(0.10f, 0.11f, 0.14f, 0.97f);
        static readonly Color UnlockViolet = new(0.66f, 0.47f, 1f, 1f);
        static readonly Color DarkInk = new(0.1f, 0.08f, 0.04f, 1f);

        static float TierWindowWidth => TierPad * 2f + TierCards * TierCardWidth + (TierCards - 1) * TierGap;
        static float TierWindowHeight => TierTop + TierCardHeight + 16f + TierFooter;

        static WV_TierChoicePanel BuildTierChoice(Transform root, RetentionIcons icons) {
            RectTransform layer = Rect(Node(root, "TierChoice").transform);
            Stretch(layer, 0f);
            var group = layer.gameObject.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            group.blocksRaycasts = false;
            group.interactable = false;

            RectTransform window = Rect(Node(layer, "Window").transform);
            Anchor(window, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 30f),
                new Vector2(TierWindowWidth, TierWindowHeight));
            // A soft dim behind the window draws the eye; it never takes a click.
            Image scrim = Panel(window, "Scrim", new Color(0f, 0f, 0f, 0.35f), rounded: false);
            scrim.raycastTarget = false;
            Stretch(scrim, -4000f);
            RectTransform body = ShadowedPanel(window, "Panel", Glass, 18f, 30f, 8f, 0.45f);
            Stretch(body, 0f);
            body.SetSiblingIndex(1);

            Image badge = HudKit.Icon(window, "how_to_vote", 34f, CardAccent);
            PlaceTopLeft(badge, TierPad, 24f, 34f, 34f);
            TextMeshProUGUI title = Label(window, "Title", "CHOOSE THE WAR", 28f, Ink, bold: true);
            title.characterSpacing = 3f;
            PlaceTopLeft(title, TierPad + 46f, 18f, 640f, 36f);
            TextMeshProUGUI subtitle = Label(window, "Subtitle", string.Empty, 16f, Muted);
            PlaceTopLeft(subtitle, TierPad + 46f, 56f, 760f, 24f);

            Image timerPill = Panel(window, "Timer", new Color(1f, 1f, 1f, 0.08f));
            timerPill.raycastTarget = false;
            Anchor(timerPill, Vector2.one, Vector2.one, Vector2.one, new Vector2(-TierPad, -24f), new Vector2(112f, 42f));
            Image timerIcon = HudKit.Icon(timerPill.transform, "timer", 22f, Muted);
            Anchor(timerIcon, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(12f, 0f), new Vector2(22f, 22f));
            TextMeshProUGUI timer = Label(timerPill.transform, "Seconds", "15s", 22f, Ink, TextAlignmentOptions.MidlineRight, bold: true);
            Stretch(timer, 40f, 14f, 0f, 0f);

            var cards = new Object[TierCards];
            var emblems = new Object[TierCards];
            for (int i = 0; i < TierCards; i++) {
                cards[i] = BuildTierCard(window, i, TierPad + i * (TierCardWidth + TierGap), TierTop, out Image emblem);
                emblems[i] = emblem;
            }

            TextMeshProUGUI footer = Label(window, "Footer", string.Empty, 15f, Muted, TextAlignmentOptions.Center);
            Anchor(footer, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 14f), new Vector2(-TierPad * 2f, 22f));

            var panel = layer.gameObject.AddComponent<WV_TierChoicePanel>();
            WireArray(panel, "cards", cards);
            WireArray(panel, "emblems", emblems);
            Wire(panel, ("window", window), ("group", group), ("titleLabel", title), ("subtitleLabel", subtitle),
                ("timerLabel", timer), ("footerLabel", footer), ("icons", icons));
            window.gameObject.SetActive(false);
            return panel;
        }

        static WV_TierCard BuildTierCard(Transform parent, int index, float x, float y, out Image emblem) {
            Button button = Button(parent, $"Tier{index}", string.Empty, TierCardFill, 12f, out TextMeshProUGUI unused);
            Object.DestroyImmediate(unused.gameObject);
            var background = button.GetComponent<Image>();
            PlaceTopLeft(button, x, y, TierCardWidth, TierCardHeight);
            var group = button.gameObject.AddComponent<CanvasGroup>();
            Border(background, CardAccent, 3f);
            Outline outline = background.GetComponent<Outline>();
            outline.enabled = false;

            Image accent = Panel(button.transform, "Accent", CardAccent, rounded: false);
            accent.raycastTarget = false;
            TopBand(accent, 0f, 5f, 14f, 14f);

            emblem = HudKit.Icon(button.transform, "shield", 46f, CardAccent);
            emblem.raycastTarget = false;
            PlaceTopLeft(emblem, 16f, 24f, 46f, 46f);
            TextMeshProUGUI tierName = Label(button.transform, "Name", "RECRUIT", 22f, Ink, bold: true);
            tierName.characterSpacing = 2f;
            PlaceTopLeft(tierName, 72f, 22f, TierCardWidth - 84f, 28f);
            TextMeshProUGUI status = Label(button.transform, "Status", "Not won yet", 14f, Muted);
            PlaceTopLeft(status, 72f, 50f, TierCardWidth - 84f, 20f);

            var stars = new Object[WV_Campaign.MaxStars];
            for (int i = 0; i < stars.Length; i++) {
                Image star = HudKit.Icon(button.transform, "star", TierStar, StarOff);
                star.raycastTarget = false;
                PlaceTopLeft(star, 16f + i * (TierStar + 6f), 88f, TierStar, TierStar);
                stars[i] = star;
            }
            TextMeshProUGUI bestWave = Label(button.transform, "BestWave", "Best wave 0", 20f, Ink, bold: true);
            PlaceTopLeft(bestWave, 16f, 88f, TierCardWidth - 32f, 30f);
            bestWave.gameObject.SetActive(false);

            Image badge = Panel(button.transform, "New", UnlockViolet);
            badge.raycastTarget = false;
            PlaceTopLeft(badge, TierCardWidth - 16f - 60f, 90f, 60f, 26f);
            TextMeshProUGUI badgeLabel = Label(badge.transform, "Label", "NEW", 15f, Color.white, TextAlignmentOptions.Center, bold: true);
            Stretch(badgeLabel, 0f);
            badge.gameObject.SetActive(false);

            TextMeshProUGUI description = Label(button.transform, "Description", string.Empty, 15f, Ink, TextAlignmentOptions.TopLeft);
            description.textWrappingMode = TextWrappingModes.Normal;
            description.overflowMode = TextOverflowModes.Truncate;
            description.lineSpacing = -4f;
            PlaceTopLeft(description, 16f, 132f, TierCardWidth - 32f, 80f);

            TextMeshProUGUI xp = Label(button.transform, "Xp", "Standard XP", 20f, Gold, bold: true);
            PlaceTopLeft(xp, 16f, 218f, TierCardWidth - 32f, 28f);

            TextMeshProUGUI votes = Label(button.transform, "Votes", "Click to vote", 15f, Muted, TextAlignmentOptions.MidlineLeft, bold: true);
            Anchor(votes, Vector2.zero, new Vector2(1f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 14f), new Vector2(-32f, 24f));

            Image stamp = Panel(button.transform, "Chosen", CardAccent);
            stamp.raycastTarget = false;
            Anchor(stamp, Vector2.zero, new Vector2(1f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 12f), new Vector2(-32f, 30f));
            TextMeshProUGUI stampLabel = Label(stamp.transform, "Label", "CHOSEN", 16f, DarkInk, TextAlignmentOptions.Center, bold: true);
            stampLabel.characterSpacing = 4f;
            Stretch(stampLabel, 0f);
            stamp.gameObject.SetActive(false);

            // The commander's next goal (the tier their campaign card names) wears a ribbon over the top edge.
            Image ribbon = Panel(button.transform, "Goal", Warning);
            ribbon.raycastTarget = false;
            Anchor(ribbon, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(156f, 24f));
            TextMeshProUGUI ribbonLabel = Label(ribbon.transform, "Label", "YOUR NEXT GOAL", 14f, DarkInk, TextAlignmentOptions.Center, bold: true);
            Stretch(ribbonLabel, 4f, 4f, 0f, 0f);
            ribbon.gameObject.SetActive(false);

            var card = button.gameObject.AddComponent<WV_TierCard>();
            WireArray(card, "stars", stars);
            Wire(card, ("button", button), ("background", background), ("selectedOutline", outline), ("accent", accent),
                ("emblem", emblem), ("nameLabel", tierName), ("statusLabel", status), ("bestWaveLabel", bestWave),
                ("newBadge", badge.gameObject), ("descriptionLabel", description), ("xpLabel", xp), ("votesLabel", votes),
                ("goalRibbon", ribbon.gameObject), ("chosenStamp", stamp.gameObject), ("group", group));
            return card;
        }

        static WV_CampaignReveal BuildCampaignReveal(Transform root, RetentionIcons icons, RetentionSummaryCard summary) {
            RectTransform layer = Rect(Node(root, "CampaignReveal").transform);
            Stretch(layer, 0f);
            RectTransform column = Rect(Node(layer, "Column").transform);
            Anchor(column, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 1f), Vector2.zero,
                new Vector2(RevealWidth, RevealStarsHeight + RevealUnlocks * RevealUnlockHeight));

            // The stars a win earned, filling one by one, and what 3 stars needs.
            RectTransform starsTile = ShadowedPanel(column, "Stars", Glass, PanelRadius);
            Anchor(starsTile, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), Vector2.zero, new Vector2(RevealWidth, RevealStarsHeight));
            Image starsEmblem = HudKit.Icon(starsTile, "military_tech", 44f, CardAccent);
            PlaceTopLeft(starsEmblem, 18f, 18f, 44f, 44f);
            TextMeshProUGUI starsTitle = Label(starsTile, "Title", "VETERAN", 20f, Ink, bold: true);
            PlaceTopLeft(starsTitle, 74f, 14f, RevealWidth - 90f, 26f);
            var stars = new Object[WV_Campaign.MaxStars];
            for (int i = 0; i < stars.Length; i++) {
                Image star = HudKit.Icon(starsTile, "star", 34f, StarOff);
                PlaceTopLeft(star, 74f + i * 40f, 42f, 34f, 34f);
                stars[i] = star;
            }
            TextMeshProUGUI starsLine = Label(starsTile, "Line", string.Empty, 15f, Warning, TextAlignmentOptions.TopLeft);
            starsLine.textWrappingMode = TextWrappingModes.Normal;
            PlaceTopLeft(starsLine, 18f, 82f, RevealWidth - 36f, 24f);
            starsTile.gameObject.SetActive(false);

            // One tile per tier a round can open (a Veteran win opens Elite and Endless).
            var tiles = new Object[RevealUnlocks];
            var discs = new Object[RevealUnlocks];
            var tileIcons = new Object[RevealUnlocks];
            var titles = new Object[RevealUnlocks];
            var lines = new Object[RevealUnlocks];
            for (int i = 0; i < RevealUnlocks; i++) {
                RectTransform tile = ShadowedPanel(column, $"Unlock{i}", Glass, PanelRadius);
                Anchor(tile, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), Vector2.zero, new Vector2(RevealWidth, RevealUnlockHeight));
                Image band = Panel(tile, "Accent", UnlockViolet, rounded: false);
                band.raycastTarget = false;
                Anchor(band, Vector2.zero, new Vector2(0f, 1f), new Vector2(0f, 0.5f), new Vector2(0f, 0f), new Vector2(5f, -20f));
                Image disc = Panel(tile, "Disc", UnlockViolet);
                disc.raycastTarget = false;
                disc.pixelsPerUnitMultiplier = UIThemeAssets.RoundedSpriteRadius / 32f;
                PlaceTopLeft(disc, 18f, 20f, 64f, 64f);
                Image icon = HudKit.Icon(disc.transform, "star_medal", 38f, Color.white);
                Anchor(icon, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(38f, 38f));
                TextMeshProUGUI kicker = Label(tile, "Kicker", "TIER UNLOCKED", 14f, UnlockViolet, bold: true);
                kicker.characterSpacing = 4f;
                PlaceTopLeft(kicker, 96f, 14f, RevealWidth - 110f, 18f);
                TextMeshProUGUI title = Label(tile, "Title", "ELITE", 22f, Ink, bold: true);
                PlaceTopLeft(title, 96f, 32f, RevealWidth - 110f, 28f);
                TextMeshProUGUI line = Label(tile, "Line", string.Empty, 14f, Muted, TextAlignmentOptions.TopLeft);
                line.textWrappingMode = TextWrappingModes.Normal;
                line.lineSpacing = -4f;
                PlaceTopLeft(line, 96f, 60f, RevealWidth - 110f, 40f);
                tile.gameObject.SetActive(false);
                tiles[i] = tile;
                discs[i] = disc;
                tileIcons[i] = icon;
                titles[i] = title;
                lines[i] = line;
            }

            var reveal = layer.gameObject.AddComponent<WV_CampaignReveal>();
            WireArray(reveal, "stars", stars);
            WireArray(reveal, "unlockTiles", tiles);
            WireArray(reveal, "unlockDiscs", discs);
            WireArray(reveal, "unlockIcons", tileIcons);
            WireArray(reveal, "unlockTitles", titles);
            WireArray(reveal, "unlockLines", lines);
            Wire(reveal, ("column", column), ("summary", summary), ("icons", icons), ("starsTile", starsTile),
                ("starsTitle", starsTitle), ("starsLine", starsLine), ("starsEmblem", starsEmblem));
            return reveal;
        }
    }
}
