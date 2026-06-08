using System.Collections.Generic;
using FishNet.Connection;
using RyanAssets.Server.ServerCore;
using RyanAssets.Shared.Player;

namespace RyanAssets.Server.ServerFeatures {
    public static class ServerReward {
        public static bool AddXPReward(NetworkConnection conn, ulong xpReward) {
            if (SharedGlobalEvents.Instance == null || !SharedGlobalEvents.Instance.Players.TryGetValue(conn, out ServerPlayerStats stats))
                return false;

            return AddXPReward(conn, stats, xpReward);
        }

        public static bool AddXPReward(string playerId, ulong xpReward) {
            NetworkConnection matchedConn = null;
            ServerPlayerStats matchedStats = default;

            if (SharedGlobalEvents.Instance == null)
                return false;

            foreach (KeyValuePair<NetworkConnection, ServerPlayerStats> pair in SharedGlobalEvents.Instance.Players) {
                if (pair.Value.player_id != playerId)
                    continue;

                matchedConn = pair.Key;
                matchedStats = pair.Value;
                break;
            }

            return matchedConn != null && AddXPReward(matchedConn, matchedStats, xpReward);
        }

        static bool AddXPReward(NetworkConnection conn, ServerPlayerStats stats, ulong xpReward) {
            ulong previousXp = stats.data.xp;
            stats.data.xp = ulong.MaxValue - previousXp < xpReward
                ? ulong.MaxValue
                : previousXp + xpReward;

            SharedGlobalEvents.Instance.Players[conn] = stats;
            ServerPlayerSave.MarkDirty(conn);
            return true;
        }
    }
}
