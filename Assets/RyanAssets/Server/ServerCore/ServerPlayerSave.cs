using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FishNet;
using FishNet.Connection;
using FishNet.Transporting;
using Newtonsoft.Json.Linq;
using RyanAssets.NetworkService;
using RyanAssets.Server.ServerModules;
using RyanAssets.Shared.Broadcasts;
using RyanAssets.Shared.Player;
using UnityEngine;

namespace RyanAssets.Server.ServerCore {
    public static class ServerPlayerSave {
        static readonly Dictionary<NetworkConnection, bool> dirtyConnections = new();
        static CancellationTokenSource saveLoopCts;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Init() {
            ServerBootStrap.StartServerEvent += OnStartServer;
            ServerBootStrap.StopServerEvent += OnStopServer;
            ServerBootStrap.StopServerAsyncEvent += SaveDirty;
            ServerPlayerEvents.OnPlayerRemovedEvent += (conn) => Save(conn);
        }

        static void OnStartServer() {
            saveLoopCts = new CancellationTokenSource();
            _ = SaveLoop(saveLoopCts.Token);
        }

        static void OnStopServer() {
            if (saveLoopCts != null)
                saveLoopCts.Cancel();
        }

        static async Task SaveLoop(CancellationToken token) {
            while (!token.IsCancellationRequested) {
                try { 
                    await Task.Delay(TimeSpan.FromSeconds(60), token);
                    await Save();
                } catch (TaskCanceledException) {
                    break;
                }
            }
        }

        public static void MarkDirty(NetworkConnection conn) {
            dirtyConnections[conn] = true;
        }

        public static void Forget(NetworkConnection conn) {
            dirtyConnections.Remove(conn);
        }

        public static Task Save() {
            return SaveDirty();
        }

        public static Task Save(NetworkConnection conn) {
            if (SharedGlobalEvents.Instance == null || !SharedGlobalEvents.Instance.Players.ContainsKey(conn))
                return Task.CompletedTask;
            if (!dirtyConnections.ContainsKey(conn) || !dirtyConnections[conn])
                return Task.CompletedTask;

            return SavePlayers(new List<NetworkConnection> { conn });
        }

        static Task SaveDirty() {
            return SavePlayers(new List<NetworkConnection>(dirtyConnections.Keys));
        }

        static async Task SavePlayers(List<NetworkConnection> connections) {
            //InstanceFinder.ServerManager.Broadcast<PromptBroadcast>(new() {
            //    title = "PromptError",
            //    description = $"i am really annoying!"
            //});
            //Debug.Log($"broadcasted!");

            JObject payload = new() {
                ["players"] = new JObject()
            };
            List<NetworkConnection> savedConnections = new();

            foreach (NetworkConnection conn in connections) {
                if (!SharedGlobalEvents.Instance.Players.TryGetValue(conn, out ServerPlayerStats stats))
                    continue;

                if (string.IsNullOrWhiteSpace(stats.player_id))
                    continue;

                payload["players"][stats.player_id] = JObject.FromObject(stats.data);
                savedConnections.Add(conn);
            }

            if (savedConnections.Count == 0)    
                return;

            (string res, JObject _) = await BackendServer.RequestAsync(
                () => BackendNetwork.PostRequest("/api/internal/v1/user/save", payload),
                "Save Player Stats"
            );

            if (res == null) {
                foreach (NetworkConnection conn in savedConnections) {
                    dirtyConnections.Remove(conn);                        
                }
                return;
            }

            PromptBroadcast prompt = new() {
                title = "PromptError",
                description = $"Failed to save player stats: {res}"
            };

            foreach (NetworkConnection conn in savedConnections)
                InstanceFinder.ServerManager.Broadcast(conn, prompt);
        }
    }
}
