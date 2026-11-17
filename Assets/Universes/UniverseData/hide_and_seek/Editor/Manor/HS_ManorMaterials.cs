using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Universes.UniverseData.hide_and_seek.Editor.Manor {
    /// <summary>
    /// URP Lit materials for each <see cref="Surface"/>, made from Poly Haven's CC0 texture sets in
    /// Assets/ThirdParty/PolyHaven/Textures (colour, OpenGL normal and a packed mask map). Box faces are
    /// UV-mapped 0..1, so each material is tiled for the size of the box it goes on; materials are
    /// cached per surface and (rounded) tiling, so a rebuild reuses them.
    /// </summary>
    public sealed class HS_ManorMaterials {
        const string TextureRoot = "Assets/ThirdParty/PolyHaven/Textures";

        /// <summary>How each surface looks: texture set, metres one texture repeat covers, tint and shine.</summary>
        readonly struct Finish {
            public readonly string texture;
            public readonly float metres, smoothness;
            public readonly Color tint;

            public Finish(string texture, float metres, Color tint, float smoothness = 1f) {
                this.texture = texture; this.metres = metres; this.tint = tint; this.smoothness = smoothness;
            }
        }

        static readonly Color White = Color.white;

        static readonly Dictionary<Surface, Finish> Finishes = new() {
            [Surface.Grass] = new("leafy_grass", 4f, new Color(0.92f, 0.95f, 0.8f)),
            [Surface.Path] = new("patio_tiles", 2f, White),
            [Surface.Driveway] = new("cobblestone_floor_01", 3f, new Color(0.9f, 0.88f, 0.85f)),
            [Surface.Parquet] = new("diagonal_parquet", 2f, White),
            [Surface.Laminate] = new("laminate_floor_02", 2.5f, White),
            [Surface.Tiles] = new("interior_tiles", 2f, White),
            [Surface.Checker] = new("floor_tiles_06", 2f, new Color(1f, 0.97f, 0.92f)),
            [Surface.Planks] = new("brown_planks_05", 2.5f, new Color(0.86f, 0.7f, 0.52f)),
            [Surface.DarkPlanks] = new("dark_wooden_planks", 2.5f, new Color(1f, 0.9f, 0.78f)),
            [Surface.Plaster] = new("white_stucco", 3f, new Color(1f, 0.95f, 0.86f)),
            [Surface.Brick] = new("large_red_bricks", 2.5f, White),
            [Surface.Roof] = new("clay_roof_tiles", 2.5f, new Color(0.8f, 0.55f, 0.45f)),
            [Surface.Trim] = new("brown_planks_05", 2f, new Color(1f, 0.96f, 0.9f)),
        };

        readonly string folder;
        readonly Dictionary<string, Material> cache = new();
        Material glass;

        /// <param name="folder">Project folder the generated materials are saved in.</param>
        public HS_ManorMaterials(string folder) {
            this.folder = folder.TrimEnd('/');
            if (!AssetDatabase.IsValidFolder(this.folder)) {
                string parent = System.IO.Path.GetDirectoryName(this.folder)!.Replace('\\', '/');
                AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(this.folder));
            }
        }

        /// <summary>Tiling for a box: the face that matters is the one across its thinnest axis.</summary>
        public static Vector2 FaceSize(Vector3 size) {
            if (size.y <= size.x && size.y <= size.z) return new Vector2(size.x, size.z);   // slab: top face
            if (size.z <= size.x) return new Vector2(size.x, size.y);                       // wall along x
            return new Vector2(size.z, size.y);                                             // wall along z
        }

        public Material For(Surface surface, Vector3 boxSize) => surface == Surface.Glass ? Glass() : For(surface, FaceSize(boxSize));

        public Material For(Surface surface, Vector2 faceMetres) {
            if (surface == Surface.Glass) return Glass();
            Finish finish = Finishes[surface];
            // Round to half repeats so walls of similar sizes share a material.
            Vector2 tiling = new(Mathf.Max(0.5f, Mathf.Round(faceMetres.x / finish.metres * 2f) * 0.5f),
                                 Mathf.Max(0.5f, Mathf.Round(faceMetres.y / finish.metres * 2f) * 0.5f));
            string name = $"{surface}_{tiling.x:0.#}x{tiling.y:0.#}";
            if (cache.TryGetValue(name, out var cached)) return cached;
            var mat = LoadOrCreate(name);
            ApplyFinish(mat, finish, tiling);
            cache[name] = mat;
            return mat;
        }

        Material LoadOrCreate(string name) {
            string path = $"{folder}/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat != null) return mat;
            mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }

        static void ApplyFinish(Material mat, Finish finish, Vector2 tiling) {
            string dir = $"{TextureRoot}/{finish.texture}/{finish.texture}";
            var baseMap = Texture($"{dir}_diff.jpg", TextureImporterType.Default, sRGB: true);
            var normal = Texture($"{dir}_nor_gl.jpg", TextureImporterType.NormalMap, sRGB: false);
            var mask = Texture($"{dir}_mask.png", TextureImporterType.Default, sRGB: false);
            mat.SetTexture("_BaseMap", baseMap);
            mat.SetColor("_BaseColor", finish.tint);
            mat.SetTextureScale("_BaseMap", tiling);
            mat.SetTexture("_BumpMap", normal);
            mat.SetFloat("_BumpScale", 1f);
            mat.EnableKeyword("_NORMALMAP");
            // Mask map: R metallic, G occlusion, A smoothness (see PolyHaven/LICENSE.txt).
            mat.SetTexture("_MetallicGlossMap", mask);
            mat.SetFloat("_SmoothnessTextureChannel", 0f);
            mat.SetFloat("_Smoothness", finish.smoothness);
            mat.EnableKeyword("_METALLICSPECGLOSSMAP");
            mat.SetTexture("_OcclusionMap", mask);
            mat.SetFloat("_OcclusionStrength", 1f);
            mat.EnableKeyword("_OCCLUSIONMAP");
            EditorUtility.SetDirty(mat);
        }

        /// <summary>The kit's additive soft-dot effect material, which the lamp glows are copies of.</summary>
        const string GlowTemplate = "Packages/com.ryan.gameframework/Runtime/GameFeel/Generated/Materials/FxDotAdd.mat";

        /// <summary>An additive soft-dot material in <paramref name="color"/> times <paramref name="intensity"/> (HDR), one per colour.</summary>
        public Material Glow(Color color, float intensity) {
            string name = $"Glow_{ColorUtility.ToHtmlStringRGB(color)}";
            if (cache.TryGetValue(name, out var cached)) return cached;
            var template = AssetDatabase.LoadAssetAtPath<Material>(GlowTemplate)
                ?? throw new System.IO.FileNotFoundException($"[HideAndSeek] Missing {GlowTemplate}. Run Ryan > Game Feel > Rebuild.");
            string path = $"{folder}/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null) {
                mat = new Material(template);
                AssetDatabase.CreateAsset(mat, path);
            } else mat.CopyPropertiesFromMaterial(template);
            mat.shader = template.shader;
            mat.SetColor("_BaseColor", new Color(color.r * intensity, color.g * intensity, color.b * intensity, 1f));
            EditorUtility.SetDirty(mat);
            cache[name] = mat;
            return mat;
        }

        /// <summary>
        /// A warm additive unlit pane for lit windows (seen from outside): <paramref name="color"/> times
        /// <paramref name="intensity"/>, one per colour and strength.
        /// </summary>
        public Material WindowGlow(Color color, float intensity) {
            string name = $"WindowGlow_{ColorUtility.ToHtmlStringRGB(color)}";
            if (cache.TryGetValue(name, out var cached)) return cached;
            string path = $"{folder}/{name}.mat";
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null) {
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.shader = shader;
            mat.SetColor("_BaseColor", new Color(color.r * intensity, color.g * intensity, color.b * intensity, 1f));
            // Transparent, additive (URP's _Blend 2), no depth write: it brightens what is behind the glass.
            mat.SetFloat("_Surface", 1f);
            mat.SetFloat("_Blend", 2f);
            mat.SetFloat("_SrcBlend", (float)BlendMode.One);
            mat.SetFloat("_DstBlend", (float)BlendMode.One);
            mat.SetFloat("_ZWrite", 0f);
            mat.SetFloat("_Cull", (float)CullMode.Back);
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.renderQueue = (int)RenderQueue.Transparent;
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.EnableKeyword("_BLENDMODE_ADD");
            EditorUtility.SetDirty(mat);
            cache[name] = mat;
            return mat;
        }

        Material Glass() {
            if (glass != null) return glass;
            glass = LoadOrCreate("Glass");
            glass.SetColor("_BaseColor", new Color(0.72f, 0.86f, 0.95f, 0.22f));
            glass.SetFloat("_Smoothness", 0.95f);
            glass.SetFloat("_Surface", 1f);
            glass.SetFloat("_Blend", 0f);
            glass.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            glass.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            glass.SetFloat("_ZWrite", 0f);
            glass.SetOverrideTag("RenderType", "Transparent");
            glass.renderQueue = (int)RenderQueue.Transparent;
            glass.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            EditorUtility.SetDirty(glass);
            return glass;
        }

        /// <summary>Loads a texture, fixing its import settings (normal map, linear masks, repeat) once.</summary>
        static Texture2D Texture(string path, TextureImporterType type, bool sRGB) {
            if (AssetImporter.GetAtPath(path) is not TextureImporter importer)
                throw new System.IO.FileNotFoundException($"[HideAndSeek] Missing texture {path}.");
            if (importer.textureType != type || importer.sRGBTexture != sRGB || importer.wrapMode != TextureWrapMode.Repeat || importer.maxTextureSize != 1024) {
                importer.textureType = type;
                importer.sRGBTexture = sRGB;
                importer.wrapMode = TextureWrapMode.Repeat;
                importer.mipmapEnabled = true;
                importer.anisoLevel = 4;
                importer.maxTextureSize = 1024;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }
    }
}
