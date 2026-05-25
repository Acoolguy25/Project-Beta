using System;
using UnityEngine;

namespace RyanAssets.Server {
    public class ServerBootStrap {
        public class ServerInfo
        {
            public string universe_id { get; set; }
            public string server_id { get; set; }
            public ushort server_port {get; set; }
        };
        public static ServerInfo serverInfo;
        public string serverInfo_json;
        public static ushort MaxPlayers { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Init() {
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
            serverInfo_json =             
        }
    }
}