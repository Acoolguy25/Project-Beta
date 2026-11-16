using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace RyanAssets.GameFeel.Editor {
    /// <summary>
    /// The kit's procedural textures (soft dot, smoke puff, ring, cross, paper, coin, beam, vignette,
    /// speed lines), written as PNGs so they import like any other art. Deterministic: a rebuild
    /// writes identical files.
    /// </summary>
    internal static class GameFeelTextures {
        public const string Folder = GameFeelAuthoring.Generated + "/Textures";

        public static Texture2D SoftDot => Load("fx_soft_dot");
        public static Texture2D Smoke => Load("fx_smoke");
        public static Texture2D Ring => Load("fx_ring");
        public static Texture2D Cross => Load("fx_cross");
        public static Texture2D Paper => Load("fx_paper");
        public static Texture2D Coin => Load("fx_coin");
        public static Texture2D Beam => Load("fx_beam");
        public static Sprite Vignette => AssetDatabase.LoadAssetAtPath<Sprite>(Folder + "/screen_vignette.png");
        public static Sprite SpeedLines => AssetDatabase.LoadAssetAtPath<Sprite>(Folder + "/screen_speed_lines.png");

        public static void BuildAll() {
            Directory.CreateDirectory(Folder);
            Write("fx_soft_dot", 64, 64, (u, v) => {
                float r = Radius(u, v);
                float a = Mathf.Pow(Mathf.Clamp01(1f - r), 2.2f);
                return White(a);
            });
            Write("fx_smoke", 128, 128, (u, v) => {
                float r = Radius(u, v);
                float n = Fbm(u * 4f, v * 4f, 4);
                float edge = Mathf.Clamp01(1f - r * (0.85f + 0.4f * n));
                float a = Mathf.SmoothStep(0f, 1f, edge) * (0.55f + 0.45f * n);
                float shade = Mathf.Lerp(0.78f, 1f, n);
                return new Color(shade, shade, shade, a);
            });
            Write("fx_ring", 128, 128, (u, v) => {
                float r = Radius(u, v);
                float band = 1f - Mathf.Abs(r - 0.82f) / 0.12f;
                float a = Mathf.Pow(Mathf.Clamp01(band), 1.5f) + Mathf.Pow(Mathf.Clamp01(1f - Mathf.Abs(r - 0.7f) / 0.3f), 3f) * 0.25f;
                return White(r > 1f ? 0f : Mathf.Clamp01(a));
            });
            Write("fx_cross", 64, 64, (u, v) => {
                float x = Mathf.Abs(u * 2f - 1f), y = Mathf.Abs(v * 2f - 1f);
                const float arm = 0.26f, length = 0.86f, soft = 0.08f;
                float horizontal = Box(x, length, soft) * Box(y, arm, soft);
                float vertical = Box(x, arm, soft) * Box(y, length, soft);
                float glow = Mathf.Pow(Mathf.Clamp01(1f - Radius(u, v)), 3f) * 0.35f;
                return White(Mathf.Clamp01(Mathf.Max(horizontal, vertical) + glow));
            });
            Write("fx_paper", 16, 16, (u, v) => {
                float x = Mathf.Abs(u * 2f - 1f), y = Mathf.Abs(v * 2f - 1f);
                return White(Box(x, 0.8f, 0.15f) * Box(y, 0.8f, 0.15f));
            });
            Write("fx_coin", 64, 64, (u, v) => {
                float r = Radius(u, v);
                float body = Box(r, 0.86f, 0.06f);
                // Rim highlight and an embossed inner ring read as a coin even when tiny.
                float rim = Mathf.Clamp01(1f - Mathf.Abs(r - 0.8f) / 0.07f);
                float inner = Mathf.Clamp01(1f - Mathf.Abs(r - 0.55f) / 0.05f);
                float light = Mathf.Lerp(0.72f, 1f, Mathf.Clamp01((v - u) * 0.6f + 0.6f));
                float shade = Mathf.Clamp01(light - inner * 0.2f + rim * 0.15f);
                return new Color(shade, shade, shade, body);
            });
            Write("fx_beam", 32, 128, (u, v) => {
                float x = Mathf.Abs(u * 2f - 1f);
                float core = Mathf.Pow(Mathf.Clamp01(1f - x), 2.5f);
                float fadeBottom = Mathf.SmoothStep(0f, 1f, v / 0.08f);
                float fadeTop = Mathf.Pow(Mathf.Clamp01(1f - v), 1.3f);
                return White(core * fadeBottom * fadeTop);
            });
            Write("screen_vignette", 256, 256, (u, v) => {
                // Nearly full at the middle of each screen edge, clear over the centre ~60%.
                float r = Radius(u, v) / Mathf.Sqrt(2f) * 1.35f;
                float a = Mathf.Pow(Mathf.Clamp01((r - 0.38f) / 0.62f), 1.4f);
                return White(a);
            }, sprite: true);
            BuildSpeedLines();
            AssetDatabase.Refresh();
        }

        static void BuildSpeedLines() {
            const int lines = 110;
            var random = new System.Random(1234);
            var angles = new float[lines];
            var widths = new float[lines];
            var starts = new float[lines];
            for (int i = 0; i < lines; i++) {
                angles[i] = (float)(random.NextDouble() * Math.PI * 2.0);
                widths[i] = Mathf.Lerp(0.004f, 0.012f, (float)random.NextDouble());
                starts[i] = Mathf.Lerp(0.42f, 0.7f, (float)random.NextDouble());
            }
            Write("screen_speed_lines", 512, 512, (u, v) => {
                float x = u * 2f - 1f, y = v * 2f - 1f;
                float r = Mathf.Sqrt(x * x + y * y);
                float angle = Mathf.Atan2(y, x);
                float a = 0f;
                for (int i = 0; i < lines; i++) {
                    float d = Mathf.Abs(Mathf.DeltaAngle(angle * Mathf.Rad2Deg, angles[i] * Mathf.Rad2Deg)) * Mathf.Deg2Rad;
                    // Lines get wider towards the edge, like rays from the centre.
                    float w = widths[i] * (0.4f + r);
                    if (d > w)
                        continue;
                    float along = Mathf.SmoothStep(0f, 1f, (r - starts[i]) / 0.3f);
                    a = Mathf.Max(a, (1f - d / w) * along);
                }
                return White(a);
            }, sprite: true);
        }

        static Texture2D Load(string name) => AssetDatabase.LoadAssetAtPath<Texture2D>(Folder + "/" + name + ".png");

        static float Radius(float u, float v) {
            float x = u * 2f - 1f, y = v * 2f - 1f;
            return Mathf.Sqrt(x * x + y * y);
        }

        /// <summary>1 inside <paramref name="half"/>, fading to 0 over <paramref name="soft"/>.</summary>
        static float Box(float x, float half, float soft) => Mathf.Clamp01((half - x) / soft + 0.5f);

        static Color White(float alpha) => new(1f, 1f, 1f, alpha);

        // Small deterministic value noise for the smoke puff.
        static float Hash(int x, int y) {
            unchecked {
                int h = x * 374761393 + y * 668265263;
                h = (h ^ (h >> 13)) * 1274126177;
                return (h & 0xFFFF) / 65535f;
            }
        }

        static float ValueNoise(float x, float y) {
            int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y);
            float tx = x - xi, ty = y - yi;
            tx = tx * tx * (3f - 2f * tx);
            ty = ty * ty * (3f - 2f * ty);
            float a = Mathf.Lerp(Hash(xi, yi), Hash(xi + 1, yi), tx);
            float b = Mathf.Lerp(Hash(xi, yi + 1), Hash(xi + 1, yi + 1), tx);
            return Mathf.Lerp(a, b, ty);
        }

        static float Fbm(float x, float y, int octaves) {
            float sum = 0f, amplitude = 0.5f, total = 0f;
            for (int i = 0; i < octaves; i++) {
                sum += ValueNoise(x, y) * amplitude;
                total += amplitude;
                x *= 2f;
                y *= 2f;
                amplitude *= 0.5f;
            }
            return sum / total;
        }

        static void Write(string name, int width, int height, Func<float, float, Color> pixel, bool sprite = false) {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            var pixels = new Color32[width * height];
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    pixels[y * width + x] = pixel((x + 0.5f) / width, (y + 0.5f) / height);
            texture.SetPixels32(pixels);
            texture.Apply();
            byte[] png = texture.EncodeToPNG();
            UnityEngine.Object.DestroyImmediate(texture);

            string path = Folder + "/" + name + ".png";
            if (!File.Exists(path) || !BytesEqual(File.ReadAllBytes(path), png))
                File.WriteAllBytes(path, png);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = sprite ? TextureImporterType.Sprite : TextureImporterType.Default;
            if (sprite)
                importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.mipmapEnabled = !sprite;
            importer.sRGBTexture = true;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
        }

        static bool BytesEqual(byte[] a, byte[] b) {
            if (a.Length != b.Length)
                return false;
            for (int i = 0; i < a.Length; i++)
                if (a[i] != b[i])
                    return false;
            return true;
        }
    }
}
