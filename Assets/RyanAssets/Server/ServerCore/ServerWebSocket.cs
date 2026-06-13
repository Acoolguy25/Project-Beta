using System.Collections;
using UnityEngine;
using RyanAssets.NetworkService;
using Newtonsoft.Json.Linq;

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
                    res.j.TryGetValue("reason", out string message);
                    ServerPlayerEvents.KickPlayer(((string)res.j["player_id"]), message);
                    break;
                default:
                    Debug.LogError($"Unknown Server WebSocket Request: {res.j["type"].ToString()}");
                    break;
            }
        }
    }
}