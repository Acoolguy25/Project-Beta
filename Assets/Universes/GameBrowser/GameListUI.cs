using System;
using System.Linq;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json.Linq;
using RyanAssets.Client.ClientModules;
using RyanAssets.DataService;
using RyanAssets.Login;
using RyanAssets.NetworkService;
using RyanAssets.PromptService;
using RyanAssets.TweenService.TweenComponents;
using RyanAssets.UI.ButtonGrid;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Universes.GameBrowser {
    /// <summary>
    /// The main menu's game browser: a grid of <see cref="GameCardView"/>s with live player counts
    /// from the backend. Clicking a card opens its <see cref="SelectedGameUI"/> page.
    /// </summary>
    public class GameListUI : ButtonGridUI<UniverseStruct> {
        [SerializeField] TMP_Text usernameText;
        [SerializeField] SelectedGameUI selectedGameUI;
        [Tooltip("Icon spun while the list refreshes.")]
        [SerializeField] Image refreshIcon;

        [Serializable]
        struct UniverseListResponse {
            public UniverseCount[] universes;
        }

        [Serializable]
        struct UniverseCount {
            public string universe_id;
            public ulong active_players;
        }

        UniverseListResponse counts;

        protected override void Awake() {
            base.Awake();
            OnCreatePrefab += (card, universe) => card.GetComponent<GameCardView>().Bind(universe, ActivePlayers(universe.id));
            OnClickPrefab += (_, universe) => selectedGameUI.OpenUniversePage(universe, ActivePlayers(universe.id));
        }

        protected override void Start() {
            base.Start();
            Load().Forget();
        }

        void OnEnable() {
            LocalPlayerDataHandler.username_changed_event += OnUsernameChanged;
            OnUsernameChanged(LocalPlayerDataHandler.localData.username);
        }

        void OnDisable() {
            LocalPlayerDataHandler.username_changed_event -= OnUsernameChanged;
        }

        void OnUsernameChanged(string username) => usernameText.text = username;

        async UniTaskVoid Load() {
            (string error, JObject json) = await BackendClient.RequestAsync(RequestCounts, "Player Count", promptWaiting: PromptId.GamePageAwait, promptResult: PromptId.GamePageConfirm);
            if (this == null || error != null || json == null)
                return;
            counts = json.ToObject<UniverseListResponse>();
            RefreshPrefabs(ListedUniverses());
        }

        static async UniTask<(string, JObject)> RequestCounts() {
            if (NetworkSettings.EditorDirectConnection)
                return (null, new JObject {
                    ["universes"] = new JArray(new JObject { ["universe_id"] = NetworkSettings.EditorUniverseId, ["active_players"] = 0 })
                });
            return await BackendNetwork.GetRequest("/api/universes/v1/list");
        }

        static UniverseStruct[] ListedUniverses() {
#if UNITY_EDITOR
            return NetworkSettings.EditorDirectConnection
                ? UniverseCfg.ActiveUniverses.Where(u => u.id == NetworkSettings.EditorUniverseId).ToArray()
                : UniverseCfg.ActiveUniverses;
#else
            return UniverseCfg.ActiveUniverses.Where(u => u.access == UniverseAccess.Public).ToArray();
#endif
        }

        ulong ActivePlayers(string universeId) =>
            counts.universes?.FirstOrDefault(u => u.universe_id == universeId).active_players ?? 0;

        // Buttons (wired in the prefab)
        public void NavigateToLoginPage_ButtonClicked() => LoginManager.Instance.loginScreen.SetLoginScreenVisible(true);

        public void Refresh_ButtonClicked() {
            if (refreshIcon != null)
                TweenImage.SpinImage(refreshIcon, 0.5f, 360f);
            Load().Forget();
        }
    }
}
