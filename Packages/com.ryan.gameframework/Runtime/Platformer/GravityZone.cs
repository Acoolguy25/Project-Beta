#if !UNITY_SERVER
using RyanAssets.Characters.Client;
#endif
using UnityEngine;

namespace RyanAssets.Platformer {
    /// <summary>
    /// A trigger volume that changes gravity for the local character while it is inside.
    /// <list type="bullet">
    /// <item><see cref="Mode.Strength"/>: keeps the direction and scales strength (below 1 gives
    /// floaty moon jumps, above 1 makes jumps short and falls fast).</item>
    /// <item><see cref="Mode.Direction"/>: also turns gravity toward <see cref="localDown"/>, so the
    /// character falls onto, then stands, walks and jumps on a wall or ceiling.</item>
    /// </list>
    /// The request is refreshed every physics step the character is inside and lapses shortly after it
    /// leaves; when volumes overlap, the most recently entered direction wins.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GravityZone : MonoBehaviour {
        public enum Mode { Strength, Direction }

        [Tooltip("Strength only, or strength and direction.")]
        public Mode mode = Mode.Strength;
        [Tooltip("Multiplier on the character's gravity strength while inside.")]
        [Min(0f)] public float gravityScale = 0.35f;
        [Tooltip("Direction gravity pulls in Direction mode, in this object's local space.")]
        public Vector3 localDown = Vector3.down;
        [Tooltip("Seconds the request outlives the character leaving the volume.")]
        [Min(0f)] public float lingerSeconds = 0.1f;

        /// <summary>World-space gravity direction in Direction mode.</summary>
        public Vector3 WorldDown => transform.TransformDirection(localDown).normalized;

        /// <summary>Strength-only volume.</summary>
        public void Configure(float scale) {
            mode = Mode.Strength;
            gravityScale = Mathf.Max(0f, scale);
        }

        /// <summary>Volume that pulls toward <paramref name="worldDown"/> with <paramref name="scale"/> times normal strength.</summary>
        public void Configure(Vector3 worldDown, float scale = 1f) {
            mode = Mode.Direction;
            gravityScale = Mathf.Max(0f, scale);
            localDown = transform.InverseTransformDirection(worldDown.normalized);
        }

#if !UNITY_SERVER
        void OnTriggerStay(Collider other) {
            if (!LocalBody.TryGet(other, out CharacterMovement movement) || movement.Gravity == null)
                return;
            Vector3? down = mode == Mode.Direction ? WorldDown : null;
            movement.Gravity.Request(this, down, gravityScale, lingerSeconds);
        }
#endif

        void OnDrawGizmosSelected() {
            if (mode != Mode.Direction)
                return;
            Gizmos.color = Color.cyan;
            Gizmos.DrawRay(transform.position, WorldDown * 3f);
        }
    }
}
