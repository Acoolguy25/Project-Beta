using FishNet.Connection;
using FishNet.Object;
using FishNet.Serializing;
using FishNet.Object.Synchronizing;
using Newtonsoft.Json.Linq;
using RyanAssets.Core;
using RyanAssets.DataService;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace RyanAssets.DataService {
    public enum TeamColor : short {
        None,
        Ghost, // Spectator
        Lobby, // Lobby / Spectator
        Blue, // Sheriff
        Red,  // Murderer
        Green // Innocent
    };
    [System.Serializable]
    public class TeamConfig {
        [SerializeField]
        public TeamColor team = TeamColor.Ghost;
        [SerializeField]
        public TeamColor displayTeam = TeamColor.Ghost;
        public TeamConfig() {

        }
        public TeamConfig(TeamColor team) {
            this.team = team;
            this.displayTeam = team;
        }
        public TeamConfig(TeamColor team, TeamColor displayTeam) {
            this.team = team;
            this.displayTeam = displayTeam;
        }
        public static Color32 TeamToColor(TeamColor teamColor) {
            return teamColor switch {
                TeamColor.Ghost or TeamColor.Lobby => Color.grey,
                TeamColor.Blue => Color.blue,
                TeamColor.Red => Color.red,
                TeamColor.Green => Color.green,
                TeamColor.None => Color.white,
                _ => Color.white
            };
        }
        public Color32 realTeamColor => TeamToColor(team);
        public Color32 displayTeamColor => TeamToColor(displayTeam);
    };
    public enum ToolEnum : short {
        Unknown = 0,
        Dagger,
        Pistol
    }
    public class PlayerData : NetworkBehaviour {
        // Player Settings
        readonly public SyncVar<string> player_id = new();
        readonly public SyncVar<string> username = new();

        // Savable Data
        readonly public SyncVar<ulong>  xp = new();
        readonly public SyncVar<ulong>  gold = new();

        // Game Player Stats
        public DateTime JoinDateTime { get; private set; } = default; // Synchronized Via SpawnData
        readonly public SyncVar<int> lives = new(initialValue: -1);
        readonly public SyncVar<int> deaths = new(initialValue: 0);
        readonly public SyncVar<TeamConfig> team = new(initialValue: new());
        readonly public SyncList<int> leaderboard = new();
        // Character Data

        readonly public SyncVar<float> walkSpeed = new(initialValue: 10f);
        readonly public SyncVar<float> sprintSpeed = new(initialValue: 23f);

        readonly public SyncVar<float> staminaMax = new(initialValue: 250f);
        readonly public SyncVar<float> staminaRegen = new(initialValue: 30f);
        readonly public SyncVar<float> staminaCooldown = new(initialValue: 0.6f);

        // Events
        public static Action<NetworkConnection, PlayerData> OnPlayerAdded;
        public static Action<NetworkConnection, PlayerData> OnPlayerRemoved;

        // Static

        public static Dictionary<NetworkConnection, PlayerData> Players;

        // Server
#if UNITY_SERVER
        [NonSerialized]
        public List<ToolEnum> tools = new();
#else   // Client
        public static PlayerData localData;
        public static InstantEvent<PlayerData> OnMyPlayerAdded;
        public static event Action<PlayerData> OnMyPlayerRemoved;
#endif
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Init() {
            Players = new();
#if !UNITY_SERVER
            localData = null;
            OnMyPlayerAdded = new();
            OnMyPlayerRemoved = null;
#endif
            OnPlayerAdded = 
            OnPlayerRemoved = null;
        }
#if UNITY_SERVER
        public override void OnOwnershipServer(NetworkConnection prevOwner) {
            OnPlayerAdded?.Invoke(Owner, this);
        }
#else
        public override void OnOwnershipClient(NetworkConnection prevOwner) {
            if (IsOwner) {
                localData = this;
                OnMyPlayerAdded?.Invoke(this);
            }
            OnPlayerAdded?.Invoke(Owner, this);
        }
#endif
        public override void OnStartNetwork() {
            Players.Add(Owner, this);
            gameObject.name = $"PlayerData ({username.Value})";
        }
        public override void OnStopNetwork() {
            Players.Remove(Owner);
            OnPlayerRemoved?.Invoke(Owner, this);
#if !UNITY_SERVER
            if (IsOwner) {
                OnMyPlayerRemoved?.Invoke(this);
                OnMyPlayerAdded.ClearLastValue();
                localData = null;
            }
#endif
        }

        public string GetPlayerName() {
            return username.Value;
        }
        public void SetPlayerTeam(TeamConfig teamConfig) {
            if (teamConfig == team.Value)
                return;
            team.Value = teamConfig;
        }
        // Helpful Static Methods
        public static List<string> GetPlayerNames(Func<NetworkConnection, PlayerData, bool> selector = null) {
            List<string> strings = new();
            foreach (var item in Players) {
                if (selector != null && !selector(item.Key, item.Value))
                    continue;
                strings.Add(item.Value.username.Value);
            }
            return strings;
        }
        public static string GetPlayerName(NetworkConnection connection) {
            if (Players.TryGetValue(connection, out PlayerData serverPlayerStats)) {
                return serverPlayerStats.GetPlayerName();
            }
            return null;
        }
        public static string GetPlayerUsername(NetworkConnection conn) {
            if (Players.TryGetValue(conn, out PlayerData stats))
                return stats.username.Value;

            return "A player";
        }
        public static PlayerData GetPlayerData(NetworkConnection conn) {
            return Players.TryGetValue(conn, out PlayerData stats) ? stats : null;
        }
        public static bool TryGetPlayerData(NetworkConnection conn, out PlayerData stats) {
            return Players.TryGetValue(conn, out stats);
        }
        public static void RunEach(Action<NetworkConnection, PlayerData> action) {
            foreach (var item in Players) {
                action.Invoke(item.Key, item.Value);
            }
        }
#if UNITY_SERVER
        // Constructors
        public void Deserialize(JObject json) {
            if (JoinDateTime == default)
                JoinDateTime = DateTime.UtcNow;
            player_id.Value = (string)json["player_id"];

            JObject data = (JObject)json["data"];
            if (data == null)
                return;

            player_id.Value = (string)data["player_id"];
            username.Value = (string)data["username"];
            xp.Value = (ulong)data["xp"];
            gold.Value = (ulong)data["gold"];
        }

        public JObject Serialize() {
            return new JObject {
                ["username"] = username.Value,
                ["xp"] = xp.Value,
                ["gold"] = gold.Value
            };
        }

        public override void WritePayload(NetworkConnection connection, Writer writer){
            writer.WriteDateTime(JoinDateTime);
        }
#else
        public override void ReadPayload(NetworkConnection connection, Reader reader) {
            JoinDateTime = reader.ReadDateTime();
        }
#endif
    }
}

// Edtior Visualization
#if UNITY_EDITOR
[CustomEditor(typeof(PlayerData))]
public class PlayerDataEditor : Editor {
    public override void OnInspectorGUI() {
        DrawDefaultInspector();

        var data = (PlayerData)target;

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("SyncVars", EditorStyles.boldLabel);

        EditorGUILayout.TextField("Player ID", data.player_id.Value);
        EditorGUILayout.TextField("Username", data.username.Value);

        EditorGUILayout.LongField("XP", (long)data.xp.Value);
        EditorGUILayout.LongField("Gold", (long)data.gold.Value);

        EditorGUILayout.IntField("Lives", data.lives.Value);
        EditorGUILayout.IntField("Deaths", data.deaths.Value);

        EditorGUILayout.LabelField("Team", data.team.Value?.team.ToString() ?? "None");
        EditorGUILayout.LabelField("DisplayTeam", data.team.Value?.displayTeam.ToString() ?? "None");

        EditorGUILayout.FloatField("Walk Speed", data.walkSpeed.Value);
        EditorGUILayout.FloatField("Sprint Speed", data.sprintSpeed.Value);

        if (Application.isPlaying)
            Repaint();
    }
}
#endif