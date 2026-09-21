using System.Collections;
using RyanAssets.Client.ClientCore;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Universes.GameBrowser;

namespace EasyDebug.Debug {
    public class DebugPlayGame : MonoBehaviour {
        const string PlayGameUniverseId = "war_valley";
        [SerializeField]
        Button continueButton;
#if UNITY_EDITOR
        IEnumerator PressButton(Button button) {
            while (!button.IsInteractable()) {
                yield return new WaitForSeconds(0.5f);
            }
            button.onClick.Invoke();
        }
        IEnumerator Start() {
            ClientConnector.OnDisconnected += OnDisconnected;
            yield return PressButton(continueButton);
            Connect();
        }

        void Connect() {
            _ = SelectedGameUI.PlayGame(PlayGameUniverseId);
        }
        void OnDisconnected() {
            if (!ClientConnector.hasCanceled)
                Connect();
        }
        private void OnDestroy() {
            ClientConnector.OnDisconnected -= OnDisconnected;
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
