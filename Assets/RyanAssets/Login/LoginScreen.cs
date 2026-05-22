using RyanAssets.UI;
using Unity.Services.Authentication;
using UnityEngine;

namespace RyanAssets.Login {
    public class LoginScreen: MonoBehaviour {
        [SerializeField]
        GameObject signin, logout;
        [SerializeField]
        CanvasGroupController continueCanvas;
        CanvasGroupController loginCanvas;
        [SerializeField, Range(0f, 1f)]
        float AnimationTime;
        public void RefreshScreen(){
            signin.SetActive(!AuthenticationService.Instance.IsSignedIn);
            logout.SetActive(AuthenticationService.Instance.IsSignedIn);
        }
        public void SetLoginScreenVisible(bool visible, bool instant = false){
            // if (visible)
            continueCanvas.SetVisible(!visible, instant? 0f: AnimationTime);
                // loginCanvas.SetVisible(visible, instant? 0f: AnimationTime);
        }
        public void Continue_ButtonPressed(){
            SetLoginScreenVisible(false);
        }
        void Start(){
            loginCanvas = GetComponent<CanvasGroupController>();
            RefreshScreen();
            SetLoginScreenVisible(true, true);
        }
    }
}