namespace Universes.UniverseData.tower_defense {
    /// <summary>
    /// Which enemy in range a tower shoots. A wire enum (it rides in <see cref="TD_TowerSnap"/> and
    /// <see cref="TD_TargetRequest"/>): APPEND-ONLY, never renumber.
    /// </summary>
    public enum TD_TargetMode : byte {
        /// <summary>The enemy furthest along the road (closest to the base).</summary>
        First,
        /// <summary>The enemy least far along the road (just came into range).</summary>
        Last,
        /// <summary>The enemy with the most health plus shield left.</summary>
        Strong,
        /// <summary>The enemy nearest to the tower.</summary>
        Close,
    }

    /// <summary>Names, defaults and cycling order of <see cref="TD_TargetMode"/>.</summary>
    public static class TD_Targeting {
        public const int Count = 4;

        public static bool IsValid(TD_TargetMode mode) => (int)mode < Count;

        /// <summary>
        /// What a new tower of <paramref name="kind"/> aims at: Snipers pick off the toughest enemy (their
        /// slow, heavy, armour-piercing shots are wasted on scouts), everything else the enemy about to leak.
        /// </summary>
        public static TD_TargetMode Default(TD_TowerKind kind) => kind == TD_TowerKind.Sniper ? TD_TargetMode.Strong : TD_TargetMode.First;

        /// <summary>The mode after <paramref name="mode"/> when a player cycles it.</summary>
        public static TD_TargetMode Next(TD_TargetMode mode) => (TD_TargetMode)(((int)mode + 1) % Count);

        public static string Name(TD_TargetMode mode) => mode switch {
            TD_TargetMode.Last => "Last",
            TD_TargetMode.Strong => "Strongest",
            TD_TargetMode.Close => "Closest",
            _ => "First",
        };

        /// <summary>One line on what the mode does, for the toast when a player switches to it.</summary>
        public static string Blurb(TD_TargetMode mode) => mode switch {
            TD_TargetMode.Last => "shoots the newest enemy in range",
            TD_TargetMode.Strong => "shoots the toughest enemy in range",
            TD_TargetMode.Close => "shoots the nearest enemy",
            _ => "shoots the enemy closest to the base",
        };

        /// <summary>
        /// Whether a tower in <paramref name="mode"/> prefers <paramref name="candidate"/> over <paramref name="current"/>.
        /// Ties fall back to the one further along the road, so every mode still protects the base.
        /// </summary>
        public static bool Prefers(TD_TargetMode mode, TD_SimEnemy candidate, TD_SimEnemy current, float candidateDistanceSqr, float currentDistanceSqr) {
            switch (mode) {
                case TD_TargetMode.Last:
                    if (candidate.distance != current.distance) return candidate.distance < current.distance;
                    break;
                case TD_TargetMode.Strong: {
                    float a = candidate.health + candidate.shield, b = current.health + current.shield;
                    if (!UnityEngine.Mathf.Approximately(a, b)) return a > b;
                    break;
                }
                case TD_TargetMode.Close:
                    if (!UnityEngine.Mathf.Approximately(candidateDistanceSqr, currentDistanceSqr)) return candidateDistanceSqr < currentDistanceSqr;
                    break;
            }
            return candidate.distance > current.distance;
        }

        /// <summary>"12.4k" style short number for damage totals on the tower card.</summary>
        public static string Short(long value) =>
            value >= 1_000_000 ? $"{value / 1_000_000f:0.#}M" : value >= 10_000 ? $"{value / 1000f:0}k" : value >= 1000 ? $"{value / 1000f:0.#}k" : value.ToString();
    }
}
