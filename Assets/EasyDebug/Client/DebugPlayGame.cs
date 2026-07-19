using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Universes.GameBrowser;

namespace EasyDebug.Debug {
    public class DebugPlayGame: MonoBehaviour {
        [SerializeField]
        string PlayGameUniverseId;
        [SerializeField]
        Button continueButton;
#if UNITY_EDITOR
        IEnumerator PressButton(Button button){
            while (!button.IsInteractable()){
                yield return new WaitForSeconds(0.5f);
            }
            button.onClick.Invoke();
        }
        IEnumerator Start(){
            yield return PressButton(continueButton);
            _ = SelectedGameUI.PlayGame(PlayGameUniverseId);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Init() {
#if !UNITY_SERVER
            SceneManager.LoadScene("MainMenu");
#endif
        }
#endif
    }
}