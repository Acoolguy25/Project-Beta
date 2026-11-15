using System;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json.Linq;
using RyanAssets.Client.ClientModules;
using RyanAssets.NetworkService;
using RyanAssets.PromptService;
using RyanAssets.UI.Textbox;
using Unity.Services.Authentication;
using UnityEngine;

namespace RyanAssets.Login {
    public class LoginManager : MonoBehaviour {
        public static LoginManager Instance;
        private void Awake() {
            Instance = this;
        }
        [SerializeField]
        public LoginScreen loginScreen;
        [SerializeField]
        CustomInputField usernameInputField;
        async UniTask<(string, JObject)> LoadPlayerStats() {
            if (NetworkSettings.EditorDirectConnection)
                return (null, NetworkSettings.CreateEditorPlayerProfile(NetworkSettings.EditorPlayerId));
            return await BackendNetwork.PostRequest("/api/players/v1/me");
        }
        async void SignedIn() {
            if (this == null || loginScreen == null) return;
            loginScreen.RefreshScreen();
            (string res, JObject json) = await BackendClient.RequestAsync(LoadPlayerStats, "Login", promptWaiting: PromptId.UsernameCheckAwait, promptResult: PromptId.UsernameResponse);
            if (this == null || loginScreen == null || usernameInputField == null) return;
            if (res != null || json == null) {
                Debug.LogError($"Login player stats request failed: {res ?? "Response JSON was empty"}");
                return;
            }
            // json["preferences"] = BackendNetwork.ParseJSON(json["preferences"].ToString());
            LocalPlayerDataHandler.PlayerInit(json);
            usernameInputField.text = json["username"].ToString();
            if (!LoginAuthenticator.didTryLogin)
                loginScreen.SetLoginScreenVisible(false, true);
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
                // Authentication can restore a session without emitting a
                // new SignedIn event. Reconcile the current state on startup.
                if (AuthenticationService.Instance.IsSignedIn)
                    SignedIn();
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
