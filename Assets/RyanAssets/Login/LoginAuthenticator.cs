using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Authentication.PlayerAccounts;
using System.Threading.Tasks;
using RyanAssets.PromptService;
using UnityEngine;
using RyanAssets.NetworkService;

namespace RyanAssets.Login {
    public class LoginAuthenticator : MonoBehaviour {
        static bool _initialized;
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
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Init() {
            _initialized = false;
        }
        async void OnEnable() {
            if (_initialized)
                return;
            _initialized = true;
            if (NetworkSettings.noNetworkLogin)
                return;
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