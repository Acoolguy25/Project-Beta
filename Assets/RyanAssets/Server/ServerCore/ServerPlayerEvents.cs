using System;
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

namespace RyanAssets.Server.ServerCore {
    public static class ServerPlayerEvents {
        public static Action<NetworkConnection> OnPlayerAddedEvent, OnPlayerRemovedEvent;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Init() {
            InstanceFinder.ServerManager.OnRemoteConnectionState += OnRemoteConnectionState;
            UnityTokenAuthenticator.OnAuthenticationSucceeded += OnAuthenticationSucceeded;
        }

        static async void OnRemoteConnectionState(NetworkConnection conn, RemoteConnectionStateArgs args) {
            if (args.ConnectionState != RemoteConnectionState.Stopped)
                return;

            if (SharedGlobalEvents.Instance == null || !SharedGlobalEvents.Instance.Players.TryGetValue(conn, out ServerPlayerStats stats))
                return;

            await ServerPlayerSave.Save(conn);
            SharedGlobalEvents.Instance.Players.Remove(conn);
            ServerPlayerSave.Forget(conn);
            OnPlayerRemovedEvent?.Invoke(conn);
            string remove_url = $"/api/internal/v1/user/remove?player_id={stats.player_id}";
            _ = BackendServer.RequestAsync(() => BackendNetwork.PostRequest(remove_url), "Player Disconnect");
        }

        static void OnAuthenticationSucceeded(NetworkConnection conn, JObject json) {
            // Debug.Log("Auth Succeeded: " + json);
            ServerPlayerStats stats = ParsePlayerStats(json);
            Debug.Log("PlayerAuthenticated: " + JsonConvert.SerializeObject(stats));
            if (SharedGlobalEvents.Instance == null) {
                Debug.LogError("Cannot add authenticated player. SharedGlobalEvents.Instance is missing.");
                return;
            }

            RemoveDuplicatePlayerConnection(conn, stats.player_id);
            SharedGlobalEvents.Instance.Players.Add(new(conn, stats));
            OnPlayerAddedEvent?.Invoke(conn);
        }

        static void RemoveDuplicatePlayerConnection(NetworkConnection newConn, string playerId) {
            if (string.IsNullOrWhiteSpace(playerId))
                return;

            NetworkConnection duplicateConn = null;
            foreach (var pair in SharedGlobalEvents.Instance.Players) {
                if (pair.Key == newConn || pair.Value.player_id != playerId)
                    continue;

                duplicateConn = pair.Key;
                break;
            }

            if (duplicateConn == null)
                return;

            Debug.LogWarning($"Replacing duplicate player connection: PlayerId={playerId}, OldClientId={duplicateConn.ClientId}, NewClientId={newConn.ClientId}");
            _ = ServerPlayerSave.Save(duplicateConn);
            SharedGlobalEvents.Instance.Players.Remove(duplicateConn);
            ServerPlayerSave.Forget(duplicateConn);
            OnPlayerRemovedEvent?.Invoke(duplicateConn);
            duplicateConn.Disconnect(true);
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
    }
}
