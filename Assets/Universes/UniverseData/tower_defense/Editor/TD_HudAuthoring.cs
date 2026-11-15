using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
#if !UNITY_SERVER
using RyanAssets.UI;
using RyanAssets.UI.Editor;
using static RyanAssets.UI.Editor.UIKit;
using Universes.UniverseData.tower_defense.Client;
#endif

namespace Universes.UniverseData.tower_defense.Editor {
    /// <summary>
    /// Generates TowerDefenseHUD.prefab on the shared <see cref="UITheme"/> (UIKit): status bar (cash,
    /// wave, base), next-wave preview, tower shop, focus card, prompt, toasts, wave banner and result.
    /// Edit this script, not the prefab, then run Ryan > Tower Defense > Build Client Assets.
    /// </summary>
    static class TD_HudAuthoring {
        const string PrefabPath = TD_Authoring.Root + "/Prefabs/TowerDefenseHUD.prefab";
        const string PortraitFolder = TD_Authoring.Root + "/UI/Portraits";
        const int SortingOrder = 20;
        const float Gap = 12f;
        const float CardRadius = 14f;
        static readonly Vector2 CardSize = new(200f, 196f);
        const float StatusTop = -60f;

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

        public static Sprite IconSprite(string name) => AssetDatabase.LoadAssetAtPath<Sprite>($"{TD_Authoring.IconFolder}/{name}.png");
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
            Shop(root, hud);
            Info(root, hud);
            PromptPill(root, hud);
            ToastPill(root, hud);
            WaveBanner(root, hud);
            Result(root, hud);

            client.sfx = UniverseSfx(root);
            client.buildClip = client.waveClip = RyanAssets.Authoring.UniverseAuthoringKit.Clip(RyanAssets.Authoring.UniverseAuthoringKit.CoinClip);
            client.deniedClip = client.leakClip = RyanAssets.Authoring.UniverseAuthoringKit.Clip(RyanAssets.Authoring.UniverseAuthoringKit.LandClip);
            return SavePrefab(root.gameObject, PrefabPath);
#endif
        }

#if !UNITY_SERVER
        static AudioSource UniverseSfx(Transform parent) => RyanAssets.Authoring.UniverseAuthoringKit.Sfx(parent);

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
            RectTransform wave = Group(bar, "Wave", true, 4f, TextAnchor.MiddleCenter, 260f);
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
            RectTransform panel = Pinned(root, "NextWave", new Vector2(0.5f, 1f), new Vector2(0f, StatusTop - 112f), new Vector2(0f, 48f));
            Rounded(panel, UIColorRole.Surface, 24f, raycast: false);
            Row(panel.gameObject, 10f, Pad(16, 6), TextAnchor.MiddleCenter);
            FitContent(panel.gameObject, true, false);
            hud.nextWave = panel.gameObject.AddComponent<TD_Fader>();
            panel.gameObject.AddComponent<CanvasGroup>();

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
                hud.enemyChips[i] = new TD_EnemyChip { root = chip.gameObject, icon = image, count = count };
            }
            RectTransform ready = Node(panel, "Ready");
            hud.readyBack = Rounded(ready, UIColorRole.SurfaceHover, 16f, raycast: false);
            Object.DestroyImmediate(ready.GetComponent<UIThemeColor>()); // coloured by state at runtime
            Row(ready.gameObject, 6f, Pad(12, 2), TextAnchor.MiddleCenter);
            Size(ready, -1f, 36f, 0f);
            TdIcon(ready, "ready", 16f, Color.white);
            hud.readyLabel = Label(ready, "Label", "[R]  Start now", UIText.Strong, UIColorRole.TextPrimary);
        }

        // ---------------------------------------------------------------- shop
        static void Shop(RectTransform root, TD_Hud hud) {
            RectTransform dock = Pinned(root, "Shop", new Vector2(0.5f, 0f), new Vector2(0f, 18f), new Vector2(0f, CardSize.y + 14f));
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
            Rounded(Fill(body, "Background"), UIColorRole.SurfaceRaised, CardRadius, raycast: false);

            // Picture area tinted with the tower's colour.
            RectTransform art = Fill(body, "Art", 8f, 8f, 8f, 82f);
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

            // Name, price and capabilities.
            RectTransform info = Fill(body, "Info", 14f, CardSize.y - 80f, 14f, 10f);
            Column(info.gameObject, 2f, null, TextAnchor.UpperLeft);
            RectTransform head = Group(info, "Head", false, 6f, TextAnchor.MiddleLeft);
            var title = Label(head, "Name", stats.name, UIText.Title, UIColorRole.TextPrimary);
            title.fontSize = 19f;
            Size(title, -1f, -1f, 1f);
            card.costIcon = TdIcon(head, "coins", 16f, Theme.currency);
            card.costLabel = Label(head, "Cost", $"${stats.cost}", UIText.Strong, UIColorRole.Currency, TextAlignmentOptions.MidlineRight);
            Object.DestroyImmediate(card.costLabel.GetComponent<UIThemeColor>());
            var blurb = Label(info, "Blurb", stats.blurb, UIText.Small, UIColorRole.TextSecondary);
            blurb.enableAutoSizing = true;
            blurb.fontSizeMin = 10f;
            blurb.fontSizeMax = 13f;
            RectTransform tags = Group(info, "Stats", false, 10f, TextAnchor.MiddleLeft);
            Stat(tags, "damage", TD_Rules.Damage(kind, 1).ToString("0"));
            Stat(tags, "range", TD_Rules.Range(kind, 1).ToString("0") + "m");
            Stat(tags, kind == TD_TowerKind.Frost ? "slow" : stats.splash > 0f ? "splash" : stats.hitsAir ? "air" : "rate",
                kind == TD_TowerKind.Frost ? $"{stats.slow * 100f:0}%" : stats.splash > 0f ? $"{stats.splash:0}m" : stats.hitsAir ? "Air" : stats.fireRate.ToString("0.0"));
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
            RectTransform holder = Pinned(root, "TowerInfo", new Vector2(1f, 0f), new Vector2(-18f, 18f), new Vector2(360f, 262f));
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
            Rounded(pill, UIColorRole.Surface, 20f, raycast: false);
            Row(pill.gameObject, 8f, Pad(18, 0), TextAnchor.MiddleCenter);
            FitContent(pill.gameObject, true, false);
            pill.gameObject.AddComponent<CanvasGroup>();
            hud.prompt = pill.gameObject.AddComponent<TD_Fader>();
            hud.promptLabel = Label(pill, "Label", "", UIText.Strong, UIColorRole.TextPrimary, TextAlignmentOptions.Center);
        }

        static void ToastPill(RectTransform root, TD_Hud hud) {
            RectTransform pill = Pinned(root, "Toast", new Vector2(0.5f, 1f), new Vector2(0f, StatusTop - 172f), new Vector2(0f, 44f));
            Rounded(pill, UIColorRole.SurfaceRaised, 22f, raycast: false);
            Row(pill.gameObject, 10f, Pad(18, 0), TextAnchor.MiddleCenter);
            FitContent(pill.gameObject, true, false);
            pill.gameObject.AddComponent<CanvasGroup>();
            hud.toast = pill.gameObject.AddComponent<TD_Fader>();
            hud.toastIcon = TdIcon(pill, "upgrade", 22f, Theme.accent);
            hud.toastLabel = Label(pill, "Label", "", UIText.Strong, UIColorRole.TextPrimary, TextAlignmentOptions.Center);
            hud.toastLabel.fontSize = 17f;
        }

        static void WaveBanner(RectTransform root, TD_Hud hud) {
            RectTransform banner = Pinned(root, "WaveBanner", new Vector2(0.5f, 0.5f), new Vector2(0f, 150f), new Vector2(900f, 150f));
            banner.gameObject.AddComponent<CanvasGroup>();
            hud.banner = banner.gameObject.AddComponent<TD_Fader>();
            hud.banner.popScale = 0.8f;
            hud.banner.duration = 0.25f;
            Column(banner.gameObject, 2f, null, TextAnchor.MiddleCenter);
            RectTransform head = Group(banner, "Head", false, 16f, TextAnchor.MiddleCenter);
            TdIcon(head, "wave", 56f, Theme.warning);
            hud.bannerTitle = Overlay(Label(head, "Title", "WAVE 1", UIText.Display, UIColorRole.TextPrimary, TextAlignmentOptions.Center));
            hud.bannerTitle.fontSize = 76f;
            hud.bannerTitle.characterSpacing = 4f;
            hud.bannerSubtitle = Overlay(Label(banner, "Subtitle", "", UIText.Title, UIColorRole.TextPrimary, TextAlignmentOptions.Center));
        }

        static void Result(RectTransform root, TD_Hud hud) {
            RectTransform scrimRect = Fill(root, "Result");
            var scrim = scrimRect.gameObject.AddComponent<Image>();
            scrim.raycastTarget = false;
            Paint(scrim, UIColorRole.Scrim, 0.6f);
            scrimRect.gameObject.AddComponent<CanvasGroup>();
            hud.result = scrimRect.gameObject.AddComponent<TD_Fader>();

            RectTransform card = Panel(scrimRect, "Card", new Vector2(0.5f, 0.5f), new Vector2(0f, 40f), new Vector2(560f, 300f), UIColorRole.SurfaceRaised, 18f);
            Column(card.gameObject, 10f, Pad(32, 28), TextAnchor.MiddleCenter);
            hud.resultIcon = TdIcon(card, "trophy", 84f, Theme.currency);
            hud.resultTitle = Label(card, "Title", "VICTORY", UIText.Display, UIColorRole.TextPrimary, TextAlignmentOptions.Center);
            hud.resultTitle.fontSize = 44f;
            hud.resultSubtitle = Label(card, "Subtitle", "", UIText.Body, UIColorRole.TextSecondary, TextAlignmentOptions.Center);
            hud.resultSubtitle.fontSize = 18f;
            hud.resultCountdown = Label(card, "Countdown", "", UIText.Caption, UIColorRole.TextMuted, TextAlignmentOptions.Center);
        }
#endif
    }
}
