using UnityEngine;
using System.Collections.Generic;

namespace Universes.UniverseData.dot_invaders.Client {
    /// <summary>One moving troop: follows the server's snapshots and glows in its team colour.</summary>
    public sealed class DI_DotView : MonoBehaviour {
#if !UNITY_SERVER
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int ColorId = Shader.PropertyToID("_Color");
        static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        [Tooltip("Emission as a multiple of the team colour; above 1 blooms. Needs _EMISSION on the material (DI_Authoring).")]
        [SerializeField, Min(0f)] float glow = 1.8f;

        // Keep a small snapshot buffer instead of repeatedly easing to each
        // packet, which made troops accelerate and brake ten times a second.
        const double InterpolationDelay = 0.15;
        readonly List<(double time, Vector3 position)> samples = new(4);
        MaterialPropertyBlock colorBlock;
        Renderer dotRenderer;

        void Awake() {
            dotRenderer = GetComponentInChildren<Renderer>();
            colorBlock = new MaterialPropertyBlock();
        }

        public void SetState(Vector3 position, Color color) {
            if (samples.Count == 0) {
                transform.position = position;
            }
            samples.Add((Time.unscaledTimeAsDouble, position));
            if (samples.Count > 8)
                samples.RemoveAt(0);

            if (dotRenderer != null) {
                dotRenderer.GetPropertyBlock(colorBlock);
                colorBlock.SetColor(BaseColorId, color);
                colorBlock.SetColor(ColorId, color);
                colorBlock.SetColor(EmissionColorId, color * glow);
                dotRenderer.SetPropertyBlock(colorBlock);
            }
        }

        void Update() {
            Interpolate(Time.unscaledTimeAsDouble - InterpolationDelay);
        }

        void Interpolate(double renderTime) {
            while (samples.Count > 2 && samples[1].time <= renderTime)
                samples.RemoveAt(0);
            if (samples.Count < 2)
                return;
            var from = samples[0];
            var to = samples[1];
            float progress = to.time > from.time ? (float)((renderTime - from.time) / (to.time - from.time)) : 1f;
            transform.position = Vector3.Lerp(from.position, to.position, progress);
        }
#endif
    }
}
