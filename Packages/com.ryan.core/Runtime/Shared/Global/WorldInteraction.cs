using UnityEngine;

namespace RyanAssets.Shared.Globals {
    /// <summary>Shared reach and occlusion checks for server-authoritative world interactions.</summary>
    public static class WorldInteraction {
        public static bool CanReach(Vector3 eye, Vector3 target, float distance, int obstructionMask) {
            Vector3 delta = target - eye;
            float length = delta.magnitude;
            if (!float.IsFinite(length) || length > distance) return false;
            return length < 0.1f || !Physics.Raycast(eye, delta / length, Mathf.Max(0, length - 0.2f), obstructionMask, QueryTriggerInteraction.Ignore);
        }
    }
}
