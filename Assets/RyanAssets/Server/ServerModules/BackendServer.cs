using UnityEngine;
using RyanAssets.NetworkService;
using System;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using RyanAssets.Core;

namespace RyanAssets.Server.ServerModules {
    public static class BackendServer {
        public static async Task<(string, JObject)> RequestAsync(Func<Task<(string, JObject)>> requestFunc, string title, int retries = 3) {
            string res = "Request Never Executed";
            for (int i = 0; i < retries || retries <= 0; i++) {
                async Task<(string, JObject)> SafeTryRequest() {
                    try {
                        return await requestFunc();
                    } catch (Exception e) {
                        return ($"Unhandled error: {e}", null);
                    }
                }
                JObject j;
                (res, j) = await SafeTryRequest();
                if (res == null) { // succeeded
                    return (null, j);
                } else { // failed
                    string retriesText = (retries > 0) ? ($"/{retries}") : "";
                    string jsonDisplay = (j != null) ? $"\nJSON: {JsonConvert.SerializeObject(j, Formatting.Indented)}" : "";
                    Debug.LogError($"{title} {res} ({i}{retriesText}){jsonDisplay}");
                    if (retries > 0)
                        await Task.Delay(TimeSpan.FromSeconds(RequestHelper.GetRetryDelay(i)));
                }
            }
            return (res, null);
        }
    }
}