using UnityEngine;

namespace RyanAssets.Platformer {
    /// <summary>
    /// A hoop that fires whoever flies into it along a fixed velocity (a <see cref="LaunchPad"/> on the
    /// same object does the launching). The hoop visual is sized to the trigger.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BoostRing : MonoBehaviour {
        [Tooltip("The hoop's drawn ring; scaled to the trigger's diameter (its axis is the ring's local Z).")]
        public Transform hoop;

        public void Configure(Vector3 localVelocity, Vector3 size, float reachAbove = 0f) {
            if (TryGetComponent(out LaunchPad pad)) pad.Configure(localVelocity);
            if (TryGetComponent(out BoxCollider box)) {
                box.isTrigger = true;
                // The trigger may reach above the drawn hoop (a flight that arrives early comes in higher up its arc).
                box.center = new Vector3(0f, reachAbove * 0.5f, 0f);
                box.size = new Vector3(size.x, size.y + reachAbove, size.z);
            }
            if (hoop != null) hoop.localScale = new Vector3(size.x, size.y, Mathf.Max(0.1f, size.z));
        }
    }
}
