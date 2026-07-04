
using UnityEngine;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using System.Net.Http.Headers;
using System;
using System.Text;
#if !SERVER_BUILD
using Unity.Services.Authentication;
#endif
using System.Net;

namespace RyanAssets.NetworkService {
    public static class BackendNetwork {
        static HttpClient client;
        static public string default_body = string.Empty;
        static string FormatException(string ExceptionString) {
            JObject json = ParseJSON(ExceptionString);
            if (json != null) {
                if (json.TryGetValue("detail", out JToken detail)) {
                    try {
                        return (string)detail[0]["msg"];
                    } catch {
                        return detail.ToString();
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


            JObject json = ParseJSON(text);
            if (!response.IsSuccessStatusCode) {
                // if (json == null)
                //     return (text, json);
                // else{
                    // UnityEngine.Debug.LogError($"{text}");
                    // if (json.TryGetValue("detail", out JToken detailToken)){
                    //     return (FormatException(text), json);
                    // }
                    // else{
                    //     return (JsonConvert.SerializeObject(json, Formatting.Indented), json);
                    // }

                // }
                return (FormatException(text), json);
            }
            if (json == null)
                return ($"JSON Parse Failed | Got: {text}", null);
            else
                return (null, json);
        }
#if !SERVER_BUILD
        public static string GetAuthorizationToken(){
            if (!NetworkSettings.noNetworkLogin) {
                return AuthenticationService.Instance.AccessToken;
            } else {
                return "Uvr2xiFAyUZJDybNdBEKcPOsMvjR";
            }
        }
#else
        public static void SetServerHeader(string header_name, string header_value){
            client.DefaultRequestHeaders.Add(header_name, header_value);
        }
#endif
        public static async Task<(string, JObject)> GetRequest(string url) {
            try {
                using HttpRequestMessage request = new(HttpMethod.Get, url);
            #if !SERVER_BUILD
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", GetAuthorizationToken());
            #endif
                HttpResponseMessage response = await client.SendAsync(request);
                return await HandleResponse(response);
            } catch (Exception e) {
                return (e.Message, null);
            }
        }
        public static async Task<(string, JObject)> PostRequest(string url, JObject body = null, string accessToken = null) {
            try {
                StringContent content = new(
                    (body != null) ? body.ToString() : default_body,
                    Encoding.UTF8,
                    "application/json"
                );

                using HttpRequestMessage request = new(HttpMethod.Post, url) {
                    Content = content
                };
                if (accessToken != null) {
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                }
            #if !SERVER_BUILD
                else {
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", GetAuthorizationToken());
                }
            #endif

                HttpResponseMessage response = await client.SendAsync(request);

                return await HandleResponse(response);
            } catch (Exception e) {
                return (e.Message, null);
            }
        }
        public static void SetBackendURL(string backend_url){
            Debug.Log($"Initialized Backend URL: {backend_url}");
            client = new() {
                BaseAddress = new Uri(backend_url)
            };
        }
    }
}
