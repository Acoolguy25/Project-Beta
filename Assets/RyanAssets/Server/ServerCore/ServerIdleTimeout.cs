using UnityEngine;
using System;
using Cysharp.Threading.Tasks;
using RyanAssets.NetworkService;
using FishNet;
using FishNet.Connection;
using FishNet.Managing.Server;
using FishNet.Transporting;
using System.Threading;
// using FishNet.Connections;

namespace RyanAssets.Server.ServerCore {
    public static class ServerIdleTimeout {
        public static bool IdleTimeoutEnabled { private set; get; }
        public static event Action OnIdleTimeoutStarted, OnIdleTimeoutStopped;
        static CancellationTokenSource idleTimeoutCts;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Init() {
            ServerBootStrap.StartServerEvent += OnStartServer;
            ServerBootStrap.StopServerEvent += OnStopServer;
        }
        static async UniTask IdleTimeoutLoop(CancellationToken token) {
            Debug.LogWarning("No Players Online; Server Will Stop Soon Due To Idle Timeout!");
            try {
                await UniTask.Delay(TimeSpan.FromSeconds(ServerBootStrap.serverSettings.ServerIdleTimeoutSeconds), cancellationToken: token);
                ServerBootStrap.StopServer("Idle Timeout");
            } catch (OperationCanceledException) { }
        }
        static void OnStartServer() {
            InstanceFinder.ServerManager.OnRemoteConnectionState += OnRemoteConnectionState;
            if (InstanceFinder.ServerManager.Clients.Count == 0)
                StartIdleTimeout();
        }
        static void OnStopServer() {
            StopIdleTimeout();
        }
        static void StartIdleTimeout() {
            StopIdleTimeout();
            IdleTimeoutEnabled = true;
            idleTimeoutCts = new();
            IdleTimeoutLoop(idleTimeoutCts.Token).Forget();
            OnIdleTimeoutStarted?.Invoke();
        }
        static void StopIdleTimeout() {
            if (idleTimeoutCts == null)
                return;

            idleTimeoutCts.Cancel();
            idleTimeoutCts.Dispose();
            idleTimeoutCts = null;
            IdleTimeoutEnabled = false;
            OnIdleTimeoutStopped?.Invoke();
        }
        private static void OnRemoteConnectionState(NetworkConnection conn, RemoteConnectionStateArgs args) {
            if (!InstanceFinder.IsServerStarted){
                Debug.LogWarning($"Ignoring because server never started!");
                return;
            }
            if (args.ConnectionState == RemoteConnectionState.Started) {
                Debug.Log($"Player joined: ClientId={conn.ClientId} ({InstanceFinder.ServerManager.Clients.Count} total)");
                foreach (NetworkConnection clientConn in InstanceFinder.ServerManager.Clients.Values){
                    Debug.Log(
                        $"ClientId={clientConn.ClientId} " +
                        $"Connected={clientConn.IsActive} " +
                        $"Objects={clientConn.Objects.Count}"
                    );
                }
                StopIdleTimeout();
            } else if (args.ConnectionState == RemoteConnectionState.Stopped) {
                int clientsLeft = InstanceFinder.ServerManager.Clients.ContainsKey(conn.ClientId)
                    ? InstanceFinder.ServerManager.Clients.Count - 1
                    : InstanceFinder.ServerManager.Clients.Count;
                Debug.Log($"Player left: ClientId={conn.ClientId} ({clientsLeft} left)");
                if (clientsLeft == 0)
                    StartIdleTimeout();
            }
        }
    }
}
