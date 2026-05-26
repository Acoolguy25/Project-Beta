
using UnityEngine;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using System.Net.Http.Headers;
using System;
using System.Text;
#if !SERVER_BUILD
using Unity.Services.Authentication;
#endif
using System.Net;

namespace RyanAssets.NetworkService {
    public enum RetryPolicy {
        NoRetry,
        RetryOrCancel,
        ForceRetry
    };
    public static class BackendNetwork {
        static readonly HttpClient client = new() {
            BaseAddress = new Uri(NetworkSettings.BackendAPIURL)
        };
        static public string default_body = string.Empty;
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
            #if !SERVER_BUILD
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
            #if !SERVER_BUILD
                SetAuthorizationToken(AuthenticationService.Instance.AccessToken);
            #endif
                StringContent content = new(
                    (body != null) ? body.ToString() : default_body,
                    Encoding.UTF8,
                    "application/json"
                );
                HttpResponseMessage response = await client.PostAsync(url, content);
                return await HandleResponse(response);
            } catch (Exception e) {
                return (e.ToString(), null);
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
