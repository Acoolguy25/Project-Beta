using RyanAssets.NetworkService;
using RyanAssets.UI;
using Unity.Services.Authentication;
using UnityEngine;

namespace RyanAssets.Login {
    public class LoginScreen: MonoBehaviour {
        [SerializeField]
        GameObject signin, logout;
        [SerializeField]
        CanvasGroupController continueCanvas;
        [Tooltip("The login screen's own settings screen (a private copy of the game menu).")]
        [SerializeField]
        HudPanel settingsPanel;
        CanvasGroupController loginCanvas;
        [SerializeField, Range(0f, 1f)]
        float AnimationTime;
        public void RefreshScreen(){
            #if !UNITY_EDITOR
                bool isSignedIn = AuthenticationService.Instance.IsSignedIn;
            #else
                bool isSignedIn = NetworkSettings.noNetworkLogin? true: AuthenticationService.Instance.IsSignedIn;
            #endif
            signin.SetActive(!isSignedIn);
            logout.SetActive(isSignedIn);
        }
        public void SetLoginScreenVisible(bool visible, bool instant = false){
            // if (visible)
            continueCanvas.SetVisible(!visible, instant? 0f: AnimationTime);
                // loginCanvas.SetVisible(visible, instant? 0f: AnimationTime);
        }
        public void Continue_ButtonPressed(){
            SetLoginScreenVisible(false);
        }
        public void ToggleSettings_ButtonPressed(){
            if (settingsPanel != null)
                settingsPanel.Toggle();
        }
        void Start(){
            loginCanvas = GetComponent<CanvasGroupController>();
            RefreshScreen();
            SetLoginScreenVisible(true, true);
            if (settingsPanel != null)
                settingsPanel.SetVisible(false, instant: true);
        }
    }
}
