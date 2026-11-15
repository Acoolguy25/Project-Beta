using System;
using Newtonsoft.Json.Linq;

namespace RyanAssets.Progress {
    /// <summary>
    /// Base class for one game's saved player progress: a plain class whose public fields are the
    /// document (properties are never saved). Keep it small; the backend caps a document at 64 KiB.
    /// <para>
    /// Change the shape freely while adding fields (missing fields keep their initializer). When an
    /// existing field changes meaning or is renamed, raise <see cref="LatestVersion"/> and convert the
    /// old document in <see cref="Migrate"/>.
    /// </para>
    /// </summary>
    /// <example><code>
    /// public sealed class OB_SaveData : ProgressData {
    ///     public const string Universe = "obby";
    ///     public override int LatestVersion => 1;
    ///     public int coursesFinished;
    ///     public float bestTime;
    /// }
    /// </code></example>
    [Serializable]
    public abstract class ProgressData {
        /// <summary>The version this document was written with. Set by <see cref="ProgressCodec"/>.</summary>
        public int version;

        /// <summary>The version this code writes. Start at 1 and raise it when <see cref="Migrate"/> has work to do.</summary>
        public abstract int LatestVersion { get; }

        /// <summary>
        /// Converts a document saved with an older <paramref name="fromVersion"/>. Fields that still
        /// exist are already filled in; <paramref name="stored"/> is the raw saved JSON for the rest.
        /// </summary>
        public virtual void Migrate(int fromVersion, JObject stored) { }

        /// <summary>
        /// Repairs values after loading (negative counters, unknown ids...). Saved data comes from an
        /// older build or a hand edit as easily as from this one, so never trust it blindly.
        /// </summary>
        public virtual void Validate() { }
    }
}
