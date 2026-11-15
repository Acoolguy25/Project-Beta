using System.Collections;
using RyanAssets.Client.ClientCore;
using RyanAssets.Login;
using UnityEngine;
using UnityEngine.SceneManagement;
using Universes.GameBrowser;

namespace EasyDebug.Debug {
    /// <summary>Editor shortcut: once signed in, skips the login screen and joins war_valley.</summary>
    public class DebugPlayGame : MonoBehaviour {
        const string PlayGameUniverseId = "war_valley";
#if UNITY_EDITOR
        IEnumerator Start() {
            ClientConnector.OnDisconnected += OnDisconnected;
            yield return new WaitUntil(() => LoginManager.Instance != null && LoginScreen.IsSignedIn());
            LoginManager.Instance.loginScreen.Continue_ButtonPressed();
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
