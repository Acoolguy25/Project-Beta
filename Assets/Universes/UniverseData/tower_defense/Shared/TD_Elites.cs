using UnityEngine;

namespace Universes.UniverseData.tower_defense {
    /// <summary>
    /// Which elite form a level 3 tower was specialised into. A wire enum (it rides in
    /// <see cref="TD_TowerSnap"/>, <see cref="TD_BuildRequest"/> and <see cref="TD_TowerEventBroadcast"/>):
    /// APPEND-ONLY, never renumber.
    /// </summary>
    public enum TD_TowerPath : byte {
        /// <summary>Not specialised (levels 1 to 3, or a level 3 tower waiting for its choice).</summary>
        None,
        /// <summary>The left choice on the tower card (the action key).</summary>
        First,
        /// <summary>The right choice on the tower card (the second elite key).</summary>
        Second,
    }

    /// <summary>
    /// One elite form: what it costs and how it changes its tower. Every number is a multiplier or an
    /// addition on top of the tower's level 3 stats, so the base table in <see cref="TD_Rules"/> stays the
    /// single source of a tower's strength and an elite stays a clear step above it.
    /// </summary>
    public readonly struct TD_EliteStats {
        public readonly string name;
        /// <summary>One short line for the tower card's choice (two lines of a narrow card at most: "2.2x fire rate").</summary>
        public readonly string blurb;
        /// <summary>Price of the specialisation before the buyer's upgrade discount.</summary>
        public readonly int cost;
        public readonly float damageScale, rateScale, rangeScale, splashScale;
        /// <summary>Extra damage on flying enemies (1 = none).</summary>
        public readonly float airDamageScale;
        /// <summary>Hits on a flying enemy also hit every other flyer this close to it (metres; 0 = none).</summary>
        public readonly float airBurst;
        /// <summary>Enemies a hit stuns for this many seconds (bosses for <see cref="TD_Elites.BossStunScale"/> of it).</summary>
        public readonly float stunSeconds;
        /// <summary>Extra enemies a chain shot jumps to.</summary>
        public readonly int extraChains;
        /// <summary>Frost: slows this much harder (a multiplier on the slowed share of speed).</summary>
        public readonly float slowScale;
        /// <summary>Frost: enemies it chills take <see cref="TD_Elites.BrittleBonus"/> more damage from everything.</summary>
        public readonly bool brittle;

        public TD_EliteStats(string name, string blurb, int cost, float damageScale = 1f, float rateScale = 1f, float rangeScale = 1f,
            float splashScale = 1f, float airDamageScale = 1f, float airBurst = 0f, float stunSeconds = 0f, int extraChains = 0,
            float slowScale = 1f, bool brittle = false) {
            this.name = name; this.blurb = blurb; this.cost = cost;
            this.damageScale = damageScale; this.rateScale = rateScale; this.rangeScale = rangeScale; this.splashScale = splashScale;
            this.airDamageScale = airDamageScale; this.airBurst = airBurst * TD_Rules.WorldScale; this.stunSeconds = stunSeconds;
            this.extraChains = extraChains; this.slowScale = slowScale; this.brittle = brittle;
        }

        /// <summary>The identity: a tower that was not specialised.</summary>
        public static TD_EliteStats None => new("", "", 0);
    }

    /// <summary>
    /// Elite specialisations: a level 3 tower can be upgraded once more into one of two forms, each a
    /// different answer to the late waves (more shots or bigger ones, crowd control or raw damage). The
    /// buyer needs the <see cref="TD_Retention.EliteUnlock"/>; the simulation reads every stat through
    /// the helpers here so a tower's path changes it everywhere at once (server, cards, range rings).
    /// </summary>
    public static class TD_Elites {
        /// <summary>The level a tower must be at before it can be specialised.</summary>
        public const int Level = TD_Rules.MaxLevel;
        /// <summary>Paths per tower (not counting <see cref="TD_TowerPath.None"/>).</summary>
        public const int PathCount = 2;
        /// <summary>Heavy Tanks shrug off most of a stun: they are stunned for this share of it.</summary>
        public const float BossStunScale = 0.3f;
        /// <summary>Extra damage (share) every hit does to an enemy chilled by a Brittle Ice tower.</summary>
        public const float BrittleBonus = 0.35f;
        /// <summary>Hard cap on how much of an enemy's speed a frost slow can take away.</summary>
        public const float MaxSlow = 0.85f;
        /// <summary>Uniform scale of an elite tower: one step bigger than level 3.</summary>
        public const float Scale = 1.36f;

        // Index = (int)kind * PathCount + (path - 1). Keep in TD_TowerKind order.
        static readonly TD_EliteStats[] Paths = {
            // Gunner
            new("Minigun", "2.2x fire rate", 380, rateScale: 2.2f),
            new("Flak Battery", "Air bursts, +60% vs air", 360, damageScale: 1.4f, airDamageScale: 1.6f, airBurst: 6f),
            // Cannon
            new("Siege Cannon", "1.8x damage, wide blasts", 640, damageScale: 1.8f, splashScale: 1.35f, rateScale: 0.9f),
            new("Concussion", "Blasts stun the ground", 600, rateScale: 1.3f, stunSeconds: 0.4f),
            // Frost
            new("Deep Freeze", "Harder slow, more range", 460, rangeScale: 1.25f, slowScale: 1.3f),
            new("Brittle Ice", $"Chilled foes take +{Mathf.RoundToInt(BrittleBonus * 100f)}%", 480, brittle: true),
            // Sniper
            new("Marksman", "2.1x damage, more range", 900, damageScale: 2.1f, rangeScale: 1.15f),
            new("Rapid Rifle", "2.4x fire rate", 820, rateScale: 2.4f, damageScale: 0.75f),
            // Tesla
            new("Storm Coil", "Chains to 3 more", 760, extraChains: 3, rangeScale: 1.15f),
            new("Overload", "Jumps stun, +25% damage", 720, damageScale: 1.25f, stunSeconds: 0.3f),
            // Artillery
            new("Big Bertha", "2x damage, huge blasts", 980, damageScale: 2f, splashScale: 1.35f, rateScale: 0.85f),
            new("Barrage", "2.4x fire rate", 900, rateScale: 2.4f, damageScale: 0.7f),
        };

        public static bool IsElite(TD_TowerPath path) => path is TD_TowerPath.First or TD_TowerPath.Second;

        public static bool IsValid(TD_TowerPath path) => (int)path <= PathCount;

        /// <summary>The elite form <paramref name="path"/> of <paramref name="kind"/> (<see cref="TD_EliteStats.None"/> for no path).</summary>
        public static TD_EliteStats Get(TD_TowerKind kind, TD_TowerPath path) {
            if (!IsElite(path)) return TD_EliteStats.None;
            int index = (int)kind * PathCount + (int)path - 1;
            return index >= 0 && index < Paths.Length ? Paths[index] : TD_EliteStats.None;
        }

        /// <summary>The specialisation price after the buyer's upgrade discount (0..1).</summary>
        public static int Cost(TD_TowerKind kind, TD_TowerPath path, float discount = 0f) =>
            Mathf.Max(1, Mathf.RoundToInt(Get(kind, path).cost * (1f - Mathf.Clamp01(discount))));

        /// <summary>Whether a tower of <paramref name="level"/> on <paramref name="path"/> can be specialised now.</summary>
        public static bool CanSpecialise(int level, TD_TowerPath path) => level >= Level && !IsElite(path);

        public static float Damage(TD_TowerKind kind, int level, TD_TowerPath path) => TD_Rules.Damage(kind, level) * Get(kind, path).damageScale;

        public static float Range(TD_TowerKind kind, int level, TD_TowerPath path, TD_Modifier modifier) =>
            TD_Daily.Range(kind, level, modifier) * Get(kind, path).rangeScale;

        public static float FireRate(TD_TowerKind kind, int level, TD_TowerPath path, TD_Modifier modifier) =>
            TD_Daily.FireRate(kind, level, modifier) * Get(kind, path).rateScale;

        public static float Splash(TD_TowerKind kind, TD_TowerPath path) => TD_Rules.Tower(kind).splash * Get(kind, path).splashScale;

        public static int ChainTargets(TD_TowerKind kind, int level, TD_TowerPath path) =>
            TD_Rules.Tower(kind).chain > 0 ? TD_Rules.ChainTargets(kind, level) + Get(kind, path).extraChains : 1;

        /// <summary>Speed multiplier on an enemy this tower slows (1 = no slow), with Deep Freeze's extra bite and the <see cref="MaxSlow"/> cap.</summary>
        public static float SlowFactor(TD_TowerKind kind, int level, TD_TowerPath path) {
            float slowed = (1f - TD_Rules.SlowFactor(kind, level)) * Get(kind, path).slowScale;
            return 1f - Mathf.Clamp(slowed, 0f, MaxSlow);
        }

        public static int SlowPercent(TD_TowerKind kind, int level, TD_TowerPath path) => Mathf.RoundToInt((1f - SlowFactor(kind, level, path)) * 100f);

        /// <summary>Seconds a hit stuns <paramref name="target"/> for (0 = no stun).</summary>
        public static float Stun(TD_TowerKind kind, TD_TowerPath path, TD_EnemyKind target) {
            float seconds = Get(kind, path).stunSeconds;
            return TD_Rules.IsBoss(target) ? seconds * BossStunScale : seconds;
        }

        /// <summary>The card name of a tower: its elite form's name, else the tower's own.</summary>
        public static string Name(TD_TowerKind kind, TD_TowerPath path) => IsElite(path) ? Get(kind, path).name : TD_Rules.Tower(kind).name;
    }
}
