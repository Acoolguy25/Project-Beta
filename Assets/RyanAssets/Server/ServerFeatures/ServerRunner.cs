using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using RyanAssets.DataService;
using RyanAssets.Shared.Player;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RyanAssets.Server.ServerFeatures {
    public static class ServerRunner {
        public static UniTask WaitForSceneAsync(string sceneName) {
            Scene scene = SceneManager.GetSceneByName(sceneName);

            if (scene.IsValid() && scene.isLoaded)
                return UniTask.CompletedTask;

            var tcs = new UniTaskCompletionSource<bool>();

            void Handler(Scene loadedScene, LoadSceneMode mode) {
                if (loadedScene.name != sceneName)
                    return;

                SceneManager.sceneLoaded -= Handler;
                tcs.TrySetResult(true);
            }

            SceneManager.sceneLoaded += Handler;

            return tcs.Task;
        }

        public static async UniTask Intermission(int duration, CancellationToken token = default) {
            await TimerCountdown("Intermission ({0})", duration, token);
        }

        public static async UniTask TimerCountdown(string message, int duration, CancellationToken token = default) {
            for (int i = duration; i > 0; i--) {
                SharedGlobalEvents.Instance.TopMessage = string.Format(message, i);
                await Awaitable.WaitForSecondsAsync(1f, token);
            }
        }

        public static async UniTask CustomTimerCountdown(
    int duration,
    Func<int, bool, bool> activationFunc,
    Func<Action, Action> registerInterrupt,
    CancellationToken token = default) {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(token);

            Action interrupt = () =>
            {
                if (cts.IsCancellationRequested)
                    return;

                activationFunc(duration, true);
                cts.Cancel();
            };

            Action unregisterInterrupt = registerInterrupt(interrupt);

            try {
                while (duration >= 0) {
                    if (!activationFunc(duration, false))
                        return;

                    await UniTask.Delay(6000, cancellationToken: cts.Token);

                    duration--;
                }
            }
            catch (OperationCanceledException) {
            }
            finally {
                unregisterInterrupt();
            }
        }
        public static void ResetLeaderboardData() {
            foreach (PlayerData playerData in PlayerData.Players.Values) {
                for (int i = 0; i <  playerData.leaderboard.Count; i++) {
                    playerData.leaderboard[i] = 0;
                }
            }
        }
        public static List<PlayerData> GetLeaderboardWinner(string leaderboardName) {
            int leaderboardIdx = SharedGlobalEvents.GetLeaderboardIndex(leaderboardName);
            return PlayerData.Players.Values.OrderByDescending(p => p.leaderboard[leaderboardIdx]).ToList();
        }
    }
}
