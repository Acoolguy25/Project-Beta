using UnityEngine;

namespace Universes.UniverseData.classic_horror.Client {
    /// <summary>
    /// Makes an evidence record findable in the dark: its rim glow (the Evidence Rim material on
    /// <see cref="rimRenderers"/>) and its lamp breathe slowly, and both brighten while the
    /// investigator's reticle is on it.
    /// </summary>
    public sealed class CH_EvidenceGlow : MonoBehaviour {
        static readonly int RimStrength = Shader.PropertyToID("_RimStrength");

        [Tooltip("Renderers that carry the Evidence Rim material as an extra material slot.")]
        public Renderer[] rimRenderers = new Renderer[0];
        public Light glowLight;
        [SerializeField, Min(0f)] float idleRim = 0.55f;
        [SerializeField, Min(0f)] float focusedRim = 1.8f;
        [SerializeField, Range(0f, 1f)] float pulseDepth = 0.45f;
        [SerializeField, Min(0.01f)] float pulseSeconds = 2.4f;
        [SerializeField, Min(0f)] float idleLight = 0.85f;
        [SerializeField, Min(0f)] float focusedLight = 1.6f;
        [Tooltip("How quickly the glow follows focus changes (per second).")]
        [SerializeField, Min(0.1f)] float focusBlendRate = 6f;

        MaterialPropertyBlock block;
        float focus, phase;
        bool focused;

        public void SetFocused(bool value) => focused = value;

        void Awake() {
            block = new MaterialPropertyBlock();
            // Records found at the same moment should not pulse in lockstep.
            phase = Random.value * pulseSeconds;
        }

        void OnDisable() {
            focused = false;
            focus = 0f;
        }

        void Update() {
            focus = Mathf.MoveTowards(focus, focused ? 1f : 0f, focusBlendRate * Time.deltaTime);
            float pulse = 1f - pulseDepth * (0.5f + 0.5f * Mathf.Sin((Time.time + phase) * Mathf.PI * 2f / pulseSeconds));
            float rim = Mathf.Lerp(idleRim * pulse, focusedRim, focus);
            foreach (Renderer target in rimRenderers) {
                if (target == null) continue;
                target.GetPropertyBlock(block);
                block.SetFloat(RimStrength, rim);
                target.SetPropertyBlock(block);
            }
            if (glowLight != null) glowLight.intensity = Mathf.Lerp(idleLight * pulse, focusedLight, focus);
        }
    }
}
