using UnityEngine;
using System;
using System.Threading.Tasks;
using RyanAssets.NetworkService;
using FishNet;
using System.Threading;
// using FishNet.Connections;

namespace RyanAssets.Server.ServerCore {
    public static class ServerHeartbeat {
        static CancellationTokenSource heartbeat_cts = new();
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Init() {
            Application.quitting += OnQuitting;
            _ = HeartbeatLoop(heartbeat_cts.Token);
        }
        static async Task HeartbeatLoop(CancellationToken token) {
            while (true) {
                _ = BackendNetwork.PostRequest("/api/internal/v1/heartbeat");
                await Task.Delay(TimeSpan.FromSeconds(60), token);
            }
        }
        static void OnQuitting() {
            heartbeat_cts.Cancel();
            Debug.Log("Server shutting down");
            _ = SendShutdown();
        }
        static async Task SendShutdown(){
            try
            {
                Task request = BackendNetwork.PostRequest("/api/internal/v1/shutdown");
                Task timeout = Task.Delay(TimeSpan.FromSeconds(2));

                await Task.WhenAny(request, timeout);
                if (timeout.IsCompleted)
                    Debug.LogWarning("Shutdown request timed out!");
                else if (!request.IsCompletedSuccessfully)
                    Debug.LogWarning($"Shutdown request failed: {request.Exception}");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Shutdown request failed: {e.Message}");
            }
        }
    }
}