using System;
using UnityEngine;
using FishNet;
using FishNet.Connection;
using FishNet.Transporting;
using Shared.Player;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using RyanAssets.Authentication;

namespace RyanAssets.Server.ServerCore {
    public static class ServerPlayerEvents {
        public static Action<NetworkConnection> OnPlayerAddedEvent, OnPlayerRemovedEvent;
        public static Dictionary<NetworkConnection, ServerPlayerStats> Players;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Init(){
            InstanceFinder.ServerManager.OnRemoteConnectionState += OnRemoteConnectionState;
            UnityTokenAuthenticator.OnAuthenticationSucceeded += OnAuthenticationSucceeded;
        }
        static void OnRemoteConnectionState(NetworkConnection conn, RemoteConnectionStateArgs args){
            if (args.ConnectionState != RemoteConnectionState.Stopped)
                return;

            if (!Players.Remove(conn))
                return;
            OnPlayerRemovedEvent?.Invoke(conn);
        }
        static void OnAuthenticationSucceeded(NetworkConnection conn, JObject json){
            ServerPlayerStats stats = json.ToObject<ServerPlayerStats>();
            Players.Add(conn, stats);
            OnPlayerAddedEvent?.Invoke(conn);
        }
    }
}