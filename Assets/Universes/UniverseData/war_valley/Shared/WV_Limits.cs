using RyanAssets.Shared.Declarations;

namespace Universes.UniverseData.war_valley.Shared {
    /// <summary>
    /// What a commander's forces are counted against. Travels on no wire; the category is always
    /// derived from the item or structure itself.
    /// </summary>
    public enum WV_ForceCategory : byte {
        /// <summary>Not counted: walls and fences, and anything that is not a commander's force.</summary>
        None = 0,
        /// <summary>Foot soldiers and ground vehicles.</summary>
        Soldier = 1,
        Aircraft = 2,
        Building = 3
    }

    /// <summary>
    /// The per-commander force limits: how many soldiers, aircraft, and buildings each player may
    /// have at once.
    /// <para>
    /// A limit counts what a commander already fields plus what they have paid for and is still in
    /// a queue, so an order that would break the limit is refused at the button, before any money
    /// changes hands, instead of being refunded when the unit fails to appear. The server and the
    /// client both count through here, so the menu's lock and the server's refusal cannot drift.
    /// </para>
    /// <para>
    /// Troops are the one thing counted from outside: who commands a troop is recorded only on the
    /// server, so callers pass in how many troops the commander fields - the server from its roster,
    /// a client from the troops it recognises as its own.
    /// </para>
    /// </summary>
    public static class WV_Limits {
        /// <summary>Foot soldiers and ground vehicles one commander may field.</summary>
        public const int MaxSoldiers = 20;
        public const int MaxAircraft = 10;
        /// <summary>Buildings one commander may own. Walls and fences are not counted.</summary>
        public const int MaxBuildings = 50;

        public static int GetLimit(WV_ForceCategory category) => category switch {
            WV_ForceCategory.Soldier => MaxSoldiers,
            WV_ForceCategory.Aircraft => MaxAircraft,
            WV_ForceCategory.Building => MaxBuildings,
            _ => int.MaxValue
        };

        public static string GetDisplayName(WV_ForceCategory category) => category switch {
            WV_ForceCategory.Soldier => "Soldiers",
            WV_ForceCategory.Aircraft => "Aircraft",
            WV_ForceCategory.Building => "Buildings",
            _ => "Forces"
        };

        /// <summary>What the HUD says when an order is refused for this category's limit.</summary>
        public static string GetLimitMessage(WV_ForceCategory category) => category switch {
            WV_ForceCategory.Soldier => $"Soldier limit reached ({MaxSoldiers} per player)",
            WV_ForceCategory.Aircraft => $"Aircraft limit reached ({MaxAircraft} per player)",
            WV_ForceCategory.Building => $"Building limit reached ({MaxBuildings} per player)",
            _ => "Limit reached"
        };

        public static WV_ForceCategory GetCategory(WV_ProductionItem item) {
            if (item.IsNone)
                return WV_ForceCategory.None;
            if (item.IsTroop)
                return WV_ForceCategory.Soldier;
            return WV_Rules.IsAircraft(item.UnitKind) ? WV_ForceCategory.Aircraft : WV_ForceCategory.Soldier;
        }

        public static WV_ForceCategory GetCategory(WV_Unit unit) =>
            unit == null ? WV_ForceCategory.None
            : unit.IsAircraft ? WV_ForceCategory.Aircraft
            : WV_ForceCategory.Soldier;

        /// <summary>
        /// A structure counts as a building unless it is a wall or fence segment: a perimeter is
        /// laid a segment at a time, and counting each one would spend the limit on a single wall.
        /// </summary>
        public static WV_ForceCategory GetCategory(StructureComponent structure) =>
            structure == null || structure.GetComponent<WV_DestructibleObstacle>() != null
                ? WV_ForceCategory.None
                : WV_ForceCategory.Building;

        /// <summary>
        /// Everything counted against <paramref name="category"/> for this commander: what they
        /// field, plus what they have paid for that is still waiting in any production queue.
        /// </summary>
        /// <param name="fieldedTroops">Living troops the commander fields, counted by the caller.</param>
        public static int CountUsed(int clientId, WV_ForceCategory category, int fieldedTroops) {
            if (clientId == WV_Owned.NoOwner || category == WV_ForceCategory.None)
                return 0;
            if (category == WV_ForceCategory.Building)
                return CountBuildings(clientId);

            int used = CountQueued(clientId, category);
            foreach (WV_Unit unit in WV_Unit.All) {
                if (!unit.IsDead && unit.Owned != null && unit.Owned.IsOwnedBy(clientId) && GetCategory(unit) == category)
                    used++;
            }
            if (category == WV_ForceCategory.Soldier)
                used += fieldedTroops;
            return used;
        }

        /// <summary>True while one more of <paramref name="category"/> fits under the commander's limit.</summary>
        public static bool HasRoom(int clientId, WV_ForceCategory category, int fieldedTroops) =>
            category == WV_ForceCategory.None
            || CountUsed(clientId, category, fieldedTroops) < GetLimit(category);

        /// <summary>Paid-for items of this category waiting in any production queue.</summary>
        public static int CountQueued(int clientId, WV_ForceCategory category) {
            int count = 0;
            foreach (WV_ProductionBuilding building in WV_ProductionBuilding.All) {
                for (int i = 0; i < building.QueueLength; i++) {
                    if (building.GetQueuedPayer(i) == clientId && GetCategory(building.GetQueuedItem(i)) == category)
                        count++;
                }
            }
            return count;
        }

        /// <summary>Standing buildings the commander owns, sites under construction included.</summary>
        public static int CountBuildings(int clientId) {
            int count = 0;
            foreach (WV_Owned owned in WV_Owned.All) {
                if (!owned.IsOwnedBy(clientId) || !owned.TryGetComponent(out StructureComponent structure))
                    continue;
                if (!structure.IsDead && GetCategory(structure) == WV_ForceCategory.Building)
                    count++;
            }
            return count;
        }
    }
}
