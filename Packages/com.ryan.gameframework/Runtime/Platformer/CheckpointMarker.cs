using UnityEngine;

namespace RyanAssets.Platformer {
    /// <summary>
    /// Presentation for a checkpoint: swaps the flag material once the local player has reached it.
    /// Reaching it is decided by the game's server; this only shows the result.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CheckpointMarker : MonoBehaviour {
        public Renderer[] flagRenderers;
        public Material idleMaterial, reachedMaterial;
        public ParticleSystem reachedEffect;
        public AudioSource reachedAudio;
        bool reached;

        public bool Reached => reached;

        public void SetReached(bool value, bool celebrate) {
            if (reached == value) return;
            reached = value;
            Material material = value ? reachedMaterial : idleMaterial;
            if (material != null)
                foreach (var r in flagRenderers) if (r != null) r.sharedMaterial = material;
            if (value && celebrate) {
                if (reachedEffect != null) reachedEffect.Play();
                if (reachedAudio != null) reachedAudio.Play();
            }
        }
    }
}
