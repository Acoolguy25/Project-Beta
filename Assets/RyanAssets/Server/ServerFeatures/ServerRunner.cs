using System.Threading;
using Cysharp.Threading.Tasks;
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
    }
}
