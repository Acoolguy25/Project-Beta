using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace RyanAssets.GameFeel {
    /// <summary>Two global volumes on the runner that crossfade between mood profiles (<see cref="MoodVolume.Apply"/>).</summary>
    internal sealed class MoodBlender {
        readonly GameFeelSettings.MoodTuning tuning;
        readonly Transform parent;
        Volume current, next;
        float fadeSeconds, fadeAge;
        bool fading;
        Camera camera;
        UniversalAdditionalCameraData cameraData;
        bool forcedPostProcessing;

        public MoodBlender(Transform parent, GameFeelSettings.MoodTuning tuning) {
            this.parent = parent;
            this.tuning = tuning;
        }

        public MoodPreset? Active { get; private set; }

        public void Apply(MoodPreset preset, VolumeProfile profile, float seconds) {
            if (profile == null)
                return;
            EnsureVolumes();
            if (Active == preset && !fading && current.sharedProfile == profile)
                return;
            // The incoming mood goes on the spare volume and fades up while the current one fades out.
            (current, next) = (next, current);
            current.sharedProfile = profile;
            current.weight = seconds > 0f ? 0f : 1f;
            current.enabled = true;
            Active = preset;
            StartFade(seconds);
        }

        public void Clear(float seconds) {
            if (current == null)
                return;
            (current, next) = (next, current);
            current.sharedProfile = null;
            current.weight = 0f;
            current.enabled = false;
            Active = null;
            StartFade(seconds);
        }

        void StartFade(float seconds) {
            fadeSeconds = Mathf.Max(0f, seconds);
            fadeAge = 0f;
            fading = true;
            if (fadeSeconds <= 0f)
                Finish();
        }

        void EnsureVolumes() {
            if (current != null)
                return;
            current = CreateVolume("Mood A");
            next = CreateVolume("Mood B");
        }

        Volume CreateVolume(string name) {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            Volume volume = go.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = tuning.priority;
            volume.weight = 0f;
            volume.enabled = false;
            return volume;
        }

        public void Update(float dt) {
            if (!fading)
                return;
            fadeAge += dt;
            float t = Mathf.Clamp01(fadeAge / fadeSeconds);
            if (current.sharedProfile != null)
                current.weight = t;
            if (next.enabled)
                next.weight = Mathf.Min(next.weight, 1f - t);
            if (t >= 1f)
                Finish();
        }

        /// <summary>
        /// Volumes do nothing on a camera with Post Processing off (obby's camera, for one), so while a mood
        /// is showing the main camera gets it turned on, and back off once the mood is cleared.
        /// </summary>
        public void LateUpdate(Camera main) {
            if (main != camera) {
                ReleaseCamera();
                camera = main;
                cameraData = main != null ? main.GetUniversalAdditionalCameraData() : null;
            }
            if (cameraData == null || !tuning.enablePostProcessing)
                return;
            bool wanted = Active.HasValue || fading;
            if (wanted && !cameraData.renderPostProcessing) {
                cameraData.renderPostProcessing = true;
                forcedPostProcessing = true;
            } else if (!wanted && forcedPostProcessing) {
                ReleaseCamera();
            }
        }

        void ReleaseCamera() {
            if (forcedPostProcessing && cameraData != null)
                cameraData.renderPostProcessing = false;
            forcedPostProcessing = false;
        }

        void Finish() {
            fading = false;
            if (current.sharedProfile != null)
                current.weight = 1f;
            next.weight = 0f;
            next.enabled = false;
            next.sharedProfile = null;
        }
    }
}
