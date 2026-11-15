using UnityEngine;

namespace RyanAssets.Rounds {
    /// <summary>Text helpers every round-based game shows on its HUD and in its top message.</summary>
    public static class RoundFormat {
        /// <summary>m:ss, or m:ss.t with <paramref name="tenths"/>.</summary>
        public static string Clock(float seconds, bool tenths = false) {
            seconds = Mathf.Max(0f, seconds);
            int minutes = (int)(seconds / 60f);
            float rest = seconds - minutes * 60f;
            return tenths ? $"{minutes}:{rest:00.0}" : $"{minutes}:{(int)rest:00}";
        }

        public static string Ordinal(int n) => n + (n % 100 is 11 or 12 or 13 ? "th" : (n % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" });

        /// <summary>Short money text: 950, 12.5K, 3.2M, 1.1B.</summary>
        public static string Money(double value) {
            double abs = System.Math.Abs(value);
            if (abs < 10_000) return ((long)value).ToString();
            if (abs < 1_000_000) return (value / 1_000).ToString("0.#") + "K";
            if (abs < 1_000_000_000) return (value / 1_000_000).ToString("0.##") + "M";
            return (value / 1_000_000_000).ToString("0.##") + "B";
        }

        public static string Plural(int count, string one, string many = null) => count == 1 ? $"1 {one}" : $"{count} {many ?? one + "s"}";
    }
}
