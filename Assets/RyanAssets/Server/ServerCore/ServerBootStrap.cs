using System;
using UnityEngine;
using Newtonsoft.Json.Linq;
using UnityEngine.SceneManagement;

using FishNet;
using FishNet.Transporting;
using RyanAssets.NetworkService;
using FishNet.Managing;
using FishNet.Managing.Scened;

namespace RyanAssets.Server.ServerCore {
    public class ServerBootStrap {
        public class ServerInfo
        {
            public string universe_id { get; set; }
            public string server_id { get; set; }
            public ushort server_port {get; set; }
            public JObject ToJObject()
            {
                return JObject.FromObject(this);
            }
        };
        public static ServerInfo serverInfo;
        public static ushort MaxPlayers { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void BeforeSceneLoad() {
            foreach (string arg in Environment.GetCommandLineArgs()) {
                string[] split = arg.Split('=', 2);

                if (split.Length != 2)
                    continue;

                switch (split[0]) {
                    case "-universe_id":
                        serverInfo.universe_id = split[1];
                        Debug.Log($"Universe ID: {serverInfo.universe_id}");
                        break;

                    case "-server_id":
                        serverInfo.server_id = split[1];
                        Debug.Log($"Server ID: {serverInfo.server_id}");
                        break;

                    case "-server_port":
                        serverInfo.server_port = ushort.Parse(split[1]);
                        Debug.Log($"Server Port: {serverInfo.server_port}");
                        break;

                    case "-max_players":
                        MaxPlayers = ushort.Parse(split[1]);
                        break;
                    
                    default:
                        Debug.LogWarning($"Unable to find {split[0]}");
                        break;
                }
            }
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AfterSceneLoad(){
            Transport transport = InstanceFinder.TransportManager.Transport;
            transport.SetMaximumClients(MaxPlayers);
            transport.SetServerBindAddress(NetworkSettings.YOUR_SERVER_IP, IPAddressType.IPv4);
            transport.SetPort(serverInfo.server_port);

            InstanceFinder.ServerManager.StartConnection();
        }
        static void StartServer()
        {
            NetworkManager nm = InstanceFinder.NetworkManager;

            nm.ServerManager.StartConnection();

            SceneLoadData sceneLoadData = new(serverInfo.universe_id + "_start")
            {
                ReplaceScenes = ReplaceOption.All
            };

            nm.SceneManager.LoadGlobalScenes(sceneLoadData);
        }
    }
}