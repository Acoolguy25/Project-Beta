using RyanAssets.DataService;

namespace Shared.Player {
    [System.Serializable]
    public struct ServerPlayerStats {
        public string player_id;
        public PlayerSettings settings;
        public PlayerData data;
    }
}