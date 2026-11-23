using System;
using System.Collections.Generic;
using UnityEngine;

namespace Universes.UniverseData.classic_horror {
    /// <summary>
    /// Every spoken line of The Drowned Choir: its id, who says it (the subtitle caption), the words, and the clip.
    /// Built from <c>Audio/Voice/Source~/lines.tsv</c> by <b>Ryan > Classic Horror > Rebuild Voice Bank</b>. The server
    /// reads only ids and lengths, to keep lines from talking over each other.
    /// </summary>
    [CreateAssetMenu(menuName = "Ryan/Classic Horror/Voice Bank")]
    public sealed class CH_VoiceBank : ScriptableObject {
        [Serializable]
        public struct Line {
            public string id;
            public string speaker;
            [TextArea] public string text;
            public AudioClip clip;
            [Tooltip("Heard over Mara's radio: the subtitle arrives with a burst of static.")]
            public bool radio;
            [Tooltip("Clip length in seconds, stored so the server need not load audio to time its lines.")]
            public float seconds;
        }

        public Line[] lines = new Line[0];

        Dictionary<string, int> index;

        public bool TryGet(string id, out Line line) {
            if (index == null || index.Count != lines.Length) {
                index = new Dictionary<string, int>(lines.Length, StringComparer.Ordinal);
                for (int i = 0; i < lines.Length; i++) index[lines[i].id] = i;
            }
            if (!string.IsNullOrEmpty(id) && index.TryGetValue(id, out int found)) { line = lines[found]; return true; }
            line = default;
            return false;
        }

        /// <summary>How long the line takes to say, or <paramref name="fallback"/> for an unknown id.</summary>
        public float Seconds(string id, float fallback = 3f) => TryGet(id, out Line line) ? line.seconds : fallback;

        void OnValidate() => index = null;
    }
}
