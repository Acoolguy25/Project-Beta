using UnityEngine;

namespace RyanAssets.Characters.Shared {
    /// <summary>Controls how a character standing on this collider follows its motion.</summary>
    [DisallowMultipleComponent]
    public class GroundMotionTransfer : MonoBehaviour {
        [Tooltip("Carry a standing character with this ground's position, including movement around its pivot when it turns.")]
        public bool transferPosition = true;

        [Tooltip("Turn a standing character with this ground.")]
        public bool transferRotation = true;
    }
}
