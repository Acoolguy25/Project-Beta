using System;
using UnityEngine;

namespace Universes.UniverseData.obby.Client {
    /// <summary>
    /// Shifts the sky, sun, ambient light, fog and clouds to match the difficulty tier the local player
    /// is on, blending over a few seconds: a bright morning with a pale day moon over the white blocks,
    /// a low golden sun over the desert, polar twilight with an aurora over the ice, an eclipse over the
    /// volcano, and a nebula with a ringed planet in the void. It also tells the rest of the world
    /// (horizon, weather, birds) when the tier changes, through <see cref="TierChanged"/>.
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
            [Header("Sky")]
            [Tooltip("Direction towards the sun; the scene's sun light turns to match.")]
            public Vector3 sunDirection;
            [Min(0f)] public float sunRadius, sunHalo;
            [Tooltip("Moon or planet tint; alpha is how visible it is.")]
            public Color moon;
            public Vector3 moonDirection;
            [Min(0f)] public float moonRadius;
            [Tooltip("Planet ring colour; alpha is strength.")]
            public Color ring;
            [Range(0f, 1f)] public float stars;
            public Color auroraA, auroraB;
            [Min(0f)] public float aurora;
            public Color nebulaA, nebulaB;
            [Min(0f)] public float nebula;
            [Tooltip("High cloud colour; alpha is coverage.")]
            public Color cirrus;
            [Tooltip("Gravity field tint for this tier.")]
            public Color field;

            public static Mood From(in OB_Mood m, in OB_SkyStyle s, Color field) => new() {
                zenith = m.zenith, horizon = m.horizon, below = m.below, fog = m.fog, sun = m.sun, sunIntensity = m.sunIntensity,
                ambientSky = m.ambientSky, ambientEquator = m.ambientEquator, ambientGround = m.ambientGround,
                cloudTop = m.cloudTop, cloudShade = m.cloudShade, fogStart = m.fogStart, fogEnd = m.fogEnd,
                sunDirection = OB_SkyStyle.Direction(s.sun), sunRadius = s.sunRadius, sunHalo = s.sunHalo,
                moon = s.moon, moonDirection = OB_SkyStyle.Direction(s.moonAt), moonRadius = s.moonRadius, ring = s.ring,
                stars = s.stars, auroraA = s.auroraA, auroraB = s.auroraB, aurora = s.aurora,
                nebulaA = s.nebulaA, nebulaB = s.nebulaB, nebula = s.nebula, cirrus = s.cirrus, field = field
            };

            public static Mood Lerp(in Mood a, in Mood b, float t) => new() {
                zenith = Color.Lerp(a.zenith, b.zenith, t), horizon = Color.Lerp(a.horizon, b.horizon, t),
                below = Color.Lerp(a.below, b.below, t), fog = Color.Lerp(a.fog, b.fog, t), sun = Color.Lerp(a.sun, b.sun, t),
                sunIntensity = Mathf.Lerp(a.sunIntensity, b.sunIntensity, t),
                ambientSky = Color.Lerp(a.ambientSky, b.ambientSky, t), ambientEquator = Color.Lerp(a.ambientEquator, b.ambientEquator, t),
                ambientGround = Color.Lerp(a.ambientGround, b.ambientGround, t),
                cloudTop = Color.Lerp(a.cloudTop, b.cloudTop, t), cloudShade = Color.Lerp(a.cloudShade, b.cloudShade, t),
                fogStart = Mathf.Lerp(a.fogStart, b.fogStart, t), fogEnd = Mathf.Lerp(a.fogEnd, b.fogEnd, t),
                sunDirection = Vector3.Slerp(a.sunDirection, b.sunDirection, t),
                sunRadius = Mathf.Lerp(a.sunRadius, b.sunRadius, t), sunHalo = Mathf.Lerp(a.sunHalo, b.sunHalo, t),
                // A moon fades out where it is and fades in at its new place, rather than sliding across the sky.
                moon = FadeThrough(a.moon, b.moon, t), moonDirection = t < 0.5f ? a.moonDirection : b.moonDirection,
                moonRadius = t < 0.5f ? a.moonRadius : b.moonRadius, ring = FadeThrough(a.ring, b.ring, t),
                stars = Mathf.Lerp(a.stars, b.stars, t),
                auroraA = Color.Lerp(a.auroraA, b.auroraA, t), auroraB = Color.Lerp(a.auroraB, b.auroraB, t), aurora = Mathf.Lerp(a.aurora, b.aurora, t),
                nebulaA = Color.Lerp(a.nebulaA, b.nebulaA, t), nebulaB = Color.Lerp(a.nebulaB, b.nebulaB, t), nebula = Mathf.Lerp(a.nebula, b.nebula, t),
                cirrus = Color.Lerp(a.cirrus, b.cirrus, t), field = Color.Lerp(a.field, b.field, t)
            };

            /// <summary>Fades <paramref name="a"/> out over the first half and <paramref name="b"/> in over the second.</summary>
            static Color FadeThrough(Color a, Color b, float t) =>
                t < 0.5f ? a.WithAlpha(a.a * (1f - t * 2f)) : b.WithAlpha(b.a * (t * 2f - 1f));
        }

        [Tooltip("One mood per difficulty tier, indexed by OB_Tier.")]
        public Mood[] moods;
        [Tooltip("Seconds a change of tier takes to blend in.")]
        [Min(0.01f)] public float blendSeconds = 3f;

        /// <summary>Raised when the tier changes; the flag is true for an instant switch (no blend).</summary>
        public event Action<OB_Tier, bool> TierChanged;

        static readonly int Zenith = Shader.PropertyToID("_ZenithColor");
        static readonly int Horizon = Shader.PropertyToID("_HorizonColor");
        static readonly int Below = Shader.PropertyToID("_BelowColor");
        static readonly int SunColor = Shader.PropertyToID("_SunColor");
        static readonly int SunDirection = Shader.PropertyToID("_SunDirection");
        static readonly int SunRadius = Shader.PropertyToID("_SunRadius");
        static readonly int SunHalo = Shader.PropertyToID("_SunHalo");
        static readonly int MoonColor = Shader.PropertyToID("_MoonColor");
        static readonly int MoonDirection = Shader.PropertyToID("_MoonDirection");
        static readonly int MoonRadius = Shader.PropertyToID("_MoonRadius");
        static readonly int RingColor = Shader.PropertyToID("_RingColor");
        static readonly int Stars = Shader.PropertyToID("_StarDensity");
        static readonly int AuroraA = Shader.PropertyToID("_AuroraColorA");
        static readonly int AuroraB = Shader.PropertyToID("_AuroraColorB");
        static readonly int Aurora = Shader.PropertyToID("_AuroraStrength");
        static readonly int NebulaA = Shader.PropertyToID("_NebulaColorA");
        static readonly int NebulaB = Shader.PropertyToID("_NebulaColorB");
        static readonly int Nebula = Shader.PropertyToID("_NebulaStrength");
        static readonly int Cirrus = Shader.PropertyToID("_CirrusColor");
        static readonly int CloudTop = Shader.PropertyToID("_ObbyCloudTop");
        static readonly int CloudShade = Shader.PropertyToID("_ObbyCloudShade");
        static readonly int HorizonHaze = Shader.PropertyToID("_ObbyHorizonHaze");
        static readonly int FieldColor = Shader.PropertyToID("_ObbyFieldColor");

        Material sky, authoredSky;
        Light sun;
        Mood from, current;
        int target = -1;
        float blendStart;
        bool blending;

        /// <summary>The tier the atmosphere is showing or blending towards.</summary>
        public OB_Tier Tier => (OB_Tier)Mathf.Max(0, target);

        void Start() {
            if (target < 0) ShowTier(OB_Tier.Basic);
        }

        void OnDestroy() {
            Shader.SetGlobalColor(CloudTop, Color.clear);
            Shader.SetGlobalColor(CloudShade, Color.clear);
            Shader.SetGlobalColor(FieldColor, Color.clear);
            if (sky != null) {
                if (RenderSettings.skybox == sky) RenderSettings.skybox = authoredSky;
                DestroySafely(sky);
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
            TierChanged?.Invoke((OB_Tier)index, false);
        }

        /// <summary>Switches to <paramref name="tier"/> at once (first frame, editor previews).</summary>
        public void ShowTier(OB_Tier tier) {
            if (moods == null || moods.Length == 0) return;
            target = Mathf.Clamp((int)tier, 0, moods.Length - 1);
            current = from = moods[target];
            blending = false;
            Apply(current);
            TierChanged?.Invoke((OB_Tier)target, true);
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
            if (sky != null) DestroySafely(sky);
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
                sky.SetVector(SunDirection, mood.sunDirection);
                sky.SetFloat(SunRadius, mood.sunRadius);
                sky.SetFloat(SunHalo, mood.sunHalo);
                sky.SetColor(MoonColor, mood.moon);
                sky.SetVector(MoonDirection, mood.moonDirection);
                sky.SetFloat(MoonRadius, mood.moonRadius);
                sky.SetColor(RingColor, mood.ring);
                sky.SetFloat(Stars, mood.stars);
                sky.SetColor(AuroraA, mood.auroraA);
                sky.SetColor(AuroraB, mood.auroraB);
                sky.SetFloat(Aurora, mood.aurora);
                sky.SetColor(NebulaA, mood.nebulaA);
                sky.SetColor(NebulaB, mood.nebulaB);
                sky.SetFloat(Nebula, mood.nebula);
                sky.SetColor(Cirrus, mood.cirrus);
            }
            if (sun != null) {
                sun.color = mood.sun;
                sun.intensity = mood.sunIntensity;
                if (mood.sunDirection.sqrMagnitude > 0.0001f) sun.transform.rotation = Quaternion.LookRotation(-mood.sunDirection);
            }
            RenderSettings.ambientSkyColor = mood.ambientSky;
            RenderSettings.ambientEquatorColor = mood.ambientEquator;
            RenderSettings.ambientGroundColor = mood.ambientGround;
            RenderSettings.fogColor = mood.fog;
            RenderSettings.fogStartDistance = mood.fogStart;
            RenderSettings.fogEndDistance = mood.fogEnd;
            Shader.SetGlobalColor(CloudTop, mood.cloudTop.WithAlpha(1f));
            Shader.SetGlobalColor(CloudShade, mood.cloudShade.WithAlpha(1f));
            Shader.SetGlobalColor(HorizonHaze, mood.horizon.WithAlpha(1f));
            Shader.SetGlobalColor(FieldColor, mood.field.WithAlpha(1f));
        }

        static void DestroySafely(UnityEngine.Object target) {
            if (Application.isPlaying) Destroy(target); else DestroyImmediate(target);
        }
    }
}
