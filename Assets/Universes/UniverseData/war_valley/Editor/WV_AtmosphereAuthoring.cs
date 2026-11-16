using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace Universes.UniverseData.war_valley.Editor {
    /// <summary>
    /// The valley's light and colour, in one place. Every value the atmosphere pass writes comes from
    /// here, so the look is tuned by editing this recipe and re-running the menu.
    /// </summary>
    public sealed class WV_AtmosphereRecipe {
        [Header("Sun")]
        public Color SunColor = new(1f, 0.9f, 0.74f, 1f);
        [Min(0f)] public float SunIntensity = 1.65f;
        [Tooltip("Sun elevation and heading, in degrees. A lower, angled sun rakes the hills and gives " +
                 "the units long shadows that read from the commander's camera.")]
        public Vector3 SunEuler = new(40f, -38f, 0f);
        [Range(0f, 1f)] public float ShadowStrength = 0.82f;

        [Header("Ambient")]
        public Color AmbientSky = new(0.6f, 0.68f, 0.78f, 1f);
        public Color AmbientEquator = new(0.6f, 0.58f, 0.5f, 1f);
        [Tooltip("Lit from below by sunlit grass: lifts the units' and buildings' undersides out of black.")]
        public Color AmbientGround = new(0.4f, 0.4f, 0.34f, 1f);

        [Header("Fog")]
        [Tooltip("A light, warm haze: fights within the commander camera's 40-150 m stay crisp while the " +
                 "far side of the valley and the mountains fall back into depth.")]
        public Color FogColor = new(0.79f, 0.79f, 0.74f, 1f);
        [Min(0f)] public float FogStart = 160f;
        [Min(1f)] public float FogEnd = 1400f;

        [Header("Grading")]
        [Tooltip("Neutral keeps the grass green and the units' team colours true; ACES pushed the whole " +
                 "valley to rust orange when judged in the live Game view.")]
        public TonemappingMode Tonemapping = TonemappingMode.Neutral;
        public float PostExposure = 0.3f;
        [Range(-100f, 100f)] public float Contrast = 20f;
        [Tooltip("Strong enough that the meadow reads green and the team colours pop from the commander's height.")]
        [Range(-100f, 100f)] public float Saturation = 35f;
        public Color ColorFilter = Color.white;
        [Range(-100f, 100f)] public float Temperature = -2f;
        [Tooltip("Slightly toward green, away from the olive-brown cast of the raw ground.")]
        [Range(-100f, 100f)] public float Tint = -6f;
        [Tooltip("Lifts the shadows so units in a building's shade stay readable.")]
        [Range(-1f, 1f)] public float ShadowLift = 0.08f;
        [Range(-1f, 1f)] public float MidtoneGamma = 0.04f;
        [Min(0f)] public float BloomThreshold = 1.1f;
        [Min(0f)] public float BloomIntensity = 0.35f;
        [Range(0f, 1f)] public float BloomScatter = 0.65f;
        [Range(0f, 1f)] public float VignetteIntensity = 0.18f;
        [Range(0.01f, 1f)] public float VignetteSmoothness = 0.45f;
    }

    /// <summary>
    /// Gives the valley warm sunlight, a graded look, and light fog for depth, replacing the flat
    /// brown murk the scene had.
    /// <para>
    /// Writes the start scene's sun and lighting settings and rebuilds the overrides of the scene's
    /// existing global Volume profile (<c>start_map/DaylightProfile.asset</c>), whose overrides had
    /// been lost. The commander camera only draws the grading because the client turns
    /// post-processing on for it (<c>WV_CameraPostProcessing</c>). Run from the client Editor: menu
    /// Ryan/War Valley/Apply Atmosphere.
    /// </para>
    /// </summary>
    public static class WV_AtmosphereAuthoring {
        const string ScenePath = WV_Authoring.Root + "/war_valley_start.unity";
        const string ProfilePath = WV_Authoring.Root + "/start_map/DaylightProfile.asset";
        const string VolumeObjectName = "Daylight Post Processing";
        static readonly int SkyGroundColor = Shader.PropertyToID("_GroundColor");

        [MenuItem("Ryan/War Valley/Apply Atmosphere")]
        public static void ApplyAtmosphere() => Apply(new WV_AtmosphereRecipe());

        public static void Apply(WV_AtmosphereRecipe recipe) {
            if (Application.isPlaying)
                throw new InvalidOperationException("Leave Play Mode before applying the War Valley atmosphere.");

            Scene previous = SceneManager.GetActiveScene();
            Scene scene = SceneManager.GetSceneByPath(ScenePath);
            if (!scene.IsValid() || !scene.isLoaded)
                scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            // RenderSettings belong to the active scene.
            SceneManager.SetActiveScene(scene);
            try {
                VolumeProfile profile = RebuildProfile(recipe);
                ApplySun(scene, recipe);
                ApplyLighting(recipe);
                WV_MapAuthoring.ConfigureGroundLayers();
                BindVolume(scene, profile);
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            } finally {
                if (previous.IsValid() && previous.isLoaded && previous != scene)
                    SceneManager.SetActiveScene(previous);
            }
            Debug.Log("War Valley: applied the atmosphere (sun, ambient, fog, colour grading).");
        }

        static void ApplySun(Scene scene, WV_AtmosphereRecipe recipe) {
            Light sun = null;
            foreach (GameObject root in scene.GetRootGameObjects()) {
                foreach (Light light in root.GetComponentsInChildren<Light>(true)) {
                    if (light.type == LightType.Directional) {
                        sun = light;
                        break;
                    }
                }
                if (sun != null)
                    break;
            }
            if (sun == null)
                throw new InvalidOperationException($"War Valley: no directional light in {ScenePath}.");

            Undo.RecordObject(sun, "War Valley sun");
            Undo.RecordObject(sun.transform, "War Valley sun");
            sun.color = recipe.SunColor;
            sun.intensity = recipe.SunIntensity;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = recipe.ShadowStrength;
            sun.transform.rotation = Quaternion.Euler(recipe.SunEuler);
            RenderSettings.sun = sun;
            EditorUtility.SetDirty(sun);
        }

        static void ApplyLighting(WV_AtmosphereRecipe recipe) {
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = recipe.AmbientSky;
            RenderSettings.ambientEquatorColor = recipe.AmbientEquator;
            RenderSettings.ambientGroundColor = recipe.AmbientGround;

            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = recipe.FogColor;
            RenderSettings.fogStartDistance = recipe.FogStart;
            RenderSettings.fogEndDistance = Mathf.Max(recipe.FogStart + 1f, recipe.FogEnd);

            // Below the horizon the sky shows its ground colour; matching it to the fog keeps the far
            // distance a haze rather than a band (the same rule WV_MapAuthoring applies).
            Material sky = RenderSettings.skybox;
            if (sky != null && sky.HasProperty(SkyGroundColor)) {
                sky.SetColor(SkyGroundColor, recipe.FogColor);
                EditorUtility.SetDirty(sky);
            }
        }

        /// <summary>
        /// Replaces every override in the daylight profile with the recipe's. The profile keeps its
        /// asset and GUID, so the scene's Volume keeps pointing at it.
        /// </summary>
        static VolumeProfile RebuildProfile(WV_AtmosphereRecipe recipe) {
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(ProfilePath);
            if (profile == null) {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, ProfilePath);
            }

            profile.components.Clear();
            foreach (UnityEngine.Object sub in AssetDatabase.LoadAllAssetsAtPath(ProfilePath)) {
                if (sub is VolumeComponent)
                    UnityEngine.Object.DestroyImmediate(sub, true);
            }

            Tonemapping tonemapping = AddOverride<Tonemapping>(profile);
            tonemapping.mode.Override(recipe.Tonemapping);

            ColorAdjustments color = AddOverride<ColorAdjustments>(profile);
            color.postExposure.Override(recipe.PostExposure);
            color.contrast.Override(recipe.Contrast);
            color.saturation.Override(recipe.Saturation);
            color.colorFilter.Override(recipe.ColorFilter);

            LiftGammaGain liftGammaGain = AddOverride<LiftGammaGain>(profile);
            liftGammaGain.lift.Override(new Vector4(1f, 1f, 1f, recipe.ShadowLift));
            liftGammaGain.gamma.Override(new Vector4(1f, 1f, 1f, recipe.MidtoneGamma));

            WhiteBalance balance = AddOverride<WhiteBalance>(profile);
            balance.temperature.Override(recipe.Temperature);
            balance.tint.Override(recipe.Tint);

            Bloom bloom = AddOverride<Bloom>(profile);
            bloom.threshold.Override(recipe.BloomThreshold);
            bloom.intensity.Override(recipe.BloomIntensity);
            bloom.scatter.Override(recipe.BloomScatter);

            Vignette vignette = AddOverride<Vignette>(profile);
            vignette.intensity.Override(recipe.VignetteIntensity);
            vignette.smoothness.Override(recipe.VignetteSmoothness);

            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            return profile;
        }

        static T AddOverride<T>(VolumeProfile profile) where T : VolumeComponent {
            T component = profile.Add<T>(overrides: false);
            component.name = typeof(T).Name;
            component.hideFlags = HideFlags.HideInInspector | HideFlags.HideInHierarchy;
            AssetDatabase.AddObjectToAsset(component, profile);
            return component;
        }

        static void BindVolume(Scene scene, VolumeProfile profile) {
            foreach (GameObject root in scene.GetRootGameObjects()) {
                foreach (Volume volume in root.GetComponentsInChildren<Volume>(true)) {
                    if (volume.gameObject.name != VolumeObjectName)
                        continue;
                    Undo.RecordObject(volume, "War Valley atmosphere");
                    volume.isGlobal = true;
                    volume.sharedProfile = profile;
                    EditorUtility.SetDirty(volume);
                    return;
                }
            }
            throw new InvalidOperationException(
                $"War Valley: no '{VolumeObjectName}' Volume in {ScenePath}; the grading has nothing to show it.");
        }
    }
}
