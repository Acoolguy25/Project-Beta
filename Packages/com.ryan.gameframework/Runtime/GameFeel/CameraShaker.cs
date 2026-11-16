using UnityEngine;

namespace RyanAssets.GameFeel {
    /// <summary>
    /// Sums decaying kicks into a noise-driven rotation and offset, and lays it over the main
    /// camera only while that camera renders (restored right after). Whatever moves the camera -
    /// Cinemachine first/third person, a commander or top-down rig, a cutscene - keeps its own
    /// transform untouched, so aiming and raycasts never see the shake.
    /// </summary>
    internal sealed class CameraShaker {
        struct Kick {
            public float strength;
            public float duration;
            public float age;
        }

        readonly GameFeelSettings.CameraShakeTuning tuning;
        readonly Kick[] kicks;
        int kickCount;
        float amplitude;
        Quaternion rotationOffset = Quaternion.identity;
        Vector3 positionOffset;

        Camera shaken;
        Vector3 savedPosition;
        Quaternion savedRotation;

        // Distinct noise rows per axis.
        const float PitchSeed = 11.3f, YawSeed = 47.9f, RollSeed = 83.1f, XSeed = 19.7f, YSeed = 61.2f;

        public CameraShaker(GameFeelSettings.CameraShakeTuning tuning) {
            this.tuning = tuning;
            kicks = new Kick[Mathf.Max(1, tuning.maxKicks)];
        }

        /// <summary>Current combined strength (0..1) before the user's intensity setting.</summary>
        public float Amplitude => amplitude;

        public void Add(float strength, float seconds) {
            strength = Mathf.Clamp01(strength);
            if (strength <= 0f || seconds <= 0f)
                return;
            int slot = kickCount < kicks.Length ? kickCount++ : Weakest();
            kicks[slot] = new Kick { strength = strength, duration = seconds, age = 0f };
        }

        public void Clear() {
            kickCount = 0;
            amplitude = 0f;
        }

        int Weakest() {
            int weakest = 0;
            float min = float.MaxValue;
            for (int i = 0; i < kickCount; i++) {
                float remaining = Remaining(kicks[i]);
                if (remaining < min) {
                    min = remaining;
                    weakest = i;
                }
            }
            return weakest;
        }

        float Remaining(in Kick kick) =>
            kick.strength * Mathf.Pow(Mathf.Clamp01(1f - kick.age / kick.duration), tuning.decayExponent);

        public void Update(float dt) {
            float sum = 0f;
            for (int i = kickCount - 1; i >= 0; i--) {
                kicks[i].age += dt;
                if (kicks[i].age >= kicks[i].duration) {
                    kicks[i] = kicks[--kickCount];
                    continue;
                }
                sum += Remaining(kicks[i]);
            }
            amplitude = Mathf.Clamp01(sum);
            float strength = amplitude * CameraShake.Intensity;
            if (strength <= 0f) {
                rotationOffset = Quaternion.identity;
                positionOffset = Vector3.zero;
                return;
            }
            float t = Time.unscaledTime * tuning.frequency;
            float angle = tuning.maxAngle * strength;
            rotationOffset = Quaternion.Euler(
                Noise(PitchSeed, t) * angle,
                Noise(YawSeed, t) * angle,
                Noise(RollSeed, t) * angle * tuning.roll);
            float offset = tuning.maxOffset * strength;
            positionOffset = new Vector3(Noise(XSeed, t) * offset, Noise(YSeed, t) * offset, 0f);
        }

        static float Noise(float seed, float t) => Mathf.PerlinNoise(seed, t) * 2f - 1f;

        public void Apply(Camera camera) {
            if (shaken != null || camera == null || (positionOffset == Vector3.zero && rotationOffset == Quaternion.identity))
                return;
            Transform t = camera.transform;
            savedPosition = t.position;
            savedRotation = t.rotation;
            Quaternion rotation = savedRotation * rotationOffset;
            t.SetPositionAndRotation(savedPosition + rotation * positionOffset, rotation);
            shaken = camera;
        }

        public void Restore(Camera camera) {
            if (shaken == null || camera != shaken)
                return;
            shaken.transform.SetPositionAndRotation(savedPosition, savedRotation);
            shaken = null;
        }
    }
}
