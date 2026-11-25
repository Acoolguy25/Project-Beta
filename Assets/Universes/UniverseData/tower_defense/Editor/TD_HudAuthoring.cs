using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
#if !UNITY_SERVER
using RyanAssets.GameFeel;
using RyanAssets.Retention.Client;
using RyanAssets.Retention.Editor;
using RyanAssets.UI;
using RyanAssets.UI.Editor;
using static RyanAssets.UI.Editor.UIKit;
using Universes.UniverseData.tower_defense.Client;
#endif

namespace Universes.UniverseData.tower_defense.Editor {
    /// <summary>
    /// Generates TowerDefenseHUD.prefab on the shared <see cref="UITheme"/> (UIKit): status bar (cash,
    /// mode and wave, base), mode vote, next-wave preview, fast-forward pill, boss bar, tower shop (with
    /// locked cards), ability pills, focus card, prompt, toasts, wave banner and result (with its star
    /// rating), the nested shared retention HUD (level chip, daily challenges, match summary); plus the
    /// client controller's sounds and effect tuning.
    /// Edit this script, not the prefab, then run Ryan > Tower Defense > Build Client Assets.
    /// </summary>
    static class TD_HudAuthoring {
        const string PrefabPath = TD_Authoring.Root + "/Prefabs/TowerDefenseHUD.prefab";
        const string PortraitFolder = TD_Authoring.Root + "/UI/Portraits";
        const int SortingOrder = 20;
        const float Gap = 12f;
        const float CardRadius = 14f;
        /// <summary>Shop card size: six cards fit between the level chip (bottom left) and the tower card (bottom right) at 1920 wide.</summary>
        static readonly Vector2 CardSize = new(168f, 172f);
        static readonly Vector2 ModeCardSize = new(264f, 292f);
        /// <summary>Top of the retention toasts (canvas units from the top): below the leaderboard with four rows.</summary>
        const float ToastsTop = 260f;
        static readonly Vector2 AbilityPillSize = new(236f, 54f);
        const float InfoHeight = 290f;
        const float StatusTop = -60f;
        /// <summary>Top of the pill row under the status bar (next wave, fast forward, boss).</summary>
        const float PillRowTop = StatusTop - 112f;
        const string KitAudio = "Packages/com.ryan.gameframework/Runtime/GameFeel/Audio/";

        // ---------------------------------------------------------------- assets
        /// <summary>Imports every icon in UI/Icons as a UI sprite.</summary>
        public static void ImportIcons() {
            foreach (string file in Directory.GetFiles(TD_Authoring.IconFolder, "*.png").Concat(Directory.Exists(PortraitFolder) ? Directory.GetFiles(PortraitFolder, "*.png") : new string[0]))
                ImportSprite(file.Replace('\\', '/'));
        }

        static void ImportSprite(string path) {
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            if (importer == null || (importer.textureType == TextureImporterType.Sprite && importer.alphaIsTransparency)) return;
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.SaveAndReimport();
        }

        /// <summary>
        /// Shared theme icons for pictures Tower Defense has no own icon for (UI/Icons holds the
        /// game-icons.net set of the original towers and enemies); see the theme's CREDITS.md.
        /// </summary>
        static readonly Dictionary<string, string> ThemeIcons = new() {
            ["tower_tesla"] = "bolt", ["tower_mortar"] = "my_location",
            ["enemy_shield"] = "shield", ["enemy_medic"] = "favorite", ["enemy_carrier"] = "meeple_army", ["enemy_drone"] = "navigation",
            ["mode_normal"] = "flag", ["mode_hard"] = "local_fire_department", ["mode_endless"] = "hourglass_bottom",
            ["ability_airstrike"] = "jet_fighter", ["ability_emp"] = "power", ["level"] = "military_tech",
            ["lock"] = "lock",
        };

        /// <summary>Tower Defense's own icon <paramref name="name"/>, else its shared theme icon (<see cref="ThemeIcons"/>).</summary>
        public static Sprite IconSprite(string name) {
            var own = AssetDatabase.LoadAssetAtPath<Sprite>($"{TD_Authoring.IconFolder}/{name}.png");
#if !UNITY_SERVER
            if (own == null && ThemeIcons.TryGetValue(name, out string theme)) return UIThemeAssets.Icon(theme);
#endif
            return own;
        }
        public static Texture2D IconTexture(string name) => AssetDatabase.LoadAssetAtPath<Texture2D>($"{TD_Authoring.IconFolder}/{name}.png");
        public static string PortraitPath(string name) => $"{PortraitFolder}/{name}.png";
        static Sprite Portrait(string name) => AssetDatabase.LoadAssetAtPath<Sprite>(PortraitPath(name));

        public static GameObject Build() {
#if UNITY_SERVER
            return AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
#else
            ImportIcons();
            RectTransform root = Canvas("TowerDefenseHUD", SortingOrder);
            var hud = root.gameObject.AddComponent<TD_Hud>();
            var client = root.gameObject.AddComponent<TD_ClientController>();
            hud.towerPortraits = Enumerable.Range(0, TD_Rules.Towers.Length)
                .Select(i => Portrait("tower_" + ((TD_TowerKind)i).ToString().ToLowerInvariant()) ?? IconSprite(TD_Palette.TowerIcon((TD_TowerKind)i))).ToArray();
            hud.enemyPortraits = Enumerable.Range(0, TD_Rules.Enemies.Length)
                .Select(i => Portrait("enemy_" + ((TD_EnemyKind)i).ToString().ToLowerInvariant()) ?? IconSprite(TD_Palette.EnemyIcon((TD_EnemyKind)i))).ToArray();
            hud.timerIcon = IconSprite("timer");
            hud.enemiesIcon = IconSprite("skull");
            hud.coinIcon = IconSprite("coins");
            hud.baseIcon = IconSprite("base");
            hud.readyIcon = IconSprite("ready");
            hud.waveIcon = IconSprite("wave");
            hud.trophyIcon = IconSprite("trophy");
            hud.defeatIcon = IconSprite("defeat");
            hud.upgradeIcon = IconSprite("upgrade");

            StatusBar(root, hud);
            NextWave(root, hud);
            SpeedPill(root, hud);
            BossBar(root, hud);
            Shop(root, hud);
            Info(root, hud);
            Abilities(root, hud);
            PromptPill(root, hud);
            ToastPill(root, hud);
            WaveBanner(root, hud);
            EnemyIntro(root, hud);
            ModeVote(root, hud);
            Result(root, hud);

            client.tuning = Tuning();
            // The shared retention HUD (level chip bottom left, challenges on J, toasts top right, the
            // match summary at the left edge beside the result card). Its level gates the shop and abilities.
            client.retentionHud = RetentionHudAuthoring.AddTo(root);
            // The kit prefab's root is a root canvas saved at scale 0 (Unity drives a root canvas's scale);
            // nested here it is a sub-canvas, so it needs a real scale or none of it is drawn.
            client.retentionHud.transform.localScale = Vector3.one;
            // Its toasts sit under the shared top bar by default, which is where this game's leaderboard
            // (Kills / Built) is: move them below room for four player rows, above the ability pills.
            client.retentionHud.SetPlacement(RetentionHudPart.Toasts, new RetentionPlacement(new Vector2(1f, 1f), new Vector2(-Theme.screenMargin, -ToastsTop)));
            // The challenges panel (J) opens above the level chip, where its right edge ran 10 units into the
            // shop's first card: open it above the shop row instead.
            client.retentionHud.SetPlacement(RetentionHudPart.ChallengesPanel, new RetentionPlacement(Vector2.zero, new Vector2(16f, 18f + CardSize.y + 14f + Gap)));
            client.retentionHud.ApplyPlacement();
            // World popups (kill cash, SHIELD DOWN, BLOCKED) are kept off these panels and the kit's toasts (TD_ScreenGuard).
            hud.keepClear = new[] { "StatusBar", "NextWave", "FastForward", "Boss", "Toast", "WaveBanner" }
                .Select(name => root.Find(name) as RectTransform).Where(rect => rect != null).ToArray();
            Debug.Assert(hud.keepClear.Length == 6, "[TowerDefense] A top-band panel was renamed; TD_Hud.keepClear misses it.");
            return SavePrefab(root.gameObject, PrefabPath);
#endif
        }

#if !UNITY_SERVER
        // ---------------------------------------------------------------- feel
        /// <summary>A sound from asset paths; missing clips are reported, not silently dropped.</summary>
        static SfxSound Sound(float volume, float pitch, float pitchJitterSemitones, float minInterval, params string[] paths) {
            var clips = paths.Select(path => (path, clip: AssetDatabase.LoadAssetAtPath<AudioClip>(path))).ToArray();
            foreach (var missing in clips.Where(c => c.clip == null)) Debug.LogWarning("[TowerDefense] Missing sound " + missing.path);
            return new SfxSound {
                clips = clips.Where(c => c.clip != null).Select(c => c.clip).ToArray(),
                volume = volume, pitch = pitch, pitchJitterSemitones = pitchJitterSemitones, minInterval = minInterval,
            };
        }

        static string[] Numbered(string folder, string stem, int count) => Enumerable.Range(1, count).Select(i => $"{folder}{stem}_{i}.ogg").ToArray();

        /// <summary>Shot sounds per tower (index-aligned with TD_TowerKind) and death blasts per enemy (TD_EnemyKind).</summary>
        static TD_FxTuning Tuning() {
            string game = TD_Authoring.AudioFolder + "/";
            var tuning = new TD_FxTuning {
                towers = new[] {
                    // Gunner: quick light pops; several towers fire 5 times a second, so it stays quiet.
                    new TD_FxTuning.TowerFx { fire = Sound(0.22f, 1.2f, 1f, 0.06f, TD_Authoring.FireClip), impact = new SfxSound(), muzzleSparks = 0.22f },
                    // Cannon: heavy metal thump out, small blast where the shell lands.
                    new TD_FxTuning.TowerFx { fire = Sound(0.6f, 0.85f, 1f, 0.08f, Numbered(game, "cannon", 3)), impact = Sound(0.45f, 1.1f, 1.5f, 0.1f, Numbered(KitAudio, "explosion", 5)), muzzleSparks = 0.45f },
                    // Frost: a soft laser zap.
                    new TD_FxTuning.TowerFx { fire = Sound(0.28f, 1.3f, 1f, 0.1f, Numbered(game, "frost", 3)), impact = new SfxSound(), muzzleSparks = 0.3f },
                    // Sniper: a deep crack and a solid hit.
                    new TD_FxTuning.TowerFx { fire = Sound(0.6f, 0.7f, 0.5f, 0.12f, TD_Authoring.FireClip), impact = Sound(0.5f, 1f, 1f, 0.1f, Numbered(KitAudio, "hit", 5)), muzzleSparks = 0.6f },
                    // Tesla: the frost zap pitched up into a crackle, a bright tick on every enemy it jumps to.
                    new TD_FxTuning.TowerFx { fire = Sound(0.32f, 1.7f, 2f, 0.08f, Numbered(game, "frost", 3)), impact = Sound(0.22f, 1.6f, 2f, 0.06f, Numbered(KitAudio, "hit", 5)), muzzleSparks = 0.5f },
                    // Mortar: a deep, slow cannon thump out, a heavy blast where the round lands.
                    new TD_FxTuning.TowerFx { fire = Sound(0.65f, 0.6f, 1f, 0.15f, Numbered(game, "cannon", 3)), impact = Sound(0.55f, 0.85f, 1.5f, 0.1f, Numbered(KitAudio, "explosion_large", 2)), muzzleSparks = 0.8f },
                },
                enemies = new[] {
                    // Blasts are sized to the vehicles (3.6-8.5 m), so a kill reads from the top-down camera.
                    new TD_FxTuning.EnemyFx { largeExplosion = false, explosionScale = 1.5f, shake = 0.12f, debrisScale = 1f },   // Scout
                    new TD_FxTuning.EnemyFx { largeExplosion = false, explosionScale = 2f, shake = 0.2f, debrisScale = 1.4f },    // APC
                    new TD_FxTuning.EnemyFx { largeExplosion = true, explosionScale = 1.5f, shake = 0.4f, debrisScale = 2f },     // Tank
                    new TD_FxTuning.EnemyFx { largeExplosion = false, explosionScale = 2.2f, shake = 0.3f, debrisScale = 1.6f },  // Chopper
                    new TD_FxTuning.EnemyFx { largeExplosion = true, explosionScale = 2.6f, shake = 0.9f, debrisScale = 3f },     // Heavy Tank (boss)
                    new TD_FxTuning.EnemyFx { largeExplosion = true, explosionScale = 1.6f, shake = 0.4f, debrisScale = 2f },     // Shield Tank
                    new TD_FxTuning.EnemyFx { largeExplosion = false, explosionScale = 2f, shake = 0.22f, debrisScale = 1.4f },   // Medic
                    new TD_FxTuning.EnemyFx { largeExplosion = true, explosionScale = 2.1f, shake = 0.5f, debrisScale = 2.4f },   // Carrier (its drones fly out of the blast)
                    new TD_FxTuning.EnemyFx { largeExplosion = false, explosionScale = 0.9f, shake = 0.04f, debrisScale = 0.5f }, // Drone (swarms: small and quiet)
                },
                shieldBreakSound = Sound(0.6f, 1.45f, 1f, 0.08f, Numbered(KitAudio, "hit", 5)),
                // A bounce off a shield: the hit sound pitched way up and quiet, a thin "tink" (rate-limited: many Gunners fire 5 times a second).
                ricochetSound = Sound(0.22f, 2.1f, 1.5f, 0.15f, Numbered(KitAudio, "hit", 5)),
                jetSound = Sound(0.9f, 0.75f, 0.5f, 0.3f, Numbered(KitAudio, "whoosh", 2)),
                empSound = Sound(0.8f, 0.55f, 0.5f, 0.3f, Numbered(KitAudio, "whoosh", 2)),
            };
            Debug.Assert(tuning.towers.Length == TD_Rules.Towers.Length && tuning.enemies.Length == TD_Rules.Enemies.Length, "[TowerDefense] Fx tuning arrays must match the tower and enemy kinds.");
            return tuning;
        }

        // ---------------------------------------------------------------- pieces
        static Image TdIcon(Transform parent, string icon, float size, Color color) {
            RectTransform rect = Node(parent, "Icon");
            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = IconSprite(icon);
            image.preserveAspect = true;
            image.raycastTarget = false;
            image.color = color;
            Size(image, size, size);
            return image;
        }

        static RectTransform Panel(Transform parent, string name, Vector2 anchor, Vector2 position, Vector2 size, UIColorRole role = UIColorRole.Surface, float radius = CardRadius) {
            RectTransform rect = Pinned(parent, name, anchor, position, size);
            Shadow(rect, 22f, 6f, 0.35f);
            Rounded(rect, role, radius, raycast: false);
            return rect;
        }

        /// <summary>A rounded track with a fill sized by its right anchor (see TD_Hud.Bar).</summary>
        static Image Track(Transform parent, string name, float height, Color fillColor) {
            RectTransform track = Node(parent, name);
            Size(track, -1f, height, 1f);
            Rounded(track, UIColorRole.SurfaceInset, height * 0.5f, raycast: false);
            RectTransform fill = Fill(track, "Fill");
            fill.anchorMax = new Vector2(1f, 1f);
            var image = fill.gameObject.AddComponent<Image>();
            image.sprite = Theme.roundedSprite;
            image.type = Image.Type.Sliced;
            image.pixelsPerUnitMultiplier = UIThemeAssets.RoundedSpriteRadius / Mathf.Max(0.5f, height * 0.5f);
            image.raycastTarget = false;
            image.color = fillColor;
            return image;
        }

        static RectTransform Group(Transform parent, string name, bool vertical, float spacing, TextAnchor align, float width = -1f) {
            RectTransform rect = Node(parent, name);
            if (vertical) Column(rect.gameObject, spacing, null, align); else Row(rect.gameObject, spacing, null, align);
            if (width >= 0f) Size(rect, width, -1f);
            return rect;
        }

        /// <summary>
        /// UIKit text that never truncates: these labels sit in tight layout rows, and TMP's ellipsis
        /// (missing from the Inter atlas) falls back to Truncate, which hides a line that is a pixel too tall.
        /// </summary>
        static TextMeshProUGUI Label(Transform parent, string name, string text, UIText style, UIColorRole role = UIColorRole.TextPrimary, TextAlignmentOptions align = TextAlignmentOptions.MidlineLeft) {
            var label = Text(parent, name, text, style, role, align);
            label.overflowMode = TextOverflowModes.Overflow;
            return label;
        }

        static TextMeshProUGUI Overlay(TextMeshProUGUI text) {
            text.fontSharedMaterial = UIThemeAssets.ShadowMaterial(text.font);
            return text;
        }

        // ---------------------------------------------------------------- status bar
        static void StatusBar(RectTransform root, TD_Hud hud) {
            RectTransform bar = Panel(root, "StatusBar", new Vector2(0.5f, 1f), new Vector2(0f, StatusTop), new Vector2(780f, 100f));
            Row(bar.gameObject, 18f, Pad(20, 12), TextAnchor.MiddleCenter);

            // Cash
            RectTransform cash = Group(bar, "Cash", false, 12f, TextAnchor.MiddleLeft, 200f);
            Image coin = TdIcon(cash, "coins", 40f, Theme.currency);
            RectTransform cashText = Group(cash, "Text", true, 0f, TextAnchor.MiddleLeft);
            Label(cashText, "Caption", "Cash", UIText.Caption, UIColorRole.TextMuted);
            hud.cashLabel = Label(cashText, "Value", "$0", UIText.Title, UIColorRole.Currency);
            hud.cashLabel.fontSize = 28f;
            hud.cashPunch = coin.rectTransform;
            Separator(bar);

            // Wave
            RectTransform wave = Group(bar, "Wave", true, 2f, TextAnchor.MiddleCenter, 260f);
            hud.modeLabel = Label(wave, "Mode", "", UIText.Caption, UIColorRole.TextMuted, TextAlignmentOptions.Center);
            Object.DestroyImmediate(hud.modeLabel.GetComponent<UIThemeColor>()); // the mode's colour, set at runtime
            hud.modeLabel.fontSize = 11f;
            hud.modeLabel.characterSpacing = 3f;
            hud.waveTitle = Label(wave, "Title", "TOWER DEFENSE", UIText.Title, UIColorRole.TextPrimary, TextAlignmentOptions.Center);
            hud.waveTitle.fontSize = 26f;
            RectTransform status = Group(wave, "Status", false, 6f, TextAnchor.MiddleCenter);
            hud.waveStatusIcon = TdIcon(status, "timer", 16f, Theme.textSecondary);
            hud.waveStatus = Label(status, "Text", "", UIText.Small, UIColorRole.TextSecondary, TextAlignmentOptions.Center);
            hud.waveStatus.fontSize = 14f;
            hud.waveProgress = Track(wave, "Progress", 6f, Theme.accent);
            Separator(bar);

            // Base
            RectTransform castle = Group(bar, "Base", false, 12f, TextAnchor.MiddleLeft, 200f);
            Image icon = TdIcon(castle, "base", 40f, Theme.textPrimary);
            hud.basePunch = icon.rectTransform;
            hud.baseIconImage = icon;
            RectTransform baseText = Group(castle, "Text", true, 4f, TextAnchor.MiddleLeft);
            Size(baseText, 148f, -1f);
            RectTransform head = Group(baseText, "Head", false, 6f, TextAnchor.MiddleLeft);
            Label(head, "Caption", "Base", UIText.Caption, UIColorRole.TextMuted);
            hud.baseLabel = Label(head, "Value", "100", UIText.Strong, UIColorRole.TextPrimary, TextAlignmentOptions.MidlineRight);
            Size(hud.baseLabel, -1f, -1f, 1f);
            hud.baseFill = Track(baseText, "Health", 10f, TD_Palette.HealthHigh);
        }

        static void Separator(Transform parent) {
            RectTransform rect = Node(parent, "Separator");
            Size(rect, 1f, 48f);
            Paint(rect.gameObject.AddComponent<Image>(), UIColorRole.Stroke).raycastTarget = false;
        }

        // ---------------------------------------------------------------- next wave
        static void NextWave(RectTransform root, TD_Hud hud) {
            RectTransform panel = Pinned(root, "NextWave", new Vector2(0.5f, 1f), new Vector2(0f, PillRowTop), new Vector2(0f, 48f));
            Rounded(panel, UIColorRole.Overlay, 24f, raycast: false);
            Row(panel.gameObject, 10f, Pad(16, 6), TextAnchor.MiddleCenter);
            FitContent(panel.gameObject, true, false);
            panel.gameObject.AddComponent<CanvasGroup>();
            hud.nextWave = panel.gameObject.AddComponent<TD_Fader>();

            TdIcon(panel, "wave", 18f, Theme.textSecondary);
            Label(panel, "Caption", "Next wave", UIText.Caption, UIColorRole.TextSecondary);
            hud.enemyChips = new TD_EnemyChip[TD_Rules.Enemies.Length];
            for (int i = 0; i < hud.enemyChips.Length; i++) {
                var kind = (TD_EnemyKind)i;
                RectTransform chip = Node(panel, TD_Rules.Enemy(kind).name);
                Rounded(chip, UIColorRole.SurfaceInset, 16f, raycast: false);
                Row(chip.gameObject, 4f, Pad(6, 2), TextAnchor.MiddleLeft);
                Size(chip, -1f, 36f, 0f);
                var portrait = Portrait("enemy_" + kind.ToString().ToLowerInvariant());
                Image image = TdIcon(chip, TD_Palette.EnemyIcon(kind), 32f, portrait != null ? Color.white : TD_Palette.Enemy(kind));
                if (portrait != null) image.sprite = portrait;
                var count = Label(chip, "Count", "×0", UIText.Strong, UIColorRole.TextPrimary);
                // "NEW  Shielded": only on the wave that introduces the kind (TD_Hud.ShowNextWave).
                var trait = Label(chip, "Trait", "NEW", UIText.Caption, UIColorRole.TextPrimary);
                trait.fontSize = 12f;
                trait.gameObject.SetActive(false);
                hud.enemyChips[i] = new TD_EnemyChip { root = chip.gameObject, icon = image, count = count, trait = trait };
            }
            RectTransform ready = Node(panel, "Ready");
            hud.readyBack = Rounded(ready, UIColorRole.SurfaceHover, 16f, raycast: false);
            Object.DestroyImmediate(ready.GetComponent<UIThemeColor>()); // coloured by state at runtime
            Row(ready.gameObject, 6f, Pad(12, 2), TextAnchor.MiddleCenter);
            Size(ready, -1f, 36f, 0f);
            TdIcon(ready, "ready", 16f, Color.white);
            hud.readyLabel = Label(ready, "Label", "[R]  Start now", UIText.Strong, UIColorRole.TextPrimary);
        }

        // ---------------------------------------------------------------- fast forward (shown during waves, where the next-wave preview sits in build phases)
        static void SpeedPill(RectTransform root, TD_Hud hud) {
            RectTransform panel = Pinned(root, "FastForward", new Vector2(0.5f, 1f), new Vector2(0f, PillRowTop), new Vector2(0f, 48f));
            Rounded(panel, UIColorRole.Overlay, 24f, raycast: false);
            Row(panel.gameObject, 10f, Pad(6, 6), TextAnchor.MiddleCenter);
            FitContent(panel.gameObject, true, false);
            panel.gameObject.AddComponent<CanvasGroup>();
            hud.speedPill = panel.gameObject.AddComponent<TD_Fader>();

            RectTransform back = Node(panel, "Back");
            hud.speedBack = Rounded(back, UIColorRole.SurfaceHover, 18f, raycast: false);
            Object.DestroyImmediate(back.GetComponent<UIThemeColor>()); // coloured by state at runtime
            Row(back.gameObject, 6f, Pad(12, 2), TextAnchor.MiddleCenter);
            Size(back, -1f, 36f, 0f);
            TdIcon(back, "rate", 16f, Color.white);
            hud.speedLabel = Label(back, "Label", "[G]  Fast forward", UIText.Strong, UIColorRole.TextPrimary);
        }

        // ---------------------------------------------------------------- boss bar
        /// <summary>
        /// A pill in the row right under the status bar, left of centre; the fast-forward pill steps to
        /// its right while it shows (TD_Hud.ShowBoss).
        /// </summary>
        static void BossBar(RectTransform root, TD_Hud hud) {
            const float width = 360f;
            RectTransform holder = Pinned(root, "Boss", new Vector2(0.5f, 1f), new Vector2(0f, PillRowTop), new Vector2(width, 48f));
            holder.pivot = new Vector2(1f, 1f);
            holder.anchoredPosition = new Vector2(-hud.bossPillGap, PillRowTop);
            holder.gameObject.AddComponent<CanvasGroup>();
            hud.bossPanel = holder.gameObject.AddComponent<TD_Fader>();
            RectTransform panel = Fill(holder, "Card");
            Rounded(panel, UIColorRole.Overlay, 24f, raycast: false);
            Row(panel.gameObject, 8f, Pad(14, 6), TextAnchor.MiddleLeft);
            TdIcon(panel, "enemy_boss", 22f, Theme.danger);
            hud.bossLabel = Label(panel, "Name", "HEAVY TANK", UIText.Strong, UIColorRole.TextPrimary);
            hud.bossLabel.fontSize = 15f;
            hud.bossFill = Track(panel, "Health", 10f, Theme.danger); // takes the rest of the row
        }

        // ---------------------------------------------------------------- shop
        static void Shop(RectTransform root, TD_Hud hud) {
            RectTransform dock = Pinned(root, "Shop", new Vector2(0.5f, 0f), new Vector2(0f, 18f), new Vector2(0f, CardSize.y + 14f));
            hud.shopGroup = dock.gameObject.AddComponent<CanvasGroup>();
            hud.shopGroup.blocksRaycasts = false;
            Row(dock.gameObject, Gap, Pad(0, 0), TextAnchor.LowerCenter);
            FitContent(dock.gameObject, true, false);
            hud.shopCards = new TD_ShopCard[TD_Rules.Towers.Length];
            for (int i = 0; i < hud.shopCards.Length; i++) hud.shopCards[i] = Card(dock, (TD_TowerKind)i, i, hud.towerPortraits[i]);
        }

        static TD_ShopCard Card(Transform parent, TD_TowerKind kind, int index, Sprite portrait) {
            var stats = TD_Rules.Tower(kind);
            Color accent = TD_Palette.Tower(kind);
            RectTransform slot = Node(parent, stats.name);
            Size(slot, CardSize.x, CardSize.y + 14f);
            var card = slot.gameObject.AddComponent<TD_ShopCard>();
            card.group = slot.gameObject.AddComponent<CanvasGroup>();
            card.group.blocksRaycasts = false;

            RectTransform body = Node(slot, "Body");
            body.anchorMin = new Vector2(0f, 0f);
            body.anchorMax = new Vector2(1f, 0f);
            body.pivot = new Vector2(0.5f, 0f);
            body.sizeDelta = new Vector2(0f, CardSize.y);
            card.body = body;
            Shadow(body, 20f, 6f, 0.4f);
            RectTransform outline = Fill(body, "Outline", -3f, -3f, -3f, -3f);
            card.outline = outline.gameObject.AddComponent<Image>();
            card.outline.sprite = Theme.roundedSprite;
            card.outline.type = Image.Type.Sliced;
            card.outline.pixelsPerUnitMultiplier = UIThemeAssets.RoundedSpriteRadius / (CardRadius + 3f);
            card.outline.raycastTarget = false;
            Rounded(Fill(body, "Background"), UIColorRole.Overlay, CardRadius, raycast: false);

            // Picture area tinted with the tower's colour.
            RectTransform art = Fill(body, "Art", 8f, 8f, 8f, 58f);
            Image artBack = Rounded(art, UIColorRole.SurfaceInset, CardRadius - 4f, raycast: false);
            Object.DestroyImmediate(artBack.GetComponent<UIThemeColor>());
            artBack.color = new Color(accent.r, accent.g, accent.b, 0.22f);
            RectTransform picture = Fill(art, "Portrait", 6f, 4f, 6f, 2f);
            card.portrait = picture.gameObject.AddComponent<Image>();
            card.portrait.sprite = portrait;
            card.portrait.preserveAspect = true;
            card.portrait.raycastTarget = false;
            RectTransform key = KeyHint(art, (index + 1).ToString(), UIColorRole.TextPrimary);
            key.anchorMin = key.anchorMax = key.pivot = new Vector2(0f, 1f);
            key.anchoredPosition = new Vector2(6f, -6f);
            key.sizeDelta = new Vector2(24f, 22f);
            Image badge = TdIcon(art, TD_Palette.TowerIcon(kind), 22f, accent);
            badge.rectTransform.anchorMin = badge.rectTransform.anchorMax = badge.rectTransform.pivot = new Vector2(1f, 1f);
            badge.rectTransform.anchoredPosition = new Vector2(-8f, -8f);
            badge.rectTransform.sizeDelta = new Vector2(22f, 22f);

            // Locked: a level badge over the darkened portrait (TD_ShopCard.Set shows it, with the level).
            RectTransform locked = Fill(art, "Locked");
            Image lockBack = Rounded(locked, UIColorRole.Scrim, CardRadius - 4f, 0.55f, raycast: false);
            lockBack.raycastTarget = false;
            Column(locked.gameObject, 2f, null, TextAnchor.MiddleCenter);
            TdIcon(locked, IconSprite("lock") != null ? "lock" : "level", 34f, Theme.textPrimary);
            card.lockLabel = Overlay(Label(locked, "Level", "LV 3", UIText.Title, UIColorRole.TextPrimary, TextAlignmentOptions.Center));
            card.lockLabel.fontSize = 20f;
            card.lockGroup = locked.gameObject;
            locked.gameObject.SetActive(false);

            // Name, price and capabilities.
            RectTransform info = Fill(body, "Info", 14f, CardSize.y - 52f, 14f, 8f);
            Column(info.gameObject, 2f, null, TextAnchor.UpperLeft);
            RectTransform head = Group(info, "Head", false, 6f, TextAnchor.MiddleLeft);
            var title = Label(head, "Name", stats.name, UIText.Title, UIColorRole.TextPrimary);
            title.fontSize = 16f;
            Size(title, -1f, -1f, 1f);
            RectTransform cost = Group(head, "Cost", false, 3f, TextAnchor.MiddleRight);
            card.costGroup = cost.gameObject;
            card.costIcon = TdIcon(cost, "coins", 16f, Theme.currency);
            card.costLabel = Label(cost, "Cost", $"${stats.cost}", UIText.Strong, UIColorRole.Currency, TextAlignmentOptions.MidlineRight);
            Object.DestroyImmediate(card.costLabel.GetComponent<UIThemeColor>());
            RectTransform tags = Group(info, "Stats", false, 10f, TextAnchor.MiddleLeft);
            Stat(tags, "damage", TD_Rules.Damage(kind, 1).ToString("0"));
            Stat(tags, "range", TD_Rules.Range(kind, 1).ToString("0") + "m");
            // The third stat is what sets the tower apart: slow, chain, splash, air or fire rate.
            if (stats.slow > 0f) Stat(tags, "slow", $"{TD_Rules.SlowPercent(kind, 1)}%");
            else if (stats.chain > 0) Stat(tags, "air", $"×{TD_Rules.ChainTargets(kind, 1)}");
            else if (stats.splash > 0f) Stat(tags, "splash", $"{stats.splash:0}m");
            else if (stats.hitsAir) Stat(tags, "air", "Air");
            else Stat(tags, "rate", stats.fireRate.ToString("0.0"));
            return card;
        }

        static void Stat(Transform parent, string icon, string value) {
            RectTransform stat = Group(parent, icon, false, 3f, TextAnchor.MiddleLeft);
            TdIcon(stat, icon, 14f, Theme.textMuted);
            var text = Label(stat, "Value", value, UIText.Small, UIColorRole.TextSecondary);
            text.fontSize = 13f;
        }

        // ---------------------------------------------------------------- focus card
        static void Info(RectTransform root, TD_Hud hud) {
            RectTransform holder = Pinned(root, "TowerInfo", new Vector2(1f, 0f), new Vector2(-18f, 18f), new Vector2(360f, InfoHeight));
            holder.gameObject.AddComponent<CanvasGroup>();
            var info = holder.gameObject.AddComponent<TD_InfoPanel>();
            info.fader = holder.gameObject.AddComponent<TD_Fader>();
            hud.info = info;
            RectTransform panel = Fill(holder, "Card");
            Shadow(panel, 22f, 6f, 0.4f);
            Rounded(panel, UIColorRole.SurfaceRaised, CardRadius, raycast: false);

            RectTransform accent = Node(panel, "Accent");
            accent.anchorMin = new Vector2(0f, 0f);
            accent.anchorMax = new Vector2(0f, 1f);
            accent.pivot = new Vector2(0f, 0.5f);
            accent.offsetMin = new Vector2(0f, 14f);
            accent.offsetMax = new Vector2(4f, -14f);
            info.accentBar = accent.gameObject.AddComponent<Image>();
            info.accentBar.raycastTarget = false;

            RectTransform content = Fill(panel, "Content", 18f, 14f, 16f, 14f);
            Column(content.gameObject, 8f, null, TextAnchor.UpperLeft);

            RectTransform header = Group(content, "Header", false, 12f, TextAnchor.MiddleLeft);
            RectTransform frame = Node(header, "Frame");
            Size(frame, 68f, 68f);
            Rounded(frame, UIColorRole.SurfaceInset, 10f, raycast: false);
            RectTransform picture = Fill(frame, "Portrait", 4f, 4f, 4f, 4f);
            info.portrait = picture.gameObject.AddComponent<Image>();
            info.portrait.preserveAspect = true;
            info.portrait.raycastTarget = false;
            RectTransform titles = Group(header, "Titles", true, 2f, TextAnchor.MiddleLeft);
            Size(titles, -1f, -1f, 1f);
            info.title = Label(titles, "Title", "Gunner", UIText.Title);
            info.subtitle = Label(titles, "Subtitle", "", UIText.Small, UIColorRole.TextSecondary);
            RectTransform stars = Group(titles, "Stars", false, 2f, TextAnchor.MiddleLeft);
            info.stars = new Image[TD_Rules.MaxLevel];
            for (int i = 0; i < info.stars.Length; i++) info.stars[i] = TdIcon(stars, "star", 18f, info.starOn);

            info.tagsLabel = Label(content, "Tags", "", UIText.Small, UIColorRole.TextMuted);
            Divider(content);
            info.damageValue = StatRow(content, "damage", "Damage");
            info.rangeValue = StatRow(content, "range", "Range");
            info.rateValue = StatRow(content, "rate", "Fire rate");
            info.sellLabel = Label(content, "Sell", "", UIText.Small, UIColorRole.TextSecondary);

            RectTransform action = Node(content, "Action");
            Size(action, -1f, 38f, 1f);
            info.actionBack = Rounded(action, UIColorRole.Success, 10f, raycast: false);
            Object.DestroyImmediate(action.GetComponent<UIThemeColor>());
            Row(action.gameObject, 8f, Pad(12, 0), TextAnchor.MiddleCenter);
            KeyHint(action, "E", UIColorRole.TextPrimary);
            info.actionLabel = Label(action, "Label", "Build", UIText.Strong, UIColorRole.TextPrimary, TextAlignmentOptions.Center);
        }

        static TextMeshProUGUI StatRow(Transform parent, string icon, string label) {
            RectTransform row = Group(parent, label, false, 8f, TextAnchor.MiddleLeft);
            Size(row, -1f, 22f);
            TdIcon(row, icon, 16f, Theme.textSecondary);
            var name = Label(row, "Label", label, UIText.Body, UIColorRole.TextSecondary);
            Size(name, -1f, -1f, 1f);
            return Label(row, "Value", "", UIText.Strong, UIColorRole.TextPrimary, TextAlignmentOptions.MidlineRight);
        }

        // ---------------------------------------------------------------- messages
        static void PromptPill(RectTransform root, TD_Hud hud) {
            RectTransform pill = Pinned(root, "Prompt", new Vector2(0.5f, 0f), new Vector2(0f, CardSize.y + 48f), new Vector2(0f, 40f));
            Rounded(pill, UIColorRole.Overlay, 20f, raycast: false);
            Row(pill.gameObject, 8f, Pad(18, 0), TextAnchor.MiddleCenter);
            FitContent(pill.gameObject, true, false);
            pill.gameObject.AddComponent<CanvasGroup>();
            hud.prompt = pill.gameObject.AddComponent<TD_Fader>();
            hud.promptLabel = Label(pill, "Label", "", UIText.Strong, UIColorRole.TextPrimary, TextAlignmentOptions.Center);
        }

        static void ToastPill(RectTransform root, TD_Hud hud) {
            RectTransform pill = Pinned(root, "Toast", new Vector2(0.5f, 1f), new Vector2(0f, StatusTop - 172f), new Vector2(0f, 44f));
            Rounded(pill, UIColorRole.Overlay, 22f, raycast: false);
            Row(pill.gameObject, 10f, Pad(18, 0), TextAnchor.MiddleCenter);
            FitContent(pill.gameObject, true, false);
            pill.gameObject.AddComponent<CanvasGroup>();
            hud.toast = pill.gameObject.AddComponent<TD_Fader>();
            hud.toastIcon = TdIcon(pill, "upgrade", 22f, Theme.accent);
            hud.toastLabel = Label(pill, "Label", "", UIText.Strong, UIColorRole.TextPrimary, TextAlignmentOptions.Center);
            hud.toastLabel.fontSize = 17f;
        }

        static void WaveBanner(RectTransform root, TD_Hud hud) {
            // Upper third, just under the toast row (so the two never touch), small enough to leave the road clear.
            RectTransform banner = Pinned(root, "WaveBanner", new Vector2(0.5f, 1f), new Vector2(0f, StatusTop - 228f), new Vector2(720f, 104f));
            banner.gameObject.AddComponent<CanvasGroup>();
            hud.banner = banner.gameObject.AddComponent<TD_Fader>();
            hud.banner.popScale = 0.8f;
            hud.banner.duration = 0.25f;
            Column(banner.gameObject, 2f, null, TextAnchor.MiddleCenter);
            RectTransform head = Group(banner, "Head", false, 16f, TextAnchor.MiddleCenter);
            hud.bannerIcon = TdIcon(head, "wave", 40f, Theme.warning);
            hud.bannerTitle = Overlay(Label(head, "Title", "WAVE 1", UIText.Display, UIColorRole.TextPrimary, TextAlignmentOptions.Center));
            hud.bannerTitle.fontSize = 52f;
            hud.bannerTitle.characterSpacing = 4f;
            hud.bannerSubtitle = Overlay(Label(banner, "Subtitle", "", UIText.Title, UIColorRole.TextPrimary, TextAlignmentOptions.Center));
        }

        // ---------------------------------------------------------------- new enemies
        /// <summary>
        /// The new-enemy card at the left edge, centred a little high (clear of the shop, the level chip and the
        /// banner; the match summary that also uses this edge only shows after the game): a heading and up to
        /// two rows (wave 6 brings Shield Tanks and Tanks together), each portrait, name and trait, and its counter.
        /// </summary>
        static void EnemyIntro(RectTransform root, TD_Hud hud) {
            const float width = 440f, portrait = 56f;
            // The fader sits on an outer holder with the shadow and the card inside it, so the shadow fades
            // with the card (UIKit.Shadow makes the shadow a sibling of the panel it is given).
            RectTransform outer = Pinned(root, "EnemyIntro", new Vector2(0f, 0.5f), new Vector2(Theme.screenMargin, 70f), new Vector2(width, 0f));
            outer.gameObject.AddComponent<CanvasGroup>().blocksRaycasts = false;
            var intro = outer.gameObject.AddComponent<TD_EnemyIntro>();
            intro.fader = outer.gameObject.AddComponent<TD_Fader>();
            hud.enemyIntro = intro;
            RectTransform holder = Pinned(outer, "Card", new Vector2(0f, 0.5f), Vector2.zero, new Vector2(width, 0f));
            Shadow(holder, 22f, 6f, 0.4f);
            Rounded(holder, UIColorRole.SurfaceRaised, CardRadius, raycast: false);
            Column(holder.gameObject, 10f, Pad(16, 14), TextAnchor.UpperLeft);
            FitContent(holder.gameObject, false, true);

            RectTransform head = Group(holder, "Head", false, 8f, TextAnchor.MiddleLeft);
            TdIcon(head, "skull", 20f, Theme.warning);
            intro.heading = Label(head, "Heading", "NEW ENEMY", UIText.Caption, UIColorRole.Warning);
            intro.heading.characterSpacing = 2f;

            intro.rows = new TD_EnemyIntro.Row[2];
            for (int i = 0; i < intro.rows.Length; i++) {
                RectTransform row = Group(holder, $"Row{i + 1}", false, 12f, TextAnchor.UpperLeft);
                RectTransform frame = Node(row, "Frame");
                Size(frame, portrait, portrait);
                Rounded(frame, UIColorRole.SurfaceInset, 10f, raycast: false);
                RectTransform picture = Fill(frame, "Portrait", 3f, 3f, 3f, 3f);
                var image = picture.gameObject.AddComponent<Image>();
                image.preserveAspect = true;
                image.raycastTarget = false;
                RectTransform text = Group(row, "Text", true, 2f, TextAnchor.UpperLeft);
                var title = Label(text, "Title", "Shield Tank", UIText.Strong, UIColorRole.TextPrimary);
                var counter = Label(text, "Counter", "", UIText.Small, UIColorRole.TextSecondary);
                counter.textWrappingMode = TextWrappingModes.Normal;
                Size(title, width - 32f - portrait - 12f, -1f);
                Size(counter, width - 32f - portrait - 12f, -1f);
                intro.rows[i] = new TD_EnemyIntro.Row { root = row.gameObject, portrait = image, title = title, counter = counter };
            }
        }

        // ---------------------------------------------------------------- abilities
        /// <summary>The ability pills on the right edge, stacked above the tower card (index-aligned with TD_Ability).</summary>
        static void Abilities(RectTransform root, TD_Hud hud) {
            hud.abilityPills = new TD_AbilityPill[TD_Abilities.Count];
            for (int i = 0; i < hud.abilityPills.Length; i++) {
                var ability = (TD_Ability)i;
                float y = 18f + InfoHeight + Gap + i * (AbilityPillSize.y + 8f);
                RectTransform holder = Pinned(root, TD_Abilities.Get(ability).name, new Vector2(1f, 0f), new Vector2(-18f, y), AbilityPillSize);
                var pill = new TD_AbilityPill { group = holder.gameObject.AddComponent<CanvasGroup>() };
                pill.group.blocksRaycasts = false;
                RectTransform body = Fill(holder, "Body");
                pill.punch = body;
                Shadow(body, 18f, 5f, 0.35f);
                pill.back = Rounded(body, UIColorRole.Overlay, AbilityPillSize.y * 0.5f, raycast: false);
                Object.DestroyImmediate(body.GetComponent<UIThemeColor>()); // coloured by state at runtime
                Row(body.gameObject, 10f, Pad(14, 6), TextAnchor.MiddleLeft);
                RectTransform key = KeyHint(body, i == 0 ? "F" : "V", UIColorRole.TextPrimary);
                pill.keyLabel = key.GetComponentInChildren<TextMeshProUGUI>();
                pill.icon = TdIcon(body, TD_Palette.AbilityIcon(ability), 28f, TD_Palette.Ability(ability));
                RectTransform text = Group(body, "Text", true, 0f, TextAnchor.MiddleLeft);
                Size(text, -1f, -1f, 1f);
                pill.nameLabel = Label(text, "Name", TD_Abilities.Get(ability).name, UIText.Strong, UIColorRole.TextPrimary);
                pill.stateLabel = Label(text, "State", "", UIText.Small, UIColorRole.TextSecondary);
                pill.stateLabel.fontSize = 13f;
                hud.abilityPills[i] = pill;
            }
        }

        // ---------------------------------------------------------------- mode vote
        /// <summary>The vote before wave 1: a title with the countdown, one card per mode (keys 1-3) and a hint line.</summary>
        static void ModeVote(RectTransform root, TD_Hud hud) {
            float width = TD_Modes.Count * ModeCardSize.x + (TD_Modes.Count - 1) * 16f;
            RectTransform panel = Pinned(root, "ModeVote", new Vector2(0.5f, 0.5f), new Vector2(0f, 30f), new Vector2(width, ModeCardSize.y + 150f));
            panel.gameObject.AddComponent<CanvasGroup>();
            hud.votePanel = panel.gameObject.AddComponent<TD_Fader>();
            hud.votePanel.popScale = 0.9f;
            Column(panel.gameObject, 12f, null, TextAnchor.UpperCenter);
            hud.voteTitle = Overlay(Label(panel, "Title", "CHOOSE A MODE", UIText.Display, UIColorRole.TextPrimary, TextAlignmentOptions.Center));
            hud.voteTitle.fontSize = 38f;
            hud.voteTitle.characterSpacing = 3f;
            // What today's modifier does, under the title, so the vote is made knowing it.
            hud.voteToday = Overlay(Label(panel, "Today", "", UIText.Body, UIColorRole.Warning, TextAlignmentOptions.Center));
            hud.voteToday.fontSize = 18f;
            RectTransform row = Group(panel, "Modes", false, 16f, TextAnchor.LowerCenter);
            Size(row, width, ModeCardSize.y + 14f);
            hud.modeCards = new TD_ModeCard[TD_Modes.Count];
            for (int i = 0; i < hud.modeCards.Length; i++) hud.modeCards[i] = ModeCard(row, (TD_Difficulty)i, i);
            hud.voteHint = Overlay(Label(panel, "Hint", "Press 1-3 to vote.", UIText.Body, UIColorRole.TextPrimary, TextAlignmentOptions.Center));
            hud.voteHint.fontSize = 17f;
        }

        static TD_ModeCard ModeCard(Transform parent, TD_Difficulty mode, int index) {
            var stats = TD_Modes.Get(mode);
            Color accent = TD_Palette.Mode(mode);
            RectTransform slot = Node(parent, stats.name);
            Size(slot, ModeCardSize.x, ModeCardSize.y + 14f);
            var card = slot.gameObject.AddComponent<TD_ModeCard>();
            card.group = slot.gameObject.AddComponent<CanvasGroup>();
            card.group.blocksRaycasts = false;

            RectTransform body = Node(slot, "Body");
            body.anchorMin = new Vector2(0f, 0f);
            body.anchorMax = new Vector2(1f, 0f);
            body.pivot = new Vector2(0.5f, 0f);
            body.sizeDelta = new Vector2(0f, ModeCardSize.y);
            card.body = body;
            Shadow(body, 22f, 6f, 0.45f);
            RectTransform outline = Fill(body, "Outline", -3f, -3f, -3f, -3f);
            card.outline = outline.gameObject.AddComponent<Image>();
            card.outline.sprite = Theme.roundedSprite;
            card.outline.type = Image.Type.Sliced;
            card.outline.pixelsPerUnitMultiplier = UIThemeAssets.RoundedSpriteRadius / (CardRadius + 3f);
            card.outline.raycastTarget = false;
            Rounded(Fill(body, "Background"), UIColorRole.SurfaceRaised, CardRadius, raycast: false);
            RectTransform bar = Node(body, "Accent");
            bar.anchorMin = new Vector2(0f, 1f);
            bar.anchorMax = new Vector2(1f, 1f);
            bar.pivot = new Vector2(0.5f, 1f);
            bar.offsetMin = new Vector2(18f, -5f);
            bar.offsetMax = new Vector2(-18f, 0f);
            card.accentBar = bar.gameObject.AddComponent<Image>();
            card.accentBar.raycastTarget = false;
            card.accentBar.color = accent;

            RectTransform content = Fill(body, "Content", 20f, 22f, 20f, 18f);
            Column(content.gameObject, 8f, null, TextAnchor.UpperCenter);
            RectTransform head = Group(content, "Head", false, 10f, TextAnchor.MiddleCenter);
            RectTransform key = KeyHint(head, (index + 1).ToString(), UIColorRole.TextPrimary);
            card.keyLabel = key.GetComponentInChildren<TextMeshProUGUI>();
            card.icon = TdIcon(head, TD_Palette.ModeIcon(mode), 40f, accent);
            card.title = Label(content, "Title", stats.name.ToUpperInvariant(), UIText.Title, UIColorRole.TextPrimary, TextAlignmentOptions.Center);
            card.title.fontSize = 26f;
            card.title.characterSpacing = 2f;
            card.blurb = Label(content, "Blurb", stats.blurb, UIText.Small, UIColorRole.TextSecondary, TextAlignmentOptions.Center);
            card.blurb.textWrappingMode = TextWrappingModes.Normal;
            card.blurb.fontSize = 14f;
            Size(card.blurb, ModeCardSize.x - 40f, 58f);
            RectTransform starRow = Group(content, "Stars", false, 6f, TextAnchor.MiddleCenter);
            card.stars = new Image[TD_Rules.MaxStars];
            for (int i = 0; i < card.stars.Length; i++) card.stars[i] = TdIcon(starRow, "star", 26f, card.starOff);
            card.best = Label(content, "Best", "", UIText.Small, UIColorRole.TextSecondary, TextAlignmentOptions.Center);
            card.votes = Label(content, "Votes", "", UIText.Strong, UIColorRole.TextPrimary, TextAlignmentOptions.Center);

            // The lock sits where the blurb, stars and votes were (all hidden while locked), under the
            // dimmed title, so nothing overlaps.
            RectTransform locked = Fill(body, "Locked");
            locked.anchorMax = new Vector2(1f, 0.6f);
            Column(locked.gameObject, 6f, Pad(18, 0), TextAnchor.MiddleCenter);
            TdIcon(locked, IconSprite("lock") != null ? "lock" : "level", 44f, Theme.textPrimary);
            card.lockLabel = Overlay(Label(locked, "Hint", stats.lockedHint, UIText.Strong, UIColorRole.TextPrimary, TextAlignmentOptions.Center));
            card.lockLabel.textWrappingMode = TextWrappingModes.Normal;
            Size(card.lockLabel, ModeCardSize.x - 40f, -1f);
            card.lockGroup = locked.gameObject;
            locked.gameObject.SetActive(false);
            return card;
        }

        static void Result(RectTransform root, TD_Hud hud) {
            RectTransform scrimRect = Fill(root, "Result");
            var scrim = scrimRect.gameObject.AddComponent<Image>();
            scrim.raycastTarget = false;
            Paint(scrim, UIColorRole.Scrim, 0.6f);
            scrimRect.gameObject.AddComponent<CanvasGroup>();
            hud.result = scrimRect.gameObject.AddComponent<TD_Fader>();

            RectTransform card = Panel(scrimRect, "Card", new Vector2(0.5f, 0.5f), new Vector2(0f, 40f), new Vector2(560f, 410f), UIColorRole.SurfaceRaised, 18f);
            Column(card.gameObject, 10f, Pad(32, 26), TextAnchor.MiddleCenter);
            hud.resultMode = Label(card, "Mode", "NORMAL", UIText.Caption, UIColorRole.TextMuted, TextAlignmentOptions.Center);
            Object.DestroyImmediate(hud.resultMode.GetComponent<UIThemeColor>()); // the mode's colour, set at runtime
            hud.resultMode.characterSpacing = 4f;
            hud.resultIcon = TdIcon(card, "trophy", 76f, Theme.currency);
            hud.resultTitle = Label(card, "Title", "VICTORY", UIText.Display, UIColorRole.TextPrimary, TextAlignmentOptions.Center);
            hud.resultTitle.fontSize = 44f;
            RectTransform stars = Group(card, "Stars", false, 10f, TextAnchor.MiddleCenter);
            hud.resultStars = new Image[TD_Rules.MaxStars];
            for (int i = 0; i < hud.resultStars.Length; i++) hud.resultStars[i] = TdIcon(stars, "star", 46f, hud.starOff);
            hud.resultSubtitle = Label(card, "Subtitle", "", UIText.Body, UIColorRole.TextSecondary, TextAlignmentOptions.Center);
            hud.resultSubtitle.fontSize = 18f;
            hud.resultRecord = Label(card, "Record", "", UIText.Small, UIColorRole.TextSecondary, TextAlignmentOptions.Center);
            hud.resultCountdown = Label(card, "Countdown", "", UIText.Caption, UIColorRole.TextMuted, TextAlignmentOptions.Center);
        }
#endif
    }
}
