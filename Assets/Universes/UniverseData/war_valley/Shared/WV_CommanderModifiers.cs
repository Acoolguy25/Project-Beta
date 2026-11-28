using System.Collections.Generic;
using UnityEngine;

namespace Universes.UniverseData.war_valley.Shared {
    /// <summary>
    /// What a commander's doctrines add up to this round, as bonuses on top of the tuned game. Every
    /// field is 0 when nothing was picked, so a default value changes nothing.
    /// </summary>
    public struct WV_CommanderModifiers {
        /// <summary>Extra share of every wave and victory bounty: 0.3 pays 30% more.</summary>
        public float BountyBonus;
        /// <summary>Extra share of what mines and other income sites pay.</summary>
        public float IncomeBonus;
        /// <summary>Extra production speed at barracks and factories: 0.2 trains 20% faster.</summary>
        public float ProductionSpeedBonus;
        public float TroopHealthBonus;
        public float TroopDamageBonus;
        public float VehicleHealthBonus;
        public float StructureHealthBonus;
        public float TurretDamageBonus;
        /// <summary>Share of a delivered troop's or vehicle's price handed back when it arrives.</summary>
        public float DeliveryRebate;
        /// <summary>Funds paid for each wave enemy this commander's forces defeat.</summary>
        public int FundsPerKill;
        /// <summary>Share of a troop's maximum health it regains every second.</summary>
        public float TroopRegenPerSecond;
        /// <summary>Extra construction speed: 0.3 builds 30% faster (Engineer Corps' front).</summary>
        public float ConstructionSpeedBonus;
        /// <summary>Share of a delivered vehicle's price handed back on top of <see cref="DeliveryRebate"/> (Armored Front).</summary>
        public float VehicleRebate;

        public float BountyMultiplier => 1f + BountyBonus;
        public float IncomeMultiplier => 1f + IncomeBonus;
        /// <summary>Multiplies a production timer: below 1 is faster.</summary>
        public float ProductionTimeMultiplier => 1f / (1f + Mathf.Max(0f, ProductionSpeedBonus));
        public float TroopHealthMultiplier => 1f + TroopHealthBonus;
        public float TroopDamageMultiplier => 1f + TroopDamageBonus;
        public float VehicleHealthMultiplier => 1f + VehicleHealthBonus;
        public float StructureHealthMultiplier => 1f + StructureHealthBonus;
        public float TurretDamageMultiplier => 1f + TurretDamageBonus;
        /// <summary>Multiplies a construction timer: below 1 is faster.</summary>
        public float ConstructionTimeMultiplier => 1f / (1f + Mathf.Max(0f, ConstructionSpeedBonus));

        /// <summary>Adds <paramref name="other"/> times <paramref name="times"/> to these bonuses.</summary>
        public void Add(in WV_CommanderModifiers other, int times = 1) {
            BountyBonus += other.BountyBonus * times;
            IncomeBonus += other.IncomeBonus * times;
            ProductionSpeedBonus += other.ProductionSpeedBonus * times;
            TroopHealthBonus += other.TroopHealthBonus * times;
            TroopDamageBonus += other.TroopDamageBonus * times;
            VehicleHealthBonus += other.VehicleHealthBonus * times;
            StructureHealthBonus += other.StructureHealthBonus * times;
            TurretDamageBonus += other.TurretDamageBonus * times;
            DeliveryRebate += other.DeliveryRebate * times;
            FundsPerKill += other.FundsPerKill * times;
            TroopRegenPerSecond += other.TroopRegenPerSecond * times;
            ConstructionSpeedBonus += other.ConstructionSpeedBonus * times;
            VehicleRebate += other.VehicleRebate * times;
        }

        public static long Scale(long value, float multiplier) =>
            System.Math.Max(value > 0 ? 1L : 0L, (long)System.Math.Round(value * (double)Mathf.Max(0f, multiplier)));
    }

    /// <summary>
    /// Each commander's current <see cref="WV_CommanderModifiers"/>, keyed by client id. Server-only
    /// state: the server fills it as doctrines are picked and clears it every round; the shared
    /// building code (income, production, turrets, construction) reads it on the server, so a client
    /// build only ever sees no bonus. Static, so EditMode tests call <see cref="Clear"/> in their setup.
    /// </summary>
    public static class WV_CommanderBonuses {
        static readonly Dictionary<int, WV_CommanderModifiers> byClient = new();
        static WV_CommanderModifiers baseline;

        /// <summary>
        /// A commander's bonuses: their own (doctrines) plus the <see cref="Baseline"/> every commander
        /// shares (the day's front). Nothing for an unknown commander but the baseline.
        /// </summary>
        public static WV_CommanderModifiers For(int clientId) {
            WV_CommanderModifiers modifiers = byClient.TryGetValue(clientId, out WV_CommanderModifiers own) ? own : default;
            modifiers.Add(baseline);
            return modifiers;
        }

        /// <summary>Bonuses every commander gets this round on top of their own (the day's front).</summary>
        public static WV_CommanderModifiers Baseline {
            get => baseline;
            set => baseline = value;
        }

        public static void Set(int clientId, WV_CommanderModifiers modifiers) {
            if (clientId != WV_Owned.NoOwner)
                byClient[clientId] = modifiers;
        }

        public static void Remove(int clientId) => byClient.Remove(clientId);

        /// <summary>Forgets every commander's bonuses and the baseline.</summary>
        public static void Clear() {
            byClient.Clear();
            baseline = default;
        }
    }
}
