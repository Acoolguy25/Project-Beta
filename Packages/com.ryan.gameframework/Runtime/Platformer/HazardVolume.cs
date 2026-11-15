using UnityEngine;

namespace RyanAssets.Platformer {
    /// <summary>
    /// Marks colliders that send a character back to its checkpoint (lava, spikes, sweeping bars).
    /// The game's server finds these with <see cref="PlatformerQuery"/> against replicated character
    /// positions, so a hazard cannot be ignored by a modified client.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HazardVolume : MonoBehaviour {
        [Tooltip("Optional emissive renderer that pulses on clients.")]
        public Renderer glowRenderer;
        public Color glowColor = new(1f, 0.35f, 0.05f);
        [Min(0f)] public float pulseSpeed = 2.5f;

#if !UNITY_SERVER
        MaterialPropertyBlock block;
        static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");
        void Update() {
            if (glowRenderer == null) return;
            block ??= new MaterialPropertyBlock();
            float pulse = 1.4f + 0.6f * Mathf.Sin(Time.time * pulseSpeed + transform.position.x);
            glowRenderer.GetPropertyBlock(block);
            block.SetColor(EmissionColor, glowColor * pulse);
            glowRenderer.SetPropertyBlock(block);
        }
#endif
    }
}
