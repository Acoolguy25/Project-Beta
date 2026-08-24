#if UNITY_SERVER
using FishNet.Connection;
using RyanAssets.DataService;
using RyanAssets.Server.ServerCore;

namespace RyanAssets.Server.ServerFeatures {
    public static class ServerReward {
        public static bool AddGoldReward(NetworkConnection connection, ulong goldReward) {
            return AddReward(connection, 0, goldReward);
        }

        public static bool AddXPReward(NetworkConnection connection, ulong xpReward) {
            return AddReward(connection, xpReward, 0);
        }

        public static bool AddReward(NetworkConnection connection, ulong xpReward, ulong goldReward) {
            if (connection == null || !connection.IsValid ||
                !PlayerData.TryGetPlayerData(connection, out PlayerData playerData) || playerData == null)
                return false;

            playerData.xp.Value = SaturatingAdd(playerData.xp.Value, xpReward);
            playerData.gold.Value = SaturatingAdd(playerData.gold.Value, goldReward);
            ServerPlayerSave.MarkDirty(connection);
            return true;
        }

        static ulong SaturatingAdd(ulong current, ulong reward) {
            return ulong.MaxValue - current < reward ? ulong.MaxValue : current + reward;
        }
    }
}
#endif
