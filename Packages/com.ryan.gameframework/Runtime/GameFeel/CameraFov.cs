using UnityEngine;

namespace RyanAssets.GameFeel {
    /// <summary>
    /// Field-of-view kicks and a sustained speed boost for whatever the main camera shows. The extra
    /// degrees are added just before the camera renders and taken off right after, so it stacks on
    /// top of any rig (Cinemachine, a game writing <c>Camera.fieldOfView</c>, a fixed camera) and
    /// never fights it. Orthographic cameras are left alone.
    /// <code>
    /// CameraFov.Kick(6f, 0.35f);              // dash, launch pad, boost pickup
    /// CameraFov.SetBoost(sprinting ? 1f : 0f); // sustained, eased (GameFeelSettings boostDegrees at 1)
    /// CameraFov.SetBoostDegrees(12f);         // sustained, in degrees
    /// </code>
    /// </summary>
    public static class CameraFov {
        /// <summary>A quick widening by <paramref name="extraDegrees"/> that eases back over <paramref name="seconds"/>.</summary>
        public static void Kick(float extraDegrees, float seconds = 0.35f) {
            GameFeelRunner runner = GameFeelRunner.Get();
            if (runner != null)
                runner.Fov.Kick(extraDegrees, seconds);
        }

        /// <summary>A sustained widening, 0..1 of the settings' boost degrees (0 eases it away).</summary>
        public static void SetBoost(float amount01) {
            GameFeelRunner runner = amount01 <= 0f ? GameFeelRunner.Existing : GameFeelRunner.Get();
            if (runner != null)
                runner.Fov.SetBoost(Mathf.Clamp01(amount01) * runner.Settings.cameraFov.boostDegrees);
        }

        /// <summary>A sustained widening of <paramref name="extraDegrees"/> (0 eases it away).</summary>
        public static void SetBoostDegrees(float extraDegrees) {
            GameFeelRunner runner = extraDegrees <= 0f ? GameFeelRunner.Existing : GameFeelRunner.Get();
            if (runner != null)
                runner.Fov.SetBoost(extraDegrees);
        }

        /// <summary>The extra degrees being added this frame (kicks plus boost).</summary>
        public static float CurrentExtraDegrees => GameFeelRunner.Existing != null ? GameFeelRunner.Existing.Fov.Extra : 0f;

        /// <summary>Drops every kick and the boost at once (cutscenes, menus).</summary>
        public static void Clear() => GameFeelRunner.Existing?.Fov.Clear();
    }

    /// <summary>The runner's FOV state; applied in beginCameraRendering and undone in endCameraRendering.</summary>
    internal sealed class CameraFovKicker {
        struct KickState {
            public float degrees, age, seconds;
        }

        readonly GameFeelSettings.CameraFovTuning tuning;
        readonly KickState[] kicks = new KickState[6];
        float boostTarget, boost;
        Camera applied;
        float savedFov;

        internal CameraFovKicker(GameFeelSettings.CameraFovTuning tuning) => this.tuning = tuning;

        internal float Extra { get; private set; }

        internal void Kick(float degrees, float seconds) {
            if (degrees == 0f)
                return;
            int slot = 0;
            float weakest = float.PositiveInfinity;
            for (int i = 0; i < kicks.Length; i++) {
                float left = KickValue(kicks[i]);
                if (left == 0f) {
                    slot = i;
                    break;
                }
                if (Mathf.Abs(left) < weakest) {
                    weakest = Mathf.Abs(left);
                    slot = i;
                }
            }
            kicks[slot] = new KickState { degrees = degrees, age = 0f, seconds = Mathf.Max(0.05f, seconds) };
        }

        internal void SetBoost(float degrees) => boostTarget = Mathf.Max(0f, degrees);

        internal void Clear() {
            for (int i = 0; i < kicks.Length; i++)
                kicks[i] = default;
            boostTarget = boost = 0f;
            Extra = 0f;
        }

        float KickValue(in KickState kick) {
            if (kick.seconds <= 0f || kick.age >= kick.seconds)
                return 0f;
            float t = kick.age / kick.seconds;
            float rise = tuning.kickRiseShare;
            float envelope = t < rise ? Mathf.SmoothStep(0f, 1f, t / rise) : 1f - Mathf.SmoothStep(0f, 1f, (t - rise) / (1f - rise));
            return kick.degrees * envelope;
        }

        internal void Update(float dt) {
            float follow = Mathf.Max(0.01f, tuning.boostFollowSeconds);
            boost = Mathf.Lerp(boost, boostTarget, 1f - Mathf.Exp(-dt * 4f / follow));
            if (boostTarget == 0f && boost < 0.01f)
                boost = 0f;
            float total = boost;
            for (int i = 0; i < kicks.Length; i++) {
                if (kicks[i].seconds <= 0f)
                    continue;
                kicks[i].age += dt;
                total += KickValue(kicks[i]);
                if (kicks[i].age >= kicks[i].seconds)
                    kicks[i] = default;
            }
            float max = tuning.maxExtraDegrees;
            Extra = Mathf.Clamp(total, -max, max);
        }

        internal void Apply(Camera camera) {
            if (Extra == 0f || camera == null || camera.orthographic)
                return;
            applied = camera;
            savedFov = camera.fieldOfView;
            camera.fieldOfView = Mathf.Clamp(savedFov + Extra, 1f, 170f);
        }

        internal void Restore(Camera camera) {
            if (applied == null || camera != applied)
                return;
            camera.fieldOfView = savedFov;
            applied = null;
        }
    }
}
