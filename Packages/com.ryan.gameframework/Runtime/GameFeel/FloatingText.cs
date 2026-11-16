using TMPro;
using UnityEngine;

namespace RyanAssets.GameFeel {
    /// <summary>
    /// Pooled world-space popups that pop in, rise and fade: damage numbers, "+$120", "CHECKPOINT".
    /// They face the camera, draw over the world, and keep a readable size at any distance.
    /// <code>
    /// FloatingText.Damage(hitPoint, 35);
    /// FloatingText.Money(padPos, 120);                       // "+$120"
    /// FloatingText.Callout(flagPos, "CHECKPOINT");
    /// FloatingText.Spawn(pos, "COMBO x3", Color.cyan, 1.2f);
    /// FloatingText.Number(pos, "+{0} XP", xp, Color.green);  // no string allocation
    /// </code>
    /// Styles (size, rise, life, colours) live in GameFeelSettings &gt; Floating Text.
    /// </summary>
    public static class FloatingText {
        /// <summary>Shows <paramref name="text"/> in the default style, recoloured and scaled.</summary>
        public static void Spawn(Vector3 position, string text, Color color, float size = 1f) {
            GameFeelSettings settings = GameFeelSettings.Active;
            if (settings != null)
                Spawn(position, text, settings.floatingText.normal.WithColor(color).Scaled(size));
        }

        /// <summary>Shows <paramref name="text"/> in a style of your own.</summary>
        public static void Spawn(Vector3 position, string text, in FloatingTextStyle style) {
            TMP_Text label = Take(position, style);
            if (label != null)
                label.SetText(text);
        }

        /// <summary>
        /// Shows a formatted number without allocating, e.g. <c>Number(pos, "+{0}", 5, color)</c> or
        /// <c>"{0:1}s"</c> for one decimal (TextMeshPro format rules).
        /// </summary>
        public static void Number(Vector3 position, string format, float value, Color color, float size = 1f) {
            GameFeelSettings settings = GameFeelSettings.Active;
            if (settings == null)
                return;
            TMP_Text label = Take(position, settings.floatingText.normal.WithColor(color).Scaled(size));
            if (label != null)
                label.SetText(format, value);
        }

        /// <summary>A red damage number ("35").</summary>
        public static void Damage(Vector3 position, float amount, float size = 1f) => Styled(position, "{0}", amount, s => s.damage, size);

        /// <summary>A gold money popup ("+$120").</summary>
        public static void Money(Vector3 position, float amount, float size = 1f) => Styled(position, "+${0}", amount, s => s.money, size);

        /// <summary>A green heal popup ("+25").</summary>
        public static void Heal(Vector3 position, float amount, float size = 1f) => Styled(position, "+{0}", amount, s => s.heal, size);

        /// <summary>A big word or two that hangs a little longer ("CHECKPOINT", "LEVEL UP").</summary>
        public static void Callout(Vector3 position, string text, float size = 1f) {
            GameFeelSettings settings = GameFeelSettings.Active;
            if (settings != null)
                Spawn(position, text, settings.floatingText.callout.Scaled(size));
        }

        /// <summary>A callout in another colour.</summary>
        public static void Callout(Vector3 position, string text, Color color, float size = 1f) {
            GameFeelSettings settings = GameFeelSettings.Active;
            if (settings != null)
                Spawn(position, text, settings.floatingText.callout.WithColor(color).Scaled(size));
        }

        static void Styled(Vector3 position, string format, float value, System.Func<GameFeelSettings.FloatingTextTuning, FloatingTextStyle> pick, float size) {
            GameFeelSettings settings = GameFeelSettings.Active;
            if (settings == null)
                return;
            TMP_Text label = Take(position, pick(settings.floatingText).Scaled(size));
            if (label != null)
                label.SetText(format, value);
        }

        static TMP_Text Take(Vector3 position, in FloatingTextStyle style) {
            GameFeelRunner runner = GameFeelRunner.Get();
            return runner != null ? runner.Texts.Spawn(position, style) : null;
        }
    }
}
