using FishNet.Object;
using FishNet.Object.Synchronizing;
using RyanAssets.Shared.Declarations;
using System;
using System.Collections.Generic;
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
            List<int> availableVariants = new();
            for (int i = 0; i < shaderVariants.Length; i++) {
                if (shaderVariants[i] != null && GetVariantColor(shaderVariants[i]) != TeamColor.Green)
                    availableVariants.Add(i);
            }

            Debug.Assert(availableVariants.Count > 0, "No non-green robot shader variants are set.");
            if (availableVariants.Count == 0)
                return;

            ApplyVariant(availableVariants[UnityEngine.Random.Range(0, availableVariants.Count)]);
        }

        public bool ApplyColor(TeamColor color) {
            for (int i = 0; i < shaderVariants.Length; i++) {
                if (shaderVariants[i] != null && GetVariantColor(shaderVariants[i]) == color) {
                    ApplyVariant(i);
                    return true;
                }
            }

            Debug.LogError($"No robot shader variant is configured for {color}.", this);
            return false;
        }

        static TeamColor GetVariantColor(Material material) {
            string colorName = material.name.StartsWith("Robot", StringComparison.OrdinalIgnoreCase)
                ? material.name.Substring("Robot".Length)
                : material.name;
            colorName = colorName.Split(' ')[0];
            return Enum.TryParse(colorName, true, out TeamColor color) ? color : TeamColor.None;
        }

        void ApplyVariant(int materialIndex) {
            currentVariant.Value = materialIndex;
            ApplyVariant(shaderVariants[materialIndex]);
        }
#else
        public override void OnStartClient() {
            currentVariant.OnChange += OnCurrentVariantChanged;
            OnCurrentVariantChanged(default, currentVariant.Value, false);
        }
        void OnCurrentVariantChanged(int oldVariant, int newVariant, bool asServer) {
            if (newVariant >= 0 && newVariant < shaderVariants.Length)
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
