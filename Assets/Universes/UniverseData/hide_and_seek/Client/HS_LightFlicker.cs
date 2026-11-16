using UnityEngine;

namespace Universes.UniverseData.hide_and_seek.Client {
    /// <summary>
    /// Makes the map's candle and lantern lights (<see cref="HS_Map.flickerLights"/>) flicker on this
    /// client. One component animates them all; the server never sees it.
    /// </summary>
    public sealed class HS_LightFlicker : MonoBehaviour {
        [Tooltip("How far a slow sway dims the flame (fraction of its intensity).")]
        [SerializeField, Range(0f, 1f)] float sway = 0.25f;
        [Tooltip("How far fast flutter dims the flame (fraction of its intensity).")]
        [SerializeField, Range(0f, 1f)] float flutter = 0.15f;
        [Tooltip("Sway cycles per second; flutter runs a few times faster.")]
        [SerializeField, Min(0.01f)] float speed = 1.6f;
        [SerializeField, Min(1f)] float flutterSpeedRatio = 4.3f;

        Light[] lights;
        float[] baseIntensity, seeds;

        void Start() {
            var map = GetComponentInParent<HS_Map>();
            lights = map.flickerLights ?? System.Array.Empty<Light>();
            baseIntensity = new float[lights.Length];
            seeds = new float[lights.Length];
            for (int i = 0; i < lights.Length; i++) {
                baseIntensity[i] = lights[i].intensity;
                seeds[i] = i * 17.31f;
            }
        }

        void Update() {
            float t = Time.time * speed;
            for (int i = 0; i < lights.Length; i++) {
                float slow = Mathf.PerlinNoise(t, seeds[i]);
                float fast = Mathf.PerlinNoise(t * flutterSpeedRatio, seeds[i] + 5.7f);
                lights[i].intensity = baseIntensity[i] * (1f - sway * slow - flutter * fast);
            }
        }

        void OnDestroy() {
            if (lights == null) return;
            for (int i = 0; i < lights.Length; i++)
                if (lights[i] != null) lights[i].intensity = baseIntensity[i];
        }
    }
}
