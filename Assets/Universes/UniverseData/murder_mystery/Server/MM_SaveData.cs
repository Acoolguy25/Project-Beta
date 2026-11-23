using System;
using Newtonsoft.Json.Linq;
using RyanAssets.DataService;
using RyanAssets.Progress;
using RyanAssets.Server.ServerCore;
using Universes.UniverseData.murder_mystery;

namespace Universes.murder_mystery.Server {
    /// <summary>What murder mystery remembers about a player between sessions. Written only by the server.</summary>
    public sealed class MM_SaveData : ProgressData {
        public const string Universe = "murder_mystery";
        // 2: MM2-style rounds. Coins, knife-throw kills, survivals and role luck were added; the meeting
        // vote counter went with the meetings.
        public override int LatestVersion => 2;

        /// <summary>Rounds of any mode the player was in when they started.</summary>
        public int roundsPlayed;
        /// <summary>Classic rounds won, by the role the player had (a hero counts as a sheriff).</summary>
        public int innocentWins, sheriffWins, murdererWins;
        /// <summary>Classic rounds the player was still alive at the end of.</summary>
        public int survivals;
        /// <summary>Classic kills made as a murderer, and how many of them were thrown knives.</summary>
        public int murders, throwKills;
        /// <summary>Classic murderers shot as the sheriff or hero.</summary>
        public int murderersShot;
        /// <summary>Times a dead sheriff's gun was picked up, making this player the hero.</summary>
        public int gunsPickedUp;
        /// <summary>Coins collected in murder mystery, ever. They unlock knife skins (<see cref="MM_KnifeSkins"/>).</summary>
        public int coinsCollected;
        /// <summary>Rounds in a row without being dealt murderer / sheriff; each one raises the odds (<see cref="MM_RoleOdds"/>).</summary>
        public int murdererLuck, sheriffLuck;

        public override void Migrate(int fromVersion, JObject stored) {
            // Version 1 fields carry over by name; the new counters start at zero.
        }

        public void RecordWin(MM_Role role) {
            switch (role) {
                case MM_Role.Innocent: innocentWins++; break;
                case MM_Role.Sheriff: sheriffWins++; break;
                case MM_Role.Murderer: murdererWins++; break;
            }
        }

        /// <summary>Was dealt <paramref name="role"/> this round: the luck for that role resets, the other grows.</summary>
        public void RecordDealt(MM_Role role) {
            murdererLuck = role == MM_Role.Murderer ? 0 : murdererLuck + 1;
            sheriffLuck = role == MM_Role.Sheriff ? 0 : sheriffLuck + 1;
        }

        public override void Validate() {
            roundsPlayed = Math.Max(0, roundsPlayed);
            innocentWins = Math.Max(0, innocentWins);
            sheriffWins = Math.Max(0, sheriffWins);
            murdererWins = Math.Max(0, murdererWins);
            survivals = Math.Max(0, survivals);
            murders = Math.Max(0, murders);
            throwKills = Math.Clamp(throwKills, 0, murders);
            murderersShot = Math.Max(0, murderersShot);
            gunsPickedUp = Math.Max(0, gunsPickedUp);
            coinsCollected = Math.Max(0, coinsCollected);
            murdererLuck = Math.Max(0, murdererLuck);
            sheriffLuck = Math.Max(0, sheriffLuck);
        }

        // ---------------------------------------------------------------- reading (server)

        /// <summary>The player's saved progress, or defaults while it loads or if it failed to.</summary>
        public static MM_SaveData Of(PlayerData player) =>
            player != null && PlayerProgress.TryGet(player, out MM_SaveData data) ? data : Defaults;

        static readonly MM_SaveData Defaults = new();
    }
}
