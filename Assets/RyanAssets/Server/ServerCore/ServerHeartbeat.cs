using UnityEngine;
using System;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using RyanAssets.NetworkService;

namespace RyanAssets.Server.ServerCore {
    public static class ServerHeartbeat {
        static CancellationTokenSource heartbeatCts;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Init() {
            Debug.Log("ServerHeartbeat Init");

            heartbeatCts = new CancellationTokenSource();
            ServerBootStrap.StopServerEvent += OnStopServer;

            _ = HeartbeatLoop(heartbeatCts.Token);
        }

        static async Task HeartbeatLoop(CancellationToken token) {
            Debug.Log($"Heartbeat loop started: {NetworkSettings.BackendAPIURL}");

            while (!token.IsCancellationRequested) {
                try {
                    (string res, JObject _) =
                        await BackendNetwork.PostRequest("/api/internal/v1/heartbeat");

                    if (res != null)
                        Debug.LogWarning($"Heartbeat failed: {res}");
                    else
                        Debug.Log("Heartbeat sent");
                }
                catch (Exception e) {
                    Debug.LogWarning($"Heartbeat exception: {e.Message}");
                }

                try {
                    await Task.Delay(TimeSpan.FromSeconds(ServerBootStrap.ServerIdleTimeoutSeconds), token);
                }
                catch (TaskCanceledException) {
                    break;
                }
            }

            Debug.Log("Heartbeat loop stopped");
        }

        static void OnStopServer() {
            if (heartbeatCts != null)
                heartbeatCts.Cancel();
            // Debug.Log("Server shutting down");

            _ = SendShutdown();
        }

        static async Task SendShutdown() {
            try {
                Task request = SendShutdownRequest();
                Task timeout = Task.Delay(TimeSpan.FromSeconds(2));

                Task finished = await Task.WhenAny(request, timeout);

                if (finished == timeout)
                    Debug.LogWarning("Shutdown request timed out!");
                else
                    await request;
            }
            catch (Exception e) {
                Debug.LogWarning($"Shutdown request failed: {e.Message}");
            }
        }

        static async Task SendShutdownRequest() {
            (string res, JObject _) =
                await BackendNetwork.PostRequest("/api/internal/v1/shutdown");

            if (res != null)
                Debug.LogWarning($"Shutdown failed: {res}");
            else
                Debug.Log("Shutdown sent");
        }
    }
}