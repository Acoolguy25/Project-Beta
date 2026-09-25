using System.Collections.Generic;
using RyanAssets.Shared.Combat;
using RyanAssets.Shared.Declarations;

namespace Universes.UniverseData.war_valley.Shared {
    /// <summary>
    /// A War Valley game mode. Survival is the only one: every commander defends the flag together
    /// against the waves. Values may be appended but never renumbered.
    /// </summary>
    public enum WV_GameMode : byte {
        Survival = 0
    }

    /// <summary>
    /// Who fights beside whom, in one place.
    /// <para>
    /// Alliance in War Valley is a property of the game mode, not of each player: the mode decides
    /// which side each commander fights for and which sides are enemies, and everything else -
    /// combat, building permissions, donations - asks this class rather than repeating the rule.
    /// A side is a <see cref="TeamConfig.realTeam"/>; a commander's own colour lives in the display
    /// half of the team and never affects alliance.
    /// </para>
    /// <para>
    /// In <see cref="WV_GameMode.Survival"/> every commander is on <see cref="Defenders"/> and every
    /// wave is on <see cref="Invaders"/>, so all players are allies and every wave is their enemy.
    /// </para>
    /// </summary>
    public static class WV_Alliances {
        /// <summary>The side the players fight on in Survival.</summary>
        public const TeamColor Defenders = TeamColor.Blue;

        /// <summary>The side the waves fight on in Survival.</summary>
        public const TeamColor Invaders = TeamColor.Red;

        /// <summary>
        /// The mode alliances are decided by. Survival is the only mode, so this is also what every
        /// client assumes; the server runner sets it once when the match starts.
        /// </summary>
        public static WV_GameMode Mode { get; set; } = WV_GameMode.Survival;

        /// <summary>The side a commander fights for under the current mode.</summary>
        public static TeamColor GetCommanderSide(int clientId) => Mode switch {
            _ => Defenders
        };

        /// <summary>The side the match's hostile waves fight for under the current mode.</summary>
        public static TeamColor GetWaveSide() => Mode switch {
            _ => Invaders
        };

        /// <summary>
        /// The "attacker may hurt target" table the mode fights under, in the shape
        /// <c>SharedGlobalEvents.TeamEnemies</c> replicates. Built fresh so the caller owns it.
        /// </summary>
        public static Dictionary<TeamColor, HashSet<TeamColor>> BuildEnemyTable() => Mode switch {
            _ => new Dictionary<TeamColor, HashSet<TeamColor>> {
                [Invaders] = new() { Defenders },
                [Defenders] = new() { Invaders }
            }
        };

        /// <summary>
        /// True when two commanders fight on the same side. Read from their replicated teams where
        /// both are known, so a client and the server give the same answer; a commander whose
        /// player record is gone falls back to the side the mode would give them.
        /// </summary>
        public static bool AreAllied(int clientA, int clientB) {
            if (clientA == WV_Owned.NoOwner || clientB == WV_Owned.NoOwner)
                return false;
            if (clientA == clientB)
                return true;
            return CombatTeams.AreAllies(WV_Permissions.GetCommanderTeam(clientA), WV_Permissions.GetCommanderTeam(clientB));
        }

        /// <summary>True when a commander fights on the same side as this team.</summary>
        public static bool IsAlliedWith(int clientId, TeamConfig team) =>
            clientId != WV_Owned.NoOwner && CombatTeams.AreAllies(WV_Permissions.GetCommanderTeam(clientId), team);
    }
}
