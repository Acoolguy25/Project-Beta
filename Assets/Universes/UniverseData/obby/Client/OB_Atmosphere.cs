using System;
using UnityEngine;

namespace Universes.UniverseData.obby.Client {
    /// <summary>
    /// Shifts the sky, sun, ambient light, fog and clouds to match the difficulty tier the local player
    /// is on, blending over a few seconds: a bright morning over the white blocks, a hazy amber sky over
    /// the desert, ash and ember light over the volcano, and a dark neon void at the end.
    /// Changes only runtime copies; the scene's sky material and settings stay as authored.
    /// </summary>
    public sealed class OB_Atmosphere : MonoBehaviour {
        [Serializable]
        public struct Mood {
            public Color zenith, horizon, below, fog, sun;
            [Min(0f)] public float sunIntensity;
            public Color ambientSky, ambientEquator, ambientGround;
            public Color cloudTop, cloudShade;
            [Min(0f)] public float fogStart, fogEnd;

            public static Mood From(in OB_Mood m) => new() {
                zenith = m.zenith, horizon = m.horizon, below = m.below, fog = m.fog, sun = m.sun, sunIntensity = m.sunIntensity,
                ambientSky = m.ambientSky, ambientEquator = m.ambientEquator, ambientGround = m.ambientGround,
                cloudTop = m.cloudTop, cloudShade = m.cloudShade, fogStart = m.fogStart, fogEnd = m.fogEnd
            };

            public static Mood Lerp(in Mood a, in Mood b, float t) => new() {
                zenith = Color.Lerp(a.zenith, b.zenith, t), horizon = Color.Lerp(a.horizon, b.horizon, t),
                below = Color.Lerp(a.below, b.below, t), fog = Color.Lerp(a.fog, b.fog, t), sun = Color.Lerp(a.sun, b.sun, t),
                sunIntensity = Mathf.Lerp(a.sunIntensity, b.sunIntensity, t),
                ambientSky = Color.Lerp(a.ambientSky, b.ambientSky, t), ambientEquator = Color.Lerp(a.ambientEquator, b.ambientEquator, t),
                ambientGround = Color.Lerp(a.ambientGround, b.ambientGround, t),
                cloudTop = Color.Lerp(a.cloudTop, b.cloudTop, t), cloudShade = Color.Lerp(a.cloudShade, b.cloudShade, t),
                fogStart = Mathf.Lerp(a.fogStart, b.fogStart, t), fogEnd = Mathf.Lerp(a.fogEnd, b.fogEnd, t)
            };
        }

        [Tooltip("One mood per difficulty tier, indexed by OB_Tier.")]
        public Mood[] moods;
        [Tooltip("Seconds a change of tier takes to blend in.")]
        [Min(0.01f)] public float blendSeconds = 3f;

        static readonly int Zenith = Shader.PropertyToID("_ZenithColor");
        static readonly int Horizon = Shader.PropertyToID("_HorizonColor");
        static readonly int Below = Shader.PropertyToID("_BelowColor");
        static readonly int SunColor = Shader.PropertyToID("_SunColor");
        static readonly int CloudTop = Shader.PropertyToID("_ObbyCloudTop");
        static readonly int CloudShade = Shader.PropertyToID("_ObbyCloudShade");

        Material sky, authoredSky;
        Light sun;
        Mood from, current;
        int target = -1;
        float blendStart;
        bool blending;

        void Start() {
            if (moods != null && moods.Length > 0) {
                current = from = moods[0];
                target = 0;
                Apply(current);
            }
        }

        void OnDestroy() {
            Shader.SetGlobalColor(CloudTop, Color.clear);
            Shader.SetGlobalColor(CloudShade, Color.clear);
            if (sky != null) {
                if (RenderSettings.skybox == sky) RenderSettings.skybox = authoredSky;
                Destroy(sky);
            }
        }

        /// <summary>Blends toward the mood of <paramref name="tier"/>.</summary>
        public void SetTier(OB_Tier tier) {
            if (moods == null || moods.Length == 0) return;
            int index = Mathf.Clamp((int)tier, 0, moods.Length - 1);
            if (index == target) return;
            from = current;
            target = index;
            blendStart = Time.unscaledTime;
            blending = true;
        }

        void Update() {
            if (!blending) return;
            float t = Mathf.Clamp01((Time.unscaledTime - blendStart) / blendSeconds);
            current = Mood.Lerp(from, moods[target], t * t * (3f - 2f * t));
            Apply(current);
            blending = t < 1f;
        }

        /// <summary>
        /// Swaps the active scene's sky for a runtime copy the first time it is needed. This runs lazily
        /// because the client prefab spawns while its scene is still loading, before it is the active
        /// scene whose sky and sun RenderSettings report.
        /// </summary>
        void EnsureSceneBindings() {
            if (sun == null) sun = RenderSettings.sun;
            Material active = RenderSettings.skybox;
            if (active == null || active == sky) return;
            if (sky != null) Destroy(sky);
            authoredSky = active;
            sky = new Material(active) { name = active.name + " (tiers)" };
            RenderSettings.skybox = sky;
        }

        void Apply(in Mood mood) {
            EnsureSceneBindings();
            if (sky != null) {
                sky.SetColor(Zenith, mood.zenith);
                sky.SetColor(Horizon, mood.horizon);
                sky.SetColor(Below, mood.below);
                sky.SetColor(SunColor, mood.sun);
            }
            if (sun != null) {
                sun.color = mood.sun;
                sun.intensity = mood.sunIntensity;
            }
            RenderSettings.ambientSkyColor = mood.ambientSky;
            RenderSettings.ambientEquatorColor = mood.ambientEquator;
            RenderSettings.ambientGroundColor = mood.ambientGround;
            RenderSettings.fogColor = mood.fog;
            RenderSettings.fogStartDistance = mood.fogStart;
            RenderSettings.fogEndDistance = mood.fogEnd;
            Shader.SetGlobalColor(CloudTop, mood.cloudTop.WithAlpha(1f));
            Shader.SetGlobalColor(CloudShade, mood.cloudShade.WithAlpha(1f));
        }
    }
}
