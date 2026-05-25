// using RyanAssets.PromptService;
using Unity.Services.Authentication;
using RyanAssets.NetworkService;
using UnityEngine;
using System;
using System.Net.Http;
using Newtonsoft.Json.Linq;
using System.Threading.Tasks;
using RyanAssets.DataService;
using RyanAssets.PromptService;
using RyanAssets.ClientModules;
using UnityEngine.UI;

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
        async Task<(string, JObject)> LoadPlayerStats() {
            return await BackendNetwork.PostRequest("/api/players/v1/me");
        }
        async void SignedIn() {
            loginScreen.RefreshScreen();
            (string res, JObject json) = await BackendClient.RequestAsync(LoadPlayerStats, "Login", promptWaiting: PromptId.UsernameCheckAwait, promptResult: PromptId.UsernameResponse);
            json["preferences"] = BackendNetwork.ParseJSON(json["preferences"].ToString());
            LocalPlayerData.PlayerInit(json);
            usernameInputField.text = json["username"].ToString();
        }
        void SignedOut() {
            loginScreen.RefreshScreen();
        }
        void Start() {
            AuthenticationService.Instance.SignedIn += SignedIn;
            AuthenticationService.Instance.Expired += SignedOut;
            AuthenticationService.Instance.SignedOut += SignedOut;
            AuthenticationService.Instance.SignInFailed += err => PromptManager.PromptError("SignIn", err);
        }
    }
}