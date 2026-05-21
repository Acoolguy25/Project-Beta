using RyanAssets.Prompt;
using Unity.Services.Authentication;
using RyanAssets.NetworkService;
using UnityEngine;
using System;
using System.Net.Http;
using Newtonsoft.Json.Linq;

namespace RyanAssets.Login {
    public class LoginManager : MonoBehaviour {
        public static LoginManager Instance;
        private void Awake() {
            Instance = this;
        }
        [SerializeField]
        LoginScreen loginScreen;
        async void SignedIn(){
            ServerNetwork.SetAuthorizationToken(AuthenticationService.Instance.AccessToken);
            loginScreen.RefreshScreen();
            while (true){
                PromptManager.PromptWait("Loading", "Connecting To Server", PromptId.NetworkLoginAwait);
                (string errMsg, JObject res2) = await ServerNetwork.PostRequest("/api/players/v1/me");
                PromptManager.PromptDelete(PromptId.NetworkLoginAwait);
                if (errMsg != null){
                    await PromptManager.Instance.PromptLocalUser("Login Failed", errMsg, PromptId.LoginResponse, PromptManager.ButtonPreset_RetryOnly);
                }
                else{
                    // TODO: Do server manipulation here
                    Debug.Log(res2);
                    break;
                }
            }
        }
        void SignedOut(){
            ServerNetwork.SetAuthorizationToken(null);
            loginScreen.RefreshScreen();
        }
        void Start(){
            AuthenticationService.Instance.SignedIn += SignedIn;
            AuthenticationService.Instance.Expired += SignedOut;
            AuthenticationService.Instance.SignedOut += SignedOut;
            AuthenticationService.Instance.SignInFailed += err => PromptManager.PromptError("SignIn", err);
        }
    }
}