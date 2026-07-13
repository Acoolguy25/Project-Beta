using UnityEngine;
using RyanAssets.DataService;
using RyanAssets.Login;
using RyanAssets.UI.ButtonGrid;
using RyanAssets.NetworkService;
using RyanAssets.PromptService;
using RyanAssets.Client.ClientModules;
using UnityEngine.UI;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json.Linq;
using System;
using RyanAssets.TweenService.TweenComponents;

namespace Universes.GameBrowser {
    public class GameListUI : ButtonGridUI<UniverseStruct> {
        [SerializeField]
        Text UsernameTextUI;
        [SerializeField]
        SelectedGameUI selectedGameUI;
        [SerializeField]
        Image refreshImage;

        void UsernameRefresh(string username) {
            UsernameTextUI.text = username;
        }
        public void NavigateToLoginPage_ButtonClicked() {
            LoginManager.Instance.loginScreen.SetLoginScreenVisible(true);
        }
        async UniTask<(string, JObject)> LoadUniverseList() {
            return await BackendNetwork.GetRequest("/api/universes/v1/list");
        }
        [Serializable]
        public struct JSONUniverseResponse {
            public JSONUniverseData[] universes;
        }
        [Serializable]
        public struct JSONUniverseData {
            public string universe_id;
            public ulong active_players;
        }
        private JSONUniverseData GetUniverseFromJSONResponse(JSONUniverseResponse response, string universe_id) {
            foreach (JSONUniverseData universe in response.universes) {
                if (universe.universe_id == universe_id)
                    return universe;
            }
            return default;
        }
        override protected async void Start() {
            base.Start();
            (string msg, JObject json) = await BackendClient.RequestAsync(LoadUniverseList, "Player Count", promptWaiting: PromptId.GamePageAwait, promptResult: PromptId.GamePageConfirm);
            if (msg != null || json == null)
                return;
            JSONUniverseResponse universeResponse = json.ToObject<JSONUniverseResponse>();

            OnCreatePrefab += (GameObject obj, UniverseStruct data) => {
                JSONUniverseData JSONuniverse = GetUniverseFromJSONResponse(universeResponse, data.id);
                obj.transform.GetChild(0).GetComponent<Image>().sprite = data.LoadSprite();
                obj.transform.GetChild(1).GetComponent<Text>().text = data.title;
                obj.transform.GetChild(2).GetComponent<Text>().text = $"{JSONuniverse.active_players} active";
                obj.name = data.id;
            };
            OnClickPrefab += (GameObject obj, UniverseStruct data) => {
                JSONUniverseData JSONuniverse = GetUniverseFromJSONResponse(universeResponse, data.id);
                selectedGameUI.OpenUniversePage(data, JSONuniverse.active_players);
            };
            RefreshPrefabs(UniverseCfg.ActiveUniverses);
        }
        private void OnEnable() {
            LocalPlayerDataHandler.username_changed_event += UsernameRefresh;
            UsernameRefresh(LocalPlayerDataHandler.localData.username);
        }
        private void OnDisable() {
            LocalPlayerDataHandler.username_changed_event -= UsernameRefresh;
        }
        public void Refresh_ButtonClicked() {
            TweenImage.SpinImage(refreshImage, 1 / 3f, 180);
            Start();
        }
    }
}
