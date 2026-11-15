using TMPro;
using UnityEngine;

namespace RyanAssets.UI {
    /// <summary>The colour slots every shared UI element is painted from.</summary>
    public enum UIColorRole {
        /// <summary>Glass panels floating over the game (chat, player list, top bar buttons).</summary>
        Surface,
        /// <summary>Opaque cards and modal panels (game menu, game browser).</summary>
        SurfaceRaised,
        /// <summary>Rows, inputs and tracks sitting on a surface.</summary>
        SurfaceInset,
        /// <summary>Hover and selected rows.</summary>
        SurfaceHover,
        /// <summary>Hairline borders and dividers.</summary>
        Stroke,
        /// <summary>Full-screen dimming behind modals.</summary>
        Scrim,
        TextPrimary,
        TextSecondary,
        TextMuted,
        Accent,
        /// <summary>Text and icons drawn on top of <see cref="Accent"/>.</summary>
        OnAccent,
        Success,
        Warning,
        Danger,
        Currency,
        /// <summary>System lines in chat (joins, leaves, server notices).</summary>
        SystemText,
    }

    /// <summary>
    /// The look of the shared interface: colours, type, shapes and motion, in one asset.
    /// <para>
    /// The top bar, chat, player list, game menu and game browser all read from the active theme,
    /// either at runtime (<see cref="UIThemeColor"/>, state colours in scripts) or when their prefabs
    /// are generated (Ryan/UI menu). Change a colour here and every <see cref="UIThemeColor"/>
    /// updates immediately; change fonts, sprites or sizes and rebuild the prefabs.
    /// </para>
    /// The asset the game uses lives at <c>Resources/UITheme</c> in com.ryan.ui.
    /// </summary>
    [CreateAssetMenu(menuName = "Ryan/UI/Theme", fileName = "UITheme")]
    public class UITheme : ScriptableObject {
        public const string ResourcePath = "UITheme";

        [Header("Surfaces")]
        public Color surface = Hex(0x0F1218, 0.72f);
        public Color surfaceRaised = Hex(0x151922, 0.97f);
        public Color surfaceInset = Hex(0xFFFFFF, 0.06f);
        public Color surfaceHover = Hex(0xFFFFFF, 0.12f);
        public Color stroke = Hex(0xFFFFFF, 0.09f);
        public Color scrim = Hex(0x05070A, 0.6f);

        [Header("Text")]
        public Color textPrimary = Hex(0xF4F6FA);
        public Color textSecondary = Hex(0xB4BCC8);
        public Color textMuted = Hex(0x7D8796);
        public Color systemText = Hex(0x8FD3FF);

        [Header("Accents")]
        public Color accent = Hex(0x3D8BFF);
        public Color onAccent = Hex(0xFFFFFF);
        public Color success = Hex(0x34C77B);
        public Color warning = Hex(0xFFB547);
        public Color danger = Hex(0xF2545B);
        public Color currency = Hex(0xFFC93C);

        [Header("Chat names")]
        [Tooltip("Player names in chat are coloured by a stable hash of the name into this list.")]
        public Color[] nameColors = {
            Hex(0xFF6B7A), Hex(0x4DB5FF), Hex(0x3DDC84), Hex(0xB98CFF),
            Hex(0xFF9F5A), Hex(0xFFD54A), Hex(0xFF8CC6), Hex(0x6FE3D8),
        };

        [Header("Type")]
        public TMP_FontAsset regularFont;
        public TMP_FontAsset semiBoldFont;
        public TMP_FontAsset boldFont;

        [Header("Shapes")]
        [Tooltip("9-sliced rounded rectangle every panel, button and pill is drawn with. Each element sets its own corner radius.")]
        public Sprite roundedSprite;
        [Tooltip("Soft blurred rounded rectangle drawn under raised panels.")]
        public Sprite shadowSprite;

        [Header("Metrics (applied when prefabs are rebuilt)")]
        [Tooltip("Canvas reference resolution every shared canvas scales from.")]
        public Vector2 referenceResolution = new(1920f, 1080f);
        [Tooltip("Gap between the screen edge and the floating HUD.")]
        public float screenMargin = 12f;
        [Tooltip("Height of top bar buttons and pills.")]
        public float topbarHeight = 40f;
        public float bodyFontSize = 16f;
        public float smallFontSize = 13f;
        public float titleFontSize = 22f;

        [Header("Motion")]
        [Min(0f)] public float fadeDuration = 0.16f;

        public Color Get(UIColorRole role) => role switch {
            UIColorRole.Surface => surface,
            UIColorRole.SurfaceRaised => surfaceRaised,
            UIColorRole.SurfaceInset => surfaceInset,
            UIColorRole.SurfaceHover => surfaceHover,
            UIColorRole.Stroke => stroke,
            UIColorRole.Scrim => scrim,
            UIColorRole.TextPrimary => textPrimary,
            UIColorRole.TextSecondary => textSecondary,
            UIColorRole.TextMuted => textMuted,
            UIColorRole.Accent => accent,
            UIColorRole.OnAccent => onAccent,
            UIColorRole.Success => success,
            UIColorRole.Warning => warning,
            UIColorRole.Danger => danger,
            UIColorRole.Currency => currency,
            UIColorRole.SystemText => systemText,
            _ => Color.magenta,
        };

        /// <summary>A stable colour for a player name, so the same player is always the same colour.</summary>
        public Color NameColor(string name) {
            if (nameColors == null || nameColors.Length == 0 || string.IsNullOrEmpty(name))
                return textPrimary;
            unchecked {
                int hash = 17;
                foreach (char c in name)
                    hash = hash * 31 + c;
                return nameColors[(hash & int.MaxValue) % nameColors.Length];
            }
        }

        /// <summary>Raised whenever a theme asset is edited in the inspector.</summary>
        public static event System.Action<UITheme> Changed;

        static UITheme active;

        /// <summary>The theme the game runs with. Falls back to the built-in defaults if the asset is missing.</summary>
        public static UITheme Active {
            get {
                if (active == null) {
                    active = Resources.Load<UITheme>(ResourcePath);
                    if (active == null) {
                        active = CreateInstance<UITheme>();
                        active.hideFlags = HideFlags.DontSave;
                    }
                }
                return active;
            }
        }

        void OnValidate() => Changed?.Invoke(this);

        public static Color Hex(int rgb, float alpha = 1f) =>
            new(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, alpha);

        public static string ToRichTextHex(Color color) => ColorUtility.ToHtmlStringRGB(color);
    }
}
