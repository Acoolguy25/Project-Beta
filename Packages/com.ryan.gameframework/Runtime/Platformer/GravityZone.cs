#if !UNITY_SERVER
using RyanAssets.Characters.Client;
#endif
using UnityEngine;

namespace RyanAssets.Platformer {
    /// <summary>
    /// A trigger volume that scales gravity for the local character while it is inside.
    /// Below 1 gives floaty moon jumps, above 1 makes jumps short and falls fast.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GravityZone : MonoBehaviour {
        [Min(0f)] public float gravityScale = 0.35f;

        public void Configure(float scale) => gravityScale = Mathf.Max(0f, scale);

#if !UNITY_SERVER
        void OnTriggerStay(Collider other) {
            if (LocalBody.TryGet(other, out CharacterMovement movement))
                movement.SetGravityScale(this, gravityScale, 0.1f);
        }
#endif
    }
}
