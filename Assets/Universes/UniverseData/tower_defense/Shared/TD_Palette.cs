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
            new(0.62f, 0.5f, 1f),    // Tesla
            new(0.36f, 0.56f, 1f),   // Artillery (the Mortar kind): a team blue, as no enemy is painted (they are army green and desert tan)
        };

        static readonly Color[] Enemies = {
            new(0.72f, 0.85f, 0.45f), // Scout
            new(0.95f, 0.75f, 0.35f), // APC
            new(0.95f, 0.5f, 0.3f),   // Tank
            new(0.55f, 0.75f, 1f),    // Chopper
            new(0.9f, 0.3f, 0.35f),   // Heavy Tank
            new(0.4f, 0.85f, 0.95f),  // Shield Tank
            new(0.45f, 0.95f, 0.6f),  // Medic
            new(0.85f, 0.55f, 0.95f), // Carrier
            new(1f, 0.92f, 0.45f),    // Drone
        };

        /// <summary>Energy shields (bubbles, the shield bar, the break flash).</summary>
        public static readonly Color Shield = new(0.4f, 0.85f, 1f);
        /// <summary>Medic heal sparkles and the healed popup.</summary>
        public static readonly Color Heal = new(0.4f, 1f, 0.55f);
        /// <summary>Star ratings (filled; empty stars use the theme's muted colour).</summary>
        public static readonly Color Star = new(1f, 0.82f, 0.25f);

        /// <summary>Accent per mode (vote cards, the wave title, the result card).</summary>
        static readonly Color[] Modes = {
            new(0.45f, 0.85f, 0.5f), // Normal
            new(0.95f, 0.38f, 0.32f), // Hard
            new(0.68f, 0.5f, 1f),    // Endless
        };

        /// <summary>Accent per ability (pills, the strike marker, the shockwave).</summary>
        static readonly Color[] Abilities = {
            new(1f, 0.55f, 0.25f), // Airstrike
            new(0.45f, 0.75f, 1f), // EMP
        };

        public static readonly Color HealthHigh = new(0.35f, 0.9f, 0.45f);
        public static readonly Color HealthMid = new(1f, 0.8f, 0.25f);
        public static readonly Color HealthLow = new(0.95f, 0.3f, 0.3f);

        public static Color Tower(TD_TowerKind kind) => Towers[(int)kind];
        public static Color Enemy(TD_EnemyKind kind) => Enemies[(int)kind];
        public static Color Mode(TD_Difficulty mode) => Modes[(int)mode];
        public static Color Ability(TD_Ability ability) => Abilities[(int)ability];

        /// <summary>Green when healthy, amber at half, red when nearly destroyed.</summary>
        public static Color Health(float fraction) => fraction > 0.5f
            ? Color.Lerp(HealthMid, HealthHigh, (fraction - 0.5f) * 2f)
            : Color.Lerp(HealthLow, HealthMid, fraction * 2f);

        /// <summary>Icon file names (in UI/Icons, without "td_" or extension) per tower and enemy.</summary>
        public static string TowerIcon(TD_TowerKind kind) => "tower_" + kind.ToString().ToLowerInvariant();
        public static string EnemyIcon(TD_EnemyKind kind) => "enemy_" + kind.ToString().ToLowerInvariant();
        public static string ModeIcon(TD_Difficulty mode) => "mode_" + mode.ToString().ToLowerInvariant();
        public static string AbilityIcon(TD_Ability ability) => "ability_" + ability.ToString().ToLowerInvariant();
    }
}
