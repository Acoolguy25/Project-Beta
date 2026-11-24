using UnityEngine;

namespace Universes.UniverseData.obby.Client {
    /// <summary>
    /// Marks a landmark prefab (floating castle, volcano, shattered planet...) and tells the scenery
    /// planner how much room it needs. The prefab's origin is the centre of its base island's top.
    /// A landmark can sink into the haze (<see cref="SetHaze"/>) when the player is in another tier.
    /// </summary>
    public sealed class OB_Landmark : MonoBehaviour {
        public OB_LandmarkKind kind;
        [Tooltip("Radius of the landmark's footprint around its origin (m); the course and other scenery keep this far off.")]
        [Min(1f)] public float radius = 60f;
        [Tooltip("Height of its highest point above its origin (m); birds circle above it.")]
        [Min(1f)] public float height = 100f;

        static readonly int FogScale = Shader.PropertyToID("_FogScale");
        static readonly int Opacity = Shader.PropertyToID("_Opacity");
        /// <summary>Above this much haze a landmark's smoke and sparks stop (the ones in the air drift out).</summary>
        const float EmitterCutoff = 0.5f;

        Renderer[] renderers;
        float[] authored;
        ParticleSystem[] emitters;
        MaterialPropertyBlock block;
        float shownHaze = -1f;

        /// <summary>
        /// Sinks the landmark into the haze: 0 shows it as authored, 1 draws its solid parts with
        /// <paramref name="hazedFogScale"/> (thicker fog), fades its streams out and stops its smoke.
        /// </summary>
        public void SetHaze(float amount, float hazedFogScale) {
            amount = Mathf.Clamp01(amount);
            if (Mathf.Approximately(amount, shownHaze)) return;
            shownHaze = amount;
            CacheParts();
            for (int i = 0; i < renderers.Length; i++) {
                Renderer part = renderers[i];
                if (authored[i] < 0f) continue;
                part.GetPropertyBlock(block);
                if (part.sharedMaterial.HasFloat(FogScale)) block.SetFloat(FogScale, Mathf.Lerp(authored[i], hazedFogScale, amount));
                else block.SetFloat(Opacity, authored[i] * (1f - amount));
                part.SetPropertyBlock(block);
            }
            bool emit = amount < EmitterCutoff;
            foreach (var emitter in emitters) {
                if (emit && !emitter.isEmitting) emitter.Play(false);
                else if (!emit && emitter.isEmitting) emitter.Stop(false, ParticleSystemStopBehavior.StopEmitting);
            }
        }

        /// <summary>Finds the parts haze applies to: solid parts fog more, streams fade; others (particles) are left alone.</summary>
        void CacheParts() {
            if (renderers != null) return;
            block = new MaterialPropertyBlock();
            renderers = GetComponentsInChildren<Renderer>(true);
            authored = new float[renderers.Length];
            for (int i = 0; i < renderers.Length; i++) {
                Material material = renderers[i].sharedMaterial;
                authored[i] = renderers[i] is ParticleSystemRenderer || material == null ? -1f
                    : material.HasFloat(FogScale) ? material.GetFloat(FogScale)
                    : material.HasFloat(Opacity) ? material.GetFloat(Opacity)
                    : -1f;
            }
            emitters = GetComponentsInChildren<ParticleSystem>(true);
        }
    }
}
