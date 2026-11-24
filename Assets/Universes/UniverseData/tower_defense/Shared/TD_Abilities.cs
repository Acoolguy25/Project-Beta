using UnityEngine;

namespace Universes.UniverseData.tower_defense {
    /// <summary>Player-called powers, unlocked by level. A wire enum: APPEND-ONLY.</summary>
    public enum TD_Ability : byte { Airstrike, Emp }

    public readonly struct TD_AbilityStats {
        public readonly string name;
        public readonly string unlockId;
        /// <summary>Waves between two uses by the same player (1 = once every wave).</summary>
        public readonly int cooldownWaves;
        /// <summary>Airstrike: damage at wave 1 before the wave's health scale (it grows with the enemies).</summary>
        public readonly float damage;
        /// <summary>Airstrike: blast radius around the lead enemy (metres).</summary>
        public readonly float radius;
        /// <summary>EMP: seconds every enemy stands still.</summary>
        public readonly float stunSeconds;
        public readonly string blurb;

        public TD_AbilityStats(string name, string unlockId, int cooldownWaves, float damage, float radius, float stunSeconds, string blurb) {
            this.name = name; this.unlockId = unlockId; this.cooldownWaves = cooldownWaves; this.damage = damage;
            this.radius = radius; this.stunSeconds = stunSeconds; this.blurb = blurb;
        }
    }

    /// <summary>
    /// Airstrike (a jet bombs the enemies nearest the base) and EMP (everything stops and loses its
    /// shield). Each player has their own, recharging by waves; the server checks every use.
    /// </summary>
    public static class TD_Abilities {
        public const int Count = 2;

        static readonly TD_AbilityStats[] Abilities = {
            new("Airstrike", TD_Retention.AirstrikeUnlock, 1, 260f, 11f, 0f, "Bombs the enemies nearest the base"),
            new("EMP Blast", TD_Retention.EmpUnlock, 3, 0f, 0f, 3f, "Stuns every enemy, strips all shields"),
        };

        public static TD_AbilityStats Get(TD_Ability ability) => Abilities[(int)ability];

        public static bool IsValid(TD_Ability ability) => (int)ability < Abilities.Length;

        /// <summary>
        /// Each Airstrike after the first in a wave does this share of the one before (and each EMP stuns this
        /// share as long): abilities are per player but the army is not, so four players calling four strikes
        /// a wave must not flatten Hard. One player alone is never affected.
        /// </summary>
        public const float RepeatFalloff = 0.6f;

        /// <summary>Share of full power for the <paramref name="earlier"/>+1-th use of an ability in one wave (1 for the first).</summary>
        public static float RepeatScale(int earlier) => Mathf.Pow(RepeatFalloff, Mathf.Max(0, earlier));

        /// <summary>Airstrike damage in <paramref name="wave"/> of <paramref name="mode"/>: it keeps pace with enemy health.</summary>
        public static float StrikeDamage(int wave, TD_Difficulty mode) => Get(TD_Ability.Airstrike).damage * TD_Rules.HealthScale(wave, mode);

        /// <summary>
        /// Whether a player who last used <paramref name="ability"/> in <paramref name="lastWave"/> (0 = never)
        /// may use it in <paramref name="wave"/>.
        /// </summary>
        public static bool Ready(TD_Ability ability, int lastWave, int wave) => lastWave <= 0 || wave - lastWave >= Get(ability).cooldownWaves;

        /// <summary>Waves until it is ready again (0 = ready now).</summary>
        public static int WavesLeft(TD_Ability ability, int lastWave, int wave) =>
            Ready(ability, lastWave, wave) ? 0 : Get(ability).cooldownWaves - (wave - lastWave);
    }
}
