using System;
using System.Collections.Generic;
using UnityEngine;

namespace Universes.UniverseData.war_valley.Shared {
    /// <summary>
    /// A War Valley technology. Travels on the wire as a byte in research requests and is part of
    /// the replicated research key, so values may be appended but never renumbered.
    /// </summary>
    public enum WV_Tech : byte {
        None = 0,
        /// <summary>Helicopter flight: unlocks the helipad and the attack chopper it launches.</summary>
        RotaryAviation = 1,
        /// <summary>Energy shields: unlocks the shield generator.</summary>
        ShieldTechnology = 2
    }

    /// <summary>
    /// One researchable technology: what it costs, how long it takes at a single research station,
    /// and which structures and units it unlocks.
    /// </summary>
    public sealed class WV_TechDefinition {
        public WV_Tech Tech { get; }
        public string DisplayName { get; }
        /// <summary>Research category, shown as the card's role line.</summary>
        public string Category { get; }
        public string Description { get; }
        public int Cost { get; }
        /// <summary>Seconds the project takes with one research station working on it alone.</summary>
        public float ResearchSeconds { get; }
        /// <summary><c>StructureComponent.StructureID</c>s that cannot be placed until this is researched.</summary>
        public IReadOnlyList<string> UnlockedStructureIds { get; }
        public IReadOnlyList<WV_UnitKind> UnlockedUnits { get; }
        public IReadOnlyList<WV_Tech> Prerequisites { get; }

        public WV_TechDefinition(
            WV_Tech tech, string displayName, string category, string description, int cost, float researchSeconds,
            string[] unlockedStructureIds = null, WV_UnitKind[] unlockedUnits = null, WV_Tech[] prerequisites = null) {
            Tech = tech;
            DisplayName = displayName;
            Category = category;
            Description = description;
            Cost = cost;
            ResearchSeconds = researchSeconds;
            UnlockedStructureIds = unlockedStructureIds ?? Array.Empty<string>();
            UnlockedUnits = unlockedUnits ?? Array.Empty<WV_UnitKind>();
            Prerequisites = prerequisites ?? Array.Empty<WV_Tech>();
        }
    }

    /// <summary>
    /// The War Valley technology tree and the research-speed rule, shared by the authoritative
    /// server, the client's research menu, and the build menu's locks.
    /// <para>
    /// Locks are declared here, against a structure's existing <c>StructureID</c> and a unit's
    /// <see cref="WV_UnitKind"/>, rather than as a second field on each prefab. One table answers
    /// "what does this research unlock" and "what does this building need" from the same data, so
    /// the two can never disagree.
    /// </para>
    /// <para>
    /// Research speed is linear on both sides: every finished research station adds its rate to
    /// its owner's total, and every project that commander runs at once takes an equal share of
    /// that total. Two projects therefore finish no sooner together than one after the other.
    /// </para>
    /// </summary>
    public static class WV_TechTree {
        static readonly WV_TechDefinition[] definitions = {
            new(WV_Tech.RotaryAviation, "Rotary Aviation", "Aviation",
                "Helicopter flight. Unlocks the Helipad and the attack Chopper it launches.",
                cost: 500, researchSeconds: 60f,
                unlockedStructureIds: new[] { "wv_helipad" },
                unlockedUnits: new[] { WV_UnitKind.Chopper }),
            new(WV_Tech.ShieldTechnology, "Shield Technology", "Defense",
                "Energy shields. Unlocks the Shield Generator, which raises a shield enemies must " +
                "break before they can hurt anything inside it.",
                cost: 900, researchSeconds: 75f,
                unlockedStructureIds: new[] { WV_Rules.ShieldGeneratorId })
        };

        public static IReadOnlyList<WV_TechDefinition> All => definitions;

        public static WV_TechDefinition Get(WV_Tech tech) {
            foreach (WV_TechDefinition definition in definitions) {
                if (definition.Tech == tech)
                    return definition;
            }
            return null;
        }

        public static string GetDisplayName(WV_Tech tech) => Get(tech)?.DisplayName ?? "research";

        /// <summary>The technology a structure waits on, or <see cref="WV_Tech.None"/> when it is always available.</summary>
        public static WV_Tech GetRequirement(string structureId) {
            if (string.IsNullOrEmpty(structureId))
                return WV_Tech.None;
            foreach (WV_TechDefinition definition in definitions) {
                foreach (string unlocked in definition.UnlockedStructureIds) {
                    if (unlocked == structureId)
                        return definition.Tech;
                }
            }
            return WV_Tech.None;
        }

        /// <summary>The technology a production item waits on. Foot soldiers are never locked.</summary>
        public static WV_Tech GetRequirement(WV_ProductionItem item) {
            if (item.IsTroop || item.IsNone)
                return WV_Tech.None;
            foreach (WV_TechDefinition definition in definitions) {
                foreach (WV_UnitKind unlocked in definition.UnlockedUnits) {
                    if (unlocked == item.UnitKind)
                        return definition.Tech;
                }
            }
            return WV_Tech.None;
        }

        /// <summary>
        /// Share of a side's research throughput one project receives: the stations' combined rate
        /// split evenly across every project running at once.
        /// </summary>
        public static float GetProjectRate(float stationRate, int activeProjects) =>
            activeProjects <= 0 ? 0f : Mathf.Max(0f, stationRate) / activeProjects;

        /// <summary>Fraction of <paramref name="definition"/> completed per second under the given load.</summary>
        public static float GetProgressPerSecond(WV_TechDefinition definition, float stationRate, int activeProjects) {
            if (definition == null)
                return 0f;
            return GetProjectRate(stationRate, activeProjects) / Mathf.Max(0.01f, definition.ResearchSeconds);
        }
    }
}
