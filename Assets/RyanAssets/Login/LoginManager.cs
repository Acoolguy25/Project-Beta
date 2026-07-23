// using RyanAssets.PromptService;
using Unity.Services.Authentication;
using RyanAssets.NetworkService;
using UnityEngine;
using System;
using System.Net.Http;
using Newtonsoft.Json.Linq;
using Cysharp.Threading.Tasks;
using RyanAssets.DataService;
using RyanAssets.PromptService;
using RyanAssets.Client.ClientModules;
using UnityEngine.UI;
using System.Linq;

namespace RyanAssets.Login {
    public class LoginManager : MonoBehaviour {
        public static LoginManager Instance;
        private void Awake() {
            Instance = this;
        }
        [SerializeField]
        public LoginScreen loginScreen;
        [SerializeField]
        InputField usernameInputField;
        async UniTask<(string, JObject)> LoadPlayerStats() {
            return await BackendNetwork.PostRequest("/api/players/v1/me");
        }
        async void SignedIn() {
            loginScreen.RefreshScreen();
            (string res, JObject json) = await BackendClient.RequestAsync(LoadPlayerStats, "Login", promptWaiting: PromptId.UsernameCheckAwait, promptResult: PromptId.UsernameResponse);
            if (res != null || json == null) {
                Debug.LogError($"Login player stats request failed: {res ?? "Response JSON was empty"}");
                return;
            }
            // json["preferences"] = BackendNetwork.ParseJSON(json["preferences"].ToString());
            LocalPlayerDataHandler.PlayerInit(json);
            usernameInputField.text = json["username"].ToString();
            if (!LoginAuthenticator.didTryLogin)
                GetComponent<LoginScreen>().SetLoginScreenVisible(false, true);
        }
        void SignedOut() {
            loginScreen.RefreshScreen();
        }
        void SignInErr(Exception err) {
            PromptManager.PromptError("SignIn", err.Message);
        }
        void Start() {
            if (!NetworkSettings.noNetworkLogin) {
                AuthenticationService.Instance.SignedIn += SignedIn;
                AuthenticationService.Instance.Expired += SignedOut;
                AuthenticationService.Instance.SignedOut += SignedOut;
                AuthenticationService.Instance.SignInFailed += SignInErr;
            } else {
                SignedIn();
            }
            usernameInputField.text = LocalPlayerDataHandler.localData.username;
        }
        void OnDestroy() {
            if (!NetworkSettings.noNetworkLogin) {
                AuthenticationService.Instance.SignedIn -= SignedIn;
                AuthenticationService.Instance.Expired -= SignedOut;
                AuthenticationService.Instance.SignedOut -= SignedOut;
                AuthenticationService.Instance.SignInFailed -= SignInErr;
            }
        }
    }
}
