using Newtonsoft.Json.Linq;
using RyanAssets.NetworkService;
using RyanAssets.PromptService;
using RyanAssets.Client.ClientModules;
using RyanAssets.Levels.Client;
using RyanAssets.DataService;
using Cysharp.Threading.Tasks;
using System;
using FishNet;
using FishNet.Connection;
using RyanAssets.Shared.Player;
using RyanAssets.Shared.Declarations;
using UnityEngine;

namespace RyanAssets.Client.ClientModules {
    public static class LocalPlayerDataHandler {
        public static LocalPlayerData localData;
        public static LocalPlayerSettings localSettings;
        public static Action<string> username_changed_event;

        public static void PlayerInit(JObject json) {
            if (json == null) {
                Debug.LogError("Cannot initialize local player data because the player stats response was null.");
                return;
            }

            localData.username = (string)json["username"];
            localData.xp = (ulong)json["xp"];
            localData.gold = (ulong)json["gold"];
            localSettings = (json.TryGetValue("preferences", out JToken preferences) && (string)preferences != null)
                ? preferences.ToObject<LocalPlayerSettings>()
                : default;
            LevelClient.UpdateLevelInstances(localData);
            username_changed_event?.Invoke(localData.username);
        }
        static JObject pending_data;
        static async UniTask<(string, JObject)> ModifyUsernameNetworkRequest() {
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
