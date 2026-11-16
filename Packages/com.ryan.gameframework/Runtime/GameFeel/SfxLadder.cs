using UnityEngine;

namespace RyanAssets.GameFeel {
    /// <summary>
    /// A pitch "ladder" for streaks: each play inside the streak window sounds a step higher (coins
    /// picked up in a row, combo hits), and a pause resets it to the bottom. Keep one per streak,
    /// e.g. as a field:
    /// <code>
    /// readonly SfxLadder coinLadder = new(SfxCue.Coin);
    /// void OnCoin(Vector3 at) => coinLadder.PlayAt(at);
    /// </code>
    /// </summary>
    public sealed class SfxLadder {
        readonly SfxCue cue;
        readonly SfxSound sound;
        readonly float semitoneStep;
        readonly int maxSteps;
        readonly float resetSeconds;
        float lastPlay = float.NegativeInfinity;

        /// <summary>Current rung (0 = bottom).</summary>
        public int Step { get; private set; }

        /// <param name="cue">Kit cue to play.</param>
        /// <param name="semitoneStep">Pitch rise per rung, in semitones (2 = whole tone).</param>
        /// <param name="maxSteps">Highest rung; further plays stay there.</param>
        /// <param name="resetSeconds">A pause this long drops back to the bottom rung.</param>
        public SfxLadder(SfxCue cue, float semitoneStep = 1f, int maxSteps = 12, float resetSeconds = 1.2f)
            : this(semitoneStep, maxSteps, resetSeconds) {
            this.cue = cue;
        }

        /// <summary>A ladder over a game's own sound instead of a kit cue.</summary>
        public SfxLadder(SfxSound sound, float semitoneStep = 1f, int maxSteps = 12, float resetSeconds = 1.2f)
            : this(semitoneStep, maxSteps, resetSeconds) {
            this.sound = sound;
        }

        SfxLadder(float semitoneStep, int maxSteps, float resetSeconds) {
            this.semitoneStep = semitoneStep;
            this.maxSteps = Mathf.Max(0, maxSteps);
            this.resetSeconds = Mathf.Max(0f, resetSeconds);
        }

        /// <summary>Plays the next rung in 2D and returns the pitch used.</summary>
        public float Play(float volume = 1f) {
            float pitch = Advance();
            GameSfx.Play(Sound, volume, pitch);
            return pitch;
        }

        /// <summary>Plays the next rung in 3D at <paramref name="position"/> and returns the pitch used.</summary>
        public float PlayAt(Vector3 position, float volume = 1f) {
            float pitch = Advance();
            GameSfx.PlayAt(Sound, position, volume, pitch);
            return pitch;
        }

        /// <summary>Back to the bottom rung.</summary>
        public void Reset() {
            Step = 0;
            lastPlay = float.NegativeInfinity;
        }

        SfxSound Sound => sound ?? GameSfx.Get(cue);

        float Advance() {
            float now = Time.unscaledTime;
            Step = now - lastPlay > resetSeconds ? 0 : Mathf.Min(Step + 1, maxSteps);
            lastPlay = now;
            return GameSfx.Semitones(Step * semitoneStep);
        }
    }
}
