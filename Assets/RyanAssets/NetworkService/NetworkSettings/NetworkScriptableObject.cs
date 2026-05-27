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
    #endif
    }
}