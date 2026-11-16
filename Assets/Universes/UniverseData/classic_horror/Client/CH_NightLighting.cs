using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace Universes.UniverseData.classic_horror.Client {
    /// <summary>
    /// The settlement's night: a cool moon, a little ambient light so ground and walls read as shapes
    /// instead of pure black, and exponential fog that swallows the distance. One source of truth for
    /// the map scene (<c>Ryan &gt; Classic Horror &gt; Configure Realtime Map Lighting</c>) and for the
    /// running client (<see cref="CH_Atmosphere"/>), which re-applies it when the map loads.
    /// </summary>
    [Serializable]
    public sealed class CH_NightLighting {
        [Tooltip("Night sky. Applied with the rest so the client never shows another scene's daytime sky.")]
        public Material skybox;
        [Tooltip("Flat ambient light. Keep it low, but above black: unlit ground should still read as ground.")]
        [ColorUsage(false, true)] public Color ambient = new(0.15f, 0.17f, 0.22f);
        [Tooltip("Moonlight colour on the scene's directional light.")]
        public Color moonColor = new(0.55f, 0.66f, 0.85f);
        [Tooltip("Moonlight intensity. The original 0.025 left everything outside a lamp or flashlight black; with 0.2 the ground "
            + "30 m from any lantern still read as black with the flashlight off.")]
        [Min(0f)] public float moonIntensity = 0.35f;
        public Color fogColor = new(0.026f, 0.036f, 0.052f);
        [Tooltip("Exponential-squared fog density: 0.02 hides things past about 60 m.")]
        [Range(0f, 0.1f)] public float fogDensity = 0.02f;
        [Tooltip("Skybox reflections on wet surfaces. Low, or puddles glow at night.")]
        [Range(0f, 1f)] public float reflectionIntensity = 0.1f;

        /// <summary>Writes the lighting into the active scene's RenderSettings and its sun.</summary>
        public void Apply() {
            if (skybox != null) RenderSettings.skybox = skybox;
            // Reflections come from the night sky, never a stale daylight cubemap.
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
            RenderSettings.customReflectionTexture = null;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = ambient;
            RenderSettings.ambientIntensity = 1f;
            RenderSettings.reflectionIntensity = reflectionIntensity;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = fogColor;
            RenderSettings.fogDensity = fogDensity;
            if (RenderSettings.sun == null) return;
            RenderSettings.sun.color = moonColor;
            RenderSettings.sun.intensity = moonIntensity;
        }
    }
}
