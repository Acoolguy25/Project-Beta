using System.Collections.Generic;
using RyanAssets.Rounds;
using UnityEngine;

namespace Universes.UniverseData.tower_defense {
    public enum TD_EnemyKind : byte { Scout, Apc, Tank, Chopper, Boss }
    public enum TD_TowerKind : byte { Gunner, Cannon, Frost, Sniper }

    public readonly struct TD_EnemyStats {
        public readonly string name;
        public readonly int health;
        public readonly float speed;
        public readonly int reward;
        public readonly int leakDamage;
        public readonly bool flying;
        public readonly float armor; // flat damage reduction per hit
        public TD_EnemyStats(string name, int health, float speed, int reward, int leakDamage, bool flying, float armor) {
            this.name = name; this.health = health; this.speed = speed; this.reward = reward;
            this.leakDamage = leakDamage; this.flying = flying; this.armor = armor;
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
        public TD_TowerStats(string name, int cost, float range, float damage, float fireRate, float splash, float slow, bool hitsAir, string blurb) {
            this.name = name; this.cost = cost; this.range = range; this.damage = damage; this.fireRate = fireRate;
            this.splash = splash; this.slow = slow; this.hitsAir = hitsAir; this.blurb = blurb;
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
            new("APC", 190, 4.6f, 10, 4, false, 3f),
            new("Tank", 650, 2.9f, 28, 10, false, 10f),
            new("Chopper", 260, 5.6f, 18, 6, true, 0f),
            new("Heavy Tank", 5200, 2.1f, 250, 40, false, 18f),
        };

        public static readonly TD_TowerStats[] Towers = {
            new("Gunner", 120, 15f, 11f, 5f, 0f, 0f, true, "Fast shots, hits air"),
            new("Cannon", 220, 17f, 70f, 0.8f, 4f, 0f, false, "Splash damage, ground only"),
            new("Frost", 160, 13f, 5f, 2f, 0f, 0.45f, true, "Slows everything it hits"),
            new("Sniper", 320, 34f, 190f, 0.45f, 0f, 0f, true, "Huge range, pierces armour"),
        };

        static PathTrack lane;
        public static PathTrack LaneTrack => lane ??= new PathTrack(Lane);

        public static TD_EnemyStats Enemy(TD_EnemyKind kind) => Enemies[(int)kind];
        public static TD_TowerStats Tower(TD_TowerKind kind) => Towers[(int)kind];

        public static float Damage(TD_TowerKind kind, int level) => Tower(kind).damage * Mathf.Pow(1.65f, level - 1);
        public static float Range(TD_TowerKind kind, int level) => Tower(kind).range * (1f + 0.15f * (level - 1));
        public static float FireRate(TD_TowerKind kind, int level) => Tower(kind).fireRate * (1f + 0.1f * (level - 1));
        public static int UpgradeCost(TD_TowerKind kind, int level) => Mathf.RoundToInt(Tower(kind).cost * 0.8f * level);

        /// <summary>Everything spent on a tower of <paramref name="level"/>: its price plus every upgrade.</summary>
        public static int Invested(TD_TowerKind kind, int level) {
            int total = Tower(kind).cost;
            for (int l = 1; l < Mathf.Min(level, MaxLevel); l++) total += UpgradeCost(kind, l);
            return total;
        }

        public static int SellRefund(TD_TowerKind kind, int level) => Mathf.RoundToInt(Invested(kind, level) * SellRefundRate);

        /// <summary>Bonus for starting the next wave with <paramref name="secondsLeft"/> of the build phase unused.</summary>
        public static int EarlyStartBonus(int secondsLeft) => Mathf.Max(0, secondsLeft) * EarlyStartBonusPerSecond;

        public static bool IsBoss(TD_EnemyKind kind) => kind == TD_EnemyKind.Boss;

        /// <summary>Damage one hit does after armour (snipers ignore it); never less than 1.</summary>
        public static float HitDamage(TD_TowerKind kind, int level, TD_EnemyKind target) {
            float armor = kind == TD_TowerKind.Sniper ? 0f : Enemy(target).armor;
            return Mathf.Max(1f, Damage(kind, level) - armor);
        }

        public static bool CanHit(TD_TowerKind kind, TD_EnemyKind target) => !Enemy(target).flying || Tower(kind).hitsAir;

        /// <summary>Health scales gently with the wave, so late waves need upgrades, not only more towers.</summary>
        public static float HealthScale(int wave) => 1f + 0.09f * (wave - 1) + 0.004f * (wave - 1) * (wave - 1);

        public static int WaveBonus(int wave) => 60 + 15 * wave;

        public static Vector3 EnemyPosition(TD_EnemyKind kind, float distance, out Vector3 forward) {
            Vector3 p = LaneTrack.Evaluate(distance, out forward);
            if (Enemy(kind).flying) p.y += FlyHeight;
            return p;
        }

        /// <summary>The groups of wave <paramref name="wave"/> (1-based), spawned in order.</summary>
        public static List<TD_SpawnGroup> Wave(int wave) {
            var groups = new List<TD_SpawnGroup>();
            void Add(TD_EnemyKind kind, int count, float spacing, float delay = 0f) {
                if (count > 0) groups.Add(new TD_SpawnGroup { kind = kind, count = count, spacing = spacing, delay = delay });
            }
            Add(TD_EnemyKind.Scout, 4 + wave, Mathf.Max(0.5f, 1.2f - wave * 0.03f));
            if (wave >= 3) Add(TD_EnemyKind.Apc, wave - 1, 1.6f, 2f);
            if (wave >= 6) Add(TD_EnemyKind.Tank, (wave - 3) / 2, 3f, 3f);
            if (wave % 4 == 0) Add(TD_EnemyKind.Chopper, 2 + wave / 3, 1.8f, 2f);
            if (wave >= 12) Add(TD_EnemyKind.Scout, wave, 0.4f, 1f);
            if (wave == 10 || wave == WaveCount) Add(TD_EnemyKind.Boss, wave == WaveCount ? 2 : 1, 8f, 4f);
            return groups;
        }

        public static int EnemyCount(int wave) {
            int n = 0;
            foreach (var g in Wave(wave)) n += g.count;
            return n;
        }

        public static bool HasBoss(int wave) {
            foreach (var g in Wave(wave)) if (IsBoss(g.kind)) return true;
            return false;
        }
    }
}
