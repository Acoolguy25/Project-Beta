#if !UNITY_SERVER
using RyanAssets.Characters.Client;
#endif
using UnityEngine;

namespace RyanAssets.Platformer {
    /// <summary>
    /// Platformer pieces act only on the local player's character: movement is client-authoritative,
    /// so each client applies springs, gravity and crumbling to its own body.
    /// </summary>
    public static class LocalBody {
#if !UNITY_SERVER
        public static bool TryGet(Collider other, out CharacterMovement movement) {
            movement = CharacterMovement.Local;
            return other != null && movement != null && movement.Body != null && other.attachedRigidbody == movement.Body;
        }

        public static bool TryGet(Rigidbody body, out CharacterMovement movement) {
            movement = CharacterMovement.Local;
            return body != null && movement != null && movement.Body == body;
        }
#endif
    }
}
