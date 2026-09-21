using UnityEngine;

namespace Universes.UniverseData.war_valley.Shared {
    /// <summary>
    /// Tints a structure or unit in its commander's colour so ownership is readable at a glance.
    /// <para>
    /// The colour comes from <see cref="WV_Rules.GetCommanderColor"/>, which derives it from the
    /// owning client id, so the server and every client agree without replicating a palette. It is
    /// applied through a <see cref="MaterialPropertyBlock"/> rather than by instancing materials:
    /// every War Valley structure of a kind shares one authored material, and assigning
    /// <c>renderer.material</c> would clone it per instance and break batching.
    /// </para>
    /// </summary>
    [RequireComponent(typeof(WV_Owned))]
    public sealed class WV_OwnerColor : MonoBehaviour {
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int ColorId = Shader.PropertyToID("_Color");

        [Tooltip("Renderers tinted with the owner's colour. Authored on the prefab so decals, " +
                 "effects, and the construction hoarding can be left at their own colours.")]
        [SerializeField] Renderer[] tintedRenderers = System.Array.Empty<Renderer>();

        MaterialPropertyBlock properties;
        WV_Owned owned;

        void Awake() {
            owned = GetComponent<WV_Owned>();
            properties = new MaterialPropertyBlock();
        }

        void OnEnable() {
            owned.OwnerChanged += Apply;
            Apply(owned.OwnerClientId);
        }

        void OnDisable() {
            owned.OwnerChanged -= Apply;
        }

        void Apply(int clientId) {
            if (tintedRenderers.Length == 0)
                return;

            Color tint = WV_Rules.GetOwnerTint(clientId);
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
