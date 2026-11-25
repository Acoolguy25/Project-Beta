#if !UNITY_SERVER
using RyanAssets.Characters.Client;
#endif
using UnityEngine;

namespace RyanAssets.Platformer {
    /// <summary>
    /// A flip arrow: touching its trigger turns the local character's gravity toward
    /// <see cref="localDown"/> and keeps it that way (no volume needed) until another flip pad, a
    /// respawn (teleports clear gravity) or <see cref="holdSeconds"/>. Floor pads, ceiling pads and
    /// floating orbs that flip a player in mid-jump are all this component. Every pad shares one gravity
    /// request, so the newest flip wins; a <see cref="GravityZone"/> entered later still takes over
    /// while the player is inside it. The character's gravity debounces direction changes (see
    /// <c>CharacterGravity</c>), so two pads touched within a fraction of a second settle on one direction
    /// and a flip cannot be chained to skip a stage.
    /// <para>Design rule: a flip must always have a surface to fall onto, and a level that flips players
    /// must catch players who fall the "wrong" way (the game's server needs an upper kill height).</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GravityFlipPad : MonoBehaviour {
        [Tooltip("Direction gravity pulls after the flip, in this object's space.")]
        public Vector3 localDown = Vector3.up;
        [Tooltip("Seconds the flip lasts without another pad.")]
        [Min(1f)] public float holdSeconds = 3600f;
        [Tooltip("Gravity strength multiplier while flipped.")]
        [Min(0f)] public float gravityScale = 1f;
        [Tooltip("Optional arrow visual that spins slowly to show the pad is live.")]
        public Transform arrow;
        [Min(0f)] public float arrowSpin = 90f;

        /// <summary>The one request every pad writes, so a later pad replaces an earlier flip.</summary>
        static readonly object FlipSource = new();

        public Vector3 WorldDown => transform.TransformDirection(localDown).normalized;

        /// <summary>Points the flip at <paramref name="worldDown"/>.</summary>
        public void Configure(Vector3 worldDown) => localDown = transform.InverseTransformDirection(worldDown.normalized);

        /// <summary>Points the flip at <paramref name="worldDown"/> and sets how long it holds (a stage's worth, not the default hour).</summary>
        public void Configure(Vector3 worldDown, float hold) {
            Configure(worldDown);
            holdSeconds = Mathf.Max(1f, hold);
        }

#if !UNITY_SERVER
        /// <summary>Raised on the client when a pad flips the local character.</summary>
        public static event System.Action<GravityFlipPad> Flipped;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Flipped = null;

        void OnTriggerEnter(Collider other) {
            if (!LocalBody.TryGet(other, out CharacterMovement movement) || movement.Gravity == null) return;
            Vector3 down = WorldDown;
            bool change = Vector3.Dot(movement.Gravity.TargetDown, down) < 0.99f;
            movement.Gravity.Request(FlipSource, down, gravityScale, holdSeconds);
            if (change) Flipped?.Invoke(this);
        }

        void Update() {
            if (arrow != null) arrow.Rotate(0f, arrowSpin * Time.deltaTime, 0f, Space.Self);
        }
#endif

        void OnDrawGizmosSelected() {
            Gizmos.color = Color.cyan;
            Gizmos.DrawRay(transform.position, WorldDown * 3f);
        }
    }
}
