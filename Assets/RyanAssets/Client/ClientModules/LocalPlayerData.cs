using Newtonsoft.Json.Linq;
using RyanAssets.NetworkService;
using RyanAssets.PromptService;
using RyanAssets.Client.ClientModules;
using RyanAssets.Levels.Client;
using RyanAssets.DataService;
using System.Threading.Tasks;
using System;
using FishNet;
using FishNet.Connection;
using RyanAssets.Shared.Player;
using UnityEngine;

namespace RyanAssets.Client.ClientModules {
    public static class LocalPlayerData {
        public static PlayerData localData;
        public static PlayerSettings localSettings;
        public static Action<string> username_changed_event;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Init() {
            SharedGlobalEvents.OnPlayerAdded += OnSyncedPlayerData;
            SharedGlobalEvents.OnPlayerUpdated += OnSyncedPlayerData;
        }

        static void OnSyncedPlayerData(NetworkConnection conn, ServerPlayerStats stats) {
            if (InstanceFinder.ClientManager == null || conn != InstanceFinder.ClientManager.Connection)
                return;

            bool usernameChanged = localData.username != stats.data.username;
            localData = stats.data;
            LevelClient.UpdateLevelInstances(localData);
            if (usernameChanged)
                username_changed_event?.Invoke(localData.username);
        }

        public static void PlayerInit(JObject json) {
            localData.username = (string)json["username"];
            localData.xp = (ulong)json["xp"];
            localData.gold = (ulong)json["gold"];
            localSettings = (json.TryGetValue("preferences", out JToken preferences) && (string)preferences != null)
                ? preferences.ToObject<PlayerSettings>()
                : default;
            LevelClient.UpdateLevelInstances(localData);
            username_changed_event?.Invoke(localData.username);
        }
        static JObject pending_data;
        static async Task<(string, JObject)> ModifyUsernameNetworkRequest() {
            return await BackendNetwork.PostRequest("/api/players/v1/me/username", pending_data);
        }
        public static async void ModifyUsername(string username) {
            pending_data = new JObject(
                new JProperty("username", username)
            );
            (string res, JObject json) = await BackendClient.RequestAsync(ModifyUsernameNetworkRequest, "Modify Username", promptWaiting: PromptId.UsernameChangeAwait, promptResult: PromptId.UsernameResponse);
            if (res == null) {
                localData.username = username;
                username_changed_event?.Invoke(username);
            }
        }
    }
}
