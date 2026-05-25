using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Authentication.PlayerAccounts;
using System.Threading.Tasks;
using RyanAssets.PromptService;
using UnityEngine;

namespace RyanAssets.Login {
    public class LoginAuthenticator : MonoBehaviour {
        public async void UnityLogin() {
            try {
                await PlayerAccountService.Instance.StartSignInAsync();
            } catch (System.Exception e) {
                Debug.Log(e);
                PromptManager.PromptError("Prompt", e);
            }
        }
        public void UnityLogout() {
            AuthenticationService.Instance.SignOut();
            PlayerAccountService.Instance.SignOut();
            AuthenticationService.Instance.ClearSessionToken();
        }
        private void UnityLogin_Complete() {
            AuthenticationService.Instance.SignInWithUnityAsync(
                PlayerAccountService.Instance.AccessToken
            );
        }
        async void Start() {
            await UnityServices.InitializeAsync();

            PlayerAccountService.Instance.SignedIn += UnityLogin_Complete;
            PlayerAccountService.Instance.SignInFailed += err => PromptManager.PromptError("SignIn", err);

            if (AuthenticationService.Instance.IsSignedIn) {
                UnityLogin_Complete();
                return;
            }

            if (AuthenticationService.Instance.SessionTokenExists) {
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
                return;
            }
        }
    }
}