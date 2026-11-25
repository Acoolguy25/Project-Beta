using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Universes.UniverseData.obby.Editor {
    /// <summary>
    /// Builds Sky Obby's world: the scene's sun and sky, and (from a client Editor) everything around the
    /// course: per-tier landmarks, floating rocks and islands, course dressing, horizon rings, weather,
    /// birds, clouds and their materials and meshes. Every mesh is generated here in the game's chunky
    /// low-poly style from the art bible (<see cref="OB_Themes"/>, <see cref="OB_WorldStyle"/>); nothing
    /// is hand-edited. Called by <see cref="OB_Authoring"/> ("Ryan/Obby/Build Client Assets"); safe to run
    /// again, as assets are overwritten in place and keep their GUIDs.
    /// <para>
    /// This part holds what the server Editor also compiles (the scene's light and sky); the parts that
    /// touch client components are in the other OB_EnvironmentAuthoring files.
    /// </para>
    /// </summary>
    public static partial class OB_EnvironmentAuthoring {
        const string Root = OB_Authoring.Root;
        const string Env = Root + "/Environment";
        const string MeshFolder = Env + "/Meshes";
        const string MaterialFolder = Env + "/Materials";
        const string PrefabFolder = Env + "/Prefabs";
        const string TextureFolder = Env + "/Textures";
        /// <summary>Where the sky material has always lived (scenes reference it).</summary>
        const string SkyMaterialPath = Root + "/Materials/Sky.mat";

        /// <summary>The sun at the Basic tier's mood; OB_Atmosphere turns it per tier.</summary>
        const float SunShadowStrength = 0.75f;

        /// <summary>
        /// Lights the active scene for the Basic tier: a soft-shadowed sun placed where the tier's sky puts
        /// it, the tier sky, trilight ambient and linear fog. OB_Atmosphere shifts all of it per tier.
        /// </summary>
        public static Light LightScene() {
            var theme = OB_Themes.Get(OB_Tier.Basic);
            OB_Mood mood = theme.mood;
            OB_SkyStyle sky = theme.world.sky;
            Vector3 towardsSun = OB_SkyStyle.Direction(sky.sun);

            var sun = new GameObject("Sun", typeof(Light));
            var light = sun.GetComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = mood.sunIntensity;
            light.color = mood.sun;
            light.shadows = LightShadows.Soft;
            light.shadowStrength = SunShadowStrength;
            sun.transform.rotation = Quaternion.LookRotation(-towardsSun);
            RenderSettings.sun = light;

            RenderSettings.skybox = SimpleMat(SkyMaterialPath, "Universes/Obby/Sky", m => ApplySky(m, mood, sky));
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
            return light;
        }

        /// <summary>Writes a tier's sky into the sky material (the same properties OB_Atmosphere animates).</summary>
        static void ApplySky(Material m, in OB_Mood mood, in OB_SkyStyle sky) {
            m.SetColor("_ZenithColor", mood.zenith);
            m.SetColor("_HorizonColor", mood.horizon);
            m.SetColor("_BelowColor", mood.below);
            m.SetColor("_SunColor", mood.sun);
            m.SetVector("_SunDirection", OB_SkyStyle.Direction(sky.sun));
            m.SetFloat("_SunRadius", sky.sunRadius);
            m.SetFloat("_SunHalo", sky.sunHalo);
            m.SetColor("_MoonColor", sky.moon);
            m.SetVector("_MoonDirection", OB_SkyStyle.Direction(sky.moonAt));
            m.SetFloat("_MoonRadius", sky.moonRadius);
            m.SetColor("_RingColor", sky.ring);
            m.SetFloat("_StarDensity", sky.stars);
            m.SetColor("_AuroraColorA", sky.auroraA);
            m.SetColor("_AuroraColorB", sky.auroraB);
            m.SetFloat("_AuroraStrength", sky.aurora);
            m.SetColor("_NebulaColorA", sky.nebulaA);
            m.SetColor("_NebulaColorB", sky.nebulaB);
            m.SetFloat("_NebulaStrength", sky.nebula);
            m.SetColor("_CirrusColor", sky.cirrus);
        }

        // ---------------------------------------------------------------- asset helpers
        static Shader FindShader(string name) => Shader.Find(name) ?? throw new System.InvalidOperationException("Shader not found: " + name);

        /// <summary>Loads the material at <paramref name="path"/>, or creates it; either way it ends up clean on <paramref name="shader"/>.</summary>
        static Material MaterialAsset(string path, Shader shader) {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null) {
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, path);
            } else if (mat.shader != shader) {
                // Switching shader: start from a fresh material so the old shader's properties and keywords go,
                // keeping the asset's own name (a new material is named after its shader).
                string name = mat.name;
                var fresh = new Material(shader);
                EditorUtility.CopySerialized(fresh, mat);
                Object.DestroyImmediate(fresh);
                mat.name = name;
            }
            return mat;
        }

        /// <summary>
        /// The GravityZone piece's material: glowing edge lines over faint faces that clear as the camera
        /// comes near, tinted per tier by OB_Atmosphere (the palette's gravity cyan until then).
        /// </summary>
        public static Material GravityFieldMaterial(string path) => SimpleMat(path, "Universes/Obby/Gravity Field", m => {
            m.SetColor("_Color", OB_Palette.Gravity);
            m.SetFloat("_FaceAlpha", 0.03f);
            m.SetFloat("_EdgeAlpha", 0.75f);
            m.SetFloat("_EdgeWidth", 0.14f);
            m.SetFloat("_EdgeGlow", 1.6f);
            m.SetFloat("_EdgeMaxPixels", 3f);
            m.SetFloat("_FaceFadeNear", 5f);
            m.SetFloat("_FaceFadeFar", 18f);
            m.SetFloat("_Shimmer", 0.5f);
            m.SetFloat("_ShimmerSpeed", 0.6f);
            m.enableInstancing = true;
        });

        static Material SimpleMat(string path, string shader, System.Action<Material> setup) {
            var mat = MaterialAsset(path, FindShader(shader));
            setup(mat);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        static void EnsureFolder(string path) {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
