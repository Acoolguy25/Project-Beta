using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using FishNet;
using FishNet.Connection;
using NUnit.Framework;
using RyanAssets.Characters.Shared;
using RyanAssets.Core;
using RyanAssets.DataService;
using RyanAssets.Server.ServerCore;
using RyanAssets.Shared.Player;
using UnityEngine;
using UnityEngine.SceneManagement;

#pragma warning disable CS1998
namespace RyanAssets.Server.ServerFeatures {
    public class ServerRunner: MonoBehaviour {
        public static event Action OnResetEvent;
        public static bool serverRunning => serverRunnerCTS != null && !serverRunnerCTS.IsCancellationRequested;
        protected static CancellationTokenSource serverRunnerCTS = null;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Init() {
            OnResetEvent = null;
        }
        public UniTask WaitForSceneAsync(string sceneName, CancellationToken token = default) {
            Scene scene = SceneManager.GetSceneByName(sceneName);

            if (scene.IsValid() && scene.isLoaded)
                return UniTask.CompletedTask;

            var tcs = new UniTaskCompletionSource();

            void Handler(Scene loadedScene, LoadSceneMode mode) {
                if (loadedScene.name != sceneName)
                    return;

                SceneManager.sceneLoaded -= Handler;
                tcs.TrySetResult();
            }

            SceneManager.sceneLoaded += Handler;

            token.Register(() =>
            {
                SceneManager.sceneLoaded -= Handler;
                tcs.TrySetCanceled(token);
            });

            return tcs.Task;
        }

        public async UniTask Intermission(int duration, CancellationToken token = default) {
            await TimerCountdown("Intermission ({0})", duration, token);
        }

        public async UniTask TimerCountdown(string message, int duration, CancellationToken token = default) {
            for (int i = duration; i > 0; i--) {
                SharedGlobalEvents.Instance.TopMessage = string.Format(message, i);
                await Awaitable.WaitForSecondsAsync(1f, token);
            }
        }

        public async UniTask CustomTimerCountdown(
            int duration,
            Func<int, bool, bool> activationFunc,
            Func<Action, Action> registerInterrupt,
            CancellationToken token = default) {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
            bool loopExited = false;
            bool interruptRegistered = false;
            Action unregisterInterrupt = null;

            void UnregisterInterrupt() {
                if (!interruptRegistered)
                    return;

                // Mark it inactive before invoking the unregister callback. This also
                // prevents a registration callback that returns the event delegate
                // itself from recursively invoking the interrupt action.
                interruptRegistered = false;
                unregisterInterrupt?.Invoke();
            }

            Action interrupt = () => {
                if (!interruptRegistered || cts.IsCancellationRequested || loopExited)
                    return;

                if (!activationFunc(duration, true)) {
                    cts.Cancel();
                    UnregisterInterrupt();
                }
            };

            try {
                unregisterInterrupt = registerInterrupt(interrupt);
                interruptRegistered = true;

                while (duration >= 0) {
                    if (!activationFunc(duration, false))
                        return;

                    await UniTask.Delay(1000, cancellationToken: cts.Token);

                    duration--;
                }
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested) {
            }
            finally {
                loopExited = true;
                UnregisterInterrupt();
            }
        }
        public void ResetLeaderboardData() {
            foreach (PlayerData playerData in PlayerData.Players.Values) {
                for (int i = 0; i < playerData.leaderboard.Count; i++) {
                    playerData.leaderboard[i] = 0;
                }
            }
        }
        public List<PlayerData> GetLeaderboardWinner(string leaderboardName) {
            int leaderboardIdx = SharedGlobalEvents.GetLeaderboardIndex(leaderboardName);
            return PlayerData.Players.Values.OrderByDescending(p => p.leaderboard[leaderboardIdx]).ToList();
        }

        public void SetGlobalInvul(bool enabled) {
            SharedGlobalEvents.Instance.GlobalInvul = enabled;
        }
        public void SetTeamKillEnabled(bool enabled) {
            SharedGlobalEvents.Instance.TeamKillEnabled = enabled;
        }

        public int GetActivePlayers() {
            return InstanceFinder.ServerManager.Clients.Values.Count((conn) => conn.IsActive && conn.IsAuthenticated);
        }
        public async UniTask WaitForPlayersAsync(int players = 1, CancellationToken token = default) {
            while (GetActivePlayers() < players) {
                await TaskHelper.WaitForAction<NetworkConnection, LocalCharacter>(
                    h => ServerPlayerCharacter.OnPlayerCharacterAdded += h,
                    h => ServerPlayerCharacter.OnPlayerCharacterAdded -= h,
                    token
                );
            }
        }

        protected virtual void Awake() {
            DontDestroyOnLoad(gameObject);
            ServerIdleTimeout.OnIdleTimeoutStarted += Restart;
            ServerBootStrap.RestartServerEvent += Restart;
        }

        protected virtual void Start() {
            var cts = new CancellationTokenSource();
            serverRunnerCTS = cts;
            StartAsync(cts.Token).Forget();
        }

        protected virtual async UniTask StartAsync(CancellationToken token) {
            await WaitForSceneAsync(ServerBootStrap.serverInfo.universe_id + "_start", token); // Wait for start scene to load
            await WaitForPlayersAsync(1, token);
        }

        protected virtual void Stop() {
            serverRunnerCTS?.Cancel();
        }

        protected virtual void Reset() {
            OnResetEvent?.Invoke();
            ServerBootStrap.LoadInitialScene();
        }

        protected virtual void Restart() {
            Stop();
            Reset();
            Start();
        }

        protected virtual void OnDestroy() {
            ServerIdleTimeout.OnIdleTimeoutStarted -= Restart;
        }
    }
}
#pragma warning restore CS1998
