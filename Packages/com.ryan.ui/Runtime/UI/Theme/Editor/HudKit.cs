using RyanAssets.UI.GameHud;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RyanAssets.UI.Editor {
    /// <summary>
    /// Building blocks for the game-specific HUDs (role cards, scoreboards, objective pills, toasts,
    /// banners, result tables), made from the shared <see cref="UITheme"/> through <see cref="UIKit"/>,
    /// so every game's HUD matches the top bar, chat and menus.
    /// <para>
    /// Game authoring scripts call these when they generate their HUD prefab; the runtime half
    /// (<see cref="HudPopup"/>, <see cref="HudChip"/>, <see cref="HudBanner"/>, <see cref="HudToastFeed"/>,
    /// <see cref="HudRows"/>, <see cref="HudBar"/>) lives in <c>RyanAssets.UI.GameHud</c>.
    /// Icons are looked up by name in the Material set or the game-icons.net set
    /// (<see cref="UIThemeAssets.Icon"/>); add new ones through <c>Tools/UIIcons/icons.json</c>.
    /// </para>
    /// Guidelines the pieces follow: glass panels over the game, raised cards for things that need
    /// reading, one accent colour per element, an icon on every value, and nothing that stays on
    /// screen longer than it is useful.
    /// </summary>
    public static class HudKit {
        /// <summary>Corner radius of pills and chips (half their height reads as fully round).</summary>
        public const float ChipHeight = 34f;
        public const float CardRadius = 16f;
        public const float GlassRadius = 12f;

        static UITheme Theme => UIKit.Theme;

        public static Sprite Icon(string name) => UIThemeAssets.Icon(name);

        // --- Surfaces -----------------------------------------------------------

        /// <summary>A screen-space HUD canvas; HUDs sit below the shared top bar (50) and menus.</summary>
        public static RectTransform Canvas(string name, int sortingOrder) => UIKit.Canvas(name, sortingOrder);

        /// <summary>A translucent panel straight over the game (theme <see cref="UIColorRole.Surface"/>).</summary>
        public static Image Glass(Component target, float radius = GlassRadius, float opacity = 1f, bool raycast = false) =>
            UIKit.Rounded(target, UIColorRole.Surface, radius, opacity, raycast);

        /// <summary>
        /// An opaque card with a soft shadow, pinned at <paramref name="anchor"/>. With
        /// <paramref name="popup"/> it starts hidden and fades/pops in through <see cref="HudPopup"/>.
        /// </summary>
        public static RectTransform Card(Transform parent, string name, Vector2 anchor, Vector2 position, Vector2 size, bool popup = true, bool shadow = true) {
            RectTransform card = UIKit.Pinned(parent, name, anchor, position, size);
            if (shadow) {
                // The shadow is a child so it fades, pops and hides with the card.
                Image shade = UIKit.Shadow(card);
                shade.rectTransform.SetParent(card, true);
                shade.rectTransform.SetAsFirstSibling();
                shade.rectTransform.anchorMin = Vector2.zero;
                shade.rectTransform.anchorMax = Vector2.one;
                float blur = 28f;
                shade.rectTransform.offsetMin = new Vector2(-blur, -blur - 8f);
                shade.rectTransform.offsetMax = new Vector2(blur, blur - 8f);
                shade.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            }
            RectTransform body = UIKit.Fill(card, "Body");
            UIKit.Rounded(body, UIColorRole.SurfaceRaised, CardRadius, raycast: false);
            body.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            if (popup)
                Popup(card, false);
            return card;
        }

        /// <summary>A coloured bar along the top (or left) edge of a card, inside its rounded body.</summary>
        public static Image AccentStripe(RectTransform card, Color color, float thickness = 4f, bool left = false) {
            RectTransform stripe = UIKit.Node(card, "Accent");
            stripe.anchorMin = left ? new Vector2(0f, 0f) : new Vector2(0f, 1f);
            stripe.anchorMax = left ? new Vector2(0f, 1f) : new Vector2(1f, 1f);
            stripe.pivot = left ? new Vector2(0f, 0.5f) : new Vector2(0.5f, 1f);
            float inset = CardRadius * 0.75f;
            stripe.sizeDelta = left ? new Vector2(thickness, -inset * 2f) : new Vector2(-inset * 2f, thickness);
            stripe.anchoredPosition = Vector2.zero;
            stripe.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            Image image = UIKit.Rounded(stripe, UIColorRole.Accent, thickness * 0.5f, raycast: false);
            Object.DestroyImmediate(image.GetComponent<UIThemeColor>());
            image.color = color;
            return image;
        }

        /// <summary>Adds a <see cref="HudPopup"/> (and its CanvasGroup) to <paramref name="target"/>.</summary>
        public static HudPopup Popup(Component target, bool visible) {
            if (!target.TryGetComponent(out CanvasGroup group))
                group = target.gameObject.AddComponent<CanvasGroup>();
            group.alpha = visible ? 1f : 0f;
            group.interactable = group.blocksRaycasts = visible;
            // Components that require a HudPopup (toasts, banners) may already have added one.
            if (!target.TryGetComponent(out HudPopup popup))
                popup = target.gameObject.AddComponent<HudPopup>();
            popup.fadeDuration = Theme.fadeDuration;
            if (!visible)
                target.gameObject.SetActive(false);
            return popup;
        }

        // --- Icons --------------------------------------------------------------

        /// <summary>
        /// Gives a leaf element a fixed size without pinning its max size. Unity 6.6 horizontal groups
        /// report the smallest finite max height of their children as their own, so a pinned 58 px
        /// emblem would clamp a taller text column next to it.
        /// </summary>
        public static LayoutElement Fixed(RectTransform target, float width, float height) {
            LayoutElement element = UIKit.Size(target, width, height);
            element.maxWidth = element.maxHeight = -1f;
            element.flexibleWidth = element.flexibleHeight = 0f;
            return element;
        }

        /// <summary>
        /// A compact keyboard keycap ("F") for inline hints: a fixed-height rounded tile at least as wide
        /// as it is tall, so single letters read as keys rather than bars.
        /// </summary>
        public static RectTransform KeyCap(Transform parent, string key, float height = 22f) {
            RectTransform cap = UIKit.Node(parent, "KeyCap");
            UIKit.Rounded(cap, UIColorRole.TextPrimary, 5f, 0.16f, raycast: false);
            TextMeshProUGUI text = UIKit.Text(cap, "Key", key, UIText.Caption, UIColorRole.TextPrimary, TextAlignmentOptions.Center);
            text.characterSpacing = 0f;
            text.fontSize = Mathf.Round(height * 0.58f);
            UIKit.Stretch(text.rectTransform);
            Fixed(cap, Mathf.Max(height, text.preferredWidth + 12f), height);
            return cap;
        }

        /// <summary>An icon tinted with a fixed colour (team, role) rather than a theme slot.</summary>
        public static Image Icon(Transform parent, string icon, float size, Color color) {
            Image image = UIKit.Icon(parent, icon, size);
            Fixed(image.rectTransform, size, size);
            Object.DestroyImmediate(image.GetComponent<UIThemeColor>());
            image.color = color;
            return image;
        }

        /// <summary>
        /// A round badge: the icon in white on an accent-coloured disc, with a faint halo, used as a
        /// card's emblem (role, team, result). <paramref name="disc"/> is what to recolour at runtime.
        /// </summary>
        public static RectTransform Emblem(Transform parent, string name, string icon, float size, Color color, out Image disc, out Image glyph) {
            RectTransform holder = UIKit.Node(parent, name);
            holder.sizeDelta = Vector2.one * size;
            Fixed(holder, size, size);

            RectTransform halo = UIKit.Node(holder, "Halo");
            UIKit.Center(halo);
            halo.sizeDelta = Vector2.one * size;
            Image haloImage = UIKit.Rounded(halo, UIColorRole.TextPrimary, size * 0.5f, 0.08f, raycast: false);

            RectTransform core = UIKit.Node(holder, "Disc");
            UIKit.Center(core);
            core.sizeDelta = Vector2.one * (size - 10f);
            disc = UIKit.Rounded(core, UIColorRole.Accent, (size - 10f) * 0.5f, raycast: false);
            Object.DestroyImmediate(disc.GetComponent<UIThemeColor>());
            disc.color = color;

            glyph = UIKit.Icon(core, icon, Mathf.Round(size * 0.52f), UIColorRole.OnAccent);
            UIKit.Center(glyph.rectTransform);
            haloImage.raycastTarget = false;
            return holder;
        }

        // --- Pills --------------------------------------------------------------

        /// <summary>
        /// A glass pill with an icon and a value, sized to its text. <paramref name="iconColor"/>
        /// colours the icon; the value is primary text.
        /// </summary>
        public static HudChip Chip(Transform parent, string name, string icon, string text, Color iconColor, float height = ChipHeight, UIText style = UIText.Strong) {
            RectTransform rect = UIKit.Node(parent, name);
            UIKit.Size(rect, -1f, height, 0f);
            rect.sizeDelta = new Vector2(120f, height);
            HudChip chip = rect.gameObject.AddComponent<HudChip>();
            chip.background = Glass(rect, height * 0.5f);
            UIKit.Row(rect.gameObject, 7f, new RectOffset(Mathf.RoundToInt(height * 0.32f), Mathf.RoundToInt(height * 0.42f), 0, 0), TextAnchor.MiddleLeft);
            FitWidth(rect);
            if (!string.IsNullOrEmpty(icon))
                chip.icon = Icon(rect, icon, Mathf.Round(height * 0.56f), iconColor);
            chip.label = UIKit.Text(rect, "Value", text, style);
            chip.label.overflowMode = TextOverflowModes.Overflow;
            return chip;
        }

        /// <summary>
        /// A key prompt: a keycap followed by what the key does ("[E] Report the body"). Returns the
        /// pill; <paramref name="label"/> is the action text and <paramref name="keyCap"/> the keycap to recolour.
        /// </summary>
        public static RectTransform KeyPrompt(Transform parent, string name, string key, string action, Color keyColor, out TMP_Text label, out Image keyCap, float height = 44f) {
            RectTransform rect = UIKit.Node(parent, name);
            rect.sizeDelta = new Vector2(320f, height);
            Glass(rect, height * 0.5f);
            UIKit.Row(rect.gameObject, 10f, new RectOffset(7, 18, 0, 0), TextAnchor.MiddleLeft);
            FitWidth(rect);

            RectTransform cap = UIKit.Node(rect, "Key");
            float capSize = height - 14f;
            Fixed(cap, capSize, capSize);
            keyCap = UIKit.Rounded(cap, UIColorRole.Accent, 8f, raycast: false);
            Object.DestroyImmediate(keyCap.GetComponent<UIThemeColor>());
            keyCap.color = keyColor;
            TextMeshProUGUI keyText = UIKit.Text(cap, "Letter", key, UIText.Title, UIColorRole.OnAccent, TextAlignmentOptions.Center);
            keyText.fontSize = capSize * 0.6f;
            UIKit.Stretch(keyText.rectTransform);

            label = UIKit.Text(rect, "Action", action, UIText.Strong);
            label.richText = true;
            label.overflowMode = TextOverflowModes.Overflow;
            return rect;
        }

        /// <summary>A rounded track with a coloured fill (<see cref="HudBar.Value"/> 0..1).</summary>
        public static HudBar Bar(Transform parent, string name, float height, Color fillColor) {
            RectTransform track = UIKit.Node(parent, name);
            UIKit.Size(track, -1f, height, 1f);
            UIKit.Rounded(track, UIColorRole.SurfaceInset, height * 0.5f, raycast: false);
            RectTransform fill = UIKit.Fill(track, "Fill");
            fill.anchorMax = new Vector2(0.5f, 1f);
            Image fillImage = UIKit.Rounded(fill, UIColorRole.Accent, height * 0.5f, raycast: false);
            Object.DestroyImmediate(fillImage.GetComponent<UIThemeColor>());
            fillImage.color = fillColor;
            HudBar bar = track.gameObject.AddComponent<HudBar>();
            bar.fill = fill;
            bar.fillImage = fillImage;
            return bar;
        }

        /// <summary>
        /// Sizes a pill to its contents when it stands alone; inside a layout group the group already
        /// uses the pill's preferred width, and a fitter there would fight it.
        /// </summary>
        static void FitWidth(RectTransform rect) {
            if (rect.parent == null || rect.parent.GetComponent<LayoutGroup>() == null)
                UIKit.FitContent(rect.gameObject, true, false);
        }

        // --- Announcements ------------------------------------------------------

        /// <summary>
        /// A centred banner card: emblem on top, an optional small kicker line ("THE MYSTERY BEGINS"),
        /// a large title and a wrapping subtitle. It grows to
        /// fit its text and starts hidden; call <see cref="HudBanner.Show"/>.
        /// </summary>
        public static HudBanner Banner(Transform parent, string name, Vector2 anchor, Vector2 position, float width, string icon, Color accent, float emblemSize = 76f, string kicker = null) {
            RectTransform card = Card(parent, name, anchor, position, new Vector2(width, 200f));
            UIKit.Column(card.gameObject, 6f, new RectOffset(32, 32, 24, 26), TextAnchor.UpperCenter);
            UIKit.FitContent(card.gameObject, false, true);
            HudBanner banner = card.gameObject.AddComponent<HudBanner>();
            banner.accentStripe = AccentStripe(card, accent);

            Emblem(card, "Emblem", icon, emblemSize, accent, out banner.emblem, out banner.emblemIcon);
            RectTransform gap = UIKit.Node(card, "Gap");
            UIKit.Size(gap, -1f, 4f);
            if (!string.IsNullOrEmpty(kicker))
                banner.kicker = UIKit.Text(card, "Kicker", kicker, UIText.Caption, UIColorRole.TextMuted, TextAlignmentOptions.Center);

            banner.title = UIKit.Text(card, "Title", name.ToUpperInvariant(), UIText.Display, UIColorRole.TextPrimary, TextAlignmentOptions.Center);
            banner.title.textWrappingMode = TextWrappingModes.Normal;
            banner.title.overflowMode = TextOverflowModes.Overflow;
            banner.title.richText = true;
            banner.subtitle = UIKit.Text(card, "Subtitle", string.Empty, UIText.Body, UIColorRole.TextSecondary, TextAlignmentOptions.Center);
            banner.subtitle.textWrappingMode = TextWrappingModes.Normal;
            banner.subtitle.overflowMode = TextOverflowModes.Overflow;
            banner.subtitle.richText = true;
            banner.subtitle.fontSize = Theme.bodyFontSize + 2f;
            return banner;
        }

        /// <summary>
        /// A column of short-lived event toasts growing downward from <paramref name="anchor"/>
        /// (newest on top). Push lines with <see cref="HudToastFeed.Push"/>.
        /// </summary>
        public static HudToastFeed ToastFeed(Transform parent, string name, Vector2 anchor, Vector2 position, float width, float rowHeight = 40f) {
            RectTransform feed = UIKit.Pinned(parent, name, anchor, position, new Vector2(width, rowHeight * 4f + 18f));
            VerticalLayoutGroup column = UIKit.Column(feed.gameObject, 6f, null, anchor.x > 0.6f ? TextAnchor.UpperRight : anchor.x < 0.4f ? TextAnchor.UpperLeft : TextAnchor.UpperCenter);
            column.childForceExpandWidth = false;
            HudToastFeed toasts = feed.gameObject.AddComponent<HudToastFeed>();

            RectTransform row = UIKit.Node(feed, "ToastTemplate");
            row.sizeDelta = new Vector2(width, rowHeight);
            UIKit.Size(row, -1f, rowHeight, 0f);
            Glass(row, 10f, 0.95f);
            UIKit.Row(row.gameObject, 10f, new RectOffset(16, 14, 0, 0), TextAnchor.MiddleLeft);
            HudToast toast = row.gameObject.AddComponent<HudToast>();

            RectTransform edge = UIKit.Node(row, "Accent");
            edge.anchorMin = new Vector2(0f, 0f);
            edge.anchorMax = new Vector2(0f, 1f);
            edge.pivot = new Vector2(0f, 0.5f);
            edge.sizeDelta = new Vector2(4f, -14f);
            edge.anchoredPosition = new Vector2(5f, 0f);
            edge.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            toast.accent = UIKit.Rounded(edge, UIColorRole.Accent, 2f, raycast: false);
            Object.DestroyImmediate(toast.accent.GetComponent<UIThemeColor>());

            toast.icon = Icon(row, "flag", 22f, Color.white);
            toast.label = UIKit.Text(row, "Text", string.Empty, UIText.Strong);
            toast.label.richText = true;
            toast.label.overflowMode = TextOverflowModes.Overflow;
            Popup(row, false);
            toasts.template = toast;
            return toasts;
        }

        // --- Tables -------------------------------------------------------------

        /// <summary>A table column: width in canvas units (or &lt; 0 to share the spare width) and alignment.</summary>
        public readonly struct Column {
            public readonly string header;
            public readonly float width;
            public readonly TextAlignmentOptions align;

            public Column(string header, float width = -1f, TextAlignmentOptions align = TextAlignmentOptions.MidlineLeft) {
                this.header = header;
                this.width = width;
                this.align = align;
            }
        }

        /// <summary>
        /// A header row plus a <see cref="HudRows"/> list under it, inside <paramref name="parent"/>
        /// (which should have a vertical layout). <paramref name="leadingIcon"/> adds an icon cell first.
        /// </summary>
        public static HudRows Table(Transform parent, string name, Column[] columns, float rowHeight = 38f, bool leadingIcon = true) {
            RectTransform table = UIKit.Node(parent, name);
            UIKit.Column(table.gameObject, 0f);
            UIKit.FitContent(table.gameObject, false, true);

            RectTransform header = TableRow(table, "Header", columns, rowHeight - 8f, leadingIcon, out _, out TMP_Text[] headerCells, out Image headerIcon);
            // The header keeps the icon cell for alignment but shows nothing in it.
            if (headerIcon != null) headerIcon.enabled = false;
            for (int i = 0; i < columns.Length; i++) {
                headerCells[i].text = columns[i].header;
                UIKit.Paint((TextMeshProUGUI)headerCells[i], UIColorRole.TextMuted);
                headerCells[i].font = Theme.semiBoldFont;
                headerCells[i].fontSize = Theme.smallFontSize - 1f;
                headerCells[i].fontStyle = FontStyles.UpperCase;
                headerCells[i].characterSpacing = 4f;
            }
            UIKit.Divider(table);

            RectTransform template = TableRow(table, "RowTemplate", columns, rowHeight, leadingIcon, out Image stripe, out TMP_Text[] cells, out Image icon);
            HudRow row = template.gameObject.AddComponent<HudRow>();
            row.stripe = stripe;
            row.cells = cells;
            row.icon = icon;
            template.gameObject.SetActive(false);
            HudRows rows = table.gameObject.AddComponent<HudRows>();
            rows.template = row;
            header.name = "Header";
            return rows;
        }

        static RectTransform TableRow(Transform parent, string name, Column[] columns, float height, bool leadingIcon, out Image stripe, out TMP_Text[] cells, out Image icon) {
            RectTransform row = UIKit.Node(parent, name);
            UIKit.Size(row, -1f, height);
            RectTransform zebra = UIKit.Fill(row, "Stripe");
            zebra.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            stripe = UIKit.Rounded(zebra, UIColorRole.SurfaceInset, 8f, 0.55f, raycast: false);
            stripe.enabled = false;
            UIKit.Row(row.gameObject, 12f, new RectOffset(12, 12, 0, 0), TextAnchor.MiddleLeft);
            icon = null;
            if (leadingIcon) {
                icon = Icon(row, "person", 22f, Color.white);
            }
            cells = new TMP_Text[columns.Length];
            for (int i = 0; i < columns.Length; i++) {
                TextMeshProUGUI cell = UIKit.Text(row, $"Cell{i}", string.Empty, i == 0 ? UIText.Strong : UIText.Body, UIColorRole.TextPrimary, columns[i].align);
                cell.richText = true;
                // Cells are coloured per row at runtime (role, fate), so the theme must not repaint them
                // whenever the table is re-enabled.
                Object.DestroyImmediate(cell.GetComponent<UIThemeColor>());
                if (columns[i].width >= 0f)
                    UIKit.Size(cell, columns[i].width, -1f);
                else
                    UIKit.Size(cell, -1f, -1f, 1f);
                cells[i] = cell;
            }
            return row;
        }
    }
}
