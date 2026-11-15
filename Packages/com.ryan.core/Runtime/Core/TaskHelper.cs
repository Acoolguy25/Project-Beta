using Cysharp.Threading.Tasks;
using System;
using System.Threading;

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

        public static UniTask WaitForAction<T>(
    Action<Action<T>> subscribe,
    Action<Action<T>> unsubscribe,
    CancellationToken token = default) {
            var tcs = new UniTaskCompletionSource();

            Action<T> handler = null;

            handler = (val) =>
            {
                unsubscribe(handler);
                tcs.TrySetResult();
            };

            subscribe(handler);

            if (token.CanBeCanceled) {
                token.Register(() =>
                {
                    unsubscribe(handler);
                    tcs.TrySetCanceled(token);
                });
            }

            return tcs.Task;
        }

        public static UniTask WaitForAction<T1, T2>(
    Action<Action<T1, T2>> subscribe,
    Action<Action<T1, T2>> unsubscribe,
    CancellationToken token = default) {
            var tcs = new UniTaskCompletionSource();

            Action<T1, T2> handler = null;

            handler = (_, _) =>
            {
                unsubscribe(handler);
                tcs.TrySetResult();
            };

            subscribe(handler);

            if (token.CanBeCanceled) {
                token.Register(() =>
                {
                    unsubscribe(handler);
                    tcs.TrySetCanceled(token);
                });
            }

            return tcs.Task;
        }
    }
}
