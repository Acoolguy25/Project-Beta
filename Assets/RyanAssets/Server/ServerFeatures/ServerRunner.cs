using RyanAssets.Shared.Player;
using System.Collections;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace RyanAssets.Server.ServerFeatures { 
    public static class ServerRunner {
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
