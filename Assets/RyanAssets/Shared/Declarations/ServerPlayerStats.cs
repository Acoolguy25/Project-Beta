using FishNet.Connection;
using RyanAssets.DataService;

namespace RyanAssets.Shared.Declarations {
    [System.Serializable]
    public enum TeamColor: short {
        Lobby,
        Blue, // Sheriff
        Red,  // 
        Green
    };
    [System.Serializable]
    public class GamePlayerStats {
        public int lives = -1;
        public TeamColor team = TeamColor.Lobby;
    }
    [System.Serializable]
    public struct ServerPlayerStats {
        public string player_id;
        public PlayerData data;
        public GamePlayerStats gamePlayerStats;
    }
}