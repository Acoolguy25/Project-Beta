using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace RyanAssets.UI.Editor {
    /// <summary>
    /// Creates the assets behind the shared <see cref="UITheme"/>: the theme asset itself, the Inter
    /// TMP font assets and the rounded sprites. Safe to run repeatedly; existing assets (and any
    /// colours tweaked in the theme) are kept.
    /// </summary>
    public static class UIThemeAssets {
        public const string Root = "Packages/com.ryan.ui/Runtime/UI/Theme";
        public const string ThemePath = Root + "/Resources/" + UITheme.ResourcePath + ".asset";
        public const string IconFolder = Root + "/Icons";
        public const string GameIconFolder = Root + "/GameIcons";

        /// <summary>Corner radius, in pixels, drawn into <c>Sprites/ui_rounded.png</c>.</summary>
        public const float RoundedSpriteRadius = 24f;
        /// <summary>Blur margin, in pixels, around the rounded shape in <c>Sprites/ui_shadow.png</c>.</summary>
        public const float ShadowSpriteMargin = 36f;

        // ASCII, Latin-1 and the punctuation the UI uses. Other characters (some player names) fall
        // back to TMP's default font, so the atlases never change at runtime.
        const string FontCharacters =
            " !\"#$%&'()*+,-./0123456789:;<=>?@ABCDEFGHIJKLMNOPQRSTUVWXYZ[\\]^_`abcdefghijklmnopqrstuvwxyz{|}~" +
            "¡¢£¤¥¦§¨©ª«¬®¯°±²³´µ¶·¸¹º»¼½¾¿ÀÁÂÃÄÅÆÇÈÉÊËÌÍÎÏÐÑÒÓÔÕÖ×ØÙÚÛÜÝÞßàáâãäåæçèéêëìíîïðñòóôõö÷øùúûüýþÿ" +
            "•…–—‘’“”€™←→↑↓✓";

        [MenuItem("Ryan/UI/Update Theme Assets")]
        public static UITheme Ensure() {
            Sprite rounded = ImportSprite(Root + "/Sprites/ui_rounded.png", 28);
            Sprite shadow = ImportSprite(Root + "/Sprites/ui_shadow.png", 63);
            TMP_FontAsset medium = FontAsset("Inter-Medium");
            TMP_FontAsset semiBold = FontAsset("Inter-SemiBold");
            TMP_FontAsset bold = FontAsset("Inter-Bold");
            ShadowMaterial(medium);
            ShadowMaterial(semiBold);
            ShadowMaterial(bold);

            var theme = AssetDatabase.LoadAssetAtPath<UITheme>(ThemePath);
            if (theme == null) {
                Directory.CreateDirectory(Path.GetDirectoryName(ThemePath)!);
                theme = ScriptableObject.CreateInstance<UITheme>();
                AssetDatabase.CreateAsset(theme, ThemePath);
            }
            // Only fill empty slots, so a swapped font or sprite survives a rebuild.
            if (theme.regularFont == null) theme.regularFont = medium;
            if (theme.semiBoldFont == null) theme.semiBoldFont = semiBold;
            if (theme.boldFont == null) theme.boldFont = bold;
            if (theme.roundedSprite == null) theme.roundedSprite = rounded;
            if (theme.shadowSprite == null) theme.shadowSprite = shadow;
            EditorUtility.SetDirty(theme);
            AssetDatabase.SaveAssets();
            return theme;
        }

        public static UITheme Theme => AssetDatabase.LoadAssetAtPath<UITheme>(ThemePath) ?? Ensure();

        /// <summary>
        /// The icon called <paramref name="name"/>: a Material icon from <see cref="IconFolder"/>, or a
        /// game-icons.net illustration from <see cref="GameIconFolder"/> (see its CREDITS.md). Both sets
        /// are white so an Image tints them. Add icons through Tools/UIIcons/icons.json.
        /// </summary>
        public static Sprite Icon(string name) {
            string path = $"{IconFolder}/icon_{name}.png";
            if (!File.Exists(path)) {
                string game = $"{GameIconFolder}/icon_{name}.png";
                if (!File.Exists(game))
                    throw new FileNotFoundException($"No UI icon '{name}' in {IconFolder} or {GameIconFolder} (add it to Tools/UIIcons/icons.json)", path);
                path = game;
            }
            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            return sprite != null ? sprite : ImportSprite(path, 0);
        }

        /// <summary>Material that draws <paramref name="font"/> with a soft drop shadow, for text straight over the game.</summary>
        public static Material ShadowMaterial(TMP_FontAsset font) {
            string path = $"{Root}/Fonts/{font.name} Shadow.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null) {
                material = new Material(font.material) { name = font.name + " Shadow" };
                AssetDatabase.CreateAsset(material, path);
            }
            material.SetTexture("_MainTex", font.atlasTexture);
            material.EnableKeyword("UNDERLAY_ON");
            material.SetColor("_UnderlayColor", new Color(0f, 0f, 0f, 0.85f));
            material.SetFloat("_UnderlayOffsetX", 0.25f);
            material.SetFloat("_UnderlayOffsetY", -0.35f);
            material.SetFloat("_UnderlayDilate", 0.25f);
            material.SetFloat("_UnderlaySoftness", 0.45f);
            EditorUtility.SetDirty(material);
            return material;
        }

        static Sprite ImportSprite(string path, int border) {
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            if (importer == null)
                throw new FileNotFoundException("Missing UI texture", path);
            Vector4 borders = Vector4.one * border;
            bool changed = importer.textureType != TextureImporterType.Sprite
                || importer.spriteBorder != borders
                || importer.mipmapEnabled
                || !importer.alphaIsTransparency;
            if (changed) {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.spritePixelsPerUnit = 100f;
                importer.spriteBorder = borders;
                importer.mipmapEnabled = false;
                importer.alphaIsTransparency = true;
                importer.filterMode = FilterMode.Bilinear;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        /// <summary>A static SDF font asset for one of the bundled Inter weights (created once).</summary>
        static TMP_FontAsset FontAsset(string face) {
            string path = $"{Root}/Fonts/{face} SDF.asset";
            var asset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
            if (asset != null)
                return asset;

            var source = AssetDatabase.LoadAssetAtPath<Font>($"{Root}/Fonts/{face}.otf");
            if (source == null)
                throw new FileNotFoundException("Missing font", $"{Root}/Fonts/{face}.otf");
            asset = TMP_FontAsset.CreateFontAsset(source, 64, 6, GlyphRenderMode.SDFAA, 1024, 2048, AtlasPopulationMode.Dynamic, false);
            asset.name = face + " SDF";
            if (!asset.TryAddCharacters(FontCharacters, out string missing))
                Debug.LogWarning($"{face}: no room or no glyph for \"{missing}\"");
            asset.atlasPopulationMode = AtlasPopulationMode.Static;
            AssetDatabase.CreateAsset(asset, path);
            asset.atlasTexture.name = face + " Atlas";
            AssetDatabase.AddObjectToAsset(asset.atlasTexture, asset);
            asset.material.name = face + " Material";
            AssetDatabase.AddObjectToAsset(asset.material, asset);
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();
            return asset;
        }
    }
}
