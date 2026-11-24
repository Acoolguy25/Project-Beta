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
    /// <summary>
    /// Rebuildable presentation assets for The Drowned Choir: lighting, the flashlight, the HUD and its presenters.
    /// The night's items and the priest puppet are in CH_Authoring.Items.cs, the scene's story set in CH_StorySetAuthoring.
    /// </summary>
    public static partial class CH_Authoring {
        public const string Root = "Assets/Universes/UniverseData/classic_horror";
        static readonly Color Amber = new(0.78f, 0.55f, 0.28f);

        public static void EnsureFolders() {
            foreach (string folder in new[] { "Prefabs", "Materials", "Data", "Scenes" })
                if (!AssetDatabase.IsValidFolder(Root + "/" + folder)) AssetDatabase.CreateFolder(Root, folder);
            if (!AssetDatabase.IsValidFolder(Root + "/Prefabs/Items")) AssetDatabase.CreateFolder(Root + "/Prefabs", "Items");
            if (!AssetDatabase.IsValidFolder("Packages/com.ryan.gameframework/Runtime/Tools/Flashlight")) AssetDatabase.CreateFolder("Packages/com.ryan.gameframework/Runtime/Tools", "Flashlight");
        }
        static Material Material(string name, Color color, bool glow = false, string folder = null) {
            string path = (folder ?? Root + "/Materials") + "/" + name + ".mat";
            var result = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (result == null) { result = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(result, path); }
            result.SetColor("_BaseColor", color);
            result.SetFloat("_Smoothness", 0.18f);
            SetEmission(result, glow ? color * 2f : Color.black);
            EditorUtility.SetDirty(result);
            return result;
        }
        /// <summary>
        /// Turns URP emission on with <paramref name="emission"/>, or off when it is black. The project's
        /// material validation clears _EMISSION unless the GI flags say the material is emissive, so both are set.
        /// </summary>
        public static void SetEmission(Material material, Color emission) {
            bool glows = emission.maxColorComponent > 0f;
            material.SetColor("_EmissionColor", emission);
            material.globalIlluminationFlags = glows ? MaterialGlobalIlluminationFlags.RealtimeEmissive : MaterialGlobalIlluminationFlags.EmissiveIsBlack;
            if (glows) material.EnableKeyword("_EMISSION");
            else material.DisableKeyword("_EMISSION");
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
        const float LanternOffset = 2.1f, LanternClearance = 0.35f, LanternProbeHeight = 6f, LanternRange = 11f;
        // The KayKit atlas is a light beige; the posts take a dark, weathered copy so they don't glow in the flashlight.
        static readonly Color LanternPostTint = new(0.3f, 0.25f, 0.21f);
        static readonly Color LanternGlow = new Color(1f, 0.58f, 0.24f) * 1.9f;

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
                Material atlas = glass.sharedMaterial;
                foreach (var renderer in visual.GetComponentsInChildren<Renderer>())
                    if (renderer != glass) renderer.sharedMaterial = LanternPostMaterial(atlas);
                glass.sharedMaterial = LanternGlassMaterial(atlas);
                glass.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                var post = root.AddComponent<CapsuleCollider>();
                post.radius = 0.12f; post.height = 3.2f; post.center = new Vector3(0f, 1.6f, 0f);
                return PrefabUtility.SaveAsPrefabAsset(root, SearchLanternPath);
            } finally { Object.DestroyImmediate(root); }
        }

        static Material LanternPostMaterial(Material atlas) {
            string path = Root + "/Materials/SearchLanternPost.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null) { material = new Material(atlas) { name = "SearchLanternPost" }; AssetDatabase.CreateAsset(material, path); }
            else material.CopyPropertiesFromMaterial(atlas);
            material.SetColor("_BaseColor", LanternPostTint);
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0.05f);
            EditorUtility.SetDirty(material);
            return material;
        }

        /// <summary>The KayKit atlas with itself as emission map: the amber glass glows, the dark iron barely does.</summary>
        static Material LanternGlassMaterial(Material atlas) {
            string path = Root + "/Materials/SearchLanternGlass.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null) { material = new Material(atlas) { name = "SearchLanternGlass" }; AssetDatabase.CreateAsset(material, path); }
            else material.CopyPropertiesFromMaterial(atlas);
            material.SetTexture("_EmissionMap", atlas.GetTexture("_BaseMap"));
            SetEmission(material, LanternGlow);
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
                // The gallows arm points along the post's forward. Turned sideways to the record, the
                // lantern hangs beside it instead of over it, out of the top of the view while reading.
                Vector3 arm = toSpot.sqrMagnitude > 0.01f ? Vector3.Cross(Vector3.up, toSpot) : spot.right;
                post.SetPositionAndRotation(ground, Quaternion.LookRotation(arm));
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

        [MenuItem("Ryan/Classic Horror/Rebuild Presentation Prefabs")]
        public static void BuildPresentation() {
            EnsureFolders();
            BuildFlashlight();
            RyanAssets.Editor.NPCCharacterAuthoring.Build();
            BuildItemViews();
            BuildPriestActor();
            BuildMaraActor();
            CH_VoiceAuthoring.Build();
            BuildHud();
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

        // Outline strength when not in focus: an item lies alone in the dark and needs an edge that reads from 4 m
        // as "something to pick up".
        const float ItemIdleRim = 0.65f;

        /// <summary>The amber glow light and CH_ItemGlow (with its code defaults) on an item.</summary>
        static void AddGlow(GameObject root, float range, float idleRim = ItemIdleRim) {
            var light = root.GetComponent<Light>();
            if (light == null) light = root.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = Amber; light.range = range; light.shadows = LightShadows.None;
            // Re-added so the prefab takes the glow's current defaults (a faint idle edge, a strong focus).
            var old = root.GetComponent<CH_ItemGlow>();
            if (old != null) Object.DestroyImmediate(old, true);
            var glow = root.AddComponent<CH_ItemGlow>();
            glow.glowLight = light;
            light.intensity = glow.IdleLight;
            glow.rimRenderers = AddRim(root, RimMaterial());
            var serialized = new SerializedObject(glow);
            serialized.FindProperty("idleRim").floatValue = idleRim;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>A copy of a pack prop's material, saved as Materials/<paramref name="name"/>, with its colour multiplied by <paramref name="tint"/>.</summary>
        static Material TintedCopy(GameObject prop, string name, Color tint, float smoothness) {
            var source = prop.GetComponentInChildren<Renderer>().sharedMaterial;
            string path = Root + "/Materials/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null) { material = new Material(source) { name = name }; AssetDatabase.CreateAsset(material, path); }
            else material.CopyPropertiesFromMaterial(source);
            material.SetColor("_BaseColor", tint);
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", smoothness);
            // The pack material keeps _EMISSION on with a black colour; the copy turns it off cleanly.
            SetEmission(material, Color.black);
            EditorUtility.SetDirty(material);
            return material;
        }

        static void UseMaterial(GameObject root, Material material) {
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true)) {
                var materials = renderer.sharedMaterials;
                for (int i = 0; i < materials.Length; i++) materials[i] = material;
                renderer.sharedMaterials = materials;
            }
        }

        static Bounds BoundsOf(GameObject root) {
            var renderers = root.GetComponentsInChildren<Renderer>();
            Bounds bounds = renderers[0].bounds;
            foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
            return bounds;
        }

        /// <summary>The height of a stand's top surface above its parent's origin.</summary>
        static float TopOf(GameObject stand) => BoundsOf(stand).max.y - stand.transform.parent.position.y;

        /// <summary>Scales a visual so it is <paramref name="height"/> tall, standing on its parent's origin.</summary>
        static void FitHeight(GameObject visual, float height) {
            Bounds before = BoundsOf(visual);
            visual.transform.localScale *= height / Mathf.Max(0.001f, before.size.y);
            Bounds after = BoundsOf(visual);
            visual.transform.position += Vector3.up * (visual.transform.parent.position.y - after.min.y);
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
            // A thin amber outline and only a hint of fresnel: the prop keeps its own material.
            material.SetColor("_RimColor", RimColour);
            material.SetFloat("_RimPower", RimPower);
            material.SetFloat("_OutlineWidth", OutlineWidth);
            material.SetFloat("_OutlineStrength", OutlineStrength);
            EditorUtility.SetDirty(material);
            return material;
        }
        static readonly Color RimColour = new(1.5f, 0.9f, 0.35f);
        const float RimPower = 5f, OutlineWidth = 0.008f, OutlineStrength = 0.55f;

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
        /// The investigation HUD, made from the shared HudKit so it matches the rest of the game, in a horror key: dark
        /// glass, one amber accent, and nothing on screen the night doesn't need right now. The objective card top centre
        /// with the compass under it, prompts low, subtitles at the bottom, the journal and ending as cards, and over it
        /// all the cutscene layer (letterbox, title card, fades) and the jump scares. The same prefab carries the
        /// client's presenters: voices, items, cutscenes, apparitions, fear loop, ambient scares and the atmosphere.
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
            client.items = root.AddComponent<CH_ItemPresenter>();
            client.voice = root.AddComponent<CH_VoicePlayer>();
            client.cutscenes = root.AddComponent<CH_CutsceneDirector>();
            client.apparitions = root.AddComponent<CH_Apparitions>();
            client.boatIcon = HudKit.Icon("compass");
            client.choirIcon = HudKit.Icon("tombstone");
            client.bellIcon = HudKit.Icon("crowned_skull");
            client.solvedIcon = HudKit.Icon("laurels_trophy");
            client.failedIcon = HudKit.Icon("skull");

            // Everything a cutscene fades away; the subtitles, scares and cutscene layer live outside it.
            RectTransform gameplay = UIKit.Fill(canvas, "Gameplay");
            client.cutscenes.gameplayHud = gameplay.gameObject.AddComponent<CanvasGroup>();
            client.cutscenes.gameplayHud.blocksRaycasts = false;
            BuildCaseStatus(gameplay, client);
            BuildCompass(gameplay, client);

            RectTransform reticle = UIKit.Pinned(gameplay, "Reticle", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(6f, 6f));
            UIKit.Rounded(reticle, UIColorRole.TextPrimary, 3f, 0.75f, raycast: false);

            RectTransform interaction = HudKit.KeyPrompt(gameplay, "Interaction", "E", "Take", Amber, out client.interactionLabel, out UnityEngine.UI.Image key, 46f);
            interaction.anchorMin = interaction.anchorMax = interaction.pivot = new Vector2(0.5f, 0.5f);
            interaction.anchoredPosition = new Vector2(0f, -110f);
            client.interactionKey = key.gameObject;
            client.interactionPrompt = HudKit.Popup(interaction, false);
            BuildBoarding(gameplay, client);

            BuildHiding(canvas, client);
            BuildRadio(canvas, client.radio);
            BuildJournal(canvas, client.journal);
            BuildEnding(canvas, client);
            BuildCutsceneLayer(canvas, client.cutscenes);
            BuildAudio(root, client);
            BuildVoices(root, client);
            BuildAtmosphere(atmosphere);
            BuildEncounter(root, client);
            BuildApparitions(client);
            UIKit.SavePrefab(root, Root + "/Prefabs/InvestigationHUD.prefab");
        }

        // Sorting above the shared HUD (top bar, player list, chat): the cutscene layer, then the subtitles over it, then scares.
        const int CutsceneSortingOrder = 800, SubtitleSortingOrder = 850;

        static Canvas OwnCanvas(GameObject target, int order) {
            var layer = target.AddComponent<Canvas>();
            layer.overrideSorting = true;
            layer.sortingOrder = order;
            return layer;
        }

        /// <summary>Letterbox bars, a title card and a full-screen black for cuts, on their own canvas over the shared HUD.</summary>
        static void BuildCutsceneLayer(RectTransform canvas, CH_CutsceneDirector director) {
            RectTransform layer = UIKit.Fill(canvas, "Cutscene");
            OwnCanvas(layer.gameObject, CutsceneSortingOrder);
            var group = layer.gameObject.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false; group.interactable = false; group.ignoreParentGroups = true;

            director.topBar = Panel("Top bar", layer, new Vector2(0f, 1f), Vector2.one, Color.black).rectTransform;
            director.topBar.pivot = new Vector2(0.5f, 1f);
            director.topBar.sizeDelta = new Vector2(0f, 0f);
            director.bottomBar = Panel("Bottom bar", layer, Vector2.zero, new Vector2(1f, 0f), Color.black).rectTransform;
            director.bottomBar.pivot = new Vector2(0.5f, 0f);
            director.bottomBar.sizeDelta = new Vector2(0f, 0f);

            RectTransform title = UIKit.Pinned(layer, "Title card", new Vector2(0.5f, 0.5f), new Vector2(0f, 40f), new Vector2(1200f, 220f));
            UIKit.Column(title.gameObject, 6f, null, TextAnchor.MiddleCenter);
            director.titleCard = title.gameObject.AddComponent<CanvasGroup>();
            director.titleCard.alpha = 0f;
            director.titleText = UIKit.Text(title, "Title", CH_Story.Title.ToUpperInvariant(), UIText.Display, UIColorRole.TextPrimary, TextAlignmentOptions.Center);
            director.titleText.fontSize = 76f;
            director.titleText.characterSpacing = 14f;
            director.subtitleText = UIKit.Text(title, "Subtitle", "Hollowmere, 1962", UIText.Caption, UIColorRole.TextMuted, TextAlignmentOptions.Center);
            director.subtitleText.color = Amber;
            director.subtitleText.fontSize = 22f;
            Object.DestroyImmediate(director.subtitleText.GetComponent<UIThemeColor>());

            var black = Panel("Blackout", layer, Vector2.zero, Vector2.one, Color.black);
            director.blackout = black.gameObject.AddComponent<CanvasGroup>();
            director.blackout.alpha = 0f; director.blackout.blocksRaycasts = false;
            title.SetAsLastSibling();

            const string sounds = AudioFolder;
            director.thud = Sound(1f, 0.55f, 0f, 0f, sounds + "wood-thud.ogg");
            director.bell = Sound(1f, 0.5f, 0f, 0f, sounds + "chapel-bell-1.ogg", sounds + "chapel-bell-2.ogg");
            director.scream = Sound(0.9f, 0.7f, 0f, 0f, FloodedSounds + "WindHowl.mp3");
            director.doors = Sound(1f, 0.75f, 0f, 0f, sounds + "door-slam-1.ogg", sounds + "door-slam-2.ogg");
            director.engine = Sound(0.8f, 0.45f, 0f, 0f, FloodedSounds + "DeepRattle.mp3");
            director.splash = Sound(0.9f, 0.6f, 0f, 0f, sounds + "creak-3.ogg");
        }

        /// <summary>Hiding: the room through the crack of a wardrobe door, and a line that says how to get out.</summary>
        static void BuildHiding(RectTransform canvas, CH_ClientController client) {
            RectTransform view = UIKit.Fill(canvas, "Hiding");
            client.hidingOverlay = view.gameObject.AddComponent<CanvasGroup>();
            client.hidingOverlay.alpha = 0f; client.hidingOverlay.blocksRaycasts = false;
            var doorColour = new Color(0.02f, 0.015f, 0.01f, 0.97f);
            Panel("Left door", view, Vector2.zero, new Vector2(0.455f, 1f), doorColour);
            Panel("Right door", view, new Vector2(0.545f, 0f), Vector2.one, doorColour);
            // The crack lets in a sliver; its edges are darker still.
            Panel("Crack shadow", view, new Vector2(0.455f, 0f), new Vector2(0.545f, 1f), new Color(0f, 0f, 0f, 0.45f));
            Panel("Top slat", view, new Vector2(0.455f, 0.82f), new Vector2(0.545f, 1f), doorColour);
            Panel("Bottom slat", view, new Vector2(0.455f, 0f), new Vector2(0.545f, 0.16f), doorColour);
            client.hidingLabel = UIKit.Text(view, "Hint", "Hiding  ·  press E to leave", UIText.Strong, UIColorRole.TextSecondary, TextAlignmentOptions.Center);
            var hint = client.hidingLabel.rectTransform;
            // Above the shared hotbar, which keeps the bottom of the screen.
            hint.anchorMin = new Vector2(0f, 0.2f); hint.anchorMax = new Vector2(1f, 0.25f); hint.offsetMin = hint.offsetMax = Vector2.zero;
        }

        /// <summary>The boarding countdown: big, centred above the prompt, impossible to miss.</summary>
        static void BuildBoarding(RectTransform canvas, CH_ClientController client) {
            RectTransform card = HudKit.Card(canvas, "Boarding", new Vector2(0.5f, 0.5f), new Vector2(0f, 170f), new Vector2(560f, 76f));
            HudKit.AccentStripe(card, new Color(0.86f, 0.24f, 0.2f), 4f);
            UIKit.Row(card.gameObject, 12f, new RectOffset(22, 26, 10, 10), TextAnchor.MiddleCenter);
            HudKit.Icon(card, "directions_run", 34f, new Color(0.86f, 0.24f, 0.2f));
            client.boardingLabel = UIKit.Text(card, "Countdown", "GET TO THE BOAT  ·  25", UIText.Title, UIColorRole.TextPrimary, TextAlignmentOptions.Center);
            client.boardingPopup = card.GetComponent<RyanAssets.UI.GameHud.HudPopup>();
        }

        /// <summary>Who speaks, and how each voice is captioned. The 3D sources carry the priest and the dead.</summary>
        static void BuildVoices(GameObject root, CH_ClientController client) {
            CH_VoicePlayer voice = client.voice;
            voice.subtitles = client.radio;
            voice.ear = Source(root.transform, "VoiceEar");
            voice.monster = Spatial(Source(root.transform, "VoicePriest"), 4f, 70f);
            voice.world = Spatial(Source(root.transform, "VoiceWorld"), 2f, 45f);
            // The subtitle's emblem says where a voice comes from: the radio, the priest, a whisper, the dead, or Mara in person.
            voice.radioIcon = UIThemeAssets.Icon("pocket_radio");
            voice.defaultIcon = UIThemeAssets.Icon("candle");
            voice.styles = new[] {
                new CH_VoicePlayer.SpeakerStyle { speaker = "MARA", caption = new Color(0.95f, 0.76f, 0.46f), icon = UIThemeAssets.Icon("person") },
                new CH_VoicePlayer.SpeakerStyle { speaker = "FATHER CRANE", caption = new Color(0.9f, 0.3f, 0.26f), icon = UIThemeAssets.Icon("crowned_skull") },
                new CH_VoicePlayer.SpeakerStyle { speaker = "LILY", caption = new Color(0.68f, 0.84f, 1f), icon = UIThemeAssets.Icon("ghost") },
                new CH_VoicePlayer.SpeakerStyle { speaker = "???", caption = new Color(0.62f, 0.6f, 0.66f), icon = UIThemeAssets.Icon("eye") },
            };
        }

        static AudioSource Spatial(AudioSource source, float minDistance, float maxDistance) {
            source.spatialBlend = 1f;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.minDistance = minDistance;
            source.maxDistance = maxDistance;
            source.dopplerLevel = 0f;
            return source;
        }

        /// <summary>The priest where he cannot be: the stings for when he is seen and when he is gone.</summary>
        static void BuildApparitions(CH_ClientController client) {
            CH_Apparitions apparitions = client.apparitions;
            apparitions.voice = client.voice;
            apparitions.jumpscare = client.jumpscare;
            apparitions.revealSting = Sound(1f, 0.6f, 0f, 0f, AudioFolder + "wood-thud.ogg");
            apparitions.fadeSting = Sound(0.7f, 0.5f, 0f, 0f, AudioFolder + "creak-1.ogg", AudioFolder + "creak-2.ogg");
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

        // Above every shared HUD canvas (top bar, player list, chat).
        const int EncounterSortingOrder = 900;

        /// <summary>The priest's face, the jumpscare sting and the lens burst (chromatic aberration, vignette, distortion).</summary>
        static void BuildEncounter(GameObject root, CH_ClientController client) {
            var scare = root.AddComponent<CH_Jumpscare>();
            client.jumpscare = scare;
            var scarePanel = Panel("Encounter", root.transform, Vector2.zero, Vector2.one, Color.black);
            // Its own canvas, sorted above the shared top bar and player list, so nothing of the
            // everyday HUD (coins, chat, the case card) is drawn over the face.
            var scareCanvas = scarePanel.gameObject.AddComponent<Canvas>();
            scareCanvas.overrideSorting = true;
            scareCanvas.sortingOrder = EncounterSortingOrder;
            scare.overlay = scarePanel.gameObject.AddComponent<CanvasGroup>();
            scare.overlay.alpha = 0; scare.overlay.blocksRaycasts = false; scare.overlay.interactable = false;
            // The picture fills the screen (EncounterFace.png: the face cropped under the chin over a red glow),
            // keeping its aspect and overflowing the shorter side rather than letterboxing.
            var face = Rect("The face", scarePanel.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            scare.face = face;
            var portrait = face.gameObject.AddComponent<UnityEngine.UI.RawImage>();
            portrait.texture = CH_EncounterFace.Build(); portrait.raycastTarget = false;
            scare.picture = portrait;
            var fit = face.gameObject.AddComponent<UnityEngine.UI.AspectRatioFitter>();
            fit.aspectMode = UnityEngine.UI.AspectRatioFitter.AspectMode.EnvelopeParent;
            fit.aspectRatio = (float)portrait.texture.width / portrait.texture.height;
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

        /// <summary>Top centre: the act, the objective, what it needs, progress, the clock and what you are carrying.</summary>
        static void BuildCaseStatus(RectTransform canvas, CH_ClientController client) {
            RectTransform card = UIKit.Pinned(canvas, "CaseStatus", new Vector2(0.5f, 1f), new Vector2(0f, -64f), new Vector2(560f, 100f));
            client.caseStatusCard = card.gameObject;
            HudKit.Glass(card, 16f);
            UIKit.Row(card.gameObject, 14f, new RectOffset(14, 20, 12, 12), TextAnchor.MiddleLeft);
            UIKit.FitContent(card.gameObject, false, true);
            HudKit.Emblem(card, "Emblem", "compass", 58f, Amber, out _, out client.caseIcon);

            RectTransform text = UIKit.Node(card, "Text");
            UIKit.Size(text, -1f, -1f, 1f);
            UIKit.Column(text.gameObject, 3f, null, TextAnchor.MiddleLeft);
            client.chapterLabel = UIKit.Text(text, "Chapter", CH_Story.Title, UIText.Caption, UIColorRole.TextMuted);
            client.chapterLabel.color = Amber;
            Object.DestroyImmediate(client.chapterLabel.GetComponent<UIThemeColor>());
            client.objectiveLabel = UIKit.Text(text, "Objective", "Crossing the reservoir...", UIText.Strong);
            client.objectiveLabel.fontSize = 18f;
            client.objectiveLabel.textWrappingMode = TextWrappingModes.Normal;
            client.detailLabel = UIKit.Text(text, "Detail", string.Empty, UIText.Small, UIColorRole.TextSecondary);
            client.detailLabel.textWrappingMode = TextWrappingModes.Normal;

            RectTransform chips = UIKit.Node(text, "Chips");
            UIKit.Row(chips.gameObject, 8f, new RectOffset(0, 0, 4, 0), TextAnchor.MiddleLeft);
            client.progressChip = HudKit.Chip(chips, "Progress", "compass", "0 / 3", Amber, 28f, UIText.Small);
            client.timerChip = HudKit.Chip(chips, "Time", "hourglass", "00:00", Amber, 28f, UIText.Small);
            client.carryChip = HudKit.Chip(chips, "Carrying", "pan_tool", "Nothing", Amber, 28f, UIText.Small);
            client.carryChip.gameObject.SetActive(false);
            RectTransform journal = UIKit.Node(chips, "JournalHint");
            UIKit.Row(journal.gameObject, 6f, new RectOffset(6, 0, 0, 0), TextAnchor.MiddleLeft);
            HudKit.KeyCap(journal, "F");
            UIKit.Text(journal, "Label", "Journal", UIText.Small, UIColorRole.TextSecondary);
            client.journal.hint = journal;
            RyanAssets.UI.GameHud.HudChip badge = HudKit.Chip(chips, "NewEntry", "notebook", "New entry", Amber, 28f, UIText.Small);
            client.journal.newEntryBadge = HudKit.Popup(badge, false);
        }

        /// <summary>Under the objective: an arrow towards the nearest thing the act needs (or where your item goes) and how far.</summary>
        static void BuildCompass(RectTransform canvas, CH_ClientController client) {
            RectTransform pill = UIKit.Pinned(canvas, "ObjectiveCompass", new Vector2(0.5f, 1f), new Vector2(0f, -184f), new Vector2(260f, 38f));
            HudKit.Glass(pill, 19f);
            UIKit.Row(pill.gameObject, 8f, new RectOffset(8, 16, 0, 0), TextAnchor.MiddleLeft);
            UIKit.FitContent(pill.gameObject, true, false);
            RectTransform pointer = UIKit.Node(pill, "DirectionPointer");
            UIKit.Size(pointer, 26f, 26f);
            UnityEngine.UI.Image arrow = HudKit.Icon(pointer, "navigation", 22f, Amber);
            UIKit.Center(arrow.rectTransform);
            var readout = UIKit.Text(pill, "Readout", "OBJECTIVE   FORWARD   12 m", UIText.Caption, UIColorRole.TextPrimary);
            client.objectiveCompass = canvas.gameObject.AddComponent<ObjectiveCompass>();
            client.objectiveCompass.Configure(pill.gameObject, pointer, readout, "OBJECTIVE");
        }

        /// <summary>Bottom centre: the subtitles, as a card that fades in for each line (with static over Mara's radio).</summary>
        static void BuildRadio(RectTransform canvas, CH_RadioFeed radio) {
            RectTransform card = HudKit.Card(canvas, "RadioTranscript", new Vector2(0.5f, 0f), new Vector2(0f, 120f), new Vector2(820f, 120f));
            HudKit.AccentStripe(card, Amber, 4f, left: true);
            UIKit.Row(card.gameObject, 16f, new RectOffset(22, 26, 18, 18), TextAnchor.UpperLeft);
            UIKit.FitContent(card.gameObject, false, true);
            HudKit.Emblem(card, "Emblem", "pocket_radio", 52f, Amber, out _, out radio.emblem);
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
            // Subtitles read over the cutscene letterbox too.
            OwnCanvas(card.gameObject, SubtitleSortingOrder);
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
            UIKit.Text(titles, "Title", "What was said tonight", UIText.Title);
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
            var note = UIKit.Text(footer, "Warning", "Hollowmere does not wait while you read.", UIText.Small, UIColorRole.TextSecondary);
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

        /// <summary>The night's ending: which one, the epilogue, the night in numbers and when the next night starts.</summary>
        const float EndingSummarySize = 18f;

        static void BuildEnding(RectTransform canvas, CH_ClientController client) {
            RectTransform card = HudKit.Card(canvas, "CaseEnding", new Vector2(0.5f, 0.5f), new Vector2(0f, 20f), new Vector2(860f, 420f));
            client.endingStripe = HudKit.AccentStripe(card, Amber, 5f);
            UIKit.Column(card.gameObject, 10f, new RectOffset(48, 48, 32, 30), TextAnchor.UpperCenter);
            UIKit.FitContent(card.gameObject, false, true);
            HudKit.Emblem(card, "Emblem", "laurels_trophy", 92f, Amber, out client.endingDisc, out client.endingIcon);
            UIKit.Text(card, "Kicker", "The night is over", UIText.Caption, UIColorRole.TextMuted, TextAlignmentOptions.Center);
            client.endingTitle = UIKit.Text(card, "Title", "YOU ESCAPED", UIText.Display, UIColorRole.TextPrimary, TextAlignmentOptions.Center);
            Object.DestroyImmediate(client.endingTitle.GetComponent<UIThemeColor>());
            client.endingLabel = UIKit.Text(card, "Epilogue", string.Empty, UIText.Body, UIColorRole.TextSecondary, TextAlignmentOptions.Center);
            client.endingLabel.fontSize = 20f;
            client.endingLabel.lineSpacing = 8f;
            client.endingLabel.textWrappingMode = TextWrappingModes.Normal;
            // The night in numbers: readable body text, not muted small print.
            client.endingSummary = UIKit.Text(card, "Summary", string.Empty, UIText.Body, UIColorRole.TextSecondary, TextAlignmentOptions.Center);
            client.endingSummary.fontSize = EndingSummarySize;
            client.endingSummary.textWrappingMode = TextWrappingModes.Normal;
            client.endingSummary.lineSpacing = 6f;
            RectTransform chips = UIKit.Node(card, "Chips");
            UIKit.Row(chips.gameObject, 10f, new RectOffset(0, 0, 10, 0), TextAnchor.MiddleCenter);
            client.endingNextCase = HudKit.Chip(chips, "NextNight", "hourglass_bottom", "Next night in 20s", Amber, 32f, UIText.Small);
            client.endingSolved = HudKit.Chip(chips, "Escapes", "star_medal", "Escapes tonight: 0", Amber, 32f, UIText.Small);
            client.endingPopup = card.GetComponent<RyanAssets.UI.GameHud.HudPopup>();
        }
    }
}
