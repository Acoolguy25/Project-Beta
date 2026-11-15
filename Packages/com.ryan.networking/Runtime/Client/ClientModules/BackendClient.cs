using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine;
using RyanAssets.PromptService;

namespace RyanAssets.Client.ClientModules {
    public enum RetryPolicy {
        NoRetry,
        RetryOrCancel,
        ForceRetry
    };
    public static class BackendClient {
        static async UniTask<(string, JObject)> SafeTryRequest(Func<UniTask<(string, JObject)>> requestFunc) {
            try {
                return await requestFunc();
            } catch (Exception e) {
                return ($"Unhandled error: {e}", null);
            }
        }
        public static async UniTask<(string, JObject)> RequestAsync(Func<UniTask<(string, JObject)>> requestFunc, string title, RetryPolicy retryPolicy = RetryPolicy.ForceRetry, PromptId promptWaiting = PromptId.Protected, PromptId promptResult = PromptId.Protected, string desc = "Connecting To Server...") {
            int tryCount = -1; // indicates first try, so the delay is 0 seconds
            while (true) {
                using CancellationTokenSource cts = new();

                if (promptWaiting != PromptId.Protected && retryPolicy != RetryPolicy.RetryOrCancel)
                    PromptManager.PromptWait(title, desc, promptWaiting);

                (string res, JObject j) result;

                if (promptWaiting != PromptId.Protected && retryPolicy == RetryPolicy.RetryOrCancel) {
                    var completedSource = new UniTaskCompletionSource<int>();
                    var requestSource = new UniTaskCompletionSource<(string, JObject)>();

                    UniTask.Create(async () => {
                        try {
                            await Awaitable.WaitForSecondsAsync(tryCount - 1, cts.Token);
                            (string res, JObject j) requestResult = await SafeTryRequest(requestFunc).AttachExternalCancellation(cts.Token);
                            requestSource.TrySetResult(requestResult);
                            completedSource.TrySetResult(0);
                        } catch (OperationCanceledException) {
                            requestSource.TrySetCanceled(cts.Token);
                        } catch (Exception e) {
                            requestSource.TrySetException(e);
                            completedSource.TrySetResult(0);
                        }
                        tryCount++;
                    }).Forget();

                    UniTask.Create(async () => {
                        await PromptManager.PromptCancelableWait(title, desc, promptWaiting);
                        completedSource.TrySetResult(1);
                    }).Forget();

                    int completed = await completedSource.Task;
                    if (completed != 0) {
                        cts.Cancel();
                        return ("User Cancelled", null);
                    }

                    result = await requestSource.Task;
                } else {
                    result = await UniTask.Create(() => SafeTryRequest(requestFunc)).AttachExternalCancellation(cts.Token);
                }

                {
                    (string res, JObject j) = result;
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
            }
        }
    }
}
