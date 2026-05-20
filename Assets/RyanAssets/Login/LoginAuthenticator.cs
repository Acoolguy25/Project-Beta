using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Authentication.PlayerAccounts;
using System.Threading.Tasks;
using UnityEngine;

namespace RyanAssets.Login {
    public class LoginAuthenticator: MonoBehaviour {
        public async void UnityLogin()
        {
            try{
                await PlayerAccountService.Instance.StartSignInAsync();
            }
            catch(System.Exception e){
                Debug.Log(e);
            }
        }
        public void UnityLogout()
        {
            AuthenticationService.Instance.SignOut();
            PlayerAccountService.Instance.SignOut();
        }
        async void Start(){
            await UnityServices.InitializeAsync();
            PlayerAccountService.Instance.SignedIn += () => 
                AuthenticationService.Instance.SignInWithUnityAsync(
                    PlayerAccountService.Instance.AccessToken
                );
            ;
        }
    }
}