using UnityEngine;

namespace RyanAssets.NetworkService {
    [CreateAssetMenu(menuName = "Config/Network Scriptable Object")]
    public sealed class NetworkScriptableObject : ScriptableObject
    {
        [Header("Connection")]
        [SerializeField] public string backend_server_ip = "127.0.0.1";
        [SerializeField] public bool backend_server_encrypted = false;
    #if UNITY_EDITOR
        [SerializeField] public bool use_in_debug = false;
        [SerializeField] public bool no_login = false;
        [Header("Local Editor Testing (LocalNetworkConfig only)")]
        [Tooltip("Connect a client Editor directly to a server Editor on this machine. Bypasses backend login, matchmaking and persistence; ignored in builds.")]
        public bool editor_direct_connection;
        public string editor_universe_id = "war_valley";
        [Range(1, 65535)] public int editor_game_port = 20000;
#endif
    }
}
