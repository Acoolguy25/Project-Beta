using Unity.Services.Authentication;
using Unity.VisualScripting;
using UnityEngine;

namespace RyanAssets.Login {
    public class LoginScreen: MonoBehaviour {
        public static LoginScreen Instance;
        private void Awake() {
            Instance = this;
        }
        [SerializeField]
        GameObject signin, logout;
        public void RefreshScreen(){
            signin.SetActive(!AuthenticationService.Instance.IsSignedIn);
            logout.SetActive(AuthenticationService.Instance.IsSignedIn);
        }
        void Start(){
            AuthenticationService.Instance.SignedIn += RefreshScreen;
            AuthenticationService.Instance.Expired += RefreshScreen;
            AuthenticationService.Instance.SignedOut += RefreshScreen;
            RefreshScreen();
        }
    }
}