#if UNITY_SERVER
using FishNet;
using FishNet.Connection;
using RyanAssets.DataService;
using RyanAssets.Server.ServerCore;

namespace Universes.UniverseData.dot_invaders {
    // Saved player progress: matches played and won, bases captured, most bases held.
    public sealed partial class DI_ServerRunner {
        /// <summary>One player's tally for the running match, written to their save when it ends or they leave.</summary>
        struct MatchRecord {
            public int captures;
            public int mostBasesHeld;
        }

        /// <summary>Counts a capture for the player who sent the troop; NPC teams have no save.</summary>
        void RecordCapture(int ownerClientId, int teamId) {
            if (ownerClientId < 0)
                return;
            int held = 0;
            foreach (BaseState state in bases)
                if (state.teamId == teamId)
                    held++;
            matchRecords.TryGetValue(ownerClientId, out MatchRecord record);
            record.captures++;
            if (held > record.mostBasesHeld)
                record.mostBasesHeld = held;
            matchRecords[ownerClientId] = record;
        }

        /// <summary>Counts the finished match for every player still on a team; a player team in front wins it.</summary>
        void RecordMatchProgress(int winnerTeamId) {
            bool playerTeamWon = winnerTeamId >= 0 && winnerTeamId < NpcTeamIdStart;
            foreach (NetworkConnection connection in InstanceFinder.ServerManager.Clients.Values) {
                if (!connection.IsValid || !playerTeams.TryGetValue(connection.ClientId, out int team)) continue;
                bool won = playerTeamWon && team == winnerTeamId;
                MatchRecord record = TakeMatchRecord(connection.ClientId);
                PlayerProgress.Modify<DI_SaveData>(connection, save => {
                    save.RecordMatch(won);
                    save.RecordCaptures(record.captures, record.mostBasesHeld);
                });
            }
        }

        /// <summary>A player leaving mid-match keeps the bases they took; the match itself is not counted.</summary>
        void RecordLeavingPlayerProgress(PlayerData player) {
            MatchRecord record = TakeMatchRecord(player.Owner.ClientId);
            if (record.captures > 0)
                PlayerProgress.Modify<DI_SaveData>(player, save => save.RecordCaptures(record.captures, record.mostBasesHeld));
        }

        MatchRecord TakeMatchRecord(int clientId) {
            matchRecords.Remove(clientId, out MatchRecord record);
            return record;
        }
    }
}
#endif
