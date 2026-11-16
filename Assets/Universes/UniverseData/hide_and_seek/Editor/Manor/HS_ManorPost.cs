using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Universes.UniverseData.hide_and_seek.Editor.Manor {
    /// <summary>
    /// The manor's post-processing: a neutral tonemap with a slightly warm grade, a soft vignette that closes the frame
    /// in, and bloom so lamps and candle flames glow. Saved as a VolumeProfile asset and put on a global
    /// Volume in the start scene (below the game feel kit's moods, which win if a game applies one).
    /// Ambient occlusion is a renderer feature, not a volume setting (see the phase B notes).
    /// </summary>
    public static class HS_ManorPost {
        public const float Priority = 5f;

        // Grade
        const float PostExposure = 0.3f, Contrast = 14f, Saturation = 6f, Temperature = 10f;
        static readonly Color ColorFilter = new(1f, 0.97f, 0.92f);
        // Vignette
        const float VignetteIntensity = 0.34f, VignetteSmoothness = 0.42f;
        // Bloom: a threshold just under white so only the flames, shades and sunlit glass glow.
        const float BloomThreshold = 0.9f, BloomIntensity = 0.6f, BloomScatter = 0.65f;
        static readonly Color BloomTint = new(1f, 0.85f, 0.7f);

        public static VolumeProfile BuildProfile(string path) {
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
            if (profile == null) {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, path);
            }
            // Neutral, not ACES: this project grades in low dynamic range, where ACES roughly halves the
            // brightness of the lamp-lit rooms and the dusk interiors turn unreadable in play.
            Override<Tonemapping>(profile).mode.Override(TonemappingMode.Neutral);
            var color = Override<ColorAdjustments>(profile);
            color.postExposure.Override(PostExposure);
            color.contrast.Override(Contrast);
            color.saturation.Override(Saturation);
            color.colorFilter.Override(ColorFilter);
            Override<WhiteBalance>(profile).temperature.Override(Temperature);
            var vignette = Override<Vignette>(profile);
            vignette.intensity.Override(VignetteIntensity);
            vignette.smoothness.Override(VignetteSmoothness);
            vignette.color.Override(Color.black);
            var bloom = Override<Bloom>(profile);
            bloom.threshold.Override(BloomThreshold);
            bloom.intensity.Override(BloomIntensity);
            bloom.scatter.Override(BloomScatter);
            bloom.tint.Override(BloomTint);
            EditorUtility.SetDirty(profile);
            return profile;
        }

        /// <summary>A global volume in the open scene showing <paramref name="profile"/>.</summary>
        public static Volume AddVolume(VolumeProfile profile) {
            var volume = new GameObject("PostProcessing").AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = Priority;
            volume.sharedProfile = profile;
            return volume;
        }

        static T Override<T>(VolumeProfile profile) where T : VolumeComponent {
            if (!profile.TryGet(out T component)) {
                component = profile.Add<T>(true);
                component.name = typeof(T).Name;
                AssetDatabase.AddObjectToAsset(component, profile);
            }
            component.active = true;
            return component;
        }
    }
}
