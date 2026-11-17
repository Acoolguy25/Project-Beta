using System.IO;
using System.Linq;
using RyanAssets.Authoring;
#if !UNITY_SERVER
using RyanAssets.GameFeel;
using RyanAssets.UI;
using RyanAssets.UI.Editor;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Universes.UniverseData.tycoon.Client;

namespace Universes.UniverseData.tycoon.Editor {
    /// <summary>
    /// Generates the TycoonHUD prefab from the shared <see cref="UITheme"/> (same fonts, surfaces and
    /// shapes as the shared HUD) plus the tycoon's own icons, lane colours and item pictures.
    /// <para>
    /// Layout: wallet card bottom-left (cash, income, collector gauge, perk shop button), build plan
    /// bottom-right (one card per lane with the next item's picture and price progress), hint pill
    /// bottom-centre, toasts top-centre and the rebirth perk shop as a centred card.
    /// </para>
    /// </summary>
    public static class TY_HudAuthoring {
        const string IconFolder = TY_Authoring.Root + "/UI/Icons";
        const string SignSpritePath = TY_Authoring.Root + "/UI/SignBack.png";
        const string WhiteSpritePath = TY_Authoring.Root + "/UI/White.png";
        const string GlowSpritePath = TY_Authoring.Root + "/UI/Glow.png";
        const string PrefabPath = TY_Authoring.Root + "/Prefabs/TycoonHUD.prefab";

        static readonly Color Pink = new(1f, 0.42f, 0.84f);
        static readonly Color Rush = new(1f, 0.62f, 0.15f);
        const string Kenney = "Assets/ThirdParty/Kenney/Sounds";

        // ---------------------------------------------------------------- sprites
        /// <summary>Imports the tycoon's Material icons (white, tinted at runtime) as sprites.</summary>
        public static void ImportIcons() {
            foreach (string path in Directory.GetFiles(IconFolder, "icon_*.png")) ImportSprite(path.Replace('\\', '/'), Vector4.zero, 100f);
            if (!File.Exists(SignSpritePath)) WriteRounded(SignSpritePath, 128, 28);
            if (!File.Exists(WhiteSpritePath)) WriteRounded(WhiteSpritePath, 8, 0);
            if (!File.Exists(GlowSpritePath)) WriteGlow(GlowSpritePath, 64);
            ImportSprite(SignSpritePath, new Vector4(32, 32, 32, 32), 128f, fullRect: true);
            ImportSprite(WhiteSpritePath, Vector4.zero, 8f);
            ImportSprite(GlowSpritePath, Vector4.zero, 64f, fullRect: true);
        }

        static void ImportSprite(string path, Vector4 border, float pixelsPerUnit, bool fullRect = false) {
            AssetDatabase.ImportAsset(path);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            if (importer == null) throw new FileNotFoundException("Sprite missing: " + path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = true;
            importer.spritePixelsPerUnit = pixelsPerUnit;
            importer.spriteBorder = border;
            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteMeshType = fullRect ? SpriteMeshType.FullRect : SpriteMeshType.Tight;
            importer.SetTextureSettings(settings);
            importer.SaveAndReimport();
        }

        static void WriteRounded(string path, int size, int radius) {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++) {
                    float dx = Mathf.Max(0f, Mathf.Max(radius - (x + 0.5f), x + 0.5f - (size - radius)));
                    float dy = Mathf.Max(0f, Mathf.Max(radius - (y + 0.5f), y + 0.5f - (size - radius)));
                    float a = radius <= 0 ? 1f : Mathf.Clamp01(radius - Mathf.Sqrt(dx * dx + dy * dy) + 0.5f);
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
        }

        /// <summary>
        /// A white radial glow with a broad bright middle that fades smoothly to nothing at the edge, so the
        /// light still shows around the brick that covers its centre.
        /// </summary>
        static void WriteGlow(string path, int size) {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            float half = size * 0.5f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++) {
                    float r = Mathf.Clamp01(new Vector2(x + 0.5f - half, y + 0.5f - half).magnitude / half);
                    float a = (1f - r * r) * (1f - r * r);
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
        }

        public static Sprite IconSprite(string name) {
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>($"{IconFolder}/icon_{name}.png");
            if (sprite == null) throw new FileNotFoundException($"Tycoon icon missing: {IconFolder}/icon_{name}.png");
            return sprite;
        }

        public static Sprite SignSprite() => AssetDatabase.LoadAssetAtPath<Sprite>(SignSpritePath);
        public static Sprite WhiteSprite() => AssetDatabase.LoadAssetAtPath<Sprite>(WhiteSpritePath);
        public static Sprite GlowSprite() => AssetDatabase.LoadAssetAtPath<Sprite>(GlowSpritePath);
        public static TMP_FontAsset Font(bool bold) => bold ? UIKit.Theme.boldFont : UIKit.Theme.semiBoldFont;

        // ---------------------------------------------------------------- building blocks
        static Image TyIcon(Transform parent, string icon, float size, Color color) {
            Image image = UIKit.Icon(parent, "flag", size);
            Object.DestroyImmediate(image.GetComponent<UIThemeColor>());
            image.sprite = IconSprite(icon);
            image.color = color;
            image.name = "Icon";
            return image;
        }

        static RectTransform Spacer(Transform parent) {
            RectTransform rect = UIKit.Node(parent, "Spacer");
            rect.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
            return rect;
        }

        static RectTransform HRow(Transform parent, string name, float spacing, float height = -1f) {
            RectTransform row = UIKit.Node(parent, name);
            UIKit.Row(row.gameObject, spacing);
            if (height > 0f) UIKit.Size(row, -1f, height);
            return row;
        }

        /// <summary>A rounded pill with an optional icon and a label, sized to its content.</summary>
        static TextMeshProUGUI Pill(Transform parent, string name, string icon, string text, Color background, Color foreground, out RectTransform pill, float height = 26f) {
            pill = UIKit.Node(parent, name);
            var bg = UIKit.Rounded(pill, UIColorRole.SurfaceInset, height * 0.5f, raycast: false);
            Object.DestroyImmediate(bg.GetComponent<UIThemeColor>());
            bg.color = background;
            UIKit.Row(pill.gameObject, 5f, UIKit.Pad(10, 0), TextAnchor.MiddleCenter);
            UIKit.Size(pill, -1f, height, 0f);
            if (!string.IsNullOrEmpty(icon)) TyIcon(pill, icon, height * 0.62f, foreground);
            var label = Label(pill, "Label", text, UIText.Caption, UIColorRole.TextPrimary, TextAlignmentOptions.Center);
            Object.DestroyImmediate(label.GetComponent<UIThemeColor>());
            label.color = foreground;
            label.characterSpacing = 1f;
            return label;
        }

        /// <summary>A rounded progress track with a horizontal fill image; returns the fill.</summary>
        static Image Bar(Transform parent, string name, float height, Color fill) {
            RectTransform track = UIKit.Node(parent, name);
            UIKit.Size(track, -1f, height, 1f);
            UIKit.Rounded(track, UIColorRole.SurfaceInset, height * 0.5f, raycast: false);
            RectTransform fillRect = UIKit.Fill(track, "Fill");
            var image = fillRect.gameObject.AddComponent<Image>();
            image.sprite = UIKit.Theme.roundedSprite;
            image.type = Image.Type.Filled;
            image.fillMethod = Image.FillMethod.Horizontal;
            image.fillOrigin = 0;
            image.fillAmount = 0.4f;
            image.color = fill;
            image.raycastTarget = false;
            return image;
        }

        /// <summary>
        /// A themed label that never truncates: Inter has no ellipsis glyph, so TMP's Ellipsis mode falls
        /// back to Truncate, which hides a whole line whenever it is a pixel taller than its rect.
        /// </summary>
        static TextMeshProUGUI Label(Transform parent, string name, string text, UIText style, UIColorRole role, TextAlignmentOptions align = TextAlignmentOptions.MidlineLeft) {
            var label = UIKit.Text(parent, name, text, style, role, align);
            label.overflowMode = TextOverflowModes.Overflow;
            return label;
        }

        static TextMeshProUGUI Colored(TextMeshProUGUI text, Color color) {
            Object.DestroyImmediate(text.GetComponent<UIThemeColor>());
            text.color = color;
            return text;
        }

        static RectTransform Card(Transform parent, string name, Vector2 anchor, Vector2 position, float width, int padding = 16, float spacing = 10f) {
            RectTransform card = UIKit.Pinned(parent, name, anchor, position, new Vector2(width, 100f));
            UIKit.Rounded(card, UIColorRole.SurfaceRaised, 18f, 0.94f);
            UIKit.Column(card.gameObject, spacing, UIKit.Pad(padding));
            UIKit.FitContent(card.gameObject, false, true);
            return card;
        }

        // ---------------------------------------------------------------- HUD
        public static GameObject Build() {
            UITheme theme = UIKit.Theme;
            RectTransform root = UIKit.Canvas("TycoonHUD", 20);
            var controller = root.gameObject.AddComponent<TY_ClientController>();
            var hud = root.gameObject.AddComponent<TY_Hud>();
            controller.hud = hud;
            controller.feedback = BuildFeedback(root);
            float m = theme.screenMargin + 4f;

            BuildWallet(root, hud, m, theme);
            BuildPlan(root, hud, m);
            BuildHint(root, hud, m);
            BuildToast(root, hud);
            BuildPerkShop(root, hud);

            return UIKit.SavePrefab(root.gameObject, PrefabPath);
        }

        /// <summary>
        /// The game feel component. Shared moments use the kit's cues; the dropper tick is the tycoon's
        /// own (Kenney "toggle", quiet and 3D so only your own belt is heard).
        /// </summary>
        static TY_Feedback BuildFeedback(RectTransform root) {
            var feedback = root.gameObject.AddComponent<TY_Feedback>();
            AudioClip tick = UniverseAuthoringKit.Clip(Kenney + "/toggle.ogg");
            if (tick == null) throw new FileNotFoundException($"Dropper tick sound missing: {Kenney}/toggle.ogg");
            feedback.dropperTick = new SfxSound {
                clips = new[] { tick }, volume = 0.35f, volumeJitter = 0.15f, pitch = 1.25f, pitchJitterSemitones = 1.5f,
                minInterval = 0.05f, minDistance = 4f, maxDistance = 32f,
            };
            return feedback;
        }

        static void BuildWallet(RectTransform root, TY_Hud hud, float margin, UITheme theme) {
            RectTransform card = Card(root, "Wallet", Vector2.zero, new Vector2(margin, margin), 380f);

            RectTransform header = HRow(card, "Header", 8f, 26f);
            TyIcon(header, "factory", 20f, theme.accent);
            hud.plotTitle = Label(header, "PlotTitle", "BRICK TYCOON", UIText.Caption, UIColorRole.TextSecondary);
            Spacer(header);
            hud.rushText = Pill(header, "RushPill", "local_fire_department", "RUSH x2  30s", Rush * new Color(1f, 1f, 1f, 0.22f), Rush, out RectTransform rush);
            hud.rushPill = rush.gameObject;
            hud.rebirthText = Pill(header, "RebirthBadge", "autorenew", "x1.5", Pink * new Color(1f, 1f, 1f, 0.22f), Pink, out RectTransform badge);
            hud.rebirthBadge = badge.gameObject;

            RectTransform cashRow = HRow(card, "CashRow", 10f, 54f);
            TyIcon(cashRow, "payments", 36f, theme.currency);
            hud.cash = Colored(Label(cashRow, "Cash", "$0", UIText.Display, UIColorRole.Currency), theme.currency);
            hud.cash.fontSize = 42f;

            RectTransform incomeRow = HRow(card, "IncomeRow", 8f, 26f);
            TyIcon(incomeRow, "trending_up", 20f, theme.success);
            hud.income = Colored(Label(incomeRow, "Income", "+$0/s", UIText.Strong, UIColorRole.Success), theme.success);
            Spacer(incomeRow);
            hud.multiplier = Pill(incomeRow, "Multiplier", "bolt", "x1 boost", theme.accent * new Color(1f, 1f, 1f, 0.22f), new Color(0.6f, 0.78f, 1f), out RectTransform multiplierPill);
            hud.multiplierPill = multiplierPill.gameObject;

            UIKit.Divider(card);

            RectTransform collectorHead = HRow(card, "CollectorHeader", 8f, 22f);
            TyIcon(collectorHead, "savings", 18f, hud.collectorNormal);
            Label(collectorHead, "Title", "Collector", UIText.Caption, UIColorRole.TextSecondary);
            Spacer(collectorHead);
            hud.collectorState = Colored(Label(collectorHead, "State", "", UIText.Caption, UIColorRole.Danger, TextAlignmentOptions.MidlineRight), hud.collectorFull);
            hud.collectorFill = Bar(card, "CollectorBar", 12f, hud.collectorNormal);
            hud.collectorText = Label(card, "CollectorText", "$0 / $400", UIText.Small, UIColorRole.TextSecondary);

            RectTransform perkRow = HRow(card, "PerkRow", 8f, 40f);
            hud.perksButton = UIKit.TextButton(perkRow, "PerksButton", "Rebirth perks", null, UIColorRole.SurfaceInset, UIColorRole.TextPrimary, 40f, "P");
            var icon = TyIcon(hud.perksButton.transform, "stars", 20f, theme.currency);
            icon.transform.SetSiblingIndex(0);
            hud.perksButtonTokens = Pill(hud.perksButton.transform, "Tokens", null, "0", theme.currency * new Color(1f, 1f, 1f, 0.2f), theme.currency, out RectTransform tokens, 24f);
            tokens.SetSiblingIndex(3);
        }

        static void BuildPlan(RectTransform root, TY_Hud hud, float margin) {
            RectTransform card = Card(root, "BuildPlan", new Vector2(1f, 0f), new Vector2(-margin, margin), 410f, 14, 8f);
            hud.buildPanel = card.gameObject;

            RectTransform header = HRow(card, "Header", 8f, 28f);
            TyIcon(header, "construction", 22f, UIKit.Theme.warning);
            Label(header, "Title", "Build plan", UIText.Title, UIColorRole.TextPrimary).fontSize = 20f;
            Spacer(header);
            hud.buildText = Label(header, "Count", "0/22 built", UIText.Small, UIColorRole.TextSecondary, TextAlignmentOptions.MidlineRight);
            hud.buildFill = Bar(card, "BuildBar", 8f, UIKit.Theme.warning);

            hud.laneCards = TY_Lanes.BuildLanes.Select(lane => LaneCard(card, lane)).ToArray();

            // Rebirth card: only shown once every lane is complete.
            RectTransform rebirth = UIKit.Node(card, "RebirthCard");
            var bg = UIKit.Rounded(rebirth, UIColorRole.SurfaceInset, 12f, raycast: false);
            Object.DestroyImmediate(bg.GetComponent<UIThemeColor>());
            bg.color = Pink * new Color(1f, 1f, 1f, 0.18f);
            UIKit.Column(rebirth.gameObject, 6f, UIKit.Pad(12, 10));
            RectTransform top = HRow(rebirth, "Top", 8f, 26f);
            TyIcon(top, "autorenew", 22f, Pink);
            Colored(Label(top, "Title", "Rebirth ready!", UIText.Strong, UIColorRole.TextPrimary), Pink);
            Spacer(top);
            hud.rebirthCardPrice = Colored(Label(top, "Price", "$50K", UIText.Strong, UIColorRole.Currency, TextAlignmentOptions.MidlineRight), UIKit.Theme.currency);
            Label(rebirth, "Info", $"Step on the pink pad: income x{1 + TY_Catalog.RebirthBonus:0.#} per rebirth and +1 perk token", UIText.Small, UIColorRole.TextSecondary)
                .textWrappingMode = TextWrappingModes.Normal;
            hud.rebirthCardFill = Bar(rebirth, "Bar", 8f, Pink);
            hud.rebirthCard = rebirth.gameObject;
        }

        static TY_BuildCard LaneCard(Transform parent, TY_ItemKind lane) {
            Color color = TY_Lanes.Color(lane);
            RectTransform row = UIKit.Node(parent, $"Lane{lane}");
            UIKit.Rounded(row, UIColorRole.SurfaceInset, 12f, raycast: false);
            UIKit.Row(row.gameObject, 10f, new RectOffset(0, 12, 0, 0), TextAnchor.MiddleLeft);
            UIKit.Size(row, -1f, 74f);
            var card = row.gameObject.AddComponent<TY_BuildCard>();
            card.lane = lane;
            card.group = row.gameObject.AddComponent<CanvasGroup>();

            RectTransform strip = UIKit.Node(row, "LaneStrip");
            UIKit.Size(strip, 5f, 74f);
            var stripImage = strip.gameObject.AddComponent<Image>();
            stripImage.sprite = UIKit.Theme.roundedSprite;
            stripImage.type = Image.Type.Sliced;
            stripImage.pixelsPerUnitMultiplier = 8f;
            stripImage.color = color;
            stripImage.raycastTarget = false;

            RectTransform frame = UIKit.Node(row, "PictureFrame");
            UIKit.Size(frame, 60f, 60f);
            var frameBg = UIKit.Rounded(frame, UIColorRole.SurfaceInset, 10f, raycast: false);
            Object.DestroyImmediate(frameBg.GetComponent<UIThemeColor>());
            frameBg.color = color * new Color(1f, 1f, 1f, 0.16f);
            RectTransform pic = UIKit.Fill(frame, "Picture", 2f, 2f, 2f, 2f);
            card.picture = pic.gameObject.AddComponent<Image>();
            card.picture.preserveAspect = true;
            card.picture.raycastTarget = false;
            // Lock / done / ready badges sit on the picture's corner.
            card.lockedBadge = Badge(frame, "Locked", "lock", new Color(0.4f, 0.42f, 0.48f));
            card.doneBadge = Badge(frame, "Done", "check_circle", UIKit.Theme.success);
            card.readyBadge = Badge(frame, "Ready", "shopping_cart", UIKit.Theme.success);

            RectTransform body = UIKit.Node(row, "Body");
            UIKit.Column(body.gameObject, 2f);
            body.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
            RectTransform titleRow = HRow(body, "TitleRow", 6f, 20f);
            TyIcon(titleRow, TY_Lanes.Icon(lane), 16f, color);
            Colored(Label(titleRow, "Lane", TY_Lanes.Title(lane), UIText.Caption, UIColorRole.TextMuted), color);
            Spacer(titleRow);
            card.step = Label(titleRow, "Step", "0/0", UIText.Caption, UIColorRole.TextMuted, TextAlignmentOptions.MidlineRight);
            card.title = Label(body, "Title", "Item", UIText.Strong, UIColorRole.TextPrimary);
            UIKit.Size(card.title, -1f, 20f);
            RectTransform bottom = HRow(body, "Bottom", 8f, 18f);
            card.effect = Label(bottom, "Effect", "+$1/s", UIText.Small, UIColorRole.TextSecondary);
            card.effect.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
            card.price = Colored(Label(bottom, "Price", "$0", UIText.Strong, UIColorRole.Currency, TextAlignmentOptions.MidlineRight), UIKit.Theme.currency);
            card.progressFill = Bar(body, "Progress", 5f, color);
            return card;
        }

        static GameObject Badge(Transform frame, string name, string icon, Color color) {
            RectTransform badge = UIKit.Pinned(frame, name, new Vector2(1f, 0f), new Vector2(6f, -6f), new Vector2(24f, 24f));
            badge.pivot = new Vector2(1f, 0f);
            var bg = UIKit.Rounded(badge, UIColorRole.SurfaceRaised, 12f, raycast: false);
            Object.DestroyImmediate(bg.GetComponent<UIThemeColor>());
            bg.color = color;
            var image = TyIcon(badge, icon, 16f, Color.white);
            UIKit.Center(image.rectTransform);
            return badge.gameObject;
        }

        static void BuildHint(RectTransform root, TY_Hud hud, float margin) {
            RectTransform pill = UIKit.Pinned(root, "Hint", new Vector2(0.5f, 0f), new Vector2(0f, margin), new Vector2(600f, 44f));
            UIKit.Rounded(pill, UIColorRole.Surface, 22f, raycast: false);
            UIKit.Row(pill.gameObject, 10f, UIKit.Pad(18, 0), TextAnchor.MiddleCenter);
            UIKit.FitContent(pill.gameObject, true, false);
            hud.hintIcon = TyIcon(pill, "flag", 22f, UIKit.Theme.warning);
            hud.hint = Label(pill, "Text", "Connecting...", UIText.Strong, UIColorRole.TextPrimary, TextAlignmentOptions.Center);
            hud.hintClaim = IconSprite("flag");
            hud.hintBuild = IconSprite("construction");
            hud.hintCollect = IconSprite("savings");
            hud.hintWait = IconSprite("precision_manufacturing");
        }

        static void BuildToast(RectTransform root, TY_Hud hud) {
            RectTransform toast = UIKit.Pinned(root, "Toast", new Vector2(0.5f, 1f), new Vector2(0f, -78f), new Vector2(520f, 56f));
            UIKit.Rounded(toast, UIColorRole.SurfaceRaised, 16f, raycast: false);
            UIKit.Row(toast.gameObject, 12f, new RectOffset(0, 22, 0, 0), TextAnchor.MiddleLeft);
            UIKit.FitContent(toast.gameObject, true, false);
            hud.toast = toast.gameObject.AddComponent<CanvasGroup>();
            hud.toast.blocksRaycasts = false;
            hud.toast.interactable = false;

            RectTransform accent = UIKit.Node(toast, "Accent");
            UIKit.Size(accent, 6f, 56f);
            hud.toastAccent = accent.gameObject.AddComponent<Image>();
            hud.toastAccent.sprite = UIKit.Theme.roundedSprite;
            hud.toastAccent.type = Image.Type.Sliced;
            hud.toastAccent.pixelsPerUnitMultiplier = 6f;
            hud.toastAccent.raycastTarget = false;
            hud.toastIcon = TyIcon(toast, "check_circle", 28f, UIKit.Theme.success);
            hud.toastText = Label(toast, "Text", "Built Dropper!", UIText.Strong, UIColorRole.TextPrimary);
            hud.toastText.fontSize = 19f;

            hud.iconBought = IconSprite("check_circle");
            hud.iconCash = IconSprite("payments");
            hud.iconDenied = IconSprite("lock");
            hud.iconRebirth = IconSprite("autorenew");
            hud.iconGolden = IconSprite("stars");
            hud.iconPerk = IconSprite("star");
            hud.iconInfo = IconSprite("auto_awesome");
            hud.iconFull = IconSprite("savings");
            hud.iconRush = IconSprite("local_fire_department");
        }

        static void BuildPerkShop(RectTransform root, TY_Hud hud) {
            UITheme theme = UIKit.Theme;
            RectTransform panel = UIKit.Pinned(root, "PerkShop", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(560f, 100f));
            UIKit.Rounded(panel, UIColorRole.SurfaceRaised, 20f);
            UIKit.Column(panel.gameObject, 12f, UIKit.Pad(20));
            UIKit.FitContent(panel.gameObject, false, true);
            hud.perksPanel = panel.gameObject;

            RectTransform header = HRow(panel, "Header", 10f, 36f);
            TyIcon(header, "stars", 28f, theme.currency);
            Label(header, "Title", "Rebirth perks", UIText.Title, UIColorRole.TextPrimary);
            Spacer(header);
            hud.perksTokens = Pill(header, "Tokens", "star", "0 tokens", theme.currency * new Color(1f, 1f, 1f, 0.2f), theme.currency, out _, 28f);
            hud.perksClose = UIKit.IconButton(header, "Close", "close", 34f, UIColorRole.SurfaceInset, 10f, out _, out _);
            Label(panel, "Subtitle", "Every rebirth earns a token. Perks are permanent and saved with your tycoon.", UIText.Small, UIColorRole.TextSecondary)
                .textWrappingMode = TextWrappingModes.Normal;

            hud.perkRows = TY_Perks.All.Select(perk => PerkRow(panel, perk)).ToArray();
        }

        static TY_PerkRow PerkRow(Transform parent, TY_Perk perk) {
            UITheme theme = UIKit.Theme;
            RectTransform row = UIKit.Node(parent, $"Perk{perk.id}");
            UIKit.Rounded(row, UIColorRole.SurfaceInset, 12f, raycast: false);
            UIKit.Row(row.gameObject, 12f, UIKit.Pad(12, 10), TextAnchor.MiddleLeft);
            UIKit.Size(row, -1f, 68f);
            var view = row.gameObject.AddComponent<TY_PerkRow>();
            view.perk = perk.id;

            RectTransform iconBox = UIKit.Node(row, "IconBox");
            UIKit.Size(iconBox, 44f, 44f);
            var boxBg = UIKit.Rounded(iconBox, UIColorRole.SurfaceInset, 10f, raycast: false);
            Object.DestroyImmediate(boxBg.GetComponent<UIThemeColor>());
            boxBg.color = theme.currency * new Color(1f, 1f, 1f, 0.18f);
            UIKit.Center(TyIcon(iconBox, perk.icon, 26f, theme.currency).rectTransform);

            RectTransform body = UIKit.Node(row, "Body");
            UIKit.Column(body.gameObject, 2f);
            body.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
            view.title = Label(body, "Title", perk.name, UIText.Strong, UIColorRole.TextPrimary);
            view.effect = Label(body, "Effect", perk.Effect(1), UIText.Small, UIColorRole.TextSecondary);
            RectTransform pips = HRow(body, "Pips", 4f, 8f);
            view.pips = Enumerable.Range(0, TY_Perks.All.Max(p => p.maxLevel)).Select(i => {
                RectTransform pip = UIKit.Node(pips, $"Pip{i}");
                UIKit.Size(pip, 22f, 6f);
                var image = pip.gameObject.AddComponent<Image>();
                image.sprite = theme.roundedSprite;
                image.type = Image.Type.Sliced;
                image.pixelsPerUnitMultiplier = 8f;
                image.raycastTarget = false;
                return image;
            }).ToArray();

            RectTransform cost = HRow(row, "Cost", 4f, 24f);
            TyIcon(cost, "star", 18f, theme.currency);
            view.cost = Colored(Label(cost, "Value", "1", UIText.Strong, UIColorRole.Currency), theme.currency);
            view.upgrade = UIKit.TextButton(row, "Upgrade", "Upgrade", null, UIColorRole.Accent, UIColorRole.OnAccent, 38f);
            UIKit.Size(view.upgrade, 112f, 38f);
            view.upgradeLabel = view.upgrade.GetComponentInChildren<TextMeshProUGUI>();
            view.upgradeGroup = view.upgrade.gameObject.AddComponent<CanvasGroup>();
            return view;
        }
    }
}
#else
using TMPro;
using UnityEditor;
using UnityEngine;

namespace Universes.UniverseData.tycoon.Editor {
    /// <summary>
    /// Server-profile stand-in: the shared UI kit is client-only, so a Linux Server Editor only loads
    /// what TY_HudAuthoring generated earlier (the HUD isn't part of the server build).
    /// </summary>
    public static class TY_HudAuthoring {
        const string Ui = TY_Authoring.Root + "/UI";
        public static void ImportIcons() { }
        public static Sprite IconSprite(string name) => AssetDatabase.LoadAssetAtPath<Sprite>($"{Ui}/Icons/icon_{name}.png");
        public static Sprite SignSprite() => AssetDatabase.LoadAssetAtPath<Sprite>(Ui + "/SignBack.png");
        public static Sprite WhiteSprite() => AssetDatabase.LoadAssetAtPath<Sprite>(Ui + "/White.png");
        public static Sprite GlowSprite() => AssetDatabase.LoadAssetAtPath<Sprite>(Ui + "/Glow.png");
        public static TMP_FontAsset Font(bool bold) => null;
        public static GameObject Build() => AssetDatabase.LoadAssetAtPath<GameObject>(TY_Authoring.Root + "/Prefabs/TycoonHUD.prefab");
    }
}
#endif
