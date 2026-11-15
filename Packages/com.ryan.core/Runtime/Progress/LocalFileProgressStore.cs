using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace RyanAssets.Progress {
    /// <summary>
    /// Keeps progress as JSON files on this machine: <c>&lt;root&gt;/&lt;universe&gt;/&lt;player&gt;.json</c>,
    /// in the same shape the backend returns. Used when the Editor runs a direct local connection
    /// (no backend), so saving can be tested without UnityBackend. Reads and writes are synchronous,
    /// which keeps saves made while play mode stops reliable.
    /// </summary>
    public sealed class LocalFileProgressStore : IProgressStore {
        /// <summary>The default folder: <c>Application.persistentDataPath/PlayerProgress</c>.</summary>
        public static string DefaultRoot => Path.Combine(Application.persistentDataPath, "PlayerProgress");

        static readonly UTF8Encoding Utf8NoBom = new(false);

        public string Root { get; }

        public LocalFileProgressStore(string root = null) {
            Root = string.IsNullOrEmpty(root) ? DefaultRoot : root;
        }

        /// <summary>The file holding one player's document. Ids are validated, so they can't escape <see cref="Root"/>.</summary>
        public string PathFor(string universeId, string playerId) {
            if (!ProgressLimits.IsValidUniverseId(universeId)) throw new ArgumentException($"Invalid universe id '{universeId}'.", nameof(universeId));
            if (!ProgressLimits.IsValidPlayerId(playerId)) throw new ArgumentException($"Invalid player id '{playerId}'.", nameof(playerId));
            return Path.Combine(Root, universeId, playerId + ".json");
        }

        public UniTask<(string error, Dictionary<string, StoredProgress> documents)> LoadAsync(
            string universeId, IReadOnlyList<string> playerIds, CancellationToken token = default) {
            var documents = new Dictionary<string, StoredProgress>();
            try {
                foreach (string playerId in playerIds) {
                    string path = PathFor(universeId, playerId);
                    if (!File.Exists(path)) continue;
                    JObject file = JObject.Parse(File.ReadAllText(path, Utf8NoBom));
                    int version = file.Value<int?>("version") ?? 0;
                    if (file["data"] is not JObject data)
                        return UniTask.FromResult<(string, Dictionary<string, StoredProgress>)>(($"{path} has no data object", null));
                    documents[playerId] = new StoredProgress(version, data);
                }
            } catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or ArgumentException
                                           or FormatException or InvalidCastException) {
                return UniTask.FromResult<(string, Dictionary<string, StoredProgress>)>((e.Message, null));
            }
            return UniTask.FromResult<(string, Dictionary<string, StoredProgress>)>((null, documents));
        }

        public UniTask<string> SaveAsync(string universeId, IReadOnlyList<ProgressWrite> writes, CancellationToken token = default) {
            try {
                foreach (ProgressWrite write in writes) {
                    string path = PathFor(universeId, write.PlayerId);
                    var file = new JObject {
                        ["version"] = write.Version,
                        ["data"] = write.Data,
                        ["updated_at"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                    };
                    WriteAtomically(path, file.ToString(Formatting.Indented));
                }
            } catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException) {
                return UniTask.FromResult(e.Message);
            }
            return UniTask.FromResult<string>(null);
        }

        /// <summary>Writes a temporary file first, so a crash mid-write never leaves half a document.</summary>
        static void WriteAtomically(string path, string contents) {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temp = path + ".tmp";
            File.WriteAllText(temp, contents, Utf8NoBom);
            if (File.Exists(path)) File.Replace(temp, path, null);
            else File.Move(temp, path);
        }
    }
}
