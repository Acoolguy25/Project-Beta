using RyanAssets.GameFeel;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Universes.UniverseData.classic_horror.Client {
    /// <summary>
    /// The look of the settlement on this client: the night lighting, the shared Horror mood (film
    /// grain, vignette, desaturation), the map's practical lamps flickering as the Presence closes in,
    /// lights-out scares, and dust motes that drift through the flashlight beam. Presentation only.
    /// </summary>
    public sealed class CH_Atmosphere : MonoBehaviour {
        public CH_NightLighting lighting = new();
        [Tooltip("The shared post-processing look applied while the case runs.")]
        [SerializeField] MoodPreset mood = MoodPreset.Horror;
        [SerializeField, Min(0f)] float moodFadeSeconds = 2.5f;

        [Header("Practical lamps")]
        [Tooltip("Lamp intensity with nothing nearby. The lantern glass glows in proportion to its light.")]
        [SerializeField, Min(0f)] float lampIntensity = 4f;
        [Tooltip("Lowest flicker intensity when the Presence is right on top of you.")]
        [SerializeField, Min(0f)] float lampFlickerFloor = 0.2f;
        [SerializeField, Min(0f)] float lampFlickerRange = 3f;
        [SerializeField, Min(0.1f)] float lampFlickerSpeed = 7f;
        [Tooltip("How quickly lamps come back after a lights-out scare.")]
        [SerializeField, Min(0.05f)] float lightsReturnSeconds = 0.6f;

        [Tooltip("Lamp brightness during the Hush, as a share of normal.")]
        [SerializeField, Range(0.05f, 1f)] float hushLampLevel = 0.3f;

        [Header("Flashlight dust")]
        [Tooltip("Dust motes kept around the camera; lit particles, so they only show inside a light.")]
        public ParticleSystem dustMotesPrefab;
        [SerializeField] Vector3 dustOffset = new(0f, 0f, 3f);

        // Shape of the lamps' stutter while they come back after a lights-out.
        const float StutterSpeed = 23f, StutterLowAmount = 0.3f;

        static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        CH_Map map;
        ParticleSystem dustMotes;
        Color[] glowColors = new Color[0];
        MaterialPropertyBlock glowBlock;
        float threat, lightsOutUntil = -100f, hushUntil = -100f;

        public float LightsOutAmount { get; private set; }
        /// <summary>The lamps' resting intensity; the map lighting menu writes it into the scene so edit mode matches play.</summary>
        public float LampIntensity => lampIntensity;

        /// <summary>0 = the Presence is far away, 1 = it is right here. Drives the lamp flicker.</summary>
        public void SetThreat(float intensity) => threat = Mathf.Clamp01(intensity);

        /// <summary>Every lamp goes out for <paramref name="seconds"/> and then stutters back.</summary>
        public void LightsOut(float seconds) {
            lightsOutUntil = Time.time + Mathf.Max(0.1f, seconds);
        }

        /// <summary>The Hush: the lamps sink to a third and breathe slowly for <paramref name="seconds"/>.</summary>
        public void Hush(float seconds) {
            hushUntil = Time.time + Mathf.Max(0.5f, seconds);
        }

        void Awake() {
            map = GetComponentInParent<CH_Map>();
        }

        void Start() {
            CacheGlows();
            lighting.Apply();
            DynamicGI.UpdateEnvironment();
            MoodVolume.Apply(mood, moodFadeSeconds);
            // Under the map, not the HUD canvas, so the canvas's scale never reaches the particles.
            if (dustMotesPrefab != null) dustMotes = Instantiate(dustMotesPrefab, map != null ? map.transform : null);
        }

        void LateUpdate() {
            UpdateLightsOut();
            UpdateLamps();
            FollowCamera();
            DitherCamera();
        }

        Camera ditheredCamera;

        /// <summary>
        /// Turns on the camera's output dithering. The night is very dark and the mist very faint, so without it the
        /// 8-bit framebuffer shows the mist and the water as hard-edged bands.
        /// </summary>
        void DitherCamera() {
            Camera main = Camera.main;
            if (main == null || main == ditheredCamera) return;
            ditheredCamera = main;
            main.GetUniversalAdditionalCameraData().dithering = true;
        }

        void UpdateLightsOut() {
            if (Time.time < lightsOutUntil) {
                LightsOutAmount = 1f;
                return;
            }
            // Lamps stutter back on over lightsReturnSeconds rather than snapping.
            float recovery = Mathf.Clamp01((Time.time - lightsOutUntil) / lightsReturnSeconds);
            bool dipped = Mathf.PerlinNoise(Time.time * StutterSpeed, 0.5f) > 0.5f;
            LightsOutAmount = (1f - recovery) * (dipped ? 1f : StutterLowAmount);
        }

        /// <summary>1 outside a Hush; during one, a slow breathing dim around <see cref="hushLampLevel"/>.</summary>
        float HushDimming(float seed) {
            if (Time.time >= hushUntil) return 1f;
            float breath = 0.5f + 0.5f * Mathf.Sin(Time.time * 1.6f + seed);
            return Mathf.Lerp(hushLampLevel * 0.6f, hushLampLevel, breath);
        }

        /// <summary>Remembers each lamp glass's authored emission, so the flicker can scale it.</summary>
        void CacheGlows() {
            if (map == null || map.practicalGlows == null) return;
            glowBlock = new MaterialPropertyBlock();
            glowColors = new Color[map.practicalGlows.Length];
            for (int i = 0; i < glowColors.Length; i++) {
                Renderer glow = map.practicalGlows[i];
                Material material = glow != null ? glow.sharedMaterial : null;
                glowColors[i] = material != null && material.HasProperty(EmissionColorId) ? material.GetColor(EmissionColorId) : Color.black;
            }
        }

        void UpdateLamps() {
            if (map == null || map.practicalLights == null) return;
            for (int i = 0; i < map.practicalLights.Length; i++) {
                Light lamp = map.practicalLights[i];
                if (lamp == null) continue;
                float flicker = lampFlickerFloor + Mathf.PerlinNoise(Time.time * lampFlickerSpeed, lamp.transform.position.x) * lampFlickerRange;
                lamp.intensity = Mathf.Lerp(lampIntensity, flicker, threat) * (1f - LightsOutAmount) * HushDimming(lamp.transform.position.x);
                SetGlow(i, lampIntensity > 0f ? lamp.intensity / lampIntensity : 0f);
            }
        }

        /// <summary>The lamp's glass follows its light, so a flicker or a lights-out is seen on the lamp itself.</summary>
        void SetGlow(int index, float amount) {
            if (index >= glowColors.Length) return;
            Renderer glow = map.practicalGlows[index];
            if (glow == null) return;
            glow.GetPropertyBlock(glowBlock);
            glowBlock.SetColor(EmissionColorId, glowColors[index] * amount);
            glow.SetPropertyBlock(glowBlock);
        }

        void FollowCamera() {
            if (dustMotes == null) return;
            Camera main = Camera.main;
            if (main == null) return;
            Transform view = main.transform;
            dustMotes.transform.SetPositionAndRotation(view.TransformPoint(dustOffset), view.rotation);
        }

        void OnDestroy() {
            if (dustMotes != null) Destroy(dustMotes.gameObject);
            // Leave the shared mood for the next game only if this map applied it.
            if (MoodVolume.Active == mood) MoodVolume.Clear(1f);
        }
    }
}
