using System;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine;
using RyanAssets.PromptService;
using System.Collections.Generic;

namespace RyanAssets.Client.ClientModules {
    public enum RetryPolicy {
        NoRetry,
        RetryOrCancel,
        ForceRetry
    };
    public static class BackendClient {
        static async Task<(string, JObject)> SafeTryRequest(Func<Task<(string, JObject)>> requestFunc) {
            try {
                return await requestFunc();
            } catch (Exception e) {
                return ($"Unhandled error: {e}", null);
            }
        }
        public static async Task<(string, JObject)> RequestAsync(Func<Task<(string, JObject)>> requestFunc, string title, RetryPolicy retryPolicy = RetryPolicy.ForceRetry, PromptId promptWaiting = PromptId.Protected, PromptId promptResult = PromptId.Protected, string desc = "Connecting To Server...") {
            while (true) {
                CancellationTokenSource cts = new();
                List<Task> tasks = new();

                var requestTask = Task.Run(() => SafeTryRequest(requestFunc), cts.Token);

                Task waitingTask = requestTask;
                if (promptWaiting != PromptId.Protected)
                    if (retryPolicy == RetryPolicy.RetryOrCancel)
                        waitingTask = PromptManager.PromptCancelableWait(title, desc, promptWaiting);
                    else
                        PromptManager.PromptWait(title, desc, promptWaiting);
                Task completed = await Task.WhenAny(requestTask, waitingTask);

                if (completed == requestTask){
                    (string res, JObject j) = await requestTask;
                    PromptManager.PromptDelete(promptWaiting);
                    if (res == null) { // succeeded
                        return (null, j);
                    } else { // failed
                        if (res == null || res == string.Empty)
                            res = "Request failed for unknown reason";
                        Debug.LogError($"{title} - {res}");
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
                else{
                    cts.Cancel();
                    return ("User Cancelled", null);
                }
            }
        }
    }
}