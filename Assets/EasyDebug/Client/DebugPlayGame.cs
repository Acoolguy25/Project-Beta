using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using Universes.GameBrowser;

namespace EasyDebug.Debug {
    public class DebugPlayGame: MonoBehaviour {
        [SerializeField]
        string PlayGameUniverseId;
        [SerializeField]
        Button continueButton;
        IEnumerator PressButton(Button button){
            while (!button.IsInteractable()){
                yield return new WaitForSeconds(0.5f);
            }
            button.onClick.Invoke();
        }
        IEnumerator Start(){
            yield return PressButton(continueButton);
            SelectedGameUI.PlayGame(PlayGameUniverseId);
        }
    }
}