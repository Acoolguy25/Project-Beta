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
using RyanAssets.Shared.Global;
using RyanAssets.Shared.Declarations;
using UnityEngine;
using UnityEngine.SceneManagement;

#pragma warning disable CS1998
namespace RyanAssets.Server.ServerFeatures {
    public class ServerRunner : MonoBehaviour {
        [SerializeField]
        protected DebugBool DebugTimerSpeedUp, DebugTimerInfinite;
        /// <summary>
        /// How much faster <see cref="DebugTimerSpeedUp"/> runs every wait this runner performs.
        /// <para>
        /// The speed-up used to be a hardcoded tenth, which made it an all-or-nothing switch: fine
        /// for skipping an intermission, useless for watching a round at double pace. The factor is
        /// authored here instead, and 10 keeps the original behaviour for a scene that already had
        /// the switch on.
        /// </para>
        /// </summary>
        [SerializeField]
        [Tooltip("Editor only. How much faster DebugTimerSpeedUp runs the clock: 10 gives the " +
                 "original tenth-length timers, 2 runs the round at double pace.")]
        protected DebugFloat DebugTimerSpeed = new(10f);
        public static event Action OnResetEvent;
        public static bool serverRunning => serverRunnerCTS != null && !serverRunnerCTS.IsCancellationRequested;
        public static ServerRunner Instance;
        protected static CancellationTokenSource serverRunnerCTS = null;
        // Cancelled by /skip to end the current wait/countdown immediately.
        static CancellationTokenSource skipCTS = new CancellationTokenSource();
        event Action UpdateGameBarEvent;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Init() {
            OnResetEvent = null;
            CancellationTokenSource cts = serverRunnerCTS;
            serverRunnerCTS = null;
            cts?.Cancel();
            cts?.Dispose();
            CancellationTokenSource skip = skipCTS;
            skipCTS = new CancellationTokenSource();
            skip?.Cancel();
            skip?.Dispose();
            Instance = null;
        }

        /// <summary>Ends the wait or countdown that is currently running, as if its time ran out.</summary>
        public static void SkipTimer() {
            CancellationTokenSource skip = skipCTS;
            skipCTS = new CancellationTokenSource();
            skip?.Cancel();
            skip?.Dispose();
        }

        // TIMER FUNCTIONS
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

            token.Register(() => {
                SceneManager.sceneLoaded -= Handler;
                tcs.TrySetCanceled(token);
            });

            return tcs.Task;
        }
        /// <summary>Waits out a timer slice. Returns true when <see cref="SkipTimer"/> ended the wait early.</summary>
        public async UniTask<bool> AwaitTime(int durationMs, CancellationToken cts = default) {
            int time2Sleep = DebugTimerInfinite.Value ? int.MaxValue
                : DebugTimerSpeedUp.Value ? Mathf.Max(1, Mathf.RoundToInt(durationMs / DebugTimerSpeed.Value))
                : durationMs;
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cts, skipCTS.Token);
            try {
                await UniTask.Delay(time2Sleep, cancellationToken: linked.Token);
            }
            catch (OperationCanceledException) when (!cts.IsCancellationRequested) {
                // A skip ended this wait. The owning token still cancels normally.
                return true;
            }
            return false;
        }

        public async UniTask Intermission(int duration, CancellationToken token = default) {
            await TimerCountdown("Intermission ({0})", duration, token);
        }

        public async UniTask TimerCountdown(string message, int duration, CancellationToken token = default) {
            for (int i = duration; i > 0; i--) {
                SharedGlobalEvents.Instance.TopMessage = string.Format(message, i);
                if (await AwaitTime(1000, token))
                    return;
            }
        }

        public async UniTask<bool> CustomTimerCountdown(
            int duration,
            Func<int, bool, bool> activationFunc,
            Action<Action> registerInterrupt,
            Action<Action> unregisterInterrupt,
            CancellationToken token = default) {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
            bool interruptRegistered = false;

            Action interrupt = () => {
                if (!interruptRegistered || cts.IsCancellationRequested)
                    return;

                if (!activationFunc(duration, true))
                    cts.Cancel();
            };

            try {
                registerInterrupt(interrupt);
                interruptRegistered = true;

                while (duration != 0) {
                    if (!activationFunc(duration, false))
                        return true;

                    if (await AwaitTime(1000, cts.Token))
                        break;

                    duration--;
                }
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested) {
                // An interrupt ended this countdown. Cancellation of the owning
                // runner token still propagates and stops the old round entirely.
            } finally {
                if (interruptRegistered) {
                    interruptRegistered = false;
                    unregisterInterrupt(interrupt);
                }
            }
            return token.IsCancellationRequested;
        }

        /// <summary>
        /// Runs the standard active-game countdown. Derived runners override
        /// <see cref="UpdateInGameBar"/> to publish their mode-specific status and
        /// return false when their game should end early.
        /// </summary>
        protected UniTask<bool> GameTimerCountdown(int duration, CancellationToken token = default) {
            return CustomTimerCountdown(
                duration,
                UpdateInGameBar,
                interrupt => UpdateGameBarEvent += interrupt,
                interrupt => UpdateGameBarEvent -= interrupt,
                token);
        }

        /// <summary>Refreshes the active-game countdown after game-state changes.</summary>
        protected void RefreshInGameBar() {
            UpdateGameBarEvent?.Invoke();
        }

        protected virtual bool UpdateInGameBar(int durationLeft, bool interrupted) {
            SetTopMessage($"Game in progress ({durationLeft})");
            return true;
        }

        public async UniTask WaitForPlayersAsync(int playerRequirement = 1, CancellationToken token = default) {
            int activePlayers;
            while ((activePlayers = GetActivePlayersCount()) < playerRequirement) {
                SetTopMessage($"Waiting for players ({activePlayers}/{playerRequirement})");
                await TaskHelper.WaitForAction<PlayerData>(
                    h => PlayerData.OnPlayerAdded += h,
                    h => PlayerData.OnPlayerAdded -= h,
                    token
                );
            }
            SetTopMessage($"Players connected ({activePlayers}/{playerRequirement})");
        }

        // LEADERBOARD FUNCTIONS
        public List<PlayerData> GetLeaderboardWinner(string leaderboardName) {
            int leaderboardIdx = SharedGlobalEvents.GetLeaderboardIndex(leaderboardName);
            return PlayerData.Players.Values.OrderByDescending(p => p.leaderboard[leaderboardIdx]).ToList();
        }
        public void SetLeaderboardEnabled(string value, bool enabled) {
            int lastIdx = SharedGlobalEvents.GetLeaderboardIndex(value);
            bool wasEnabled = lastIdx >= 0;
            if (wasEnabled == enabled) {
                return;
            }
            if (enabled) {
                foreach (PlayerData player in PlayerData.Players.Values) {
                    player.leaderboard.AddRange(new[] { 0 });
                }
            } else if (wasEnabled) {
                foreach (PlayerData player in PlayerData.Players.Values) {
                    player.leaderboard.RemoveAt(lastIdx);
                }
            }
            SharedGlobalEvents.Instance.LeaderboardHeaders.Remove(value);
            if (enabled)
                SharedGlobalEvents.Instance.LeaderboardHeaders.AddRange(new[] { value });
        }
        public bool GetLeaderboardEnabled(string value) {
            return SharedGlobalEvents.Instance.LeaderboardHeaders.Contains(value);
        }
        public void ClearLeaderboard() {
            foreach (PlayerData playerData in PlayerData.Players.Values) {
                playerData.leaderboard.Clear();
            }
            SharedGlobalEvents.Instance.LeaderboardHeaders.Clear();
        }
        public void ResetLeaderboardData() {
            foreach (PlayerData playerData in PlayerData.Players.Values) {
                for (int i = 0; i < playerData.leaderboard.Count; i++) {
                    playerData.leaderboard[i] = 0;
                }
            }
        }
        // Teams
        public void SetPlayerTeams(TeamConfig teamColor) {
            foreach (PlayerData playerData in PlayerData.Players.Values) {
                playerData.SetPlayerTeam(teamColor);
            }
        }
        
        // Baby functions
        protected virtual void OnPlayerAdded(PlayerData playerData) {
            playerData.leaderboard.AddRange(Enumerable.Repeat(0, SharedGlobalEvents.Instance.LeaderboardHeaders.Count));
        }
        protected virtual void OnCharacterAdded(LocalCharacter localCharacter) {

        }
        public static void SetTopMessage(string topMessage) {
            SharedGlobalEvents.Instance.TopMessage = topMessage;
        }
        public void SetGlobalInvul(bool enabled) {
            SharedGlobalEvents.Instance.GlobalInvul = enabled;
        }
        public void SetTeamKillEnabled(bool enabled) {
            SharedGlobalEvents.Instance.TeamKillEnabled = enabled;
        }
        public PlayerData[] GetActivePlayers() {
            return PlayerData.Players.Values.Where(player => player.Owner.IsActive && player.Owner.IsAuthenticated && player.Owner.LoadedStartScenes()).ToArray();
        }
        public int GetActivePlayersCount() {
            return GetActivePlayers().Length;
        }

        // LIFECYCLE FUNCTIONS

        protected virtual void Awake() {
            Instance = this;
            DontDestroyOnLoad(gameObject);
            PlayerData.OnPlayerAdded += OnPlayerAdded;
            ServerPlayerCharacter.CharacterAdded += OnCharacterAdded;
            ServerIdleTimeout.OnIdleTimeoutStarted += Restart;
            ServerBootStrap.RestartServerEvent += Restart;
        }

        protected virtual void Start() {
            CancellationTokenSource previousCts = serverRunnerCTS;
            var cts = new CancellationTokenSource();
            serverRunnerCTS = cts;
            previousCts?.Cancel();
            previousCts?.Dispose();
            RunRoundAsync(cts).Forget(exception => Debug.LogException(exception, this));
        }

        async UniTask RunRoundAsync(CancellationTokenSource cts) {
            CancellationToken token = cts.Token;
            try {
                await StartAsync(token);
                // The concrete base runner has no round to restart. Derived runners
                // await base.StartAsync only for scene/player readiness.
                if (GetType() == typeof(ServerRunner))
                    await UniTask.WaitUntilCanceled(token);

                // Even an immediately completed round must not recurse into Start.
                await UniTask.Yield(PlayerLoopTiming.Update, token);
                // A previous round can finish after a manual restart has installed a
                // replacement token. Only the currently-owned round may start another
                // restart cycle.
                if (serverRunnerCTS == cts && !token.IsCancellationRequested)
                    Restart();
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) {
                // Stopping or replacing this round is expected.
            }
            catch (Exception exception) {
                Debug.LogException(exception, this);
                // A failed round is not running. Never stop a replacement round
                // or automatically retry a broken setup every frame.
                if (serverRunnerCTS == cts)
                    Stop();
            }
        }

        protected virtual async UniTask StartAsync(CancellationToken token) {
            await WaitForSceneAsync(ServerBootStrap.serverInfo.universe_id + "_start", token); // Wait for start scene to load
            await WaitForPlayersAsync(1, token);
        }

        protected virtual void Stop() {
            CancellationTokenSource cts = serverRunnerCTS;
            serverRunnerCTS = null;
            cts?.Cancel();
            cts?.Dispose();
        }

        protected virtual void Reset() {
            SetGlobalInvul(true);
            SetTeamKillEnabled(true);
            OnResetEvent?.Invoke();
            ClearLeaderboard();
            ServerTool.ClearFloatingTools();
            // TODO: Implement reset for resetting the scenes, but only if needed
            //ServerBootStrap.LoadInitialScene();
        }

        protected virtual void Restart() {
            Stop();
            Reset();
            Start();
        }

        protected virtual void OnDestroy() {
            PlayerData.OnPlayerAdded -= OnPlayerAdded;
            ServerPlayerCharacter.CharacterAdded -= OnCharacterAdded;
            ServerIdleTimeout.OnIdleTimeoutStarted -= Restart;
            ServerBootStrap.RestartServerEvent -= Restart;
            if (Instance == this) {
                Stop();
                Instance = null;
            }
        }
    }
}
#pragma warning restore CS1998
