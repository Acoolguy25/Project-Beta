using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace RyanAssets.Progress {
    /// <summary>Where a player's document stands.</summary>
    public enum ProgressState {
        /// <summary>The store hasn't answered yet; changes are queued and applied once it does.</summary>
        Loading,
        /// <summary>The saved document (or a new player's defaults) is in memory and changes are saved.</summary>
        Loaded,
        /// <summary>
        /// The store couldn't be reached or the saved document couldn't be read. The player plays with
        /// defaults and nothing is saved this session, so a hiccup can never overwrite real progress.
        /// </summary>
        SessionOnly,
    }

    /// <summary>
    /// Holds the progress documents of the players on this server, one per player and document type,
    /// and writes the changed ones to an <see cref="IProgressStore"/>. Engine-agnostic (players are
    /// plain ids) so it can be tested without a network; the server-side <c>PlayerProgress</c> facade
    /// wires it to joins, leaves, autosave and shutdown.
    /// <para>
    /// Saves are serialized: each one captures its documents when it is requested and they are sent
    /// one request at a time, in order, so an older snapshot can never land after a newer one. A load
    /// waits for pending saves too, so a player who rejoins quickly reads what they just saved.
    /// </para>
    /// </summary>
    public sealed class ProgressBook {
        abstract class Entry {
            public readonly string PlayerId, UniverseId;
            public ProgressState State = ProgressState.Loading;
            public bool Released;
            /// <summary>Bumped by every change; equal to <see cref="SavedRevision"/> when nothing is unsaved.</summary>
            public int Revision, SavedRevision;
            /// <summary>The compact JSON last written (or loaded), so unchanged documents are never re-sent.</summary>
            public string LastSavedJson;

            protected Entry(string playerId, string universeId) {
                PlayerId = playerId;
                UniverseId = universeId;
            }

            public abstract Type DataType { get; }
            public abstract ProgressData Data { get; }
            public abstract bool HasCapture { get; }
            public abstract void RunCapture();
            public bool IsDirty => Revision != SavedRevision;
        }

        sealed class Entry<T> : Entry where T : ProgressData, new() {
            public readonly UniTaskCompletionSource<T> Completion = new();
            public readonly List<Action<T>> Pending = new();
            public Action<T> Capture;
            public T Value;

            public Entry(string playerId, string universeId) : base(playerId, universeId) { }

            public override Type DataType => typeof(T);
            public override ProgressData Data => Value;
            public override bool HasCapture => Capture != null;
            public override void RunCapture() => Capture?.Invoke(Value);
        }

        readonly struct Snapshot {
            public readonly Entry Entry;
            public readonly int Revision;
            public readonly JObject Json;
            public readonly string Text;

            public Snapshot(Entry entry, int revision, JObject json, string text) {
                Entry = entry;
                Revision = revision;
                Json = json;
                Text = text;
            }
        }

        readonly IProgressStore store;
        readonly Dictionary<string, Dictionary<Type, Entry>> players = new();
        readonly SemaphoreSlim gate = new(1, 1);

        /// <summary>Documents larger than this (compact JSON) are refused with an error instead of sent.</summary>
        public int MaxDocumentBytes { get; set; } = ProgressLimits.DefaultMaxDocumentBytes;

        public IProgressStore Store => store;

        public ProgressBook(IProgressStore store) {
            this.store = store ?? throw new ArgumentNullException(nameof(store));
        }

        // ---------------------------------------------------------------- loading and reading

        /// <summary>
        /// Starts loading a player's <typeparamref name="T"/> for <paramref name="universeId"/>, or returns
        /// the load already started. The task always completes with usable data: the saved document, a new
        /// player's defaults, or defaults in <see cref="ProgressState.SessionOnly"/> when the store failed.
        /// </summary>
        public UniTask<T> Load<T>(string playerId, string universeId) where T : ProgressData, new() {
            if (!ProgressLimits.IsValidPlayerId(playerId)) throw new ArgumentException($"Invalid player id '{playerId}'.", nameof(playerId));
            if (!ProgressLimits.IsValidUniverseId(universeId)) throw new ArgumentException($"Invalid universe id '{universeId}'.", nameof(universeId));
            if (TryFind(playerId, typeof(T), out Entry found)) {
                if (found.UniverseId != universeId)
                    throw new InvalidOperationException($"{typeof(T).Name} for {playerId} is already loaded for universe '{found.UniverseId}'.");
                return ((Entry<T>)found).Completion.Task;
            }

            var entry = new Entry<T>(playerId, universeId);
            if (!players.TryGetValue(playerId, out var entries)) players[playerId] = entries = new Dictionary<Type, Entry>();
            entries[typeof(T)] = entry;
            LoadEntry(entry).Forget();
            return entry.Completion.Task;
        }

        async UniTaskVoid LoadEntry<T>(Entry<T> entry) where T : ProgressData, new() {
            string error;
            Dictionary<string, StoredProgress> documents;
            await gate.WaitAsync();
            try {
                (error, documents) = await store.LoadAsync(entry.UniverseId, new[] { entry.PlayerId });
            } catch (Exception e) {
                error = e.Message;
                documents = null;
            } finally {
                gate.Release();
            }

            T data = null;
            ProgressState state = ProgressState.Loaded;
            bool migrated = false;
            if (error != null) {
                Debug.LogWarning($"Progress {typeof(T).Name} for {entry.PlayerId} could not be loaded ({error}). " +
                                 "Playing with defaults; nothing is saved for them this session.");
                state = ProgressState.SessionOnly;
            } else if (documents != null && documents.TryGetValue(entry.PlayerId, out StoredProgress stored)) {
                try {
                    data = ProgressCodec.Deserialize<T>(stored.Data, stored.Version);
                    migrated = stored.Version != data.LatestVersion;
                } catch (Exception e) {
                    // ProgressFormatException, or a Validate that threw on odd saved values.
                    Debug.LogError($"Progress for {entry.PlayerId} in {entry.UniverseId} is unreadable: {e.Message} " +
                                   "The saved document is left untouched; nothing is saved for them this session.");
                    state = ProgressState.SessionOnly;
                }
            }
            data ??= new T();

            entry.Value = data;
            entry.State = state;
            if (state == ProgressState.Loaded)
                // A migrated document is saved in its new shape at the next save.
                entry.LastSavedJson = migrated ? null : ProgressCodec.Serialize(data).ToString(Formatting.None);
            if (migrated) entry.Revision++;

            if (!entry.Released) {
                foreach (Action<T> change in entry.Pending) Apply(entry, change);
            }
            entry.Pending.Clear();
            entry.Completion.TrySetResult(data);
        }

        /// <summary>The player's document once it has loaded; false while loading or when never requested.</summary>
        public bool TryGet<T>(string playerId, out T data) where T : ProgressData, new() {
            if (TryFind(playerId, typeof(T), out Entry entry) && entry.State != ProgressState.Loading) {
                data = ((Entry<T>)entry).Value;
                return true;
            }
            data = null;
            return false;
        }

        /// <summary>Null when <see cref="Load{T}"/> was never called for this player.</summary>
        public ProgressState? GetState<T>(string playerId) where T : ProgressData =>
            TryFind(playerId, typeof(T), out Entry entry) ? entry.State : null;

        public bool IsTracked(string playerId) => players.ContainsKey(playerId);

        // ---------------------------------------------------------------- changing

        /// <summary>
        /// Changes a player's document and marks it for saving. While the document is still loading the
        /// change is queued and applied right after the load, so early events are never lost.
        /// Returns false (and logs) when the document was never loaded.
        /// </summary>
        public bool Modify<T>(string playerId, Action<T> change) where T : ProgressData, new() {
            if (change == null) throw new ArgumentNullException(nameof(change));
            if (!TryFind(playerId, typeof(T), out Entry found)) {
                Debug.LogWarning($"Progress {typeof(T).Name} for {playerId} was changed before Load was called; the change is dropped.");
                return false;
            }
            var entry = (Entry<T>)found;
            if (entry.State == ProgressState.Loading) entry.Pending.Add(change);
            else Apply(entry, change);
            return true;
        }

        /// <summary>Changes the <typeparamref name="T"/> of every player who has one.</summary>
        public void ModifyAll<T>(Action<T> change) where T : ProgressData, new() {
            foreach (string playerId in players.Keys.ToList())
                if (TryFind(playerId, typeof(T), out _)) Modify(playerId, change);
        }

        /// <summary>
        /// Runs <paramref name="capture"/> right before every save of this document, for games that keep
        /// their live state elsewhere and copy it into the document only when it is saved. Documents with
        /// a capture are checked at every autosave and sent only when their JSON changed.
        /// </summary>
        public void SetCapture<T>(string playerId, Action<T> capture) where T : ProgressData, new() {
            if (!TryFind(playerId, typeof(T), out Entry found))
                throw new InvalidOperationException($"Load {typeof(T).Name} for {playerId} before setting its capture.");
            ((Entry<T>)found).Capture = capture;
        }

        /// <summary>Marks a document changed after editing it directly (through <see cref="TryGet{T}"/>).</summary>
        public void MarkDirty<T>(string playerId) where T : ProgressData {
            if (TryFind(playerId, typeof(T), out Entry entry)) entry.Revision++;
        }

        static void Apply<T>(Entry<T> entry, Action<T> change) where T : ProgressData, new() {
            try {
                change(entry.Value);
            } catch (Exception e) {
                Debug.LogException(e);
            }
            entry.Revision++;
        }

        // ---------------------------------------------------------------- saving

        /// <summary>Saves every changed document of every player.</summary>
        public UniTask SaveDirty() => Send(players.Values.SelectMany(e => e.Values).ToList());

        /// <summary>Saves one player's changed documents.</summary>
        public UniTask Save(string playerId) =>
            players.TryGetValue(playerId, out var entries) ? Send(entries.Values.ToList()) : UniTask.CompletedTask;

        /// <summary>Saves one player's changed documents and forgets them (the player left).</summary>
        public UniTask Release(string playerId) {
            if (!players.Remove(playerId, out var entries)) return UniTask.CompletedTask;
            foreach (Entry entry in entries.Values) entry.Released = true;
            return Send(entries.Values.ToList());
        }

        async UniTask Send(List<Entry> entries) {
            // Capture now, synchronously: the request below may wait for an earlier save to finish.
            var snapshots = new List<Snapshot>();
            foreach (Entry entry in entries)
                if (TrySnapshot(entry, out Snapshot snapshot)) snapshots.Add(snapshot);
            if (snapshots.Count == 0) return;

            await gate.WaitAsync();
            try {
                foreach (var universe in snapshots.GroupBy(s => s.Entry.UniverseId)) {
                    List<Snapshot> group = universe.ToList();
                    for (int start = 0; start < group.Count; start += ProgressLimits.MaxBatch) {
                        List<Snapshot> batch = group.GetRange(start, Math.Min(ProgressLimits.MaxBatch, group.Count - start));
                        await SendBatch(universe.Key, batch);
                    }
                }
            } finally {
                gate.Release();
            }
        }

        async UniTask SendBatch(string universeId, List<Snapshot> batch) {
            var writes = batch.Select(s => new ProgressWrite(s.Entry.PlayerId, s.Entry.Data.LatestVersion, s.Json)).ToList();
            string error;
            try {
                error = await store.SaveAsync(universeId, writes);
            } catch (Exception e) {
                error = e.Message;
            }
            if (error != null) {
                bool lost = batch.Any(s => s.Entry.Released);
                Debug.LogWarning($"Saving progress for {string.Join(", ", batch.Select(s => s.Entry.PlayerId))} in {universeId} failed: {error}." +
                                 (lost ? " Players who already left lose this change." : " It is retried at the next save."));
                return;
            }
            foreach (Snapshot s in batch) {
                s.Entry.LastSavedJson = s.Text;
                if (s.Revision > s.Entry.SavedRevision) s.Entry.SavedRevision = s.Revision;
            }
        }

        bool TrySnapshot(Entry entry, out Snapshot snapshot) {
            snapshot = default;
            if (entry.State != ProgressState.Loaded || (!entry.IsDirty && !entry.HasCapture)) return false;
            try {
                entry.RunCapture();
            } catch (Exception e) {
                Debug.LogException(e);
                return false;
            }
            JObject json = ProgressCodec.Serialize(entry.Data);
            string text = json.ToString(Formatting.None);
            int revision = entry.Revision;
            if (text == entry.LastSavedJson) {
                entry.SavedRevision = revision;
                return false;
            }
            int size = System.Text.Encoding.UTF8.GetByteCount(text);
            if (size > MaxDocumentBytes) {
                Debug.LogError($"Progress {entry.DataType.Name} for {entry.PlayerId} is {size} bytes, over the {MaxDocumentBytes} byte limit; not saved. Keep saved data small.");
                return false;
            }
            snapshot = new Snapshot(entry, revision, json, text);
            return true;
        }

        bool TryFind(string playerId, Type type, out Entry entry) {
            entry = null;
            return playerId != null && players.TryGetValue(playerId, out var entries) && entries.TryGetValue(type, out entry);
        }
    }
}
