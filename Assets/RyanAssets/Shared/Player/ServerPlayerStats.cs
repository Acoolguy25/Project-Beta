using FishNet.Connection;
using RyanAssets.DataService;

namespace RyanAssets.Shared.Player {
    [System.Serializable]
    public struct ServerPlayerStats {
        // public NetworkConnection conn;
        public string player_id;
        // public PlayerSettings settings;
        public PlayerData data;
    }
}