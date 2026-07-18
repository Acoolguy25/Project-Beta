using Newtonsoft.Json.Linq;
using RyanAssets.NetworkService;
using System.Collections;
using UnityEngine;

namespace RyanAssets.Server.ServerCore {
    public static class ServerWebSocket {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Init() {
            BackendSocket.Instance.StartSocket("/api/internal/v1/ws", onMessage: OnMessage);
        }
        static void OnMessage((string res, JObject j) res) {
            if (res.res != null) // Some quack error!
                return;
            Debug.Log("Server Message Received: " + res.j.ToString());
            switch ((string)res.j["type"]) {
                case "shutdown":
                    ServerBootStrap.StopServer(res.j["reason"].ToString());
                    break;
                case "kick":
                    ServerPlayerEvents.KickPlayer(((string)res.j["player_id"]), message: res.j["reason"]?.ToString());
                    break;
                case "update":
                    ServerBootStrap.serverInfo = res.j["info"].ToObject<ServerBootStrap.ServerInfo>();
                    ServerBootStrap.serverSettings = res.j["settings"].ToObject<ServerBootStrap.ServerSettings>();
                    ServerBootStrap.ApplyServerInfo();
                    break;
                default:
                    Debug.LogError($"Unknown Server WebSocket Request: {res.j["type"].ToString()}");
                    break;
            }
        }
    }
}