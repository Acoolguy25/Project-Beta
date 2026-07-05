using System.Threading;
using Cysharp.Threading.Tasks;

namespace RyanAssets.Core {
    public static class TaskHelper {
        public static async UniTask<bool> AwaitTaskTimeout(
            UniTask task,
            int msTimeout = 5000,
            CancellationToken externalToken = default) {
            using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(externalToken);

            UniTask delayTask = UniTask.Delay(msTimeout, cancellationToken: cts.Token);
            int winner = await UniTask.WhenAny(task, delayTask);

            if (winner == 0) {
                cts.Cancel();
                return true;
            }

            return false;
        }

        public static async UniTask<(bool, T)> AwaitTaskTimeout<T>(
            UniTask<T> task,
            int msTimeout = 5000,
            CancellationToken externalToken = default) {
            using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(externalToken);

            UniTask delayTask = UniTask.Delay(msTimeout, cancellationToken: cts.Token);
            var (winner, res) = await UniTask.WhenAny(task, delayTask);

            if (winner) {
                cts.Cancel();
                return (true, res);
            }

            return (false, res);
        }
    }
}
