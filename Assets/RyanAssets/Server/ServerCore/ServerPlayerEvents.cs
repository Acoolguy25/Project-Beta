using Cysharp.Threading.Tasks;
using Cysharp.Threading.Tasks.Triggers;
using FishNet;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Transporting;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RyanAssets.Authentication;
using RyanAssets.DataService;
using RyanAssets.NetworkService;
using RyanAssets.Server.ServerModules;
using RyanAssets.Shared.Declarations;
using RyanAssets.Shared.Requests;
using RyanAssets.Shared.Global;
using System;
using Unity.VisualScripting.Antlr3.Runtime.Misc;
using UnityEngine;

namespace RyanAssets.Server.ServerCore {
    public static class ServerPlayerEvents {
        //public static event Action<NetworkConnection> OnPlayerAddedEvent, OnPlayerRemovedEvent;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Init() {
            //InstanceFinder.ServerManager.OnRemoteConnectionState += OnRemoteConnectionState;
            //UnityTokenAuthenticator.OnAuthenticationSucceeded += OnAuthenticationSucceeded;
            PlayerData.OnPlayerRemoved += RemovePlayerConnection;
            UnityTokenAuthenticator.EarlyPlayerDisconnected += RemotePlayerEarlyConnection;
        }

        //static void OnRemoteConnectionState(NetworkConnection conn, RemoteConnectionStateArgs args) {
        //    if (args.ConnectionState != RemoteConnectionState.Stopped)
        //        return;

        //    RemovePlayerConnection(conn);
        //    if (SharedGlobalEvents.Instance == null || !PlayerData.Players.TryGetValue(conn, out PlayerData stats))
        //        return;
        //}

        //static void OnAuthenticationSucceeded(NetworkConnection conn, PlayerData stats, JObject json) {
        //Debug.Log("Auth Succeeded: " + json);

        //stats.gamePlayerStats ??= new GamePlayerStats();
        //Debug.Log("PlayerAuthenticated: " + JsonConvert.SerializeObject(stats));

        //OnPlayerAddedEvent?.Invoke(conn);
        //}
        static void RemovePlayerConnection(PlayerData stats) {
            //if (PlayerData.Players.TryGetValue(conn, out PlayerData playerStats)) {
            //InstanceFinder.ServerManager.BroadcastExcept<PlayerLeaveBroadcast>(conn, new() { player = conn, stats = playerStats });
            RemotePlayerEarlyConnection(stats.Owner, stats.player_id.Value);
            //PlayerData.Players.Remove(conn);
            //}
            ServerPlayerSave.Forget(stats.Owner, stats).Forget();
            //OnPlayerRemovedEvent?.Invoke(conn);
        }
        public static void RemotePlayerEarlyConnection(NetworkConnection conn, JObject json) {
            RemotePlayerEarlyConnection(conn, json["player_id"]?.ToString());
        }
        public static void RemotePlayerEarlyConnection(NetworkConnection conn, string player_id) {
            string remove_url = $"/api/internal/v1/user/remove?player_id={player_id}";
            BackendServer.RequestAsync(() => BackendNetwork.PostRequest(remove_url), "Player Disconnect").Forget();
        }

        public static bool KickPlayer(NetworkConnection conn, string message = null) {
            if (message != null)
                InstanceFinder.ServerManager.Broadcast<PromptBroadcast>(conn, new() {
                    title = "Disconnected",
                    description = message
                }, requireAuthenticated: false);
            conn.Disconnect(message == null);
            return true;
        }
        public static bool KickPlayer(string playerId, string message = null) {
            foreach ((NetworkConnection conn, PlayerData stats) in PlayerData.Players) {
                if (stats.player_id.Value == playerId) {
                    KickPlayer(conn, message);
                    return true;
                }
            }
            return false;
        }
    }
}
