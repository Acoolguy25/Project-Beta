using UnityEditor;
using UnityEngine;
#if !UNITY_SERVER
using RyanAssets.Retention.Client;
using RyanAssets.Retention.Editor;
using RyanAssets.UI;
using RyanAssets.UI.Editor;
using RyanAssets.UI.GameHud;
using TMPro;
using UnityEngine.UI;
using Universes.UniverseData.capture_the_flag.Client;
#endif

namespace Universes.UniverseData.capture_the_flag.Editor {
    /// <summary>
    /// Generates FlagWarsHUD from the shared HudKit: scoreboard and flag chips, team card, objective
    /// pill, flag toasts and banners, edge-of-screen waypoints, hit marker, kill feed and respawn card,
    /// the loadout card (G/H), the medal popup, the tactical minimap (CTF_MinimapAuthoring), and the shared retention HUD (level chip, challenges,
    /// toasts, match summary) nested and placed for this layout. The prefab carries
    /// <see cref="CTF_ClientController"/>, <see cref="CTF_CombatFeedback"/>, <see cref="CTF_GunFeel"/>,
    /// <see cref="CTF_LoadoutHud"/> and <see cref="CTF_MedalFeed"/>. Client Editor only; the server clone
    /// keeps the existing prefab.
    /// </summary>
    internal static class CTF_HudAuthoring {
        /// <summary>
        /// Top of the scoreboard: under the shared top bar's status pill and the health pill it shows
        /// below that while you are hurt (60 to 92 px from the top).
        /// </summary>
        const float BoardTop = -100f;
        const float BoardHeight = 64f, ChipGap = 6f;
        /// <summary>Crosshair, in pixels: tick length and thickness, gap from the centre, cooldown ring size.</summary>
        const float TickLength = 9f, TickWidth = 2f, TickGap = 6f, DotSize = 3f, RingSize = 34f;
        const string RingTexture = "CrosshairRing";
        internal const string PrefabPath = CTF_Authoring.Root + "/Prefabs/FlagWarsHUD.prefab";
        const string KitBeamPath = "Packages/com.ryan.gameframework/Runtime/GameFeel/Generated/Prefabs/FxBeaconBeam.prefab";
        /// <summary>How far the beam's motes may lean out of the column while they rise.</summary>
        const float MoteSpreadDegrees = 6f;
        /// <summary>
        /// Bottom-left stack, in pixels from the bottom: team card, loadout card above it, the level chip above
        /// that. Kept low so the match summary card, which hangs below the chat lines, ends above the level chip.
        /// </summary>
        const float StackLeft = 20f, TeamCardBottom = 90f, CardHeight = 64f, StackGap = 12f;
        const float LoadoutBottom = TeamCardBottom + CardHeight + StackGap;
        const float LevelChipBottom = LoadoutBottom + CardHeight + StackGap;
        /// <summary>Room under the top bar for the chat's two resting lines, above the match summary card.</summary>
        const float ChatLinesClearance = 64f;
        /// <summary>The retention kit's level chip height (RetentionHudAuthoring.ChipHeight) plus a gap, for the challenges panel and the summary card above it.</summary>
        const float LevelChipClearance = 58f;

        public static GameObject Build() {
#if UNITY_SERVER
            return AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
#else
            RectTransform root = HudKit.Canvas("FlagWarsHUD", 30);
            var hud = root.gameObject.AddComponent<CTF_ClientController>();
            hud.flagIcon = HudKit.Icon("flying_flag");
            hud.homeIcon = HudKit.Icon("home");
            hud.carriedIcon = HudKit.Icon("run");
            hud.droppedIcon = HudKit.Icon("place");
            hud.attackIcon = HudKit.Icon("crossed_swords");
            hud.defendIcon = HudKit.Icon("templar_shield");
            hud.victoryIcon = HudKit.Icon("laurels_trophy");
            hud.defeatIcon = HudKit.Icon("tombstone");
            hud.healthIcon = HudKit.Icon("favorite");
            hud.speedIcon = HudKit.Icon("bolt");
            hud.radarIcon = HudKit.Icon(CTF_Authoring.PowerUpIcon(CTF_PowerUpKind.Radar));
            hud.beacons.beamPrefab = BuildFlagBeam();

            // Waypoints first so every card draws over them.
            hud.waypoints = BuildWaypoints(root);
            BuildScoreboard(root, hud);
            BuildTeamCard(root, hud);
            BuildObjective(root, hud);

            // Flag toasts on the right, below the kit's toasts (top right, under the player list).
            hud.toasts = HudKit.ToastFeed(root, "Feed", new Vector2(1f, FlagFeedHeight), new Vector2(-20f, 0f), 420f);
            // The big banner is for the result (and its MVP line); a capture during play gets a smaller one, higher
            // up but below the bots and twist chips (try 1: at 230 it covered both), above the respawn card.
            hud.banner = HudKit.Banner(root, "Banner", new Vector2(0.5f, 0.5f), new Vector2(0f, 120f), 600f, "flying_flag", new Color(1f, 0.79f, 0.24f), 84f);
            hud.captureBanner = HudKit.Banner(root, "CaptureBanner", new Vector2(0.5f, 0.5f), new Vector2(0f, 160f), 420f, "flying_flag", new Color(1f, 0.79f, 0.24f), 54f);

            var gunFeel = root.gameObject.AddComponent<CTF_GunFeel>();
            // Each gun's held model, sound, tracer and flash, and its name on the hotbar (try-1 review: every gun was the pistol).
            gunFeel.looks = CTF_GunAuthoring.AddTo(root.gameObject);
            var combat = BuildCombat(root, gunFeel);
            BuildLoadoutCard(root);
            // A gun or perk picked while dead says so on the card ("Next life"), not over the respawn count.
            root.GetComponent<CTF_LoadoutHud>().quietWhileShown = combat.respawnCard;
            BuildMedals(root, hud.toasts);
            hud.rosterBoard = BuildRosterBoard(root);
            BuildCareerCard(root);
            // The tactical map shares the left column with the challenges panel (J), and steps aside for it.
            hud.minimap = CTF_MinimapAuthoring.Build(root, hud, new Vector2(StackLeft, LevelChipBottom + LevelChipClearance));
            combat.hud = hud;
            combat.waypoints = hud.waypoints;
            // Edge markers never sit on the right-hand feeds (try-3 review: a flag marker hid a kill feed line).
            hud.waypoints.keepClear = new[] { (RectTransform)combat.killFeed.transform, (RectTransform)hud.toasts.transform };
            hud.waypoints.topMargin = WaypointTopMargin;
            NestRetention(root);
            return CTF_Authoring.Kit.Save(root.gameObject, "", "FlagWarsHUD");
#endif
        }

#if !UNITY_SERVER
        /// <summary>Waypoints stay below the scoreboard, its flag chips and the bots chip (share of the screen height).</summary>
        const float WaypointTopMargin = 0.3f;
        /// <summary>
        /// The right column's feeds, as anchors (share of the screen height from the bottom): the flag toasts
        /// start below a full stack of the kit's toasts (four, as when several unlocks reveal at once; at 0.58
        /// the fourth covered a flag toast), and the kill feed starts below four flag toasts.
        /// </summary>
        const float FlagFeedHeight = 0.5f, KillFeedHeight = 0.31f;

        // ---------------------------------------------------------------- retention kit
        /// <summary>
        /// The shared retention HUD, nested and placed: the level chip tops the bottom-left stack (team
        /// card, loadout card, level), the challenges panel (J) grows up from just above it, and the match
        /// summary card hangs from under the top bar. The toasts keep the kit's place
        /// (top right, under the top bar), clear of the flag toasts and kill feed lower on the right.
        /// </summary>
        static void NestRetention(RectTransform root) {
            RetentionHud kit = RetentionHudAuthoring.AddTo(root);
            kit.SetPlacement(RetentionHudPart.LevelChip, new RetentionPlacement(Vector2.zero, new Vector2(StackLeft, LevelChipBottom)));
            var aboveStack = new RetentionPlacement(Vector2.zero, new Vector2(StackLeft, LevelChipBottom + LevelChipClearance));
            kit.SetPlacement(RetentionHudPart.ChallengesPanel, aboveStack);
            // The card hangs from under the top bar and the chat's resting lines (it grows down), so a long card
            // never covers the bar's buttons (try-1 shakedown: grown up from the stack, a full card reached the
            // menu and gold) and the kit's "account level" chat line stays readable above it.
            kit.SetPlacement(RetentionHudPart.SummaryCard, new RetentionPlacement(new Vector2(0f, 1f),
                new Vector2(StackLeft, -(UIKit.Theme.screenMargin + UIKit.Theme.topbarHeight + ChatLinesClearance))));
        }

        // ---------------------------------------------------------------- roster board
        /// <summary>
        /// The full board (Tab, and at the result): both teams side by side with every player and bot,
        /// centred under the result banner and above the hotbar.
        /// </summary>
        /// <summary>Board centre below the screen centre: under the result banner (which ends about 165 below centre) and above the hotbar.</summary>
        const float RosterBoardY = -300f;

        static CTF_RosterBoard BuildRosterBoard(RectTransform root) {
            var view = root.gameObject.AddComponent<CTF_RosterBoard>();
            RectTransform card = HudKit.Card(root, "RosterBoard", new Vector2(0.5f, 0.5f), new Vector2(0f, RosterBoardY), new Vector2(660f, 228f));
            UIKit.Row(card.gameObject, 24f, new RectOffset(20, 20, 14, 14), TextAnchor.UpperCenter);
            RosterColumn(card, CTF_Team.Red, out view.redHeader, out view.redRows);
            RosterColumn(card, CTF_Team.Blue, out view.blueHeader, out view.blueRows);
            view.board = card.GetComponent<HudPopup>();
            return view;
        }

        static void RosterColumn(RectTransform card, CTF_Team team, out TMP_Text header, out TMP_Text rows) {
            RectTransform column = UIKit.Node(card, CTF_Rules.Name(team));
            UIKit.Size(column, 296f, 200f);
            UIKit.Column(column.gameObject, 4f, null, TextAnchor.UpperLeft);
            header = UIKit.Text(column, "Header", CTF_Rules.Name(team), UIText.Title);
            header.fontSize = 20f;
            header.richText = true;
            // Top-aligned and never truncated: the kit's default (middle, ellipsis) showed only the column header.
            rows = UIKit.Text(column, "Rows", "", UIText.Body, UIColorRole.TextPrimary, TextAlignmentOptions.TopLeft);
            rows.fontSize = 15f;
            rows.richText = true;
            rows.lineSpacing = 6f;
            rows.overflowMode = TextOverflowModes.Overflow;
            UIKit.Size(rows, 296f, 168f);
        }

        // ---------------------------------------------------------------- career card
        /// <summary>The kit's challenges panel width (RetentionHudAuthoring.PanelWidth): the career card sits just right of it.</summary>
        const float ChallengesPanelWidth = 420f, CareerGap = 12f, CareerWidth = 320f;

        /// <summary>Standing goals beside the challenges panel (J), bottom-aligned with it.</summary>
        static void BuildCareerCard(RectTransform root) {
            var view = root.gameObject.AddComponent<CTF_CareerCard>();
            RectTransform card = HudKit.Card(root, "CareerCard", Vector2.zero,
                new Vector2(StackLeft + ChallengesPanelWidth + CareerGap, LevelChipBottom + LevelChipClearance), new Vector2(CareerWidth, 200f));
            UIKit.Column(card.gameObject, 8f, new RectOffset(20, 20, 18, 20), TextAnchor.UpperLeft);
            UIKit.FitContent(card.gameObject, false, true);
            HudKit.AccentStripe(card, view.doneColor);
            RectTransform title = UIKit.Node(card, "Header");
            UIKit.Row(title.gameObject, 8f, null, TextAnchor.MiddleLeft);
            HudKit.Icon(title, "military_tech", 24f, view.doneColor);
            UIKit.Text(title, "Title", "Career goals", UIText.Title);
            view.header = UIKit.Text(card, "Summary", "", UIText.Small, UIColorRole.TextSecondary);
            view.header.richText = true;
            UIKit.Size(view.header, CareerWidth - 40f);
            view.rows = UIKit.Text(card, "Goals", "", UIText.Body, UIColorRole.TextPrimary, TextAlignmentOptions.TopLeft);
            view.rows.fontSize = 15f;
            view.rows.richText = true;
            view.rows.lineSpacing = 8f;
            view.rows.overflowMode = TextOverflowModes.Overflow;
            UIKit.Size(view.rows, CareerWidth - 40f);
            view.card = card.GetComponent<HudPopup>();
        }

        // ---------------------------------------------------------------- loadout and medals
        /// <summary>Above the team card: the gun (G) and the perk (H) for the next life.</summary>
        static void BuildLoadoutCard(RectTransform root) {
            RectTransform card = UIKit.Pinned(root, "LoadoutCard", Vector2.zero, new Vector2(StackLeft, LoadoutBottom), new Vector2(360f, CardHeight));
            HudKit.Glass(card, 16f);
            UIKit.Row(card.gameObject, 12f, new RectOffset(12, 16, 0, 0), TextAnchor.MiddleLeft);
            UIKit.FitContent(card.gameObject, true, false);
            var view = root.gameObject.AddComponent<CTF_LoadoutHud>();
            LoadoutSlot(card, "Gun", "G", "pistol", "Sidearm", out view.gunIcon, out view.gunName, out view.gunNote);
            RectTransform divider = UIKit.Node(card, "Divider");
            UIKit.Size(divider, 1f, 36f);
            UIKit.Paint(divider.gameObject.AddComponent<Image>(), UIColorRole.Stroke).raycastTarget = false;
            LoadoutSlot(card, "Perk", "H", "star", "No perk", out view.perkIcon, out view.perkName, out view.perkNote);
            view.nextLife = HudKit.Chip(card, "NextLife", "replay", "Next life", new Color(1f, 0.79f, 0.24f), 26f, UIText.Small);
            view.nextLife.SetVisible(false);
            view.weaponIcons = Icons(CTF_Loadouts.Weapons.Length, i => CTF_Loadouts.Weapons[i].icon);
            view.perkIcons = Icons(CTF_Loadouts.Perks.Length, i => CTF_Loadouts.Perks[i].icon);
            view.card = HudKit.Popup(card, false);
        }

        static void LoadoutSlot(RectTransform card, string name, string key, string icon, string title, out Image iconImage, out TMP_Text label, out TMP_Text note) {
            RectTransform slot = UIKit.Node(card, name);
            UIKit.Row(slot.gameObject, 6f, null, TextAnchor.MiddleLeft);
            HudKit.KeyCap(slot, key);
            iconImage = HudKit.Icon(slot, icon, 28f, Color.white);
            RectTransform text = UIKit.Node(slot, "Text");
            UIKit.Column(text.gameObject, -2f, null, TextAnchor.MiddleLeft);
            label = UIKit.Text(text, "Name", title, UIText.Strong);
            label.fontSize = 17f;
            note = UIKit.Text(text, "Note", "1/1 unlocked", UIText.Caption, UIColorRole.TextMuted);
        }

        /// <summary>Your medals pop up under the crosshair; others' major ones go to the flag toasts.</summary>
        static void BuildMedals(RectTransform root, HudToastFeed toasts) {
            RectTransform popup = UIKit.Pinned(root, "MedalPopup", new Vector2(0.5f, 0.5f), new Vector2(0f, -200f), new Vector2(300f, 60f));
            HudKit.Glass(popup, 30f);
            UIKit.Row(popup.gameObject, 12f, new RectOffset(8, 20, 0, 0), TextAnchor.MiddleLeft);
            UIKit.FitContent(popup.gameObject, true, false);
            var feed = root.gameObject.AddComponent<CTF_MedalFeed>();
            HudKit.Emblem(popup, "Emblem", "star_medal", 46f, new Color(0.98f, 0.8f, 0.3f), out feed.disc, out feed.icon);
            RectTransform text = UIKit.Node(popup, "Text");
            UIKit.Column(text.gameObject, -2f, null, TextAnchor.MiddleLeft);
            feed.title = UIKit.Text(text, "Title", "Double Kill", UIText.Title);
            feed.title.fontSize = 22f;
            feed.xp = UIKit.Text(text, "Xp", "+25 XP", UIText.Strong, UIColorRole.TextSecondary);
            feed.medalIcons = Icons(CTF_MedalTracker.Definitions.Length, i => CTF_MedalTracker.Get((CTF_Medal)i).icon);
            feed.toasts = toasts;
            feed.popup = HudKit.Popup(popup, false);
        }

        static Sprite[] Icons(int count, System.Func<int, string> name) {
            var icons = new Sprite[count];
            for (int i = 0; i < count; i++) icons[i] = HudKit.Icon(name(i));
            return icons;
        }

        // ---------------------------------------------------------------- flag beam
        /// <summary>
        /// The kit's beacon beam with its motes sent up the column. The kit emits them from a flat
        /// circle, so they slid outward along the floor and left team-coloured blobs across both bases.
        /// </summary>
        static ParticleSystem BuildFlagBeam() {
            var source = AssetDatabase.LoadAssetAtPath<ParticleSystem>(KitBeamPath);
            if (source == null) throw new System.InvalidOperationException($"Kit beacon missing at {KitBeamPath}: run Ryan > Game Feel > Rebuild.");
            var beam = Object.Instantiate(source);
            beam.name = "FlagBeam";
            foreach (var system in beam.GetComponentsInChildren<ParticleSystem>(true)) {
                var shape = system.shape;
                if (!shape.enabled || shape.shapeType != ParticleSystemShapeType.Circle) continue;
                // A narrow upward cone from the same ring: the shape's (-90, 0, 0) turn points its +z up.
                shape.shapeType = ParticleSystemShapeType.Cone;
                shape.angle = MoteSpreadDegrees;
            }
            return CTF_Authoring.Kit.Save(beam.gameObject, "", "FlagBeam").GetComponent<ParticleSystem>();
        }

        // ---------------------------------------------------------------- scoreboard
        /// <summary>
        /// Top centre, under the shared top bar: both scores on their team colour, the clock between
        /// them, and where each flag is right now.
        /// </summary>
        static void BuildScoreboard(RectTransform root, CTF_ClientController hud) {
            RectTransform board = UIKit.Pinned(root, "Scoreboard", new Vector2(0.5f, 1f), new Vector2(0f, BoardTop), new Vector2(420f, BoardHeight));
            HudKit.Glass(board, 18f);
            UIKit.Row(board.gameObject, 0f, new RectOffset(6, 6, 6, 6), TextAnchor.MiddleCenter);

            hud.redScoreLabel = TeamScore(board, CTF_Team.Red);
            RectTransform clock = UIKit.Node(board, "Clock");
            UIKit.Size(clock, 156f, 52f);
            UIKit.Column(clock.gameObject, -2f, null, TextAnchor.MiddleCenter);
            hud.timerLabel = UIKit.Text(clock, "Time", "5:00", UIText.Title, UIColorRole.TextPrimary, TextAlignmentOptions.Center);
            hud.timerLabel.fontSize = 26f;
            hud.phaseLabel = UIKit.Text(clock, "Phase", $"First to {CTF_Rules.CapturesToWin}", UIText.Caption, UIColorRole.TextMuted, TextAlignmentOptions.Center);
            hud.blueScoreLabel = TeamScore(board, CTF_Team.Blue);

            // Where each flag is: one chip centred under each team's score.
            hud.redFlagStatus = FlagChip(root, CTF_Team.Red, -138f);
            hud.blueFlagStatus = FlagChip(root, CTF_Team.Blue, 138f);

            // Under the clock, between the flag chips: "4v4 · Regular bots" while bots play.
            hud.botsChip = HudKit.Chip(root, "BotsChip", "groups", "4v4 · Regular bots", UIKit.Theme.textSecondary, 26f, UIText.Small);
            RectTransform bots = (RectTransform)hud.botsChip.transform;
            bots.anchorMin = bots.anchorMax = new Vector2(0.5f, 1f);
            bots.pivot = new Vector2(0.5f, 1f);
            bots.anchoredPosition = new Vector2(0f, BoardTop - BoardHeight - ChipGap * 2f - FlagChipHeight);
            hud.botsChip.SetVisible(false);

            // Under that: today's twist ("Today: Flag Frenzy - grabs and captures pay 2x XP").
            hud.twistChip = HudKit.Chip(root, "TwistChip", "bolt", "Today: Flag Frenzy - grabs and captures pay 2x XP", hud.twistColor, 26f, UIText.Small);
            RectTransform twist = (RectTransform)hud.twistChip.transform;
            twist.anchorMin = twist.anchorMax = new Vector2(0.5f, 1f);
            twist.pivot = new Vector2(0.5f, 1f);
            twist.anchoredPosition = bots.anchoredPosition - new Vector2(0f, 26f + ChipGap);
            hud.twistChip.SetVisible(false);
        }

        const float FlagChipHeight = 28f;

        static HudChip FlagChip(RectTransform root, CTF_Team team, float x) {
            var chip = HudKit.Chip(root, CTF_Rules.Name(team) + "Flag", "home", "Home", CTF_Rules.Color(team), FlagChipHeight, UIText.Small);
            RectTransform rect = (RectTransform)chip.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(x, BoardTop - BoardHeight - ChipGap);
            return chip;
        }

        static TMP_Text TeamScore(RectTransform board, CTF_Team team) {
            RectTransform block = UIKit.Node(board, CTF_Rules.Name(team) + "Score");
            UIKit.Size(block, 120f, 52f);
            Image fill = UIKit.Rounded(block, UIColorRole.Accent, 13f, raycast: false);
            Object.DestroyImmediate(fill.GetComponent<UIThemeColor>());
            fill.color = CTF_Rules.Color(team) * new Color(1f, 1f, 1f, 0.92f);
            bool red = team == CTF_Team.Red;
            UIKit.Row(block.gameObject, 8f, new RectOffset(14, 14, 0, 0), red ? TextAnchor.MiddleLeft : TextAnchor.MiddleRight);
            Image flag = HudKit.Icon(block, "flying_flag", 26f, Color.white);
            TMP_Text score = UIKit.Text(block, "Score", "0", UIText.Display, UIColorRole.OnAccent, red ? TextAlignmentOptions.MidlineRight : TextAlignmentOptions.MidlineLeft);
            score.fontSize = 34f;
            UIKit.Size(score, -1f, -1f, 1f);
            if (!red) {
                score.transform.SetAsFirstSibling();
                flag.rectTransform.localScale = new Vector3(-1f, 1f, 1f);
            }
            return score;
        }

        // ---------------------------------------------------------------- team and objective
        /// <summary>Bottom-left: your team, and the two weapon keys.</summary>
        static void BuildTeamCard(RectTransform root, CTF_ClientController hud) {
            RectTransform card = UIKit.Pinned(root, "TeamCard", Vector2.zero, new Vector2(StackLeft, TeamCardBottom), new Vector2(260f, CardHeight));
            HudKit.Glass(card, 16f);
            UIKit.Row(card.gameObject, 12f, new RectOffset(10, 18, 0, 0), TextAnchor.MiddleLeft);
            UIKit.FitContent(card.gameObject, true, false);
            HudKit.Emblem(card, "Emblem", "templar_shield", 46f, CTF_Rules.Color(CTF_Team.Red), out hud.teamDisc, out _);
            RectTransform text = UIKit.Node(card, "Text");
            UIKit.Column(text.gameObject, 0f, null, TextAnchor.MiddleLeft);
            UIKit.Text(text, "Caption", "Your team", UIText.Caption, UIColorRole.TextMuted);
            hud.teamLabel = UIKit.Text(text, "Team", "Red", UIText.Title);
            hud.teamLabel.fontSize = 20f;

            RectTransform divider = UIKit.Node(card, "Divider");
            UIKit.Size(divider, 1f, 36f);
            UIKit.Paint(divider.gameObject.AddComponent<Image>(), UIColorRole.Stroke).raycastTarget = false;
            WeaponKey(card, "1", "pistol");
            WeaponKey(card, "2", "dagger");
            hud.teamCard = HudKit.Popup(card, false);
        }

        static void WeaponKey(RectTransform parent, string key, string icon) {
            RectTransform slot = UIKit.Node(parent, "Weapon" + key);
            UIKit.Row(slot.gameObject, 4f, null, TextAnchor.MiddleLeft);
            HudKit.KeyCap(slot, key);
            UIKit.Icon(slot, icon, 26f, UIColorRole.TextSecondary);
        }

        /// <summary>Objective, lower centre: what to do right now and how far away it is.</summary>
        static void BuildObjective(RectTransform root, CTF_ClientController hud) {
            RectTransform objective = UIKit.Pinned(root, "Objective", new Vector2(0.5f, 0f), new Vector2(0f, 150f), new Vector2(520f, 48f));
            HudKit.Glass(objective, 24f);
            UIKit.Row(objective.gameObject, 10f, new RectOffset(10, 20, 0, 0), TextAnchor.MiddleLeft);
            UIKit.FitContent(objective.gameObject, true, false);
            HudKit.Emblem(objective, "Emblem", "flying_flag", 36f, CTF_Rules.Color(CTF_Team.Red), out hud.objectiveDisc, out hud.objectiveIcon);
            hud.objectiveLabel = UIKit.Text(objective, "Text", "Grab their flag", UIText.Strong);
            hud.objectiveLabel.richText = true;
            hud.objectiveLabel.fontSize = 18f;
            hud.objectiveDistance = HudKit.Chip(objective, "Distance", "navigation", "40 m", UIKit.Theme.textSecondary, 28f, UIText.Small);
            hud.objectiveDistance.background.enabled = false;
            hud.objective = HudKit.Popup(objective, false);
        }

        // ---------------------------------------------------------------- waypoints
        static CTF_Waypoints BuildWaypoints(RectTransform root) {
            RectTransform area = UIKit.Fill(root, "Waypoints");
            var waypoints = area.gameObject.AddComponent<CTF_Waypoints>();
            waypoints.area = area;
            waypoints.flagMarkers = new[] {
                Marker(area, "RedFlagMarker", "flying_flag", CTF_Rules.Color(CTF_Team.Red)),
                Marker(area, "BlueFlagMarker", "flying_flag", CTF_Rules.Color(CTF_Team.Blue)),
            };
            waypoints.homeMarker = Marker(area, "HomeMarker", "home", Color.white);
            waypoints.killerMarker = Marker(area, "KillerMarker", "skull", CTF_Rules.Color(CTF_Team.Blue));
            return waypoints;
        }

        /// <summary>A disc with an icon, the distance under it, and an arrow ring that points off screen.</summary>
        static CTF_WaypointMarker Marker(RectTransform area, string name, string icon, Color color) {
            const float disc = 40f;
            RectTransform rect = UIKit.Node(area, name);
            UIKit.Center(rect);
            rect.sizeDelta = new Vector2(disc, disc);
            var marker = rect.gameObject.AddComponent<CTF_WaypointMarker>();
            marker.group = rect.gameObject.AddComponent<CanvasGroup>();
            marker.group.blocksRaycasts = marker.group.interactable = false;

            RectTransform arrow = UIKit.Node(rect, "Arrow");
            UIKit.Center(arrow);
            arrow.sizeDelta = Vector2.one * disc;
            RectTransform tip = UIKit.Node(arrow, "Tip");
            tip.anchorMin = tip.anchorMax = new Vector2(0.5f, 1f);
            tip.pivot = new Vector2(0.5f, 0f);
            tip.anchoredPosition = new Vector2(0f, 2f);
            tip.sizeDelta = new Vector2(22f, 22f);
            Image tipImage = tip.gameObject.AddComponent<Image>();
            tipImage.sprite = HudKit.Icon("navigation");
            tipImage.preserveAspect = true;
            tipImage.raycastTarget = false;
            tipImage.color = Color.white;
            marker.arrow = arrow;

            UIKit.Center(HudKit.Emblem(rect, "Emblem", icon, disc, color, out marker.disc, out marker.icon));
            RectTransform label = UIKit.Node(rect, "Distance");
            label.anchorMin = label.anchorMax = new Vector2(0.5f, 0f);
            label.pivot = new Vector2(0.5f, 1f);
            label.anchoredPosition = new Vector2(0f, -2f);
            label.sizeDelta = new Vector2(90f, 20f);
            marker.distance = UIKit.Text(label, "Text", "0 m", UIText.Caption, UIColorRole.TextPrimary, TextAlignmentOptions.Center);
            UIKit.Stretch(marker.distance.rectTransform);
            marker.distance.characterSpacing = 0f;
            marker.distance.fontStyle = FontStyles.Bold;
            marker.distance.outlineWidth = 0.2f;
            marker.distance.outlineColor = new Color32(0, 0, 0, 200);
            return marker;
        }

        // ---------------------------------------------------------------- combat
        static CTF_CombatFeedback BuildCombat(RectTransform root, CTF_GunFeel gunFeel) {
            var combat = root.gameObject.AddComponent<CTF_CombatFeedback>();
            combat.pistolIcon = HudKit.Icon("pistol");
            combat.daggerIcon = HudKit.Icon("dagger");
            combat.fallIcon = HudKit.Icon("mountains");
            combat.skullIcon = HudKit.Icon("skull");
            combat.weaponIcons = Icons(CTF_Loadouts.Weapons.Length, i => CTF_Loadouts.Weapons[i].icon);

            BuildCrosshair(root, gunFeel);

            // Hit marker: an X around the crosshair (built after it, so it draws on top).
            RectTransform marker = UIKit.Pinned(root, "HitMarker", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(44f, 44f));
            combat.hitMarkerIcon = UIKit.Icon(marker, "close", 44f);
            Object.DestroyImmediate(combat.hitMarkerIcon.GetComponent<UIThemeColor>());
            combat.hitMarkerIcon.color = Color.white;
            UIKit.Center(combat.hitMarkerIcon.rectTransform);
            combat.hitMarker = HudKit.Popup(marker, false);
            combat.hitMarker.fadeDuration = 0.06f;
            combat.hitMarker.popFrom = 1.2f;

            // Kill feed: right side, under the flag toasts.
            combat.killFeed = HudKit.ToastFeed(root, "KillFeed", new Vector2(1f, KillFeedHeight), new Vector2(-20f, 0f), 380f, 34f);

            // Respawn card: centre, below the banner.
            RectTransform card = HudKit.Card(root, "RespawnCard", new Vector2(0.5f, 0.5f), new Vector2(0f, -70f), new Vector2(360f, 150f));
            UIKit.Column(card.gameObject, 2f, new RectOffset(24, 24, 18, 20), TextAnchor.UpperCenter);
            UIKit.FitContent(card.gameObject, false, true);
            HudKit.AccentStripe(card, new Color(1f, 0.3f, 0.25f));
            UIKit.Text(card, "Title", "Respawning in", UIText.Caption, UIColorRole.TextMuted, TextAlignmentOptions.Center);
            combat.respawnCount = UIKit.Text(card, "Count", "4", UIText.Display, UIColorRole.TextPrimary, TextAlignmentOptions.Center);
            combat.respawnCount.fontSize = 56f;
            combat.respawnBy = UIKit.Text(card, "By", "Eliminated", UIText.Body, UIColorRole.TextSecondary, TextAlignmentOptions.Center);
            combat.respawnBy.richText = true;
            // The fight that killed you, and the nudge to get even.
            combat.respawnFight = UIKit.Text(card, "Fight", "", UIText.Small, UIColorRole.TextMuted, TextAlignmentOptions.Center);
            combat.respawnFight.richText = true;
            combat.respawnFight.overflowMode = TextOverflowModes.Overflow;
            UIKit.Size(combat.respawnFight, 312f);
            combat.respawnPayback = UIKit.Text(card, "Payback", "", UIText.Small, UIColorRole.TextPrimary, TextAlignmentOptions.Center);
            combat.respawnPayback.richText = true;
            Object.DestroyImmediate(combat.respawnPayback.GetComponent<UIThemeColor>());
            combat.respawnPayback.color = new Color(1f, 0.8f, 0.25f);
            UIKit.Size(combat.respawnPayback, 312f);
            combat.respawnCard = card.GetComponent<HudPopup>();
            return combat;
        }

        /// <summary>Four ticks and a dot at the screen centre, with a cooldown ring around them.</summary>
        static void BuildCrosshair(RectTransform root, CTF_GunFeel gunFeel) {
            RectTransform aim = UIKit.Pinned(root, "Crosshair", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(RingSize, RingSize));
            var crosshair = root.gameObject.AddComponent<CTF_Crosshair>();

            RectTransform ringRect = UIKit.Node(aim, "CooldownRing");
            UIKit.Center(ringRect);
            ringRect.sizeDelta = new Vector2(RingSize, RingSize);
            var ring = ringRect.gameObject.AddComponent<Image>();
            ring.sprite = RingSprite();
            ring.type = Image.Type.Filled;
            ring.fillMethod = Image.FillMethod.Radial360;
            ring.fillOrigin = (int)Image.Origin360.Top;
            ring.fillClockwise = false;
            ring.fillAmount = 0f;
            ring.color = new Color(1f, 1f, 1f, 0.75f);
            ring.raycastTarget = false;
            crosshair.cooldownRing = ring;

            Tick(aim, "Dot", Vector2.zero, new Vector2(DotSize, DotSize));
            float offset = TickGap + TickLength * 0.5f;
            crosshair.ticks = new[] {
                Tick(aim, "Up", new Vector2(0f, offset), new Vector2(TickWidth, TickLength)),
                Tick(aim, "Down", new Vector2(0f, -offset), new Vector2(TickWidth, TickLength)),
                Tick(aim, "Left", new Vector2(-offset, 0f), new Vector2(TickLength, TickWidth)),
                Tick(aim, "Right", new Vector2(offset, 0f), new Vector2(TickLength, TickWidth)),
            };
            // The ticks open with the gun's spread from this gap (a perfectly accurate gun).
            crosshair.baseGap = offset;
            crosshair.gunFeel = gunFeel;
            crosshair.crosshair = HudKit.Popup(aim, false);
        }

        /// <summary>A white tick with a dark outline, readable on sky and on bright walls alike.</summary>
        static RectTransform Tick(RectTransform parent, string name, Vector2 position, Vector2 size) {
            RectTransform rect = UIKit.Node(parent, name);
            UIKit.Center(rect);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            var image = rect.gameObject.AddComponent<Image>();
            image.color = Color.white;
            image.raycastTarget = false;
            var outline = rect.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.65f);
            outline.effectDistance = new Vector2(1f, -1f);
            return rect;
        }

        /// <summary>A thin anti-aliased ring, saved as a sprite in this game's Textures folder.</summary>
        static Sprite RingSprite() {
            const float outer = 0.48f, width = 0.07f, feather = 0.02f;
            CTF_Authoring.Kit.SaveTexture(RingTexture, (u, v) => {
                float d = Vector2.Distance(new Vector2(u, v), new Vector2(0.5f, 0.5f));
                float edge = Mathf.Min(outer - d, d - (outer - width));
                return new Color(1f, 1f, 1f, Mathf.Clamp01(edge / feather + 0.5f));
            }, 128);
            string path = $"{CTF_Authoring.Root}/Textures/{RingTexture}.png";
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }
#endif
    }
}
