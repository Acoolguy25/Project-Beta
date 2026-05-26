using UnityEngine;
using System;
using System.Threading.Tasks;
using RyanAssets.NetworkService;
using FishNet;
using FishNet.Connection;
using FishNet.Managing.Server;
using FishNet.Transporting;
using System.Threading;
// using FishNet.Connections;

namespace RyanAssets.Server.ServerCore {
    public static class ServerTimeout {
        readonly static CancellationTokenSource idleTimeoutCts = new();
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Init(){
            ServerBootStrap.StartServerEvent += OnStartServer;
            // ServerBootStrap.StopServerEvent += OnStopServer;
        }
        static async Task IdleTimeoutLoop(CancellationToken token) {
            Debug.LogWarning("No Players Online; Server Will Stop Soon Due To Idle Timeout!");
            await Task.Delay(TimeSpan.FromSeconds(ServerBootStrap.ServerIdleTimeoutSeconds), token);
            Debug.LogWarning("Stopping Server Due To Idle Timeout!");
            ServerBootStrap.StopServer();
        }
        public static void OnStartServer() {
            InstanceFinder.ServerManager.OnRemoteConnectionState += OnRemoteConnectionState;
            if (InstanceFinder.ServerManager.Clients.Count == 0)
                _ = IdleTimeoutLoop(idleTimeoutCts.Token);
        }
        // public static void OnStopServer() {

        // }
        private static void OnRemoteConnectionState(NetworkConnection conn, RemoteConnectionStateArgs args) {
            if (args.ConnectionState == RemoteConnectionState.Started) {
                Debug.Log($"Player joined: ClientId={conn.ClientId}");
                idleTimeoutCts.Cancel();
            } else if (args.ConnectionState == RemoteConnectionState.Stopped && InstanceFinder.ServerManager.Clients.Count == 0) {
                Debug.Log($"Player left: ClientId={conn.ClientId}");
                _ = IdleTimeoutLoop(idleTimeoutCts.Token);
            }
        }
    }
}