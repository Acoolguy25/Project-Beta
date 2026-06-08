using System;
using UnityEngine;
using FishNet;
using FishNet.Connection;
using FishNet.Transporting;
using System.Collections.Generic;
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
        public static Dictionary<NetworkConnection, ServerPlayerStats> Players = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Init() {
            InstanceFinder.ServerManager.OnRemoteConnectionState += OnRemoteConnectionState;
            UnityTokenAuthenticator.OnAuthenticationSucceeded += OnAuthenticationSucceeded;
        }

        static async void OnRemoteConnectionState(NetworkConnection conn, RemoteConnectionStateArgs args) {
            if (args.ConnectionState != RemoteConnectionState.Stopped)
                return;

            if (!Players.TryGetValue(conn, out ServerPlayerStats stats))
                return;

            await ServerPlayerSave.Save(conn);
            Players.Remove(conn);
            if (SharedGlobalEvents.Instance != null)
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
            Players.Add(conn, stats);
            if (SharedGlobalEvents.Instance != null)
                SharedGlobalEvents.Instance.Players.Add(new(conn, stats));
            OnPlayerAddedEvent?.Invoke(conn);
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
