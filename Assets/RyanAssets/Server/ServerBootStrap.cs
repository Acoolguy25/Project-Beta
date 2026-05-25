using System;
using UnityEngine;

namespace RyanAssets.Server {
    public class ServerBootStrap {
        public static string UniverseId { get; private set; }
        public static string ServerId { get; private set; }
        public static ushort ServerPort { get; private set; }
        public static ushort MaxPlayers { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Init() {
            foreach (string arg in Environment.GetCommandLineArgs()) {
                string[] split = arg.Split('=', 2);

                if (split.Length != 2)
                    continue;

                switch (split[0]) {
                    case "-universe_id":
                        UniverseId = split[1];
                        break;

                    case "-server_id":
                        ServerId = split[1];
                        break;

                    case "-server_port":
                        ServerPort = ushort.Parse(split[1]);
                        break;

                    case "-max_players":
                        MaxPlayers = ushort.Parse(split[1]);
                        break;
                    
                    default:
                        Debug.LogWarning($"Unable to find {split[0]}");
                        break;
                }
            }

            Debug.Log($"Universe ID: {UniverseId}");
            Debug.Log($"Server ID: {ServerId}");
            Debug.Log($"Server Port: {ServerPort}");
            
        }
    }
}