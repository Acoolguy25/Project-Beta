using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace RyanAssets.Progress {
    /// <summary>Raised when a saved document cannot be turned into a <see cref="ProgressData"/>.</summary>
    public sealed class ProgressFormatException : Exception {
        public ProgressFormatException(string message, Exception inner = null) : base(message, inner) { }
    }

    /// <summary>
    /// Turns <see cref="ProgressData"/> into JSON and back, applying versioning. Only public instance
    /// fields are written (like Unity's own serializer), no type names are ever read, and nesting is
    /// limited, so a stored document can't instantiate arbitrary types.
    /// </summary>
    public static class ProgressCodec {
        const int MaxDepth = 32;

        static readonly JsonSerializer Serializer = JsonSerializer.Create(new JsonSerializerSettings {
            ContractResolver = new PublicFieldsResolver(),
            TypeNameHandling = TypeNameHandling.None,
            // Lists and arrays in a loaded document replace the defaults instead of appending to them.
            ObjectCreationHandling = ObjectCreationHandling.Replace,
            MissingMemberHandling = MissingMemberHandling.Ignore,
            MaxDepth = MaxDepth,
        });

        /// <summary>Serializes <paramref name="data"/>, stamping it with its <see cref="ProgressData.LatestVersion"/>.</summary>
        public static JObject Serialize(ProgressData data) {
            if (data == null) throw new ArgumentNullException(nameof(data));
            data.version = data.LatestVersion;
            return JObject.FromObject(data, Serializer);
        }

        /// <summary>
        /// Builds a <typeparamref name="T"/> from a stored document. A null document gives a new
        /// player's defaults. Older versions run through <see cref="ProgressData.Migrate"/>.
        /// </summary>
        /// <exception cref="ProgressFormatException">The document is newer than this code or unreadable.</exception>
        public static T Deserialize<T>(JObject stored, int storedVersion) where T : ProgressData, new() {
            T data = new();
            int latest = data.LatestVersion;
            if (stored != null) {
                if (storedVersion > latest)
                    throw new ProgressFormatException(
                        $"{typeof(T).Name} was saved with version {storedVersion}, but this build only knows {latest}.");
                try {
                    using JsonReader reader = stored.CreateReader();
                    reader.MaxDepth = MaxDepth;
                    Serializer.Populate(reader, data);
                    if (storedVersion < latest) data.Migrate(storedVersion, stored);
                } catch (Exception e) {
                    // Wrong JSON types, overflowing numbers, or a Migrate that tripped over old data.
                    throw new ProgressFormatException($"{typeof(T).Name} could not be read: {e.Message}", e);
                }
            }
            data.version = latest;
            data.Validate();
            return data;
        }

        /// <summary>Size of the compact JSON the backend stores and limits.</summary>
        public static int SizeInBytes(JObject document) => Encoding.UTF8.GetByteCount(document.ToString(Formatting.None));

        /// <summary>Public instance fields only; properties such as LatestVersion are never written.</summary>
        sealed class PublicFieldsResolver : DefaultContractResolver {
            protected override List<MemberInfo> GetSerializableMembers(Type objectType) =>
                objectType.GetFields(BindingFlags.Public | BindingFlags.Instance)
                    .Where(field => !field.IsInitOnly && !field.IsDefined(typeof(NonSerializedAttribute), true)
                        && !field.IsDefined(typeof(JsonIgnoreAttribute), true))
                    .Cast<MemberInfo>()
                    .ToList();
        }
    }
}
