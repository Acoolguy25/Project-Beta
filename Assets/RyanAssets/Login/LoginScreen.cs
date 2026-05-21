using RyanAssets.Prompt;
using Unity.VisualScripting;
using Unity.Services.Authentication;
using UnityEngine;

namespace RyanAssets.Login {
    public class LoginScreen: MonoBehaviour {
        [SerializeField]
        GameObject signin, logout;
        public void RefreshScreen(){
            signin.SetActive(!AuthenticationService.Instance.IsSignedIn);
            logout.SetActive(AuthenticationService.Instance.IsSignedIn);
        }
        void Start(){
            RefreshScreen();
        }
    }
}