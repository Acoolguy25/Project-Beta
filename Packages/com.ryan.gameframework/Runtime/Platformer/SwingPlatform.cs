using UnityEngine;

namespace RyanAssets.Platformer {
    /// <summary>
    /// A trapeze: a platform hanging from a pivot (this object's origin) that swings about its local X axis
    /// on the shared motion clock (<see cref="NetworkPlatform"/> does the moving, so every peer and the server
    /// agree and riders are carried). Place the object at the pivot, turned to one end of the swing.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SwingPlatform : MonoBehaviour {
        [Tooltip("The platform: a box whose top face hangs one rope length below the pivot.")]
        public Transform platform;
        [Tooltip("Rope visuals (thin boxes from the pivot down to the platform), left and right.")]
        public Transform[] ropes;
        [Tooltip("Width of a rope, metres.")]
        [Min(0.02f)] public float ropeWidth = 0.18f;

        /// <summary>
        /// Sizes the swing: <paramref name="size"/> = (platform width, rope length, platform depth), swinging
        /// <paramref name="amplitude"/> degrees either side of straight down every <paramref name="period"/> seconds.
        /// The object must already be turned to the start of the swing (+amplitude about X).
        /// The swinging itself is a NetworkPlatform on the same object, which the game configures with <see cref="MotionDelta"/> and <see cref="MotionSpeed"/> (the platform component lives in a layer this assembly does not reference).
        /// </summary>
        public void Configure(Vector3 size, float amplitude, float thickness) {
            float rope = size.y;
            if (platform != null) {
                platform.localScale = new Vector3(size.x, thickness, size.z);
                platform.localPosition = new Vector3(0f, -rope - thickness * 0.5f, 0f);
            }
            if (ropes != null)
                for (int i = 0; i < ropes.Length; i++) {
                    if (ropes[i] == null) continue;
                    float side = i % 2 == 0 ? -1f : 1f;
                    ropes[i].localScale = new Vector3(ropeWidth, rope, ropeWidth);
                    ropes[i].localPosition = new Vector3(side * (size.x * 0.5f - ropeWidth), -rope * 0.5f, 0f);
                }
        }

        /// <summary>Rotation delta (degrees) of a NetworkPlatform that swings from +amplitude to -amplitude and back.</summary>
        public static Vector3 MotionDelta(float amplitude) => new(-2f * amplitude, 0f, 0f);

        /// <summary>Rotation speed (degrees per second) that makes one full swing take <paramref name="period"/> seconds.</summary>
        public static float MotionSpeed(float amplitude, float period) => 4f * amplitude / Mathf.Max(0.5f, period);
    }
}
