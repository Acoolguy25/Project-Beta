using RyanAssets.Shared.Player;
using System.Collections;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RyanAssets.Server.ServerFeatures { 
    public static class ServerRunner {
        public static Task WaitForSceneAsync(string sceneName){
            Scene scene = SceneManager.GetSceneByName(sceneName);

            if (scene.IsValid() && scene.isLoaded)
                return Task.CompletedTask;

            var tcs = new TaskCompletionSource<bool>();

            void Handler(Scene loadedScene, LoadSceneMode mode)
            {
                if (loadedScene.name != sceneName)
                    return;

                SceneManager.sceneLoaded -= Handler;
                tcs.SetResult(true);
            }

            SceneManager.sceneLoaded += Handler;

            return tcs.Task;
        }
        public static async Task Intermission(int duration, CancellationToken token = default){
            await TimerCountdown("Intermission ({0})", duration);
        }
        public static async Task TimerCountdown(string message, int duration, CancellationToken token = default){
            for (int i = duration; i > 0; i--){
                SharedGlobalEvents.Instance.TopMessage = string.Format(message, i);
                await Awaitable.WaitForSecondsAsync(1f, token);
            }
        }
    }
}
