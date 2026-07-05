using System;
using Cysharp.Threading.Tasks;
using UnityEngine;
using FishNet;
using FishNet.Connection;
using FishNet.Transporting;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using RyanAssets.Authentication;
using RyanAssets.Server.ServerModules;
using RyanAssets.NetworkService;
using RyanAssets.DataService;
using RyanAssets.Shared.Player;
using RyanAssets.Shared.Declarations;

namespace RyanAssets.Server.ServerCore {
    public static class ServerPlayerEvents {
        public static Action<NetworkConnection> OnPlayerAddedEvent, OnPlayerRemovedEvent;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Init() {
            InstanceFinder.ServerManager.OnRemoteConnectionState += OnRemoteConnectionState;
            UnityTokenAuthenticator.OnAuthenticationSucceeded += OnAuthenticationSucceeded;
        }

        static void OnRemoteConnectionState(NetworkConnection conn, RemoteConnectionStateArgs args) {
            if (args.ConnectionState != RemoteConnectionState.Stopped)
                return;

            RemovePlayerConnection(conn);
            if (SharedGlobalEvents.Instance == null || !SharedGlobalEvents.Instance.Players.TryGetValue(conn, out ServerPlayerStats stats))
                return;
            string remove_url = $"/api/internal/v1/user/remove?player_id={stats.player_id}";
            BackendServer.RequestAsync(() => BackendNetwork.PostRequest(remove_url), "Player Disconnect").Forget();
        }

        static void OnAuthenticationSucceeded(NetworkConnection conn, JObject json) {
            // Debug.Log("Auth Succeeded: " + json);
            ServerPlayerStats stats = ParsePlayerStats(json);
            stats.gamePlayerStats ??= new GamePlayerStats();
            Debug.Log("PlayerAuthenticated: " + JsonConvert.SerializeObject(stats));
            if (SharedGlobalEvents.Instance == null) {
                Debug.LogError("Cannot add authenticated player. SharedGlobalEvents.Instance is missing.");
                return;
            }

            SharedGlobalEvents.Instance.Players.Add(new(conn, stats));
            OnPlayerAddedEvent?.Invoke(conn);
        }

        static void RemovePlayerConnection(NetworkConnection conn) {
            if (SharedGlobalEvents.Instance.Players.TryGetValue(conn, out ServerPlayerStats playerStats)) {
                InstanceFinder.ServerManager.BroadcastExcept<PlayerLeaveBroadcast>(conn, new() { player = conn, stats = playerStats });
                SharedGlobalEvents.Instance.Players.Remove(conn);
            }
            ServerPlayerSave.Forget(conn);
            OnPlayerRemovedEvent?.Invoke(conn);
        }

        static ServerPlayerStats ParsePlayerStats(JObject json) {
            JObject normalizedJson = (JObject)json.DeepClone();
            JToken settings = normalizedJson["settings"];

            if (settings == null || settings.Type == JTokenType.Null || settings.Type == JTokenType.Undefined) {
                normalizedJson["settings"] = JObject.FromObject(default(PlayerSettings));
            } else if (settings.Type == JTokenType.String) {
                JObject parsedSettings = BackendNetwork.ParseJSON((string)settings);
                normalizedJson["settings"] = parsedSettings ?? JObject.FromObject(default(PlayerSettings));
            }

            return normalizedJson.ToObject<ServerPlayerStats>();
        }

        public static void KickPlayer(NetworkConnection conn, string message = null) {
            if (message != null)
                InstanceFinder.ServerManager.Broadcast<PromptBroadcast>(conn, new() {
                    title = "Disconnected",
                    description = message
                }, requireAuthenticated: false);
            conn.Disconnect(message == null);
        }
        public static void KickPlayer(string playerId, string message = null) {
            foreach ((NetworkConnection conn, ServerPlayerStats stats) in SharedGlobalEvents.Instance.Players) {
                if (stats.player_id == playerId) {
                    KickPlayer(conn, message);
                    return;
                }
            }

        }
    }
}
