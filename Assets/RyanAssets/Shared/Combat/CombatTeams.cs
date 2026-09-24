using System.Collections.Generic;
using RyanAssets.Shared.Declarations;
using RyanAssets.Shared.Global;

namespace RyanAssets.Shared.Combat {
    /// <summary>
    /// Who may shoot whom. One implementation so a turret, a unit, and an explosion all agree on
    /// which entities are hostile instead of each system carrying its own copy of the rule.
    /// </summary>
    public static class CombatTeams {
        /// <summary>
        /// True when both sides fight for the same real team. Display colours are deliberately
        /// ignored: two commanders painted in different colours on the same side are still allies.
        /// </summary>
        public static bool AreAllies(TeamConfig a, TeamConfig b) =>
            a != null && b != null && a.realTeam != TeamColor.None && a.realTeam == b.realTeam;

        public static bool AreEnemies(TeamConfig attacker, TeamConfig target) {
            if (attacker == null || target == null)
                return false;
            if (attacker.realTeam == target.realTeam)
                return false;

            Dictionary<TeamColor, HashSet<TeamColor>> enemies = SharedGlobalEvents.TeamEnemies;
            // Before a runner publishes its team table, treat nothing as hostile rather than
            // letting freshly spawned entities open fire on their own side.
            return enemies != null
                && enemies.TryGetValue(attacker.realTeam, out HashSet<TeamColor> hostile)
                && hostile.Contains(target.realTeam);
        }
    }
}
