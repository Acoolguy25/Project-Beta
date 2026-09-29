using UnityEngine;

namespace RyanAssets.NetworkService {
    public static class NetworkSettings {
#if UNITY_EDITOR
        public static readonly string DEPLOY_SERVER_IP = "ryangames.duckdns.org";
#endif
        // public static readonly ushort BackendAPIPort = 8212;
        // #if (LOCAL_BACKEND && UNITY_EDITOR) || SERVER_BUILD
        //     public static readonly string YOUR_SERVER_IP = "127.0.0.1";
        // #else
        //     public static readonly string YOUR_SERVER_IP = DEPLOY_SERVER_IP;
        // #endif
        public static string BackendAPIURL { get; private set; }

        public static NetworkScriptableObject activeConfig;
        public static bool noNetworkLogin;
        public static bool EditorDirectConnection { get; private set; }
        public static string EditorUniverseId { get; private set; }
        public static ushort EditorGamePort { get; private set; }
        public static string EditorPlayerId { get; private set; }

        // Local profiles are session data; they never enter the backend's player database.
        public static Newtonsoft.Json.Linq.JObject CreateEditorPlayerProfile(string playerId) => new() {
            ["player_id"] = playerId,
            ["username"] = "Editor " + playerId.Substring("editor-".Length, 8),
            ["xp"] = 0UL,
            ["gold"] = 0UL
        };
        // public static NetworkScriptableObject productionConfig;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Init() {
            EditorDirectConnection = false;
            EditorUniverseId = null;
            EditorGamePort = 0;
            EditorPlayerId = null;
            noNetworkLogin = false;
#if UNITY_EDITOR
            var editorConfig = loadResource("LocalNetworkConfig");
            EditorDirectConnection = editorConfig != null && editorConfig.editor_direct_connection;
            if (EditorDirectConnection) {
                EditorUniverseId = editorConfig.editor_universe_id;
                EditorGamePort = (ushort)Mathf.Clamp(editorConfig.editor_game_port, 1, ushort.MaxValue);
                EditorPlayerId = "editor-" + Hash128.Compute(Application.dataPath).ToString();
                Debug.Log($"Local Editor connection: {EditorUniverseId} at 127.0.0.1:{EditorGamePort}; backend disabled.");
            }
#endif
#if UNITY_SERVER
                activeConfig = loadResource("ServerNetworkConfig");
#else
            NetworkScriptableObject productionConfig = loadResource("ProductionNetworkConfig");
#if UNITY_EDITOR
            if (!productionConfig.use_in_debug) {
                activeConfig = loadResource("LocalNetworkConfig");
            } else {
                activeConfig = productionConfig;
            }
            noNetworkLogin = activeConfig.no_login;
#else
                    activeConfig = productionConfig;
                    noNetworkLogin = false;
#endif
#endif
            InitConfig();
            noNetworkLogin |= EditorDirectConnection;
            BackendNetwork.SetBackendURL(BackendAPIURL);
            BackendSocket.SetBaseAddress(BackendAPIURL);
        }
        static NetworkScriptableObject loadResource(string name) {
            return Resources.Load<NetworkScriptableObject>("NetworkSettings/" + name);
        }
        static void InitConfig() {
            if (activeConfig.backend_server_encrypted)
                BackendAPIURL = $"https://{activeConfig.backend_server_ip}";
            else
                BackendAPIURL = $"http://{activeConfig.backend_server_ip}";
        }
    }
}
