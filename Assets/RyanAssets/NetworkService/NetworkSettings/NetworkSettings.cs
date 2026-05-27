using UnityEngine;

namespace RyanAssets.NetworkService {
    public static class NetworkSettings {
#if UNITY_EDITOR
        public static readonly string DEPLOY_SERVER_IP = "5.78.211.52";
#endif
        // public static readonly ushort BackendAPIPort = 8212;
        // #if (LOCAL_BACKEND && UNITY_EDITOR) || SERVER_BUILD
        //     public static readonly string YOUR_SERVER_IP = "127.0.0.1";
        // #else
        //     public static readonly string YOUR_SERVER_IP = DEPLOY_SERVER_IP;
        // #endif
        public static string BackendAPIURL { get; private set; }

        public static NetworkScriptableObject activeConfig;
        // public static NetworkScriptableObject productionConfig;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Init(){
            #if UNITY_SERVER
                activeConfig = loadResource("ServerNetworkConfig");
            #else
                NetworkScriptableObject productionConfig = loadResource("ProductionNetworkConfig");
                #if UNITY_EDITOR
                    if (!productionConfig.use_in_debug){
                        activeConfig = loadResource("LocalNetworkConfig");
                    } else {
                        activeConfig = productionConfig;
                    }
                #else
                    activeConfig = productionConfig;
                #endif
            #endif
            InitConfig();
            BackendNetwork.SetBackendURL(BackendAPIURL);
        }
        static NetworkScriptableObject loadResource(string name){
            return Resources.Load<NetworkScriptableObject>("NetworkSettings/" + name);
        }
        static void InitConfig(){
            if (activeConfig.backend_server_encrypted)
                BackendAPIURL = $"https://{activeConfig.backend_server_ip}";
            else
                BackendAPIURL = $"http://{activeConfig.backend_server_ip}";
        }
    }
}