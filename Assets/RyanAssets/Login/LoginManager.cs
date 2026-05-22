using RyanAssets.Prompt;
using Unity.Services.Authentication;
using RyanAssets.NetworkService;
using UnityEngine;
using System;
using System.Net.Http;
using Newtonsoft.Json.Linq;
using System.Threading.Tasks;
using RyanAssets.DataService;
using UnityEngine.UI;

namespace RyanAssets.Login {
    public class LoginManager : MonoBehaviour {
        public static LoginManager Instance;
        private void Awake() {
            Instance = this;
        }
        [SerializeField]
        LoginScreen loginScreen;
        [SerializeField]
        InputField usernameInputField;
        async Task<(string, JObject)> LoadPlayerStats(){
            return await ServerNetwork.PostRequest("/api/players/v1/me");
        }
        async void SignedIn(){
            loginScreen.RefreshScreen();
            // while (true){
            //     PromptManager.PromptWait("Loading", "Connecting To Server", PromptId.NetworkLoginAwait);
            //     PromptManager.PromptDelete(PromptId.NetworkLoginAwait);
            (string res, JObject json) = await ServerNetwork.RequestAsync(LoadPlayerStats, "Login", promptWaiting: PromptId.UsernameCheckAwait, promptResult: PromptId.UsernameResponse);
            //     if (errMsg != null){
            //         await PromptManager.Instance.PromptLocalUser("Login Failed", errMsg, PromptId.LoginResponse, PromptManager.ButtonPreset_RetryOnly);
            //     }
            //     else{
            //         Debug.Log(res2);
            //         break;
            //     }
            // }
            // TODO: Load player stats here
            json["preferences"] = ServerNetwork.ParseJSON((string) json["preferences"]);
            LocalPlayerData.PlayerInit(json);
            usernameInputField.text = (string) json["username"];
        }
        void SignedOut(){
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