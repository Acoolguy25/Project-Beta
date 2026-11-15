using System.Collections.Generic;
using System.IO;
using System.Linq;
using RyanAssets.Characters.Shared;
using RyanAssets.Items.Platform;
using RyanAssets.Platformer;
using TMPro;
using UnityEditor;
using UnityEditor.Build.Profile;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.UI;
#if !UNITY_SERVER
using RyanAssets.Characters.Client;
using Universes.UniverseData.obby.Client;
#endif

namespace Universes.UniverseData.obby.Editor {
    /// <summary>
    /// Rebuilds the obby's textures, materials, fonts, effects, sky backdrop, piece prefabs, client
    /// presentation prefab (HUD, effects, backdrop, post-processing), start scene and build-profile
    /// entries. Colours come from <see cref="OB_Palette"/>. Safe to run again: assets are overwritten
    /// in place, keeping their GUIDs.
    /// Run "Build Server Assets" from an Editor in the Linux Server profile for the server runner.
    /// </summary>
    public static class OB_Authoring {
        public const string Root = "Assets/Universes/UniverseData/obby";
        const string Pieces = Root + "/Prefabs/Pieces";
        const string StartScene = Root + "/obby_start.unity";
        const string ServerScene = Root + "/obby_server.unity";
        const string ServerPrefab = Root + "/Server/OB_ServerRunner.prefab";
        const string ClientPrefab = Root + "/Prefabs/ObbyClient.prefab";
        const string Fx = Root + "/Prefabs/Fx";
        const string Backdrop = Root + "/Prefabs/Backdrop";
        const string Icons = "Packages/com.ryan.ui/Runtime/UI/Icons/Material/";
        const string ThumbnailPath = Root + "/Resources/UniverseDataPub/obby/thumbnail.png";
        const float LobbyTop = 100f;
        const float LobbySize = 18f;

        // ---------------------------------------------------------------- entry points
        [MenuItem("Ryan/Obby/Build Client Assets")]
        public static void BuildClientAssets() {
            if (Application.isPlaying) throw new System.InvalidOperationException("Stop play mode first.");
            EnsureFolders();
            BuildTextures();
            var fx = BuildEffects();
            var prefabs = BuildPieces(fx);
            var client = BuildClient(fx);
            BuildStartScene(prefabs, client);
            RegisterScenes();
            // Assets from the earlier look that nothing references any more.
            foreach (string retired in new[] {
                Root + "/Prefabs/ObbyHUD.prefab", Root + "/Textures/Tile.png",
                Backdrop + "/Island0.prefab", Backdrop + "/Island1.prefab",
                Root + "/Materials/IslandGrass.mat", Root + "/Materials/IslandEarth.mat", Root + "/Materials/IslandTrunk.mat",
                Root + "/Materials/IslandLeaves.mat", Root + "/Materials/IslandLeavesWarm.mat"
            })
                if (AssetDatabase.LoadMainAssetAtPath(retired) != null) AssetDatabase.DeleteAsset(retired);
            AssetDatabase.SaveAssets();
            Debug.Log("[Obby] Client assets built.");
        }

        /// <summary>Batchmode entry: client assets, then a thumbnail render if a GPU device exists.</summary>
        public static void BuildClientAssetsBatch() {
            BuildClientAssets();
            if (SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null) RenderPreviews();
        }

        [MenuItem("Ryan/Obby/Build Server Assets (Linux Server profile)")]
        public static void BuildServerAssets() {
#if UNITY_SERVER
            // The server Editor is usually a ParrelSync clone, which blocks asset saves by default.
            bool? blocked = CloneSaveBlock(null);
            CloneSaveBlock(false);
            try {
                SaveServerAssets();
            } finally {
                if (blocked.HasValue) CloneSaveBlock(blocked.Value);
            }
#else
            throw new System.InvalidOperationException("Switch to the Linux Server build profile (UNITY_SERVER) to build the server runner prefab.");
#endif
        }

        /// <summary>Reads (value null) or sets ParrelSync's cached "block asset saving in clones" preference.</summary>
        static bool? CloneSaveBlock(bool? value) {
            var prefs = System.Type.GetType("ParrelSync.Preferences, ParrelSync");
            var pref = prefs?.GetField("AssetModPref", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)?.GetValue(null);
            var property = pref?.GetType().GetProperty("Value");
            if (property == null) return null;
            if (value.HasValue) property.SetValue(pref, value.Value);
            return (bool)property.GetValue(pref);
        }

#if UNITY_SERVER
        static void SaveServerAssets() {
            EnsureFolders();
            var go = new GameObject("OB_ServerRunner");
            var runner = go.AddComponent<Universes.UniverseData.obby.Server.OB_ServerRunner>();
            // DebugBool switches default to on; ship the runner with real-time timers like the other universes.
            var serialized = new SerializedObject(runner);
            serialized.FindProperty("DebugTimerSpeedUp._editor_value").boolValue = false;
            serialized.FindProperty("DebugTimerInfinite._editor_value").boolValue = false;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, ServerPrefab);
            Object.DestroyImmediate(go);
            if (prefab == null) throw new System.InvalidOperationException("Could not save " + ServerPrefab);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            PrefabUtility.InstantiatePrefab(prefab, scene);
            EditorSceneManager.SaveScene(scene, ServerScene);
            RegisterScenes();
            AssetDatabase.SaveAssets();
            Debug.Log("[Obby] Server assets built.");
        }
#endif

        static void EnsureFolders() {
            foreach (string folder in new[] { "Prefabs", "Prefabs/Pieces", "Prefabs/Fx", "Prefabs/Backdrop", "Materials", "Fonts", "Textures", "Resources", "Resources/UniverseDataPub", "Resources/UniverseDataPub/obby", "Server" }) {
                string path = Root + "/" + folder;
                if (AssetDatabase.IsValidFolder(path)) continue;
                string parent = Path.GetDirectoryName(path).Replace('\\', '/');
                AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
            }
        }

        // ---------------------------------------------------------------- textures
        delegate Color Pixel(float u, float v);

        enum TextureUse { Tiled, Sprite, Particle }

        /// <summary>Renders <paramref name="pixel"/> into a PNG and imports it. Sprites are 9-sliced by <paramref name="border"/> pixels.</summary>
        static Texture2D SaveTexture(string name, Pixel pixel, int size = 128, TextureUse use = TextureUse.Tiled, int border = 0) {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    tex.SetPixel(x, y, pixel((x + 0.5f) / size, (y + 0.5f) / size));
            tex.Apply();
            string path = Root + "/Textures/" + name + ".png";
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.wrapMode = use == TextureUse.Tiled ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
            importer.mipmapEnabled = use != TextureUse.Sprite;
            importer.filterMode = use == TextureUse.Tiled ? FilterMode.Trilinear : FilterMode.Bilinear;
            importer.anisoLevel = use == TextureUse.Tiled ? 4 : 1;
            importer.alphaIsTransparency = use != TextureUse.Tiled;
            if (use == TextureUse.Sprite) {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.spritePixelsPerUnit = 100f;
                importer.spriteBorder = Vector4.one * border;
            }
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static Sprite SpriteAt(string path) => AssetDatabase.LoadAssetAtPath<Sprite>(path);

        static float Hash(float x, float y) {
            float h = Mathf.Sin(x * 127.1f + y * 311.7f) * 43758.5453f;
            return h - Mathf.Floor(h);
        }

        /// <summary>Hermite step from 0 at <paramref name="edge0"/> to 1 at <paramref name="edge1"/> (shader-style smoothstep).</summary>
        static float Smooth(float edge0, float edge1, float x) {
            float t = Mathf.Clamp01((x - edge0) / (edge1 - edge0));
            return t * t * (3f - 2f * t);
        }

        /// <summary>Signed distance (in UV units) from a rounded square of half-size 0.5 - <paramref name="inset"/>.</summary>
        static float RoundedBox(float u, float v, float radius, float inset) {
            float qx = Mathf.Abs(u - 0.5f) - (0.5f - inset - radius);
            float qy = Mathf.Abs(v - 0.5f) - (0.5f - inset - radius);
            float outside = new Vector2(Mathf.Max(qx, 0f), Mathf.Max(qy, 0f)).magnitude;
            return outside + Mathf.Min(Mathf.Max(qx, qy), 0f) - radius;
        }

        static void BuildTextures() {
            // Chevrons pointing along +V, for belts and boost pads.
            SaveTexture("Arrows", (u, v) => {
                float x = Mathf.Abs(u - 0.5f);
                float chevron = Mathf.Repeat(v - x * 0.9f, 0.5f);
                bool arrow = chevron < 0.18f && x < 0.38f;
                return arrow ? OB_Palette.Gold : OB_Palette.Ink;
            });
            SaveTexture("Stripes", (u, v) => Mathf.Repeat(u + v, 0.5f) < 0.25f ? OB_Palette.Gold : OB_Palette.Ink);
            SaveTexture("Lava", (u, v) => {
                float n = 0.5f + 0.25f * Mathf.Sin(u * 18f + Mathf.Sin(v * 9f) * 2f) + 0.25f * Mathf.Sin(v * 14f + Mathf.Cos(u * 7f) * 2f);
                return Color.Lerp(new Color(0.55f, 0.05f, 0.02f), new Color(1f, 0.65f, 0.1f), Mathf.Clamp01(n));
            });
            SaveTexture("Checker", (u, v) => (Mathf.FloorToInt(u * 4) + Mathf.FloorToInt(v * 4)) % 2 == 0 ? OB_Palette.Paper : OB_Palette.Ink);
            // Cracks for crumble blocks, multiplied over the block colour (white = no change).
            SaveTexture("Cracks", (u, v) => {
                float crack = Mathf.Min(Mathf.Abs(u - 0.5f - 0.12f * Mathf.Sin(v * 9f)), Mathf.Abs(v - 0.35f - 0.1f * Mathf.Sin(u * 11f)));
                float c = crack < 0.012f ? 0.45f : crack < 0.03f ? 0.8f : 0.94f + 0.06f * Hash(u, v);
                return new Color(c, c, c, 1);
            });

            // UI: a rounded panel and its soft drop shadow, both white so Image.color tints them.
            SaveTexture("Panel", (u, v) => {
                float d = RoundedBox(u, v, 30f / 96f, 0f) * 96f;
                return new Color(1f, 1f, 1f, Mathf.Clamp01(0.5f - d));
            }, 96, TextureUse.Sprite, 36);
            SaveTexture("PanelShadow", (u, v) => {
                float d = RoundedBox(u, v, 40f / 128f, 18f / 128f) * 128f;
                float a = 1f - Smooth(-6f, 18f, d);
                return new Color(1f, 1f, 1f, a * a);
            }, 128, TextureUse.Sprite, 60);

            // Particles.
            SaveTexture("FxGlow", (u, v) => {
                float r = Mathf.Clamp01(new Vector2(u - 0.5f, v - 0.5f).magnitude * 2f);
                float a = Mathf.Pow(1f - r, 2.2f);
                return new Color(1f, 1f, 1f, a);
            }, 64, TextureUse.Particle);
            SaveTexture("FxSparkle", (u, v) => {
                float x = Mathf.Abs(u - 0.5f) * 2f, y = Mathf.Abs(v - 0.5f) * 2f;
                float r = Mathf.Sqrt(x * x + y * y);
                float rays = Mathf.Max(Mathf.Clamp01(1f - x * 9f) * (1f - y), Mathf.Clamp01(1f - y * 9f) * (1f - x));
                float core = Mathf.Pow(Mathf.Clamp01(1f - r * 2.2f), 2f);
                return new Color(1f, 1f, 1f, Mathf.Clamp01(rays * rays + core));
            }, 64, TextureUse.Particle);
            SaveTexture("FxPuff", (u, v) => {
                float angle = Mathf.Atan2(v - 0.5f, u - 0.5f);
                float edge = 0.86f + 0.08f * Mathf.Sin(angle * 5f) + 0.05f * Mathf.Sin(angle * 9f + 1.3f);
                float r = new Vector2(u - 0.5f, v - 0.5f).magnitude * 2f / edge;
                float a = 1f - Smooth(0.55f, 1f, r);
                float shade = Mathf.Lerp(0.9f, 1f, Mathf.Clamp01(v * 1.2f));
                return new Color(shade, shade, shade, a);
            }, 64, TextureUse.Particle);
        }

        // ---------------------------------------------------------------- materials
        static Texture2D Tex(string name) => AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "/Textures/" + name + ".png");

        static Shader FindShader(string name) => Shader.Find(name) ?? throw new System.InvalidOperationException("Shader not found: " + name);

        /// <summary>Loads the material at <paramref name="path"/>, or creates it; either way it ends up clean on <paramref name="shader"/>.</summary>
        static Material MaterialAsset(string path, Shader shader) {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null) {
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, path);
            } else if (mat.shader != shader) {
                // Switching shader: drop the old shader's properties and keywords too.
                var fresh = new Material(shader);
                EditorUtility.CopySerialized(fresh, mat);
                Object.DestroyImmediate(fresh);
            }
            return mat;
        }

        /// <summary>A URP Lit material, for props that are not toy blocks (metal, flags, belts, lava, glass).</summary>
        static Material Mat(string name, Color color, Texture2D texture = null, float emission = 0f, float alpha = 1f, float smoothness = 0.25f, float metallic = 0f) {
            var mat = MaterialAsset(Root + "/Materials/" + name + ".mat", FindShader("Universal Render Pipeline/Lit"));
            color.a = alpha;
            mat.SetColor("_BaseColor", color);
            mat.SetTexture("_BaseMap", texture);
            mat.SetFloat("_Smoothness", smoothness);
            mat.SetFloat("_Metallic", metallic);
            if (emission > 0f) {
                mat.EnableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", color * emission);
                mat.SetTexture("_EmissionMap", texture);
                mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            } else {
                mat.DisableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", Color.black);
            }
            bool transparent = alpha < 1f;
            mat.SetFloat("_Surface", transparent ? 1f : 0f);
            mat.SetFloat("_Blend", 0f);
            mat.SetFloat("_SrcBlend", transparent ? (float)BlendMode.SrcAlpha : (float)BlendMode.One);
            mat.SetFloat("_DstBlend", transparent ? (float)BlendMode.OneMinusSrcAlpha : (float)BlendMode.Zero);
            mat.SetFloat("_ZWrite", transparent ? 0f : 1f);
            mat.SetOverrideTag("RenderType", transparent ? "Transparent" : "Opaque");
            mat.renderQueue = transparent ? (int)RenderQueue.Transparent : -1;
            if (transparent) mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); else mat.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
            EditorUtility.SetDirty(mat);
            return mat;
        }

        /// <summary>A toy-block material (bevelled edges and a world-scaled grid, see OB_Block.shader).</summary>
        static Material BlockMat(string name, Color color, float grid = 0.1f, float emission = 0f, float smoothness = 0.42f,
            Texture2D detail = null, float detailStrength = 0f, float bevel = 0.35f) {
            var mat = MaterialAsset(Root + "/Materials/" + name + ".mat", FindShader("Universes/Obby/Block"));
            mat.SetColor("_BaseColor", color);
            mat.SetFloat("_GridStrength", grid);
            mat.SetFloat("_EmissionStrength", emission);
            mat.SetFloat("_Smoothness", smoothness);
            mat.SetFloat("_BevelLight", bevel);
            mat.SetTexture("_DetailMap", detail);
            mat.SetFloat("_DetailStrength", detail != null ? detailStrength : 0f);
            mat.SetColor("_SideColor", Color.clear);
            mat.SetFloat("_CapDepth", 0f);
            mat.SetColor("_GlowColor", Color.black);
            mat.SetFloat("_GlowSeams", 0f);
            mat.enableInstancing = true;
            EditorUtility.SetDirty(mat);
            return mat;
        }

        /// <summary>A toy-block material painted in a tier's style: own side colour, top cap, glowing edges and seams.</summary>
        static Material ThemeMat(string name, in OB_BlockStyle style, float bevel = 0.3f) {
            var mat = BlockMat(name, style.top, style.grid, smoothness: style.smoothness, bevel: bevel);
            mat.SetColor("_SideColor", style.side.WithAlpha(1f));
            mat.SetFloat("_CapDepth", style.cap);
            mat.SetColor("_GlowColor", (style.glow * style.glowStrength).WithAlpha(1f));
            mat.SetFloat("_GlowSeams", style.glowSeams);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        /// <summary>A URP particle material: soft alpha blend, or additive for glows and sparkles.</summary>
        static Material FxMat(string name, Texture2D texture, bool additive) {
            var mat = MaterialAsset(Root + "/Materials/" + name + ".mat", FindShader("Universal Render Pipeline/Particles/Unlit"));
            mat.SetTexture("_BaseMap", texture);
            mat.SetColor("_BaseColor", Color.white);
            mat.SetFloat("_Surface", 1f);
            mat.SetFloat("_Blend", additive ? 2f : 0f);
            mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            mat.SetFloat("_DstBlend", additive ? (float)BlendMode.One : (float)BlendMode.OneMinusSrcAlpha);
            mat.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            mat.SetFloat("_DstBlendAlpha", additive ? (float)BlendMode.One : (float)BlendMode.OneMinusSrcAlpha);
            mat.SetFloat("_ZWrite", 0f);
            mat.SetFloat("_Cull", (float)CullMode.Off);
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.renderQueue = (int)RenderQueue.Transparent;
            EditorUtility.SetDirty(mat);
            return mat;
        }

        static Material SimpleMat(string name, string shader, System.Action<Material> setup) {
            var mat = MaterialAsset(Root + "/Materials/" + name + ".mat", FindShader(shader));
            setup(mat);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        static Material CloudMat() => SimpleMat("Cloud", "Universes/Obby/Cloud", m => {
            m.SetColor("_TopColor", OB_Palette.Cloud);
            m.SetColor("_ShadeColor", OB_Palette.CloudShade);
            m.enableInstancing = true;
        });

        // ---------------------------------------------------------------- effects
        sealed class FxPrefabs {
            public ParticleSystem coinBurst, checkpointBurst, finishBurst, respawnPoof, jumpPuff, landPuff, lavaEmbers, gravityDrift;
        }

        static Gradient Fade(Color color, float fadeInEnd = 0.05f, float fadeOutStart = 0.6f) => new() {
            colorKeys = new[] { new GradientColorKey(color, 0f), new GradientColorKey(color, 1f) },
            alphaKeys = new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, fadeInEnd), new GradientAlphaKey(1f, fadeOutStart), new GradientAlphaKey(0f, 1f) }
        };

        static ParticleSystem.MinMaxGradient PaletteColors() {
            var gradient = new Gradient { mode = GradientMode.Fixed };
            var keys = new GradientColorKey[OB_Palette.Blocks.Length];
            for (int i = 0; i < keys.Length; i++) keys[i] = new GradientColorKey(OB_Palette.Blocks[i], (i + 1f) / keys.Length);
            gradient.colorKeys = keys;
            return new ParticleSystem.MinMaxGradient(gradient) { mode = ParticleSystemGradientMode.RandomColor };
        }

        static AnimationCurve Curve(params float[] timeValue) {
            var keys = new Keyframe[timeValue.Length / 2];
            for (int i = 0; i < keys.Length; i++) keys[i] = new Keyframe(timeValue[i * 2], timeValue[i * 2 + 1]);
            var curve = new AnimationCurve(keys);
            for (int i = 0; i < keys.Length; i++) AnimationUtility.SetKeyLeftTangentMode(curve, i, AnimationUtility.TangentMode.Auto);
            return curve;
        }

        /// <summary>A world-space particle system with no emission; callers add bursts or a rate.</summary>
        static ParticleSystem NewParticles(string name, Transform parent, Material material, float duration, bool loop, int maxParticles = 200) {
            var go = new GameObject(name);
            if (parent != null) go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.duration = duration;
            main.loop = loop;
            main.playOnAwake = false;   // one-shots are played by OB_Fx, ambient loops by OB_Course on clients only
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.scalingMode = ParticleSystemScalingMode.Shape;
            main.maxParticles = maxParticles;
            main.startSpeed = 0f;
            var emission = ps.emission;
            emission.rateOverTime = 0f;
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.sortingFudge = 0f;
            return ps;
        }

        static void Burst(ParticleSystem ps, params (float time, int count)[] bursts) {
            var emission = ps.emission;
            emission.SetBursts(bursts.Select(b => new ParticleSystem.Burst(b.time, (short)b.count)).ToArray());
        }

        static void FadeOver(ParticleSystem ps, Gradient gradient) {
            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = gradient;
        }

        static void SizeOver(ParticleSystem ps, AnimationCurve curve) {
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, curve);
        }

        static ParticleSystem SaveFx(ParticleSystem ps) {
            var prefab = PrefabUtility.SaveAsPrefabAsset(ps.gameObject, Fx + "/" + ps.name + ".prefab");
            Object.DestroyImmediate(ps.gameObject);
            return prefab.GetComponent<ParticleSystem>();
        }

        /// <summary>Confetti: palette-coloured paper squares tumbling down.</summary>
        static ParticleSystem Confetti(string name, Material paper, int count, Vector2 speed, float spread, bool secondWave) {
            var ps = NewParticles(name, null, paper, 0.6f, false, count * 2 + 20);
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.8f, 2.8f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed.x, speed.y);
            main.startSize = new ParticleSystem.MinMaxCurve(0.35f, 0.6f);
            main.startRotation3D = true;
            main.startRotationX = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startRotationY = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startRotationZ = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = PaletteColors();
            main.gravityModifier = 0.55f;
            Burst(ps, secondWave ? new[] { (0f, count), (0.35f, count / 2) } : new[] { (0f, count) });
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = spread;
            shape.radius = 1.2f;
            shape.rotation = new Vector3(-90f, 0f, 0f);
            var drag = ps.limitVelocityOverLifetime;
            drag.enabled = true;
            drag.limit = 3.5f;
            drag.dampen = 0.08f;
            var spin = ps.rotationOverLifetime;
            spin.enabled = true;
            spin.separateAxes = true;
            spin.x = new ParticleSystem.MinMaxCurve(-6f, 6f);
            spin.y = new ParticleSystem.MinMaxCurve(-4f, 4f);
            spin.z = new ParticleSystem.MinMaxCurve(-6f, 6f);
            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 1.2f;
            noise.frequency = 0.5f;
            FadeOver(ps, Fade(Color.white, 0.01f, 0.8f));
            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Mesh;
            renderer.mesh = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
            renderer.alignment = ParticleSystemRenderSpace.Local;
            return ps;
        }

        /// <summary>One bright flash at the effect origin, used under bursts.</summary>
        static void Flash(Transform parent, Material glow, Color color, float size, float life) {
            var ps = NewParticles("Flash", parent, glow, 0.2f, false, 2);
            var main = ps.main;
            main.startLifetime = life;
            main.startSize = size;
            main.startColor = color;
            Burst(ps, (0f, 1));
            SizeOver(ps, Curve(0f, 0.5f, 0.3f, 1f, 1f, 1.2f));
            FadeOver(ps, Fade(Color.white, 0.02f, 0.3f));
        }

        static FxPrefabs BuildEffects() {
            var glow = FxMat("FxGlow", Tex("FxGlow"), true);
            // Sparkles and drifting motes are alpha-blended: additive would vanish against the bright sky.
            var sparkle = FxMat("FxSparkle", Tex("FxSparkle"), false);
            var mote = FxMat("FxMote", Tex("FxGlow"), false);
            var puff = FxMat("FxPuff", Tex("FxPuff"), false);
            var paper = FxMat("FxConfetti", null, false);
            var fx = new FxPrefabs();

            {   // Coin: a gold flash and a ring of sparkles.
                var ps = NewParticles("CoinBurst", null, sparkle, 0.5f, false, 40);
                var main = ps.main;
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.45f, 0.8f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(5f, 11f);
                main.startSize = new ParticleSystem.MinMaxCurve(1.4f, 2.4f);
                main.startColor = new ParticleSystem.MinMaxGradient(OB_Palette.Gold, OB_Palette.Hex(0xFF9F1C));
                main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI);
                main.gravityModifier = 0.4f;
                Burst(ps, (0f, 22));
                var shape = ps.shape;
                shape.shapeType = ParticleSystemShapeType.Sphere;
                shape.radius = 0.4f;
                var drag = ps.limitVelocityOverLifetime;
                drag.enabled = true;
                drag.limit = 1.5f;
                drag.dampen = 0.15f;
                SizeOver(ps, Curve(0f, 1f, 1f, 0f));
                FadeOver(ps, Fade(Color.white, 0.02f, 0.5f));
                Flash(ps.transform, glow, OB_Palette.Gold, 5f, 0.3f);
                fx.coinBurst = SaveFx(ps);
            }
            {   // Checkpoint: confetti fountain from the pad plus a green flash.
                var ps = Confetti("CheckpointConfetti", paper, 70, new Vector2(12f, 20f), 22f, false);
                Flash(ps.transform, glow, OB_Palette.Go, 10f, 0.4f);
                fx.checkpointBurst = SaveFx(ps);
            }
            {   // Finish: a bigger confetti blast in two waves with gold sparkles.
                var ps = Confetti("FinishConfetti", paper, 150, new Vector2(18f, 30f), 35f, true);
                var sparks = NewParticles("Sparkles", ps.transform, sparkle, 0.5f, false, 60);
                var main = sparks.main;
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.8f, 1.4f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(10f, 22f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.8f, 1.8f);
                main.startColor = OB_Palette.Gold;
                Burst(sparks, (0f, 40));
                var shape = sparks.shape;
                shape.shapeType = ParticleSystemShapeType.Hemisphere;
                shape.radius = 1f;
                shape.rotation = new Vector3(-90f, 0f, 0f);
                SizeOver(sparks, Curve(0f, 1f, 1f, 0f));
                Flash(ps.transform, glow, OB_Palette.Gold, 16f, 0.5f);
                fx.finishBurst = SaveFx(ps);
            }
            {   // Respawn: a cloud poof with cyan sparkles.
                var ps = NewParticles("RespawnPoof", null, puff, 0.3f, false, 30);
                var main = ps.main;
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 0.85f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(3f, 7f);
                main.startSize = new ParticleSystem.MinMaxCurve(2.2f, 3.6f);
                main.startColor = OB_Palette.Cloud;
                main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
                Burst(ps, (0f, 16));
                var shape = ps.shape;
                shape.shapeType = ParticleSystemShapeType.Sphere;
                shape.radius = 1.2f;
                shape.position = new Vector3(0f, 2.5f, 0f);
                var drag = ps.limitVelocityOverLifetime;
                drag.enabled = true;
                drag.limit = 0.5f;
                drag.dampen = 0.2f;
                SizeOver(ps, Curve(0f, 0.6f, 0.4f, 1f, 1f, 1.1f));
                FadeOver(ps, Fade(Color.white, 0.05f, 0.45f));
                var sparks = NewParticles("Sparkles", ps.transform, sparkle, 0.3f, false, 20);
                var sm = sparks.main;
                sm.startLifetime = new ParticleSystem.MinMaxCurve(0.4f, 0.7f);
                sm.startSpeed = new ParticleSystem.MinMaxCurve(4f, 9f);
                sm.startSize = new ParticleSystem.MinMaxCurve(0.6f, 1.1f);
                sm.startColor = OB_Palette.Gravity;
                Burst(sparks, (0f, 12));
                var ss = sparks.shape;
                ss.shapeType = ParticleSystemShapeType.Sphere;
                ss.radius = 1f;
                ss.position = new Vector3(0f, 2.5f, 0f);
                SizeOver(sparks, Curve(0f, 1f, 1f, 0f));
                fx.respawnPoof = SaveFx(ps);
            }
            fx.jumpPuff = SaveFx(DustRing("JumpPuff", puff, 7, new Vector2(2f, 4f), new Vector2(1.2f, 2f), 0.4f));
            fx.landPuff = SaveFx(DustRing("LandPuff", puff, 12, new Vector2(4f, 8f), new Vector2(1.8f, 3.2f), 0.55f));
            {   // Lava embers: rise from the lava's top face. OB_Course sets the rate and drift per piece.
                var ps = NewParticles("LavaEmbers", null, mote, 2f, true, 80);
                var main = ps.main;
                main.startLifetime = new ParticleSystem.MinMaxCurve(1f, 1.8f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.45f, 0.9f);
                main.startColor = new ParticleSystem.MinMaxGradient(OB_Palette.Gold, OB_Palette.Hex(0xFF9F1C));
                var emission = ps.emission;
                emission.rateOverTime = 6f;
                var shape = ps.shape;
                shape.shapeType = ParticleSystemShapeType.Box;
                shape.position = new Vector3(0f, 0.5f, 0f);
                shape.scale = new Vector3(1f, 0f, 1f);
                var noise = ps.noise;
                noise.enabled = true;
                noise.strength = 0.8f;
                noise.frequency = 0.8f;
                SizeOver(ps, Curve(0f, 1f, 1f, 0.2f));
                FadeOver(ps, Fade(Color.white, 0.15f, 0.5f));
                fx.lavaEmbers = SaveFx(ps);
            }
            {   // Gravity drift: streaks that fall the way the field pulls. OB_Course sets the direction.
                var ps = NewParticles("GravityDrift", null, mote, 2f, true, 120);
                var main = ps.main;
                main.startLifetime = new ParticleSystem.MinMaxCurve(1.2f, 2f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.25f, 0.45f);
                main.startColor = Color.Lerp(OB_Palette.Blocks[4], Color.white, 0.4f);
                var emission = ps.emission;
                emission.rateOverTime = 10f;
                var shape = ps.shape;
                shape.shapeType = ParticleSystemShapeType.Box;
                shape.scale = Vector3.one;
                FadeOver(ps, Fade(Color.white, 0.2f, 0.6f));
                var renderer = ps.GetComponent<ParticleSystemRenderer>();
                renderer.renderMode = ParticleSystemRenderMode.Stretch;
                renderer.velocityScale = 0.35f;
                renderer.lengthScale = 1.5f;
                fx.gravityDrift = SaveFx(ps);
            }
            return fx;
        }

        /// <summary>A flat ring of dust puffs at the feet, oriented by the effect's up axis.</summary>
        static ParticleSystem DustRing(string name, Material puff, int count, Vector2 speed, Vector2 size, float life) {
            var ps = NewParticles(name, null, puff, 0.2f, false, count * 3);
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(life * 0.7f, life);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed.x, speed.y);
            main.startSize = new ParticleSystem.MinMaxCurve(size.x, size.y);
            main.startColor = OB_Palette.Cloud;
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            Burst(ps, (0f, count));
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = 0.9f;
            shape.radiusThickness = 0f;
            shape.rotation = new Vector3(90f, 0f, 0f);
            shape.position = new Vector3(0f, 0.3f, 0f);
            var drag = ps.limitVelocityOverLifetime;
            drag.enabled = true;
            drag.limit = 0.5f;
            drag.dampen = 0.25f;
            var lift = ps.velocityOverLifetime;
            lift.enabled = true;
            lift.space = ParticleSystemSimulationSpace.Local;
            lift.x = 0f;
            lift.y = 1.2f;
            lift.z = 0f;
            SizeOver(ps, Curve(0f, 0.5f, 0.35f, 1f, 1f, 1.15f));
            FadeOver(ps, Fade(Color.white, 0.05f, 0.35f));
            return ps;
        }

        // ---------------------------------------------------------------- prefab helpers
        static GameObject Cube(string name, Transform parent, Vector3 position, Vector3 scale, Material material, bool collider = true) =>
            Primitive(PrimitiveType.Cube, name, parent, position, scale, material, collider);

        static GameObject Primitive(PrimitiveType type, string name, Transform parent, Vector3 position, Vector3 scale, Material material, bool collider) {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            if (parent != null) go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = material;
            if (!collider) Object.DestroyImmediate(go.GetComponent<Collider>());
            return go;
        }

        static GameObject Save(GameObject go, string name) {
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, Pieces + "/" + name + ".prefab");
            Object.DestroyImmediate(go);
            return prefab;
        }

        static BoxCollider Trigger(GameObject owner, Vector3 centre, Vector3 size) {
            var box = owner.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.center = centre;
            box.size = size;
            return box;
        }

        sealed class PiecePrefabs {
            public GameObject platform, checkpointPad, finishPad, lava, spring, boost, gravityZone, conveyor;
            public GameObject crumble, beat, wallJumpWall, mover, spinnerBar, spinnerCross, coin, lobby, gate;
            public Material[] palette;
            public OB_Course.TierMaterials[] tierPlatforms;
            public Material beatSolidA, beatGhostA, beatSolidB, beatGhostB;
        }

        // ---------------------------------------------------------------- pieces
        static PiecePrefabs BuildPieces(FxPrefabs fx) {
            Color pink = OB_Palette.Blocks[6], blue = OB_Palette.Blocks[0];
            var p = new PiecePrefabs {
                palette = OB_Palette.Blocks.Select((c, i) => BlockMat($"Platform{i}", c)).ToArray(),
                tierPlatforms = Enumerable.Range(0, OB_Tiers.Count).Select(t => {
                    var theme = OB_Themes.Get((OB_Tier)t);
                    return new OB_Course.TierMaterials {
                        materials = theme.blocks.Select((style, i) => ThemeMat($"Tier{theme.tier}{i}", style)).ToArray()
                    };
                }).ToArray(),
                beatSolidA = BlockMat("BeatPink", pink, 0.06f, 0.35f),
                beatGhostA = Mat("BeatPinkGhost", pink, emission: 0.2f, alpha: 0.22f),
                beatSolidB = BlockMat("BeatBlue", blue, 0.06f, 0.35f),
                beatGhostB = Mat("BeatBlueGhost", blue, emission: 0.2f, alpha: 0.22f)
            };
            var lavaMat = Mat("Lava", OB_Palette.Lava, Tex("Lava"), 1.6f, smoothness: 0.6f);
            var grey = BlockMat("PadGrey", OB_Palette.Stone, 0.08f);
            var pole = Mat("Pole", OB_Palette.Paper, smoothness: 0.6f, metallic: 0.5f);
            var flagIdle = Mat("FlagIdle", OB_Palette.Blocks[3], emission: 0.2f);
            var flagReached = Mat("FlagReached", OB_Palette.Go, emission: 1.2f);
            var gold = Mat("Gold", OB_Palette.Gold, emission: 0.7f, smoothness: 0.8f, metallic: 0.9f);
            var checker = Mat("FinishChecker", Color.white, Tex("Checker"), smoothness: 0.3f);
            var springRed = BlockMat("SpringRed", OB_Palette.Blocks[3], 0f);
            var springTop = BlockMat("SpringTop", OB_Palette.Gold, 0f, 0.4f);
            var coil = Mat("SpringCoil", OB_Palette.StoneDark, smoothness: 0.8f, metallic: 0.9f);
            var boostMat = Mat("Boost", OB_Palette.Gravity, Tex("Arrows"), 1.2f);
            var belt = Mat("Belt", Color.white, Tex("Arrows"), smoothness: 0.2f);
            var zone = Mat("GravityZone", OB_Palette.Blocks[4], emission: 0.3f, alpha: 0.07f);
            var cracks = BlockMat("Crumble", OB_Palette.Hex(0xF2C98B), 0f, detail: Tex("Cracks"), detailStrength: 1f);
            var stripes = Mat("WallJump", Color.white, Tex("Stripes"), 0.15f);
            var moverMat = BlockMat("Mover", OB_Palette.Paper, 0.06f, 0.12f);
            var lobbyMat = BlockMat("Lobby", OB_Palette.Stone, 0.12f);
            var gateMat = Mat("Gate", OB_Palette.Gravity, emission: 0.8f, alpha: 0.3f);
            var font = Font("Fredoka-Bold");

            p.platform = Save(Cube("Platform", null, Vector3.zero, Vector3.one, p.palette[0]), "Platform");

            {   // Checkpoint pad: authored at its generated size (6 x 1 x 6), origin at the box centre.
                var root = new GameObject("CheckpointPad");
                Cube("Floor", root.transform, Vector3.zero, new Vector3(6f, 1f, 6f), grey);
                Primitive(PrimitiveType.Cylinder, "Pole", root.transform, new Vector3(2.5f, 2f, 2.5f), new Vector3(0.15f, 1.5f, 0.15f), pole, false);
                var flag = Cube("Flag", root.transform, new Vector3(3.15f, 3.05f, 2.5f), new Vector3(1.2f, 0.75f, 0.06f), flagIdle, false);
                var marker = root.AddComponent<CheckpointMarker>();
                marker.flagRenderers = new[] { flag.GetComponent<Renderer>() };
                marker.idleMaterial = flagIdle;
                marker.reachedMaterial = flagReached;
                var confetti = (GameObject)PrefabUtility.InstantiatePrefab(fx.checkpointBurst.gameObject, root.transform);
                confetti.transform.localPosition = new Vector3(0f, 0.5f, 0f);
                marker.reachedEffect = confetti.GetComponent<ParticleSystem>();
                p.checkpointPad = Save(root, "CheckpointPad");
            }
            {
                var root = new GameObject("FinishPad");
                Cube("Floor", root.transform, Vector3.zero, new Vector3(10f, 1f, 10f), checker);
                Cube("PillarL", root.transform, new Vector3(-4.6f, 3f, 0f), new Vector3(0.6f, 5f, 0.6f), gold);
                Cube("PillarR", root.transform, new Vector3(4.6f, 3f, 0f), new Vector3(0.6f, 5f, 0.6f), gold);
                Cube("Banner", root.transform, new Vector3(0f, 5.8f, 0f), new Vector3(9.8f, 0.8f, 0.4f), checker, false);
                var text = new GameObject("Label", typeof(TextMeshPro));
                text.transform.SetParent(root.transform, false);
                text.transform.localPosition = new Vector3(0f, 5.8f, -0.25f);
                var tmp = text.GetComponent<TextMeshPro>();
                tmp.text = "FINISH";
                tmp.font = font.asset;
                tmp.fontSharedMaterial = font.outline;
                tmp.fontSize = 6f;
                tmp.alignment = TextAlignmentOptions.Center;
                tmp.color = OB_Palette.Gold;
                tmp.rectTransform.sizeDelta = new Vector2(9f, 1f);
                p.finishPad = Save(root, "FinishPad");
            }
            {
                var go = Cube("Lava", null, Vector3.zero, Vector3.one, lavaMat);
                go.AddComponent<HazardVolume>().glowRenderer = go.GetComponent<Renderer>();
                PrefabUtility.InstantiatePrefab(fx.lavaEmbers.gameObject, go.transform);
                p.lava = Save(go, "Lava");
            }
            {   // Spring: origin on the surface it sits on.
                var root = new GameObject("Spring");
                Cube("Base", root.transform, new Vector3(0f, 0.15f, 0f), new Vector3(1.7f, 0.3f, 1.7f), springRed);
                var visual = new GameObject("Visual").transform;
                visual.SetParent(root.transform, false);
                visual.localPosition = new Vector3(0f, 0.3f, 0f);
                Primitive(PrimitiveType.Cylinder, "Coil", visual, new Vector3(0f, 0.12f, 0f), new Vector3(0.9f, 0.12f, 0.9f), coil, false);
                Cube("Top", visual, new Vector3(0f, 0.3f, 0f), new Vector3(1.4f, 0.14f, 1.4f), springTop, false);
                var trigger = new GameObject("Trigger");
                trigger.transform.SetParent(root.transform, false);
                Trigger(trigger, new Vector3(0f, 0.75f, 0f), new Vector3(1.5f, 0.9f, 1.5f));
                var pad = trigger.AddComponent<LaunchPad>();
                pad.stretchVisual = visual;
                p.spring = Save(root, "Spring");
            }
            {
                var root = new GameObject("BoostPad");
                Cube("Panel", root.transform, new Vector3(0f, 0.03f, 0f), new Vector3(2.6f, 0.06f, 2.6f), boostMat, false);
                var trigger = new GameObject("Trigger");
                trigger.transform.SetParent(root.transform, false);
                Trigger(trigger, new Vector3(0f, 0.6f, 0f), new Vector3(2.4f, 1.2f, 2.4f));
                var pad = trigger.AddComponent<LaunchPad>();
                pad.localLaunchVelocity = new Vector3(0f, 9f, 24f);
                pad.stretchAmount = 0f;
                p.boost = Save(root, "BoostPad");
            }
            {
                var go = Cube("GravityZone", null, Vector3.zero, Vector3.one, zone);
                go.GetComponent<BoxCollider>().isTrigger = true;
                go.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
                go.AddComponent<GravityZone>();
                PrefabUtility.InstantiatePrefab(fx.gravityDrift.gameObject, go.transform);
                p.gravityZone = Save(go, "GravityZone");
            }
            {
                var go = Cube("Conveyor", null, Vector3.zero, Vector3.one, belt);
                var belt2 = go.AddComponent<ConveyorSurface>();
                belt2.beltRenderer = go.GetComponent<Renderer>();
                p.conveyor = Save(go, "Conveyor");
            }
            {
                var root = new GameObject("CrumbleBlock");
                root.AddComponent<BoxCollider>();
                var visual = Cube("Visual", root.transform, Vector3.zero, Vector3.one, cracks, false);
                root.AddComponent<CrumbleBlock>().visual = visual.transform;
                p.crumble = Save(root, "CrumbleBlock");
            }
            {
                var go = Cube("BeatBlock", null, Vector3.zero, Vector3.one, p.beatSolidA);
                var block = go.AddComponent<TimedToggleBlock>();
                block.visuals = new[] { go.GetComponent<Renderer>() };
                block.solidMaterial = p.beatSolidA;
                block.ghostMaterial = p.beatGhostA;
                p.beat = Save(go, "BeatBlock");
            }
            {
                var go = Cube("WallJumpWall", null, Vector3.zero, Vector3.one, stripes);
                go.AddComponent<WallJumpSurface>();
                p.wallJumpWall = Save(go, "WallJumpWall");
            }
            {
                var go = Cube("Mover", null, Vector3.zero, Vector3.one, moverMat);
                var body = go.AddComponent<Rigidbody>();
                body.isKinematic = true;
                body.useGravity = false;
                go.AddComponent<NetworkPlatform>();
                go.AddComponent<GroundMotionTransfer>().transferRotation = false;
                p.mover = Save(go, "Mover");
            }
            // Sweeper bars are bumpers, not lava: a hit knocks the player flying (KnockbackSurface).
            var sweeper = BlockMat("Sweeper", OB_Palette.Hex(0xFF4F6D), 0.08f, 0.35f, bevel: 0.4f);
            p.spinnerBar = Spinner("SpinnerBar", sweeper, pole, false);
            p.spinnerCross = Spinner("SpinnerCross", sweeper, pole, true);
            p.coin = BuildCoin(gold);
            {
                var go = Cube("Gate", null, Vector3.zero, Vector3.one, gateMat);
                go.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
                p.gate = Save(go, "StartGate");
            }
            {
                var root = new GameObject("Lobby");
                Cube("Floor", root.transform, new Vector3(0f, -0.5f, 0f), new Vector3(LobbySize, 1f, LobbySize), lobbyMat);
                for (int side = -1; side <= 1; side += 2) {
                    Cube($"Rail{side}", root.transform, new Vector3(side * (LobbySize * 0.5f - 0.25f), 0.4f, 0f), new Vector3(0.5f, 0.8f, LobbySize), p.palette[0]);
                }
                Cube("RailBack", root.transform, new Vector3(0f, 0.4f, -LobbySize * 0.5f + 0.25f), new Vector3(LobbySize, 0.8f, 0.5f), p.palette[0]);
                for (int i = 0; i < 4; i++) {
                    float x = (i % 2 == 0 ? -1 : 1) * (LobbySize * 0.5f - 0.6f), z = (i < 2 ? -1 : 1) * (LobbySize * 0.5f - 0.6f);
                    Cube($"Pillar{i}", root.transform, new Vector3(x, 2f, z), new Vector3(0.8f, 4f, 0.8f), p.palette[1 + i]);
                }
                var sign = new GameObject("Sign", typeof(TextMeshPro));
                sign.transform.SetParent(root.transform, false);
                sign.transform.localPosition = new Vector3(0f, 3.2f, LobbySize * 0.5f - 0.6f);
                var tmp = sign.GetComponent<TextMeshPro>();
                tmp.text = "SKY OBBY\n<size=45%>A new course every race. Reach the finish first!</size>";
                tmp.font = font.asset;
                tmp.fontSharedMaterial = font.outline;
                tmp.fontSize = 5f;
                tmp.alignment = TextAlignmentOptions.Center;
                tmp.color = OB_Palette.Paper;
                tmp.rectTransform.sizeDelta = new Vector2(12f, 3f);
                sign.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
                p.lobby = Save(root, "Lobby");
            }
            return p;
        }

        static GameObject Spinner(string name, Material bumper, Material hub, bool cross) {
            var root = new GameObject(name);
            var body = root.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
            root.AddComponent<NetworkPlatform>();
            for (int i = 0; i < (cross ? 2 : 1); i++) {
                var bar = Cube($"Bar{i}", root.transform, Vector3.zero, new Vector3(11.4f, 0.5f, 0.5f), bumper);
                bar.transform.localRotation = Quaternion.Euler(0f, i * 90f, 0f);
                bar.AddComponent<KnockbackSurface>();
            }
            Primitive(PrimitiveType.Cylinder, "Hub", root.transform, new Vector3(0f, 0f, 0f), new Vector3(0.9f, 0.4f, 0.9f), hub, false);
            return Save(root, name);
        }

        static GameObject BuildCoin(Material gold) {
            var root = new GameObject("Coin");
            var spin = new GameObject("Spin").transform;
            spin.SetParent(root.transform, false);
            spin.gameObject.AddComponent<Spinner>().degreesPerSecond = new Vector3(0f, 140f, 0f);
            var model = AssetDatabase.LoadAssetAtPath<GameObject>("Packages/com.ryan.gameframework/Runtime/Items/Coin/Low Poly Coin.fbx");
            if (model != null) {
                var coin = (GameObject)PrefabUtility.InstantiatePrefab(model, spin);
                foreach (var c in coin.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
                foreach (var r in coin.GetComponentsInChildren<Renderer>()) {
                    r.sharedMaterial = gold;
                    r.shadowCastingMode = ShadowCastingMode.Off;
                }
                // Normalise the imported coin to a 1.1 m upright disc, whatever its import scale/axis.
                var bounds = Bounds(coin);
                Vector3 e = bounds.size;
                if (e.y < e.x * 0.5f && e.y < e.z * 0.5f) coin.transform.localRotation = Quaternion.Euler(90f, 0f, 0f) * coin.transform.localRotation;
                bounds = Bounds(coin);
                float scale = 1.1f / Mathf.Max(0.0001f, Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z));
                coin.transform.localScale *= scale;
                bounds = Bounds(coin);
                coin.transform.localPosition -= bounds.center - spin.position;
            } else {
                Primitive(PrimitiveType.Cylinder, "Disc", spin, Vector3.zero, new Vector3(1f, 0.08f, 1f), gold, false).transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            }
            return Save(root, "Coin");
        }

        static Bounds Bounds(GameObject go) {
            var renderers = go.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
            var bounds = renderers[0].bounds;
            foreach (var r in renderers) bounds.Encapsulate(r.bounds);
            return bounds;
        }

        // ---------------------------------------------------------------- fonts
        /// <summary>A TMP font asset for one of the bundled Fredoka faces, plus its styled materials.</summary>
        sealed class FontSet {
            public TMP_FontAsset asset;
            /// <summary>Thick ink outline and drop shadow: for text over the 3D world.</summary>
            public Material outline;
            /// <summary>Soft drop shadow only: for text on HUD panels.</summary>
            public Material shadow;
        }

        const string FontCharacters = " !\"#$%&'()*+,-./0123456789:;<=>?@ABCDEFGHIJKLMNOPQRSTUVWXYZ[\\]^_`abcdefghijklmnopqrstuvwxyz{|}~•…–—×·°";

        /// <summary>
        /// Creates (once) a static SDF font asset with ASCII and common punctuation baked in, so the
        /// asset never changes at runtime; other characters (player names) fall back to TMP's default font.
        /// </summary>
        static FontSet Font(string face) {
            string path = Root + "/Fonts/" + face + " SDF.asset";
            var asset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
            if (asset == null) {
                var source = AssetDatabase.LoadAssetAtPath<UnityEngine.Font>(Root + "/Fonts/" + face + ".ttf")
                    ?? throw new System.InvalidOperationException("Missing font " + face);
                asset = TMP_FontAsset.CreateFontAsset(source, 90, 9, GlyphRenderMode.SDFAA, 1024, 1024, AtlasPopulationMode.Dynamic, false);
                asset.name = face + " SDF";
                asset.TryAddCharacters(FontCharacters);
                asset.atlasPopulationMode = AtlasPopulationMode.Static;
                AssetDatabase.CreateAsset(asset, path);
                asset.atlasTexture.name = face + " Atlas";
                AssetDatabase.AddObjectToAsset(asset.atlasTexture, asset);
                asset.material.name = face + " Material";
                AssetDatabase.AddObjectToAsset(asset.material, asset);
                EditorUtility.SetDirty(asset);
                AssetDatabase.SaveAssets();
            }
            return new FontSet {
                asset = asset,
                outline = FontPreset(asset, face + " Outline", 0.24f, 0.9f),
                shadow = FontPreset(asset, face + " Shadow", 0f, 0.6f)
            };
        }

        static Material FontPreset(TMP_FontAsset font, string name, float outline, float shadow) {
            var mat = MaterialAsset(Root + "/Fonts/" + name + ".mat", FindShader("TextMeshPro/Distance Field"));
            mat.SetTexture("_MainTex", font.atlasTexture);
            mat.SetFloat("_TextureWidth", font.atlasWidth);
            mat.SetFloat("_TextureHeight", font.atlasHeight);
            mat.SetFloat("_GradientScale", font.atlasPadding + 1);
            mat.SetFloat("_WeightNormal", font.normalStyle);
            mat.SetFloat("_WeightBold", font.boldStyle);
            mat.SetColor("_FaceColor", Color.white);
            mat.SetFloat("_OutlineWidth", outline);
            mat.SetColor("_OutlineColor", OB_Palette.Ink);
            mat.SetFloat("_OutlineSoftness", 0f);
            if (outline > 0f) mat.EnableKeyword("OUTLINE_ON"); else mat.DisableKeyword("OUTLINE_ON");
            if (shadow > 0f) {
                mat.EnableKeyword("UNDERLAY_ON");
                mat.SetColor("_UnderlayColor", OB_Palette.Ink.WithAlpha(0.6f));
                mat.SetFloat("_UnderlayOffsetX", 0f);
                mat.SetFloat("_UnderlayOffsetY", -shadow);
                mat.SetFloat("_UnderlayDilate", outline > 0f ? 0.5f : 0.1f);
                mat.SetFloat("_UnderlaySoftness", 0.35f);
            } else {
                mat.DisableKeyword("UNDERLAY_ON");
            }
            EditorUtility.SetDirty(mat);
            return mat;
        }

        // ---------------------------------------------------------------- client presentation
        static RectTransform Box(string name, Transform parent, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size) {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = anchor;
            rect.pivot = pivot;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            return rect;
        }

        static RectTransform Stretch(string name, Transform parent, Vector2 min, Vector2 max, Vector2 offsetMin = default, Vector2 offsetMax = default) {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = min;
            rect.anchorMax = max;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
            return rect;
        }

        /// <summary>A rounded, sliced panel image on <paramref name="rect"/>; <paramref name="radius"/> is the corner radius in canvas pixels.</summary>
        static Image Panel(RectTransform rect, Color color, float radius = 30f) {
            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = SpriteAt(Root + "/Textures/Panel.png");
            image.type = Image.Type.Sliced;
            image.pixelsPerUnitMultiplier = 30f / radius;
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        /// <summary>A soft drop shadow behind <paramref name="panel"/> (added as its first child, drawn first).</summary>
        static void Shadow(RectTransform panel, float radius = 30f, float strength = 0.45f) {
            var rect = Stretch("Shadow", panel, Vector2.zero, Vector2.one, new Vector2(-16f, -24f), new Vector2(16f, 10f));
            rect.SetAsFirstSibling();
            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = SpriteAt(Root + "/Textures/PanelShadow.png");
            image.type = Image.Type.Sliced;
            image.pixelsPerUnitMultiplier = 30f / radius;
            image.color = OB_Palette.Ink.WithAlpha(strength);
            image.raycastTarget = false;
        }

        /// <summary>A panel with a soft shadow: returns the panel body that content goes in.</summary>
        static RectTransform Card(string name, Transform parent, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size, Color color,
            float radius = 30f, float shadow = 0.45f) {
            var root = Box(name, parent, anchor, pivot, position, size);
            Shadow(root, radius, shadow);
            var body = Stretch("Body", root, Vector2.zero, Vector2.one);
            Panel(body, color, radius);
            return root;
        }

        static Image Icon(string name, Transform parent, string icon, Vector2 anchor, Vector2 position, float size, Color color) {
            var rect = Box(name, parent, anchor, new Vector2(0.5f, 0.5f), position, Vector2.one * size);
            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = SpriteAt(Icons + icon + ".png");
            image.color = color;
            image.preserveAspect = true;
            image.raycastTarget = false;
            return image;
        }

        static TMP_Text Label(RectTransform rect, FontSet font, Material material, float size, Color color, TextAlignmentOptions align) {
            var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
            text.font = font.asset;
            text.fontSharedMaterial = material;
            text.fontSize = size;
            text.color = color;
            text.alignment = align;
            text.raycastTarget = false;
            text.textWrappingMode = TextWrappingModes.Normal;
            text.overflowMode = TextOverflowModes.Overflow;
            return text;
        }

        static GameObject BuildClient(FxPrefabs fx) {
#if UNITY_SERVER
            // The client assembly is not compiled in the server profile; keep the prefab authored by a client Editor.
            return AssetDatabase.LoadAssetAtPath<GameObject>(ClientPrefab);
#else
            var bold = Font("Fredoka-Bold");
            var semi = Font("Fredoka-SemiBold");
            var root = new GameObject("ObbyClient");
            var client = root.AddComponent<OB_ClientController>();

            // Post-processing for the whole obby (the client camera already renders post effects).
            var volume = root.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 10f;
            volume.sharedProfile = BuildPostProfile();

            var fxRoot = new GameObject("Fx");
            fxRoot.transform.SetParent(root.transform, false);
            client.fx = fxRoot.AddComponent<OB_Fx>();
            client.fx.coinBurst = fx.coinBurst;
            client.fx.finishBurst = fx.finishBurst;
            client.fx.respawnPoof = fx.respawnPoof;
            client.fx.jumpPuff = fx.jumpPuff;
            client.fx.landPuff = fx.landPuff;

            var skyRoot = new GameObject("Backdrop");
            skyRoot.transform.SetParent(root.transform, false);
            client.backdrop = skyRoot.AddComponent<OB_Backdrop>();
            BuildBackdrop(client.backdrop);
            // Parkour and ragdoll physics for the local player while in the obby.
            client.parkour = root.AddComponent<CharacterParkour>();
            client.atmosphere = skyRoot.AddComponent<OB_Atmosphere>();
            client.atmosphere.moods = Enumerable.Range(0, OB_Tiers.Count)
                .Select(t => OB_Atmosphere.Mood.From(OB_Themes.Get((OB_Tier)t).mood)).ToArray();

            var canvasGo = new GameObject("HUD", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            canvasGo.transform.SetParent(root.transform, false);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 30;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            var hud = canvasGo.transform;
            // Slim and out of the way: translucent panels, small type, and nothing near the middle of the
            // screen except brief toasts and the countdown.
            Color panel = OB_Palette.Ink.WithAlpha(0.58f), muted = OB_Palette.Hex(0xC3CEEE);
            Vector2 topLeft = new(0f, 1f), topRight = Vector2.one, top = new(0.5f, 1f), bottomLeft = Vector2.zero, centre = new(0.5f, 0.5f);
            const float shadow = 0.22f;
            // The shared HUD owns the top corners (chat, player list) and the top strip (round topbar),
            // so the race clock sits under the topbar and the stage card in the bottom-left corner.
            const float clockTop = -132f;

            {   // Stage card, bottom left: stage number, tier chip, stage name and course progress.
                var card = Card("StageCard", hud, bottomLeft, bottomLeft, new Vector2(24f, 24f), new Vector2(380f, 112f), panel, 24f, shadow);
                client.stageCard = card;
                var body = card.Find("Body");
                client.stageLabel = Label(Box("Stage", body, topLeft, topLeft, new Vector2(20f, -12f), new Vector2(220f, 30f)), bold, bold.shadow, 20, OB_Palette.Gold, TextAlignmentOptions.MidlineLeft);
                client.stageLabel.characterSpacing = 3f;
                var chip = Box("TierChip", body, topRight, topRight, new Vector2(-16f, -13f), new Vector2(124f, 28f));
                client.tierChip = Panel(chip, OB_Palette.Paper, 14f);
                client.tierLabel = Label(Stretch("Tier", chip, Vector2.zero, Vector2.one), bold, bold.asset.material, 16, OB_Palette.Ink, TextAlignmentOptions.Center);
                client.tierLabel.characterSpacing = 2f;
                client.tierLabel.textWrappingMode = TextWrappingModes.NoWrap;
                client.stageNameLabel = Label(Box("StageName", body, topLeft, topLeft, new Vector2(20f, -42f), new Vector2(340f, 44f)), bold, bold.shadow, 34, OB_Palette.Paper, TextAlignmentOptions.MidlineLeft);
                client.stageNameLabel.textWrappingMode = TextWrappingModes.NoWrap;
                var track = Box("Progress", body, topLeft, topLeft, new Vector2(20f, -94f), new Vector2(340f, 6f));
                Panel(track, OB_Palette.InkSoft, 3f);
                var fill = Stretch("Fill", track, Vector2.zero, new Vector2(0.3f, 1f));
                Panel(fill, OB_Palette.Gold, 3f);
                client.progressFill = fill;
            }
            {   // Stage hint: plain outlined text above the card that fades out once read.
                client.hintLabel = Label(Box("Hint", hud, bottomLeft, bottomLeft, new Vector2(30f, 146f), new Vector2(560f, 60f)), semi, semi.outline, 20, muted, TextAlignmentOptions.BottomLeft);
                client.hintLabel.lineSpacing = -6f;
            }
            {   // Race clock, top centre, with its status underneath.
                var pill = Card("TimerPill", hud, top, top, new Vector2(-85f, clockTop), new Vector2(210f, 56f), panel, 28f, shadow);
                var body = pill.Find("Body");
                Icon("Clock", body, "icon_schedule", new Vector2(0f, 0.5f), new Vector2(32f, 0f), 30f, OB_Palette.Gold);
                client.timerLabel = Label(Stretch("Timer", body, Vector2.zero, Vector2.one, new Vector2(56f, 0f), new Vector2(-14f, 0f)), bold, bold.shadow, 34, OB_Palette.Paper, TextAlignmentOptions.MidlineLeft);
                client.timerLabel.textWrappingMode = TextWrappingModes.NoWrap;
                client.statusLabel = Label(Box("Status", hud, top, top, new Vector2(-85f, clockTop - 60f), new Vector2(210f, 24f)), semi, semi.outline, 17, OB_Palette.Paper, TextAlignmentOptions.Center);
                client.leaderLabel = Label(Box("Leader", hud, top, top, new Vector2(0f, clockTop - 86f), new Vector2(900f, 30f)), bold, bold.outline, 20, OB_Palette.Paper, TextAlignmentOptions.Center);
            }
            {   // Coins, beside the clock.
                var pill = Card("CoinPill", hud, top, top, new Vector2(105f, clockTop), new Vector2(150f, 56f), panel, 28f, shadow);
                client.coinPill = pill;
                var body = pill.Find("Body");
                Icon("Coin", body, "icon_paid", new Vector2(0f, 0.5f), new Vector2(32f, 0f), 34f, OB_Palette.Gold);
                client.coinsLabel = Label(Stretch("Coins", body, Vector2.zero, Vector2.one, new Vector2(56f, 0f), new Vector2(-12f, 0f)), bold, bold.shadow, 30, OB_Palette.Gold, TextAlignmentOptions.MidlineLeft);
                client.coinsLabel.textWrappingMode = TextWrappingModes.NoWrap;
            }
            {   // Toast: a small coloured pill, high above the player so it never hides the next jump.
                var toast = Box("Toast", hud, new Vector2(0.5f, 0.68f), centre, Vector2.zero, new Vector2(560f, 60f));
                client.toast = toast;
                client.toastGroup = toast.gameObject.AddComponent<CanvasGroup>();
                client.toastGroup.interactable = client.toastGroup.blocksRaycasts = false;
                Shadow(toast, 30f, shadow);
                var body = Stretch("Body", toast, Vector2.zero, Vector2.one);
                client.toastBackground = Panel(body, OB_Palette.Gold, 30f);
                client.toastLabel = Label(Stretch("Label", body, Vector2.zero, Vector2.one, new Vector2(22f, 0f), new Vector2(-22f, 0f)), bold, bold.asset.material, 28, OB_Palette.Ink, TextAlignmentOptions.Center);
                client.toastLabel.textWrappingMode = TextWrappingModes.NoWrap;
            }
            {   // Countdown and GO!
                client.centerLabel = Label(Box("Center", hud, new Vector2(0.5f, 0.55f), centre, Vector2.zero, new Vector2(700f, 220f)), bold, bold.outline, 150, OB_Palette.Paper, TextAlignmentOptions.Center);
                client.centerLabel.textWrappingMode = TextWrappingModes.NoWrap;
            }
            {   // Results board.
                var results = Card("Results", hud, centre, centre, new Vector2(0f, -40f), new Vector2(980f, 600f), OB_Palette.Ink.WithAlpha(0.92f), 36f);
                client.resultsPanel = results.gameObject;
                var body = results.Find("Body");
                var header = Stretch("Header", body, new Vector2(0f, 1f), Vector2.one, new Vector2(0f, -104f), Vector2.zero);
                Panel(header, OB_Palette.Gold, 36f);
                Icon("Medal", header, "icon_military_tech", new Vector2(0f, 0.5f), new Vector2(64f, 0f), 60f, OB_Palette.Ink);
                Label(Stretch("Title", header, Vector2.zero, Vector2.one, new Vector2(110f, 0f), new Vector2(-40f, 0f)), bold, bold.asset.material, 52, OB_Palette.Ink, TextAlignmentOptions.MidlineLeft).text = "RACE RESULTS";
                client.resultsBody = Label(Stretch("Rows", body, Vector2.zero, Vector2.one, new Vector2(56f, 36f), new Vector2(-48f, -130f)), semi, semi.shadow, 34, OB_Palette.Paper, TextAlignmentOptions.TopLeft);
                client.resultsBody.lineSpacing = 24f;
                client.resultsBody.textWrappingMode = TextWrappingModes.NoWrap;
            }

            var audio = new GameObject("Sfx");
            audio.transform.SetParent(root.transform, false);
            client.sfx = audio.AddComponent<AudioSource>();
            client.sfx.playOnAwake = false;
            client.sfx.spatialBlend = 0f;
            var coinClip = AssetDatabase.LoadAssetAtPath<AudioClip>("Packages/com.ryan.gameframework/Runtime/Items/Coin/freesound_gamestudio-drop-coin-384921.mp3");
            client.coinClip = coinClip;
            client.checkpointClip = coinClip;
            client.finishClip = coinClip;
            client.fallClip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/UnityTechnologies/SpaceRobotKyle/Sfx/Player_Land.wav");
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, ClientPrefab);
            Object.DestroyImmediate(root);
            return prefab;
#endif
        }

        // ---------------------------------------------------------------- post-processing
        static T Override<T>(VolumeProfile profile) where T : VolumeComponent {
            if (!profile.TryGet(out T component)) {
                component = profile.Add<T>(true);
                component.name = typeof(T).Name;
                AssetDatabase.AddObjectToAsset(component, profile);
            }
            component.active = true;
            return component;
        }

        /// <summary>The obby's grade: filmic tonemapping, a soft bloom on glowing pickups and hazards, a light vignette.</summary>
        static VolumeProfile BuildPostProfile() {
            string path = Root + "/Materials/ObbyPost.asset";
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
            if (profile == null) {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, path);
            }
            var tonemap = Override<Tonemapping>(profile);
            tonemap.mode.Override(TonemappingMode.ACES);
            var bloom = Override<Bloom>(profile);
            bloom.threshold.Override(1.05f);
            bloom.intensity.Override(0.35f);
            bloom.scatter.Override(0.65f);
            var color = Override<ColorAdjustments>(profile);
            color.postExposure.Override(0f);
            color.contrast.Override(10f);
            color.saturation.Override(18f);
            var vignette = Override<Vignette>(profile);
            vignette.intensity.Override(0.2f);
            vignette.smoothness.Override(0.45f);
            vignette.color.Override(OB_Palette.Ink);
            EditorUtility.SetDirty(profile);
            return profile;
        }

        // ---------------------------------------------------------------- sky backdrop
#if !UNITY_SERVER
        static void BuildBackdrop(OB_Backdrop backdrop) {
            var cloud = CloudMat();
            var sea = SimpleMat("CloudSea", "Universes/Obby/Cloud Sea", m => {
                m.SetColor("_TopColor", OB_Palette.Cloud);
                m.SetColor("_ShadeColor", OB_Palette.CloudShade);
            });

            var random = new System.Random(1337);
            float Range(float a, float b) => Mathf.Lerp(a, b, (float)random.NextDouble());

            var clouds = new List<GameObject>();
            for (int v = 0; v < 4; v++) {
                // A flat-bottomed cumulus: a row of overlapping puffs, biggest in the middle, on a squashed base.
                var root = new GameObject($"Cloud{v}");
                int puffs = 4 + v;
                float width = 14f + v * 5f;
                for (int i = 0; i < puffs; i++) {
                    float t = puffs == 1 ? 0.5f : i / (puffs - 1f);
                    float x = (t - 0.5f) * width;
                    float size = Mathf.Lerp(7f, 13f, 1f - Mathf.Abs(t - 0.5f) * 2f) * Range(0.85f, 1.15f);
                    Puff(root.transform, new Vector3(x, size * 0.25f + Range(0f, 1.5f), Range(-2.5f, 2.5f)), Vector3.one * size, cloud);
                }
                Puff(root.transform, new Vector3(0f, 0f, 0f), new Vector3(width + 8f, 4.5f, 10f), cloud);
                clouds.Add(SaveAt(root, Backdrop + $"/Cloud{v}.prefab"));
            }

            var tierIslands = new OB_Backdrop.TierPrefabs[OB_Tiers.Count];
            for (int t = 0; t < OB_Tiers.Count; t++) {
                var theme = OB_Themes.Get((OB_Tier)t);
                var set = new GameObject[2];
                for (int v = 0; v < set.Length; v++) set[v] = BuildIsland(theme, v, random, cloud);
                tierIslands[t].prefabs = set;
            }

            var seaGo = Primitive(PrimitiveType.Plane, "CloudSea", null, Vector3.zero, new Vector3(260f, 1f, 260f), sea, false);
            var seaRenderer = seaGo.GetComponent<Renderer>();
            seaRenderer.shadowCastingMode = ShadowCastingMode.Off;
            seaRenderer.receiveShadows = false;
            var seaPrefab = SaveAt(seaGo, Backdrop + "/CloudSea.prefab");

            backdrop.cloudPrefabs = clouds.ToArray();
            backdrop.tierIslands = tierIslands;
            backdrop.cloudSea = ((GameObject)PrefabUtility.InstantiatePrefab(seaPrefab, backdrop.transform)).transform;
        }

        /// <summary>
        /// A floating island in a tier's style: a capped slab on stepped rock, dressed with the tier's
        /// props, with a couple of cloud puffs clinging underneath.
        /// </summary>
        static GameObject BuildIsland(OB_Theme theme, int variant, System.Random random, Material cloud) {
            float Range(float a, float b) => Mathf.Lerp(a, b, (float)random.NextDouble());
            string tier = theme.tier.ToString();
            var top = ThemeMat($"Island{tier}Top", theme.islandTop, 0.25f);
            var rock = ThemeMat($"Island{tier}Base", theme.islandBase, 0.15f);
            bool glowing = theme.prop is OB_IslandProp.LavaVents or OB_IslandProp.Crystals;
            var prop = BlockMat($"Island{tier}Prop", theme.propColor, 0f, emission: theme.prop == OB_IslandProp.Crystals ? 1.6f : 0f,
                smoothness: theme.prop is OB_IslandProp.IceSpikes or OB_IslandProp.Crystals ? 0.85f : 0.25f, bevel: 0.25f);
            var accent = BlockMat($"Island{tier}Accent", theme.propAccent, 0f, emission: glowing ? 2.4f : 0f, bevel: 0.3f);

            var root = new GameObject($"Island{tier}{variant}").transform;
            float w = variant == 0 ? 26f : 38f, d = variant == 0 ? 20f : 28f;
            Cube("Top", root, Vector3.zero, new Vector3(w, 2.5f, d), top, false);
            float y = -2.75f;
            for (int step = 0; step < 3; step++) {
                float shrink = 0.82f - step * 0.2f, h = 3.5f - step * 0.5f;
                var block = Cube($"Rock{step}", root, new Vector3(Range(-1f, 1f), y - h * 0.5f + 1.5f, Range(-1f, 1f)), new Vector3(w * shrink, h, d * shrink), rock, false);
                block.transform.localRotation = Quaternion.Euler(0f, Range(-6f, 6f), 0f);
                y -= h;
            }

            Vector3 Spot(float margin = 0.35f) => new(Range(-w * margin, w * margin), 1.25f, Range(-d * margin, d * margin));
            GameObject Piece(string name, Vector3 at, Vector3 size, Material material, float tilt = 0f) {
                var piece = Cube(name, root, at + Vector3.up * size.y * 0.5f, size, material, false);
                piece.transform.localRotation = Quaternion.Euler(Range(-tilt, tilt), Range(0f, 90f), Range(-tilt, tilt));
                return piece;
            }
            int count = variant == 0 ? 3 : 5;
            switch (theme.prop) {
                case OB_IslandProp.Columns:
                    // Ruined white columns with coloured capitals, and a few fallen blocks.
                    for (int i = 0; i < count; i++) {
                        var at = Spot();
                        float h = Range(3f, 9f);
                        Cube($"Column{i}", root, at + Vector3.up * h * 0.5f, new Vector3(1.6f, h, 1.6f), prop, false);
                        if (h > 5f) Cube($"Capital{i}", root, at + Vector3.up * (h + 0.4f), new Vector3(2.4f, 0.8f, 2.4f), accent, false);
                    }
                    for (int i = 0; i < count; i++) Piece($"Block{i}", Spot(0.4f), Vector3.one * Range(1f, 2.2f), prop, 10f);
                    break;
                case OB_IslandProp.Trees:
                    for (int i = 0; i < count - 1; i++) {
                        var at = Spot();
                        float height = Range(4f, 7f);
                        Cube($"Trunk{i}", root, at + Vector3.up * height * 0.5f, new Vector3(1.2f, height, 1.2f), prop, false);
                        float crown = Range(4.5f, 6.5f);
                        var leaves = Cube($"Leaves{i}", root, at + Vector3.up * (height + crown * 0.35f), Vector3.one * crown, accent, false);
                        leaves.transform.localRotation = Quaternion.Euler(0f, Range(0f, 90f), 0f);
                    }
                    break;
                case OB_IslandProp.Cacti:
                    // A raised mesa and block cacti with arms and a flower.
                    Cube("Mesa", root, new Vector3(Range(-w * 0.2f, w * 0.2f), 2.5f, Range(-d * 0.15f, d * 0.15f)), new Vector3(w * 0.4f, 3f, d * 0.4f), top, false);
                    for (int i = 0; i < count - 1; i++) {
                        var at = Spot(0.4f);
                        float h = Range(4f, 7f);
                        Cube($"Cactus{i}", root, at + Vector3.up * h * 0.5f, new Vector3(1.3f, h, 1.3f), prop, false);
                        float side = i % 2 == 0 ? 1f : -1f, armY = h * Range(0.45f, 0.65f);
                        Cube($"ArmOut{i}", root, at + new Vector3(side * 1.2f, armY, 0f), new Vector3(1.4f, 0.9f, 0.9f), prop, false);
                        Cube($"ArmUp{i}", root, at + new Vector3(side * 1.6f, armY + 1.1f, 0f), new Vector3(0.9f, 2.2f, 0.9f), prop, false);
                        Cube($"Flower{i}", root, at + Vector3.up * (h + 0.3f), new Vector3(0.8f, 0.6f, 0.8f), accent, false);
                    }
                    break;
                case OB_IslandProp.IceSpikes:
                    for (int i = 0; i < count + 2; i++) {
                        float h = Range(4f, 12f), thick = Range(1f, 1.8f);
                        Piece($"Spike{i}", Spot(0.4f), new Vector3(thick, h, thick), prop, 14f);
                    }
                    for (int i = 0; i < count; i++) Piece($"Drift{i}", Spot(0.42f) - Vector3.up * 0.4f, new Vector3(Range(2f, 4f), 1.2f, Range(2f, 4f)), accent, 4f);
                    break;
                case OB_IslandProp.LavaVents:
                    // Basalt vents with glowing magma cores, and a lava pool set into the slab.
                    for (int i = 0; i < count - 1; i++) {
                        var at = Spot();
                        float h = Range(2f, 4.5f), size = Range(3f, 4.5f);
                        Cube($"Vent{i}", root, at + Vector3.up * h * 0.5f, new Vector3(size, h, size), prop, false);
                        Cube($"Magma{i}", root, at + Vector3.up * (h + 0.05f), new Vector3(size * 0.55f, 0.3f, size * 0.55f), accent, false);
                    }
                    Cube("Pool", root, new Vector3(Range(-w * 0.2f, w * 0.2f), 1.2f, Range(-d * 0.2f, d * 0.2f)), new Vector3(w * 0.28f, 0.2f, d * 0.22f), accent, false);
                    break;
                case OB_IslandProp.Crystals:
                    // Neon crystal clusters, with shards floating above.
                    for (int i = 0; i < count + 2; i++) {
                        float h = Range(3f, 10f), thick = Range(0.8f, 1.6f);
                        Piece($"Crystal{i}", Spot(0.4f), new Vector3(thick, h, thick), i % 2 == 0 ? prop : accent, 22f);
                    }
                    for (int i = 0; i < count; i++)
                        Piece($"Shard{i}", Spot(0.45f) + Vector3.up * Range(8f, 16f), Vector3.one * Range(0.8f, 1.8f), accent, 45f);
                    break;
            }
            Puff(root, new Vector3(w * 0.4f, -3f, 0f), new Vector3(10f, 6f, 9f), cloud);
            Puff(root, new Vector3(-w * 0.45f, -5f, d * 0.2f), new Vector3(8f, 5f, 8f), cloud);
            return SaveAt(root.gameObject, Backdrop + $"/{root.name}.prefab");
        }

        static void Puff(Transform parent, Vector3 position, Vector3 scale, Material cloud) {
            var puff = Primitive(PrimitiveType.Sphere, "Puff", parent, position, scale, cloud, false);
            var renderer = puff.GetComponent<Renderer>();
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        static GameObject SaveAt(GameObject go, string path) {
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
            Object.DestroyImmediate(go);
            return prefab;
        }
#endif

        // ---------------------------------------------------------------- scenes
        static void BuildStartScene(PiecePrefabs p, GameObject client) {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            {
                // Warm late-morning key light at 42 degrees, cool periwinkle bounce from the clouds below.
                // This is the Basic tier's mood; OB_Atmosphere shifts it as players climb the tiers.
                var mood = OB_Themes.Get(OB_Tier.Basic).mood;
                var sun = new GameObject("Sun", typeof(Light));
                var light = sun.GetComponent<Light>();
                light.type = LightType.Directional;
                light.intensity = mood.sunIntensity;
                light.color = mood.sun;
                light.shadows = LightShadows.Soft;
                light.shadowStrength = 0.75f;
                sun.transform.rotation = Quaternion.Euler(42f, -35f, 0f);
                RenderSettings.sun = light;

                var sky = SimpleMat("Sky", "Universes/Obby/Sky", m => {
                    m.SetColor("_ZenithColor", mood.zenith);
                    m.SetColor("_HorizonColor", mood.horizon);
                    m.SetColor("_BelowColor", mood.below);
                    m.SetColor("_SunColor", mood.sun);
                    m.SetVector("_SunDirection", -sun.transform.forward);
                });
                RenderSettings.skybox = sky;
                RenderSettings.ambientMode = AmbientMode.Trilight;
                RenderSettings.ambientSkyColor = mood.ambientSky;
                RenderSettings.ambientEquatorColor = mood.ambientEquator;
                RenderSettings.ambientGroundColor = mood.ambientGround;
                RenderSettings.fog = true;
                RenderSettings.fogMode = FogMode.Linear;
                RenderSettings.fogColor = mood.fog;
                RenderSettings.fogStartDistance = mood.fogStart;
                RenderSettings.fogEndDistance = mood.fogEnd;
                Lightmapping.bakeOnSceneLoad = Lightmapping.BakeOnSceneLoadMode.Never;

                var courseGo = new GameObject("Course");
                var course = courseGo.AddComponent<OB_Course>();
                var lobby = (GameObject)PrefabUtility.InstantiatePrefab(p.lobby, scene);
                lobby.transform.position = new Vector3(0f, LobbyTop, 0f);
                var spawn = new GameObject("LobbySpawn").transform;
                spawn.SetParent(courseGo.transform, false);
                spawn.position = new Vector3(0f, LobbyTop, -3f);
                var start = new GameObject("CourseStart").transform;
                start.SetParent(courseGo.transform, false);
                start.position = new Vector3(0f, LobbyTop, LobbySize * 0.5f);
                var gate = (GameObject)PrefabUtility.InstantiatePrefab(p.gate, scene);
                gate.transform.SetParent(courseGo.transform, false);
                gate.transform.position = new Vector3(0f, LobbyTop + 2f, LobbySize * 0.5f + 0.2f);
                gate.transform.localScale = new Vector3(LobbySize, 4f, 0.4f);

                course.lobbySpawn = spawn;
                course.courseStart = start;
                course.startGate = gate;
                course.platform = p.platform; course.checkpointPad = p.checkpointPad; course.finishPad = p.finishPad;
                course.lava = p.lava; course.spring = p.spring; course.boost = p.boost; course.gravityZone = p.gravityZone;
                course.conveyor = p.conveyor; course.crumble = p.crumble; course.beat = p.beat; course.wallJumpWall = p.wallJumpWall;
                course.mover = p.mover; course.spinnerBar = p.spinnerBar; course.spinnerCross = p.spinnerCross; course.coin = p.coin;
                course.tierPlatforms = p.tierPlatforms;
                course.beatSolidA = p.beatSolidA; course.beatGhostA = p.beatGhostA; course.beatSolidB = p.beatSolidB; course.beatGhostB = p.beatGhostB;
                course.clientPrefab = client;
                EditorUtility.SetDirty(course);
                EditorSceneManager.SaveScene(scene, StartScene);
            }
        }

        /// <summary>
        /// Clients load universe scenes from the shared scene list; the Linux Server profile overrides it with
        /// its own list, which also carries the server-only scene.
        /// </summary>
        [MenuItem("Ryan/Obby/Register Scenes")]
        public static void RegisterScenes() {
            if (File.Exists(StartScene) && EditorBuildSettings.scenes.All(s => s.path != StartScene))
                EditorBuildSettings.scenes = EditorBuildSettings.scenes.Append(new EditorBuildSettingsScene(StartScene, true)).ToArray();
            var wanted = new[] { StartScene, ServerScene }.Where(File.Exists).ToArray();
            foreach (string guid in AssetDatabase.FindAssets("t:BuildProfile", new[] { "Assets/Settings/Build Profiles" })) {
                var profile = AssetDatabase.LoadAssetAtPath<BuildProfile>(AssetDatabase.GUIDToAssetPath(guid));
                if (profile == null || !new SerializedObject(profile).FindProperty("m_OverrideGlobalSceneList").boolValue) continue;
                var scenes = profile.scenes.ToList();
                int before = scenes.Count;
                foreach (string path in wanted)
                    if (scenes.All(s => s.path != path)) scenes.Add(new EditorBuildSettingsScene(path, true));
                if (scenes.Count == before) continue;
                profile.scenes = scenes.ToArray();
                EditorUtility.SetDirty(profile);
            }
            AssetDatabase.SaveAssets();
        }

        // ---------------------------------------------------------------- previews
        /// <summary>Builds a sample course in a temporary copy of the start scene and renders previews plus the browser thumbnail.</summary>
        [MenuItem("Ryan/Obby/Render Previews")]
        public static void RenderPreviews() {
            int seed = 20261115;
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorSceneManager.OpenScene(StartScene, OpenSceneMode.Single);
            {
                var course = Object.FindAnyObjectByType<OB_Course>();
                var layout = course.Build(seed, 14);
                // Dress the shot like the game: the client prefab brings the sky backdrop and post-processing.
                var client = course.clientPrefab != null ? (GameObject)PrefabUtility.InstantiatePrefab(course.clientPrefab) : null;
#if !UNITY_SERVER
                if (client != null) client.GetComponentInChildren<OB_Backdrop>().Arrange(layout, course.LobbySpawn);
#endif
                string outDir = Path.GetFullPath(Path.Combine(Application.dataPath, "../Logs/obby-previews"));
                Directory.CreateDirectory(outDir);
                var camGo = new GameObject("PreviewCamera", typeof(Camera));
                var cam = camGo.GetComponent<Camera>();
                cam.fieldOfView = 55f;
                cam.farClipPlane = 1000f;
                cam.clearFlags = CameraClearFlags.Skybox;
                cam.GetUniversalAdditionalCameraData().renderPostProcessing = true;
                Vector3 start = course.LobbySpawn, end = layout.finishTop;
                Vector3 mid = (start + end) * 0.5f;
                Shoot(cam, mid + new Vector3(-150f, 90f, -40f), mid, Path.Combine(outDir, "overview.png"), 1600, 900);
                for (int s = 0; s < layout.StageCount; s++) {
                    var st = layout.stages[s];
                    Vector3 next = s + 1 < layout.StageCount ? layout.stages[s + 1].spawn : layout.finishTop;
                    Vector3 focus = (st.spawn + next) * 0.5f;
                    Vector3 dir = (next - st.spawn); dir.y = 0; dir = dir.sqrMagnitude > 0.01f ? dir.normalized : Vector3.forward;
                    Vector3 side = Vector3.Cross(Vector3.up, dir);
                    Shoot(cam, focus - side * 22f - dir * 8f + Vector3.up * 12f, focus, Path.Combine(outDir, $"stage{s:00}-{st.kind}.png"), 960, 540);
                }
                Shoot(cam, start + new Vector3(-9f, 7f, -14f), start + new Vector3(0f, 2f, 18f), Path.Combine(outDir, "thumbnail.png"), 640, 360);
                File.Copy(Path.Combine(outDir, "thumbnail.png"), ThumbnailPath, true);
                Object.DestroyImmediate(camGo);
                if (client != null) Object.DestroyImmediate(client);
                course.Clear();
            }
            AssetDatabase.ImportAsset(ThumbnailPath);
            var importer = (TextureImporter)AssetImporter.GetAtPath(ThumbnailPath);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.SaveAndReimport();
        }

        static void Shoot(Camera cam, Vector3 position, Vector3 target, string path, int width, int height) {
            cam.transform.position = position;
            cam.transform.LookAt(target);
            var rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(width, height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            RenderTexture.active = null;
            cam.targetTexture = null;
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(tex);
        }
    }
}
