using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using RyanAssets.Progress;
using UnityEngine;
using UnityEngine.TestTools;

namespace RyanAssets.Progress.Tests {
    public class ProgressTests {
        // ------------------------------------------------------------ fixtures

        public struct SamplePerks {
            public byte speed, vault;
        }

        /// <summary>Version 2 renamed "gold" to "coins".</summary>
        public sealed class SampleSave : ProgressData {
            public override int LatestVersion => 2;
            public int coins;
            public string title = "rookie";
            public List<int> unlocked = new() { 1 };
            public Dictionary<string, int> wins = new();
            public SamplePerks perks;
            [NonSerialized] public int notSaved;
            public int Doubled => coins * 2;

            public override void Migrate(int fromVersion, JObject stored) {
                if (fromVersion < 2 && stored["gold"] != null) coins = stored.Value<int>("gold");
            }

            public override void Validate() => coins = Math.Max(0, coins);
        }

        public sealed class OtherSave : ProgressData {
            public override int LatestVersion => 1;
            public int wins;
        }

        /// <summary>An in-memory store that can fail or hold its answers back.</summary>
        sealed class MemoryStore : IProgressStore {
            public readonly Dictionary<(string universe, string player), StoredProgress> Documents = new();
            public int LoadCalls, SaveCalls, Writes;
            public string Fail;
            public UniTaskCompletionSource HoldLoads, HoldSaves;

            public void Put(string universe, string player, int version, JObject data) =>
                Documents[(universe, player)] = new StoredProgress(version, data);

            public JObject Get(string universe, string player) =>
                Documents.TryGetValue((universe, player), out StoredProgress stored) ? stored.Data : null;

            public async UniTask<(string error, Dictionary<string, StoredProgress> documents)> LoadAsync(
                string universeId, IReadOnlyList<string> playerIds, CancellationToken token = default) {
                LoadCalls++;
                if (HoldLoads != null) await HoldLoads.Task;
                if (Fail != null) return (Fail, null);
                var result = new Dictionary<string, StoredProgress>();
                foreach (string id in playerIds)
                    if (Documents.TryGetValue((universeId, id), out StoredProgress stored))
                        result[id] = new StoredProgress(stored.Version, (JObject)stored.Data.DeepClone());
                return (null, result);
            }

            public async UniTask<string> SaveAsync(string universeId, IReadOnlyList<ProgressWrite> writes, CancellationToken token = default) {
                SaveCalls++;
                if (HoldSaves != null) await HoldSaves.Task;
                if (Fail != null) return Fail;
                foreach (ProgressWrite write in writes) {
                    Writes++;
                    Put(universeId, write.PlayerId, write.Version, (JObject)write.Data.DeepClone());
                }
                return null;
            }
        }

        const string Universe = "test_universe";
        const string Player = "player_1";

        static T Run<T>(UniTask<T> task) {
            Assert.AreEqual(UniTaskStatus.Succeeded, task.Status, "expected the task to finish synchronously");
            return task.GetAwaiter().GetResult();
        }

        static void Run(UniTask task) {
            Assert.AreEqual(UniTaskStatus.Succeeded, task.Status, "expected the task to finish synchronously");
            task.GetAwaiter().GetResult();
        }

        // ------------------------------------------------------------ codec

        [Test]
        public void CodecRoundTripsPublicFieldsOnly() {
            var save = new SampleSave { coins = 7, title = "pro", notSaved = 99, perks = new SamplePerks { speed = 2 } };
            save.unlocked.Add(4);
            save.wins["sheriff"] = 3;

            JObject json = ProgressCodec.Serialize(save);
            Assert.AreEqual(2, json.Value<int>("version"), "the latest version is stamped");
            Assert.IsNull(json["Doubled"], "properties are not saved");
            Assert.IsNull(json["LatestVersion"]);
            Assert.IsNull(json["notSaved"], "[NonSerialized] fields are not saved");

            SampleSave back = ProgressCodec.Deserialize<SampleSave>(json, 2);
            Assert.AreEqual(7, back.coins);
            Assert.AreEqual("pro", back.title);
            CollectionAssert.AreEqual(new[] { 1, 4 }, back.unlocked, "saved lists replace the default instead of appending");
            Assert.AreEqual(3, back.wins["sheriff"]);
            Assert.AreEqual(2, back.perks.speed);
            Assert.AreEqual(0, back.notSaved);
        }

        [Test]
        public void NewPlayerGetsDefaults() {
            SampleSave save = ProgressCodec.Deserialize<SampleSave>(null, 0);
            Assert.AreEqual(2, save.version);
            Assert.AreEqual("rookie", save.title);
            CollectionAssert.AreEqual(new[] { 1 }, save.unlocked);
        }

        [Test]
        public void MissingFieldsKeepTheirDefaultsAndUnknownOnesAreIgnored() {
            var stored = JObject.Parse("{\"version\":2,\"coins\":3,\"removedLongAgo\":true,\"$type\":\"System.IO.File, mscorlib\"}");
            SampleSave save = ProgressCodec.Deserialize<SampleSave>(stored, 2);
            Assert.AreEqual(3, save.coins);
            Assert.AreEqual("rookie", save.title);
        }

        [Test]
        public void OlderVersionsMigrate() {
            SampleSave save = ProgressCodec.Deserialize<SampleSave>(JObject.Parse("{\"version\":1,\"gold\":40}"), 1);
            Assert.AreEqual(40, save.coins);
            Assert.AreEqual(2, save.version, "a migrated document carries the latest version");
        }

        [Test]
        public void ValidateRepairsLoadedValues() {
            SampleSave save = ProgressCodec.Deserialize<SampleSave>(JObject.Parse("{\"coins\":-5}"), 2);
            Assert.AreEqual(0, save.coins);
        }

        [Test]
        public void NewerOrMalformedDocumentsAreRejected() {
            Assert.Throws<ProgressFormatException>(() => ProgressCodec.Deserialize<SampleSave>(JObject.Parse("{\"coins\":1}"), 3));
            Assert.Throws<ProgressFormatException>(() => ProgressCodec.Deserialize<SampleSave>(JObject.Parse("{\"coins\":\"lots\"}"), 2));
            Assert.Throws<ProgressFormatException>(() => ProgressCodec.Deserialize<SampleSave>(JObject.Parse("{\"unlocked\":{\"a\":1}}"), 2));
        }

        [Test]
        public void IdsFollowTheBackendRules() {
            Assert.IsTrue(ProgressLimits.IsValidPlayerId("editor-0123456789abcdef0123456789abcdef"));
            Assert.IsTrue(ProgressLimits.IsValidUniverseId("tower_defense"));
            foreach (string bad in new[] { null, "", "../x", "a/b", "a b", "a\n", new string('x', 129) })
                Assert.IsFalse(ProgressLimits.IsValidPlayerId(bad), $"'{bad}' should be rejected");
            Assert.IsFalse(ProgressLimits.IsValidUniverseId(new string('x', 65)));
        }

        // ------------------------------------------------------------ local file store

        string root;

        [SetUp]
        public void CreateRoot() => root = Path.Combine(Path.GetTempPath(), "ProgressTests-" + Guid.NewGuid().ToString("N"));

        [TearDown]
        public void DeleteRoot() {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }

        [Test]
        public void LocalStoreSavesAndLoads() {
            var store = new LocalFileProgressStore(root);
            var first = new JObject { ["coins"] = 1 };
            Assert.IsNull(Run(store.SaveAsync(Universe, new[] { new ProgressWrite(Player, 2, first) })));
            Assert.IsNull(Run(store.SaveAsync(Universe, new[] { new ProgressWrite(Player, 2, new JObject { ["coins"] = 5 }) })));

            (string error, Dictionary<string, StoredProgress> documents) = Run(store.LoadAsync(Universe, new[] { Player, "nobody" }));
            Assert.IsNull(error);
            Assert.AreEqual(1, documents.Count, "players with nothing saved are absent");
            Assert.AreEqual(2, documents[Player].Version);
            Assert.AreEqual(5, documents[Player].Data.Value<int>("coins"), "a save overwrites the previous one");
            Assert.IsTrue(File.Exists(Path.Combine(root, Universe, Player + ".json")));
            Assert.IsFalse(File.Exists(Path.Combine(root, Universe, Player + ".json.tmp")));
        }

        [Test]
        public void LocalStoreRefusesIdsThatCouldEscapeItsFolder() {
            var store = new LocalFileProgressStore(root);
            Assert.IsNotNull(Run(store.SaveAsync(Universe, new[] { new ProgressWrite("../../evil", 1, new JObject()) })));
            Assert.IsNotNull(Run(store.SaveAsync("../up", new[] { new ProgressWrite(Player, 1, new JObject()) })));
            Assert.IsNotNull(Run(store.LoadAsync(Universe, new[] { "..\\evil" })).error);
            Assert.IsFalse(Directory.Exists(Path.Combine(root, "..", "up")));
        }

        [Test]
        public void LocalStoreReportsACorruptFile() {
            var store = new LocalFileProgressStore(root);
            Directory.CreateDirectory(Path.Combine(root, Universe));
            File.WriteAllText(Path.Combine(root, Universe, Player + ".json"), "{not json");
            Assert.IsNotNull(Run(store.LoadAsync(Universe, new[] { Player })).error);
        }

        [Test]
        public void BookWorksEndToEndOnTheLocalStore() {
            var book = new ProgressBook(new LocalFileProgressStore(root));
            Run(book.Load<SampleSave>(Player, Universe));
            book.Modify<SampleSave>(Player, s => s.coins = 12);
            Run(book.Release(Player));

            var again = new ProgressBook(new LocalFileProgressStore(root));
            Assert.AreEqual(12, Run(again.Load<SampleSave>(Player, Universe)).coins);
        }

        // ------------------------------------------------------------ book

        [Test]
        public void NewPlayersAreNotWrittenUntilSomethingChanges() {
            var store = new MemoryStore();
            var book = new ProgressBook(store);
            SampleSave save = Run(book.Load<SampleSave>(Player, Universe));
            Assert.AreEqual("rookie", save.title);
            Assert.AreEqual(ProgressState.Loaded, book.GetState<SampleSave>(Player));
            Assert.AreSame(save, Run(book.Load<SampleSave>(Player, Universe)), "loading again returns the same document");
            Assert.AreEqual(1, store.LoadCalls);

            Run(book.SaveDirty());
            Assert.AreEqual(0, store.SaveCalls);
        }

        [Test]
        public void ChangesAreSavedOnceAndOnlyWhenTheyDiffer() {
            var store = new MemoryStore();
            var book = new ProgressBook(store);
            Run(book.Load<SampleSave>(Player, Universe));

            Assert.IsTrue(book.Modify<SampleSave>(Player, s => s.coins += 5));
            Run(book.SaveDirty());
            Run(book.SaveDirty());
            Assert.AreEqual(1, store.Writes);
            Assert.AreEqual(5, store.Get(Universe, Player).Value<int>("coins"));
            Assert.AreEqual(2, store.Documents[(Universe, Player)].Version);

            book.Modify<SampleSave>(Player, s => s.coins += 0);
            Run(book.SaveDirty());
            Assert.AreEqual(1, store.Writes, "a change that leaves the JSON identical is not re-sent");
        }

        [Test]
        public void DocumentTypesAndPlayersAreSeparate() {
            var store = new MemoryStore();
            var book = new ProgressBook(store);
            Run(book.Load<SampleSave>(Player, Universe));
            Run(book.Load<OtherSave>(Player, "other_universe"));
            Run(book.Load<SampleSave>("player_2", Universe));
            book.Modify<OtherSave>(Player, s => s.wins = 2);
            book.ModifyAll<SampleSave>(s => s.coins = 9);
            Run(book.SaveDirty());

            Assert.AreEqual(2, store.Get("other_universe", Player).Value<int>("wins"));
            Assert.AreEqual(9, store.Get(Universe, Player).Value<int>("coins"));
            Assert.AreEqual(9, store.Get(Universe, "player_2").Value<int>("coins"));
            Assert.Throws<InvalidOperationException>(() => book.Load<OtherSave>(Player, Universe));
        }

        [Test]
        public void ChangesMadeWhileLoadingAreAppliedAfterTheLoad() {
            var store = new MemoryStore { HoldLoads = new UniTaskCompletionSource() };
            store.Put(Universe, Player, 2, new JObject { ["coins"] = 10 });
            var book = new ProgressBook(store);

            UniTask<SampleSave> load = book.Load<SampleSave>(Player, Universe);
            Assert.AreEqual(ProgressState.Loading, book.GetState<SampleSave>(Player));
            Assert.IsFalse(book.TryGet(Player, out SampleSave _));
            book.Modify<SampleSave>(Player, s => s.coins += 1);

            store.HoldLoads.TrySetResult();
            Assert.AreEqual(11, Run(load).coins);
            Run(book.SaveDirty());
            Assert.AreEqual(11, store.Get(Universe, Player).Value<int>("coins"));
        }

        [Test]
        public void AnUnreachableStoreNeverOverwritesRealProgress() {
            var store = new MemoryStore { Fail = "connection refused" };
            store.Put(Universe, Player, 2, new JObject { ["coins"] = 500 });
            var book = new ProgressBook(store);

            SampleSave save = Run(book.Load<SampleSave>(Player, Universe));
            Assert.AreEqual(0, save.coins, "defaults while the store is down");
            Assert.AreEqual(ProgressState.SessionOnly, book.GetState<SampleSave>(Player));

            store.Fail = null;
            book.Modify<SampleSave>(Player, s => s.coins = 1);
            Run(book.Release(Player));
            Assert.AreEqual(0, store.SaveCalls);
            Assert.AreEqual(500, store.Get(Universe, Player).Value<int>("coins"));
        }

        [Test]
        public void AnUnreadableDocumentIsLeftUntouched() {
            var store = new MemoryStore();
            store.Put(Universe, Player, 9, new JObject { ["coins"] = 3 });
            var book = new ProgressBook(store);

            LogAssert.Expect(LogType.Error, new Regex("unreadable"));
            Run(book.Load<SampleSave>(Player, Universe));
            Assert.AreEqual(ProgressState.SessionOnly, book.GetState<SampleSave>(Player));
            book.Modify<SampleSave>(Player, s => s.coins = 1);
            Run(book.SaveDirty());
            Assert.AreEqual(0, store.SaveCalls);
        }

        [Test]
        public void MigratedDocumentsAreRewrittenInTheirNewShape() {
            var store = new MemoryStore();
            store.Put(Universe, Player, 1, new JObject { ["gold"] = 25 });
            var book = new ProgressBook(store);
            Assert.AreEqual(25, Run(book.Load<SampleSave>(Player, Universe)).coins);

            Run(book.SaveDirty());
            Assert.AreEqual(2, store.Documents[(Universe, Player)].Version);
            Assert.AreEqual(25, store.Get(Universe, Player).Value<int>("coins"));
        }

        [Test]
        public void FailedSavesAreRetried() {
            var store = new MemoryStore();
            var book = new ProgressBook(store);
            Run(book.Load<SampleSave>(Player, Universe));
            book.Modify<SampleSave>(Player, s => s.coins = 4);

            store.Fail = "backend down";
            Run(book.SaveDirty());
            Assert.IsNull(store.Get(Universe, Player));

            store.Fail = null;
            Run(book.SaveDirty());
            Assert.AreEqual(4, store.Get(Universe, Player).Value<int>("coins"));
        }

        [Test]
        public void ReleaseSavesAndForgetsThePlayer() {
            var store = new MemoryStore();
            var book = new ProgressBook(store);
            Run(book.Load<SampleSave>(Player, Universe));
            book.Modify<SampleSave>(Player, s => s.coins = 8);

            Run(book.Release(Player));
            Assert.AreEqual(8, store.Get(Universe, Player).Value<int>("coins"));
            Assert.IsFalse(book.IsTracked(Player));
            Assert.IsNull(book.GetState<SampleSave>(Player));
            LogAssert.Expect(LogType.Warning, new Regex("before Load"));
            Assert.IsFalse(book.Modify<SampleSave>(Player, s => s.coins = 9));

            Assert.AreEqual(8, Run(book.Load<SampleSave>(Player, Universe)).coins, "a rejoining player gets what was saved");
        }

        [Test]
        public void CaptureCopiesLiveStateRightBeforeEachSave() {
            var store = new MemoryStore();
            var book = new ProgressBook(store);
            Run(book.Load<SampleSave>(Player, Universe));
            int liveCoins = 3;
            book.SetCapture<SampleSave>(Player, s => s.coins = liveCoins);

            Run(book.SaveDirty());
            Assert.AreEqual(3, store.Get(Universe, Player).Value<int>("coins"));
            Run(book.SaveDirty());
            Assert.AreEqual(1, store.Writes, "an unchanged capture is not re-sent");

            liveCoins = 6;
            Run(book.Release(Player));
            Assert.AreEqual(6, store.Get(Universe, Player).Value<int>("coins"));
        }

        [Test]
        public void OversizedDocumentsAreRefused() {
            var store = new MemoryStore();
            var book = new ProgressBook(store) { MaxDocumentBytes = 128 };
            Run(book.Load<SampleSave>(Player, Universe));
            book.Modify<SampleSave>(Player, s => s.title = new string('x', 200));

            LogAssert.Expect(LogType.Error, new Regex("byte limit"));
            Run(book.SaveDirty());
            Assert.AreEqual(0, store.SaveCalls);
        }

        [UnityTest]
        public IEnumerator SavesLandInTheOrderTheyWereRequested() => UniTask.ToCoroutine(async () => {
            var store = new MemoryStore();
            var book = new ProgressBook(store);
            await book.Load<SampleSave>(Player, Universe);

            store.HoldSaves = new UniTaskCompletionSource();
            book.Modify<SampleSave>(Player, s => s.coins = 1);
            UniTask first = book.SaveDirty();      // captured coins = 1, then waits on the store
            book.Modify<SampleSave>(Player, s => s.coins = 2);
            UniTask second = book.Save(Player);    // captured coins = 2, waits for the first save
            store.HoldSaves.TrySetResult();
            await UniTask.WhenAll(first, second);

            Assert.AreEqual(2, store.SaveCalls);
            Assert.AreEqual(2, store.Get(Universe, Player).Value<int>("coins"));
        });
    }
}
