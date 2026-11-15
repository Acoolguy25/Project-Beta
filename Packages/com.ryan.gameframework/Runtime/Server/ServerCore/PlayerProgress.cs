using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using FishNet.Connection;
using RyanAssets.DataService;
using RyanAssets.NetworkService;
using RyanAssets.Progress;
using UnityEngine;

namespace RyanAssets.Server.ServerCore {
    /// <summary>
    /// Server-only player progress that survives between sessions: one small document per player and
    /// game (a <see cref="ProgressData"/> subclass), kept in UnityBackend, or in JSON files under
    /// <c>Application.persistentDataPath/PlayerProgress</c> when the Editor uses a direct local
    /// connection. Clients never read or write it; send them what they need in a broadcast.
    /// <para>
    /// Changed documents are saved every <see cref="AutosaveIntervalSeconds"/>, when their player leaves,
    /// and when the server shuts down. If the store can't be reached at join, the player gets defaults
    /// and nothing of theirs is saved that session, so a backend hiccup never wipes real progress.
    /// </para>
    /// </summary>
    /// <example><code>
    /// protected override void OnPlayerAdded(PlayerData player) {
    ///     base.OnPlayerAdded(player);
    ///     PlayerProgress.Load&lt;OB_SaveData&gt;(player, OB_SaveData.Universe).Forget();
    /// }
    /// void Finish(NetworkConnection connection, float time) =&gt;
    ///     PlayerProgress.Modify&lt;OB_SaveData&gt;(connection, save =&gt; save.RecordFinish(time));
    /// </code></example>
    public static class PlayerProgress {
        public const float DefaultAutosaveSeconds = 60f;
        const float MinimumAutosaveSeconds = 5f;

        static ProgressBook book;
        static IProgressStore storeOverride;
        static CancellationTokenSource autosaveCts;
        static float autosaveSeconds = DefaultAutosaveSeconds;

        /// <summary>Seconds between autosaves of changed documents (at least 5). Takes effect from the next wait.</summary>
        public static float AutosaveIntervalSeconds {
            get => autosaveSeconds;
            set => autosaveSeconds = Mathf.Max(MinimumAutosaveSeconds, value);
        }

        /// <summary>The documents of the players on this server. Created on first use.</summary>
        public static ProgressBook Book {
            get {
                if (book == null) {
                    IProgressStore store = storeOverride ?? CreateDefaultStore();
                    book = new ProgressBook(store);
                    StartAutosave();
                }
                return book;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() {
            StopAutosave();
            book = null;
            storeOverride = null;
            autosaveSeconds = DefaultAutosaveSeconds;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Init() {
            // PlayerData and ServerBootStrap clear their own events at SubsystemRegistration, before this.
            PlayerData.OnPlayerRemoved += OnPlayerRemoved;
            ServerBootStrap.StopServerEvent += StopAutosave;
            ServerBootStrap.StopServerAsyncEvent -= SaveAllOnShutdown;
            ServerBootStrap.StopServerAsyncEvent += SaveAllOnShutdown;
        }

        static IProgressStore CreateDefaultStore() {
            if (NetworkSettings.EditorDirectConnection) {
                var local = new LocalFileProgressStore();
                Debug.Log($"Player progress: backend disabled, saving to {local.Root}");
                return local;
            }
            return new BackendProgressStore();
        }

        /// <summary>
        /// Replaces where progress is kept (tests, tools). Call before the first <see cref="Load{T}"/>;
        /// documents already loaded from the previous store are dropped unsaved.
        /// </summary>
        public static void UseStore(IProgressStore store) {
            if (book != null && PlayerData.Players != null && PlayerData.Players.Count > 0)
                Debug.LogWarning("PlayerProgress.UseStore was called with players already loaded; their unsaved changes are dropped.");
            storeOverride = store;
            book = null;
        }

        // ---------------------------------------------------------------- per player

        /// <summary>
        /// Loads (once) the player's <typeparamref name="T"/> for <paramref name="universeId"/>. Call it
        /// when the player joins; awaiting is optional, since changes made while loading are queued.
        /// </summary>
        public static UniTask<T> Load<T>(PlayerData player, string universeId) where T : ProgressData, new() {
            if (player == null) throw new ArgumentNullException(nameof(player));
            string playerId = player.player_id.Value;
            if (!ProgressLimits.IsValidPlayerId(playerId)) {
                Debug.LogWarning($"Player '{player.username.Value}' has no valid player id ('{playerId}'); their {typeof(T).Name} is not saved.");
                return UniTask.FromResult(new T());
            }
            return Book.Load<T>(playerId, universeId);
        }

        /// <summary>The player's document once loaded (see <see cref="ProgressBook.TryGet{T}"/>).</summary>
        public static bool TryGet<T>(PlayerData player, out T data) where T : ProgressData, new() {
            data = null;
            return book != null && player != null && book.TryGet(player.player_id.Value, out data);
        }

        public static bool TryGet<T>(NetworkConnection connection, out T data) where T : ProgressData, new() {
            data = null;
            return PlayerData.TryGetPlayerData(connection, out PlayerData player) && TryGet(player, out data);
        }

        /// <summary>Changes the player's document; it is saved at the next autosave, leave or shutdown.</summary>
        public static bool Modify<T>(PlayerData player, Action<T> change) where T : ProgressData, new() =>
            book != null && player != null && book.Modify(player.player_id.Value, change);

        /// <summary>Same as <see cref="Modify{T}(PlayerData, Action{T})"/>; false when the connection has no player.</summary>
        public static bool Modify<T>(NetworkConnection connection, Action<T> change) where T : ProgressData, new() =>
            connection != null && PlayerData.TryGetPlayerData(connection, out PlayerData player) && Modify(player, change);

        /// <summary>Changes the <typeparamref name="T"/> of every player on the server.</summary>
        public static void ModifyAll<T>(Action<T> change) where T : ProgressData, new() => book?.ModifyAll(change);

        /// <summary>
        /// For games that keep live state elsewhere: <paramref name="capture"/> copies it into the
        /// document right before each save. Call after <see cref="Load{T}"/>.
        /// </summary>
        public static void CaptureBeforeSave<T>(PlayerData player, Action<T> capture) where T : ProgressData, new() {
            if (player != null && ProgressLimits.IsValidPlayerId(player.player_id.Value))
                Book.SetCapture(player.player_id.Value, capture);
        }

        /// <summary>Marks a document changed after editing the object from <see cref="TryGet{T}(PlayerData, out T)"/> directly.</summary>
        public static void MarkDirty<T>(PlayerData player) where T : ProgressData {
            if (book != null && player != null) book.MarkDirty<T>(player.player_id.Value);
        }

        /// <summary>Null when the document was never loaded for this player.</summary>
        public static ProgressState? GetState<T>(PlayerData player) where T : ProgressData =>
            book != null && player != null ? book.GetState<T>(player.player_id.Value) : null;

        /// <summary>Saves the player's changed documents now (after a milestone, say).</summary>
        public static UniTask Save(PlayerData player) =>
            book != null && player != null ? book.Save(player.player_id.Value) : UniTask.CompletedTask;

        /// <summary>Saves every changed document now.</summary>
        public static UniTask SaveAll() => book != null ? book.SaveDirty() : UniTask.CompletedTask;

        // ---------------------------------------------------------------- lifecycle

        static void OnPlayerRemoved(PlayerData player) {
            if (book == null || player == null || string.IsNullOrEmpty(player.player_id.Value)) return;
            book.Release(player.player_id.Value).Forget(e => Debug.LogException(e));
        }

        static UniTask SaveAllOnShutdown() {
            StopAutosave();
            return SaveAll();
        }

        static void StartAutosave() {
            StopAutosave();
            autosaveCts = new CancellationTokenSource();
            AutosaveLoop(autosaveCts.Token).Forget();
        }

        static void StopAutosave() {
            CancellationTokenSource cts = autosaveCts;
            autosaveCts = null;
            cts?.Cancel();
            cts?.Dispose();
        }

        static async UniTaskVoid AutosaveLoop(CancellationToken token) {
            while (!token.IsCancellationRequested) {
                try {
                    await UniTask.Delay(TimeSpan.FromSeconds(autosaveSeconds), ignoreTimeScale: true, cancellationToken: token);
                    await SaveAll();
                } catch (OperationCanceledException) {
                    break;
                } catch (Exception e) {
                    Debug.LogException(e);
                }
            }
        }
    }
}
