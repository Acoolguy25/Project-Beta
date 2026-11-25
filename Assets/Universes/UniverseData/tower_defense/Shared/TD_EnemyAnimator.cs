using System.Collections.Generic;
using UnityEngine;

namespace Universes.UniverseData.tower_defense {
    /// <summary>
    /// Makes an enemy look like it is travelling the way its vehicle does: wheels and tracks turn with the
    /// ground speed, rotors spin, ground vehicles rock on their suspension, aircraft hover, bob and bank into
    /// the bends of the road. Purely visual: it only moves the enemy's body and the model's wheel and rotor parts.
    /// </summary>
    public sealed class TD_EnemyAnimator : MonoBehaviour {
        public enum Style { Wheeled, Tracked, Helicopter, Hover, Drone }

        public Style style;
        [Tooltip("The model's body: moved and tilted (never the enemy root, which follows the road).")]
        public Transform body;
        [Header("Ground vehicles")]
        [Tooltip("Wheel turns per metre driven (1 / wheel circumference).")]
        [Min(0f)] public float wheelTurnsPerMetre = 0.5f;
        [Tooltip("Suspension rock (metres) at full speed.")]
        [Min(0f)] public float rockHeight = 0.05f;
        [Min(0f)] public float rockFrequency = 9f;
        [Tooltip("Nose dip in degrees when the vehicle speeds up or slows down.")]
        [Min(0f)] public float pitchDegrees = 3f;
        [Header("Aircraft and hovercraft")]
        [Tooltip("Rotor spin in degrees per second.")]
        [Min(0f)] public float rotorSpeed = 1800f;
        [Min(0f)] public float hoverHeight = 0.18f;
        [Min(0f)] public float hoverFrequency = 2.2f;
        [Tooltip("Roll in degrees when the vehicle turns at the bends of the road.")]
        [Min(0f)] public float bankDegrees = 22f;

        struct Part {
            public Transform transform;
            public Vector3 localCentre; // spin point, in the parent's space
            public Vector3 axis;        // in the body's space
            public float degreesPerMetre, degreesPerSecond;
        }

        readonly List<Part> parts = new();
        float phase, lastYaw, bank, pitch, lastSpeed;
        Vector3 basePosition;
        Quaternion baseRotation;
        bool ready;

        void Awake() {
            phase = Random.value * 10f;
            if (body == null) return;
            basePosition = body.localPosition;
            baseRotation = body.localRotation;
            foreach (var renderer in body.GetComponentsInChildren<Renderer>(true)) {
                var t = renderer.transform;
                string n = t.name;
                if (n.Contains("Wheel") && !n.EndsWith("Wheels") && style is Style.Wheeled or Style.Tracked)
                    Add(t, renderer, Vector3.right, degreesPerMetre: 360f * wheelTurnsPerMetre);
                else if (n.Contains("SubPropel")) Add(t, renderer, Vector3.right, degreesPerSecond: rotorSpeed);
                else if (n.Contains("Propel") || n.Contains("Rotor")) Add(t, renderer, Vector3.up, degreesPerSecond: rotorSpeed);
            }
            ready = true;
        }

        void Add(Transform t, Renderer renderer, Vector3 axis, float degreesPerMetre = 0f, float degreesPerSecond = 0f) {
            // Spin about the part's visual centre whatever its pivot is.
            parts.Add(new Part {
                transform = t, localCentre = t.parent.InverseTransformPoint(renderer.bounds.center), axis = axis,
                degreesPerMetre = degreesPerMetre, degreesPerSecond = degreesPerSecond,
            });
        }

        /// <summary>Advances the animation by <paramref name="dt"/> seconds for an enemy driving <paramref name="speed"/> metres per second.</summary>
        /// <param name="shake">Sideways shudder from a hit, in metres.</param>
        public void Animate(float speed, float dt, float shake) {
            if (!ready || dt <= 0f) return;
            phase += dt;
            // Heading change per second -> bank; speed change -> pitch.
            float yaw = transform.eulerAngles.y;
            float turn = Mathf.DeltaAngle(lastYaw, yaw) / dt;
            lastYaw = yaw;
            float accel = (speed - lastSpeed) / dt;
            lastSpeed = speed;
            bank = Mathf.Lerp(bank, Mathf.Clamp(turn / 90f, -1f, 1f) * bankDegrees, 1f - Mathf.Exp(-6f * dt));
            pitch = Mathf.Lerp(pitch, Mathf.Clamp(accel, -2f, 2f) * 0.5f * pitchDegrees, 1f - Mathf.Exp(-4f * dt));

            Vector3 offset = Vector3.zero;
            Quaternion tilt = Quaternion.identity;
            float moving = Mathf.Clamp01(speed / 4f);
            switch (style) {
                case Style.Wheeled:
                case Style.Tracked:
                    offset.y = Mathf.Sin(phase * rockFrequency * (style == Style.Tracked ? 0.6f : 1f)) * rockHeight * moving;
                    tilt = Quaternion.Euler(pitch + Mathf.Sin(phase * rockFrequency * 0.5f) * moving, 0f, Mathf.Sin(phase * rockFrequency * 0.7f) * moving * 0.8f);
                    break;
                case Style.Helicopter:
                    offset.y = Mathf.Sin(phase * hoverFrequency) * hoverHeight;
                    tilt = Quaternion.Euler(Mathf.Min(speed, 8f) * 0.9f + pitch, 0f, -bank);
                    break;
                case Style.Hover:
                    offset.y = Mathf.Sin(phase * hoverFrequency * 1.3f) * hoverHeight * 0.8f;
                    tilt = Quaternion.Euler(Mathf.Sin(phase * hoverFrequency) * 2.5f + pitch, 0f, -bank * 0.6f + Mathf.Sin(phase * hoverFrequency * 0.8f) * 2f);
                    break;
                case Style.Drone:
                    offset.y = Mathf.Sin(phase * hoverFrequency * 2.4f) * hoverHeight * 0.7f;
                    tilt = Quaternion.Euler(Mathf.Min(speed, 10f) * 1.2f, 0f, -bank * 1.3f + Mathf.Sin(phase * 7f) * 4f);
                    break;
            }
            offset.x += shake;
            body.localPosition = basePosition + offset;
            body.localRotation = baseRotation * tilt;

            float metres = speed * dt;
            for (int i = 0; i < parts.Count; i++) {
                var part = parts[i];
                float degrees = part.degreesPerMetre * metres + part.degreesPerSecond * dt;
                Vector3 centre = part.transform.parent.TransformPoint(part.localCentre);
                part.transform.RotateAround(centre, body.TransformDirection(part.axis), degrees);
            }
        }
    }
}
