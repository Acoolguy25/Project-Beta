using UnityEngine;
using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json.Linq;
using RyanAssets.Core;
using RyanAssets.NetworkService;

namespace RyanAssets.Server.ServerCore {
    public static class ServerHeartbeat {
        static CancellationTokenSource heartbeatCts;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Init() {
            Debug.Log("ServerHeartbeat Init");

            heartbeatCts = new CancellationTokenSource();
            ServerBootStrap.StopServerEvent += OnStopServer;

            HeartbeatLoop(heartbeatCts.Token).Forget();
        }

        static async UniTask HeartbeatLoop(CancellationToken token) {
            Debug.Log($"Heartbeat loop started: {NetworkSettings.BackendAPIURL}");

            while (!token.IsCancellationRequested) {
                try {
                    (string res, JObject _) =
                        await BackendNetwork.PostRequest("/api/internal/v1/heartbeat");

                    if (res != null)
                        Debug.LogWarning($"Heartbeat failed: {res}");
                    if (token.IsCancellationRequested)
                        break;
                    // else
                        // Debug.Log("Heartbeat sent");
                }
                catch (Exception e) {
                    Debug.LogWarning($"Heartbeat exception: {e.Message}");
                }

                try {
                    await UniTask.Delay(TimeSpan.FromSeconds(ServerBootStrap.serverSettings.ServerHeartbeatIntvSeconds), cancellationToken: token);
                }
                catch (OperationCanceledException) {
                    break;
                }
            }

            Debug.Log("Heartbeat loop stopped");
        }

        static void OnStopServer() {
            if (heartbeatCts != null)
                heartbeatCts.Cancel();
            // Debug.Log("Server shutting down");

            SendShutdown().Forget();
        }

        static async UniTask SendShutdown() {
            try {
                if (!await TaskHelper.AwaitTaskTimeout(SendShutdownRequest(), 2000))
                    Debug.LogWarning("Shutdown request timed out!");
            }
            catch (Exception e) {
                Debug.LogWarning($"Shutdown request failed: {e.Message}");
            }
        }

        static async UniTask SendShutdownRequest() {
            (string res, JObject _) =
                await BackendNetwork.PostRequest("/api/internal/v1/shutdown");

            if (res != null)
                Debug.LogWarning($"Shutdown failed: {res}");
            else
                Debug.Log("Shutdown sent");
        }
    }
}
