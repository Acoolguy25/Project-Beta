using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using FishNet;
using FishNet.Connection;
using FishNet.Transporting;
using Newtonsoft.Json.Linq;
using RyanAssets.NetworkService;
using RyanAssets.Server.ServerModules;
using RyanAssets.Shared.Declarations;
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
            SaveLoop(saveLoopCts.Token).Forget();
        }

        static void OnStopServer() {
            if (saveLoopCts != null)
                saveLoopCts.Cancel();
        }

        static async UniTask SaveLoop(CancellationToken token) {
            while (!token.IsCancellationRequested) {
                try {
                    await UniTask.Delay(TimeSpan.FromSeconds(60), cancellationToken: token);
                    await Save();
                } catch (OperationCanceledException) {
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

        public static UniTask Save() {
            return SaveDirty();
        }

        public static UniTask Save(NetworkConnection conn) {
            if (SharedGlobalEvents.Instance == null || !SharedGlobalEvents.Instance.Players.ContainsKey(conn))
                return UniTask.CompletedTask;
            if (!dirtyConnections.ContainsKey(conn) || !dirtyConnections[conn])
                return UniTask.CompletedTask;

            return SavePlayers(new List<NetworkConnection> { conn });
        }

        static UniTask SaveDirty() {
            return SavePlayers(new List<NetworkConnection>(dirtyConnections.Keys));
        }

        static async UniTask SavePlayers(List<NetworkConnection> connections) {
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
