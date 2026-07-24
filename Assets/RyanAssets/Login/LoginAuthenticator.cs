using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Authentication.PlayerAccounts;
using RyanAssets.PromptService;
using UnityEngine;
using RyanAssets.NetworkService;

namespace RyanAssets.Login {
    public class LoginAuthenticator : MonoBehaviour {
        static bool _initialized;
        bool _unityAuthSignInInProgress;
        public static bool didTryLogin;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Init() {
            _initialized = false;
        }
        public async void UnityLogin() {
            try {
                didTryLogin = true;
                await PlayerAccountService.Instance.StartSignInAsync();
                // StartSignInAsync is the completion point even when the
                // PlayerAccountService event was consumed during restoration.
                UnityLogin_Complete();
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
        private async void UnityLogin_Complete() {
            // A restored player-account session is not guaranteed to raise
            // PlayerAccountService.SignedIn, so this is also called explicitly
            // after StartSignInAsync completes.
            if (AuthenticationService.Instance.IsSignedIn)
                return;
            if (_unityAuthSignInInProgress)
                return;

            _unityAuthSignInInProgress = true;
            try {
                await AuthenticationService.Instance.SignInWithUnityAsync(
                    PlayerAccountService.Instance.AccessToken
                );
            } catch (System.Exception e) {
                Debug.LogException(e);
                PromptManager.PromptError("SignIn", e);
            } finally {
                _unityAuthSignInInProgress = false;
            }
        }
        async void OnEnable() {
            didTryLogin = false;
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
