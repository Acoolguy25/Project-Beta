#if UNITY_SERVER
using FishNet;
using FishNet.Connection;
using RyanAssets.Server.ServerCore;

namespace Universes.UniverseData.dot_invaders {
    // Saved player progress: matches played and won.
    public sealed partial class DI_ServerRunner {
        /// <summary>Counts the finished match for every player still on a team; a player team in front wins it.</summary>
        void RecordMatchProgress(int winnerTeamId) {
            bool playerTeamWon = winnerTeamId >= 0 && winnerTeamId < NpcTeamIdStart;
            foreach (NetworkConnection connection in InstanceFinder.ServerManager.Clients.Values) {
                if (!connection.IsValid || !playerTeams.TryGetValue(connection.ClientId, out int team)) continue;
                bool won = playerTeamWon && team == winnerTeamId;
                PlayerProgress.Modify<DI_SaveData>(connection, save => save.RecordMatch(won));
            }
        }
    }
}
#endif
