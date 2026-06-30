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
        public int deaths = 0;
        public TeamColor team = TeamColor.Lobby;
        public float walkSpeed = 10f;
        public float sprintSpeed = 30f;
    }
    [System.Serializable]
    public struct ServerPlayerStats {
        public string player_id;
        public PlayerData data;
        public GamePlayerStats gamePlayerStats;
    }
}