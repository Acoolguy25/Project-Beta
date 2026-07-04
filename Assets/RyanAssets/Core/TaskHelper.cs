using System.Collections;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace RyanAssets.Core
{
    public static class TaskHelper
    {

        public static async Task<bool> AwaitTaskTimeout(Task task, int ms_timeout = 5000)
        {
            CancellationTokenSource cancellationTokenSource = new();
            Task waitTask = Task.Run(async () =>
            {
                await Task.Delay(ms_timeout);
            }, cancellationTokenSource.Token);
            await Task.WhenAny(waitTask, task);
            if (waitTask.IsCompleted)
                return false;
            cancellationTokenSource.Cancel();
            return true;
        }
    }
}