using System;
using RyanAssets.GameFeel;
using UnityEngine;

namespace Universes.UniverseData.obby.Client {
    /// <summary>A game-feel kit sound cue with Sky Obby's volume and pitch for one moment.</summary>
    [Serializable]
    public struct OB_Cue {
        public SfxCue cue;
        [Range(0f, 1f)] public float volume;
        [Range(0.25f, 3f)] public float pitch;

        public OB_Cue(SfxCue cue, float volume = 1f, float pitch = 1f) {
            this.cue = cue;
            this.volume = volume;
            this.pitch = pitch;
        }

        /// <summary>Plays in 2D; <paramref name="pitchScale"/> multiplies the cue's own pitch.</summary>
        public readonly void Play(float pitchScale = 1f) => GameSfx.Play(cue, volume, pitch * pitchScale);

        /// <summary>Plays in 3D at <paramref name="position"/>.</summary>
        public readonly void PlayAt(Vector3 position, float pitchScale = 1f) => GameSfx.PlayAt(cue, position, volume, pitch * pitchScale);
    }
}
