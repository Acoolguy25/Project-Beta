using System;
using System.Collections;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using Cysharp.Threading.Tasks;

namespace RyanAssets.Core
{
    public static class TaskHelper {
        //public static async Task<bool> AwaitTaskTimeout(Task task, int ms_timeout = 5000)
        //{
        //    CancellationTokenSource cancellationTokenSource = new();
        //    Task waitTask = Task.Run(async () =>
        //    {
        //        await Task.Delay(ms_timeout);
        //    }, cancellationTokenSource.Token);
        //    await Task.WhenAny(waitTask, task);
        //    if (waitTask.IsCompleted)
        //        return false;
        //    cancellationTokenSource.Cancel();
        //    return true;
        //}
        public static async UniTask<(bool, T)> AwaitTaskTimeout<T>(
    UniTask<T> task,
    int msTimeout = 5000,
    CancellationToken externalToken = default) {
            using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(externalToken);

            UniTask delayTask = UniTask.Delay(msTimeout, cancellationToken: cts.Token);

            var (winner, res) = await UniTask.WhenAny(task, delayTask);

            if (winner) {
                cts.Cancel(); // cancel delay
                return (true, res);  // task finished first
            }

            return (false, res); // timeout happened
        }
    }
}