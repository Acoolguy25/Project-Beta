using UnityEditor;
using UnityEngine;
#if !UNITY_SERVER
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
    /// pill, flag toasts and banners, edge-of-screen waypoints, hit marker, kill feed and respawn card.
    /// The prefab carries <see cref="CTF_ClientController"/>, <see cref="CTF_CombatFeedback"/> and
    /// <see cref="CTF_GunFeel"/>. Client Editor only; the server clone keeps the existing prefab.
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
        const string PrefabPath = CTF_Authoring.Root + "/Prefabs/FlagWarsHUD.prefab";

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

            // Waypoints first so every card draws over them.
            hud.waypoints = BuildWaypoints(root);
            BuildScoreboard(root, hud);
            BuildTeamCard(root, hud);
            BuildObjective(root, hud);

            hud.toasts = HudKit.ToastFeed(root, "Feed", new Vector2(1f, 0.66f), new Vector2(-20f, 0f), 420f);
            hud.banner = HudKit.Banner(root, "Banner", new Vector2(0.5f, 0.5f), new Vector2(0f, 120f), 600f, "flying_flag", new Color(1f, 0.79f, 0.24f), 84f);

            BuildCombat(root);
            root.gameObject.AddComponent<CTF_GunFeel>();
            return CTF_Authoring.Kit.Save(root.gameObject, "", "FlagWarsHUD");
#endif
        }

#if !UNITY_SERVER
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
        }

        static HudChip FlagChip(RectTransform root, CTF_Team team, float x) {
            var chip = HudKit.Chip(root, CTF_Rules.Name(team) + "Flag", "home", "Home", CTF_Rules.Color(team), 28f, UIText.Small);
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
            RectTransform card = UIKit.Pinned(root, "TeamCard", Vector2.zero, new Vector2(20f, 200f), new Vector2(260f, 64f));
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
        static void BuildCombat(RectTransform root) {
            var combat = root.gameObject.AddComponent<CTF_CombatFeedback>();
            combat.pistolIcon = HudKit.Icon("pistol");
            combat.daggerIcon = HudKit.Icon("dagger");
            combat.fallIcon = HudKit.Icon("mountains");
            combat.skullIcon = HudKit.Icon("skull");

            BuildCrosshair(root);

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
            combat.killFeed = HudKit.ToastFeed(root, "KillFeed", new Vector2(1f, 0.4f), new Vector2(-20f, 0f), 380f, 34f);

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
            combat.respawnCard = card.GetComponent<HudPopup>();
        }

        /// <summary>Four ticks and a dot at the screen centre, with a cooldown ring around them.</summary>
        static void BuildCrosshair(RectTransform root) {
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
            Tick(aim, "Up", new Vector2(0f, offset), new Vector2(TickWidth, TickLength));
            Tick(aim, "Down", new Vector2(0f, -offset), new Vector2(TickWidth, TickLength));
            Tick(aim, "Left", new Vector2(-offset, 0f), new Vector2(TickLength, TickWidth));
            Tick(aim, "Right", new Vector2(offset, 0f), new Vector2(TickLength, TickWidth));
            crosshair.crosshair = HudKit.Popup(aim, false);
        }

        /// <summary>A white tick with a dark outline, readable on sky and on bright walls alike.</summary>
        static void Tick(RectTransform parent, string name, Vector2 position, Vector2 size) {
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
