using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using FishNet;
using FishNet.Connection;
using FishNet.Transporting;
using Newtonsoft.Json.Linq;
using RyanAssets.DataService;
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
            PlayerData.OnPlayerAdded += (conn, playerData) => Save(conn);
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

        public static async UniTask Forget(NetworkConnection conn, PlayerData stats) {
            if (dirtyConnections.TryGetValue(conn, out var dirty) && dirty)
                await Save(stats);
            dirtyConnections.Remove(conn);
        }

        public static UniTask Save() {
            return SaveDirty();
        }

        public static UniTask Save(NetworkConnection conn) {
            if (SharedGlobalEvents.Instance == null || !PlayerData.Players.ContainsKey(conn))
                return UniTask.CompletedTask;
            if (!dirtyConnections.ContainsKey(conn) || !dirtyConnections[conn])
                return UniTask.CompletedTask;

            return SavePlayers(new List<NetworkConnection> { conn });
        }
        public static UniTask Save(PlayerData playerData) {
            return SavePlayers(new List<PlayerData> { playerData });
        }

        static UniTask SaveDirty() {
            return SavePlayers(new List<NetworkConnection>(dirtyConnections.Keys));
        }
        static async UniTask SavePlayers(List<NetworkConnection> connections) {
            List<PlayerData> players = new();
            foreach (NetworkConnection conn in connections) {
                if (PlayerData.Players.TryGetValue(conn, out PlayerData playerData))
                    players.Add(playerData);
            }
            await SavePlayers(players);
        }

        static async UniTask SavePlayers(List<PlayerData> players) {
            JObject payload = new() {
                ["players"] = new JObject()
            };

            List<PlayerData> savedPlayers = new();

            foreach (PlayerData player in players) {
                if (string.IsNullOrWhiteSpace(player.player_id.Value))
                    continue;

                payload["players"][player.player_id.Value] = player.Serialize();
                savedPlayers.Add(player);
            }

            if (savedPlayers.Count == 0)
                return;

            (string res, JObject _) = await BackendServer.RequestAsync(
                () => BackendNetwork.PostRequest("/api/internal/v1/user/save", payload),
                "Save Player Stats"
            );

            if (res == null) {
                foreach (PlayerData player in savedPlayers)
                    dirtyConnections.Remove(player.Owner);

                return;
            }

            PromptBroadcast prompt = new() {
                title = "PromptError",
                description = $"Failed to save player stats: {res}"
            };

            foreach (PlayerData player in savedPlayers) {
                if (player.Owner != null)
                    InstanceFinder.ServerManager.Broadcast(player.Owner, prompt);
            }
        }
    }
}
