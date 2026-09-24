using RyanAssets.Shared.Declarations;
using UnityEngine;

namespace Universes.UniverseData.war_valley.Shared {
    /// <summary>
    /// The single authority over a structure's albedo: whose it is, and what shape it is in.
    /// <para>
    /// Ownership comes from <see cref="WV_Rules.GetCommanderColor"/>, which derives it from the
    /// owning client id, so the server and every client agree without replicating a palette. Battle
    /// damage is layered on top of that: past <see cref="WV_Rules.CorrosionHealthFraction"/> the
    /// owner's colour is progressively eaten by rust, so a building that is nearly finished off
    /// reads that way from across the valley rather than only in a health bar.
    /// </para>
    /// <para>
    /// Both live here rather than in two components because both write the same
    /// <see cref="MaterialPropertyBlock"/> on the same renderers, and the second writer would
    /// silently discard the first one's colour. A property block is used rather than
    /// <c>renderer.material</c> because every War Valley structure of a kind shares one authored
    /// material, and assigning the instance material would clone it per structure and break batching.
    /// </para>
    /// </summary>
    [RequireComponent(typeof(WV_Owned))]
    public sealed class WV_OwnerColor : MonoBehaviour {
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int ColorId = Shader.PropertyToID("_Color");

        [Tooltip("Renderers tinted with the owner's colour. Authored on the prefab so decals, " +
                 "effects, and the construction hoarding can be left at their own colours.")]
        [SerializeField] Renderer[] tintedRenderers = System.Array.Empty<Renderer>();

        [Tooltip("Entity whose health drives the corrosion. Optional: leave unset for a structure " +
                 "that should only ever show its owner's colour.")]
        [SerializeField] StructureComponent damageSource;

        MaterialPropertyBlock properties;
        WV_Owned owned;

        void Awake() {
            owned = GetComponent<WV_Owned>();
            properties = new MaterialPropertyBlock();
            if (damageSource == null)
                damageSource = GetComponent<StructureComponent>();
        }

        void OnEnable() {
            owned.OwnerChanged += HandleOwnerChanged;
            if (damageSource != null) {
                damageSource.Health.OnChange += HandleHealthChanged;
                damageSource.MaxHealth.OnChange += HandleHealthChanged;
            }
            Apply();
        }

        void OnDisable() {
            owned.OwnerChanged -= HandleOwnerChanged;
            if (damageSource != null) {
                damageSource.Health.OnChange -= HandleHealthChanged;
                damageSource.MaxHealth.OnChange -= HandleHealthChanged;
            }
        }

        void HandleOwnerChanged(int clientId) => Apply();

        void HandleHealthChanged(long previous, long next, bool asServer) => Apply();

        /// <summary>
        /// 1 while the structure is untouched, falling to 0 as it dies. A structure with no health
        /// source, or one whose health has not been initialized yet, counts as undamaged so a freshly
        /// spawned building is not painted as a rusted wreck for its first frame.
        /// </summary>
        float HealthFraction {
            get {
                if (damageSource == null)
                    return 1f;
                long max = damageSource.MaxHealth.Value;
                return max <= 0 ? 1f : Mathf.Clamp01(damageSource.Health.Value / (float)max);
            }
        }

        void Apply() {
            if (tintedRenderers.Length == 0)
                return;

            Color tint = WV_Rules.GetDamagedTint(owned.OwnerClientId, HealthFraction);
            // URP Lit reads _BaseColor and the older Standard path reads _Color. Writing both keeps
            // the tint working across the pack's materials without branching on the shader.
            properties.SetColor(BaseColorId, tint);
            properties.SetColor(ColorId, tint);

            foreach (Renderer renderer in tintedRenderers) {
                if (renderer != null)
                    renderer.SetPropertyBlock(properties);
            }
        }
    }
}
