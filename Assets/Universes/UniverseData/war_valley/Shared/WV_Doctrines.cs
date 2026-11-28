using System;
using System.Collections.Generic;
using UnityEngine;

namespace Universes.UniverseData.war_valley.Shared {
    /// <summary>
    /// A between-wave upgrade a commander picks from a hand of cards: the roguelite choice that makes
    /// each run play differently. The byte values travel on the wire: append only.
    /// </summary>
    public enum WV_DoctrineId : byte {
        WarBonds = 0,
        DeepVeins = 1,
        DrillSergeants = 2,
        HardenedInfantry = 3,
        Sharpshooters = 4,
        ArmoredColumns = 5,
        Fortifications = 6,
        Quartermaster = 7,
        WarChest = 8,
        Bastion = 9,
        BountyHunters = 10,
        Overcharge = 11,
        FieldMedics = 12,
        TotalWar = 13,
        IronCitadel = 14,
        Blitzkrieg = 15
    }

    /// <summary>How rare a card is. Elite cards are stronger, appear less often, and only once unlocked.</summary>
    public enum WV_DoctrineRarity : byte {
        Common = 0,
        Elite = 1
    }

    /// <summary>
    /// What a doctrine acts on, so a pick can be shown landing there (rings on the troops, the
    /// buildings, the flag). Several may be set.
    /// </summary>
    [Flags]
    public enum WV_DoctrineTargets : byte {
        None = 0,
        Troops = 1,
        Vehicles = 2,
        Structures = 4,
        Turrets = 8,
        Mines = 16,
        Production = 32,
        Flag = 64
    }

    /// <summary>A doctrine's family, which gives its card its colour: what kind of strength it adds.</summary>
    public enum WV_DoctrineFamily : byte {
        Economy = 0,
        Troops = 1,
        Armour = 2,
        Base = 3,
        Flag = 4
    }

    /// <summary>One doctrine: what it is called, what each rank adds, and what unlocks it.</summary>
    public sealed class WV_DoctrineDefinition {
        public WV_DoctrineId Id { get; }
        public string Title { get; }
        /// <summary>What one rank does, in words; shown on the card.</summary>
        public string Description { get; }
        /// <summary>A theme icon name (Theme/Icons, Theme/GameIcons).</summary>
        public string Icon { get; }
        public WV_DoctrineRarity Rarity { get; }
        public int MaxRank { get; }
        /// <summary>The level unlock (<see cref="WV_Retention"/>) that adds this card to the deck, or null for the starting deck.</summary>
        public string UnlockId { get; }
        /// <summary>What one rank adds to the commander's lasting bonuses.</summary>
        public WV_CommanderModifiers PerRank { get; }
        /// <summary>Funds paid once when picked: a flat sum plus an amount per wave already held.</summary>
        public int InstantFunds { get; }
        public int InstantFundsPerWave { get; }
        /// <summary>Share of the flag's base health added to its maximum (and healed) when picked.</summary>
        public float FlagHealthBonus { get; }
        /// <summary>Share of the flag's maximum health repaired when picked.</summary>
        public float FlagRepair { get; }

        public WV_DoctrineDefinition(WV_DoctrineId id, string title, string description, string icon,
            WV_DoctrineRarity rarity, int maxRank, string unlockId, WV_CommanderModifiers perRank,
            int instantFunds = 0, int instantFundsPerWave = 0, float flagHealthBonus = 0f, float flagRepair = 0f) {
            Id = id;
            Title = title;
            Description = description;
            Icon = icon;
            Rarity = rarity;
            MaxRank = Mathf.Max(1, maxRank);
            UnlockId = unlockId;
            PerRank = perRank;
            InstantFunds = instantFunds;
            InstantFundsPerWave = instantFundsPerWave;
            FlagHealthBonus = flagHealthBonus;
            FlagRepair = flagRepair;
        }

        /// <summary>The funds picking this card pays right now, after <paramref name="wavesHeld"/> waves.</summary>
        public long GetInstantFunds(int wavesHeld) => InstantFunds + (long)InstantFundsPerWave * Mathf.Max(0, wavesHeld);
    }

    /// <summary>
    /// The deck of doctrines and the rules for dealing a hand from it. Deterministic: the same seed
    /// and ranks always deal the same cards, which keeps the deal testable and lets a reconnecting
    /// client be shown the very hand it was dealt.
    /// </summary>
    public static class WV_DoctrineCatalog {
        /// <summary>The highest <see cref="WV_DoctrineId"/> value plus one: the length of a rank array.</summary>
        public const int Count = 16;

        static readonly WV_DoctrineDefinition[] all = {
            new(WV_DoctrineId.WarBonds, "War Bonds", "Wave and victory bounties pay 30% more.", "paid",
                WV_DoctrineRarity.Common, 3, null, new WV_CommanderModifiers { BountyBonus = 0.3f }),
            new(WV_DoctrineId.DeepVeins, "Deep Veins", "Your mines pay 20% more.", "monetization_on",
                WV_DoctrineRarity.Common, 3, null, new WV_CommanderModifiers { IncomeBonus = 0.2f }),
            new(WV_DoctrineId.DrillSergeants, "Drill Sergeants", "Barracks and factories work 18% faster.", "barracks",
                WV_DoctrineRarity.Common, 3, null, new WV_CommanderModifiers { ProductionSpeedBonus = 0.18f }),
            new(WV_DoctrineId.HardenedInfantry, "Hardened Infantry", "Your troops have 20% more health, the ones on the field too.", "shield",
                WV_DoctrineRarity.Common, 3, null, new WV_CommanderModifiers { TroopHealthBonus = 0.2f }),
            new(WV_DoctrineId.Sharpshooters, "Sharpshooters", "Your troops deal 15% more damage, from now on.", "crosshair",
                WV_DoctrineRarity.Common, 3, null, new WV_CommanderModifiers { TroopDamageBonus = 0.15f }),
            new(WV_DoctrineId.ArmoredColumns, "Armored Columns", "Your vehicles have 25% more health, the ones on the field too.", "battle_tank",
                WV_DoctrineRarity.Common, 3, null, new WV_CommanderModifiers { VehicleHealthBonus = 0.25f }),
            new(WV_DoctrineId.Fortifications, "Fortifications", "Your buildings and walls have 25% more health, standing ones too.", "castle",
                WV_DoctrineRarity.Common, 3, null, new WV_CommanderModifiers { StructureHealthBonus = 0.25f }),
            new(WV_DoctrineId.Quartermaster, "Quartermaster", "15% of a troop's or vehicle's price comes back when it arrives.", "backpack",
                WV_DoctrineRarity.Common, 2, null, new WV_CommanderModifiers { DeliveryRebate = 0.15f }),
            new(WV_DoctrineId.WarChest, "War Chest", "Funds now: 250, plus 50 for every wave held.", "coins",
                WV_DoctrineRarity.Common, 3, null, default, instantFunds: 250, instantFundsPerWave: 50),
            new(WV_DoctrineId.Bastion, "Bastion", "The flag gains 20% more health and repairs 10%.", "flying_flag",
                WV_DoctrineRarity.Common, 3, null, default, flagHealthBonus: 0.2f, flagRepair: 0.1f),
            new(WV_DoctrineId.BountyHunters, "Bounty Hunters", "+6 funds for every enemy your forces defeat.", "star_medal",
                WV_DoctrineRarity.Common, 3, WV_Retention.CardBountyHunters, new WV_CommanderModifiers { FundsPerKill = 6 }),
            new(WV_DoctrineId.Overcharge, "Overcharge", "Your turrets deal 30% more damage.", "bolt",
                WV_DoctrineRarity.Common, 3, WV_Retention.CardOvercharge, new WV_CommanderModifiers { TurretDamageBonus = 0.3f }),
            new(WV_DoctrineId.FieldMedics, "Field Medics", "Your troops regain 2% of their health every second.", "favorite",
                WV_DoctrineRarity.Common, 2, WV_Retention.CardFieldMedics, new WV_CommanderModifiers { TroopRegenPerSecond = 0.02f }),
            new(WV_DoctrineId.TotalWar, "Total War", "Troops and turrets deal 25% more damage; vehicles have 20% more health.", "crossed_swords",
                WV_DoctrineRarity.Elite, 1, WV_Retention.EliteDoctrines,
                new WV_CommanderModifiers { TroopDamageBonus = 0.25f, TurretDamageBonus = 0.25f, VehicleHealthBonus = 0.2f }),
            new(WV_DoctrineId.IronCitadel, "Iron Citadel", "Buildings have 50% more health; the flag gains 30% and repairs fully.", "templar_shield",
                WV_DoctrineRarity.Elite, 1, WV_Retention.EliteDoctrines,
                new WV_CommanderModifiers { StructureHealthBonus = 0.5f }, flagHealthBonus: 0.3f, flagRepair: 1f),
            new(WV_DoctrineId.Blitzkrieg, "Blitzkrieg", "Production 40% faster and mines pay 25% more.", "speed",
                WV_DoctrineRarity.Elite, 1, WV_Retention.EliteDoctrines,
                new WV_CommanderModifiers { ProductionSpeedBonus = 0.4f, IncomeBonus = 0.25f }),
        };

        public static IReadOnlyList<WV_DoctrineDefinition> All => all;

        public static WV_DoctrineDefinition Get(WV_DoctrineId id) {
            int index = (int)id;
            return index >= 0 && index < all.Length ? all[index] : null;
        }

        public static bool IsValid(byte id) => id < all.Length;

        /// <summary>A fresh, all-zero rank array.</summary>
        public static byte[] NewRanks() => new byte[Count];

        /// <summary>The lasting bonuses a commander's ranks add up to.</summary>
        public static WV_CommanderModifiers Sum(IReadOnlyList<byte> ranks) {
            var total = default(WV_CommanderModifiers);
            if (ranks == null)
                return total;
            for (int i = 0; i < ranks.Count && i < all.Length; i++) {
                int rank = Mathf.Min(ranks[i], all[i].MaxRank);
                if (rank > 0)
                    total.Add(all[i].PerRank, rank);
            }
            return total;
        }

        /// <summary>
        /// Deals up to <paramref name="handSize"/> different cards a commander may still take:
        /// unlocked (<paramref name="isUnlocked"/> answers for a card's unlock id) and below its max
        /// rank. Each slot is an elite card with <paramref name="eliteChance"/> when elites are
        /// allowed and one is left, otherwise a common one.
        /// </summary>
        public static List<WV_DoctrineId> Deal(int seed, IReadOnlyList<byte> ranks, int handSize,
            Func<string, bool> isUnlocked, float eliteChance) {
            var commons = new List<WV_DoctrineId>();
            var elites = new List<WV_DoctrineId>();
            foreach (WV_DoctrineDefinition doctrine in all) {
                int rank = ranks != null && (int)doctrine.Id < ranks.Count ? ranks[(int)doctrine.Id] : 0;
                if (rank >= doctrine.MaxRank)
                    continue;
                if (doctrine.UnlockId != null && (isUnlocked == null || !isUnlocked(doctrine.UnlockId)))
                    continue;
                (doctrine.Rarity == WV_DoctrineRarity.Elite ? elites : commons).Add(doctrine.Id);
            }

            var random = new System.Random(seed);
            var hand = new List<WV_DoctrineId>(handSize);
            while (hand.Count < handSize && (commons.Count > 0 || elites.Count > 0)) {
                bool elite = elites.Count > 0 && (commons.Count == 0 || random.NextDouble() < eliteChance);
                List<WV_DoctrineId> source = elite ? elites : commons;
                int pick = random.Next(source.Count);
                hand.Add(source[pick]);
                source.RemoveAt(pick);
            }
            return hand;
        }

        /// <summary>
        /// Puts <paramref name="featured"/> first in <paramref name="hand"/> (the day's featured card),
        /// replacing the hand's last card so the hand keeps its size. Nothing changes when the card is
        /// unknown, locked behind an unlock, or already at its max rank. Returns whether the hand holds it.
        /// </summary>
        public static bool Feature(List<WV_DoctrineId> hand, WV_DoctrineId featured, IReadOnlyList<byte> ranks) {
            WV_DoctrineDefinition doctrine = Get(featured);
            if (hand == null || doctrine == null || doctrine.UnlockId != null)
                return false;
            int rank = ranks != null && (int)featured < ranks.Count ? ranks[(int)featured] : 0;
            if (rank >= doctrine.MaxRank)
                return false;
            int size = Math.Max(1, hand.Count);
            hand.Remove(featured);
            hand.Insert(0, featured);
            while (hand.Count > size)
                hand.RemoveAt(hand.Count - 1);
            return true;
        }

        /// <summary>A seed for one deal: the round, the commander and how many hands they have been dealt.</summary>
        public static int Seed(int roundSeed, int clientId, int dealIndex) =>
            unchecked(roundSeed * 486187739 + (clientId + 1) * 16777619 + dealIndex * 97);

        /// <summary>What <paramref name="doctrine"/> acts on, from what it adds.</summary>
        public static WV_DoctrineTargets Targets(WV_DoctrineDefinition doctrine) {
            if (doctrine == null)
                return WV_DoctrineTargets.None;
            WV_CommanderModifiers m = doctrine.PerRank;
            var targets = WV_DoctrineTargets.None;
            if (m.TroopHealthBonus > 0f || m.TroopDamageBonus > 0f || m.TroopRegenPerSecond > 0f || m.FundsPerKill > 0)
                targets |= WV_DoctrineTargets.Troops;
            if (m.VehicleHealthBonus > 0f)
                targets |= WV_DoctrineTargets.Vehicles;
            if (m.StructureHealthBonus > 0f)
                targets |= WV_DoctrineTargets.Structures;
            if (m.TurretDamageBonus > 0f)
                targets |= WV_DoctrineTargets.Turrets;
            if (m.IncomeBonus > 0f)
                targets |= WV_DoctrineTargets.Mines;
            if (m.ProductionSpeedBonus > 0f || m.DeliveryRebate > 0f)
                targets |= WV_DoctrineTargets.Production;
            if (doctrine.FlagHealthBonus > 0f || doctrine.FlagRepair > 0f || doctrine.InstantFunds > 0 || m.BountyBonus > 0f)
                targets |= WV_DoctrineTargets.Flag;
            return targets;
        }

        /// <summary>The family a doctrine's card is coloured by.</summary>
        public static WV_DoctrineFamily Family(WV_DoctrineDefinition doctrine) {
            if (doctrine == null)
                return WV_DoctrineFamily.Economy;
            WV_CommanderModifiers m = doctrine.PerRank;
            if (doctrine.FlagHealthBonus > 0f || doctrine.FlagRepair > 0f)
                return WV_DoctrineFamily.Flag;
            if (m.StructureHealthBonus > 0f || m.TurretDamageBonus > 0f)
                return WV_DoctrineFamily.Base;
            if (m.VehicleHealthBonus > 0f)
                return WV_DoctrineFamily.Armour;
            if (m.TroopHealthBonus > 0f || m.TroopDamageBonus > 0f || m.TroopRegenPerSecond > 0f || m.ProductionSpeedBonus > 0f)
                return WV_DoctrineFamily.Troops;
            return WV_DoctrineFamily.Economy;
        }

        /// <summary>"Rank 2/3" for the card, or "New" for a first rank.</summary>
        public static string DescribeRank(WV_DoctrineDefinition doctrine, int currentRank) =>
            currentRank <= 0 ? (doctrine.MaxRank > 1 ? $"New - rank 1/{doctrine.MaxRank}" : "New")
                : $"Rank {currentRank + 1}/{doctrine.MaxRank}";
    }
}
