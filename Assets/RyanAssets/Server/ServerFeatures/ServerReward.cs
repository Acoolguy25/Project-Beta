using System.Collections.Generic;
using FishNet.Connection;
using RyanAssets.Server.ServerCore;
using RyanAssets.Shared.Player;

namespace RyanAssets.Server.ServerFeatures {
    public static class ServerReward {
        public static bool AddXPReward(NetworkConnection conn, ulong xpReward) {
            if (!ServerPlayerEvents.Players.TryGetValue(conn, out ServerPlayerStats stats))
                return false;

            return AddXPReward(conn, stats, xpReward);
        }

        public static bool AddXPReward(string playerId, ulong xpReward) {
            NetworkConnection matchedConn = null;
            ServerPlayerStats matchedStats = default;

            foreach (KeyValuePair<NetworkConnection, ServerPlayerStats> pair in ServerPlayerEvents.Players) {
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

            ServerPlayerEvents.Players[conn] = stats;
            if (SharedGlobalEvents.Instance != null)
                SharedGlobalEvents.Instance.Players[conn] = stats;
            ServerPlayerSave.MarkDirty(conn);
            return true;
        }
    }
}
