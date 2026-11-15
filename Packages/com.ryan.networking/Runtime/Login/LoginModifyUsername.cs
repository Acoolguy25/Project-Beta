using System.Text.RegularExpressions;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json.Linq;
using RyanAssets.Client.ClientModules;
using RyanAssets.NetworkService;
using RyanAssets.PromptService;
using RyanAssets.UI.Textbox;
using UnityEngine;

namespace RyanAssets.Login {
    public class LoginModifyUsername : MonoBehaviour {
        [SerializeField]
        CustomInputField usernameInputField;
        string newUsername;
        static readonly string usernameRegexPattern = @"^[A-Za-z0-9]+(?:_[A-Za-z0-9]+)?$";
        public void EditButtonClicked() {
            usernameInputField.Select();
            usernameInputField.ActivateInputField();
        }
        async UniTask<(string, JObject)> CheckUsernameAvailability() {
            return await BackendNetwork.GetRequest($"/api/players/v1/check-availability?username={newUsername}");
        }
        public async void UsernameSubmit() {
            newUsername = usernameInputField.text;
            if (newUsername == LocalPlayerDataHandler.localData.username)
                return;
            else if (!Regex.IsMatch(newUsername, usernameRegexPattern)) {
                await PromptManager.Instance.PromptLocalUser("Invalid Username", "Use letters and numbers only, with at most one underscore between them.", PromptId.UsernameResponse, PromptManager.ButtonPreset_CancelOnly);
                return;
            }

            (string res, JObject json) = await BackendClient.RequestAsync(CheckUsernameAvailability, "Username Availability", promptWaiting: PromptId.UsernameCheckAwait, promptResult: PromptId.UsernameResponse, retryPolicy: RetryPolicy.RetryOrCancel);
            // Debug.Log(json);
            if (res != null || json == null)
                return;
            if (!(bool)json["Available"]) {
                PromptManager.PromptError("Username Unavailable", "That username is taken. Please choose another one.");
                return;
            }
            PromptButton resp = await PromptManager.Instance.PromptLocalUser("Change username?",
                $"Your username will be \"{newUsername}\". This can't be undone.", PromptId.UsernameChangeConfirm,
                PromptManager.ButtonPreset_YesNo
            );
            if (resp != PromptButton.Yes) {
                usernameInputField.text = LocalPlayerDataHandler.localData.username;
                return;
            }
            LocalPlayerDataHandler.ModifyUsername(newUsername);
        }
    }
}
