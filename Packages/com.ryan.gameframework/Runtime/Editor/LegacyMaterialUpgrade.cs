using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace RyanAssets.Editor {
    /// <summary>Explicit, folder-scoped conversion for imported built-in materials.
    /// Texture references, UV transforms, tint, normal maps, and cutouts survive.</summary>
    public static class LegacyMaterialUpgrade {
        public static int UpgradeFolder(string folder) {
            if (!AssetDatabase.IsValidFolder(folder)) throw new System.ArgumentException(folder);
            int count = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { folder })) {
                foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GUIDToAssetPath(guid))) {
                    if (asset is Material material && Upgrade(material)) {
                        AssetDatabase.SaveAssetIfDirty(material);
                        count++;
                    }
                }
            }
            return count;
        }

        public static bool Upgrade(Material material) {
            if (material == null || material.shader == null) return false;
            string shaderName = material.shader.name;
            if (shaderName.StartsWith("Universal Render Pipeline/") || shaderName.StartsWith("RyanAssets/")) return false;
            if (shaderName.Contains("Skybox") || shaderName.Contains("Motion Vector")) return false;

            Texture ReadTexture(string property) => material.HasProperty(property) ? material.GetTexture(property) : null;
            float ReadFloat(string property, float fallback) => material.HasProperty(property) ? material.GetFloat(property) : fallback;
            var albedo = ReadTexture("_MainTex");
            var normal = ReadTexture("_BumpMap");
            var metal = ReadTexture("_MetallicGlossMap") ?? ReadTexture("_Spc");
            var occlusion = ReadTexture("_OcclusionMap") ?? ReadTexture("_AO");
            var emission = ReadTexture("_EmissionMap");
            Color tint = material.HasProperty("_Color") ? material.GetColor("_Color") : Color.white;
            Color emissionColor = material.HasProperty("_EmissionColor") ? material.GetColor("_EmissionColor") : Color.black;
            Vector2 scale = material.HasProperty("_MainTex") ? material.GetTextureScale("_MainTex") : Vector2.one;
            Vector2 offset = material.HasProperty("_MainTex") ? material.GetTextureOffset("_MainTex") : Vector2.zero;
            float smoothness = ReadFloat("_Glossiness", 0.25f), metallic = ReadFloat("_Metallic", 0);
            float bumpScale = ReadFloat("_BumpScale", 1), cutoff = ReadFloat("_Cutoff", 0.45f);
            float mode = ReadFloat("_Mode", 0);
            bool cutout = mode == 1 || shaderName.Contains("Leaves") || material.IsKeywordEnabled("_ALPHATEST_ON");
            bool transparent = mode >= 2 || shaderName.Contains("Particles");
            bool water = shaderName.Contains("PBR_Water");
            Shader shader = Shader.Find(water ? "RyanAssets/Water Surface URP" : "Universal Render Pipeline/Lit");
            if (shader == null) throw new System.InvalidOperationException("Missing URP replacement shader.");
            Undo.RecordObject(material, "Upgrade legacy material");
            material.shader = shader;
            material.shaderKeywords = System.Array.Empty<string>();
            if (water) {
                material.SetColor("_BaseColor", new Color(0.045f, 0.12f, 0.13f, 0.82f));
                material.renderQueue = (int)RenderQueue.Transparent;
            } else {
                material.SetTexture("_BaseMap", albedo);
                material.SetTextureScale("_BaseMap", scale);
                material.SetTextureOffset("_BaseMap", offset);
                material.SetColor("_BaseColor", tint);
                material.SetFloat("_Smoothness", smoothness);
                material.SetFloat("_Metallic", metallic);
                material.SetTexture("_BumpMap", normal);
                material.SetFloat("_BumpScale", bumpScale);
                material.SetTexture("_MetallicGlossMap", metal);
                material.SetTexture("_OcclusionMap", occlusion);
                material.SetTexture("_EmissionMap", emission);
                material.SetColor("_EmissionColor", emissionColor);
                if (normal != null) material.EnableKeyword("_NORMALMAP");
                if (metal != null) material.EnableKeyword("_METALLICSPECGLOSSMAP");
                if (occlusion != null) material.EnableKeyword("_OCCLUSIONMAP");
                if (emissionColor.maxColorComponent > 0) material.EnableKeyword("_EMISSION");
                material.SetFloat("_AlphaClip", cutout ? 1 : 0);
                material.SetFloat("_Cutoff", cutoff);
                material.SetFloat("_Cull", cutout ? 0 : 2);
                if (cutout) material.EnableKeyword("_ALPHATEST_ON");
                material.SetFloat("_Surface", transparent ? 1 : 0);
                material.SetFloat("_Blend", 0);
                material.SetFloat("_SrcBlend", transparent ? (int)BlendMode.SrcAlpha : (int)BlendMode.One);
                material.SetFloat("_DstBlend", transparent ? (int)BlendMode.OneMinusSrcAlpha : (int)BlendMode.Zero);
                material.SetFloat("_ZWrite", transparent ? 0 : 1);
                if (transparent) material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                material.SetOverrideTag("RenderType", transparent ? "Transparent" : cutout ? "TransparentCutout" : "Opaque");
                material.renderQueue = transparent ? 3000 : cutout ? 2450 : 2000;
            }
            EditorUtility.SetDirty(material);
            return true;
        }
    }
}
