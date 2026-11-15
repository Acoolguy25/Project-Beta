using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json.Linq;
using RyanAssets.Core;
using RyanAssets.NetworkService;
using UnityEngine;

namespace RyanAssets.Progress {
    /// <summary>
    /// Progress documents in UnityBackend (<c>/api/internal/v1/progress</c>). Only game servers can
    /// reach these routes: the backend accepts loopback callers or the shared internal token.
    /// </summary>
    public sealed class BackendProgressStore : IProgressStore {
        public const string LoadPath = "/api/internal/v1/progress/load";
        public const string SavePath = "/api/internal/v1/progress/save";

        /// <summary>Tries per request before giving up (waits 1 s, then 4 s, ... between tries).</summary>
        public int Attempts { get; set; } = 3;

        public async UniTask<(string error, Dictionary<string, StoredProgress> documents)> LoadAsync(
            string universeId, IReadOnlyList<string> playerIds, CancellationToken token = default) {
            var body = new JObject {
                ["universe_id"] = universeId,
                ["player_ids"] = new JArray(playerIds),
            };
            (string error, JObject response) = await PostWithRetries(LoadPath, body, "Load progress", token);
            if (error != null) return (error, null);
            if (response["documents"] is not JObject documents) return ("Load progress: response has no documents", null);

            var result = new Dictionary<string, StoredProgress>();
            foreach (JProperty entry in documents.Properties()) {
                if (entry.Value is not JObject document || document["data"] is not JObject data)
                    return ($"Load progress: malformed document for {entry.Name}", null);
                result[entry.Name] = new StoredProgress(document.Value<int?>("version") ?? 0, data);
            }
            return (null, result);
        }

        public async UniTask<string> SaveAsync(string universeId, IReadOnlyList<ProgressWrite> writes, CancellationToken token = default) {
            var documents = new JObject();
            foreach (ProgressWrite write in writes)
                documents[write.PlayerId] = new JObject { ["version"] = write.Version, ["data"] = write.Data };
            var body = new JObject { ["universe_id"] = universeId, ["documents"] = documents };

            (string error, JObject response) = await PostWithRetries(SavePath, body, "Save progress", token);
            if (error != null) return error;
            // Not fatal: the backend skips players it doesn't know (an Editor test id, say).
            if (response["unknown_players"] is JArray unknown && unknown.Count > 0)
                Debug.LogWarning($"Save progress ({universeId}): the backend has no account for {string.Join(", ", unknown)}; skipped.");
            return null;
        }

        async UniTask<(string, JObject)> PostWithRetries(string path, JObject body, string title, CancellationToken token) {
            string error = "Request never ran";
            for (int attempt = 0; attempt < Math.Max(1, Attempts); attempt++) {
                if (attempt > 0)
                    await UniTask.Delay(TimeSpan.FromSeconds(RequestHelper.GetRetryDelay(attempt - 1)), ignoreTimeScale: true, cancellationToken: token);
                JObject response;
                try {
                    (error, response) = await BackendNetwork.PostRequest(path, body);
                } catch (Exception e) {
                    error = $"Unhandled error: {e.Message}";
                    response = null;
                }
                if (error == null) return (null, response ?? new JObject());
                Debug.LogWarning($"{title} failed ({attempt + 1}/{Math.Max(1, Attempts)}): {error}");
            }
            return (error, null);
        }
    }
}
