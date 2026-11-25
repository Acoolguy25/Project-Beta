using UnityEngine;
#if !UNITY_SERVER
using System.IO;
using System.Linq;
using RyanAssets.GameFeel;
using RyanAssets.Retention.Client;
using RyanAssets.Retention.Editor;
using RyanAssets.UI;
using RyanAssets.UI.Editor;
using TMPro;
using UnityEditor;
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
    /// <item>bottom centre: tag prompt (seekers) or taunt prompt (hiders); right: tag feed; top centre: toasts; left middle:
    /// the results banner with every survivor's hiding spot;</item>
    /// <item>full screen: the seekers' blindfold countdown (the hider's heartbeat vignette is the kit's ScreenFx danger overlay);</item>
    /// <item>bottom centre: the ability bar (keys 1-3); next to the room chip: round points; under the round card: the map event;</item>
    /// <item>the shared retention HUD (<see cref="RetentionHudAuthoring"/>) nested in, its level chip and challenges bottom right.</item>
    /// </list>
    /// The prefab also carries the game's client-side feel: sounds (Audio/, credited in CREDITS.md), the
    /// heartbeat, seeker footsteps, the manor ambience and the candle flicker.
    /// Edit this script, not the prefab, then run Ryan > Hide and Seek > Build Client Assets.
    /// </summary>
    public static class HS_HudAuthoring {
        public const string PrefabPath = HS_Authoring.Root + "/Prefabs/HideAndSeekHUD.prefab";

        // Icon names (Packages/com.ryan.ui/Runtime/UI/Theme/Icons/icon_<name>.png).
        const string HiderIcon = "directions_run", SeekerIcon = "search", CaughtIcon = "pan_tool", SpectatorIcon = "visibility",
            WaitingIcon = "groups", HidingIcon = "visibility_off", SeekingIcon = "search", ResultsIcon = "emoji_events",
            PingIcon = "my_location", TagIcon = "pan_tool", InfoIcon = "timer", LocationIcon = "home", TimerIcon = "timer",
            TauntIcon = "campaign", PointsIcon = "star", LockIcon = "lock", BlackoutIcon = "candle", FrenzyIcon = "siren", ShuffleIcon = "run";

        const string AudioFolder = HS_Authoring.Root + "/Audio";
        /// <summary>The kit's edge-only vignette (clear centre), reused for the blended hider's tint.</summary>
        const string EdgeVignette = "Packages/com.ryan.gameframework/Runtime/GameFeel/Generated/Textures/screen_danger_vignette.png";
        const string KitAudioFolder = "Packages/com.ryan.gameframework/Runtime/GameFeel/Audio";

        public static GameObject Build() {
#if UNITY_SERVER
            return UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
#else
            RectTransform canvas = Canvas("HideAndSeekHUD", 25);
            var hud = canvas.gameObject.AddComponent<HS_ClientController>();
            BuildHeartbeat(canvas, hud);
            BuildBlindfold(canvas, hud);
            BuildRoundCard(canvas, hud);
            BuildRoleCard(canvas, hud);
            BuildTagPrompt(canvas, hud);
            BuildTauntPrompt(canvas, hud);
            BuildFeed(canvas, hud);
            BuildToast(canvas, hud);
            BuildAlert(canvas, hud);
            BuildBanner(canvas, hud);
            BuildPointsChip(canvas, hud);
            BuildEventChip(canvas, hud);
            BuildAbilityBar(canvas, hud);
            BuildRetention(canvas, hud);
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
            hud.tauntIcon = UIThemeAssets.Icon(TauntIcon);
            hud.blackoutIcon = UIThemeAssets.Icon(BlackoutIcon);
            hud.frenzyIcon = UIThemeAssets.Icon(FrenzyIcon);
            hud.shuffleIcon = UIThemeAssets.Icon(ShuffleIcon);
            hud.abilityIcons = new Sprite[HS_Abilities.Count];
            for (int i = 1; i < HS_Abilities.Count; i++) hud.abilityIcons[i] = UIThemeAssets.Icon(HS_Abilities.Info((HS_Ability)i).Icon);
            AssignSounds(hud);
            BuildFootsteps(canvas, hud);
            BuildAmbience(canvas, hud);
            var flicker = canvas.gameObject.AddComponent<HS_LightFlicker>();
            BuildAbilityFeel(canvas, hud);
            BuildBlackout(canvas, hud, flicker);
            return SavePrefab(canvas.gameObject, PrefabPath);
#endif
        }

#if !UNITY_SERVER
        // ---------------------------------------------------------------- sounds
        static AudioClip Clip(string folder, string name) {
            string path = $"{folder}/{name}.ogg";
            return AssetDatabase.LoadAssetAtPath<AudioClip>(path) ?? throw new FileNotFoundException($"[HideAndSeek] Missing sound {path}.");
        }

        /// <summary>A game sound from this game's Audio folder. Distances are world units (0 = the kit's default).</summary>
        static SfxSound Sound(float volume, float pitchJitter, float minInterval, float minDistance, float maxDistance, params string[] clips) =>
            Sound(AudioFolder, volume, pitchJitter, minInterval, minDistance, maxDistance, clips);

        static SfxSound Sound(string folder, float volume, float pitchJitter, float minInterval, float minDistance, float maxDistance, params string[] clips) => new() {
            clips = clips.Select(c => Clip(folder, c)).ToArray(),
            volume = volume,
            volumeJitter = 0.1f,
            pitchJitterSemitones = pitchJitter,
            minInterval = minInterval,
            minDistance = minDistance,
            maxDistance = maxDistance,
        };

        static void AssignSounds(HS_ClientController hud) {
            hud.startSound = Sound(KitAudioFolder, 0.7f, 0.5f, 0.1f, 0f, 0f, "whoosh_1", "whoosh_2");
            hud.pingSound = Sound(KitAudioFolder, 0.6f, 0.3f, 0.2f, 0f, 0f, "alarm");
            // The tag sting is heard in 3D by bystanders, across a couple of rooms.
            hud.tagSting = Sound(0.85f, 0f, 0.15f, 12f, 120f, "sting_tag");
            hud.lastHiderSting = Sound(0.9f, 0f, 0.5f, 0f, 0f, "sting_last_hider");
            hud.lastHiderVoice = Sound(0.9f, 0f, 0.5f, 0f, 0f, "voice_hurry_up");
            // Taunts must carry: a seeker anywhere in the house hears roughly where it came from.
            hud.tauntSound = Sound(1f, 1.5f, 0.3f, 15f, 170f, "taunt_1", "taunt_2", "taunt_3");
            hud.blindTick = Sound(0.55f, 0.2f, 0.2f, 0f, 0f, "tick");
            hud.pointsSound = Sound(KitAudioFolder, 0.55f, 1f, 0.1f, 0f, 0f, "coin");
            hud.eventSound = Sound(KitAudioFolder, 0.8f, 0f, 0.5f, 0f, 0f, "horn");
            hud.recordSound = Sound(KitAudioFolder, 0.8f, 0f, 0.5f, 0f, 0f, "level_up");
        }

        /// <summary>Ability sounds from the kit's library; 3D ones carry about a room or two (world units).</summary>
        static void BuildAbilityFeel(RectTransform canvas, HS_ClientController hud) {
            var feel = canvas.gameObject.AddComponent<HS_AbilityFeel>();
            // The blended hider's view: a cold edge tint behind every HUD element (first child), clear in the middle.
            RectTransform tint = Fill(canvas, "BlendVignette");
            tint.SetAsFirstSibling();
            var tintImage = tint.gameObject.AddComponent<Image>();
            tintImage.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(EdgeVignette)
                ?? throw new FileNotFoundException($"[HideAndSeek] Missing {EdgeVignette}; rebuild the GameFeel assets.");
            tintImage.color = new Color(0.45f, 0.8f, 1f, 1f);
            tintImage.raycastTarget = false;
            var tintGroup = tint.gameObject.AddComponent<CanvasGroup>();
            tintGroup.alpha = 0f;
            tintGroup.blocksRaycasts = tintGroup.interactable = false;
            Wire(feel, ("controller", hud), ("bar", hud.abilityBar), ("blendVignette", tintGroup),
                ("dashSound", Sound(KitAudioFolder, 0.75f, 1f, 0.1f, 10f, 70f, "whoosh_1", "whoosh_2")),
                ("blendSound", Sound(KitAudioFolder, 0.6f, 0.5f, 0.2f, 0f, 0f, "pickup")),
                ("radarSound", Sound(KitAudioFolder, 0.7f, 0f, 0.2f, 12f, 90f, "countdown_beep")),
                ("flashSound", Sound(KitAudioFolder, 0.8f, 1f, 0.1f, 10f, 80f, "hit_1", "hit_2", "hit_3")),
                ("dazzledSound", Sound(KitAudioFolder, 0.6f, 0f, 0.2f, 0f, 0f, "explosion_1")));
        }

        static void BuildBlackout(RectTransform canvas, HS_ClientController hud, HS_LightFlicker flicker) {
            var blackout = canvas.gameObject.AddComponent<HS_Blackout>();
            Wire(blackout, ("controller", hud), ("flicker", flicker),
                ("powerDown", Sound(KitAudioFolder, 0.7f, 0f, 0.5f, 0f, 0f, "explosion_large_1")),
                ("powerUp", Sound(KitAudioFolder, 0.6f, 0f, 0.5f, 0f, 0f, "build_1")));
        }

        /// <summary>Sets serialized fields (private ones included) on a component the prefab is built with.</summary>
        static void Wire(Object target, params (string field, object value)[] fields) {
            var so = new SerializedObject(target);
            foreach (var (field, value) in fields) {
                SerializedProperty property = so.FindProperty(field) ?? throw new System.ArgumentException($"{target.GetType().Name} has no serialized field '{field}'.");
                if (value is Object reference) property.objectReferenceValue = reference;
                else property.boxedValue = value;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static AudioSource Loop(RectTransform canvas, string name, string clip, float volume, float spatialBlend, float minDistance = 1f, float maxDistance = 500f) {
            var go = new GameObject(name, typeof(AudioSource));
            go.transform.SetParent(canvas, false);
            var source = go.GetComponent<AudioSource>();
            source.clip = Clip(AudioFolder, clip);
            source.loop = true;
            source.playOnAwake = false;
            source.volume = volume;
            source.spatialBlend = spatialBlend;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.minDistance = minDistance;
            source.maxDistance = maxDistance;
            source.dopplerLevel = 0f;
            source.outputAudioMixerGroup = UITheme.Active.soundGroup;
            return source;
        }

        static void BuildAmbience(RectTransform canvas, HS_ClientController hud) {
            var ambience = canvas.gameObject.AddComponent<HS_Ambience>();
            AudioSource wind = Loop(canvas, "Wind", "wind_loop", 0.5f, 0f);
            // The clock is heard through the foyer and the rooms beside it (world units).
            AudioSource clock = Loop(canvas, "Clock", "clock_loop", 0.7f, 1f, 8f, 70f);
            Wire(ambience, ("controller", hud), ("wind", wind), ("clock", clock),
                ("creaks", Sound(0.55f, 2f, 1f, 6f, 60f, "creak_1", "creak_2", "creak_3", "door_creak_1", "door_creak_2")));
        }

        static void BuildFootsteps(RectTransform canvas, HS_ClientController hud) {
            var footsteps = canvas.gameObject.AddComponent<HS_SeekerFootsteps>();
            // Heavy wooden thuds that carry through floors and walls.
            Wire(footsteps, ("controller", hud), ("step", Sound(0.9f, 1.5f, 0.05f, 10f, 90f, "seeker_step_1", "seeker_step_2", "seeker_step_3", "seeker_step_4", "seeker_step_5")));
        }

        /// <summary>The hider's heartbeat; its vignette is the kit's danger overlay, which sits under every HUD.</summary>
        static void BuildHeartbeat(RectTransform canvas, HS_ClientController hud) {
            var heartbeat = canvas.gameObject.AddComponent<HS_Heartbeat>();
            Wire(heartbeat, ("controller", hud), ("beat", Sound(0.9f, 0f, 0.15f, 0f, 0f, "heartbeat")));
        }

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
            RectTransform chip = Pinned(chipLayer, "LocationChip", Vector2.zero, new Vector2(16f, 126f), new Vector2(LocationChipWidth, 38f));
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
            RectTransform prompt = Pinned(layer, "TagPrompt", new Vector2(0.5f, 0f), new Vector2(0f, 150f), new Vector2(380f, 52f));
            Shadow(prompt, 20f, 6f, 0.4f);
            Rounded(prompt, UIColorRole.Danger, 26f, raycast: false);
            Row(prompt.gameObject, 10f, Pad(16, 0), TextAnchor.MiddleCenter);
            KeyHint(prompt, "E", UIColorRole.OnAccent);
            Icon(prompt, TagIcon, 22f, UIColorRole.OnAccent);
            hud.tagLabel = Text(prompt, "Label", "Tag", UIText.Strong, UIColorRole.OnAccent, TextAlignmentOptions.MidlineLeft);
            hud.tagLabel.fontSize = 18f;
            hud.tagPrompt = Fader(layer, false);
        }

        /// <summary>Hiders' taunt prompt, in the tag prompt's place (nobody sees both): key, label and a recharge bar.</summary>
        static void BuildTauntPrompt(RectTransform canvas, HS_ClientController hud) {
            RectTransform layer = Layer(canvas, "Taunt");
            RectTransform prompt = Pinned(layer, "TauntPrompt", new Vector2(0.5f, 0f), new Vector2(0f, 150f), new Vector2(300f, 52f));
            Shadow(prompt, 20f, 6f, 0.4f);
            Rounded(prompt, UIColorRole.SurfaceRaised, 26f, raycast: false);
            RectTransform row = Fill(prompt, "Row", 16f, 0f, 16f, 6f);
            Row(row.gameObject, 10f, Pad(0), TextAnchor.MiddleCenter);
            KeyHint(row, "R", UIColorRole.TextPrimary);
            Icon(row, TauntIcon, 22f, UIColorRole.Warning);
            hud.tauntLabel = Text(row, "Label", "Taunt", UIText.Strong, UIColorRole.TextPrimary, TextAlignmentOptions.MidlineLeft);
            hud.tauntLabel.fontSize = 17f;
            RectTransform track = Pinned(prompt, "RechargeTrack", new Vector2(0.5f, 0f), new Vector2(0f, 6f), new Vector2(220f, 4f));
            Rounded(track, UIColorRole.SurfaceInset, 2f, raycast: false);
            var fill = Rounded(Fill(track, "Fill"), UIColorRole.Warning, 2f, raycast: false);
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Horizontal;
            fill.fillOrigin = (int)Image.OriginHorizontal.Left;
            fill.fillAmount = 1f;
            hud.tauntFill = fill;
            hud.tauntPrompt = Fader(layer, false);
        }

        static void BuildFeed(RectTransform canvas, HS_ClientController hud) {
            RectTransform list = Pinned(canvas, "TagFeed", new Vector2(1f, 0.5f), new Vector2(-16f, -40f), new Vector2(FeedWidth, 200f));
            var column = Column(list.gameObject, 8f, Pad(0), TextAnchor.UpperRight);
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
            // One line across the rest of the row (the row does not size its children, so set the rect
            // itself). Two 16-character names fit at full size; anything longer shrinks a little, never wraps.
            const float labelWidth = FeedWidth - 20f - 18f - 8f;
            Size(feedRow.label, labelWidth, 36f, 1f);
            feedRow.label.rectTransform.sizeDelta = new Vector2(labelWidth, 36f);
            feedRow.label.textWrappingMode = TextWrappingModes.NoWrap;
            ShrinkToFit(feedRow.label, 14f);
            hud.feedTemplate = feedRow;
        }

        const float FeedWidth = 460f;

        static void ShrinkToFit(TMP_Text label, float minSize) {
            label.enableAutoSizing = true;
            label.fontSizeMax = label.fontSize;
            label.fontSizeMin = minSize;
        }

        static void BuildToast(RectTransform canvas, HS_ClientController hud) {
            RectTransform layer = Layer(canvas, "Toast");
            // Just under the round card: clear of the character's head and of the world popups above it (taunt XP).
            RectTransform toast = Pinned(layer, "Toast", new Vector2(0.5f, 0.78f), Vector2.zero, new Vector2(780f, 64f));
            Shadow(toast, 24f, 6f, 0.4f);
            Rounded(toast, UIColorRole.SurfaceRaised, 32f, raycast: false);
            Row(toast.gameObject, 12f, Pad(22, 0), TextAnchor.MiddleCenter);
            hud.toastIcon = Icon(toast, InfoIcon, 28f, UIColorRole.Warning);
            hud.toastLabel = Text(toast, "Message", "Run and hide!", UIText.Title, UIColorRole.TextPrimary, TextAlignmentOptions.Center);
            hud.toast = Fader(layer, false);
        }

        /// <summary>
        /// The last-hider headline: upper third (above the characters' heads, under the toast), at most
        /// about 45% of the screen wide and 56 px tall, so the player's own character and the feed stay in view.
        /// </summary>
        static void BuildAlert(RectTransform canvas, HS_ClientController hud) {
            RectTransform layer = Layer(canvas, "Alert");
            RectTransform box = Pinned(layer, "Alert", new Vector2(0.5f, 0.70f), Vector2.zero, new Vector2(860f, 70f));
            TextMeshProUGUI label = Text(box, "Text", "LAST HIDER!", UIText.Display, UIColorRole.Danger, TextAlignmentOptions.Center);
            Stretch(label.rectTransform);
            label.fontSize = 56f;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            ShrinkToFit(label, 30f);
            // The theme's drop-shadow variant of the bold font keeps red text readable over any room.
            var shadowed = AssetDatabase.LoadAssetAtPath<Material>(BoldShadowMaterial);
            if (shadowed != null && label.font != null && shadowed.mainTexture == label.font.material.mainTexture) label.fontSharedMaterial = shadowed;
            hud.alertLabel = label;
            hud.alert = Fader(layer, false);
        }

        const string BoldShadowMaterial = "Packages/com.ryan.ui/Runtime/UI/Theme/Fonts/Inter-Bold SDF Shadow.mat";

        static void Stretch(RectTransform rect) {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }

        static void BuildBanner(RectTransform canvas, HS_ClientController hud) {
            RectTransform layer = Layer(canvas, "Results");
            // Left side, its centre at 36% of the height: below the band where the survivors' names float
            // over the world (about 30-36% from the top), above the role card, clear of your own character.
            const float width = 560f, height = BannerHeight;
            RectTransform banner = Card(layer, "ResultsBanner", new Vector2(0f, 0.36f), new Vector2(24f, 0f), new Vector2(width, height), UIColorRole.SurfaceRaised, 24f);
            RectTransform accent = Pinned(banner, "Accent", new Vector2(0.5f, 1f), new Vector2(0f, -10f), new Vector2(width - 60f, 6f));
            hud.bannerAccent = Free(Rounded(accent, UIColorRole.Success, 3f, raycast: false));
            hud.bannerIcon = Free(PlaceIcon(banner, ResultsIcon, 36f, new Vector2(0.5f, 1f), new Vector2(0f, -22f)));
            // Rows from the top, in canvas units: icon 22-58, title 60-98, subtitle 100-124, then the list.
            hud.bannerTitle = BannerRow(banner, "Title", "HIDERS WIN", UIText.Display, UIColorRole.TextPrimary, 60f, 98f);
            hud.bannerTitle.fontSize = 34f;
            hud.bannerSubtitle = BannerRow(banner, "Subtitle", "", UIText.Body, UIColorRole.TextSecondary, 100f, 124f);
            hud.bannerSubtitle.fontSize = 18f;
            // The round's MVP and your own points, record and streak (HS_RoundRecapBroadcast).
            hud.bannerMvp = BannerRow(banner, "Mvp", "", UIText.Strong, UIColorRole.TextPrimary, 130f, 154f);
            hud.bannerMvp.fontSize = 18f;
            hud.bannerYou = BannerRow(banner, "You", "", UIText.Body, UIColorRole.TextPrimary, 156f, 180f);
            hud.bannerYou.fontSize = 17f;
            // A line taller than its row is dropped whole by Ellipsis/Truncate (the font has no ellipsis
            // glyph), so the fixed-height rows overflow instead; the texts are short and fit the width.
            foreach (TMP_Text row in new TMP_Text[] { hud.bannerTitle, hud.bannerSubtitle, hud.bannerMvp, hud.bannerYou }) {
                row.textWrappingMode = TextWrappingModes.NoWrap;
                row.overflowMode = TextOverflowModes.Overflow;
                row.verticalAlignment = VerticalAlignmentOptions.Middle;
            }
            // Where the survivors hid: up to HS_ClientController.bannerSpotLines lines, then "+N more", all
            // inside the card (BannerHeight leaves room for BannerListLines lines at this size).
            hud.bannerSpots = BannerRow(banner, "HidingSpots", "", UIText.Body, UIColorRole.TextPrimary, BannerListTop, height - 14f);
            hud.bannerSpots.fontSize = BannerListFontSize;
            hud.bannerSpots.lineSpacing = 0f;
            hud.bannerSpots.alignment = TextAlignmentOptions.Top;
            hud.bannerSpots.textWrappingMode = TextWrappingModes.NoWrap;
            hud.bannerSpots.overflowMode = TextOverflowModes.Overflow;
            hud.bannerSpotLines = BannerListLines - 1;
            hud.bannerCard = banner;
            hud.bannerShadow = (RectTransform)layer.Find(banner.name + "Shadow");
            hud.bannerListTop = BannerListTop;
            hud.bannerLineHeight = BannerListFontSize * 1.3f;
            hud.bannerBottomMargin = 18f;
            hud.banner = Fader(layer, false);
        }

        /// <summary>The results card: its list holds this many lines (the survivors plus "+N more").</summary>
        const int BannerListLines = 5;
        const float BannerListFontSize = 17f, BannerListTop = 190f;
        /// <summary>Card height: header, then the list at about 1.3 line heights per line, then the bottom margin.</summary>
        const float BannerHeight = BannerListTop + BannerListLines * BannerListFontSize * 1.3f + 18f;

        /// <summary>A full-width centred text row of the banner, between two distances from its top edge.</summary>
        static TextMeshProUGUI BannerRow(RectTransform banner, string name, string text, UIText style, UIColorRole role, float top, float bottom) {
            TextMeshProUGUI label = Text(banner, name, text, style, role, TextAlignmentOptions.Center);
            RectTransform rect = label.rectTransform;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.offsetMin = new Vector2(24f, -bottom);
            rect.offsetMax = new Vector2(-24f, -top);
            return label;
        }

        // ---------------------------------------------------------------- round points, map event
        /// <summary>Round points, beside the room chip: a star and the total, punched when it grows.</summary>
        static void BuildPointsChip(RectTransform canvas, HS_ClientController hud) {
            RectTransform layer = Layer(canvas, "Points");
            RectTransform chip = Pinned(layer, "PointsChip", Vector2.zero, new Vector2(16f + LocationChipWidth + 8f, 126f), new Vector2(PointsChipWidth, 38f));
            Shadow(chip, 16f, 4f, 0.3f);
            Rounded(chip, UIColorRole.Surface, 19f, raycast: false);
            Row(chip.gameObject, 8f, Pad(12, 0), TextAnchor.MiddleLeft, controlWidth: false);
            Icon(chip, PointsIcon, 18f, UIColorRole.Warning);
            hud.pointsLabel = Text(chip, "Points", "0", UIText.Strong, UIColorRole.TextPrimary);
            TMP_Text caption = Text(chip, "Caption", "PTS", UIText.Caption, UIColorRole.TextMuted);
            // The row does not control widths (the icon keeps its size), so the texts get explicit rects too:
            // a text keeps TMP's default 200-unit width otherwise and pushes "PTS" out of the chip.
            foreach ((TMP_Text text, float width) in new[] { (hud.pointsLabel, 52f), (caption, 34f) }) {
                Size(text, width, 38f, 0f);
                text.rectTransform.sizeDelta = new Vector2(width, 38f);
                text.textWrappingMode = TextWrappingModes.NoWrap;
                text.overflowMode = TextOverflowModes.Overflow;
            }
            hud.pointsPunch = chip;
            // "+15 Close call": rises from just above the chip and fades (HS_ClientController.ShowPointsPopup).
            TextMeshProUGUI popup = Text(layer, "PointsPopup", "+15 Close call", UIText.Strong, UIColorRole.Warning, TextAlignmentOptions.MidlineLeft);
            popup.rectTransform.anchorMin = popup.rectTransform.anchorMax = popup.rectTransform.pivot = Vector2.zero;
            popup.rectTransform.anchoredPosition = new Vector2(16f + LocationChipWidth + 8f + 6f, 126f + 38f + 8f);
            popup.rectTransform.sizeDelta = new Vector2(360f, 30f);
            popup.fontSize = 22f;
            popup.textWrappingMode = TextWrappingModes.NoWrap;
            popup.overflowMode = TextOverflowModes.Overflow;
            popup.raycastTarget = false;
            popup.gameObject.SetActive(false);
            hud.pointsPopup = popup;
            hud.pointsChip = Fader(layer, false);
        }

        const float LocationChipWidth = 240f, PointsChipWidth = 150f;

        /// <summary>The running map event (Blackout, Frenzy, Shuffle) and its countdown, just under the round card.</summary>
        static void BuildEventChip(RectTransform canvas, HS_ClientController hud) {
            RectTransform layer = Layer(canvas, "Event");
            RectTransform chip = Pinned(layer, "EventChip", new Vector2(0.5f, 1f), new Vector2(0f, -170f), new Vector2(260f, 40f));
            Shadow(chip, 16f, 4f, 0.35f);
            Rounded(chip, UIColorRole.SurfaceRaised, 20f, raycast: false);
            Row(chip.gameObject, 8f, Pad(14, 0), TextAnchor.MiddleCenter, controlWidth: false);
            hud.eventIcon = Free(Icon(chip, BlackoutIcon, 20f, UIColorRole.Warning));
            hud.eventLabel = Text(chip, "Event", "BLACKOUT 0:18", UIText.Strong, UIColorRole.Warning, TextAlignmentOptions.MidlineLeft);
            Size(hud.eventLabel, 200f, 40f, 0f);
            hud.eventLabel.textWrappingMode = TextWrappingModes.NoWrap;
            hud.eventLabel.overflowMode = TextOverflowModes.Overflow;
            hud.eventChip = Fader(layer, false);
        }

        // ---------------------------------------------------------------- abilities
        const float SlotSize = 76f, SlotGap = 14f, SlotLabelHeight = 20f;

        /// <summary>Three ability keys at the bottom centre, under the tag and taunt prompts.</summary>
        static void BuildAbilityBar(RectTransform canvas, HS_ClientController hud) {
            RectTransform layer = Layer(canvas, "Abilities");
            int count = HS_Abilities.SlotsPerSide;
            float width = count * SlotSize + (count - 1) * SlotGap;
            RectTransform bar = Pinned(layer, "AbilityBar", new Vector2(0.5f, 0f), new Vector2(0f, 18f), new Vector2(width, SlotSize + SlotLabelHeight + 4f));
            var slots = new HS_AbilitySlotView[count];
            for (int i = 0; i < count; i++) slots[i] = BuildSlot(bar, i, new Vector2(i * (SlotSize + SlotGap), 0f));
            // On the canvas, not the layer: a hidden layer is deactivated, and the bar must keep listening.
            // The line above the bar for ability results (HS_AbilityBar.Say): clear of the view and of the centre toast.
            TextMeshProUGUI message = Text(layer, "AbilityMessage", "Radar: 2 contacts nearby!", UIText.Strong, UIColorRole.TextPrimary, TextAlignmentOptions.Center);
            RectTransform messageRect = message.rectTransform;
            messageRect.anchorMin = messageRect.anchorMax = new Vector2(0.5f, 0f);
            messageRect.pivot = new Vector2(0.5f, 0f);
            messageRect.anchoredPosition = new Vector2(0f, 18f + SlotSize + SlotLabelHeight + 12f);
            messageRect.sizeDelta = new Vector2(720f, 30f);
            message.fontSize = 20f;
            message.textWrappingMode = TextWrappingModes.NoWrap;
            message.overflowMode = TextOverflowModes.Overflow;
            message.raycastTarget = false;
            var shadowed = AssetDatabase.LoadAssetAtPath<Material>(BoldShadowMaterial);
            if (shadowed != null && message.font != null && shadowed.mainTexture == message.font.material.mainTexture) message.fontSharedMaterial = shadowed;
            var abilityBar = canvas.gameObject.AddComponent<HS_AbilityBar>();
            Wire(abilityBar, ("controller", hud), ("bar", Fader(layer, false)), ("messageLabel", message),
                ("readySound", Sound(KitAudioFolder, 0.35f, 0f, 0.3f, 0f, 0f, "pickup")));
            hud.abilityBar = abilityBar;
            var so = new SerializedObject(abilityBar);
            SerializedProperty list = so.FindProperty("slots");
            list.arraySize = count;
            for (int i = 0; i < count; i++) list.GetArrayElementAtIndex(i).objectReferenceValue = slots[i];
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static HS_AbilitySlotView BuildSlot(RectTransform bar, int index, Vector2 offset) {
            RectTransform slot = Pinned(bar, $"Slot{index + 1}", Vector2.zero, offset, new Vector2(SlotSize, SlotSize + SlotLabelHeight + 4f));
            var view = slot.gameObject.AddComponent<HS_AbilitySlotView>();
            RectTransform face = Pinned(slot, "Face", new Vector2(0.5f, 1f), Vector2.zero, new Vector2(SlotSize, SlotSize));
            Shadow(face, 18f, 5f, 0.4f);
            Rounded(face, UIColorRole.SurfaceRaised, 18f, raycast: false);
            Image icon = Free(PlaceIcon(face, "sprint", 40f, new Vector2(0.5f, 0.5f), new Vector2(0f, 2f)));
            Image cooldown = Radial(face, "Cooldown", UIColorRole.SurfaceInset, 0.8f, 1f);
            cooldown.fillAmount = 0f;
            Image active = Radial(face, "Active", UIColorRole.Success, 0.35f, 1f);
            active.gameObject.SetActive(false);
            TextMeshProUGUI timer = Label(face, "Timer", "", UIText.Title, UIColorRole.TextPrimary, TextAlignmentOptions.Center, Vector2.zero, Vector2.one);
            timer.fontSize = 26f;
            RectTransform key = Pinned(face, "Key", new Vector2(0f, 1f), new Vector2(6f, -6f), new Vector2(22f, 22f));
            Rounded(key, UIColorRole.TextPrimary, 6f, 0.16f, raycast: false);
            TextMeshProUGUI keyLabel = Label(key, "Key", (index + 1).ToString(), UIText.Caption, UIColorRole.TextPrimary, TextAlignmentOptions.Center, Vector2.zero, Vector2.one);
            keyLabel.fontSize = 13f;
            keyLabel.overflowMode = TextOverflowModes.Overflow;

            // Locked: a light veil (the ability's icon stays readable under it at lockedIconAlpha), a clear
            // lock badge in the top-right corner and the unlock level along the bottom.
            RectTransform locked = Fill(face, "Locked");
            Rounded(locked, UIColorRole.Surface, 18f, 0.35f, raycast: false);
            RectTransform badge = Pinned(locked, "LockBadge", new Vector2(1f, 1f), new Vector2(-5f, -5f), new Vector2(24f, 24f));
            Rounded(badge, UIColorRole.SurfaceInset, 12f, raycast: false);
            PlaceIcon(badge, LockIcon, 16f, new Vector2(0.5f, 0.5f), Vector2.zero, UIColorRole.TextPrimary);
            RectTransform levelPill = Pinned(locked, "LevelPill", new Vector2(0.5f, 0f), new Vector2(0f, 5f), new Vector2(46f, 18f));
            Rounded(levelPill, UIColorRole.SurfaceInset, 9f, raycast: false);
            TextMeshProUGUI lockLabel = Label(levelPill, "Level", "LV 2", UIText.Caption, UIColorRole.Warning, TextAlignmentOptions.Center, Vector2.zero, Vector2.one);
            lockLabel.fontSize = 12f;
            lockLabel.overflowMode = TextOverflowModes.Overflow;

            TextMeshProUGUI nameLabel = Label(slot, "Name", "Dash", UIText.Caption, UIColorRole.TextSecondary, TextAlignmentOptions.Center, Vector2.zero, new Vector2(1f, SlotLabelHeight / (SlotSize + SlotLabelHeight + 4f)));
            nameLabel.fontSize = 12f;
            nameLabel.textWrappingMode = TextWrappingModes.NoWrap;
            nameLabel.overflowMode = TextOverflowModes.Overflow;
            Wire(view, ("icon", icon), ("cooldownFill", cooldown), ("activeRing", active), ("keyLabel", keyLabel), ("nameLabel", nameLabel),
                ("timerLabel", timer), ("lockOverlay", locked.gameObject), ("lockLabel", lockLabel), ("punchTarget", face));
            return view;
        }

        /// <summary>A full-size radial overlay (clockwise from the top) on a rounded face.</summary>
        static Image Radial(RectTransform face, string name, UIColorRole role, float opacity, float fill) {
            Image image = Free(Rounded(Fill(face, name), role, 18f, opacity, raycast: false));
            image.type = Image.Type.Filled;
            image.fillMethod = Image.FillMethod.Radial360;
            image.fillOrigin = (int)Image.Origin360.Top;
            image.fillClockwise = false;
            image.fillAmount = fill;
            return image;
        }

        // ---------------------------------------------------------------- shared retention HUD
        /// <summary>
        /// Nests the kit's HUD and moves its level chip and challenges panel to the bottom right (the role
        /// card holds the bottom left). The kit (try 2) puts the match summary at the left edge, where this
        /// game's results banner sits, so the summary moves to the right centre: the tag feed there is
        /// cleared at round end, and the kit keeps the challenges panel shut while the summary shows.
        /// Toasts keep the kit's top-right spot.
        /// </summary>
        static void BuildRetention(RectTransform canvas, HS_ClientController hud) {
            RetentionHud retention = RetentionHudAuthoring.AddTo(canvas);
            retention.SetPlacement(RetentionHudPart.LevelChip, new RetentionPlacement(new Vector2(1f, 0f), new Vector2(-16f, 16f)));
            retention.SetPlacement(RetentionHudPart.ChallengesPanel, new RetentionPlacement(new Vector2(1f, 0f), new Vector2(-16f, 70f)));
            retention.SetPlacement(RetentionHudPart.SummaryCard, new RetentionPlacement(new Vector2(1f, 0.5f), new Vector2(-24f, 0f)));
            // Script changes to a nested prefab instance are only kept when recorded as overrides.
            PrefabUtility.RecordPrefabInstancePropertyModifications(retention);
            RecordRect(retention.GetComponentInChildren<RetentionLevelChip>(true));
            RecordRect(retention.GetComponentInChildren<RetentionChallengesPanel>(true));
            RecordRect(retention.GetComponentInChildren<RetentionSummaryCard>(true));
            hud.retentionHud = retention;
        }

        static void RecordRect(Component part) {
            if (part == null) throw new FileNotFoundException("The retention HUD prefab is missing a part; rebuild it (Ryan > Retention > Rebuild HUD).");
            PrefabUtility.RecordPrefabInstancePropertyModifications(part.transform);
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
