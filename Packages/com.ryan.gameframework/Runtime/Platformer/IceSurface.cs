using RyanAssets.Characters.Shared;
using UnityEngine;

namespace RyanAssets.Platformer {
    /// <summary>
    /// Slippery ground. A character standing on this collider (or a child of it) keeps most of its
    /// speed: starting, stopping and turning take a run-up, so it slides toward edges. Movement reads
    /// the traction through <see cref="ISurfaceTraction"/>; nothing runs per frame here.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class IceSurface : MonoBehaviour, ISurfaceTraction {
        [Tooltip("Share of normal grip: 1 is ordinary ground, 0.15 is ice that takes about a second to stop on.")]
        [Range(0.02f, 1f)] public float traction = 0.15f;

        public float Traction => traction;

        public void Configure(float grip) => traction = Mathf.Clamp(grip, 0.02f, 1f);
    }
}
