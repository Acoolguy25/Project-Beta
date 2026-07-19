using UnityEngine;

namespace RyanAssets.Characters.Shared {
    [DisallowMultipleComponent]
    public class RobotColor : MonoBehaviour {
        [SerializeField] private Renderer[] renderers;

        [Header("Material Variants")]
        [SerializeField] private Material[] shaderVariants;
        [SerializeField, Min(0)] private int targetMaterialSlot;
        [SerializeField] private bool randomizeOnStart = true;

        public Material[] ShaderVariants => shaderVariants;

        private void Reset() {
            renderers = GetComponentsInChildren<Renderer>(true);
        }

        private void Start() {
            if (randomizeOnStart)
                ApplyRandomVariant();
        }

        public bool ApplyRandomVariant() {
            if (!TryGetRandomVariant(out Material variant))
                return false;

            ApplyVariant(variant);
            return true;
        }

        public void ApplyVariant(Material variant) {
            if (variant == null)
                return;

            EnsureRenderers();

            foreach (Renderer targetRenderer in renderers) {
                if (targetRenderer == null)
                    continue;

                Material[] materials = targetRenderer.sharedMaterials;
                if (materials == null || materials.Length == 0 || targetMaterialSlot >= materials.Length)
                    continue;

                materials[targetMaterialSlot] = variant;
                targetRenderer.sharedMaterials = materials;
            }
        }

        private bool TryGetRandomVariant(out Material variant) {
            if (shaderVariants != null && shaderVariants.Length > 0) {
                variant = shaderVariants[Random.Range(0, shaderVariants.Length)];
                return variant != null;
            }

            variant = null;
            return false;
        }

        private void EnsureRenderers() {
            if (renderers != null && renderers.Length > 0)
                return;

            renderers = GetComponentsInChildren<Renderer>(true);
        }
    }
}
