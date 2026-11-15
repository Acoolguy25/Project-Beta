using System;
using System.Collections.Generic;
using RyanAssets.Shared.Declarations;
using UnityEngine;

namespace Universes.UniverseData.war_valley.Shared {
    /// <summary>
    /// How hard the waves get, and how fast. Serialized on the server runner so a designer tunes
    /// difficulty in the Inspector rather than hand-writing every wave.
    /// </summary>
    [Serializable]
    public sealed class WV_WaveTuning {
        [Tooltip("Waves in a round. Surviving the last one wins it.")]
        [Min(1)] public int WaveCount = 20;

        [Tooltip("Strength of the first wave, in knifemen. Tougher troops are worth more.")]
        [Min(1f)] public float BaseStrength = 4f;

        [Tooltip("Strength added every wave.")]
        [Min(0f)] public float StrengthPerWave = 1.5f;

        [Tooltip("Extra strength that compounds: later waves grow faster than early ones.")]
        [Min(0f)] public float StrengthAcceleration = 0.06f;

        [Tooltip("Every Nth wave is a surge: bigger, and announced. 0 turns surges off.")]
        [Min(0)] public int SurgeEvery = 5;

        [Tooltip("How much bigger a surge wave is.")]
        [Min(1f)] public float SurgeMultiplier = 1.3f;

        [Tooltip("Extra enemy health per wave, as a fraction: 0.06 is +6% a wave.")]
        [Min(0f)] public float HealthGrowthPerWave = 0.03f;

        [Tooltip("Most enemies one wave may field, so late waves stay playable.")]
        [Min(1)] public int MaxEnemiesPerWave = 40;

        [Tooltip("Maximum living wave enemies before spawning waits for the defenders to catch up.")]
        [Min(1)] public int MaxActiveEnemies = 60;

        [Tooltip("Preparation time before the first wave, enough to build and train a starter squad.")]
        [Min(5)] public int PreparationSeconds = 55;

        [Tooltip("Seconds from the start of one wave to the next, early on.")]
        [Min(5)] public int FirstIntermissionSeconds = 95;

        [Tooltip("Shortest gap between waves, reached as the round goes on.")]
        [Min(5)] public int MinimumIntermissionSeconds = 65;

        [Tooltip("Seconds taken off the gap each wave until it reaches the minimum.")]
        [Min(0f)] public float IntermissionShrinkPerWave = 1.5f;

        [Tooltip("Every Nth wave brings a Warlord boss, and so does the final wave. 0 leaves bosses " +
                 "to the final wave alone.")]
        [Min(0)] public int BossEvery = 10;

        [Tooltip("A Warlord's health, in multiples of the wave's own enemy health.")]
        [Min(1f)] public float BossHealthMultiplier = 12f;

        [Tooltip("Funds paid to every commander for each wave they hold off, before the per-wave growth.")]
        [Min(0)] public int WaveBountyBase = 150;

        [Tooltip("Extra wave bounty per wave number, so holding later waves pays more.")]
        [Min(0)] public int WaveBountyPerWave = 25;

        [Tooltip("Funds paid to every commander when the last wave is beaten.")]
        [Min(0)] public int VictoryBounty = 2500;
    }

    /// <summary>
    /// The flavour of a wave: which troops it leans on, which approaches it uses, and how fast it
    /// streams in. Themes rotate so consecutive waves ask for different defences, and a wave is
    /// announced by its theme so the commanders know what is coming before it arrives.
    /// </summary>
    public enum WV_WaveTheme : byte {
        /// <summary>A mix of everything unlocked, led by the newest kind.</summary>
        Standard = 0,
        /// <summary>Lots of small, cheap melee troops: more bodies for the same strength.</summary>
        Swarm = 1,
        /// <summary>Fast runners that stream in quickly.</summary>
        Blitz = 2,
        /// <summary>Gunners and Skinny Legends: a ranged wave that out-shoots walls of melee.</summary>
        Marksmen = 3,
        /// <summary>Heavy bruisers from a single approach, slowly, around a Warlord boss.</summary>
        Siege = 4,
        /// <summary>A surge from every approach at once.</summary>
        Pincer = 5
    }

    /// <summary>The four ways into the valley, by compass direction. Wave groups each arrive along one.</summary>
    public enum WV_Approach : byte {
        West = 0,
        North = 1,
        South = 2,
        East = 3
    }

    /// <summary>A wave's boss: one oversized troop with a name of its own and many times the health.</summary>
    public sealed class WV_WaveBoss {
        public const string DefaultName = "Warlord";

        public WV_TroopKind Kind { get; }
        public string DisplayName { get; }
        /// <summary>Health in multiples of the wave's own enemy health.</summary>
        public float HealthMultiplier { get; }
        /// <summary>The boss's body, derived from its kind's build but larger and harder-hitting.</summary>
        public CharacterBuild Build { get; }

        public WV_WaveBoss(WV_TroopKind kind, string displayName, float healthMultiplier, CharacterBuild build) {
            Kind = kind;
            DisplayName = displayName;
            HealthMultiplier = healthMultiplier;
            Build = build;
        }
    }

    /// <summary>One batch of a single troop kind within a wave.</summary>
    public readonly struct WV_WaveGroup {
        public readonly WV_TroopKind Kind;
        public readonly int Count;

        public WV_WaveGroup(WV_TroopKind kind, int count) {
            Kind = kind;
            Count = count;
        }
    }

    /// <summary>Everything the runner needs to field one wave.</summary>
    public sealed class WV_Wave {
        /// <summary>Counting from 1.</summary>
        public int Number { get; }
        public IReadOnlyList<WV_WaveGroup> Groups { get; }
        public float HealthMultiplier { get; }
        public int IntermissionSeconds { get; }
        public bool IsSurge { get; }
        /// <summary>Troops in the groups, not counting the boss.</summary>
        public int TotalCount { get; }
        public WV_WaveTheme Theme { get; }
        /// <summary>The boss this wave brings, or null.</summary>
        public WV_WaveBoss Boss { get; }
        /// <summary>Multiplies the gap between one enemy arriving and the next: below 1 streams faster.</summary>
        public float SpawnSpacingMultiplier { get; }
        /// <summary>Which approach each entry of <see cref="Groups"/> arrives along, parallel to it. The boss arrives with the first.</summary>
        public IReadOnlyList<WV_Approach> Approaches { get; }

        public WV_Wave(int number, IReadOnlyList<WV_WaveGroup> groups, float healthMultiplier, int intermissionSeconds, bool isSurge)
            : this(number, groups, healthMultiplier, intermissionSeconds, isSurge, WV_WaveTheme.Standard, null, 1f, null) { }

        public WV_Wave(
            int number, IReadOnlyList<WV_WaveGroup> groups, float healthMultiplier, int intermissionSeconds, bool isSurge,
            WV_WaveTheme theme, WV_WaveBoss boss, float spawnSpacingMultiplier, IReadOnlyList<WV_Approach> approaches) {
            Number = number;
            Groups = groups;
            HealthMultiplier = healthMultiplier;
            IntermissionSeconds = intermissionSeconds;
            IsSurge = isSurge;
            Theme = theme;
            Boss = boss;
            SpawnSpacingMultiplier = Mathf.Max(0.1f, spawnSpacingMultiplier);
            int total = 0;
            foreach (WV_WaveGroup group in groups)
                total += group.Count;
            TotalCount = total;
            if (approaches == null || approaches.Count != groups.Count) {
                var fallback = new WV_Approach[groups.Count];
                for (int i = 0; i < fallback.Length; i++)
                    fallback[i] = (WV_Approach)(i % WV_WavePlan.ApproachCount);
                approaches = fallback;
            }
            Approaches = approaches;
        }

        /// <summary>Everything this wave throws at the flag, the boss included.</summary>
        public int EnemyCount => TotalCount + (Boss != null ? 1 : 0);

        /// <summary>The approaches this wave uses, without repeats, in the order its groups arrive.</summary>
        public IReadOnlyList<WV_Approach> DistinctApproaches {
            get {
                var distinct = new List<WV_Approach>();
                foreach (WV_Approach approach in Approaches) {
                    if (!distinct.Contains(approach))
                        distinct.Add(approach);
                }
                return distinct;
            }
        }
    }

    /// <summary>
    /// Builds each wave from its number, so the round gets progressively harder without a hand-
    /// written table.
    /// <para>
    /// A wave has a strength budget that grows every wave and faster as the round goes on. New
    /// kinds of troop join the waves as they unlock (<see cref="WV_TroopProfile.FirstWave"/>) - the
    /// Shrimp swarm, then the Speedy, the Skinny Legend, and finally the Punk - and each kind is
    /// paid for from the budget at its threat, so a wave of Punks is small and a wave of Shrimp is
    /// large. The newest kind leads the wave it arrives in, earlier kinds keep a share, and enemy
    /// health climbs a little every wave on top. Every few waves a surge wave is bigger again.
    /// </para>
    /// <para>
    /// On top of the budget each wave has a theme (<see cref="WV_WaveTheme"/>) that decides which
    /// troops lead it, which of the valley's approaches it uses, and how fast it streams in, and
    /// every tenth wave and the final one bring a Warlord boss.
    /// </para>
    /// <para>
    /// Deterministic: the same number and tuning always give the same wave, which is what lets the
    /// difficulty curve be tested and tuned.
    /// </para>
    /// </summary>
    public static class WV_WavePlan {
        /// <summary>How many approaches the valley has (<see cref="WV_Approach"/>).</summary>
        public const int ApproachCount = 4;

        /// <summary>Share of a themed wave's budget spent on the theme's featured kinds.</summary>
        const float FeaturedShare = 0.65f;

        /// <summary>A Swarm brings this much more strength, paid for in its cheapest soldiers.</summary>
        const float SwarmStrengthBonus = 1.25f;

        /// <summary>Themes that rotate through ordinary waves, in the order they first become possible.</summary>
        static readonly WV_WaveTheme[] RotatingThemes = {
            WV_WaveTheme.Standard, WV_WaveTheme.Swarm, WV_WaveTheme.Marksmen, WV_WaveTheme.Blitz
        };

        public static WV_Wave Build(int waveNumber, WV_WaveTuning tuning) {
            if (tuning == null)
                throw new ArgumentNullException(nameof(tuning));

            waveNumber = Mathf.Max(1, waveNumber);
            int step = waveNumber - 1;
            bool surge = IsSurge(waveNumber, tuning);
            bool boss = IsBossWave(waveNumber, tuning);
            WV_WaveTheme theme = GetTheme(waveNumber, tuning);

            float strength = GetStrength(waveNumber, tuning);
            if (theme == WV_WaveTheme.Swarm)
                strength *= SwarmStrengthBonus;
            List<WV_WaveGroup> groups = Compose(waveNumber, strength, GetFeaturedKinds(theme));
            groups = CapCount(groups, Mathf.Max(1, tuning.MaxEnemiesPerWave - (boss ? 1 : 0)));

            float health = 1f + Mathf.Max(0f, tuning.HealthGrowthPerWave) * step;
            int intermission = Mathf.Max(
                Mathf.Max(5, tuning.MinimumIntermissionSeconds),
                Mathf.RoundToInt(tuning.FirstIntermissionSeconds - tuning.IntermissionShrinkPerWave * step));
            return new WV_Wave(
                waveNumber, groups, health, intermission, surge, theme,
                boss ? BuildBoss(tuning) : null, GetSpawnSpacingMultiplier(theme),
                AssignApproaches(waveNumber, theme, groups.Count));
        }

        public static bool IsSurge(int waveNumber, WV_WaveTuning tuning) =>
            tuning.SurgeEvery > 0 && waveNumber % tuning.SurgeEvery == 0;

        /// <summary>Whether a Warlord comes with this wave: every <see cref="WV_WaveTuning.BossEvery"/>th wave, and the last.</summary>
        public static bool IsBossWave(int waveNumber, WV_WaveTuning tuning) =>
            waveNumber == tuning.WaveCount || (tuning.BossEvery > 0 && waveNumber % tuning.BossEvery == 0);

        /// <summary>
        /// The theme of a wave. Boss waves are sieges and surges are pincers; the first two waves are
        /// always standard so new commanders meet the basics first; every other wave rotates through
        /// the themes whose troops have already joined the waves, never repeating the wave before.
        /// </summary>
        public static WV_WaveTheme GetTheme(int waveNumber, WV_WaveTuning tuning) {
            if (IsBossWave(waveNumber, tuning))
                return WV_WaveTheme.Siege;
            if (IsSurge(waveNumber, tuning))
                return WV_WaveTheme.Pincer;
            if (waveNumber <= 2)
                return WV_WaveTheme.Standard;

            var available = new List<WV_WaveTheme>();
            foreach (WV_WaveTheme candidate in RotatingThemes) {
                if (IsThemeUnlocked(candidate, waveNumber))
                    available.Add(candidate);
            }
            // Stepping by a number coprime to most list lengths makes the order feel shuffled while
            // staying deterministic, and the previous wave's theme is skipped so no theme repeats.
            WV_WaveTheme theme = available[(waveNumber * 3) % available.Count];
            if (available.Count > 1 && theme == GetTheme(waveNumber - 1, tuning))
                theme = available[(waveNumber * 3 + 1) % available.Count];
            return theme;
        }

        /// <summary>A theme rotates in once every troop it leans on fights in waves.</summary>
        static bool IsThemeUnlocked(WV_WaveTheme theme, int waveNumber) {
            foreach (WV_TroopKind kind in GetFeaturedKinds(theme)) {
                if (WV_TroopCatalog.Get(kind).FirstWave > waveNumber)
                    return false;
            }
            return true;
        }

        /// <summary>The troops a theme spends most of its budget on. Empty means the standard mix.</summary>
        public static IReadOnlyList<WV_TroopKind> GetFeaturedKinds(WV_WaveTheme theme) => theme switch {
            WV_WaveTheme.Swarm => new[] { WV_TroopKind.Shrimp, WV_TroopKind.Knife },
            WV_WaveTheme.Blitz => new[] { WV_TroopKind.Speedy },
            WV_WaveTheme.Marksmen => new[] { WV_TroopKind.Gunner, WV_TroopKind.SkinnyLegend },
            WV_WaveTheme.Siege => new[] { WV_TroopKind.Punk, WV_TroopKind.Knife },
            _ => Array.Empty<WV_TroopKind>()
        };

        public static float GetSpawnSpacingMultiplier(WV_WaveTheme theme) => theme switch {
            WV_WaveTheme.Blitz => 0.45f,
            WV_WaveTheme.Swarm => 0.6f,
            WV_WaveTheme.Siege => 1.4f,
            _ => 1f
        };

        /// <summary>
        /// Which approach each group arrives along. A siege comes from one side; a pincer and a
        /// blitz spread over as many sides as they have groups; everything else uses two adjacent
        /// sides, so a single wall rarely covers a whole wave. The starting side moves every wave.
        /// </summary>
        public static WV_Approach[] AssignApproaches(int waveNumber, WV_WaveTheme theme, int groupCount) {
            var approaches = new WV_Approach[groupCount];
            int first = (waveNumber * 5 + 1) % ApproachCount;
            for (int i = 0; i < groupCount; i++) {
                int side = theme switch {
                    WV_WaveTheme.Siege => first,
                    WV_WaveTheme.Pincer or WV_WaveTheme.Blitz => first + i,
                    _ => first + i % 2
                };
                approaches[i] = (WV_Approach)(side % ApproachCount);
            }
            return approaches;
        }

        static WV_WaveBoss BuildBoss(WV_WaveTuning tuning) {
            CharacterBuild punk = WV_TroopCatalog.Get(WV_TroopKind.Punk).Build;
            var build = new CharacterBuild(
                punk.Proportions * 1.45f, health: punk.HealthMultiplier,
                speed: punk.SpeedMultiplier * 0.9f, damage: punk.DamageMultiplier * 1.6f);
            return new WV_WaveBoss(WV_TroopKind.Punk, WV_WaveBoss.DefaultName,
                Mathf.Max(1f, tuning.BossHealthMultiplier), build);
        }

        /// <summary>Funds each commander is paid for holding off wave <paramref name="waveNumber"/>.</summary>
        public static long GetWaveBounty(int waveNumber, WV_WaveTuning tuning) =>
            Math.Max(0, tuning.WaveBountyBase) + (long)Math.Max(0, tuning.WaveBountyPerWave) * Mathf.Max(0, waveNumber - 1);

        /// <summary>"north and east", "every side" - for the wave announcement.</summary>
        public static string DescribeApproaches(WV_Wave wave) {
            IReadOnlyList<WV_Approach> sides = wave.DistinctApproaches;
            if (sides.Count >= ApproachCount)
                return "every side";
            var names = new List<string>(sides.Count);
            foreach (WV_Approach side in sides)
                names.Add("the " + side.ToString().ToLowerInvariant());
            return names.Count switch {
                0 => "nowhere",
                1 => names[0],
                2 => names[0] + " and " + names[1],
                _ => string.Join(", ", names.GetRange(0, names.Count - 1)) + " and " + names[names.Count - 1]
            };
        }

        public static string GetThemeName(WV_WaveTheme theme) => theme switch {
            WV_WaveTheme.Swarm => "Swarm",
            WV_WaveTheme.Blitz => "Blitz",
            WV_WaveTheme.Marksmen => "Marksmen",
            WV_WaveTheme.Siege => "Siege",
            WV_WaveTheme.Pincer => "Pincer",
            _ => "Assault"
        };

        /// <summary>One line of advice per theme, so a commander knows how to answer it.</summary>
        public static string GetThemeHint(WV_WaveTheme theme) => theme switch {
            WV_WaveTheme.Swarm => "masses of small melee troops - turrets and gunners mow them down",
            WV_WaveTheme.Blitz => "fast runners streaming in - wall the gaps before they reach the flag",
            WV_WaveTheme.Marksmen => "ranged troops - close in with vehicles, they out-shoot melee",
            WV_WaveTheme.Siege => "heavy bruisers on one side - mass everything there",
            WV_WaveTheme.Pincer => "attacks from every side at once - spread your defence",
            _ => "a mixed assault"
        };

        /// <summary>The announcement for a wave as it begins.</summary>
        public static string Describe(WV_Wave wave, int waveCount) {
            string header = $"Wave {wave.Number}/{waveCount} - {GetThemeName(wave.Theme)}";
            string boss = wave.Boss != null ? $", led by a {wave.Boss.DisplayName}" : string.Empty;
            return $"{header}: {wave.EnemyCount} enemies from {DescribeApproaches(wave)}{boss} ({GetThemeHint(wave.Theme)})";
        }

        /// <summary>The wave's strength budget, in knifemen.</summary>
        public static float GetStrength(int waveNumber, WV_WaveTuning tuning) {
            int step = Mathf.Max(0, waveNumber - 1);
            float strength = tuning.BaseStrength
                             + tuning.StrengthPerWave * step
                             + tuning.StrengthAcceleration * step * step;
            return IsSurge(waveNumber, tuning) ? strength * tuning.SurgeMultiplier : strength;
        }

        /// <summary>
        /// Splits the budget across every kind already unlocked, and every unlocked kind fields at
        /// least one soldier. In a standard wave the kind that unlocked most recently gets the largest
        /// share, so a new enemy is noticed the wave it appears, and the rest is shared evenly. A
        /// themed wave instead spends most of its budget on the theme's featured kinds.
        /// </summary>
        static List<WV_WaveGroup> Compose(int waveNumber, float strength, IReadOnlyList<WV_TroopKind> featured) {
            var unlocked = new List<WV_TroopProfile>();
            WV_TroopProfile newest = null;
            foreach (WV_TroopProfile profile in WV_TroopCatalog.All) {
                if (profile.FirstWave > waveNumber)
                    continue;
                unlocked.Add(profile);
                if (newest == null || profile.FirstWave > newest.FirstWave)
                    newest = profile;
            }

            var groups = new List<WV_WaveGroup>();
            if (unlocked.Count == 0)
                return groups;

            var leaders = new List<WV_TroopProfile>();
            foreach (WV_TroopProfile profile in unlocked) {
                foreach (WV_TroopKind kind in featured) {
                    if (profile.Kind == kind)
                        leaders.Add(profile);
                }
            }
            float leadShare = FeaturedShare;
            if (leaders.Count == 0) {
                leaders.Add(newest);
                leadShare = 0.4f;
            }
            if (leaders.Count == unlocked.Count)
                leadShare = 1f;

            float leaderBudget = strength * leadShare / leaders.Count;
            int others = unlocked.Count - leaders.Count;
            float otherBudget = others > 0 ? strength * (1f - leadShare) / others : 0f;

            // Leaders first, so the first group - which the boss marches with - is the theme's own.
            foreach (WV_TroopProfile profile in leaders)
                groups.Add(Group(profile, leaderBudget));
            foreach (WV_TroopProfile profile in unlocked) {
                if (!leaders.Contains(profile))
                    groups.Add(Group(profile, otherBudget));
            }
            return groups;
        }

        static WV_WaveGroup Group(WV_TroopProfile profile, float budget) =>
            new(profile.Kind, Mathf.Max(1, Mathf.RoundToInt(budget / Mathf.Max(0.1f, profile.Threat))));

        /// <summary>Scales every group down evenly when a wave would field more than the cap.</summary>
        static List<WV_WaveGroup> CapCount(List<WV_WaveGroup> groups, int maxEnemies) {
            int total = 0;
            foreach (WV_WaveGroup group in groups)
                total += group.Count;
            if (total <= maxEnemies)
                return groups;

            float scale = maxEnemies / (float)total;
            var capped = new List<WV_WaveGroup>(groups.Count);
            int remaining = maxEnemies;
            for (int i = 0; i < groups.Count && remaining > 0; i++) {
                WV_WaveGroup group = groups[i];
                int reserved = Mathf.Min(groups.Count - i - 1, remaining - 1);
                int count = Mathf.Clamp(Mathf.FloorToInt(group.Count * scale), 1, remaining - reserved);
                remaining -= count;
                capped.Add(new WV_WaveGroup(group.Kind, count));
            }
            return capped;
        }
    }
}
