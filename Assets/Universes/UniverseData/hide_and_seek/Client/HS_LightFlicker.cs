using UnityEngine;

namespace Universes.UniverseData.hide_and_seek.Client {
    /// <summary>
    /// Makes the map's candle and lantern lights (<see cref="HS_Map.flickerLights"/>) flicker on this
    /// client, and dims each one's glow sprite (<see cref="HS_Map.flickerGlows"/>) with it, so the flame
    /// and its light move together. One component animates them all; the server never sees it.
    /// </summary>
    public sealed class HS_LightFlicker : MonoBehaviour {
        [Tooltip("How far a slow sway dims the flame (fraction of its intensity).")]
        [SerializeField, Range(0f, 1f)] float sway = 0.25f;
        [Tooltip("How far fast flutter dims the flame (fraction of its intensity).")]
        [SerializeField, Range(0f, 1f)] float flutter = 0.15f;
        [Tooltip("Sway cycles per second; flutter runs a few times faster.")]
        [SerializeField, Min(0.01f)] float speed = 1.6f;
        [SerializeField, Min(1f)] float flutterSpeedRatio = 4.3f;

        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

        Light[] lights;
        Renderer[] glows;
        Color[] glowColor;
        float[] baseIntensity, seeds;
        MaterialPropertyBlock block;

        void Start() {
            var map = GetComponentInParent<HS_Map>();
            lights = map.flickerLights ?? System.Array.Empty<Light>();
            glows = map.flickerGlows ?? System.Array.Empty<Renderer>();
            baseIntensity = new float[lights.Length];
            seeds = new float[lights.Length];
            glowColor = new Color[lights.Length];
            block = new MaterialPropertyBlock();
            for (int i = 0; i < lights.Length; i++) {
                baseIntensity[i] = lights[i].intensity;
                seeds[i] = i * 17.31f;
                Renderer glow = Glow(i);
                if (glow != null) glowColor[i] = glow.sharedMaterial.GetColor(BaseColor);
            }
        }

        Renderer Glow(int i) => i < glows.Length ? glows[i] : null;

        void Update() {
            float t = Time.time * speed;
            for (int i = 0; i < lights.Length; i++) {
                float slow = Mathf.PerlinNoise(t, seeds[i]);
                float fast = Mathf.PerlinNoise(t * flutterSpeedRatio, seeds[i] + 5.7f);
                float level = 1f - sway * slow - flutter * fast;
                lights[i].intensity = baseIntensity[i] * level;
                Renderer glow = Glow(i);
                if (glow == null) continue;
                Color c = glowColor[i];
                block.SetColor(BaseColor, new Color(c.r * level, c.g * level, c.b * level, c.a));
                glow.SetPropertyBlock(block);
            }
        }

        void OnDestroy() {
            if (lights == null) return;
            for (int i = 0; i < lights.Length; i++)
                if (lights[i] != null) lights[i].intensity = baseIntensity[i];
            if (glows != null) foreach (var glow in glows) if (glow != null) glow.SetPropertyBlock(null);
        }
    }
}
