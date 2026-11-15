using UnityEngine;
#if !UNITY_SERVER
using RyanAssets.UI;
using RyanAssets.UI.Editor;
using TMPro;
using UnityEngine.UI;
using Universes.UniverseData.hide_and_seek.Client;
using static RyanAssets.UI.Editor.UIKit;
#endif

namespace Universes.UniverseData.hide_and_seek.Editor {
    /// <summary>
    /// Generates the Hide and Seek HUD prefab from the shared UITheme (fonts, rounded
    /// panels, shadows, Material icons), laid out around the shared top bar, chat and player list:
    /// <list type="bullet">
    /// <item>top centre: round card with phase, timer and countdown bar between hiders and seekers left;</item>
    /// <item>bottom left: role card (icon, role, what to do now) and the room you're in;</item>
    /// <item>bottom centre: tag prompt; right: tag feed; centre: toasts and the results banner;</item>
    /// <item>full screen: the seekers' blindfold countdown.</item>
    /// </list>
    /// Edit this script, not the prefab, then run Ryan > Hide and Seek > Build Client Assets.
    /// </summary>
    public static class HS_HudAuthoring {
        public const string PrefabPath = HS_Authoring.Root + "/Prefabs/HideAndSeekHUD.prefab";

        // Icon names (Packages/com.ryan.ui/Runtime/UI/Theme/Icons/icon_<name>.png).
        const string HiderIcon = "directions_run", SeekerIcon = "search", CaughtIcon = "pan_tool", SpectatorIcon = "visibility",
            WaitingIcon = "groups", HidingIcon = "visibility_off", SeekingIcon = "search", ResultsIcon = "emoji_events",
            PingIcon = "my_location", TagIcon = "pan_tool", InfoIcon = "timer", LocationIcon = "home", TimerIcon = "timer";

        public static GameObject Build() {
#if UNITY_SERVER
            return UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
#else
            RectTransform canvas = Canvas("HideAndSeekHUD", 25);
            var hud = canvas.gameObject.AddComponent<HS_ClientController>();
            BuildBlindfold(canvas, hud);
            BuildRoundCard(canvas, hud);
            BuildRoleCard(canvas, hud);
            BuildTagPrompt(canvas, hud);
            BuildFeed(canvas, hud);
            BuildToast(canvas, hud);
            BuildBanner(canvas, hud);
            hud.hiderIcon = UIThemeAssets.Icon(HiderIcon);
            hud.seekerIcon = UIThemeAssets.Icon(SeekerIcon);
            hud.caughtIcon = UIThemeAssets.Icon(CaughtIcon);
            hud.spectatorIcon = UIThemeAssets.Icon(SpectatorIcon);
            hud.waitingIcon = UIThemeAssets.Icon(WaitingIcon);
            hud.hidingIcon = UIThemeAssets.Icon(HidingIcon);
            hud.seekingIcon = UIThemeAssets.Icon(SeekingIcon);
            hud.resultsIcon = UIThemeAssets.Icon(ResultsIcon);
            hud.pingIcon = UIThemeAssets.Icon(PingIcon);
            hud.tagIcon = UIThemeAssets.Icon(TagIcon);
            hud.infoIcon = UIThemeAssets.Icon(InfoIcon);
            var sfx = canvas.gameObject.AddComponent<AudioSource>();
            sfx.playOnAwake = false;
            sfx.spatialBlend = 0f;
            hud.sfx = sfx;
            hud.tagClip = RyanAssets.Authoring.UniverseAuthoringKit.Clip(RyanAssets.Authoring.UniverseAuthoringKit.LandClip);
            hud.pingClip = hud.startClip = RyanAssets.Authoring.UniverseAuthoringKit.Clip(RyanAssets.Authoring.UniverseAuthoringKit.CoinClip);
            return SavePrefab(canvas.gameObject, PrefabPath);
#endif
        }

#if !UNITY_SERVER
        /// <summary>Graphics whose colour the controller sets at runtime must not follow a theme slot.</summary>
        static T Free<T>(T graphic) where T : Graphic {
            if (graphic.TryGetComponent(out UIThemeColor themeColor)) Object.DestroyImmediate(themeColor);
            return graphic;
        }

        /// <summary>
        /// A full-screen, non-blocking layer that one panel and its shadow live in. Shadows are siblings of
        /// their panel, so the layer (not the panel) gets the fader, or a hidden panel leaves its shadow behind.
        /// </summary>
        static RectTransform Layer(RectTransform canvas, string name) => Fill(canvas, name + "Layer");

        static RectTransform Card(Transform parent, string name, Vector2 anchor, Vector2 position, Vector2 size, UIColorRole role = UIColorRole.Surface, float radius = 16f) {
            RectTransform card = Pinned(parent, name, anchor, position, size);
            Shadow(card, 24f, 6f, 0.35f);
            Rounded(card, role, radius, raycast: false);
            return card;
        }

        static TextMeshProUGUI Label(Transform parent, string name, string text, UIText style, UIColorRole role, TextAlignmentOptions align, Vector2 min, Vector2 max) {
            TextMeshProUGUI label = Text(parent, name, text, style, role, align);
            label.rectTransform.anchorMin = min;
            label.rectTransform.anchorMax = max;
            label.rectTransform.offsetMin = label.rectTransform.offsetMax = Vector2.zero;
            return label;
        }

        static Image PlaceIcon(Transform parent, string icon, float size, Vector2 anchor, Vector2 position, UIColorRole role = UIColorRole.TextPrimary) {
            Image image = Icon(parent, icon, size, role);
            RectTransform rect = image.rectTransform;
            rect.anchorMin = rect.anchorMax = rect.pivot = anchor;
            rect.anchoredPosition = position;
            return image;
        }

        // ---------------------------------------------------------------- round card
        static void BuildRoundCard(RectTransform canvas, HS_ClientController hud) {
            RectTransform layer = Layer(canvas, "Round");
            RectTransform card = Card(layer, "RoundCard", new Vector2(0.5f, 1f), new Vector2(0f, -66f), new Vector2(460f, 96f), UIColorRole.SurfaceRaised, 20f);
            hud.roundCard = Fader(layer, false);

            // Centre: phase over timer, countdown bar along the bottom.
            RectTransform centre = Pinned(card, "Phase", new Vector2(0.5f, 0.5f), new Vector2(0f, 2f), new Vector2(200f, 84f));
            RectTransform phaseRow = Pinned(centre, "PhaseRow", new Vector2(0.5f, 1f), new Vector2(0f, -6f), new Vector2(200f, 24f));
            Row(phaseRow.gameObject, 6f, Pad(0), TextAnchor.MiddleCenter);
            hud.phaseIcon = Free(Icon(phaseRow, HidingIcon, 18f));
            hud.phaseLabel = Free(Text(phaseRow, "PhaseLabel", "WAITING", UIText.Caption, UIColorRole.TextSecondary, TextAlignmentOptions.Center));
            hud.phaseLabel.characterSpacing = 6f;
            hud.timerLabel = Label(centre, "Timer", "0:00", UIText.Display, UIColorRole.TextPrimary, TextAlignmentOptions.Center, new Vector2(0f, 0.12f), new Vector2(1f, 0.74f));
            hud.timerLabel.fontSize = 40f;
            hud.timerLabel.fontStyle = FontStyles.Bold;

            RectTransform track = Pinned(card, "CountdownTrack", new Vector2(0.5f, 0f), new Vector2(0f, 10f), new Vector2(180f, 6f));
            Rounded(track, UIColorRole.SurfaceInset, 3f, raycast: false);
            RectTransform fill = Fill(track, "Fill");
            var fillImage = Free(Rounded(fill, UIColorRole.Warning, 3f, raycast: false));
            fillImage.type = Image.Type.Filled;
            fillImage.fillMethod = Image.FillMethod.Horizontal;
            fillImage.fillOrigin = (int)Image.OriginHorizontal.Left;
            fillImage.fillAmount = 1f;
            hud.timerFill = fillImage;

            hud.hidersLabel = TeamCounter(card, "Hiders", HiderIcon, UIColorRole.Success, new Vector2(0f, 0.5f), new Vector2(16f, 0f));
            hud.seekersLabel = TeamCounter(card, "Seekers", SeekerIcon, UIColorRole.Danger, new Vector2(1f, 0.5f), new Vector2(-16f, 0f));
        }

        /// <summary>An icon badge, a big count and a caption, on one side of the round card.</summary>
        static TMP_Text TeamCounter(RectTransform card, string caption, string icon, UIColorRole color, Vector2 anchor, Vector2 position) {
            RectTransform box = Pinned(card, caption, anchor, position, new Vector2(110f, 76f));
            RectTransform badge = Pinned(box, "Badge", new Vector2(anchor.x, 0.5f), Vector2.zero, new Vector2(44f, 44f));
            Rounded(badge, color, 22f, 0.18f, raycast: false);
            PlaceIcon(badge, icon, 26f, new Vector2(0.5f, 0.5f), Vector2.zero, color);
            bool left = anchor.x < 0.5f;
            var align = left ? TextAlignmentOptions.Left : TextAlignmentOptions.Right;
            Vector2 min = left ? new Vector2(0.46f, 0.38f) : new Vector2(0f, 0.38f), max = left ? new Vector2(1f, 0.95f) : new Vector2(0.54f, 0.95f);
            TMP_Text count = Label(box, "Count", "-", UIText.Title, UIColorRole.TextPrimary, align, min, max);
            count.fontSize = 28f;
            Label(box, "Caption", caption, UIText.Caption, color, align, new Vector2(min.x, 0.08f), new Vector2(max.x, 0.38f)).fontSize = 10f;
            return count;
        }

        // ---------------------------------------------------------------- role card and location
        static void BuildRoleCard(RectTransform canvas, HS_ClientController hud) {
            RectTransform layer = Layer(canvas, "Role");
            RectTransform card = Card(layer, "RoleCard", Vector2.zero, new Vector2(16f, 16f), new Vector2(400f, 100f), UIColorRole.SurfaceRaised, 18f);
            hud.roleCard = Fader(layer, false);
            RectTransform accent = Pinned(card, "Accent", new Vector2(0f, 0.5f), new Vector2(10f, 0f), new Vector2(6f, 64f));
            hud.roleAccent = Free(Rounded(accent, UIColorRole.Success, 3f, raycast: false));
            RectTransform badge = Pinned(card, "Badge", new Vector2(0f, 0.5f), new Vector2(28f, 0f), new Vector2(56f, 56f));
            Rounded(badge, UIColorRole.SurfaceInset, 28f, raycast: false);
            hud.roleIcon = Free(PlaceIcon(badge, HiderIcon, 32f, new Vector2(0.5f, 0.5f), Vector2.zero));
            Label(card, "YouAre", "YOU ARE", UIText.Caption, UIColorRole.TextMuted, TextAlignmentOptions.BottomLeft, new Vector2(0.25f, 0.66f), new Vector2(0.96f, 0.86f)).fontSize = 10f;
            hud.roleTitle = Free(Label(card, "Role", "HIDER", UIText.Title, UIColorRole.Success, TextAlignmentOptions.MidlineLeft, new Vector2(0.25f, 0.4f), new Vector2(0.96f, 0.7f)));
            hud.roleTitle.fontSize = 24f;
            hud.roleTitle.overflowMode = TextOverflowModes.Overflow; // a tight one-word line: never let TMP truncate it away
            hud.roleHint = Label(card, "Hint", "", UIText.Small, UIColorRole.TextSecondary, TextAlignmentOptions.TopLeft, new Vector2(0.25f, 0.06f), new Vector2(0.96f, 0.42f));
            hud.roleHint.textWrappingMode = TextWrappingModes.Normal;
            hud.roleHint.overflowMode = TextOverflowModes.Ellipsis;

            RectTransform chipLayer = Layer(canvas, "Location");
            RectTransform chip = Pinned(chipLayer, "LocationChip", Vector2.zero, new Vector2(16f, 126f), new Vector2(240f, 38f));
            Shadow(chip, 16f, 4f, 0.3f);
            Rounded(chip, UIColorRole.Surface, 19f, raycast: false);
            Row(chip.gameObject, 8f, Pad(12, 0), TextAnchor.MiddleLeft, controlWidth: false);
            Icon(chip, LocationIcon, 18f, UIColorRole.Accent);
            hud.locationLabel = Text(chip, "Room", "Living Room", UIText.Strong, UIColorRole.TextPrimary);
            Size(hud.locationLabel, 190f, 38f, 0f);
            hud.locationChip = Fader(chipLayer, false);
        }

        // ---------------------------------------------------------------- prompt, feed, toast, banner
        static void BuildTagPrompt(RectTransform canvas, HS_ClientController hud) {
            RectTransform layer = Layer(canvas, "Tag");
            RectTransform prompt = Pinned(layer, "TagPrompt", new Vector2(0.5f, 0f), new Vector2(0f, 150f), new Vector2(300f, 52f));
            Shadow(prompt, 20f, 6f, 0.4f);
            Rounded(prompt, UIColorRole.Danger, 26f, raycast: false);
            Row(prompt.gameObject, 10f, Pad(16, 0), TextAnchor.MiddleCenter);
            KeyHint(prompt, "E", UIColorRole.OnAccent);
            Icon(prompt, TagIcon, 22f, UIColorRole.OnAccent);
            hud.tagLabel = Text(prompt, "Label", "Tag", UIText.Strong, UIColorRole.OnAccent, TextAlignmentOptions.MidlineLeft);
            hud.tagLabel.fontSize = 18f;
            hud.tagPrompt = Fader(layer, false);
        }

        static void BuildFeed(RectTransform canvas, HS_ClientController hud) {
            RectTransform list = Pinned(canvas, "TagFeed", new Vector2(1f, 0.5f), new Vector2(-16f, -40f), new Vector2(360f, 200f));
            var column = Column(list.gameObject, 6f, Pad(0), TextAnchor.UpperRight);
            column.childControlHeight = column.childControlWidth = true;
            column.childForceExpandHeight = false;
            hud.feedList = list;

            RectTransform row = Node(list, "FeedRow");
            Size(row, -1f, 36f, 1f);
            Rounded(row, UIColorRole.Surface, 10f, raycast: false);
            Row(row.gameObject, 8f, Pad(10, 0), TextAnchor.MiddleLeft, controlWidth: false);
            var feedRow = row.gameObject.AddComponent<HS_FeedRow>();
            feedRow.icon = Icon(row, TagIcon, 18f, UIColorRole.Warning);
            feedRow.label = Text(row, "Text", "Seeker found Hider", UIText.Body, UIColorRole.TextPrimary);
            Size(feedRow.label, 300f, 36f, 1f);
            hud.feedTemplate = feedRow;
        }

        static void BuildToast(RectTransform canvas, HS_ClientController hud) {
            RectTransform layer = Layer(canvas, "Toast");
            RectTransform toast = Pinned(layer, "Toast", new Vector2(0.5f, 0.7f), Vector2.zero, new Vector2(680f, 64f));
            Shadow(toast, 24f, 6f, 0.4f);
            Rounded(toast, UIColorRole.SurfaceRaised, 32f, raycast: false);
            Row(toast.gameObject, 12f, Pad(22, 0), TextAnchor.MiddleCenter);
            hud.toastIcon = Icon(toast, InfoIcon, 28f, UIColorRole.Warning);
            hud.toastLabel = Text(toast, "Message", "Run and hide!", UIText.Title, UIColorRole.TextPrimary, TextAlignmentOptions.Center);
            hud.toast = Fader(layer, false);
        }

        static void BuildBanner(RectTransform canvas, HS_ClientController hud) {
            RectTransform layer = Layer(canvas, "Results");
            RectTransform banner = Card(layer, "ResultsBanner", new Vector2(0.5f, 0.58f), Vector2.zero, new Vector2(620f, 180f), UIColorRole.SurfaceRaised, 24f);
            RectTransform accent = Pinned(banner, "Accent", new Vector2(0.5f, 1f), new Vector2(0f, -10f), new Vector2(560f, 6f));
            hud.bannerAccent = Free(Rounded(accent, UIColorRole.Success, 3f, raycast: false));
            hud.bannerIcon = Free(PlaceIcon(banner, ResultsIcon, 56f, new Vector2(0.5f, 1f), new Vector2(0f, -26f)));
            hud.bannerTitle = Label(banner, "Title", "HIDERS WIN", UIText.Display, UIColorRole.TextPrimary, TextAlignmentOptions.Center, new Vector2(0f, 0.22f), new Vector2(1f, 0.56f));
            hud.bannerTitle.fontSize = 44f;
            hud.bannerSubtitle = Label(banner, "Subtitle", "", UIText.Body, UIColorRole.TextSecondary, TextAlignmentOptions.Center, new Vector2(0f, 0.04f), new Vector2(1f, 0.24f));
            hud.bannerSubtitle.fontSize = 18f;
            hud.banner = Fader(layer, false);
        }

        // ---------------------------------------------------------------- blindfold
        static void BuildBlindfold(RectTransform canvas, HS_ClientController hud) {
            RectTransform blind = Fill(canvas, "Blindfold");
            var cover = blind.gameObject.AddComponent<Image>();
            cover.color = new Color(0.02f, 0.025f, 0.04f, 1f);
            cover.raycastTarget = false;
            hud.blindfold = blind.gameObject;
            PlaceIcon(blind, HidingIcon, 72f, new Vector2(0.5f, 0.5f), new Vector2(0f, 190f), UIColorRole.TextMuted);
            hud.blindHint = Label(blind, "Hint", "EYES CLOSED", UIText.Caption, UIColorRole.TextSecondary, TextAlignmentOptions.Center, new Vector2(0f, 0.6f), new Vector2(1f, 0.64f));
            hud.blindHint.fontSize = 16f;
            hud.blindCountdown = Label(blind, "Countdown", "30", UIText.Display, UIColorRole.TextPrimary, TextAlignmentOptions.Center, new Vector2(0f, 0.38f), new Vector2(1f, 0.6f));
            hud.blindCountdown.fontSize = 150f;
            Label(blind, "Explain", "The hiders are hiding. You'll be released when the count reaches zero.", UIText.Body, UIColorRole.TextSecondary, TextAlignmentOptions.Center, new Vector2(0f, 0.32f), new Vector2(1f, 0.37f)).fontSize = 20f;
            hud.blindMapName = Label(blind, "MapName", "", UIText.Caption, UIColorRole.Accent, TextAlignmentOptions.Center, new Vector2(0f, 0.26f), new Vector2(1f, 0.3f));
            blind.gameObject.SetActive(false);
        }
#endif
    }
}
