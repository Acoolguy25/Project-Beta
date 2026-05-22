using UnityEngine;
using RyanAssets.DataService;
using RyanAssets.Login;
using UnityEngine.UI;

namespace Universes.GameBrowser{
    public class GameListUI : MonoBehaviour {
        [SerializeField]
        Text UsernameTextUI;
        void UsernameRefresh(string username){
            UsernameTextUI.text = username;
        }
        public void NavigateToLoginPage_ButtonClicked(){
            LoginManager.Instance.loginScreen.SetLoginScreenVisible(true);
        }
        void Start(){
            LocalPlayerData.username_changed_event += UsernameRefresh;
        }
    }
}