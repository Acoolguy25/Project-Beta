using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace RyanAssets.Progress {
    /// <summary>One player's stored document for one universe.</summary>
    public readonly struct StoredProgress {
        public readonly int Version;
        public readonly JObject Data;

        public StoredProgress(int version, JObject data) {
            Version = version;
            Data = data;
        }
    }

    /// <summary>A document to write: whose it is and what to store.</summary>
    public readonly struct ProgressWrite {
        public readonly string PlayerId;
        public readonly int Version;
        public readonly JObject Data;

        public ProgressWrite(string playerId, int version, JObject data) {
            PlayerId = playerId;
            Version = version;
            Data = data;
        }
    }

    /// <summary>
    /// Where progress documents live: the UnityBackend (<see cref="BackendProgressStore"/>) or JSON
    /// files on this machine (<see cref="LocalFileProgressStore"/>). Errors follow the project's
    /// backend convention: a null error string means success.
    /// </summary>
    public interface IProgressStore {
        /// <summary>
        /// Reads the documents of <paramref name="playerIds"/>. Players with nothing saved are absent
        /// from the result. Returns an error (and no documents) when the store can't be reached.
        /// </summary>
        UniTask<(string error, Dictionary<string, StoredProgress> documents)> LoadAsync(
            string universeId, IReadOnlyList<string> playerIds, CancellationToken token = default);

        /// <summary>Writes every document in <paramref name="writes"/>; returns null on success.</summary>
        UniTask<string> SaveAsync(string universeId, IReadOnlyList<ProgressWrite> writes, CancellationToken token = default);
    }

    /// <summary>The id and size rules the backend enforces, checked here before anything is sent.</summary>
    public static class ProgressLimits {
        /// <summary>Default per-document limit; matches UnityBackend's progress_max_bytes default.</summary>
        public const int DefaultMaxDocumentBytes = 64 * 1024;
        /// <summary>Players per load/save request; matches UnityBackend's progress_max_batch default.</summary>
        public const int MaxBatch = 100;

        static readonly Regex PlayerIdPattern = new("^[A-Za-z0-9_-]{1,128}\\z", RegexOptions.CultureInvariant);
        static readonly Regex UniverseIdPattern = new("^[A-Za-z0-9_-]{1,64}\\z", RegexOptions.CultureInvariant);

        public static bool IsValidPlayerId(string playerId) => playerId != null && PlayerIdPattern.IsMatch(playerId);
        public static bool IsValidUniverseId(string universeId) => universeId != null && UniverseIdPattern.IsMatch(universeId);
    }
}
