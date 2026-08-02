using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

namespace RyanAssets.Characters.Shared {
    [DisallowMultipleComponent]
    public class RobotColor : NetworkBehaviour {
        [SerializeField] private Renderer[] renderers;

        [Header("Material Variants")]
        [SerializeField] private Material[] shaderVariants;
        [SerializeField, Min(0)] private int targetMaterialSlot;
#pragma warning disable CS0414
        [SerializeField] private bool randomizeOnStart = true;
#pragma warning restore CS0414

        readonly SyncVar<int> currentVariant = new();
        public Material[] ShaderVariants => shaderVariants;

#if UNITY_SERVER
        public override void OnStartServer() {
            if (randomizeOnStart)
                ApplyRandomVariant();
        }

        public void ApplyRandomVariant() {
            Debug.Assert(shaderVariants != null && shaderVariants.Length > 0, "Shader variants are not set.");
            int materialIndex = Random.Range(0, shaderVariants.Length);
            currentVariant.Value = materialIndex;
            Material variant = shaderVariants[materialIndex];
            ApplyVariant(variant);
        }
#else
        public override void OnStartClient() {
            currentVariant.OnChange += OnCurrentVariantChanged;
            OnCurrentVariantChanged(default, currentVariant.Value, false);
        }
        void OnCurrentVariantChanged(int oldVariant, int newVariant, bool asServer) {
            ApplyVariant(shaderVariants[newVariant]);
        }
#endif

        public void ApplyVariant(Material variant) {
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
    }
}
