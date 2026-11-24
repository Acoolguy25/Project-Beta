using UnityEngine;

namespace Universes.UniverseData.obby.Client {
    /// <summary>
    /// Small deterministic random stream (PCG) for scenery placement. Every racer builds the same world
    /// from the course seed, so it must not depend on <see cref="Random"/> or on the runtime's
    /// System.Random. A separate <c>stream</c> per placement pass keeps passes independent: adding
    /// rocks never moves a landmark.
    /// </summary>
    public struct OB_EnvRandom {
        const uint Multiplier = 747796405u;
        const uint Increment = 2891336453u;
        const float ToUnit = 1f / 16777216f;

        uint state;

        public OB_EnvRandom(int seed, uint stream) {
            state = unchecked((uint)seed * Multiplier + stream * Increment + 1u);
            NextUInt();
        }

        public uint NextUInt() {
            state = unchecked(state * Multiplier + Increment);
            uint word = unchecked(((state >> (int)((state >> 28) + 4u)) ^ state) * 277803737u);
            return (word >> 22) ^ word;
        }

        /// <summary>Uniform in [0, 1).</summary>
        public float Value() => (NextUInt() >> 8) * ToUnit;

        public float Range(float min, float max) => min + (max - min) * Value();

        public float Range(Vector2 range) => Range(range.x, range.y);

        /// <summary>Uniform integer in [0, <paramref name="max"/>).</summary>
        public int Next(int max) => max <= 0 ? 0 : Mathf.Min((int)(Value() * max), max - 1);

        public bool Chance(float probability) => Value() < probability;

        /// <summary>A sign, +1 or -1.</summary>
        public float Sign() => Value() < 0.5f ? -1f : 1f;

        /// <summary>A unit vector on the horizontal plane.</summary>
        public Vector3 OnCircle() {
            float angle = Value() * Mathf.PI * 2f;
            return new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
        }

        /// <summary>A uniformly random rotation.</summary>
        public Quaternion Rotation() => Quaternion.Euler(Range(0f, 360f), Range(0f, 360f), Range(0f, 360f));
    }
}
