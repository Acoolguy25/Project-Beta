using RyanAssets.GameFeel;
using UnityEngine;
using UnityEngine.Rendering;

namespace Universes.UniverseData.classic_horror.Client {
    /// <summary>
    /// Transient encounter presentation: the priest's face, a sting, a camera jolt, and a burst of
    /// chromatic aberration and vignette from <see cref="lensVolume"/> that then eases away so the
    /// view recovers cleanly. No input capture or gameplay time changes.
    /// </summary>
    public sealed class CH_Jumpscare : MonoBehaviour {
        [System.Serializable]
        public struct Encounter {
            [Min(0.1f)] public float seconds;
            [Range(0.25f, 3f)] public float stingPitch;
            [Range(0f, 1f)] public float shake;
            [Min(0f)] public float shakeSeconds;
            [Tooltip("Peak weight of the lens volume (chromatic aberration and vignette).")]
            [Range(0f, 1f)] public float lens;
            [Tooltip("Colour the face is multiplied by, so a catch does not look like a warning.")]
            public Color tint;
            [Tooltip("How close the face is cropped (1 = the full portrait).")]
            [Range(1f, 2f)] public float zoom;
        }

        public CanvasGroup overlay;
        public RectTransform face;
        [Tooltip("The face picture, tinted per encounter.")]
        public UnityEngine.UI.Graphic picture;
        public AudioSource sting;
        [Tooltip("Global volume with the scare's chromatic aberration and vignette. Its weight is animated from 0.")]
        public Volume lensVolume;
        [Tooltip("Indexed by CH_ScareKind: warning, discovery, fatal.")]
        public Encounter[] encounters = {
            new() { seconds = 2.25f, stingPitch = 0.85f, shake = 0.55f, shakeSeconds = 0.45f, lens = 0.8f, tint = Color.white, zoom = 1f },
            new() { seconds = 1.75f, stingPitch = 1.25f, shake = 0.4f, shakeSeconds = 0.35f, lens = 0.6f, tint = Color.white, zoom = 1f },
            // The catch: drowned in red and a little closer; at 1.06 x the 1.22 end scale the mouth still sits inside a 16:9 frame.
            new() { seconds = 3.5f, stingPitch = 0.7f, shake = 0.9f, shakeSeconds = 0.7f, lens = 1f, tint = new Color(1f, 0.42f, 0.36f), zoom = 1.06f },
        };
        [Tooltip("Fraction of the encounter the face stays fully visible before fading.")]
        [Range(0f, 1f)] public float holdFraction = 0.55f;
        [Tooltip("When the lens effect is gone, as a multiple of the encounter's length. It holds at its peak while the face is up, "
            + "so the world comes back through it as the face fades, and eases out after.")]
        [Range(1f, 3f)] public float lensRecoveryFraction = 1.6f;
        [Tooltip("Scale of the full-screen face when it appears and when it starts to fade: it lunges at the camera.")]
        [SerializeField] Vector2 faceScale = new(1f, 1.22f);
        [SerializeField, Min(0f)] float faceWobbleDegrees = 2.5f;
        [SerializeField, Min(0f)] float faceWobbleSpeed = 7f;

        int lastSequence;
        float started = -100, duration, lensPeak, zoom = 1f;
        public int PlayedCount { get; private set; }
        public bool Playing => Time.unscaledTime - started < duration;

        public void ResetCase() {
            lastSequence = 0;
            started = -100;
            Clear();
        }

        public void Play(int sequence, CH_ScareKind kind) {
            if (sequence <= lastSequence || overlay == null) return;
            lastSequence = sequence;
            // A fatal encounter may replace a warning; warnings never restart an active scare.
            if (kind != CH_ScareKind.Fatal && Playing) return;
            int index = Mathf.Clamp((int)kind, 0, encounters.Length - 1);
            Encounter encounter = encounters[index];
            PlayedCount++;
            started = Time.unscaledTime;
            duration = encounter.seconds;
            lensPeak = encounter.lens;
            zoom = Mathf.Max(1f, encounter.zoom);
            if (picture != null) picture.color = encounter.tint;
            overlay.alpha = 1;
            if (sting != null) { sting.pitch = encounter.stingPitch; sting.Play(); }
            CameraShake.Kick(encounter.shake, encounter.shakeSeconds);
        }

        void Update() {
            if (overlay == null) return;
            float t = (Time.unscaledTime - started) / Mathf.Max(0.01f, duration);
            if (t >= lensRecoveryFraction) {
                if (started > 0f) Clear();
                started = -100f;
                return;
            }
            // One sudden appearance and a fade, without repeated flashes.
            overlay.alpha = 1f - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(holdFraction, 1f, t));
            face.localScale = Vector3.one * (zoom * Mathf.Lerp(faceScale.x, faceScale.y, Mathf.Sqrt(Mathf.Clamp01(t))));
            face.localRotation = Quaternion.Euler(0, 0, Mathf.Sin(t * faceWobbleSpeed) * faceWobbleDegrees);
            if (lensVolume != null)
                lensVolume.weight = lensPeak * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(holdFraction, lensRecoveryFraction, t)));
        }

        /// <summary>Back to a clean view: no face, no sting, no lens effect.</summary>
        void Clear() {
            if (overlay != null) overlay.alpha = 0;
            if (sting != null && sting.isPlaying) sting.Stop();
            if (lensVolume != null) lensVolume.weight = 0f;
        }

        void OnDisable() => Clear();
    }
}
