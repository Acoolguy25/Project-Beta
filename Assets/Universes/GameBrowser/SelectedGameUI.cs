using UnityEngine;
using RyanAssets.Prompt;

namespace Universes.GameBrowser {
    public class SelectedGameUI: MonoBehaviour {
        public void ReportGame_ButtonClicked(){
            PromptManager.PromptOk("Report Sent", "A *VERY REAL* report was sent to the owner!\nUwU OwO QwQ TwT >w< ^w^ ;w; T~T Q~Q @w@ x3 :3 o_o O_O XwX");
        }
    }
}