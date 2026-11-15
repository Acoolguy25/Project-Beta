using System;
using UnityEngine;

namespace RyanAssets.Rounds {
    /// <summary>
    /// A yawed box in world space for server rule checks (buy pads, flag bases, safe rooms). Plain data,
    /// so the server can test replicated character positions without colliders or physics callbacks.
    /// </summary>
    [Serializable]
    public struct ZoneBox {
        public Vector3 center;
        public Vector3 size;
        public float yaw;

        public ZoneBox(Vector3 center, Vector3 size, float yaw = 0f) {
            this.center = center;
            this.size = size;
            this.yaw = yaw;
        }

        public static ZoneBox FromTransform(Transform t, Vector3 size) => new(t.position, size, t.eulerAngles.y);

        public bool Contains(Vector3 point) {
            Vector3 local = Quaternion.Euler(0f, -yaw, 0f) * (point - center);
            return Mathf.Abs(local.x) <= size.x * 0.5f && Mathf.Abs(local.y) <= size.y * 0.5f && Mathf.Abs(local.z) <= size.z * 0.5f;
        }

        /// <summary>Horizontal distance check that ignores the box height, for pads players stand on.</summary>
        public bool ContainsFootprint(Vector3 point, float aboveTop = 3f) {
            Vector3 local = Quaternion.Euler(0f, -yaw, 0f) * (point - center);
            return Mathf.Abs(local.x) <= size.x * 0.5f && Mathf.Abs(local.z) <= size.z * 0.5f
                && local.y >= -size.y * 0.5f - 0.5f && local.y <= size.y * 0.5f + aboveTop;
        }
    }
}
