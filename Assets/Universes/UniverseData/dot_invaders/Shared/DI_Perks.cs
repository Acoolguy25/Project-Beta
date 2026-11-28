using System;
using System.Collections.Generic;
using UnityEngine;

namespace Universes.UniverseData.dot_invaders {
    /// <summary>
    /// A commander perk: a lasting edge for your team for the whole match, unlocked by Dot Invaders level
    /// and equipped from the perk panel. Its numbers live here, so the server, the panel and the tests
    /// all read the same table.
    /// </summary>
    public sealed class DI_PerkDefinition {
        /// <summary>Stable id: saved in the player's loadout and used as the unlock id.</summary>
        public readonly string Id;
        public readonly string Title;
        public readonly string Description;
        /// <summary>Theme icon name.</summary>
        public readonly string Icon;
        public readonly int UnlockLevel;
        public readonly DI_PerkEffects Effects;

        public DI_PerkDefinition(string id, string title, string description, string icon, int unlockLevel, DI_PerkEffects effects) {
            Id = id;
            Title = title;
            Description = description;
            Icon = icon;
            UnlockLevel = unlockLevel;
            Effects = effects;
        }

        public override string ToString() => Id;
    }

    /// <summary>What a team's equipped perks do, combined. <see cref="None"/> changes nothing.</summary>
    public struct DI_PerkEffects {
        /// <summary>Extra troops in the home base at the start.</summary>
        public int startTroops;
        /// <summary>Troop move speed multiplier.</summary>
        public float moveSpeed;
        /// <summary>Training speed multiplier of every base.</summary>
        public float production;
        /// <summary>Attackers needed per defender in your bases (a fortress multiplies on top).</summary>
        public float defence;
        /// <summary>Damage your troops deal to a garrison they land on.</summary>
        public float siege;
        /// <summary>Damage your troops deal in road clashes.</summary>
        public float clash;
        /// <summary>The team has the once-per-match Overdrive ability.</summary>
        public bool overdrive;

        public static DI_PerkEffects None => new() { moveSpeed = 1f, production = 1f, defence = 1f, siege = 1f, clash = 1f };

        /// <summary>Two sets together: bonuses add, multipliers multiply.</summary>
        public static DI_PerkEffects Combine(DI_PerkEffects a, DI_PerkEffects b) => new() {
            startTroops = a.startTroops + b.startTroops,
            moveSpeed = a.moveSpeed * b.moveSpeed,
            production = a.production * b.production,
            defence = a.defence * b.defence,
            siege = a.siege * b.siege,
            clash = a.clash * b.clash,
            overdrive = a.overdrive || b.overdrive,
        };
    }

    /// <summary>Tuning of the Overdrive ability, exposed on the server runner.</summary>
    [Serializable]
    public sealed class DI_OverdriveTuning {
        public const float DefaultProductionMultiplier = 3f;
        public const float DefaultSeconds = 12f;

        [Tooltip("How much faster every base of the team trains while Overdrive runs.")]
        [Range(1f, 10f)] public float productionMultiplier = DefaultProductionMultiplier;
        [Tooltip("How long Overdrive lasts (seconds).")]
        [Range(1f, 60f)] public float seconds = DefaultSeconds;
        [Tooltip("Seconds into the match before Overdrive can be used, so it cannot snowball the opening.")]
        [Range(0f, 120f)] public float lockedSeconds = 20f;
    }

    /// <summary>Where a player's Overdrive stands, as the HUD shows it.</summary>
    public enum DI_AbilityState {
        /// <summary>Not equipped this match.</summary>
        None = 0,
        /// <summary>Equipped, not usable yet (the opening seconds).</summary>
        Locked = 1,
        Ready = 2,
        Active = 3,
        /// <summary>Used this match.</summary>
        Spent = 4,
    }

    /// <summary>The perk table, the slot rule and loadout checks.</summary>
    public static class DI_Perks {
        public const string HeadStart = "head_start";
        public const string QuickMarch = "quick_march";
        public const string SupplyLines = "supply_lines";
        public const string Overdrive = "overdrive";
        public const string IronWalls = "iron_walls";
        public const string SiegeBreakers = "siege_breakers";
        public const string Vanguard = "vanguard";
        /// <summary>Unlock id of the second perk slot (not a perk).</summary>
        public const string SecondSlot = "perk_slot_2";
        public const int SecondSlotLevel = 20;
        public const int MaxSlots = 2;

        const int HeadStartTroops = 10;
        const float QuickMarchSpeed = 1.15f;
        const float SupplyLinesProduction = 1.15f;
        const float IronWallsDefence = 1.25f;
        const float SiegeBreakersDamage = 1.3f;
        const float VanguardClash = 1.25f;

        static DI_PerkEffects With(Func<DI_PerkEffects, DI_PerkEffects> change) => change(DI_PerkEffects.None);

        static readonly DI_PerkDefinition[] all = {
            new(HeadStart, "Head Start", $"Start with +{HeadStartTroops} troops in your home base.", "rally_troops", 2,
                With(e => { e.startTroops = HeadStartTroops; return e; })),
            new(QuickMarch, "Quick March", $"Your troops move {Percent(QuickMarchSpeed)} faster.", "sprint", 3,
                With(e => { e.moveSpeed = QuickMarchSpeed; return e; })),
            new(SupplyLines, "Supply Lines", $"Your bases train {Percent(SupplyLinesProduction)} faster.", "barracks", 5,
                With(e => { e.production = SupplyLinesProduction; return e; })),
            new(Overdrive, "Overdrive",
                $"Once a match, press X: your bases train {DI_OverdriveTuning.DefaultProductionMultiplier:0.#}x for {DI_OverdriveTuning.DefaultSeconds:0} seconds.", "bolt", 7,
                With(e => { e.overdrive = true; return e; })),
            new(IronWalls, "Iron Walls", $"Attackers need {Percent(IronWallsDefence)} more troops to take your bases.", "templar_shield", 9,
                With(e => { e.defence = IronWallsDefence; return e; })),
            new(SiegeBreakers, "Siege Breakers", $"Your troops deal {Percent(SiegeBreakersDamage)} more damage to garrisons.", "battle_tank", 12,
                With(e => { e.siege = SiegeBreakersDamage; return e; })),
            new(Vanguard, "Vanguard", $"Your troops win road clashes: {Percent(VanguardClash)} more clash damage.", "crossed_swords", 15,
                With(e => { e.clash = VanguardClash; return e; })),
        };

        static string Percent(float multiplier) => $"{Mathf.RoundToInt((multiplier - 1f) * 100f)}%";

        public static IReadOnlyList<DI_PerkDefinition> All => all;

        public static DI_PerkDefinition Find(string id) {
            if (string.IsNullOrEmpty(id)) return null;
            foreach (DI_PerkDefinition perk in all)
                if (perk.Id == id) return perk;
            return null;
        }

        /// <summary>Perk slots a player of <paramref name="level"/> has (0 before the first perk unlocks).</summary>
        public static int SlotsAt(int level) {
            if (level >= SecondSlotLevel) return MaxSlots;
            foreach (DI_PerkDefinition perk in all)
                if (level >= perk.UnlockLevel) return 1;
            return 0;
        }

        public static bool IsUnlockedAt(string id, int level) => Find(id) is { } perk && level >= perk.UnlockLevel;

        /// <summary>
        /// A valid loadout from <paramref name="requested"/>: known, unlocked at <paramref name="level"/>,
        /// no duplicates, at most the slots the level gives, in the requested order.
        /// </summary>
        public static List<string> Sanitize(IEnumerable<string> requested, int level) {
            var loadout = new List<string>();
            if (requested == null) return loadout;
            int slots = SlotsAt(level);
            foreach (string id in requested) {
                if (loadout.Count >= slots) break;
                if (IsUnlockedAt(id, level) && !loadout.Contains(id))
                    loadout.Add(id);
            }
            return loadout;
        }

        /// <summary>The combined effects of a loadout (unknown ids are ignored).</summary>
        public static DI_PerkEffects EffectsOf(IEnumerable<string> loadout) {
            DI_PerkEffects effects = DI_PerkEffects.None;
            if (loadout == null) return effects;
            foreach (string id in loadout)
                if (Find(id) is { } perk)
                    effects = DI_PerkEffects.Combine(effects, perk.Effects);
            return effects;
        }

        /// <summary>
        /// The loadout after choosing <paramref name="id"/>: equipped perks come off; otherwise it goes
        /// into a free slot, or replaces the oldest when every slot is full. What the perk panel sends.
        /// </summary>
        public static List<string> Toggle(IReadOnlyList<string> loadout, string id, int level) {
            var next = Sanitize(loadout, level);
            if (next.Remove(id)) return next;
            if (!IsUnlockedAt(id, level)) return next;
            if (next.Count >= SlotsAt(level) && next.Count > 0) next.RemoveAt(0);
            next.Add(id);
            return next;
        }
    }
}
