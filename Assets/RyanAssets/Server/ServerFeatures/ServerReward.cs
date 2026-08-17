using System.Collections.Generic;
using FishNet.Connection;
using RyanAssets.Server.ServerCore;
using RyanAssets.Shared.Global;
using RyanAssets.Shared.Declarations;
using RyanAssets.DataService;

namespace RyanAssets.Server.ServerFeatures {
    public static class ServerReward {
        public static bool AddXPReward(NetworkConnection conn, ulong xpReward) {
            if (SharedGlobalEvents.Instance == null || !PlayerData.Players.TryGetValue(conn, out PlayerData stats))
                return false;

            return AddXPReward(conn, stats, xpReward);
        }

        public static bool AddXPReward(string playerId, ulong xpReward) {
            NetworkConnection matchedConn = null;
            PlayerData matchedStats = default;

            if (SharedGlobalEvents.Instance == null)
                return false;

            foreach (KeyValuePair<NetworkConnection, PlayerData> pair in PlayerData.Players) {
                if (pair.Value.player_id.Value != playerId)
                    continue;

                matchedConn = pair.Key;
                matchedStats = pair.Value;
                break;
            }

            return matchedConn != null && AddXPReward(matchedConn, matchedStats, xpReward);
        }

        static bool AddXPReward(NetworkConnection conn, PlayerData stats, ulong xpReward) {
            ulong previousXp = stats.xp.Value;
            stats.xp.Value = ulong.MaxValue - previousXp < xpReward
                ? ulong.MaxValue
                : previousXp + xpReward;

            ServerPlayerSave.MarkDirty(conn);
            return true;
        }
    }
}
