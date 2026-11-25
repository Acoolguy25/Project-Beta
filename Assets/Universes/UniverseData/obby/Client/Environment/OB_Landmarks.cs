using System;
using System.Collections.Generic;
using UnityEngine;

namespace Universes.UniverseData.obby.Client {
    /// <summary>
    /// Moves instances of the tiers' landmark prefabs to where the scenery planner put them for the
    /// current course; a long tier stretch shows a kind more than once. Instances are pooled per kind
    /// and reused for every course; ones the course does not need are hidden. Landmarks of tiers other
    /// than the one the player is in sink into the haze (<see cref="ShowTier"/>), so a volcano does not
    /// stand over the ice tier where the two stretches meet.
    /// </summary>
    public sealed class OB_Landmarks : MonoBehaviour {
        [Serializable]
        public struct TierSet {
            [Tooltip("Landmark prefabs in the order of the tier's OB_WorldStyle.landmarks.")]
            public OB_Landmark[] prefabs;
        }

        [Tooltip("Landmark prefabs per difficulty tier (indexed by OB_Tier).")]
        public TierSet[] tiers;
        [Tooltip("Fog scale for the landmarks of tiers the player is not in (the landmark material's own is below 1): at about 3 they are lost in the haze a few hundred metres off.")]
        [Min(0f)] public float otherTierFogScale = 3.5f;
        [Tooltip("Seconds a landmark takes to sink into or come out of the haze when the player changes tier.")]
        [Min(0.01f)] public float hazeSeconds = 3f;

        /// <summary>Pooled instances per tier, then per landmark kind.</summary>
        readonly List<List<OB_Landmark>[]> instances = new();
        /// <summary>How far each tier's landmarks have sunk into the haze (0 shown, 1 hazed).</summary>
        readonly float[] haze = new float[OB_Tiers.Count];
        /// <summary>The tier the player is in, or -1 before the first one is known (everything shown).</summary>
        int viewerTier = -1;

        /// <summary>Hazes out every tier's landmarks but <paramref name="tier"/>'s, over <see cref="hazeSeconds"/> or at once.</summary>
        public void ShowTier(OB_Tier tier, bool instant) {
            viewerTier = (int)tier;
            if (!instant) return;
            for (int t = 0; t < haze.Length; t++) haze[t] = HazeTarget(t);
            ApplyHaze();
        }

        float HazeTarget(int tier) => viewerTier < 0 || tier == viewerTier ? 0f : 1f;

        void Update() {
            bool changed = false;
            float step = Time.unscaledDeltaTime / hazeSeconds;
            for (int t = 0; t < haze.Length; t++) {
                float next = Mathf.MoveTowards(haze[t], HazeTarget(t), step);
                changed |= next != haze[t];
                haze[t] = next;
            }
            if (changed) ApplyHaze();
        }

        void ApplyHaze() {
            for (int t = 0; t < instances.Count; t++) {
                float eased = haze[t] * haze[t] * (3f - 2f * haze[t]);
                foreach (var pool in instances[t])
                    foreach (var landmark in pool)
                        if (landmark.gameObject.activeSelf) landmark.SetHaze(eased, otherTierFogScale);
            }
        }

        /// <summary>Footprint radius (x) and height (y) of a tier's landmark, or zero when it has no prefab.</summary>
        public Vector2 Size(OB_Tier tier, int index) {
            OB_Landmark prefab = Prefab(tier, index);
            return prefab != null ? new Vector2(prefab.radius, prefab.height) : Vector2.zero;
        }

        OB_Landmark Prefab(OB_Tier tier, int index) {
            if (tiers == null || (int)tier >= tiers.Length) return null;
            var set = tiers[(int)tier].prefabs;
            return set != null && index >= 0 && index < set.Length ? set[index] : null;
        }

        /// <summary>Shows the landmarks in <paramref name="placements"/> (other kinds are ignored) and hides the rest.</summary>
        public void Arrange(IReadOnlyList<OB_Placement> placements) {
            EnsurePools();
            foreach (var tier in instances)
                foreach (var pool in tier)
                    foreach (var landmark in pool) landmark.gameObject.SetActive(false);
            var used = new int[OB_Tiers.Count][];
            for (int t = 0; t < used.Length; t++) used[t] = new int[instances[t].Length];
            foreach (var placement in placements) {
                if (placement.kind != (byte)OB_SceneryKind.Landmark) continue;
                int t = (int)placement.tier;
                OB_Landmark prefab = Prefab(placement.tier, placement.variant);
                if (prefab == null) continue;
                var pool = instances[t][placement.variant];
                int index = used[t][placement.variant]++;
                if (index == pool.Count) pool.Add(Instantiate(prefab, transform));
                var landmark = pool[index];
                landmark.transform.SetPositionAndRotation(placement.matrix.GetColumn(3), placement.matrix.rotation);
                landmark.gameObject.SetActive(true);
            }
            ApplyHaze();
        }

        void EnsurePools() {
            if (instances.Count > 0) return;
            for (int t = 0; t < OB_Tiers.Count; t++) {
                var prefabs = tiers != null && t < tiers.Length ? tiers[t].prefabs : null;
                var pools = new List<OB_Landmark>[prefabs?.Length ?? 0];
                for (int i = 0; i < pools.Length; i++) pools[i] = new List<OB_Landmark>();
                instances.Add(pools);
            }
        }
    }
}
