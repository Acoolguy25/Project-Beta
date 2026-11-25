using UnityEngine;

namespace Universes.UniverseData.obby.Client {
    /// <summary>
    /// Ambient motion for a landmark part: steady spin (windmill sails, portal rings, orbiting planet
    /// shards), a slow bob (balloons, floating rocks) and a rocking tilt (hanging lanterns, balloon
    /// baskets). Runs off the clock, so every racer's world moves the same way.
    /// </summary>
    public sealed class OB_EnvMotion : MonoBehaviour {
        [Header("Spin")]
        [Tooltip("Local axis to spin about.")]
        public Vector3 spinAxis = Vector3.up;
        [Tooltip("Degrees per second (negative turns the other way).")]
        public float spinSpeed;

        [Header("Bob")]
        [Tooltip("Local offset at the top of the bob (m).")]
        public Vector3 bobOffset;
        [Min(0.01f)] public float bobPeriod = 6f;

        [Header("Rock")]
        [Tooltip("Local axis to rock about.")]
        public Vector3 rockAxis = Vector3.forward;
        [Tooltip("Largest tilt either way (degrees).")]
        public float rockDegrees;
        [Min(0.01f)] public float rockPeriod = 5f;

        [Tooltip("Offset into every cycle (seconds), so neighbouring parts do not move in step.")]
        public float phase;

        Vector3 restPosition;
        Quaternion restRotation;
        bool captured;

        void OnEnable() {
            if (captured) return;
            restPosition = transform.localPosition;
            restRotation = transform.localRotation;
            captured = true;
        }

        void Update() {
            float t = Time.time + phase;
            const float tau = Mathf.PI * 2f;
            Quaternion rotation = restRotation;
            if (spinSpeed != 0f) rotation *= Quaternion.AngleAxis(spinSpeed * t, spinAxis);
            if (rockDegrees != 0f) rotation *= Quaternion.AngleAxis(Mathf.Sin(t * tau / rockPeriod) * rockDegrees, rockAxis);
            transform.localRotation = rotation;
            if (bobOffset != Vector3.zero) transform.localPosition = restPosition + bobOffset * Mathf.Sin(t * tau / bobPeriod);
        }
    }
}
