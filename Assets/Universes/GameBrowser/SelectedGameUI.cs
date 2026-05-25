using UnityEngine;
using RyanAssets.Prompt;
using RyanAssets.UI;
using UnityEngine.UI;
using RyanAssets.NetworkService;
using Newtonsoft.Json.Linq;
using System.Threading.Tasks;

namespace Universes.GameBrowser {
    [RequireComponent(typeof(CanvasGroupController))]
    public class SelectedGameUI: MonoBehaviour {
        public UniverseStruct activeUniverse;
        
        [SerializeField]
        CanvasGroupController selectedGameCanvasUI;
        [SerializeField]
        Image thumbnailImage;
        [SerializeField]
        Text title, description, active_players;
        string joining_universe_id;
        
        public void OpenUniversePage(UniverseStruct universe, ulong player_count){
            activeUniverse = universe;
            selectedGameCanvasUI.SetVisible(true, 0.5f);
            title.text = universe.title;
            description.text = universe.description;
            thumbnailImage.sprite = universe.LoadSprite();
            active_players.text = $"{player_count}";
        }
        public void CloseUniversePage(){
            selectedGameCanvasUI.SetVisible(false, 0.5f);
        }
        async Task<(string, JObject)> GetMyServer(){
            return await ServerNetwork.PostRequest($"/api/universes/v1/play?universe_id={joining_universe_id}");
        }
        public async Task PlayGame(string universe_id){
            joining_universe_id = universe_id;
            (string res, JObject json) = await ServerNetwork.RequestAsync(GetMyServer, "Getting Server", promptWaiting: PromptId.PlayGameAwait, promptResult: PromptId.PlayGameConfirm);
            Debug.Log(json);
        }

        // Button Clicks
        public void ReportGame_ButtonClicked(){
            PromptManager.PromptOk("Report Sent", "A *VERY REAL* report was sent to the owner!\nUwU OwO QwQ TwT >w< ^w^ ;w; T~T Q~Q @w@ x3 :3 o_o O_O XwX");
        }
        public async void PlayGame_ButtonClicked(){
            await PlayGame(activeUniverse.id);
        }
        public void CloseGame_ButtonClicked(){
            CloseUniversePage();
        }
    }
}