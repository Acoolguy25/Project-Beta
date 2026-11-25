#if !UNITY_SERVER
using RyanAssets.Characters.Client;
#endif
using UnityEngine;

namespace RyanAssets.Platformer {
    /// <summary>
    /// Limits walking input for a while after a <see cref="LaunchPad"/> on the same object fires (a cannon, a boost ring).
    /// Holding forward through a launch otherwise adds walk speed to the launch speed and overshoots the landing, so the
    /// flight steers at <see cref="airControl"/> of normal. Client only; the launch itself is untouched.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(LaunchPad))]
    public sealed class FlightControl : MonoBehaviour {
        [Tooltip("Seconds after the launch that walking input is limited (the planned flight plus a little); 0 leaves input alone.")]
        public float flightSeconds;
        [Tooltip("Share of normal walking input kept in that flight (1 = all of it).")]
        [Range(0f, 1f)] public float airControl = 1f;

        public void Configure(float seconds, float share) {
            flightSeconds = seconds;
            airControl = share;
        }

#if !UNITY_SERVER
        LaunchPad pad;
        FlightMode mode;

        void OnEnable() {
            pad = GetComponent<LaunchPad>();
            LaunchPad.Launched += OnLaunched;
        }

        void OnDisable() => LaunchPad.Launched -= OnLaunched;

        void OnLaunched(LaunchPad fired, CharacterMovement movement) {
            if (fired != pad || flightSeconds <= 0f || airControl >= 1f) return;
            if (mode == null || mode.owner != movement) {
                mode = new FlightMode { owner = movement };
                movement.AddMode(mode);
            }
            mode.until = Time.fixedTime + flightSeconds;
            mode.share = airControl;
        }

        /// <summary>A movement mode that never takes over a step: it only scales the walking steering for the flight.</summary>
        sealed class FlightMode : IMovementMode {
            public CharacterMovement owner;
            public float until, share = 1f;

            public bool Step(CharacterMovement movement, float dt) {
                if (Time.fixedTime < until && !movement.Grounded) movement.SetMoveBoost(share);
                return false;
            }
        }
#endif
    }
}
