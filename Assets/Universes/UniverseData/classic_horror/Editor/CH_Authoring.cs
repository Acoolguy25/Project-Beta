using System.Collections.Generic;
using FishNet.Managing.Object;
using FishNet.Object;
using RyanAssets.Characters.Shared;
using RyanAssets.GameFeel;
using RyanAssets.Shared.Declarations;
using RyanAssets.Tools.Client;
using RyanAssets.Tools.Shared;
using RyanAssets.UI.Navigation;
using RyanAssets.UI;
using RyanAssets.UI.Editor;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Universes.UniverseData.classic_horror.Client;

namespace Universes.UniverseData.classic_horror.Editor {
    /// <summary>Rebuildable presentation assets. Scene layout and story edits are kept separate.</summary>
    public static class CH_Authoring {
        public const string Root = "Assets/Universes/UniverseData/classic_horror";
        static readonly Color Amber = new(0.78f, 0.55f, 0.28f);

        public static void EnsureFolders() {
            foreach (string folder in new[] { "Prefabs", "Materials", "Data", "Scenes" })
                if (!AssetDatabase.IsValidFolder(Root + "/" + folder)) AssetDatabase.CreateFolder(Root, folder);
            if (!AssetDatabase.IsValidFolder("Packages/com.ryan.gameframework/Runtime/Tools/Flashlight")) AssetDatabase.CreateFolder("Packages/com.ryan.gameframework/Runtime/Tools", "Flashlight");
        }
        static Material Material(string name, Color color, bool glow = false, string folder = null) {
            string path = (folder ?? Root + "/Materials") + "/" + name + ".mat";
            var result = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (result == null) { result = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(result, path); }
            result.SetColor("_BaseColor", color);
            result.SetFloat("_Smoothness", 0.18f);
            if (glow) { result.EnableKeyword("_EMISSION"); result.SetColor("_EmissionColor", color * 2f); }
            EditorUtility.SetDirty(result);
            return result;
        }
        static GameObject Shape(string name, PrimitiveType type, Transform parent, Vector3 position, Vector3 scale, Material material, Vector3 angles = default) {
            var obj = GameObject.CreatePrimitive(type);
            obj.name = name;
            obj.transform.SetParent(parent, false);
            obj.transform.localPosition = position;
            obj.transform.localEulerAngles = angles;
            obj.transform.localScale = scale;
            Object.DestroyImmediate(obj.GetComponent<Collider>());
            obj.GetComponent<Renderer>().sharedMaterial = material;
            return obj;
        }
        static void Set(Object target, string property, string value) { var so = new SerializedObject(target); so.FindProperty(property).stringValue = value; so.ApplyModifiedPropertiesWithoutUndo(); }

        [MenuItem("Ryan/Classic Horror/Configure Realtime Map Lighting")]
        public static void ConfigureMapLighting() {
            if (Application.isPlaying) throw new System.InvalidOperationException("Stop play mode before editing map lighting.");
            var previous = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            string path = Root + "/Scenes/classic_horror_start.unity";
            var scene = UnityEngine.SceneManagement.SceneManager.GetSceneByPath(path);
            bool opened = !scene.IsValid() || !scene.isLoaded;
            if (opened) scene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(path, UnityEditor.SceneManagement.OpenSceneMode.Additive);
            try {
                UnityEngine.SceneManagement.SceneManager.SetActiveScene(scene);
                EnsureFolders();
                var skyShader = Shader.Find("Classic Horror/Night Sky");
                if (skyShader == null) throw new System.InvalidOperationException("Classic Horror night sky shader is missing.");
                string skyPath = Root + "/Materials/NightSky.mat";
                var sky = AssetDatabase.LoadAssetAtPath<Material>(skyPath);
                if (sky == null) {
                    sky = new Material(skyShader) { name = "NightSky" };
                    AssetDatabase.CreateAsset(sky, skyPath);
                }
                // The running client re-applies the same values (CH_Atmosphere), so edit them there.
                PlaceSearchLanterns(scene, AuthoredLampIntensity());
                BuildNightGrade(scene);
                FixPackParticleBlending(scene);
                CH_NightLighting lighting = AuthoredNightLighting();
                lighting.Apply();
                RenderSettings.skybox = lighting.skybox != null ? lighting.skybox : sky;
                if (RenderSettings.sun != null) EditorUtility.SetDirty(RenderSettings.sun);
                // FishNet creates unsaved holding scenes during loading. Baking here opens a
                // save dialog for those scenes, even with both GI options disabled.
                Lightmapping.bakeOnSceneLoad = Lightmapping.BakeOnSceneLoadMode.Never;
                var settings = AssetDatabase.LoadAssetAtPath<LightingSettings>(Root + "/Data/RealtimeLighting.lighting");
                if (settings == null) {
                    settings = new LightingSettings { name = "Classic Horror Realtime Lighting" };
                    AssetDatabase.CreateAsset(settings, Root + "/Data/RealtimeLighting.lighting");
                }
                settings.bakedGI = false;
                settings.realtimeGI = false;
                Lightmapping.lightingSettings = settings;
                Lightmapping.lightingDataAsset = null;
                LightmapSettings.lightmaps = System.Array.Empty<LightmapData>();
                EditorUtility.SetDirty(settings);
                AssetDatabase.SaveAssets();
                UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);
            } finally {
                if (previous.IsValid() && previous.isLoaded) UnityEngine.SceneManagement.SceneManager.SetActiveScene(previous);
                if (opened) UnityEditor.SceneManagement.EditorSceneManager.CloseScene(scene, true);
            }
        }

        const string KayKitHalloween = "Assets/ThirdParty/KayKit/HalloweenBits/Models/";
        const string SearchLanternPath = Root + "/Prefabs/SearchLantern.prefab";
        const string LanternPostName = "Lantern post";
        // Where the lantern stands next to its search spot, and how much room the post needs.
        const float LanternOffset = 1.6f, LanternClearance = 0.35f, LanternProbeHeight = 6f, LanternRange = 11f;
        static readonly Color LanternGlow = new Color(1f, 0.58f, 0.24f) * 2.4f;

        /// <summary>
        /// A gallows post with a hanging lantern (KayKit Halloween Bits, CC0) whose glass is emissive,
        /// so each search spot's warm light has a visible source that flickers with it (CH_Atmosphere).
        /// </summary>
        static GameObject BuildSearchLantern() {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(KayKitHalloween + "post_lantern.fbx");
            if (model == null) throw new System.InvalidOperationException("KayKit post_lantern.fbx is missing.");
            var root = new GameObject("SearchLantern");
            try {
                var visual = (GameObject)PrefabUtility.InstantiatePrefab(model);
                visual.transform.SetParent(root.transform, false);
                Renderer glass = null;
                foreach (var renderer in visual.GetComponentsInChildren<Renderer>())
                    if (renderer.name.EndsWith("_lantern")) glass = renderer;
                if (glass == null) throw new System.InvalidOperationException("post_lantern.fbx has no lantern mesh.");
                glass.sharedMaterial = LanternGlassMaterial(glass.sharedMaterial);
                glass.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                var post = root.AddComponent<CapsuleCollider>();
                post.radius = 0.12f; post.height = 3.2f; post.center = new Vector3(0f, 1.6f, 0f);
                return PrefabUtility.SaveAsPrefabAsset(root, SearchLanternPath);
            } finally { Object.DestroyImmediate(root); }
        }

        /// <summary>The KayKit atlas with itself as emission map: the amber glass glows, the dark iron barely does.</summary>
        static Material LanternGlassMaterial(Material atlas) {
            string path = Root + "/Materials/SearchLanternGlass.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null) { material = new Material(atlas) { name = "SearchLanternGlass" }; AssetDatabase.CreateAsset(material, path); }
            else material.CopyPropertiesFromMaterial(atlas);
            material.EnableKeyword("_EMISSION");
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            material.SetTexture("_EmissionMap", atlas.GetTexture("_BaseMap"));
            material.SetColor("_EmissionColor", LanternGlow);
            EditorUtility.SetDirty(material);
            return material;
        }

        /// <summary>
        /// Stands a lantern post beside every practical light's search spot, on the ground and clear of
        /// walls, hangs the light inside its glass and records the glass in CH_Map.practicalGlows.
        /// Re-running moves the existing posts instead of adding more.
        /// </summary>
        static void PlaceSearchLanterns(UnityEngine.SceneManagement.Scene scene, float intensity) {
            CH_Map map = null;
            foreach (var root in scene.GetRootGameObjects()) if ((map = root.GetComponentInChildren<CH_Map>(true)) != null) break;
            if (map == null || map.practicalLights == null) throw new System.InvalidOperationException("The horror map has no CH_Map with practical lights.");
            var prefab = BuildSearchLantern();
            Physics.SyncTransforms();
            var glows = new Renderer[map.practicalLights.Length];
            for (int i = 0; i < glows.Length; i++) {
                Light lamp = map.practicalLights[i];
                if (lamp == null) continue;
                Transform spot = lamp.transform.parent;
                Transform post = spot.Find(LanternPostName);
                if (post == null) {
                    post = ((GameObject)PrefabUtility.InstantiatePrefab(prefab, spot)).transform;
                    post.name = LanternPostName;
                }
                post.gameObject.SetActive(false); // keep its own collider out of the clearance checks
                Vector3 ground = LanternGround(spot, post);
                post.gameObject.SetActive(true);
                Vector3 toSpot = spot.position - ground; toSpot.y = 0f;
                post.SetPositionAndRotation(ground, toSpot.sqrMagnitude > 0.01f ? Quaternion.LookRotation(toSpot) : spot.rotation);
                Renderer glass = null;
                foreach (var renderer in post.GetComponentsInChildren<Renderer>())
                    if (renderer.name.EndsWith("_lantern")) glass = renderer;
                glows[i] = glass;
                if (glass != null) lamp.transform.position = glass.bounds.center;
                lamp.range = LanternRange;
                lamp.intensity = intensity;
                lamp.shadows = LightShadows.None;
                // Important, so the huge terrain's eight per-object light slots go to lamps before the pack's dim bounce lights.
                lamp.renderMode = LightRenderMode.ForcePixel;
            }
            map.practicalGlows = glows;
            EditorUtility.SetDirty(map);
        }

        /// <summary>The first spot around the search point, LanternOffset away, with open ground and room for the post.</summary>
        static Vector3 LanternGround(Transform spot, Transform post) {
            Vector3[] directions = { spot.right, -spot.right, -spot.forward, spot.forward, (spot.right - spot.forward).normalized, (-spot.right - spot.forward).normalized };
            Vector3 fallback = spot.position;
            foreach (Vector3 direction in directions) {
                Vector3 flat = new Vector3(direction.x, 0f, direction.z).normalized * LanternOffset;
                Vector3 top = spot.position + flat + Vector3.up * LanternProbeHeight;
                if (!Physics.Raycast(top, Vector3.down, out RaycastHit hit, LanternProbeHeight * 2f, ~0, QueryTriggerInteraction.Ignore)) continue;
                if (Mathf.Abs(hit.point.y - spot.position.y) > 1.5f) continue;
                Vector3 bottom = hit.point + Vector3.up * (LanternClearance + 0.3f), head = hit.point + Vector3.up * 3f;
                if (Physics.CheckCapsule(bottom, head, LanternClearance, ~0, QueryTriggerInteraction.Ignore)) {
                    if (fallback == spot.position) fallback = hit.point;
                    continue;
                }
                return hit.point;
            }
            Debug.LogWarning($"Classic Horror: no clear ground for the lantern at {spot.name}; it may touch a wall.", spot);
            return fallback;
        }

        const string NightGradePath = Root + "/Data/HorrorAtmosphere.asset";
        // Above the shared mood volume (20): the kit's Horror preset keeps its film grain, vignette and
        // bloom, and this grade replaces its ACES tonemapping and -0.45 exposure, which crushed this
        // moonlit map to black in the live Game view.
        const float NightGradePriority = 25f;

        /// <summary>The map's own colour grade, layered over the shared Horror mood the client applies.</summary>
        static void BuildNightGrade(UnityEngine.SceneManagement.Scene scene) {
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(NightGradePath);
            if (profile == null) throw new System.InvalidOperationException("Missing " + NightGradePath);
            Override<Tonemapping>(profile).mode.Override(TonemappingMode.Neutral);
            var color = Override<ColorAdjustments>(profile);
            color.postExposure.Override(1.3f);
            color.contrast.Override(6f);
            color.saturation.Override(-20f);
            color.colorFilter.Override(Color.white);
            EditorUtility.SetDirty(profile);
            foreach (var root in scene.GetRootGameObjects())
                foreach (var volume in root.GetComponentsInChildren<Volume>(true))
                    if (volume.sharedProfile == profile) { volume.priority = NightGradePriority; EditorUtility.SetDirty(volume); }
        }

        const string ParticleMaterialFolder = Root + "/Materials/Particles";

        /// <summary>
        /// Two things made the Flooded Grounds particles (falling leaves, dust, halo rings) draw as
        /// squares at night: the leaf material samples the pack's alpha-less .tif, and URP's
        /// "preserve specular" alpha mode adds the flashlight's specular highlight across the whole
        /// quad, transparent corners included. The scene's particle renderers get copies of those
        /// materials, kept in this game's folder, that use the pack's "_Transparent.png" twin where
        /// there is one and plain alpha blending. The pack's own materials stay untouched; re-running
        /// refreshes the copies.
        /// </summary>
        static void FixPackParticleBlending(UnityEngine.SceneManagement.Scene scene) {
            if (!AssetDatabase.IsValidFolder(ParticleMaterialFolder)) AssetDatabase.CreateFolder(Root + "/Materials", "Particles");
            var copies = new Dictionary<Material, Material>();
            foreach (var root in scene.GetRootGameObjects())
                foreach (var renderer in root.GetComponentsInChildren<ParticleSystemRenderer>(true)) {
                    Material material = renderer.sharedMaterial;
                    if (material == null || !material.HasProperty("_Surface") || material.GetFloat("_Surface") < 1f) continue;
                    string path = AssetDatabase.GetAssetPath(material);
                    bool packMaterial = path.StartsWith(Root + "/Flooded_Grounds/"), ourCopy = path.StartsWith(ParticleMaterialFolder + "/");
                    if (!packMaterial && !ourCopy) continue;
                    if (!copies.TryGetValue(material, out Material copy)) {
                        copy = material;
                        if (packMaterial) {
                            string copyPath = ParticleMaterialFolder + "/" + material.name + ".mat";
                            copy = AssetDatabase.LoadAssetAtPath<Material>(copyPath);
                            if (copy == null) { copy = new Material(material); AssetDatabase.CreateAsset(copy, copyPath); }
                            else copy.CopyPropertiesFromMaterial(material);
                        }
                        Texture alphaTwin = TransparentTwin(copy.GetTexture("_BaseMap"));
                        if (alphaTwin != null) {
                            copy.SetTexture("_BaseMap", alphaTwin);
                            copy.SetTexture("_MainTex", alphaTwin);
                        }
                        UsePlainAlphaBlending(copy);
                        copies.Add(material, copy);
                    }
                    renderer.sharedMaterial = copy;
                    EditorUtility.SetDirty(renderer);
                }
        }

        /// <summary>
        /// Straight alpha blending for a transparent URP material, without "preserve specular" (which
        /// lights the transparent part of a particle quad) and without a glossy highlight.
        /// </summary>
        static void UsePlainAlphaBlending(Material material) {
            if (material.HasProperty("_BlendModePreserveSpecular")) material.SetFloat("_BlendModePreserveSpecular", 0f);
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0f);
            material.SetFloat("_Blend", 0f);
            material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetFloat("_SrcBlendAlpha", (float)UnityEngine.Rendering.BlendMode.One);
            material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_DstBlendAlpha", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            EditorUtility.SetDirty(material);
        }

        /// <summary>The pack's "&lt;name&gt;_Transparent.png" beside an alpha-less texture, or null when there is none (or it is already that).</summary>
        static Texture TransparentTwin(Texture texture) {
            if (texture == null || texture.name.EndsWith("_Transparent")) return null;
            string path = AssetDatabase.GetAssetPath(texture);
            string twin = System.IO.Path.GetDirectoryName(path).Replace('\\', '/') + "/" + texture.name + "_Transparent.png";
            return AssetDatabase.LoadAssetAtPath<Texture>(twin);
        }

        static float AuthoredLampIntensity() {
            var hud = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/Prefabs/InvestigationHUD.prefab");
            var atmosphere = hud != null ? hud.GetComponent<CH_Atmosphere>() : null;
            if (atmosphere == null) throw new System.InvalidOperationException("Rebuild the Investigation HUD before configuring the map lighting.");
            return atmosphere.LampIntensity;
        }

        /// <summary>The night lighting authored on the investigation HUD's CH_Atmosphere (its defaults if the HUD is not built yet).</summary>
        static CH_NightLighting AuthoredNightLighting() {
            var hud = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/Prefabs/InvestigationHUD.prefab");
            var atmosphere = hud != null ? hud.GetComponent<CH_Atmosphere>() : null;
            return atmosphere != null && atmosphere.lighting != null ? atmosphere.lighting : new CH_NightLighting();
        }

        /// <summary>
        /// Overwrites Data/StoryLibrary.asset with the text authored in CH_StoryLibrary's field
        /// defaults. The asset otherwise keeps whatever it was first saved with, so new story
        /// text in code only reaches the game through this.
        /// </summary>
        [MenuItem("Ryan/Classic Horror/Reset Story Library To Authored Text")]
        public static void ResetStoryLibrary() {
            string path = Root + "/Data/StoryLibrary.asset";
            var authored = ScriptableObject.CreateInstance<CH_StoryLibrary>();
            var library = AssetDatabase.LoadAssetAtPath<CH_StoryLibrary>(path);
            if (library == null) {
                AssetDatabase.CreateAsset(authored, path);
            } else {
                EditorUtility.CopySerialized(authored, library);
                library.name = "StoryLibrary";
                EditorUtility.SetDirty(library);
                Object.DestroyImmediate(authored);
            }
            AssetDatabase.SaveAssets();
        }

        [MenuItem("Ryan/Classic Horror/Rebuild Presentation Prefabs")]
        public static void BuildPresentation() {
            EnsureFolders();
            BuildFlashlight();
            RyanAssets.Editor.NPCCharacterAuthoring.Build();
            BuildClueViews();
            BuildHud();
            var library = AssetDatabase.LoadAssetAtPath<CH_StoryLibrary>(Root + "/Data/StoryLibrary.asset");
            if (library == null) AssetDatabase.CreateAsset(ScriptableObject.CreateInstance<CH_StoryLibrary>(), Root + "/Data/StoryLibrary.asset");
            AssetDatabase.SaveAssets();
        }

        /// <summary>
        /// Rebuilds only this game's HUD prefab (and the dust and lens assets it uses), without
        /// touching the shared flashlight or monster prefabs that Rebuild Presentation Prefabs also writes.
        /// </summary>
        [MenuItem("Ryan/Classic Horror/Rebuild Investigation HUD")]
        public static void RebuildHud() {
            EnsureFolders();
            BuildHud();
            AssetDatabase.SaveAssets();
        }

        static void BuildFlashlight() {
            const string folder = "Packages/com.ryan.gameframework/Runtime/Tools/Flashlight";
            var shell = Material("FlashlightShell", new Color(0.045f, 0.052f, 0.055f), false, folder);
            var lens = Material("FlashlightLens", new Color(0.85f, 0.91f, 0.82f), true, folder);
            var root = new GameObject("Flashlight");
            var visual = new GameObject("LightHousing"); visual.transform.SetParent(root.transform, false);
            Shape("Barrel", PrimitiveType.Cylinder, visual.transform, Vector3.zero, new Vector3(0.15f, 0.23f, 0.15f), shell, new Vector3(90, 0, 0));
            Shape("LampHead", PrimitiveType.Cylinder, visual.transform, new Vector3(0, 0, 0.24f), new Vector3(0.24f, 0.075f, 0.24f), shell, new Vector3(90, 0, 0));
            Shape("Lens", PrimitiveType.Cylinder, visual.transform, new Vector3(0, 0, 0.317f), new Vector3(0.205f, 0.008f, 0.205f), lens, new Vector3(90, 0, 0));
            var lightGO = new GameObject("Beam"); lightGO.transform.SetParent(visual.transform, false);
            var beam = lightGO.AddComponent<Light>();
            beam.type = LightType.Spot; beam.range = 48; beam.spotAngle = 54; beam.innerSpotAngle = 30; beam.intensity = 16;
            beam.color = new Color(0.86f, 0.92f, 0.8f); beam.shadows = LightShadows.Soft;
            beam.cullingMask = ~LayerMask.GetMask("LocalCharacter");
            visual.AddComponent<AudioSource>().playOnAwake = false;
            root.AddComponent<NetworkObject>();
            var tool = root.AddComponent<ToolFlashlightShared>();
            tool.toolEnum = ToolEnum.Flashlight; tool.toolName = "Flashlight";
            tool.toolImage = AssetDatabase.LoadAssetAtPath<Sprite>(folder + "/FlashlightIcon.png");
            tool.toolDesc = "Left click to toggle. Unlimited power. Light can reveal you to things in the dark.";
            tool.staminaCostInit = 0; tool.hitDamageInit = 0; tool.currentAmmo = -1; tool.maxClipAmmoInit = -1;
            tool.attackCooldownInit = 0.2f; tool.weaponRoot = visual;
            Set(tool, "clientScript", "RyanAssets.Tools.Client.FlashlightToolClient, RyanAssets.Tools.Client");
            Set(tool, "clientObserver", ""); Set(tool, "serverScript", "");
            var serialized = new SerializedObject(tool); serialized.FindProperty("beam").objectReferenceValue = beam; serialized.ApplyModifiedPropertiesWithoutUndo();
            root.AddComponent<FirstPersonToolView>();
            SetPrefabHash(root, folder + "/Flashlight.prefab");
            var saved = PrefabUtility.SaveAsPrefabAsset(root, folder + "/Flashlight.prefab");
            Register(saved); Object.DestroyImmediate(root);
        }

        static void Register(GameObject prefab) {
            var catalog = AssetDatabase.LoadAssetAtPath<DefaultPrefabObjects>("Assets/DefaultPrefabObjects.asset");
            catalog.AddObject(prefab.GetComponent<NetworkObject>(), true);
            EditorUtility.SetDirty(catalog);
        }
        static void SetPrefabHash(GameObject root, string path) {
            // A cloned NetworkObject carries its source's hash. Assign the new
            // identity before saving, including when replacing an existing prefab.
            var key = new System.Text.StringBuilder();
            foreach (char c in (path + root.name).Trim().ToLowerInvariant())
                if (c >= 'a' && c <= 'z' || c >= '0' && c <= '9') key.Append(c);
            root.GetComponent<NetworkObject>().SetAssetPathHash(GameKit.Dependencies.Utilities.Hashing.GetStableHashU64(key.ToString()));
        }




        const string FloodedProps = Root + "/Flooded_Grounds/Prefabs/Props/";
        const string QuickEffects = "Assets/GabrielAguiarProductions/FreeQuickEffectsVol1/Prefabs/";

        static GameObject PlaceVisual(string path, Transform parent, Vector3 position, Vector3 scale, Vector3 angles = default) {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) throw new System.InvalidOperationException("Missing Classic Horror presentation asset: " + path);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            // Imported Flooded Grounds props carry their demo-scene world transforms.
            instance.transform.localPosition = position;
            instance.transform.localRotation = Quaternion.Euler(angles);
            instance.transform.localScale = scale;
            // These are presentation-only children. Keep their prefab links, but do
            // not let imported demo colliders block the case's interaction rays.
            foreach (var collider in instance.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
            foreach (var body in instance.GetComponentsInChildren<Rigidbody>(true)) {
                body.isKinematic = true;
                body.detectCollisions = false;
            }
            return instance;
        }

        static Material UrpParticleMaterial(Material source) {
            string path = Root + "/Materials/VFX_" + source.name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null) throw new System.InvalidOperationException("URP particle shader is missing.");
            if (material == null) {
                material = new Material(shader) { name = "VFX_" + source.name };
                AssetDatabase.CreateAsset(material, path);
            }
            material.shader = shader;
            material.SetTexture("_BaseMap", source.mainTexture);
            material.SetColor("_BaseColor", Color.white);
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", 2f);
            material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
            material.SetFloat("_ZWrite", 0f);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            EditorUtility.SetDirty(material);
            return material;
        }

        static GameObject PlaceEffect(string path, Transform parent, Vector3 position, float scale, Color tint) {
            var effect = PlaceVisual(path, parent, position, Vector3.one * scale);
            foreach (var particle in effect.GetComponentsInChildren<ParticleSystem>(true)) {
                var main = particle.main;
                main.startColor = tint;
            }
            foreach (var renderer in effect.GetComponentsInChildren<ParticleSystemRenderer>(true)) {
                var materials = renderer.sharedMaterials;
                for (int i = 0; i < materials.Length; i++)
                    if (materials[i] != null && !materials[i].shader.name.StartsWith("Universal Render Pipeline/"))
                        materials[i] = UrpParticleMaterial(materials[i]);
                renderer.sharedMaterials = materials;
            }
            return effect;
        }

        static void ClearVisualChildren(GameObject root) {
            for (int i = root.transform.childCount - 1; i >= 0; i--)
                Object.DestroyImmediate(root.transform.GetChild(i).gameObject);
        }

        [MenuItem("Ryan/Classic Horror/Rebuild Evidence Prefabs")]
        public static void BuildClueViews() {
            EnsureFolders();
            var paper = Material("EvidencePaper", new Color(0.65f, 0.58f, 0.41f));
            var ink = Material("EvidenceInk", new Color(0.12f, 0.16f, 0.15f));
            var rune = Material("EvidenceGlow", new Color(0.78f, 0.54f, 0.25f), true);
            string cluePath = Root + "/Prefabs/EvidenceRecord.prefab";
            var clue = PrefabUtility.LoadPrefabContents(cluePath);
            try {
                ClearVisualChildren(clue);
                PlaceVisual(FloodedProps + "Prop_SmallTable_B.prefab", clue.transform, Vector3.zero, Vector3.one);
                PlaceVisual(FloodedProps + "Prop_Lamp_A.prefab", clue.transform, new Vector3(0.32f, 0.70f, 0.19f), Vector3.one * 0.68f);
                var notes = Shape("Field Notes", PrimitiveType.Quad, clue.transform, new Vector3(-0.16f, 0.735f, -0.04f), new Vector3(0.48f, 0.34f, 1f), paper, new Vector3(90f, 18f, 0f));
                Shape("Case Heading", PrimitiveType.Quad, notes.transform, new Vector3(-0.06f, 0.28f, -0.004f), new Vector3(0.68f, 0.09f, 1f), ink);
                for (int i = 0; i < 3; i++)
                    Shape("Handwritten Line " + i, PrimitiveType.Quad, notes.transform,
                        new Vector3(-0.08f, 0.06f - i * 0.17f, -0.004f), new Vector3(0.55f - i * 0.08f, 0.025f, 1f), ink);
                Shape("Wax Seal", PrimitiveType.Sphere, clue.transform, new Vector3(-0.34f, 0.745f, -0.15f), new Vector3(0.065f, 0.012f, 0.065f), rune);
                PlaceEffect(QuickEffects + "vfx_Portal_01.prefab", clue.transform, new Vector3(-0.16f, 0.76f, 0f), 0.1f, new Color(0.95f, 0.62f, 0.3f, 0.35f));
                var light = clue.GetComponent<Light>();
                light.color = Amber; light.range = 4f; light.intensity = 0.85f;
                var glow = clue.GetComponent<CH_EvidenceGlow>();
                if (glow == null) glow = clue.AddComponent<CH_EvidenceGlow>();
                glow.glowLight = light;
                glow.rimRenderers = AddRim(clue, RimMaterial());
                PrefabUtility.SaveAsPrefabAsset(clue, cluePath);
            } finally { PrefabUtility.UnloadPrefabContents(clue); }

            string sourcePath = Root + "/Prefabs/HauntingSource.prefab";
            var source = PrefabUtility.LoadPrefabContents(sourcePath);
            try {
                ClearVisualChildren(source);
                PlaceVisual(FloodedProps + "Prop_Pulpit_A.prefab", source.transform, Vector3.zero, Vector3.one * 0.82f);
                PlaceVisual(FloodedProps + "Prop_Vase_A.prefab", source.transform, new Vector3(0f, 1.75f, 0.15f), Vector3.one * 0.9f);
                for (int i = 0; i < 3; i++) {
                    float a = i * Mathf.PI * 2f / 3f;
                    PlaceVisual(FloodedProps + "Prop_Lamp_C.prefab", source.transform,
                        new Vector3(Mathf.Sin(a) * 1.3f, 0.58f, Mathf.Cos(a) * 1.3f), Vector3.one * 0.6f);
                }
                PlaceEffect(QuickEffects + "vfx_Portal_01.prefab", source.transform, new Vector3(0f, 1.9f, 0.22f), 0.34f, new Color(0.3f, 0.75f, 0.7f, 0.7f));
                PlaceEffect(QuickEffects + "vfx_Electricity_01.prefab", source.transform, new Vector3(0f, 2.0f, 0.16f), 0.16f, new Color(0.45f, 0.85f, 0.78f, 0.45f));
                var light = source.GetComponent<Light>();
                light.color = new Color(0.33f, 0.73f, 0.64f); light.range = 11f; light.intensity = 0.75f;
                PrefabUtility.SaveAsPrefabAsset(source, sourcePath);
            } finally { PrefabUtility.UnloadPrefabContents(source); }
            AssetDatabase.SaveAssets();
        }

        /// <summary>The additive amber edge glow that makes a record readable as "something to inspect" in the dark.</summary>
        static Material RimMaterial() {
            string path = Root + "/Materials/EvidenceRim.mat";
            var shader = Shader.Find("Classic Horror/Evidence Rim");
            if (shader == null) throw new System.InvalidOperationException("Classic Horror evidence rim shader is missing.");
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null) {
                material = new Material(shader) { name = "EvidenceRim" };
                AssetDatabase.CreateAsset(material, path);
            }
            material.shader = shader;
            EditorUtility.SetDirty(material);
            return material;
        }

        /// <summary>Appends <paramref name="rim"/> as an extra material slot on every mesh of the record, so the rim draws over the whole prop.</summary>
        static Renderer[] AddRim(GameObject root, Material rim) {
            var renderers = new List<Renderer>();
            foreach (var renderer in root.GetComponentsInChildren<MeshRenderer>(true)) {
                var materials = new List<Material>(renderer.sharedMaterials);
                if (!materials.Contains(rim)) materials.Add(rim);
                renderer.sharedMaterials = materials.ToArray();
                renderers.Add(renderer);
            }
            return renderers.ToArray();
        }

        static RectTransform Rect(string name, Transform parent, Vector2 min, Vector2 max, Vector2 offsetMin, Vector2 offsetMax) {
            var go = new GameObject(name, typeof(RectTransform)); go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform; rect.anchorMin = min; rect.anchorMax = max; rect.offsetMin = offsetMin; rect.offsetMax = offsetMax;
            return rect;
        }
        static UnityEngine.UI.Image Panel(string name, Transform parent, Vector2 min, Vector2 max, Color color) {
            var rect = Rect(name, parent, min, max, Vector2.zero, Vector2.zero);
            var image = rect.gameObject.AddComponent<UnityEngine.UI.Image>(); image.color = color; image.raycastTarget = false; return image;
        }

        /// <summary>
        /// The investigation HUD, made from the shared HudKit so it matches the rest of the game, in a
        /// horror key: dark glass, one amber accent, and nothing on screen that the case doesn't need
        /// right now. Case status top centre, compass under it, prompts low, radio transcript at the
        /// bottom, journal and ending as cards. The same prefab carries the client's presenters:
        /// radio, journal, fear loop, ambient scares, encounter scares and the map's atmosphere.
        /// </summary>
        static void BuildHud() {
            RectTransform canvas = HudKit.Canvas("InvestigationHUD", 40);
            var root = canvas.gameObject;
            var client = root.AddComponent<CH_ClientController>();
            var atmosphere = root.AddComponent<CH_Atmosphere>();
            client.fearLoop = root.AddComponent<CH_FearLoop>();
            client.ambientScares = root.AddComponent<CH_AmbientScares>();
            client.radio = root.AddComponent<CH_RadioFeed>();
            client.journal = root.AddComponent<CH_Journal>();
            client.evidenceIcon = HudKit.Icon("magnifier");
            client.offeringIcon = HudKit.Icon("candle");
            client.solvedIcon = HudKit.Icon("laurels_trophy");
            client.failedIcon = HudKit.Icon("skull");

            BuildCaseStatus(canvas, client);
            BuildCompass(canvas, client);

            RectTransform reticle = UIKit.Pinned(canvas, "Reticle", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(6f, 6f));
            UIKit.Rounded(reticle, UIColorRole.TextPrimary, 3f, 0.75f, raycast: false);

            RectTransform interaction = HudKit.KeyPrompt(canvas, "Interaction", "E", "Inspect", Amber, out client.interactionLabel, out UnityEngine.UI.Image key, 46f);
            interaction.anchorMin = interaction.anchorMax = interaction.pivot = new Vector2(0.5f, 0.5f);
            interaction.anchoredPosition = new Vector2(0f, -110f);
            client.interactionKey = key.gameObject;
            client.interactionPrompt = HudKit.Popup(interaction, false);

            BuildRadio(canvas, client.radio);
            BuildJournal(canvas, client.journal);
            BuildEnding(canvas, client);
            BuildAudio(root, client);
            BuildAtmosphere(atmosphere);
            BuildEncounter(root, client);
            UIKit.SavePrefab(root, Root + "/Prefabs/InvestigationHUD.prefab");
        }

        const string AudioFolder = Root + "/Audio/";
        const string FloodedSounds = Root + "/Flooded_Grounds/Content/Sounds/";

        static AudioClip Clip(string path) {
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            if (clip == null) throw new System.InvalidOperationException("Missing Classic Horror audio: " + path);
            return clip;
        }

        /// <summary>A 2D source routed through the SFX mixer group, so the player's SFX volume applies.</summary>
        static AudioSource Source(Transform parent, string name, AudioClip clip = null, bool loop = false) {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var source = go.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f;
            source.clip = clip;
            source.loop = loop;
            source.outputAudioMixerGroup = GameFeelSettings.Active != null ? GameFeelSettings.Active.audio.mixerGroup : null;
            return source;
        }

        static SfxSound Sound(float volume, float pitch, float minDistance, float maxDistance, params string[] clips) => new() {
            clips = System.Array.ConvertAll(clips, Clip), volume = volume, pitch = pitch,
            pitchJitterSemitones = 0.8f, minDistance = minDistance, maxDistance = maxDistance
        };

        /// <summary>Radio static, the pickup sting, the fear loop's body sounds and the ambient scares.</summary>
        static void BuildAudio(GameObject root, CH_ClientController client) {
            client.radio.staticSource = Source(root.transform, "RadioAudio");
            client.radio.staticClip = Clip(AudioFolder + "radio-static.ogg");
            client.objectiveCollectAudio = Source(root.transform, "ObjectiveCollectAudio");
            client.objectiveCollectCue = Clip(AudioFolder + "objective-collect.mp3");

            CH_FearLoop fear = client.fearLoop;
            fear.heartbeatSource = Source(root.transform, "HeartbeatAudio");
            fear.heartbeatClip = Clip(AudioFolder + "heartbeat.ogg");
            fear.breathingSource = Source(root.transform, "BreathingAudio", Clip(AudioFolder + "breathing.ogg"), loop: true);
            fear.droneSource = Source(root.transform, "PresenceAudio", Clip(FloodedSounds + "DeepRattle.mp3"), loop: true);
            fear.droneSource.volume = 0f;
            fear.droneSource.playOnAwake = true;
            // A soft accent under the music's own 0.6 s crossfade, not a cover for a hard cut.
            fear.chaseSting = Sound(0.5f, 0.62f, 0f, 0f, AudioFolder + "wood-thud.ogg");

            CH_AmbientScares ambient = client.ambientScares;
            ambient.doorSlam = Sound(1f, 0.9f, 4f, 70f, AudioFolder + "door-slam-1.ogg", AudioFolder + "door-slam-2.ogg");
            // A distant, pitched-up wind howl reads as a cry carried over the water.
            ambient.distantCry = Sound(0.75f, 1.35f, 25f, 220f, FloodedSounds + "WindHowl.mp3");
            ambient.lightsOut = Sound(0.7f, 0.8f, 0f, 0f, "Packages/com.ryan.gameframework/Runtime/Tools/Flashlight/flashlight_off.mp3",
                AudioFolder + "creak-1.ogg");
            ambient.chapelBell = Sound(0.9f, 0.55f, 30f, 280f, AudioFolder + "chapel-bell-1.ogg", AudioFolder + "chapel-bell-2.ogg");

            CH_Journal journal = client.journal;
            journal.openSound = Clip(AudioFolder + "journal-open.ogg");
            journal.closeSound = Clip(AudioFolder + "journal-close.ogg");
            journal.pageSounds = new[] { Clip(AudioFolder + "journal-page-1.ogg"), Clip(AudioFolder + "journal-page-2.ogg") };
        }

        static void BuildAtmosphere(CH_Atmosphere atmosphere) {
            atmosphere.lighting = new CH_NightLighting { skybox = AssetDatabase.LoadAssetAtPath<Material>(Root + "/Materials/NightSky.mat") };
            if (atmosphere.lighting.skybox == null)
                Debug.LogWarning("Classic Horror: NightSky.mat is missing; run Ryan > Classic Horror > Configure Realtime Map Lighting.");
            atmosphere.dustMotesPrefab = BuildDustMotes();
        }

        /// <summary>The priest's face, the jumpscare sting and the lens burst (chromatic aberration, vignette, distortion).</summary>
        static void BuildEncounter(GameObject root, CH_ClientController client) {
            var scare = root.AddComponent<CH_Jumpscare>();
            client.jumpscare = scare;
            var scarePanel = Panel("Encounter", root.transform, Vector2.zero, Vector2.one, new Color(0.002f, 0, 0, 0.98f));
            scare.overlay = scarePanel.gameObject.AddComponent<CanvasGroup>();
            scare.overlay.alpha = 0; scare.overlay.blocksRaycasts = false; scare.overlay.interactable = false;
            var face = Rect("The face", scarePanel.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-650, -650), new Vector2(650, 650));
            scare.face = face;
            var portrait = face.gameObject.AddComponent<UnityEngine.UI.RawImage>();
            portrait.texture = AssetDatabase.LoadAssetAtPath<Texture2D>(RyanAssets.Editor.NPCCharacterAuthoring.Root + "/Data/PresencePortrait.png"); portrait.raycastTarget = false;
            scare.sting = scarePanel.gameObject.AddComponent<AudioSource>();
            scare.sting.clip = Clip("Packages/com.ryan.ui/Runtime/Client/ClientAudio/JumpscareAudio/jumpscare.mp3");
            scare.sting.playOnAwake = false; scare.sting.volume = 0.75f; scare.sting.spatialBlend = 0;
            scare.sting.outputAudioMixerGroup = GameFeelSettings.Active != null ? GameFeelSettings.Active.audio.mixerGroup : null;

            var lens = new GameObject("EncounterLens");
            lens.transform.SetParent(root.transform, false);
            scare.lensVolume = lens.AddComponent<Volume>();
            scare.lensVolume.isGlobal = true;
            // Above the shared mood (20) so the burst wins while it lasts.
            scare.lensVolume.priority = 50f;
            scare.lensVolume.weight = 0f;
            scare.lensVolume.sharedProfile = BuildEncounterProfile();
        }

        static VolumeProfile BuildEncounterProfile() {
            string path = Root + "/Data/EncounterLens.asset";
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
            if (profile == null) {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, path);
            }
            var aberration = Override<ChromaticAberration>(profile);
            aberration.intensity.Override(1f);
            var vignette = Override<Vignette>(profile);
            vignette.color.Override(new Color(0.22f, 0f, 0.01f));
            vignette.intensity.Override(0.6f);
            vignette.smoothness.Override(0.45f);
            var distortion = Override<LensDistortion>(profile);
            distortion.intensity.Override(-0.3f);
            EditorUtility.SetDirty(profile);
            return profile;
        }

        static T Override<T>(VolumeProfile profile) where T : VolumeComponent {
            if (!profile.TryGet(out T component)) {
                component = profile.Add<T>(true);
                component.hideFlags = HideFlags.HideInInspector | HideFlags.HideInHierarchy;
                AssetDatabase.AddObjectToAsset(component, profile);
            }
            component.active = true;
            return component;
        }

        /// <summary>Lit dust that hangs around the camera: invisible in the dark, drifting specks inside the flashlight beam.</summary>
        static ParticleSystem BuildDustMotes() {
            string path = Root + "/Prefabs/FlashlightDust.prefab";
            var root = new GameObject("FlashlightDust");
            try {
                var particles = root.AddComponent<ParticleSystem>();
                var main = particles.main;
                main.loop = true;
                main.prewarm = true;
                main.playOnAwake = true;
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                main.startLifetime = new ParticleSystem.MinMaxCurve(6f, 10f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(0.01f, 0.06f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.012f, 0.03f);
                main.startColor = new Color(0.92f, 0.9f, 0.82f, 0.6f);
                main.gravityModifier = -0.003f;
                main.maxParticles = 450;
                var emission = particles.emission;
                emission.rateOverTime = 55f;
                var shape = particles.shape;
                shape.shapeType = ParticleSystemShapeType.Box;
                shape.scale = new Vector3(7f, 4f, 9f);
                var noise = particles.noise;
                noise.enabled = true;
                noise.strength = 0.06f;
                noise.frequency = 0.2f;
                noise.scrollSpeed = 0.05f;
                var fade = particles.colorOverLifetime;
                fade.enabled = true;
                var gradient = new Gradient();
                gradient.SetKeys(
                    new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                    new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.2f), new GradientAlphaKey(1f, 0.8f), new GradientAlphaKey(0f, 1f) });
                fade.color = gradient;
                var renderer = root.GetComponent<ParticleSystemRenderer>();
                renderer.renderMode = ParticleSystemRenderMode.Billboard;
                renderer.sharedMaterial = DustMaterial();
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                return PrefabUtility.SaveAsPrefabAsset(root, path).GetComponent<ParticleSystem>();
            } finally { Object.DestroyImmediate(root); }
        }

        static Material DustMaterial() {
            string path = Root + "/Materials/FlashlightDust.mat";
            var shader = Shader.Find("Universal Render Pipeline/Particles/Lit");
            if (shader == null) throw new System.InvalidOperationException("URP lit particle shader is missing.");
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null) {
                material = new Material(shader) { name = "FlashlightDust" };
                AssetDatabase.CreateAsset(material, path);
            }
            material.shader = shader;
            material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>("Packages/com.ryan.gameframework/Runtime/GameFeel/Generated/Textures/fx_soft_dot.png"));
            material.SetColor("_BaseColor", Color.white);
            // Alpha blended and lit: only light that reaches a speck makes it visible.
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_ZWrite", 0f);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            UsePlainAlphaBlending(material);
            return material;
        }

        /// <summary>Top centre: chapter, the current objective, case progress and the clock.</summary>
        static void BuildCaseStatus(RectTransform canvas, CH_ClientController client) {
            RectTransform card = UIKit.Pinned(canvas, "CaseStatus", new Vector2(0.5f, 1f), new Vector2(0f, -64f), new Vector2(560f, 100f));
            HudKit.Glass(card, 16f);
            UIKit.Row(card.gameObject, 14f, new RectOffset(14, 20, 12, 12), TextAnchor.MiddleLeft);
            UIKit.FitContent(card.gameObject, false, true);
            HudKit.Emblem(card, "Emblem", "magnifier", 58f, Amber, out _, out client.caseIcon);

            RectTransform text = UIKit.Node(card, "Text");
            UIKit.Size(text, -1f, -1f, 1f);
            UIKit.Column(text.gameObject, 3f, null, TextAnchor.MiddleLeft);
            client.chapterLabel = UIKit.Text(text, "Chapter", "Investigator", UIText.Caption, UIColorRole.TextMuted);
            client.chapterLabel.color = Amber;
            Object.DestroyImmediate(client.chapterLabel.GetComponent<UIThemeColor>());
            client.objectiveLabel = UIKit.Text(text, "Objective", "Establishing the radio link...", UIText.Strong);
            client.objectiveLabel.fontSize = 18f;
            client.objectiveLabel.textWrappingMode = TextWrappingModes.Normal;

            RectTransform chips = UIKit.Node(text, "Chips");
            UIKit.Row(chips.gameObject, 8f, new RectOffset(0, 0, 4, 0), TextAnchor.MiddleLeft);
            client.progressChip = HudKit.Chip(chips, "Progress", "magnifier", "Evidence 0 / 4", Amber, 28f, UIText.Small);
            client.timerChip = HudKit.Chip(chips, "Time", "hourglass", "00:00", Amber, 28f, UIText.Small);
            RectTransform journal = UIKit.Node(chips, "JournalHint");
            UIKit.Row(journal.gameObject, 6f, new RectOffset(6, 0, 0, 0), TextAnchor.MiddleLeft);
            HudKit.KeyCap(journal, "F");
            UIKit.Text(journal, "Label", "Journal", UIText.Small, UIColorRole.TextSecondary);
            client.journal.hint = journal;
            RyanAssets.UI.GameHud.HudChip badge = HudKit.Chip(chips, "NewEntry", "notebook", "New entry", Amber, 28f, UIText.Small);
            client.journal.newEntryBadge = HudKit.Popup(badge, false);
        }

        /// <summary>Under the case status: an arrow towards the nearest lead and how far it is.</summary>
        static void BuildCompass(RectTransform canvas, CH_ClientController client) {
            RectTransform pill = UIKit.Pinned(canvas, "ObjectiveCompass", new Vector2(0.5f, 1f), new Vector2(0f, -184f), new Vector2(260f, 38f));
            HudKit.Glass(pill, 19f);
            UIKit.Row(pill.gameObject, 8f, new RectOffset(8, 16, 0, 0), TextAnchor.MiddleLeft);
            UIKit.FitContent(pill.gameObject, true, false);
            RectTransform pointer = UIKit.Node(pill, "DirectionPointer");
            UIKit.Size(pointer, 26f, 26f);
            UnityEngine.UI.Image arrow = HudKit.Icon(pointer, "navigation", 22f, Amber);
            UIKit.Center(arrow.rectTransform);
            var readout = UIKit.Text(pill, "Readout", "SEARCH   FORWARD   12 m", UIText.Caption, UIColorRole.TextPrimary);
            client.objectiveCompass = canvas.gameObject.AddComponent<ObjectiveCompass>();
            client.objectiveCompass.Configure(pill.gameObject, pointer, readout, "SEARCH");
        }

        /// <summary>Bottom centre: the radio transcript, as a card that fades in for each new message.</summary>
        static void BuildRadio(RectTransform canvas, CH_RadioFeed radio) {
            RectTransform card = HudKit.Card(canvas, "RadioTranscript", new Vector2(0.5f, 0f), new Vector2(0f, 120f), new Vector2(820f, 120f));
            HudKit.AccentStripe(card, Amber, 4f, left: true);
            UIKit.Row(card.gameObject, 16f, new RectOffset(22, 26, 18, 18), TextAnchor.UpperLeft);
            UIKit.FitContent(card.gameObject, false, true);
            HudKit.Emblem(card, "Emblem", "pocket_radio", 52f, Amber, out _, out _);
            RectTransform text = UIKit.Node(card, "Text");
            UIKit.Size(text, -1f, -1f, 1f);
            UIKit.Column(text.gameObject, 4f, null, TextAnchor.UpperLeft);
            radio.caption = UIKit.Text(text, "Caption", "Radio", UIText.Caption, UIColorRole.TextMuted);
            radio.caption.color = Amber;
            Object.DestroyImmediate(radio.caption.GetComponent<UIThemeColor>());
            radio.transcript = UIKit.Text(text, "Transcript", string.Empty, UIText.Body);
            radio.transcript.fontSize = 19f;
            radio.transcript.textWrappingMode = TextWrappingModes.Normal;
            radio.transcript.richText = true;
            radio.popup = card.GetComponent<RyanAssets.UI.GameHud.HudPopup>();
        }

        /// <summary>The field journal: a readable card with page buttons; the world keeps running behind it.</summary>
        static void BuildJournal(RectTransform canvas, CH_Journal journal) {
            RectTransform card = HudKit.Card(canvas, "FieldJournal", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1040f, 700f));
            journal.panel = card.gameObject;
            card.GetComponentInChildren<UnityEngine.UI.Image>().raycastTarget = true;
            HudKit.AccentStripe(card, Amber, 5f);
            UIKit.Column(card.gameObject, 14f, new RectOffset(40, 40, 30, 30), TextAnchor.UpperLeft);

            RectTransform header = UIKit.Node(card, "Header");
            UIKit.Row(header.gameObject, 14f, null, TextAnchor.MiddleLeft);
            HudKit.Emblem(header, "Emblem", "notebook", 56f, Amber, out _, out _);
            RectTransform titles = UIKit.Node(header, "Titles");
            UIKit.Size(titles, -1f, -1f, 1f);
            UIKit.Column(titles.gameObject, 0f, null, TextAnchor.MiddleLeft);
            UIKit.Text(titles, "Title", "Field notes", UIText.Title);
            journal.pageLabel = UIKit.Text(titles, "Page", "Page 1 / 1", UIText.Small, UIColorRole.TextMuted);
            journal.close = UIKit.IconButton(header, "Close", "close", 40f, UIColorRole.SurfaceInset, 20f, out _, out _);
            UIKit.Divider(card);

            journal.body = UIKit.Text(card, "Entry", string.Empty, UIText.Body, UIColorRole.TextPrimary, TextAlignmentOptions.TopLeft);
            journal.body.fontSize = 23f;
            journal.body.lineSpacing = 12f;
            journal.body.textWrappingMode = TextWrappingModes.Normal;
            journal.body.overflowMode = TextOverflowModes.Overflow;
            journal.body.richText = true;
            UIKit.Size(journal.body, -1f, 440f);

            RectTransform footer = UIKit.Node(card, "Footer");
            UIKit.Row(footer.gameObject, 10f, null, TextAnchor.MiddleLeft);
            UnityEngine.UI.Image warn = HudKit.Icon(footer, "warning", 20f, Amber);
            var note = UIKit.Text(footer, "Warning", "The settlement does not pause while you read.", UIText.Small, UIColorRole.TextSecondary);
            UIKit.Size(note, -1f, -1f, 1f);
            journal.previous = UIKit.TextButton(footer, "Previous", "Previous", "arrow_back", UIColorRole.SurfaceInset, UIColorRole.TextPrimary, 42f);
            UIKit.Size(journal.previous, 150f, 42f);
            journal.next = UIKit.TextButton(footer, "Next", "Next", "arrow_forward", UIColorRole.Accent, UIColorRole.OnAccent, 42f);
            UIKit.Size(journal.next, 150f, 42f);
            warn.raycastTarget = false;
            card.gameObject.SetActive(false);
            // The journal opens and closes by script, not through HudPopup.
            Object.DestroyImmediate(card.GetComponent<RyanAssets.UI.GameHud.HudPopup>());
            card.GetComponent<CanvasGroup>().alpha = 1f;
        }

        /// <summary>The case ending: how it ended, the epilogue, and when the next case starts.</summary>
        static void BuildEnding(RectTransform canvas, CH_ClientController client) {
            RectTransform card = HudKit.Card(canvas, "CaseEnding", new Vector2(0.5f, 0.5f), new Vector2(0f, 20f), new Vector2(860f, 420f));
            client.endingStripe = HudKit.AccentStripe(card, Amber, 5f);
            UIKit.Column(card.gameObject, 10f, new RectOffset(48, 48, 32, 30), TextAnchor.UpperCenter);
            UIKit.FitContent(card.gameObject, false, true);
            HudKit.Emblem(card, "Emblem", "laurels_trophy", 92f, Amber, out client.endingDisc, out client.endingIcon);
            UIKit.Text(card, "Kicker", "Case closed", UIText.Caption, UIColorRole.TextMuted, TextAlignmentOptions.Center);
            client.endingTitle = UIKit.Text(card, "Title", "Last rites", UIText.Display, UIColorRole.TextPrimary, TextAlignmentOptions.Center);
            Object.DestroyImmediate(client.endingTitle.GetComponent<UIThemeColor>());
            client.endingLabel = UIKit.Text(card, "Epilogue", string.Empty, UIText.Body, UIColorRole.TextSecondary, TextAlignmentOptions.Center);
            client.endingLabel.fontSize = 20f;
            client.endingLabel.lineSpacing = 8f;
            client.endingLabel.textWrappingMode = TextWrappingModes.Normal;
            client.endingSummary = UIKit.Text(card, "Summary", string.Empty, UIText.Small, UIColorRole.TextMuted, TextAlignmentOptions.Center);
            client.endingSummary.textWrappingMode = TextWrappingModes.Normal;
            client.endingSummary.lineSpacing = 6f;
            RectTransform chips = UIKit.Node(card, "Chips");
            UIKit.Row(chips.gameObject, 10f, new RectOffset(0, 0, 10, 0), TextAnchor.MiddleCenter);
            client.endingNextCase = HudKit.Chip(chips, "NextCase", "hourglass_bottom", "Next case in 20s", Amber, 32f, UIText.Small);
            client.endingSolved = HudKit.Chip(chips, "Solved", "star_medal", "Cases solved: 0", Amber, 32f, UIText.Small);
            client.endingPopup = card.GetComponent<RyanAssets.UI.GameHud.HudPopup>();
        }
    }
}
