using System.Collections.Generic;
using RyanAssets.Rounds;
using UnityEngine;

namespace Universes.UniverseData.tower_defense {
    // Wire enums (bytes on the network): APPEND-ONLY, never renumber.
    public enum TD_EnemyKind : byte { Scout, Apc, Tank, Chopper, Boss, Shield, Medic, Carrier, Drone }
    public enum TD_TowerKind : byte { Gunner, Cannon, Frost, Sniper, Tesla, Mortar }

    public readonly struct TD_EnemyStats {
        public readonly string name;
        public readonly int health;
        public readonly float speed;
        public readonly int reward;
        public readonly int leakDamage;
        public readonly bool flying;
        public readonly float armor; // flat damage reduction per hit
        /// <summary>Energy shield as a share of health: soaks hits until it breaks, and weak hits and frost bounce off it.</summary>
        public readonly float shield;
        /// <summary>Health per second given to every other enemy within <see cref="TD_Rules.HealRadius"/>, as a share of this enemy's own maximum health.</summary>
        public readonly float heal;
        /// <summary>Drones released when this enemy is destroyed.</summary>
        public readonly int splits;
        /// <summary>A word or two for the next-wave chip on why this enemy is a threat ("Shielded").</summary>
        public readonly string trait;
        /// <summary>What beats it, for the new-enemy card the first time it comes ("Use Cannons, Snipers or Tesla").</summary>
        public readonly string counter;

        public TD_EnemyStats(string name, int health, float speed, int reward, int leakDamage, bool flying, float armor,
            float shield = 0f, float heal = 0f, int splits = 0, string trait = "", string counter = "") {
            this.name = name; this.health = health; this.speed = speed; this.reward = reward;
            this.leakDamage = leakDamage; this.flying = flying; this.armor = armor;
            this.shield = shield; this.heal = heal; this.splits = splits; this.trait = trait; this.counter = counter;
        }
    }

    public readonly struct TD_TowerStats {
        public readonly string name;
        public readonly int cost;
        public readonly float range;
        public readonly float damage;
        public readonly float fireRate;
        public readonly float splash;
        public readonly float slow;
        public readonly bool hitsAir;
        public readonly string blurb;
        /// <summary>Extra enemies a shot jumps to at level 1 (one more per level); 0 = single target.</summary>
        public readonly int chain;
        /// <summary>Enemies closer than this are too close to shell (mortar).</summary>
        public readonly float minRange;
        /// <summary>Retention unlock id that lets a player build it; empty = everyone can.</summary>
        public readonly string unlockId;

        public TD_TowerStats(string name, int cost, float range, float damage, float fireRate, float splash, float slow, bool hitsAir, string blurb,
            int chain = 0, float minRange = 0f, string unlockId = "") {
            this.name = name; this.cost = cost; this.range = range; this.damage = damage; this.fireRate = fireRate;
            this.splash = splash; this.slow = slow; this.hitsAir = hitsAir; this.blurb = blurb;
            this.chain = chain; this.minRange = minRange; this.unlockId = unlockId ?? "";
        }
    }

    public struct TD_SpawnGroup {
        public TD_EnemyKind kind;
        public int count;
        public float spacing;
        public float delay;
    }

    /// <summary>
    /// The whole defence: lane, tower slots, enemy and tower stats, wave plan and economy. Shared so
    /// the server's simulation and the clients' drawing agree on every distance and number.
    /// </summary>
    public static class TD_Rules {
        public const int MaxLevel = 3;
        public const int BaseHealth = 100;
        public const int StartCash = 400;
        /// <summary>Waves in a Normal or Hard game; Endless keeps going (<see cref="TD_Modes"/>).</summary>
        public const int WaveCount = 20;
        public const float FlyHeight = 7f;
        public const float SlotReach = 6f;
        public const float SlowSeconds = 1.6f;
        /// <summary>Share of everything spent on a tower (build plus upgrades) paid back when it is sold.</summary>
        public const float SellRefundRate = 0.7f;
        /// <summary>Cash per build-phase second skipped when every player votes to start the next wave early.</summary>
        public const int EarlyStartBonusPerSecond = 4;
        /// <summary>The base counts as critical (alarm, heartbeat) at or below this many health points.</summary>
        public const int BaseCriticalHealth = 30;
        /// <summary>Simulation speed while fast forward is voted on.</summary>
        public const float FastSpeed = 2f;
        /// <summary>
        /// A shielded enemy ignores any hit weaker than this until its shield breaks (and frost cannot slow it).
        /// Gunners stay under it at every level (29.9 at level 3), unless Veteran Crews (+10%) lift a level 3
        /// one over it; the Veteran Crews unlock says so.
        /// </summary>
        public const float ShieldBlockBelow = 30f;
        /// <summary>Medics heal enemies up to this many metres ahead or behind them on the road.</summary>
        public const float HealRadius = 12f;
        /// <summary>Splash damage on enemies around the one a shell was aimed at, as a share of the hit.</summary>
        public const float SplashFalloff = 0.6f;
        /// <summary>A chain shot jumps at most this far (metres) to its next enemy.</summary>
        public const float ChainRange = 11f;
        /// <summary>Damage kept on each jump of a chain shot.</summary>
        public const float ChainFalloff = 0.8f;
        /// <summary>Drones released by a destroyed carrier spread out this far behind it (metres).</summary>
        public const float SplitSpacing = 1.4f;
        /// <summary>Health grows by this factor per wave past wave 20 in Endless, on top of the normal curve.</summary>
        public const float EndlessGrowth = 1.12f;
        /// <summary>From this wave on a Heavy Tank comes every <see cref="BossEvery"/> waves.</summary>
        public const int BossEvery = 10;

        /// <summary>Fast forward runs while more than half of the players vote for it.</summary>
        public static bool FastVotePasses(int votes, int players) => players > 0 && votes * 2 > players;

        /// <summary>Lane from the enemy hangar to the base, on flat ground (y = 0).</summary>
        public static readonly Vector3[] Lane = {
            new(-70f, 0f, -48f), new(-70f, 0f, 28f), new(-36f, 0f, 28f), new(-36f, 0f, -30f), new(0f, 0f, -30f),
            new(0f, 0f, 40f), new(34f, 0f, 40f), new(34f, 0f, -44f), new(70f, 0f, -44f), new(70f, 0f, 10f)
        };

        /// <summary>Tower pads beside the lane.</summary>
        public static readonly Vector3[] Slots = {
            new(-61f, 0f, -24f), new(-79f, 0f, 6f), new(-53f, 0f, 19f), new(-53f, 0f, 37f), new(-45f, 0f, 4f),
            new(-27f, 0f, 14f), new(-18f, 0f, -21f), new(-18f, 0f, -39f), new(-9f, 0f, 2f), new(9f, 0f, 20f),
            new(17f, 0f, 49f), new(17f, 0f, 31f), new(25f, 0f, 0f), new(43f, 0f, -18f), new(52f, 0f, -35f),
            new(52f, 0f, -53f), new(61f, 0f, -20f), new(79f, 0f, -24f)
        };

        public static readonly TD_EnemyStats[] Enemies = {
            new("Scout", 70, 7.5f, 6, 2, false, 0f),
            new("APC", 190, 4.6f, 10, 4, false, 3f, trait: "Light armour",
                counter: "Armour takes a little off every hit. Upgraded Gunners still work; Cannons do better."),
            new("Tank", 650, 2.9f, 28, 10, false, 10f, trait: "Heavy armour",
                counter: "Heavy armour shrugs off light shots. Use Cannons, Snipers (they pierce it) or Artillery."),
            new("Chopper", 260, 5.6f, 18, 6, true, 0f, trait: "Flies",
                counter: "Flies over the road: Cannons and Artillery cannot hit it. Use Gunners, Snipers, Frost or Tesla."),
            new("Heavy Tank", 5200, 2.1f, 250, 40, false, 18f, trait: "Boss",
                counter: "Huge health and armour. Snipers pierce it, Frost slows it; save an Airstrike for it."),
            new("Shield Tank", 300, 3.4f, 24, 4, false, 6f, shield: 1.1f, trait: "Shielded",
                counter: $"Weak hits (under {ShieldBlockBelow:0}) and Frost bounce off its shield. Use Cannons, Snipers or Tesla."),
            new("Medic", 260, 4.2f, 20, 5, false, 2f, heal: 0.1f, trait: "Heals others",
                counter: "Repairs the enemies around it. Kill it first: splash and chains reach it in the pack."),
            new("Carrier", 520, 3.6f, 24, 8, false, 4f, splits: 4, trait: "Drops drones",
                counter: "Bursts into 4 flying drones. Kill it early, and keep towers that hit air near the base."),
            new("Drone", 40, 9f, 3, 1, true, 0f, trait: "Fast swarm",
                counter: "A fast flying swarm. Gunners and Tesla chains shred it; ground-only towers cannot touch it."),
        };

        public static readonly TD_TowerStats[] Towers = {
            new("Gunner", 120, 15f, 11f, 5f, 0f, 0f, true, "Fast shots, hits air"),
            new("Cannon", 220, 17f, 70f, 0.8f, 4f, 0f, false, "Splash damage, ground only"),
            new("Frost", 160, 13f, 5f, 2f, 0f, 0.45f, true, "Slows everything it hits"),
            new("Sniper", 320, 34f, 190f, 0.45f, 0f, 0f, true, "Huge range, pierces armour"),
            new("Tesla", 280, 15f, 34f, 1.1f, 0f, 0f, true, "Lightning chains, breaks shields", chain: 2, unlockId: TD_Retention.TeslaUnlock),
            // The Mortar kind (and its unlock id) shipped first; the tower shows as Artillery to fit its models.
            new("Artillery", 360, 44f, 150f, 0.3f, 7f, 0f, false, "Long-range shells, big blasts", minRange: 12f, unlockId: TD_Retention.MortarUnlock),
        };

        static PathTrack lane;
        public static PathTrack LaneTrack => lane ??= new PathTrack(Lane);

        public static TD_EnemyStats Enemy(TD_EnemyKind kind) => Enemies[(int)kind];
        public static TD_TowerStats Tower(TD_TowerKind kind) => Towers[(int)kind];

        public static float Damage(TD_TowerKind kind, int level) => Tower(kind).damage * Mathf.Pow(1.65f, level - 1);
        /// <summary>Range and slow both grow by this fraction per level above 1.</summary>
        public const float PerLevelGrowth = 0.15f;

        public static float Range(TD_TowerKind kind, int level) => Tower(kind).range * (1f + PerLevelGrowth * (level - 1));
        public static float FireRate(TD_TowerKind kind, int level) => Tower(kind).fireRate * (1f + 0.1f * (level - 1));
        public static int UpgradeCost(TD_TowerKind kind, int level) => Mathf.RoundToInt(Tower(kind).cost * 0.8f * level);

        /// <summary>The upgrade price after a discount (0..1, from a player's perks).</summary>
        public static int UpgradeCost(TD_TowerKind kind, int level, float discount) =>
            Mathf.Max(1, Mathf.RoundToInt(UpgradeCost(kind, level) * (1f - Mathf.Clamp01(discount))));

        /// <summary>Enemies a chain shot of <paramref name="kind"/> at <paramref name="level"/> hits in total (1 = no chain).</summary>
        public static int ChainTargets(TD_TowerKind kind, int level) => Tower(kind).chain > 0 ? 1 + Tower(kind).chain + (level - 1) : 1;

        /// <summary>Everything spent on a tower of <paramref name="level"/>: its price plus every upgrade.</summary>
        public static int Invested(TD_TowerKind kind, int level) {
            int total = Tower(kind).cost;
            for (int l = 1; l < Mathf.Min(level, MaxLevel); l++) total += UpgradeCost(kind, l);
            return total;
        }

        public static int SellRefund(TD_TowerKind kind, int level) => Mathf.RoundToInt(Invested(kind, level) * SellRefundRate);

        /// <summary>Bonus for starting the next wave with <paramref name="secondsLeft"/> of the build phase unused.</summary>
        public static int EarlyStartBonus(int secondsLeft) => Mathf.Max(0, secondsLeft) * EarlyStartBonusPerSecond;

        /// <summary>
        /// The early-call bonus for <paramref name="wave"/>: none before wave 1, whose long first build
        /// phase is for spending the starting cash, not for farming the bonus.
        /// </summary>
        public static int EarlyStartBonus(int wave, int secondsLeft) => wave <= 1 ? 0 : EarlyStartBonus(secondsLeft);

        /// <summary>Speed multiplier on an enemy hit by a <paramref name="kind"/> tower of <paramref name="level"/> (1 = no slow).</summary>
        public static float SlowFactor(TD_TowerKind kind, int level) => Mathf.Clamp01(1f - Tower(kind).slow * (1f + PerLevelGrowth * (level - 1)));

        /// <summary>The slow of <see cref="SlowFactor"/> as a whole percentage, for cards ("Slows 45%").</summary>
        public static int SlowPercent(TD_TowerKind kind, int level) => Mathf.RoundToInt((1f - SlowFactor(kind, level)) * 100f);

        public static bool IsBoss(TD_EnemyKind kind) => kind == TD_EnemyKind.Boss;

        /// <summary>Health damage of a <paramref name="raw"/> hit on <paramref name="target"/> after armour (unless it pierces); never less than 1.</summary>
        public static float ArmorDamage(float raw, TD_EnemyKind target, bool pierce) => Mathf.Max(1f, raw - (pierce ? 0f : Enemy(target).armor));

        /// <summary>Damage one hit does after armour (snipers ignore it); never less than 1.</summary>
        public static float HitDamage(TD_TowerKind kind, int level, TD_EnemyKind target) => ArmorDamage(Damage(kind, level), target, Pierces(kind));

        public static bool Pierces(TD_TowerKind kind) => kind == TD_TowerKind.Sniper;

        public static bool CanHit(TD_TowerKind kind, TD_EnemyKind target) => !Enemy(target).flying || Tower(kind).hitsAir;

        /// <summary>Health scales gently with the wave, so late waves need upgrades, not only more towers.</summary>
        public static float HealthScale(int wave) => 1f + 0.09f * (wave - 1) + 0.006f * (wave - 1) * (wave - 1);

        /// <summary>Health scale in a game of <paramref name="mode"/>: the mode's multiplier and, past wave 20 in Endless, compound growth.</summary>
        public static float HealthScale(int wave, TD_Difficulty mode) {
            float scale = HealthScale(wave) * TD_Modes.Get(mode).healthScale;
            if (wave > WaveCount) scale *= Mathf.Pow(EndlessGrowth, wave - WaveCount);
            return scale;
        }

        public static int WaveBonus(int wave) => 60 + 15 * wave;

        public static int WaveBonus(int wave, TD_Difficulty mode) => Mathf.RoundToInt(WaveBonus(wave) * TD_Modes.Get(mode).waveBonusScale);

        /// <summary>Cash for destroying <paramref name="kind"/> in a game of <paramref name="mode"/> (at least 1).</summary>
        public static int KillReward(TD_EnemyKind kind, TD_Difficulty mode) => Mathf.Max(1, Mathf.RoundToInt(Enemy(kind).reward * TD_Modes.Get(mode).rewardScale));

        /// <summary>Base health a win needs for three stars, and for two.</summary>
        public const int ThreeStarHealth = 80, TwoStarHealth = 40;

        /// <summary>Stars for holding a game with <paramref name="baseHealth"/> left: 3 at <see cref="ThreeStarHealth"/>+, 2 at <see cref="TwoStarHealth"/>+, 1 for any win, 0 for a loss.</summary>
        public static int Stars(int baseHealth) => baseHealth >= ThreeStarHealth ? 3 : baseHealth >= TwoStarHealth ? 2 : baseHealth > 0 ? 1 : 0;

        public const int MaxStars = 3;

        /// <summary>Whether a hit of <paramref name="raw"/> damage bounces off a shield (and so does nothing until it breaks).</summary>
        public static bool BouncesOffShields(float raw) => raw < ShieldBlockBelow;

        /// <summary>
        /// Share of a game's waves a player must have been in for its win to count (Hard unlock, wins, stars,
        /// the win challenges): someone who joins on wave 18 of 20 did not win it.
        /// </summary>
        public const float WinShare = 0.5f;

        /// <summary>Whether a win counts for a player whose game began on <paramref name="joinedWave"/> of <paramref name="waves"/>.</summary>
        public static bool WinCounts(int joinedWave, int waves) => waves - Mathf.Max(1, joinedWave) + 1 >= Mathf.CeilToInt(waves * WinShare);

        /// <summary>The last wave a player can join on and still have the win count.</summary>
        public static int LastWinJoinWave(int waves) => waves + 1 - Mathf.CeilToInt(waves * WinShare);

        public static Vector3 EnemyPosition(TD_EnemyKind kind, float distance, out Vector3 forward) {
            Vector3 p = LaneTrack.Evaluate(distance, out forward);
            if (Enemy(kind).flying) p.y += FlyHeight;
            return p;
        }

        public static Vector3 EnemyPosition(TD_EnemyKind kind, float distance) => EnemyPosition(kind, distance, out _);

        public static bool IsBossWave(int wave) => wave >= BossEvery && wave % BossEvery == 0;

        /// <summary>
        /// The groups of wave <paramref name="wave"/> (1-based), spawned in order. Waves past 20 (Endless)
        /// keep the same formulas, so the army keeps growing; every tenth wave brings more Heavy Tanks.
        /// Each enemy type answers a lazy build: shields and armour beat weak fast guns, medics beat slow
        /// damage, carriers and drone swarms beat single-target towers and ground-only splash.
        /// </summary>
        public static List<TD_SpawnGroup> Wave(int wave) {
            var groups = new List<TD_SpawnGroup>();
            void Add(TD_EnemyKind kind, int count, float spacing, float delay = 0f) {
                if (count > 0) groups.Add(new TD_SpawnGroup { kind = kind, count = count, spacing = spacing, delay = delay });
            }
            Add(TD_EnemyKind.Scout, 4 + wave, Mathf.Max(0.5f, 1.2f - wave * 0.03f));
            // Tanks come in pairs of waves; the waves where the tank count holds get two extra APCs, so
            // the ground army grows every wave instead of in steps.
            int tanks = wave >= 6 ? (wave - 4) / 2 : 0;
            if (wave >= 3) Add(TD_EnemyKind.Apc, wave - 2 + (tanks > 0 && wave % 2 == 1 ? 2 : 0), 1.6f, 2f);
            // Shield tanks from wave 6: Gunners (at any level, without Veteran Crews) and Frost cannot touch them until the shield breaks.
            if (wave >= 6) Add(TD_EnemyKind.Shield, 1 + (wave - 6) / 3, 2.2f, 2f);
            Add(TD_EnemyKind.Tank, tanks, 3f, 3f);
            // Medics ride right behind the tanks (they are faster, so they catch up) and keep them topped up.
            if (wave >= 7 && wave % 2 == 1) Add(TD_EnemyKind.Medic, 1 + (wave - 7) / 3, 1.2f, 0.6f);
            if (wave % 4 == 0) Add(TD_EnemyKind.Chopper, 2 + wave / 3, 1.8f, 2f);
            // Carriers burst into drones when destroyed, so killing them late lets a swarm loose near the base.
            if (wave >= 11) Add(TD_EnemyKind.Carrier, 1 + (wave - 11) / 2, 2.6f, 2f);
            // Drone swarms: fast and flying, from wave 9 every fourth wave, and every odd wave from 15.
            if (wave >= 9 && (wave % 4 == 1 || (wave >= 15 && wave % 2 == 1))) Add(TD_EnemyKind.Drone, 8 + (wave - 9) * 2, 0.32f, 1.5f);
            // A second, faster scout rush grows from wave 8 on (no sudden jump at any wave).
            if (wave >= 8) Add(TD_EnemyKind.Scout, (wave - 7) * 2, 0.45f, 1f);
            if (IsBossWave(wave)) Add(TD_EnemyKind.Boss, wave / BossEvery, 8f, 4f);
            return groups;
        }

        static readonly Dictionary<int, int[]> waveCounts = new();
        static readonly int[] noEnemies = new int[System.Enum.GetValues(typeof(TD_EnemyKind)).Length];

        /// <summary>
        /// Enemies of each kind (index = <see cref="TD_EnemyKind"/>) in <paramref name="wave"/>, computed once
        /// per wave and shared: callers must not modify the array. Below wave 1 it is all zeros.
        /// </summary>
        public static int[] WaveCounts(int wave) {
            if (wave < 1) return noEnemies;
            if (waveCounts.TryGetValue(wave, out int[] cached)) return cached;
            var counts = new int[Enemies.Length];
            foreach (var g in Wave(wave)) counts[(int)g.kind] += g.count;
            return waveCounts[wave] = counts;
        }

        /// <summary>Enemies in <paramref name="wave"/>, including the drones its carriers release.</summary>
        public static int EnemyCount(int wave) {
            int n = 0;
            int[] counts = WaveCounts(wave);
            for (int i = 0; i < counts.Length; i++) n += counts[i] * (1 + Enemies[i].splits);
            return n;
        }

        public static bool HasBoss(int wave) => WaveCounts(wave)[(int)TD_EnemyKind.Boss] > 0;

        static int[] firstWaves;

        /// <summary>The first wave <paramref name="kind"/> comes in (from the wave plan; 0 = never in a 20-wave game).</summary>
        public static int FirstWave(TD_EnemyKind kind) {
            if (firstWaves == null) {
                var first = new int[Enemies.Length];
                for (int wave = WaveCount; wave >= 1; wave--) {
                    int[] counts = WaveCounts(wave);
                    for (int i = 0; i < counts.Length; i++)
                        if (counts[i] > 0) first[i] = wave;
                }
                firstWaves = first;
            }
            return firstWaves[(int)kind];
        }

        /// <summary>Whether <paramref name="wave"/> is the first with <paramref name="kind"/> in it (a new enemy to introduce; never wave 1).</summary>
        public static bool IsNew(TD_EnemyKind kind, int wave) => wave > 1 && FirstWave(kind) == wave;
    }
}
