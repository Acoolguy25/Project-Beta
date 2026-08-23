using System.Collections.Generic;
using FishNet.Connection;
using RyanAssets.Server.ServerCore;
using RyanAssets.Shared.Global;
using RyanAssets.Shared.Declarations;
using RyanAssets.DataService;

namespace RyanAssets.Server.ServerFeatures {
    public static class ServerReward {
        public static bool AddGoldReward(NetworkConnection conn, ulong goldReward) {
            return AddReward(conn, 0, goldReward);
        }
        public static bool AddXPReward(NetworkConnection conn, ulong xpReward) {
            return AddReward(conn, xpReward, 0);
        }

        static bool AddReward(NetworkConnection conn, ulong xpReward, ulong goldReward) {
            if (!PlayerData.Players.TryGetValue(conn, out PlayerData stats))
                return false;

            return AddReward(conn, stats, xpReward, goldReward);
        }

        static bool AddReward(NetworkConnection conn, PlayerData stats, ulong xpReward, ulong goldReward) {
            ulong previousXp = stats.xp.Value;
            ulong previousGold = stats.gold.Value;

            stats.xp.Value = ulong.MaxValue - previousXp < xpReward
                ? ulong.MaxValue
                : previousXp + xpReward;
            stats.gold.Value = ulong.MaxValue - previousGold < goldReward
                ? ulong.MaxValue
                : previousGold + goldReward;

            ServerPlayerSave.MarkDirty(conn);
            return true;
        }
    }
}
