using System;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json.Linq;
using RyanAssets.Client.ClientCore;
using RyanAssets.Client.ClientModules;
using RyanAssets.NetworkService;
using RyanAssets.PromptService;
using RyanAssets.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Universes.GameBrowser {
    /// <summary>The page of one game: big thumbnail, description, player count and the Play button.</summary>
    public class SelectedGameUI : MonoBehaviour {
        [NonSerialized]
        public UniverseStruct activeUniverse;

        [SerializeField] CanvasGroupController page;
        [SerializeField] Image thumbnailImage;
        [SerializeField] TMP_Text title;
        [SerializeField] TMP_Text description;
        [SerializeField] TMP_Text activePlayers;
        [SerializeField, Min(0f)] float fadeDuration = 0.2f;

        static string joiningUniverseId;

        public void OpenUniversePage(UniverseStruct universe, ulong playerCount) {
            activeUniverse = universe;
            title.text = universe.title;
            description.text = universe.description;
            thumbnailImage.sprite = universe.LoadSprite();
            activePlayers.text = GameCardView.FormatPlaying(playerCount);
            page.SetVisible(true, fadeDuration);
        }

        public void CloseUniversePage() => page.SetVisible(false, fadeDuration);

        static async UniTask<(string, JObject)> GetMyServer() =>
            await BackendNetwork.PostRequest($"/api/universes/v1/play?universe_id={joiningUniverseId}");

        public static async UniTask PlayGame(string universeId) {
            if (NetworkSettings.EditorDirectConnection) {
                ClientConnector.Instance.JoinLocalEditorServer();
                return;
            }
            joiningUniverseId = universeId;
            (string error, JObject json) = await BackendClient.RequestAsync(GetMyServer, "Getting Server", promptWaiting: PromptId.PlayGameAwait, promptResult: PromptId.PlayGameConfirm, retryPolicy: RetryPolicy.RetryOrCancel);
            if (error != null || json == null)
                return;
            ClientConnector.Instance.JoinGameServer(joiningUniverseId, json);
        }

        // Buttons (wired in the prefab)
        public void ReportGame_ButtonClicked() =>
            PromptManager.PromptOk("Report Game", "Reporting isn't available yet.");

        public async void PlayGame_ButtonClicked() => await PlayGame(activeUniverse.id);

        public void CloseGame_ButtonClicked() => CloseUniversePage();
    }
}
