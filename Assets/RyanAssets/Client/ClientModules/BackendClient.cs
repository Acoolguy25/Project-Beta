using System;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine;

using RyanAssets.NetworkService;
using RyanAssets.PromptService;

namespace RyanAssets.Client.ClientModules {
    public enum RetryPolicy {
        NoRetry,
        RetryOrCancel,
        ForceRetry
    };
    public static class BackendClient {
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
                    PromptButton userResult = await PromptManager.Instance.PromptLocalUser(title + " Failed", res, promptResult,
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
    }
}