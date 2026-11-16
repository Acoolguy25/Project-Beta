using UnityEngine;

namespace RyanAssets.GameFeel {
    /// <summary>
    /// Pooled one-shot sounds: 2D (UI, announcements) or 3D at a world position. Every sound plays
    /// through the SFX mixer group, so the player's SFX Volume setting applies. Each sound picks a
    /// random variant and jitters its pitch and volume a little so repeats never sound canned.
    /// <code>
    /// GameSfx.Play(SfxCue.Purchase);                       // 2D
    /// GameSfx.PlayAt(SfxCue.Explosion, transform.position); // 3D
    /// GameSfx.Play(mySerializedSound);                      // a game's own SfxSound
    /// </code>
    /// All calls do nothing outside Play mode and compile out of dedicated server builds.
    /// </summary>
    public static class GameSfx {
        /// <summary>Plays a kit cue in 2D.</summary>
        public static void Play(SfxCue cue, float volume = 1f, float pitch = 1f) =>
            Play(Get(cue), volume, pitch);

        /// <summary>Plays a kit cue in 3D at <paramref name="position"/>.</summary>
        public static void PlayAt(SfxCue cue, Vector3 position, float volume = 1f, float pitch = 1f) =>
            PlayAt(Get(cue), position, volume, pitch);

        /// <summary>Plays a game's own <see cref="SfxSound"/> in 2D.</summary>
        public static void Play(SfxSound sound, float volume = 1f, float pitch = 1f) {
            GameFeelRunner runner = GameFeelRunner.Get();
            if (runner != null)
                runner.Sfx.Play(sound, false, default, volume, pitch);
        }

        /// <summary>Plays a game's own <see cref="SfxSound"/> in 3D at <paramref name="position"/>.</summary>
        public static void PlayAt(SfxSound sound, Vector3 position, float volume = 1f, float pitch = 1f) {
            GameFeelRunner runner = GameFeelRunner.Get();
            if (runner != null)
                runner.Sfx.Play(sound, true, position, volume, pitch);
        }

        /// <summary>Plays a bare clip in 2D (no jitter, no rate limit).</summary>
        public static void PlayClip(AudioClip clip, float volume = 1f, float pitch = 1f) {
            GameFeelRunner runner = GameFeelRunner.Get();
            if (runner != null)
                runner.Sfx.PlayClip(clip, false, default, volume, pitch);
        }

        /// <summary>Plays a bare clip in 3D at <paramref name="position"/> (no jitter, no rate limit).</summary>
        public static void PlayClipAt(AudioClip clip, Vector3 position, float volume = 1f, float pitch = 1f) {
            GameFeelRunner runner = GameFeelRunner.Get();
            if (runner != null)
                runner.Sfx.PlayClip(clip, true, position, volume, pitch);
        }

        /// <summary>The kit's sound for <paramref name="cue"/> (null if the library lacks it), e.g. to read its clips.</summary>
        public static SfxSound Get(SfxCue cue) {
            GameFeelSettings settings = GameFeelSettings.Active;
            return settings != null && settings.cues != null ? settings.cues.Get(cue) : null;
        }

        /// <summary>The pitch multiplier for a musical interval: Semitones(12) = 2, Semitones(-12) = 0.5.</summary>
        public static float Semitones(float steps) => Mathf.Pow(2f, steps / 12f);
    }
}
