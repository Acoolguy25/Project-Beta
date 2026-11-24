using System;
using UnityEngine;

namespace Universes.UniverseData.obby.Client {
    /// <summary>
    /// The far horizon: three silhouette rings per tier (cloud banks, green hills, dunes, ice peaks,
    /// volcanic ridges, void shards) kilometres out, drawn without the scene fog so they stay visible
    /// as layered shapes fading into the sky's haze. The rings follow the camera; nearer rings lag a
    /// little towards the course centre, which gives parallax as players move. A change of tier
    /// cross-dissolves to the new tier's rings.
    /// </summary>
    public sealed class OB_HorizonLayers : MonoBehaviour {
        [Serializable]
        public struct Ring {
            public MeshRenderer renderer;
            [Tooltip("0 = locked to the camera (infinitely far); higher values lag towards the course centre (parallax).")]
            [Range(0f, 0.5f)] public float parallax;
            [Tooltip("Colour of the ring's crests.")]
            public Color top;
            [Tooltip("How much the ring fades into the sky's horizon haze (0..1).")]
            [Range(0f, 1f)] public float haze;
        }

        [Serializable]
        public struct TierRings {
            [Tooltip("Rings nearest first.")]
            public Ring[] rings;
            [Tooltip("Colour the rings fade into at their foot.")]
            public Color foot;
            [Tooltip("Emissive rim along the crests; alpha is strength.")]
            public Color crestGlow;
        }

        [Tooltip("Rings per difficulty tier (indexed by OB_Tier).")]
        public TierRings[] tiers;
        [Tooltip("Seconds a change of tier takes to dissolve between horizons.")]
        [Min(0.01f)] public float blendSeconds = 3f;

        static readonly int TopColor = Shader.PropertyToID("_TopColor");
        static readonly int FootColor = Shader.PropertyToID("_FootColor");
        static readonly int CrestGlow = Shader.PropertyToID("_CrestGlow");
        static readonly int Haze = Shader.PropertyToID("_Haze");
        static readonly int Fade = Shader.PropertyToID("_Fade");

        MaterialPropertyBlock block;
        Vector3 anchor;
        int current = -1, previous = -1;
        float blendStart = float.NegativeInfinity;

        /// <summary>The point nearer rings lag towards (the course centre).</summary>
        public void SetAnchor(Vector3 point) => anchor = point;

        public void SetTier(OB_Tier tier, bool instant) {
            if (tiers == null || tiers.Length == 0) return;
            int index = Mathf.Clamp((int)tier, 0, tiers.Length - 1);
            if (index == current) return;
            previous = instant ? -1 : current;
            current = index;
            blendStart = instant ? float.NegativeInfinity : Time.unscaledTime;
            for (int t = 0; t < tiers.Length; t++)
                if (t != current && t != previous) Show(t, 0f);
        }

        void LateUpdate() {
            Camera view = Camera.main;
            if (view != null) Follow(view.transform.position);
        }

        /// <summary>Centres the rings on <paramref name="eye"/> and advances the blend (called each frame; also by preview renders).</summary>
        public void Follow(Vector3 eye) {
            if (current < 0) return;
            float t = Mathf.Clamp01((Time.unscaledTime - blendStart) / blendSeconds);
            Place(current, eye);
            Show(current, t);
            if (previous >= 0) {
                Place(previous, eye);
                Show(previous, 1f - t);
                if (t >= 1f) previous = -1;
            }
        }

        void Place(int tier, Vector3 eye) {
            foreach (var ring in tiers[tier].rings)
                if (ring.renderer != null) ring.renderer.transform.position = Vector3.Lerp(eye, anchor, ring.parallax);
        }

        void Show(int tier, float weight) {
            block ??= new MaterialPropertyBlock();
            var set = tiers[tier];
            if (set.rings == null) return;
            foreach (var ring in set.rings) {
                if (ring.renderer == null) continue;
                bool visible = weight > 0f;
                if (ring.renderer.enabled != visible) ring.renderer.enabled = visible;
                if (!visible) continue;
                block.SetColor(TopColor, ring.top);
                block.SetColor(FootColor, set.foot);
                block.SetColor(CrestGlow, set.crestGlow);
                block.SetFloat(Haze, ring.haze);
                block.SetFloat(Fade, weight);
                ring.renderer.SetPropertyBlock(block);
            }
        }
    }
}
