using System;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using RyanAssets.NetworkService;
using RyanAssets.Prompt;
using RyanAssets.DataService;
using UnityEngine;
using UnityEngine.UI;

namespace RyanAssets.Login {
    public class LoginModifyUsername: MonoBehaviour {
        [SerializeField]
        InputField usernameInputField;
        string newUsername;
        public void EditButtonClicked(){
            usernameInputField.Select();
        }
        async Task<(string, JObject)> CheckUsernameAvailability(){
            return await ServerNetwork.GetRequest($"/api/players/v1/check-availability?username={newUsername}");
        }
        public async void UsernameSubmit(){
            newUsername = usernameInputField.text;
            if (newUsername == LocalPlayerData.localData.username)
                return;

            (string res, JObject json) = await ServerNetwork.RequestAsync(CheckUsernameAvailability, "Username Availability", promptWaiting: PromptId.UsernameCheckAwait, promptResult: PromptId.UsernameResponse, retryPolicy: RetryPolicy.RetryOrCancel);
            // Debug.Log(json);
            if (!(bool) json["Available"]){
                PromptManager.PromptError("Username Unavailable", "Selected username is taken, please choose another one!");
                return;
            }
            PromptButton resp = await PromptManager.Instance.PromptLocalUser("Confirm Username Change?", 
                $"Are you sure that you want your username to be\n\"{newUsername}\"\nThis action is irreverisble!", PromptId.UsernameChangeConfirm,
                PromptManager.ButtonPreset_YesNo
            );
            if (resp != PromptButton.Yes){
                usernameInputField.text = LocalPlayerData.localData.username;
                return;
            }
            LocalPlayerData.ModifyUsername(newUsername);
        }
    }
}