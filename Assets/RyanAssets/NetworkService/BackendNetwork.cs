
using UnityEngine;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using System.Net.Http.Headers;
using System;
using System.Text;
using RyanAssets.Prompt;
using Unity.Services.Authentication;
using System.Net;

namespace RyanAssets.NetworkService {
    public enum RetryPolicy {
        NoRetry,
        RetryOrCancel,
        ForceRetry
    };
    public class ServerNetwork {
        static readonly HttpClient client = new() {
            BaseAddress = new Uri(NetworkSettings.BackendAPIURL)
        };
        static string FormatException(string ExceptionString) {
            JObject json = ParseJSON(ExceptionString);
            if (json != null) {
                if (json.TryGetValue("detail", out JToken detail)) {
                    try {
                        return (string)detail[0]["msg"];
                    } catch {
                        return (string)detail;
                    }
                }
            }
            return ExceptionString;
        }
        static public JObject ParseJSON(string text) {
            try {
                JObject json = JObject.Parse(text);
                return json;
            } catch {
                return null;
            }
        }
        static async Task<(string, JObject)> HandleResponse(HttpResponseMessage response) {
            if (response.StatusCode == HttpStatusCode.NoContent)
                return (null, null);
            string text = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode) {
                return (text, null);
            }

            JObject json = ParseJSON(text);
            if (json == null)
                return ($"JSON Parse Failed | Got: {text}", null);
            else
                return (null, json);
        }
        public static async Task<(string, JObject)> GetRequest(string url) {
            try {
                #if !IS_SERVER
                    SetAuthorizationToken(AuthenticationService.Instance.AccessToken);
                #endif
                HttpResponseMessage response = await client.GetAsync(url);
                return await HandleResponse(response);
            } catch (Exception e) {
                return (e.ToString(), null);
            }
        }
        public static async Task<(string, JObject)> PostRequest(string url, JObject body = null) {
            try {
                #if !IS_SERVER
                    SetAuthorizationToken(AuthenticationService.Instance.AccessToken);
                #endif
                StringContent content = new(
                    (body != null) ? body.ToString() : string.Empty,
                    Encoding.UTF8,
                    "application/json"
                );
                HttpResponseMessage response = await client.PostAsync(url, content);
                return await HandleResponse(response);
            } catch (Exception e) {
                return (e.ToString(), null);
            }
        }
        public static async Task<(string, JObject)> RequestAsync(Func<Task<(string, JObject)>> requestFunc, string title, RetryPolicy retryPolicy = RetryPolicy.ForceRetry, PromptId promptWaiting = PromptId.Protected, PromptId promptResult = PromptId.Protected) {
            while (true) {
                if (promptWaiting != PromptId.Protected)
                    PromptManager.PromptWait(title, "Connecting To Server...", promptWaiting);
                async Task<(string, JObject)> SafeTryRequest() {
                    try {
                        return await requestFunc();
                    } catch (Exception e) {
                        return ($"Unhandled error: {e}", null);
                    }
                }
                (string res, JObject j) = await SafeTryRequest();
                PromptManager.PromptDelete(promptWaiting);
                if (res == null) { // succeeded
                    return (null, j);
                } else { // failed
                    Debug.LogError(res);
                    PromptButton userResult = await PromptManager.Instance.PromptLocalUser(title + " Failed", FormatException(res), promptResult,
                        (retryPolicy == RetryPolicy.NoRetry) ? PromptManager.ButtonPreset_OkOnly :
                        (retryPolicy == RetryPolicy.RetryOrCancel) ? PromptManager.ButtonPreset_RetryCancel :
                        (retryPolicy == RetryPolicy.ForceRetry) ? PromptManager.ButtonPreset_RetryOnly :
                        PromptManager.ButtonPreset_None
                    );
                    if (userResult == PromptButton.Retry)
                        continue;
                    else if (userResult == PromptButton.Ok || userResult == PromptButton.Cancel)
                        return (res, j);
                }
            }
        }
        // Removed because AccessToken can be refreshed!
        public static void SetAuthorizationToken(string accessToken) {
            // Debug.Log($"Access Token: {accessToken[..20]}..");
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue(
                    "Bearer",
                    accessToken
                );
        }
    }
}