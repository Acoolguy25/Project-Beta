using UnityEngine;

namespace Universes.UniverseData.tower_defense {
    /// <summary>
    /// The colour each tower and enemy is identified by, shared by the world (tower trims, shots) and
    /// the HUD (shop cards, wave preview), so a frost tower is the same blue everywhere.
    /// </summary>
    public static class TD_Palette {
        static readonly Color[] Towers = {
            new(1f, 0.78f, 0.25f),   // Gunner
            new(1f, 0.5f, 0.24f),    // Cannon
            new(0.45f, 0.83f, 1f),   // Frost
            new(0.93f, 0.33f, 0.4f), // Sniper
        };

        static readonly Color[] Enemies = {
            new(0.72f, 0.85f, 0.45f), // Scout
            new(0.95f, 0.75f, 0.35f), // APC
            new(0.95f, 0.5f, 0.3f),   // Tank
            new(0.55f, 0.75f, 1f),    // Chopper
            new(0.9f, 0.3f, 0.35f),   // Heavy Tank
        };

        public static readonly Color HealthHigh = new(0.35f, 0.9f, 0.45f);
        public static readonly Color HealthMid = new(1f, 0.8f, 0.25f);
        public static readonly Color HealthLow = new(0.95f, 0.3f, 0.3f);

        public static Color Tower(TD_TowerKind kind) => Towers[(int)kind];
        public static Color Enemy(TD_EnemyKind kind) => Enemies[(int)kind];

        /// <summary>Green when healthy, amber at half, red when nearly destroyed.</summary>
        public static Color Health(float fraction) => fraction > 0.5f
            ? Color.Lerp(HealthMid, HealthHigh, (fraction - 0.5f) * 2f)
            : Color.Lerp(HealthLow, HealthMid, fraction * 2f);

        /// <summary>Icon file names (in UI/Icons, without "td_" or extension) per tower and enemy.</summary>
        public static string TowerIcon(TD_TowerKind kind) => "tower_" + kind.ToString().ToLowerInvariant();
        public static string EnemyIcon(TD_EnemyKind kind) => "enemy_" + kind.ToString().ToLowerInvariant();
    }
}
