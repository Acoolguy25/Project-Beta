using System;
using System.Collections.Generic;
using RyanAssets.Characters.Shared;
using RyanAssets.DataService;
using RyanAssets.Progress;
using RyanAssets.Server.ServerCore;
using RyanAssets.Shared.Declarations;
using Universes.UniverseData.murder_mystery;

namespace Universes.murder_mystery.Server {
    /// <summary>What murder mystery remembers about a player between sessions. Written only by the server.</summary>
    public sealed class MM_SaveData : ProgressData {
        public const string Universe = "murder_mystery";
        public override int LatestVersion => 1;

        /// <summary>Rounds of any mode the player was in when they started.</summary>
        public int roundsPlayed;
        /// <summary>Classic rounds won, by the role the player had.</summary>
        public int innocentWins, sheriffWins, murdererWins;
        /// <summary>Meeting votes cast for a suspect who really was a murderer.</summary>
        public int correctVotes;

        public void RecordWin(MM_Role role) {
            switch (role) {
                case MM_Role.Innocent: innocentWins++; break;
                case MM_Role.Sheriff: sheriffWins++; break;
                case MM_Role.Murderer: murdererWins++; break;
            }
        }

        public override void Validate() {
            roundsPlayed = Math.Max(0, roundsPlayed);
            innocentWins = Math.Max(0, innocentWins);
            sheriffWins = Math.Max(0, sheriffWins);
            murdererWins = Math.Max(0, murdererWins);
            correctVotes = Math.Max(0, correctVotes);
        }

        // ---------------------------------------------------------------- recording (server)

        /// <summary>
        /// A Classic round ended with <paramref name="winner"/>'s side on top. Murderers win together;
        /// sheriffs and innocents are the town and win together (red is the murderers' team).
        /// </summary>
        public static void RecordClassicResult(TeamColor winner) {
            bool murderersWon = winner == TeamColor.Red;
            foreach (PlayerData player in PlayerData.Players.Values) {
                MM_Role role = player != null && player.team.Value != null ? MM_ClassicRound.RoleOf(player.team.Value.realTeam) : MM_Role.None;
                if (role is MM_Role.None or MM_Role.Spectator || (role == MM_Role.Murderer) != murderersWon) continue;
                PlayerProgress.Modify<MM_SaveData>(player, save => save.RecordWin(role));
            }
        }

        /// <summary>After a meeting vote: every player whose vote named an actual murderer gets credit.</summary>
        public static void RecordVotes(IReadOnlyList<GameCharacter> suspects) {
            foreach (PlayerData player in PlayerData.Players.Values) {
                int option = player != null ? player.voteOption.Value : -1;
                if (option < 0 || option >= suspects.Count || suspects[option] == null) continue;
                TeamConfig team = suspects[option].GetTeam();
                if (team != null && MM_ClassicRound.RoleOf(team.realTeam) == MM_Role.Murderer)
                    PlayerProgress.Modify<MM_SaveData>(player, save => save.correctVotes++);
            }
        }
    }
}
