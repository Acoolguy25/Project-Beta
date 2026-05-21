
using UnityEngine;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using System.Net.Http.Headers;
using PlasticPipe.PlasticProtocol.Messages;
using System;
using System.Text;

namespace RyanAssets.NetworkService {
    public class ServerNetwork {
        static readonly HttpClient client = new() {
            BaseAddress = new Uri(NetworkSettings.BackendURL)
        };
        static async Task<(string, JObject)> HandleResponse(HttpResponseMessage response){
            string text = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode){
                return (text, null);
            }

            try{
                JObject json = JObject.Parse(text);
                return (null, json);
            }
            catch (Exception e){
                return ($"JSON Parse Failed: {e}\nGot: ${text}", null);
            }
        }
        public static async Task<(string, JObject)> GetRequest(string url){
            try{
                HttpResponseMessage response = await client.GetAsync(url);
                return await HandleResponse(response);
            } catch (Exception e){
                return (e.ToString(), null);
            }
        }
        public static async Task<(string, JObject)> PostRequest(string url, JObject body = null){
            try{
                StringContent content = new(
                    (body != null)? body.ToString(): string.Empty,
                    Encoding.UTF8,
                    "application/json"
                );
                HttpResponseMessage response = await client.PostAsync(url, content);
                return await HandleResponse(response);
            } catch (Exception e){
                return (e.ToString(), null);
            }
        }
        public static void SetAuthorizationToken(string accessToken){
            // Debug.Log($"Access Token: {accessToken[..20]}..");
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue(
                    "Bearer",
                    accessToken
                );
        }
    }
}