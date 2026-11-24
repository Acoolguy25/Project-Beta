using System;
using RyanAssets.Retention;
using UnityEngine;

namespace Universes.UniverseData.war_valley.Shared {
    /// <summary>The day's front: one rule change for every round of a UTC day. Sent as a byte: only ever append.</summary>
    public enum WV_Front : byte { None, WarProfiteers, IronStorm, SupplyLines, EngineerCorps, RapidDeployment, ArmoredFront }

    /// <summary>How strong each front is, and a QA switch to play another day.</summary>
    [Serializable]
    public sealed class WV_FrontTuning {
        [Tooltip("QA only: play the front (and the retention kit's daily clock) of the day this many days " +
                 "from today. Keep 0 in builds.")]
        public int PreviewDayOffset;

        [Tooltip("War Profiteers: extra share of every wave bounty.")]
        [Range(0f, 1f)] public float ProfiteersBountyBonus = 0.25f;
        [Tooltip("War Profiteers' second rule: funds for every wave enemy a commander's forces defeat.")]
        [Min(0)] public int ProfiteersFundsPerKill = 4;

        [Tooltip("Iron Storm: extra enemy health, and the extra share of round XP it pays for it.")]
        [Range(0f, 1f)] public float IronStormEnemyHealth = 0.15f;
        [Range(0f, 1f)] public float IronStormXpBonus = 0.25f;
        [Tooltip("Iron Storm's second rule: extra heavy enemies (Punks) at the head of every wave.")]
        [Min(0)] public int IronStormExtraHeavies = 1;
        [Tooltip("Iron Storm's mark: every enemy is plated, this much bigger, so the day is seen on the field.")]
        [Range(1f, 1.5f)] public float IronStormEnemyScale = 1.12f;

        [Tooltip("Supply Lines: extra starting funds and extra income.")]
        [Min(0)] public int SupplyLinesFunds = 300;
        [Range(0f, 1f)] public float SupplyLinesIncome = 0.1f;

        [Tooltip("Engineer Corps: extra structure health.")]
        [Range(0f, 1f)] public float EngineerStructureHealth = 0.2f;
        [Tooltip("Engineer Corps' second rule: extra construction speed.")]
        [Range(0f, 2f)] public float EngineerConstructionSpeed = 0.3f;

        [Tooltip("Rapid Deployment: extra production speed.")]
        [Range(0f, 1f)] public float RapidProductionSpeed = 0.2f;
        [Tooltip("Rapid Deployment's second rule: free Gunners every commander gets at the start of the round.")]
        [Min(0)] public int RapidFreeGunners = 2;

        [Tooltip("Armored Front: extra vehicle health.")]
        [Range(0f, 1f)] public float ArmoredVehicleHealth = 0.25f;
        [Tooltip("Armored Front's second rule: share of a delivered vehicle's price handed back.")]
        [Range(0f, 1f)] public float ArmoredVehicleRebate = 0.2f;

        /// <summary>The clock the front and the daily challenges run on: now, moved by <see cref="PreviewDayOffset"/>.</summary>
        public DateTime Now(DateTime utcNow) => PreviewDayOffset == 0 ? utcNow : utcNow.AddDays(PreviewDayOffset);
    }

    /// <summary>What a front changes in a round.</summary>
    public readonly struct WV_FrontEffect {
        /// <summary>Bonuses every commander gets on top of their doctrines.</summary>
        public readonly WV_CommanderModifiers Modifiers;
        public readonly float EnemyHealthMultiplier;
        /// <summary>Extra share of the round's XP, paid as its own summary line.</summary>
        public readonly float XpBonus;
        public readonly int StartingFunds;
        /// <summary>The doctrine card every commander's first hand of the day holds.</summary>
        public readonly WV_DoctrineId Featured;
        /// <summary>Extra heavy enemies at the head of every wave (Iron Storm).</summary>
        public readonly int ExtraHeavies;
        /// <summary>How much bigger every wave enemy is drawn (Iron Storm's plating): 1 for none.</summary>
        public readonly float EnemyScale;
        /// <summary>Free Gunners every commander gets at the start (Rapid Deployment).</summary>
        public readonly int FreeGunners;

        public WV_FrontEffect(WV_CommanderModifiers modifiers, float enemyHealthMultiplier, float xpBonus, int startingFunds,
            WV_DoctrineId featured, int extraHeavies = 0, float enemyScale = 1f, int freeGunners = 0) {
            Modifiers = modifiers;
            EnemyHealthMultiplier = enemyHealthMultiplier;
            XpBonus = xpBonus;
            StartingFunds = startingFunds;
            Featured = featured;
            ExtraHeavies = extraHeavies;
            EnemyScale = enemyScale;
            FreeGunners = freeGunners;
        }
    }

    /// <summary>
    /// The day's front, so tomorrow's war plays differently from today's and not only the challenge
    /// list changes: richer bounties, tougher enemies for more XP, a bigger opening, sturdier walls,
    /// faster factories or heavier armour, and a featured doctrine card that every commander's first
    /// hand of the day holds. Seeded by the UTC day the daily challenges use (<see cref="RetentionDay"/>),
    /// so every server plays the same front. Days walk through the fronts in shuffled blocks and never
    /// repeat a front two days running. Shared: the server plays it, the HUD names it.
    /// </summary>
    public static class WV_DailyFront {
        static readonly WV_Front[] Fronts = {
            WV_Front.WarProfiteers, WV_Front.IronStorm, WV_Front.SupplyLines,
            WV_Front.EngineerCorps, WV_Front.RapidDeployment, WV_Front.ArmoredFront,
        };

        public static int Count => Fronts.Length;

        /// <summary>Today's front.</summary>
        public static WV_Front Today(DateTime utcNow) => Featured(RetentionDay.Of(utcNow));

        /// <summary>
        /// The front of <paramref name="day"/>. Days go in blocks, each a shuffle of the fronts seeded by
        /// the block; a block never starts with the front the block before ended on. Changing this
        /// reshuffles the calendar, so only in a release.
        /// </summary>
        public static WV_Front Featured(int day) {
            int n = Fronts.Length;
            int block = FloorDiv(day, n), index = day - block * n;
            int[] order = Shuffle(block);
            if (order[0] == Shuffle(block - 1)[n - 1])
                (order[0], order[1]) = (order[1], order[0]);
            return Fronts[order[index]];
        }

        public static WV_FrontEffect Effect(WV_Front front, WV_FrontTuning tuning) {
            tuning ??= new WV_FrontTuning();
            var modifiers = new WV_CommanderModifiers();
            float enemyHealth = 1f, xp = 0f, enemyScale = 1f;
            int funds = 0, heavies = 0, gunners = 0;
            // Every front has two rules: a number, and one that changes what is on the field or what to build.
            switch (front) {
                case WV_Front.WarProfiteers:
                    modifiers.BountyBonus = tuning.ProfiteersBountyBonus;
                    modifiers.FundsPerKill = tuning.ProfiteersFundsPerKill;
                    break;
                case WV_Front.IronStorm:
                    enemyHealth += tuning.IronStormEnemyHealth;
                    xp = tuning.IronStormXpBonus;
                    heavies = tuning.IronStormExtraHeavies;
                    enemyScale = tuning.IronStormEnemyScale;
                    break;
                case WV_Front.SupplyLines:
                    funds = tuning.SupplyLinesFunds;
                    modifiers.IncomeBonus = tuning.SupplyLinesIncome;
                    break;
                case WV_Front.EngineerCorps:
                    modifiers.StructureHealthBonus = tuning.EngineerStructureHealth;
                    modifiers.ConstructionSpeedBonus = tuning.EngineerConstructionSpeed;
                    break;
                case WV_Front.RapidDeployment:
                    modifiers.ProductionSpeedBonus = tuning.RapidProductionSpeed;
                    gunners = tuning.RapidFreeGunners;
                    break;
                case WV_Front.ArmoredFront:
                    modifiers.VehicleHealthBonus = tuning.ArmoredVehicleHealth;
                    modifiers.VehicleRebate = tuning.ArmoredVehicleRebate;
                    break;
            }
            return new WV_FrontEffect(modifiers, enemyHealth, xp, funds, FeaturedDoctrine(front), heavies, enemyScale, gunners);
        }

        /// <summary>The doctrine card the front features: one that plays to its theme, open from level 1.</summary>
        public static WV_DoctrineId FeaturedDoctrine(WV_Front front) => front switch {
            WV_Front.WarProfiteers => WV_DoctrineId.WarBonds,
            WV_Front.IronStorm => WV_DoctrineId.Sharpshooters,
            WV_Front.SupplyLines => WV_DoctrineId.DeepVeins,
            WV_Front.EngineerCorps => WV_DoctrineId.Fortifications,
            WV_Front.RapidDeployment => WV_DoctrineId.DrillSergeants,
            WV_Front.ArmoredFront => WV_DoctrineId.ArmoredColumns,
            _ => WV_DoctrineId.WarBonds,
        };

        /// <summary>The front's name for the HUD ("Iron Storm").</summary>
        public static string Title(WV_Front front) => front switch {
            WV_Front.WarProfiteers => "War Profiteers",
            WV_Front.IronStorm => "Iron Storm",
            WV_Front.SupplyLines => "Supply Lines",
            WV_Front.EngineerCorps => "Engineer Corps",
            WV_Front.RapidDeployment => "Rapid Deployment",
            WV_Front.ArmoredFront => "Armored Front",
            _ => "",
        };

        /// <summary>What the front does, as a short clause ("enemies +15% health, +25% XP").</summary>
        public static string EffectText(WV_Front front, WV_FrontTuning tuning) {
            tuning ??= new WV_FrontTuning();
            return front switch {
                WV_Front.WarProfiteers => $"wave bounties +{Percent(tuning.ProfiteersBountyBonus)}%, +{tuning.ProfiteersFundsPerKill} funds a kill",
                WV_Front.IronStorm => $"plated enemies +{Percent(tuning.IronStormEnemyHealth)}% health, {Heavies(tuning.IronStormExtraHeavies)} every wave, +{Percent(tuning.IronStormXpBonus)}% XP",
                WV_Front.SupplyLines => $"+{tuning.SupplyLinesFunds} starting funds, income +{Percent(tuning.SupplyLinesIncome)}%",
                WV_Front.EngineerCorps => $"structures +{Percent(tuning.EngineerStructureHealth)}% health, built {Percent(tuning.EngineerConstructionSpeed)}% faster",
                WV_Front.RapidDeployment => $"production +{Percent(tuning.RapidProductionSpeed)}% faster, {Gunners(tuning.RapidFreeGunners)} at the start",
                WV_Front.ArmoredFront => $"vehicles +{Percent(tuning.ArmoredVehicleHealth)}% health, {Percent(tuning.ArmoredVehicleRebate)}% of their price back",
                _ => "",
            };
        }

        /// <summary>"Today: Iron Storm - enemies +15% health, +25% XP", or "" when there is no front.</summary>
        public static string Describe(WV_Front front, WV_FrontTuning tuning) =>
            front == WV_Front.None ? "" : $"Today: {Title(front)} - {EffectText(front, tuning)}";

        static int Percent(float share) => Mathf.RoundToInt(share * 100f);

        static string Heavies(int count) => count == 1 ? "an extra Punk" : $"{count} extra Punks";

        static string Gunners(int count) => count == 1 ? "a free Gunner" : $"{count} free Gunners";

        static int FloorDiv(int a, int b) => a >= 0 ? a / b : -((-a + b - 1) / b);

        static int[] Shuffle(int block) {
            int n = Fronts.Length;
            var order = new int[n];
            for (int i = 0; i < n; i++)
                order[i] = i;
            ulong state = ((ulong)DailyChallenges.StableHash(WV_Retention.WV_SaveUniverse + ":front") << 32) ^ (uint)block ^ 0x9E3779B97F4A7C15UL;
            for (int i = n - 1; i > 0; i--) {
                int j = (int)(Next(ref state) % (ulong)(i + 1));
                (order[i], order[j]) = (order[j], order[i]);
            }
            return order;
        }

        /// <summary>SplitMix64, as the daily challenges use.</summary>
        static ulong Next(ref ulong state) {
            ulong z = state += 0x9E3779B97F4A7C15UL;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }
    }
}
