using UnityEngine;

namespace RyanAssets.Platformer {
    /// <summary>
    /// A coloured block that is solid while its <see cref="OnOffChannels"/> channel is in one state
    /// and a see-through outline in the other. Blocks of the two colours on one channel swap whenever a
    /// switch on that channel is hit.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class OnOffBlock : MonoBehaviour {
        public int channel;
        [Tooltip("Solid once the channel is flipped (the second colour) instead of from the start.")]
        public bool solidWhenFlipped;
        public Renderer[] visuals;
        public Material solidMaterial, ghostMaterial;

        Collider[] solids;

        public bool IsSolid => OnOffChannels.IsFlipped(channel) == solidWhenFlipped;

        public void Configure(int blockChannel, bool flippedColour, Material solid, Material ghost) {
            channel = blockChannel;
            solidWhenFlipped = flippedColour;
            solidMaterial = solid;
            ghostMaterial = ghost;
            Apply();
        }

        void Awake() => solids = GetComponents<Collider>();

        void OnEnable() {
            OnOffChannels.Changed += OnChanged;
            OnOffChannels.Reset += Apply;
            Apply();
        }

        void OnDisable() {
            OnOffChannels.Changed -= OnChanged;
            OnOffChannels.Reset -= Apply;
        }

        void OnChanged(int changed) {
            if (changed == channel) Apply();
        }

        void Apply() {
            if (solids == null) return;
            bool solid = IsSolid;
            foreach (var c in solids) c.enabled = solid;
#if !UNITY_SERVER
            Material material = solid ? solidMaterial : ghostMaterial;
            if (material == null) return;
            foreach (var r in visuals) if (r != null) r.sharedMaterial = material;
#endif
        }
    }
}
