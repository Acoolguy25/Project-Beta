using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FishNet;
using FishNet.Connection;
using Newtonsoft.Json.Linq;
using RyanAssets.NetworkService;
using RyanAssets.Server.ServerModules;
using RyanAssets.Shared.Broadcasts;
using RyanAssets.Shared.Player;
using UnityEngine;

namespace RyanAssets.Server.ServerCore {
    public static class ServerPlayerSave {
        static readonly Dictionary<NetworkConnection, int> dirtyVersions = new();
        static CancellationTokenSource saveLoopCts;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Init() {
            ServerBootStrap.StartServerEvent += OnStartServer;
            ServerBootStrap.StopServerEvent += OnStopServer;
            ServerBootStrap.StopServerAsyncEvent += SaveAll;
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
            dirtyVersions.TryGetValue(conn, out int version);
            dirtyVersions[conn] = version + 1;
        }

        public static void Forget(NetworkConnection conn) {
            dirtyVersions.Remove(conn);
        }

        public static Task Save() {
            return SaveDirty();
        }

        public static Task Save(NetworkConnection conn) {
            if (!ServerPlayerEvents.Players.ContainsKey(conn))
                return Task.CompletedTask;

            return SavePlayers(new List<NetworkConnection> { conn }, removeDirtyOnSuccess: true);
        }

        public static Task SaveAll() {
            return SavePlayers(new List<NetworkConnection>(ServerPlayerEvents.Players.Keys), removeDirtyOnSuccess: true);
        }

        static Task SaveDirty() {
            return SavePlayers(new List<NetworkConnection>(dirtyVersions.Keys), removeDirtyOnSuccess: true);
        }

        static async Task SavePlayers(List<NetworkConnection> connections, bool removeDirtyOnSuccess) {
            JObject payload = new();
            List<NetworkConnection> savedConnections = new();
            Dictionary<NetworkConnection, int> savedDirtyVersions = new();

            foreach (NetworkConnection conn in connections) {
                if (!ServerPlayerEvents.Players.TryGetValue(conn, out ServerPlayerStats stats))
                    continue;

                if (string.IsNullOrWhiteSpace(stats.player_id))
                    continue;

                payload[stats.player_id] = JObject.FromObject(stats.data);
                savedConnections.Add(conn);
                if (dirtyVersions.TryGetValue(conn, out int version))
                    savedDirtyVersions[conn] = version;
            }

            if (savedConnections.Count == 0)
                return;

            (string res, JObject _) = await BackendServer.RequestAsync(
                () => BackendNetwork.PostRequest("/api/internal/v1/user/save", payload),
                "Save Player Stats"
            );

            if (res == null) {
                if (removeDirtyOnSuccess) {
                    foreach (NetworkConnection conn in savedConnections) {
                        bool hadSavedVersion = savedDirtyVersions.TryGetValue(conn, out int savedVersion);
                        if (!hadSavedVersion || dirtyVersions.TryGetValue(conn, out int currentVersion) && currentVersion == savedVersion)
                            dirtyVersions.Remove(conn);
                    }
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
