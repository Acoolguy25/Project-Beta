using RyanAssets.DataService;
using RyanAssets.Shared.Combat;
using RyanAssets.Shared.Declarations;

namespace Universes.UniverseData.war_valley.Shared {
    /// <summary>
    /// Who may do what with a commander's buildings.
    /// <para>
    /// Two levels, and one rule for each, so the server's validation and the client's buttons can
    /// never disagree:
    /// </para>
    /// <list type="bullet">
    /// <item><b>Use</b> - select it, read its stats, queue units, set its rally point, research at
    /// it. Open to the owner and to every ally, because a base is shared by the side that holds it:
    /// in survival every commander is on the same side.</item>
    /// <item><b>Manage</b> - demolish it or cancel what is in its queue. The owner alone, since both
    /// throw away something the owner built or an ally paid for.</item>
    /// </list>
    /// <para>
    /// Alliance is decided by real team, the same field combat uses, and is read from the building's
    /// own replicated team rather than from its owner's connection - so a base whose commander has
    /// left stays usable by the side that is still fighting from it.
    /// </para>
    /// </summary>
    public static class WV_Permissions {
        /// <summary>Whether <paramref name="clientId"/> may select, queue at, and otherwise use this object.</summary>
        public static bool CanUse(int clientId, WV_Owned owned) {
            if (owned == null || clientId == WV_Owned.NoOwner)
                return false;
            if (owned.IsOwnedBy(clientId))
                return true;
            return TryGetCommanderTeam(clientId, out TeamConfig team) && CombatTeams.AreAllies(team, owned.Team);
        }

        /// <summary>Whether <paramref name="clientId"/> may demolish this object or cancel its queue.</summary>
        public static bool CanManage(int clientId, WV_Owned owned) =>
            owned != null && owned.IsOwnedBy(clientId);

        /// <summary>
        /// The team a commander's structures, units, and troops fight under: the commander's own real
        /// team, displayed in their commander colour. Falls back to the side every War Valley
        /// commander starts on when their player record is not available.
        /// </summary>
        public static TeamConfig GetCommanderTeam(int clientId) {
            TeamColor realTeam = TryGetCommanderTeam(clientId, out TeamConfig team) && team.realTeam != TeamColor.None
                ? team.realTeam
                : TeamColor.Blue;
            return new TeamConfig(realTeam, WV_Rules.GetCommanderColor(clientId));
        }

        /// <summary>The replicated team of a connected commander.</summary>
        public static bool TryGetCommanderTeam(int clientId, out TeamConfig team) {
            team = null;
            if (clientId == WV_Owned.NoOwner || !PlayerData.TryGetPlayerData(clientId, out PlayerData player))
                return false;
            team = player.GetTeam();
            return team != null;
        }

        /// <summary>The side - real team - a commander researches and fights for.</summary>
        public static TeamColor GetSide(int clientId) => GetCommanderTeam(clientId).realTeam;
    }
}
