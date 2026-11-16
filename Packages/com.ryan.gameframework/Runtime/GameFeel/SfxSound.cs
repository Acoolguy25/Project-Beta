using System;
using UnityEngine;

namespace RyanAssets.GameFeel {
    /// <summary>
    /// One playable sound: a set of interchangeable clip variants plus how loud, how varied and how
    /// often it may play. Serialize one on your own component to give a game its own sounds, then
    /// play it with <see cref="GameSfx.Play(SfxSound, float, float)"/> or
    /// <see cref="GameSfx.PlayAt(SfxSound, Vector3, float, float)"/>.
    /// </summary>
    [Serializable]
    public sealed class SfxSound {
        [Tooltip("Variants; one is picked at random each time, never the same one twice in a row.")]
        public AudioClip[] clips = Array.Empty<AudioClip>();
        [Tooltip("Base volume before the caller's own volume and the SFX setting.")]
        [Range(0f, 1f)] public float volume = 1f;
        [Tooltip("Random volume variation: each play is scaled by 1 - [0, jitter].")]
        [Range(0f, 0.5f)] public float volumeJitter = 0.1f;
        [Tooltip("Base pitch (1 = as recorded).")]
        [Range(0.25f, 3f)] public float pitch = 1f;
        [Tooltip("Random pitch variation in semitones, either way.")]
        [Range(0f, 6f)] public float pitchJitterSemitones = 0.6f;
        [Tooltip("A repeat sooner than this many seconds after the last play is dropped, so twenty coins in one frame do not play twenty times.")]
        [Min(0f)] public float minInterval = 0.04f;
        [Tooltip("3D sounds: full volume within this distance (m). 0 uses the kit default.")]
        [Min(0f)] public float minDistance;
        [Tooltip("3D sounds: inaudible beyond this distance (m). 0 uses the kit default.")]
        [Min(0f)] public float maxDistance;

        [NonSerialized] internal float lastPlayTime = float.NegativeInfinity;
        [NonSerialized] internal int lastClipIndex = -1;

        public bool HasClips => clips != null && clips.Length > 0;

        /// <summary>Picks the next variant, avoiding an immediate repeat when there is a choice.</summary>
        internal AudioClip PickClip() {
            if (!HasClips)
                return null;
            int count = clips.Length;
            int index;
            if (count == 1 || lastClipIndex < 0 || lastClipIndex >= count) {
                index = UnityEngine.Random.Range(0, count);
            } else {
                // Draw from the other count - 1 variants.
                index = UnityEngine.Random.Range(0, count - 1);
                if (index >= lastClipIndex)
                    index++;
            }
            lastClipIndex = index;
            return clips[index];
        }
    }
}
