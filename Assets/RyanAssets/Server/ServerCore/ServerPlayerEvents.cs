using System;
using UnityEngine;
using FishNet;
using FishNet.Connection;
using FishNet.Transporting;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using RyanAssets.Authentication;
using RyanAssets.Server.ServerModules;
using RyanAssets.NetworkService;
using RyanAssets.DataService;
using RyanAssets.Shared.Player;
using RyanAssets.Shared.Broadcasts;

namespace RyanAssets.Server.ServerCore {
    public static class ServerPlayerEvents {
        public static Action<NetworkConnection> OnPlayerAddedEvent, OnPlayerRemovedEvent;
        public static Dictionary<NetworkConnection, ServerPlayerStats> Players = new();
        static readonly Dictionary<NetworkConnection, int> dirtyVersions = new();
        static CancellationTokenSource saveLoopCts;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Init() {
            InstanceFinder.ServerManager.OnRemoteConnectionState += OnRemoteConnectionState;
            UnityTokenAuthenticator.OnAuthenticationSucceeded += OnAuthenticationSucceeded;
            ServerBootStrap.StartServerEvent += OnStartServer;
            ServerBootStrap.StopServerEvent += OnStopServer;
            ServerBootStrap.StopServerAsyncEvent += SaveAll;
        }

        static async void OnRemoteConnectionState(NetworkConnection conn, RemoteConnectionStateArgs args) {
            if (args.ConnectionState != RemoteConnectionState.Stopped)
                return;

            if (!Players.TryGetValue(conn, out ServerPlayerStats stats))
                return;

            await Save(conn);
            Players.Remove(conn);
            if (SharedGlobalEvents.Instance != null)
                SharedGlobalEvents.Instance.Players.Remove(conn);
            dirtyVersions.Remove(conn);
            OnPlayerRemovedEvent?.Invoke(conn);
            string remove_url = $"/api/internal/v1/user/remove?player_id={stats.player_id}";
            _ = BackendServer.RequestAsync(() => BackendNetwork.PostRequest(remove_url), "Player Disconnect");
        }

        static void OnAuthenticationSucceeded(NetworkConnection conn, JObject json) {
            // Debug.Log("Auth Succeeded: " + json);
            ServerPlayerStats stats = ParsePlayerStats(json);
            Debug.Log("PlayerAuthenticated: " + JsonConvert.SerializeObject(stats));
            Players.Add(conn, stats);
            if (SharedGlobalEvents.Instance != null)
                SharedGlobalEvents.Instance.Players.Add(new(conn, stats));
            OnPlayerAddedEvent?.Invoke(conn);
        }

        static ServerPlayerStats ParsePlayerStats(JObject json) {
            JObject normalizedJson = (JObject)json.DeepClone();
            JToken settings = normalizedJson["settings"];

            if (settings == null || settings.Type == JTokenType.Null || settings.Type == JTokenType.Undefined) {
                normalizedJson["settings"] = JObject.FromObject(default(PlayerSettings));
            } else if (settings.Type == JTokenType.String) {
                JObject parsedSettings = BackendNetwork.ParseJSON((string)settings);
                normalizedJson["settings"] = parsedSettings ?? JObject.FromObject(default(PlayerSettings));
            }

            return normalizedJson.ToObject<ServerPlayerStats>();
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

        public static bool AddXPReward(NetworkConnection conn, ulong xpReward) {
            if (!Players.TryGetValue(conn, out ServerPlayerStats stats))
                return false;

            return AddXPReward(conn, stats, xpReward);
        }

        public static bool AddXPReward(string playerId, ulong xpReward) {
            NetworkConnection matchedConn = null;
            ServerPlayerStats matchedStats = default;

            foreach (KeyValuePair<NetworkConnection, ServerPlayerStats> pair in Players) {
                if (pair.Value.player_id != playerId)
                    continue;

                matchedConn = pair.Key;
                matchedStats = pair.Value;
                break;
            }

            return matchedConn != null && AddXPReward(matchedConn, matchedStats, xpReward);
        }

        static bool AddXPReward(NetworkConnection conn, ServerPlayerStats stats, ulong xpReward) {
            ulong previousXp = stats.data.xp;
            stats.data.xp = ulong.MaxValue - previousXp < xpReward
                ? ulong.MaxValue
                : previousXp + xpReward;

            Players[conn] = stats;
            if (SharedGlobalEvents.Instance != null)
                SharedGlobalEvents.Instance.Players[conn] = stats;
            MarkDirty(conn);
            return true;
        }

        static void MarkDirty(NetworkConnection conn) {
            dirtyVersions.TryGetValue(conn, out int version);
            dirtyVersions[conn] = version + 1;
        }

        public static Task Save() {
            return SaveDirty();
        }

        public static Task Save(NetworkConnection conn) {
            if (!Players.ContainsKey(conn))
                return Task.CompletedTask;

            return SavePlayers(new List<NetworkConnection> { conn }, removeDirtyOnSuccess: true);
        }

        public static Task SaveAll() {
            return SavePlayers(new List<NetworkConnection>(Players.Keys), removeDirtyOnSuccess: true);
        }

        static Task SaveDirty() {
            return SavePlayers(new List<NetworkConnection>(dirtyVersions.Keys), removeDirtyOnSuccess: true);
        }

        static async Task SavePlayers(List<NetworkConnection> connections, bool removeDirtyOnSuccess) {
            JObject payload = new();
            List<NetworkConnection> savedConnections = new();
            Dictionary<NetworkConnection, int> savedDirtyVersions = new();

            foreach (NetworkConnection conn in connections) {
                if (!Players.TryGetValue(conn, out ServerPlayerStats stats))
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
