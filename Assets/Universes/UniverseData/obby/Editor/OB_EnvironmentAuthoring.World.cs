#if !UNITY_SERVER
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Universes.UniverseData.obby.Client;

namespace Universes.UniverseData.obby.Editor {
    public static partial class OB_EnvironmentAuthoring {
        /// <summary>Clouds keep their old home, so their GUIDs (and the scenes that use them) stay put.</summary>
        const string CloudFolder = Root + "/Prefabs/Backdrop";
        const string CloudMaterialPath = Root + "/Materials/Cloud.mat";
        const string CloudSeaMaterialPath = Root + "/Materials/CloudSea.mat";
        /// <summary>Seed for the authored shapes (not the course): the same meshes on every rebuild.</summary>
        const int ShapeSeed = 1337;

        // Material tuning shared by the tiers.
        // Short fade bands: a prop near the camera is either solid or gone, never a large stippled slab.
        const float CameraFadeNear = 2f, CameraFadeFar = 3.2f;
        const float SceneryFadeNear = 4f, SceneryFadeFar = 6.5f;
        // Landmarks take most of the fog: their own tier's stand a few hundred metres off and stay crisp,
        // while the neighbours' set-pieces kilometres away recede into the haze instead of crowding the view.
        const float SceneryFog = 0.8f, LandmarkFog = 0.8f, BirdFog = 0.8f;
        const float ClothSway = 0.35f, ClothSwaySpeed = 3.2f;
        static readonly Vector4 ClothWave = new(0.9f, 1.1f, 0f, 0f);
        const float WingFlap = 0.45f, WingFlapSpeed = 16f;
        const float BrightRim = 0.15f, NeonRim = 0.25f;

        /// <summary>What the client prefab needs from the world.</summary>
        public sealed class World {
            public OB_Backdrop backdrop;
            public OB_Atmosphere atmosphere;
        }

        /// <summary>The materials one tier's world is drawn with.</summary>
        sealed class TierMaterials {
            public Material scenery, dressing, cloth, landmark, bird, fall;
        }

        /// <summary>Builds every world asset and the client's "Backdrop" object under <paramref name="parent"/>.</summary>
        public static World BuildWorld(Transform parent) {
            foreach (string folder in new[] { MeshFolder, MaterialFolder, PrefabFolder, TextureFolder, CloudFolder }) EnsureFolder(folder);
            var root = new GameObject("Backdrop");
            root.transform.SetParent(parent, false);

            var atmosphere = root.AddComponent<OB_Atmosphere>();
            atmosphere.moods = Enumerable.Range(0, OB_Tiers.Count).Select(t => {
                var theme = OB_Themes.Get((OB_Tier)t);
                return OB_Atmosphere.Mood.From(theme.mood, theme.world.sky, theme.world.field);
            }).ToArray();

            var backdrop = root.AddComponent<OB_Backdrop>();
            backdrop.atmosphere = atmosphere;
            BuildClouds(backdrop);

            var materials = Enumerable.Range(0, OB_Tiers.Count).Select(t => BuildMaterials(OB_Themes.Get((OB_Tier)t))).ToArray();
            backdrop.tierScenery = BuildScenery(materials);
            backdrop.tierDressing = BuildDressing(materials, backdrop.dressingSettings);
            backdrop.scenery = Layer(root.transform, "Scenery", 1800f, ShadowCastingMode.On);
            backdrop.dressing = Layer(root.transform, "Dressing", 1200f, ShadowCastingMode.On);

            var landmarks = new GameObject("Landmarks").AddComponent<OB_Landmarks>();
            landmarks.transform.SetParent(root.transform, false);
            landmarks.tiers = BuildLandmarks(materials);
            backdrop.landmarks = landmarks;

            backdrop.horizon = BuildHorizon(root.transform);
            backdrop.weather = BuildWeather(root.transform);
            backdrop.birds = BuildBirds(root.transform, materials);
            RetireOldAssets();
            return new World { backdrop = backdrop, atmosphere = atmosphere };
        }

        static OB_InstancedLayer Layer(Transform parent, string name, float drawDistance, ShadowCastingMode shadows) {
            var layer = new GameObject(name).AddComponent<OB_InstancedLayer>();
            layer.transform.SetParent(parent, false);
            layer.drawDistance = drawDistance;
            layer.shadows = shadows;
            return layer;
        }

        // ---------------------------------------------------------------- materials
        static TierMaterials BuildMaterials(OB_Theme theme) {
            var world = theme.world;
            string tier = theme.tier.ToString();
            bool glossy = world.dressing.build is OB_BuildStyle.Ice or OB_BuildStyle.Crystal;
            bool dark = theme.tier >= OB_Tier.Insane;
            Color rim = dark ? theme.accent.WithAlpha(NeonRim) : OB_Palette.SkyHorizon.WithAlpha(BrightRim);
            void Common(Material m, float fog, float fadeNear, float fadeFar) {
                m.SetColor("_Tint", Color.white);
                m.SetFloat("_Smoothness", glossy ? 0.55f : 0.15f);
                m.SetFloat("_EmissionBoost", world.dressing.glowStrength);
                m.SetColor("_RimColor", rim);
                m.SetFloat("_FogScale", fog);
                m.SetFloat("_CameraFadeNear", fadeNear);
                m.SetFloat("_CameraFadeFar", fadeFar);
                m.SetVector("_SwayAxis", Vector4.zero);
                m.SetFloat("_SwayAmplitude", 0f);
                m.SetFloat("_SwayPhaseByInstance", 0f);
                m.SetFloat("_Fade", 1f);
                m.SetFloat("_Cull", (float)CullMode.Back);
                m.enableInstancing = true;
                // URP's material validation drops emission unless the material says it is realtime emissive.
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            var set = new TierMaterials {
                scenery = EnvMat($"Env{tier}Scenery", m => Common(m, SceneryFog, SceneryFadeNear, SceneryFadeFar)),
                dressing = EnvMat($"Env{tier}Dressing", m => Common(m, 1f, CameraFadeNear, CameraFadeFar)),
                cloth = EnvMat($"Env{tier}Cloth", m => {
                    Common(m, 1f, CameraFadeNear, CameraFadeFar);
                    m.SetVector("_SwayAxis", new Vector4(0f, 0f, 1f, 0f));
                    m.SetFloat("_SwayAmplitude", ClothSway);
                    m.SetFloat("_SwayFrequency", ClothSwaySpeed);
                    m.SetVector("_SwayWave", ClothWave);
                    m.SetFloat("_Cull", (float)CullMode.Off);
                }),
                landmark = EnvMat($"Env{tier}Landmark", m => Common(m, LandmarkFog, 0f, 0f)),
                bird = EnvMat($"Env{tier}Bird", m => {
                    Common(m, BirdFog, 0f, 0f);
                    m.SetColor("_Tint", world.birds.WithAlpha(1f));
                    m.SetFloat("_EmissionBoost", world.birdGlow);
                    m.SetVector("_SwayAxis", new Vector4(0f, 1f, 0f, 0f));
                    m.SetFloat("_SwayAmplitude", WingFlap);
                    m.SetFloat("_SwayFrequency", WingFlapSpeed);
                    m.SetVector("_SwayWave", Vector4.zero);
                    m.SetFloat("_SwayPhaseByInstance", 1f);
                    m.SetFloat("_Cull", (float)CullMode.Off);
                }),
                fall = FallMat(world.fall, world.fallColor, world.fallHighlight, world.fallGlow)
            };
            return set;
        }

        static Material EnvMat(string name, System.Action<Material> setup) =>
            SimpleMat($"{MaterialFolder}/{name}.mat", "Universes/Obby/Environment", setup);

        /// <summary>Speed of each kind of fall (m/s); ice is frozen.</summary>
        static float FallSpeed(OB_FallKind kind) => kind switch {
            OB_FallKind.Water => 14f,
            OB_FallKind.Sand => 9f,
            OB_FallKind.Lava => 4f,
            OB_FallKind.Void => 7f,
            _ => 0f
        };

        static Material FallMat(OB_FallKind kind, Color color, Color highlight, float glow) {
            if (kind == OB_FallKind.None) return null;
            return SimpleMat($"{MaterialFolder}/Fall{kind}.mat", "Universes/Obby/Fall", m => {
                m.SetColor("_Color", color);
                m.SetColor("_Highlight", highlight);
                m.SetFloat("_Glow", glow);
                m.SetFloat("_Speed", FallSpeed(kind));
                m.SetFloat("_StreakDensity", kind == OB_FallKind.Lava ? 5f : 9f);
                m.SetFloat("_StreakLength", kind == OB_FallKind.Sand ? 10f : 6f);
                m.SetFloat("_Opacity", kind is OB_FallKind.Lava or OB_FallKind.Ice ? 0.95f : 0.8f);
                m.enableInstancing = true;
            });
        }

        // ---------------------------------------------------------------- clouds
        /// <summary>Cloud clusters (flat-bottomed cumulus of overlapping puffs) and the cloud sea under the course.</summary>
        static void BuildClouds(OB_Backdrop backdrop) {
            var cloud = SimpleMat(CloudMaterialPath, "Universes/Obby/Cloud", m => {
                m.SetColor("_TopColor", OB_Palette.Cloud);
                m.SetColor("_ShadeColor", OB_Palette.CloudShade);
                m.enableInstancing = true;
            });
            var sea = SimpleMat(CloudSeaMaterialPath, "Universes/Obby/Cloud Sea", m => {
                m.SetColor("_TopColor", OB_Palette.Cloud);
                m.SetColor("_ShadeColor", OB_Palette.CloudShade);
            });
            var random = new OB_EnvRandom(ShapeSeed, 1u);
            var clouds = new List<GameObject>();
            for (int v = 0; v < 4; v++) {
                var root = new GameObject($"Cloud{v}");
                int puffs = 4 + v;
                float width = 14f + v * 5f;
                for (int i = 0; i < puffs; i++) {
                    float t = puffs == 1 ? 0.5f : i / (puffs - 1f);
                    float size = Mathf.Lerp(7f, 13f, 1f - Mathf.Abs(t - 0.5f) * 2f) * random.Range(0.85f, 1.15f);
                    Puff(root.transform, new Vector3((t - 0.5f) * width, size * 0.25f + random.Range(0f, 1.5f), random.Range(-2.5f, 2.5f)), Vector3.one * size, cloud);
                }
                Puff(root.transform, Vector3.zero, new Vector3(width + 8f, 4.5f, 10f), cloud);
                clouds.Add(SavePrefab(root, $"{CloudFolder}/Cloud{v}.prefab"));
            }
            var seaGo = GameObject.CreatePrimitive(PrimitiveType.Plane);
            seaGo.name = "CloudSea";
            Object.DestroyImmediate(seaGo.GetComponent<Collider>());
            seaGo.transform.localScale = new Vector3(260f, 1f, 260f);
            var seaRenderer = seaGo.GetComponent<Renderer>();
            seaRenderer.sharedMaterial = sea;
            seaRenderer.shadowCastingMode = ShadowCastingMode.Off;
            seaRenderer.receiveShadows = false;
            var seaPrefab = SavePrefab(seaGo, $"{CloudFolder}/CloudSea.prefab");
            backdrop.cloudPrefabs = clouds.ToArray();
            backdrop.cloudSea = ((GameObject)PrefabUtility.InstantiatePrefab(seaPrefab, backdrop.transform)).transform;
        }

        static void Puff(Transform parent, Vector3 position, Vector3 scale, Material material) {
            var puff = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            puff.name = "Puff";
            Object.DestroyImmediate(puff.GetComponent<Collider>());
            puff.transform.SetParent(parent, false);
            puff.transform.localPosition = position;
            puff.transform.localScale = scale;
            var renderer = puff.GetComponent<Renderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        // ---------------------------------------------------------------- saving
        /// <summary>Saves <paramref name="mesh"/> under Environment/Meshes/<paramref name="folder"/>, overwriting in place to keep the GUID.</summary>
        static Mesh SaveMesh(Mesh mesh, string folder) {
            string directory = $"{MeshFolder}/{folder}";
            EnsureFolder(directory);
            string path = $"{directory}/{mesh.name}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing == null) {
                AssetDatabase.CreateAsset(mesh, path);
                return mesh;
            }
            existing.Clear();
            EditorUtility.CopySerialized(mesh, existing);
            Object.DestroyImmediate(mesh);
            EditorUtility.SetDirty(existing);
            return existing;
        }

        static Mesh SaveMesh(OB_EnvMeshBuilder builder, string name, string folder) => SaveMesh(builder.Build(name), folder);

        /// <summary>
        /// Saves <paramref name="go"/> as the prefab at <paramref name="path"/> and destroys it. When the prefab
        /// already exists with the same hierarchy, the new values are copied onto its objects instead, so a
        /// rebuild keeps their file ids and an unchanged prefab stays byte-identical.
        /// </summary>
        static GameObject SavePrefab(GameObject go, string path) {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) != null) {
                var contents = PrefabUtility.LoadPrefabContents(path);
                try {
                    if (SameShape(go.transform, contents.transform)) {
                        CopyInto(go.transform, contents.transform);
                        PrefabUtility.SaveAsPrefabAsset(contents, path);
                        Object.DestroyImmediate(go);
                        return AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    }
                } finally {
                    PrefabUtility.UnloadPrefabContents(contents);
                }
            }
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
            Object.DestroyImmediate(go);
            if (prefab == null) throw new System.InvalidOperationException("Could not save " + path);
            return prefab;
        }

        /// <summary>Same names, components and children, all the way down.</summary>
        static bool SameShape(Transform a, Transform b) {
            if (a.name != b.name || a.childCount != b.childCount) return false;
            var ca = a.GetComponents<Component>();
            var cb = b.GetComponents<Component>();
            if (ca.Length != cb.Length) return false;
            for (int i = 0; i < ca.Length; i++)
                if (ca[i] == null || cb[i] == null || ca[i].GetType() != cb[i].GetType()) return false;
            for (int i = 0; i < a.childCount; i++)
                if (!SameShape(a.GetChild(i), b.GetChild(i))) return false;
            return true;
        }

        static void CopyInto(Transform from, Transform to) {
            to.SetLocalPositionAndRotation(from.localPosition, from.localRotation);
            to.localScale = from.localScale;
            to.gameObject.SetActive(from.gameObject.activeSelf);
            var source = from.GetComponents<Component>();
            var target = to.GetComponents<Component>();
            for (int i = 0; i < source.Length; i++)
                if (source[i] is not Transform) EditorUtility.CopySerialized(source[i], target[i]);
            for (int i = 0; i < from.childCount; i++) CopyInto(from.GetChild(i), to.GetChild(i));
        }

        /// <summary>A child with a mesh renderer, for prefabs.</summary>
        static MeshRenderer MeshChild(Transform parent, string name, Mesh mesh, Material material, ShadowCastingMode shadows) {
            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(parent, false);
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = shadows;
            renderer.receiveShadows = shadows != ShadowCastingMode.Off;
            renderer.lightProbeUsage = LightProbeUsage.BlendProbes;
            return renderer;
        }

        /// <summary>Assets from the earlier backdrop that nothing references any more.</summary>
        static void RetireOldAssets() {
            foreach (OB_Tier tier in System.Enum.GetValues(typeof(OB_Tier))) {
                foreach (string variant in new[] { "0", "1" }) Retire($"{CloudFolder}/Island{tier}{variant}.prefab");
                foreach (string part in new[] { "Top", "Base", "Prop", "Accent" }) Retire($"{Root}/Materials/Island{tier}{part}.mat");
            }
        }

        static void Retire(string path) {
            if (AssetDatabase.LoadMainAssetAtPath(path) != null) AssetDatabase.DeleteAsset(path);
        }
    }
}
#endif
