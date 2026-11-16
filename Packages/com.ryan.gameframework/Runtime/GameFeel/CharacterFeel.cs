using RyanAssets.Characters.Shared;
using UnityEngine;

namespace RyanAssets.GameFeel {
    /// <summary>
    /// Runtime switches for the automatic character feel (jump and landing dust, squash, landing
    /// shake). Tuning lives in GameFeelSettings &gt; Characters; these are for a game to adjust while it runs.
    /// <code>
    /// CharacterFeel.LandingShake = false;          // a game with its own camera work
    /// CharacterFeel.NotifyTeleported(character);   // after a checkpoint warp: no shake on arrival
    /// </code>
    /// </summary>
    public static class CharacterFeel {
        /// <summary>
        /// Whether a hard landing of the local player kicks the camera (default true; the settings'
        /// Land Shake must be on as well, and the player's Camera Shake setting scales it).
        /// </summary>
        public static bool LandingShake { get; set; } = true;

        /// <summary>
        /// Tells the kit a character was moved by code: the fall measured so far is dropped and the
        /// next landing within GameFeelSettings &gt; Characters &gt; Teleport Grace Seconds does not shake the
        /// camera. Big jumps (over 25 m in a frame) are detected without this call.
        /// </summary>
        public static void NotifyTeleported(GameCharacter character) {
            if (character == null)
                return;
            GameFeelRunner runner = GameFeelRunner.Get();
            if (runner != null && runner.Characters != null)
                runner.Characters.NotifyTeleported(character);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => LandingShake = true;
    }
}
