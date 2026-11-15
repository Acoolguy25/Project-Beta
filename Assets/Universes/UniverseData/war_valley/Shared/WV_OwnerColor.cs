using RyanAssets.Core;
using RyanAssets.Shared.Declarations;
using UnityEngine;

namespace Universes.UniverseData.war_valley.Shared {
    /// <summary>
    /// Applies ownership colours to units and structures, together with structure damage tint.
    /// <para>
    /// The building keeps the Cartoon Military art's own colour scheme. Only its primary part - the
    /// surface that reads as its main body, found by <see cref="MeshPrimaryRegion"/> - is painted in
    /// its commander's colour (<see cref="WV_Rules.GetCommanderUIColor"/>, derived from the owning
    /// client id so every machine agrees without a replicated palette). The part is chosen once,
    /// across all of the building's renderers, so a multi-piece model gets one coloured body rather
    /// than a coloured patch on every piece.
    /// </para>
    /// <para>
    /// Battle damage is layered on top: once the structure is finished and falls below
    /// <see cref="WV_Rules.CorrosionHealthFraction"/>, every colour on it - the painted body and the
    /// original art alike - is pulled toward rust, so a building about to go up reads that way from
    /// across the valley. A site under construction is never rusted: it starts at a fraction of its
    /// health by design, and that is not damage.
    /// </para>
    /// <para>
    /// Both live here because both write the same per-material <see cref="MaterialPropertyBlock"/>s,
    /// and a second writer would discard the first one's colour. Property blocks rather than
    /// <c>renderer.material</c> keep every building of a kind on its one shared material.
    /// </para>
    /// </summary>
    [RequireComponent(typeof(WV_Owned))]
    public sealed class WV_OwnerColor : MonoBehaviour {
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int ColorId = Shader.PropertyToID("_Color");
        static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
        static readonly int MainTexId = Shader.PropertyToID("_MainTex");

        [Tooltip("The building's own renderers. Authored on the prefab so decals, effects, and the " +
                 "construction hoarding keep their own colours. The primary part is chosen among these.")]
        [SerializeField] Renderer[] tintedRenderers = System.Array.Empty<Renderer>();

        [Tooltip("Optional explicit body parts using Team Color Lit materials. Their full authored textures receive the commander's tint; tracks, wheels and rotors keep their original materials.")]
        [SerializeField] Renderer[] bodyRenderers = System.Array.Empty<Renderer>();

        [Tooltip("Entity whose health drives the corrosion. Optional: leave unset for a structure " +
                 "that should never rust.")]
        [SerializeField] StructureComponent damageSource;

        MaterialPropertyBlock properties;
        WV_Owned owned;
        WV_Constructable constructable;

        bool prepared;
        /// <summary>
        /// A wall, fence or gate. These get only a light wash of the owner's colour and no flat
        /// painted body: across a long plain run a solid team-colour slab looked like a painted plank.
        /// </summary>
        bool isWall;
        readonly System.Collections.Generic.Dictionary<Renderer, int> paintedSubmeshes = new();
        /// <summary>Each renderer's authored material colours, which rust multiplies from.</summary>
        Color[][] authoredColors;

        void Awake() {
            owned = GetComponent<WV_Owned>();
            constructable = GetComponent<WV_Constructable>();
            // Walls, fences and gates are the obstacles attackers must breach.
            isWall = GetComponent<WV_DestructibleObstacle>() != null;
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

        /// <summary>Unscaled time at which the current hit flash ends; 0 when none is showing.</summary>
        float flashUntil;

        void HandleHealthChanged(long previous, long next, bool asServer) {
            // A finished structure losing health has just been hit: flash it so the player sees
            // which building is taking fire. Construction growth and repairs raise health and never flash.
            if (next < previous && previous > 0 && (constructable == null || constructable.IsOperational))
                flashUntil = Time.unscaledTime + WV_Rules.HitFlashSeconds;
            Apply();
        }

        void Update() {
            if (flashUntil <= 0f)
                return;
            if (Time.unscaledTime >= flashUntil)
                flashUntil = 0f;
            Apply();
        }

        /// <summary>1 at the instant of a hit, fading to 0 over <see cref="WV_Rules.HitFlashSeconds"/>.</summary>
        float Flash => flashUntil <= 0f
            ? 0f
            : Mathf.Clamp01((flashUntil - Time.unscaledTime) / WV_Rules.HitFlashSeconds) * 0.6f;

        /// <summary>
        /// Structures - those without explicit Team Color body parts - are brought onto the shared
        /// palette. Units keep their own art, which their team-colour shader already harmonises.
        /// </summary>
        bool UsesStructurePalette => bodyRenderers.Length == 0;

        /// <summary>Rust pulls a colour toward brown and darkens it, as weathered steel does.</summary>
        static Color Corrode(Color color, float corrosion) {
            if (corrosion <= 0f)
                return color;
            Color rusted = Color.Lerp(color, WV_Rules.CorrodedTint, corrosion);
            float darken = Mathf.Lerp(1f, 0.75f, corrosion);
            rusted.r *= darken;
            rusted.g *= darken;
            rusted.b *= darken;
            rusted.a = color.a;
            return rusted;
        }

        Color Finish(Color color, float corrosion, float flash) {
            color = Corrode(color, corrosion);
            return flash > 0f ? Color.Lerp(color, WV_Rules.HitFlashTint, flash) : color;
        }

        /// <summary>0 while the structure is healthy or still being built, rising to 1 as it dies.</summary>
        float Corrosion {
            get {
                if (damageSource == null || (constructable != null && !constructable.IsOperational))
                    return 0f;
                long max = damageSource.MaxHealth.Value;
                return max <= 0 ? 0f : WV_Rules.GetCorrosion(Mathf.Clamp01(damageSource.Health.Value / (float)max));
            }
        }

        void Apply() {
            // A placement preview is an unspawned copy of the prefab; it keeps the preview's own tint.
            // A headless server draws nothing, so it never pays for the mesh analysis either.
            if (tintedRenderers.Length == 0 || !owned.IsLive || Application.isBatchMode)
                return;
            if (!prepared)
                Prepare();

            float corrosion = Corrosion;
            float flash = Flash;
            bool palette = UsesStructurePalette;
            Color paint = Finish(
                palette ? WV_Rules.GetCommanderPaintColor(owned.OwnerClientId) : WV_Rules.GetCommanderUIColor(owned.OwnerClientId),
                corrosion, flash);

            for (int r = 0; r < tintedRenderers.Length; r++) {
                Renderer renderer = tintedRenderers[r];
                if (renderer == null)
                    continue;
                Color[] colors = authoredColors[r];
                for (int slot = 0; slot < colors.Length; slot++) {
                    properties.Clear();
                    if (paintedSubmeshes.TryGetValue(renderer, out int paintedSlot) && (paintedSlot < 0 || slot == paintedSlot)) {
                        // A split structure atlas region uses flat paint. Explicit unit bodies
                        // retain their authored textures through the Team Color Lit shader.
                        if (paintedSlot >= 0) {
                            properties.SetTexture(BaseMapId, Texture2D.whiteTexture);
                            properties.SetTexture(MainTexId, Texture2D.whiteTexture);
                        }
                        SetColor(paint);
                    } else {
                        Color authored = palette
                            ? WV_Rules.GetStructurePaletteColor(colors[slot], owned.OwnerClientId,
                                isWall ? WV_Rules.WallPaletteStrength : WV_Rules.StructurePaletteStrength)
                            : colors[slot];
                        SetColor(Finish(authored, corrosion, flash));
                    }
                    renderer.SetPropertyBlock(properties, slot);
                }
            }
        }

        void SetColor(Color color) {
            // URP Lit reads _BaseColor and the older Standard path reads _Color. Writing both keeps
            // the colour working across the pack's materials without branching on the shader.
            properties.SetColor(BaseColorId, color);
            properties.SetColor(ColorId, color);
        }

        /// <summary>
        /// Picks the primary part once: the largest body across every tinted renderer, measured in
        /// world space so a big piece scaled down does not outrank a small piece scaled up. When it
        /// lives in a single-material mesh, that renderer draws the shared split copy with its
        /// material in both slots, which keeps the one shared material and its batching.
        /// </summary>
        void Prepare() {
            prepared = true;
            if (bodyRenderers.Length > 0) {
                foreach (Renderer body in bodyRenderers) {
                    if (body != null)
                        paintedSubmeshes[body] = -1; // All slots: the authored shader preserves texture detail.
                }
            } else if (!isWall) {
                float bestArea = 0f;
                Renderer primaryRenderer = null;
                MeshFilter bestFilter = null;
                MeshPrimaryRegion.Result best = default;
                foreach (Renderer renderer in tintedRenderers) {
                    if (renderer == null || !renderer.TryGetComponent(out MeshFilter filter)
                        || !MeshPrimaryRegion.TryGetPrimary(filter.sharedMesh, out var result))
                        continue;
                    Vector3 scale = renderer.transform.lossyScale;
                    float area = result.Area * Mathf.Pow(Mathf.Abs(scale.x * scale.y * scale.z), 2f / 3f);
                    if (area <= bestArea)
                        continue;
                    bestArea = area;
                    bestFilter = filter;
                    best = result;
                    primaryRenderer = renderer;
                }
                if (primaryRenderer != null)
                    PrepareBody(primaryRenderer, bestFilter, best);
            }

            authoredColors = new Color[tintedRenderers.Length][];
            for (int r = 0; r < tintedRenderers.Length; r++) {
                Material[] materials = tintedRenderers[r] != null
                    ? tintedRenderers[r].sharedMaterials
                    : System.Array.Empty<Material>();
                authoredColors[r] = new Color[materials.Length];
                for (int slot = 0; slot < materials.Length; slot++)
                    authoredColors[r][slot] = AuthoredColor(materials[slot]);
            }
        }

        void PrepareBody(Renderer renderer, MeshFilter filter, MeshPrimaryRegion.Result region) {
            paintedSubmeshes[renderer] = region.Submesh;
            if (!region.IsSplit)
                return;
            Material material = renderer.sharedMaterial;
            filter.sharedMesh = region.Mesh;
            renderer.sharedMaterials = new[] { material, material };
        }

        static Color AuthoredColor(Material material) {
            if (material == null)
                return Color.white;
            if (material.HasProperty(BaseColorId))
                return material.GetColor(BaseColorId);
            return material.HasProperty(ColorId) ? material.GetColor(ColorId) : Color.white;
        }
    }
}
