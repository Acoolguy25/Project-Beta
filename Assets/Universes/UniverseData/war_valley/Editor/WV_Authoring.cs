using System;
using System.Collections.Generic;
using System.Linq;
using FishNet.Component.Animating;
using FishNet.Component.Transforming;
using FishNet.Object;
using RyanAssets.Shared.Combat;
using RyanAssets.Shared.Component;
using RyanAssets.Shared.Declarations;
using RyanAssets.Shared.WorldUI;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using Unity.AI.Navigation;
using Universes.UniverseData.war_valley.Shared;

namespace Universes.UniverseData.war_valley.Editor {
    /// <summary>
    /// Rebuildable War Valley prefabs: the buildable structures, the units they train, and the shared
    /// presentation pieces both use.
    /// <para>
    /// Everything here is generated from authored art already in the project - the Cartoon Military
    /// Model Pack for structures and vehicles, the Space Robot Kyle rig for infantry - and every model
    /// is nested as a connected prefab instance rather than copied, so a change to the source art still
    /// flows through. Re-running a rebuild is safe: existing prefabs are replaced in place, which keeps
    /// their GUIDs and therefore every scene and FishNet spawnable-prefab reference to them.
    /// </para>
    /// <para>
    /// Every generated prefab is stamped with <see cref="AuthoringVersion"/>. When the Editor loads
    /// with a structure or unit prefab missing or stamped older than this script - the code changed
    /// what it builds, or added a structure such as the gate - the prefabs are rebuilt once that
    /// session, the way the commander HUD already is, so pulling new authoring code never leaves
    /// stale or missing prefabs behind.
    /// </para>
    /// </summary>
    public static partial class WV_Authoring {
        public const string Root = "Assets/Universes/UniverseData/war_valley";

        /// <summary>
        /// Stamp written into each generated prefab's import settings. Change it whenever the
        /// prefabs this script builds change, and every copy older than it is rebuilt on next load.
        /// </summary>
        const string AuthoringVersion = "war-valley-authoring-4";
        const string AutoRebuildSessionKey = "WV_Authoring.AutoRebuildAttempted";
        const string PackRoot = "Assets/CartoonMilitaryModelPack/Prefebs";
        const string StructuresFolder = Root + "/Structures";
        const string UnitsFolder = Root + "/Units";
        const string PresentationFolder = Root + "/Presentation";

        const string RobotModelPath = "Assets/UnityTechnologies/SpaceRobotKyle/Models/KyleRobot.fbx";
        const string RobotControllerPath = "Packages/com.ryan.gameframework/Runtime/Characters/CustomAnimations/RobotNPC.controller";
        const string CircleTexturePath = "Assets/GabrielAguiarProductions/FreeQuickEffectsVol1/Textures/Circle01_v1.png";

        // Shared presentation every structure reuses rather than growing a War Valley copy of.
        const string WorldTimerBarPath = "Packages/com.ryan.core/Runtime/Shared/WorldUI/WorldTimerBar.prefab";
        const string EffectsRoot = "Assets/GabrielAguiarProductions/FreeQuickEffectsVol1/Prefabs";
        const string ExplosionVfxPath = EffectsRoot + "/vfx_Explosion_01.prefab";
        const string SmokeVfxPath = EffectsRoot + "/vfx_Smoke_01.prefab";
        const string MuzzleFlashVfxPath = EffectsRoot + "/vfx_MuzzleFlash_01.prefab";
        const string GunShotAudioPath = "Packages/com.ryan.gameframework/Runtime/Tools/Audio/pistol-gun-shot.mp3";
        const string ShieldVfxPath = EffectsRoot + "/vfx_Shield_01.prefab";
        /// <summary>Radius the shield effect is authored at: its largest billboard is ten units across.</summary>
        const float ShieldVfxRadius = 5f;
        const string GatePostPath = PackRoot + "/Building_Prefebs/Fence_02_Cornor_Prefeb.prefab";

        /// <summary>Thinnest a wall's collision box is allowed to be, so it reliably stops a body or a shot.</summary>
        const float MinWallDepth = 0.6f;

        /// <summary>Metres of construction site left around a thin structure's strip on each axis.</summary>
        const float SitePadding = 1.5f;

        static readonly int StructureLayer = LayerMask.NameToLayer("Structure");
        static readonly int CharacterLayer = LayerMask.NameToLayer("Character");
        static readonly int IgnoreRaycastLayer = LayerMask.NameToLayer("Ignore Raycast");

        // --- Entry points ----------------------------------------------------

        [MenuItem("Ryan/War Valley/Rebuild All Prefabs")]
        public static void RebuildAll() {
            EnsureFolders();
            BuildScaffold();
            BuildSiteBox();
            BuildSelectionIndicator();
            BuildRangeIndicator();
            BuildUnits();
            BuildStructures();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"War Valley: rebuilt {Structures.Length} structures and {Units.Length} units.");
        }

        [MenuItem("Ryan/War Valley/Rebuild Structure Prefabs")]
        public static void RebuildStructures() {
            EnsureFolders();
            BuildRangeIndicator();
            BuildScaffold();
            BuildSiteBox();
            BuildStructures();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        [MenuItem("Ryan/War Valley/Rebuild Unit Prefabs")]
        public static void RebuildUnits() {
            EnsureFolders();
            BuildSelectionIndicator();
            BuildRangeIndicator();
            BuildUnits();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        /// <summary>
        /// Rebuilds every structure and unit once per Editor session when any of them is missing or
        /// predates <see cref="AuthoringVersion"/>. Skipped in batch mode, in play mode, and in a
        /// ParrelSync clone - a clone shares this project's assets, and two Editors rebuilding the
        /// same prefabs at once would fight over them.
        /// </summary>
        internal static void RebuildIfStale() {
            if (Application.isBatchMode
                || EditorApplication.isPlayingOrWillChangePlaymode
                || IsProjectClone()
                || SessionState.GetBool(AutoRebuildSessionKey, false))
                return;

            List<string> stale = FindStalePrefabs();
            if (stale.Count == 0)
                return;

            SessionState.SetBool(AutoRebuildSessionKey, true);
            Debug.Log(
                $"War Valley: {stale.Count} generated prefab(s) are missing or predate the authoring code " +
                $"({string.Join(", ", stale)}); rebuilding structures and units.");
            if (Structures.All(def => AssetDatabase.LoadAssetAtPath<GameObject>(StructurePath(def)) != null)
                && Units.All(def => AssetDatabase.LoadAssetAtPath<GameObject>(UnitPath(def)) != null))
                UpgradeGameplayPrefabs();
            else
                RebuildAll();
        }

        static bool IsProjectClone() => Application.dataPath.Replace('\\', '/').Contains("_clone_");

        static List<string> FindStalePrefabs() {
            var stale = new List<string>();
            foreach (StructureDef def in Structures)
                AddIfStale(StructurePath(def), stale);
            foreach (UnitDef def in Units)
                AddIfStale(UnitPath(def), stale);
            return stale;
        }

        static void AddIfStale(string path, List<string> stale) {
            AssetImporter importer = AssetImporter.GetAtPath(path);
            if (importer == null || importer.userData != AuthoringVersion)
                stale.Add(System.IO.Path.GetFileNameWithoutExtension(path));
        }

        static void EnsureFolders() {
            foreach (string folder in new[] { "Structures", "Units", "Presentation", "Client" }) {
                if (!AssetDatabase.IsValidFolder($"{Root}/{folder}"))
                    AssetDatabase.CreateFolder(Root, folder);
            }
        }

        // --- Presentation ----------------------------------------------------

        /// <summary>
        /// The construction hoarding every site stands inside. Assembled from the pack's own fence
        /// sections so a half-built base reads as a fenced-off works rather than as debug geometry.
        /// </summary>
        static GameObject BuildScaffold() {
            string path = PresentationFolder + "/WV_ConstructionScaffold.prefab";
            var root = new GameObject("ConstructionScaffold");
            try {
                GameObject fenceSource = Load<GameObject>(PackRoot + "/Building_Prefebs/Fence_02_Prefeb.prefab");
                GameObject cornerSource = Load<GameObject>(PackRoot + "/Building_Prefebs/Fence_02_Cornor_Prefeb.prefab");

                // Pieces go under an inner transform so the assembled hoarding can be normalized to
                // exactly one grid cell afterwards, whatever proportions the source fence art has.
                var hoarding = new GameObject("Hoarding");
                hoarding.transform.SetParent(root.transform, false);

                const float half = WV_Rules.GridSize * 0.5f;
                // The pack's fence section runs along its own Z axis, so a run that should read as
                // north-south is placed unrotated and an east-west run is turned through 90 degrees.
                var sides = new (Vector3 position, float yaw)[] {
                    (new Vector3(0f, 0f, half), 90f),
                    (new Vector3(0f, 0f, -half), 270f),
                    (new Vector3(half, 0f, 0f), 0f),
                    (new Vector3(-half, 0f, 0f), 180f)
                };
                foreach ((Vector3 position, float yaw) in sides)
                    PlaceFitted(fenceSource, hoarding.transform, position, yaw, WV_Rules.GridSize, "FenceSection");

                var corners = new[] {
                    new Vector3(half, 0f, half), new Vector3(-half, 0f, half),
                    new Vector3(half, 0f, -half), new Vector3(-half, 0f, -half)
                };
                foreach (Vector3 corner in corners)
                    PlaceFitted(cornerSource, hoarding.transform, corner, 0f, WV_Rules.GridSize * 0.3f, "FenceCorner");

                // Normalize to one cell exactly. A structure then scales the whole prefab by its
                // footprint and gets hoarding that matches the slot instead of overhanging it.
                Bounds bounds = WorldBounds(hoarding);
                float widest = Mathf.Max(bounds.size.x, bounds.size.z);
                if (widest > 0.0001f)
                    hoarding.transform.localScale = Vector3.one * (WV_Rules.GridSize / widest);
                bounds = WorldBounds(hoarding);
                hoarding.transform.localPosition = new Vector3(-bounds.center.x, -bounds.min.y, -bounds.center.z);

                // The hoarding is decoration around a site that already has its own collider.
                foreach (Collider collider in root.GetComponentsInChildren<Collider>(true))
                    UnityEngine.Object.DestroyImmediate(collider);

                return SavePrefab(root, path);
            } finally {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        /// <summary>
        /// The dark volume drawn over a slot that is being built on, so a site reads as "something is
        /// going up here" from across the valley rather than only from close enough to see the
        /// hoarding. Deliberately translucent and double-sided: the building rises inside it.
        /// </summary>
        static GameObject BuildSiteBox() {
            string materialPath = PresentationFolder + "/WV_ConstructionSite.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (material == null) {
                material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                AssetDatabase.CreateAsset(material, materialPath);
            }
            material.SetColor("_BaseColor", new Color(0.02f, 0.02f, 0.03f, 0.38f));
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", 0f);
            material.SetFloat("_ZWrite", 0f);
            // Both faces, so the box still reads when the camera is inside a large footprint.
            material.SetFloat("_Cull", 0f);
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            EditorUtility.SetDirty(material);

            string path = PresentationFolder + "/WV_ConstructionSiteBox.prefab";
            var root = new GameObject("ConstructionSiteBox");
            try {
                GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
                box.name = "SiteVolume";
                box.transform.SetParent(root.transform, false);
                // A unit cube lifted half its height, so the root's pivot sits on the slot floor and
                // a structure can scale the root straight to (span, height, span).
                box.transform.localPosition = new Vector3(0f, 0.5f, 0f);
                UnityEngine.Object.DestroyImmediate(box.GetComponent<Collider>());
                Renderer renderer = box.GetComponent<Renderer>();
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                return SavePrefab(root, path);
            } finally {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        /// <summary>The ring drawn under a unit the local player has selected.</summary>
        static GameObject BuildSelectionIndicator() {
            string materialPath = PresentationFolder + "/WV_SelectionRing.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (material == null) {
                material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                AssetDatabase.CreateAsset(material, materialPath);
            }
            material.SetTexture("_BaseMap", Load<Texture2D>(CircleTexturePath));
            material.SetColor("_BaseColor", new Color(0.35f, 1f, 0.55f, 0.9f));
            // Additive-ish transparent so the ring reads on dirt as well as on grass.
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", 0f);
            material.SetFloat("_ZWrite", 0f);
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            EditorUtility.SetDirty(material);

            string path = PresentationFolder + "/WV_SelectionRing.prefab";
            var root = new GameObject("SelectionRing");
            try {
                GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                quad.name = "Ring";
                quad.transform.SetParent(root.transform, false);
                // Lie flat on the ground, lifted slightly to avoid z-fighting with the terrain.
                quad.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                quad.transform.localPosition = new Vector3(0f, 0.08f, 0f);
                UnityEngine.Object.DestroyImmediate(quad.GetComponent<Collider>());
                Renderer renderer = quad.GetComponent<Renderer>();
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                return SavePrefab(root, path);
            } finally {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        // --- Helpers ---------------------------------------------------------

        /// <summary>
        /// Authors explicit unit body references and reusable materials that retain texture detail
        /// while accepting the commander's colour.
        /// </summary>
        public static void ConfigureUnitBodyColors(GameObject model, WV_OwnerColor ownerColor) {
            Renderer[] bodies = model.GetComponentsInChildren<Renderer>(true)
                .Where(renderer => renderer.name.IndexOf("Body", StringComparison.OrdinalIgnoreCase) >= 0).ToArray();
            if (bodies.Length == 0)
                throw new InvalidOperationException($"War Valley unit '{model.name}' has no authored body renderer.");
            Shader shader = Shader.Find("RyanAssets/Team Color Lit");
            if (shader == null)
                throw new InvalidOperationException("War Valley team-color shader is missing.");
            const string folder = PresentationFolder + "/Materials";
            if (!AssetDatabase.IsValidFolder(folder))
                AssetDatabase.CreateFolder(PresentationFolder, "Materials");
            foreach (Renderer body in bodies) {
                Material[] materials = body.sharedMaterials;
                for (int slot = 0; slot < materials.Length; slot++) {
                    Material source = materials[slot];
                    if (source == null || source.shader == shader)
                        continue;
                    string path = $"{folder}/{source.name}_TeamColor.mat";
                    var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                    if (material == null) {
                        material = new Material(source) { shader = shader, name = source.name + " Team Color" };
                        AssetDatabase.CreateAsset(material, path);
                    }
                    materials[slot] = material;
                }
                body.sharedMaterials = materials;
            }
            SetPrivateField(ownerColor, "bodyRenderers", bodies);
        }

        /// <summary>
        /// Instantiates authored art as a connected nested prefab, scales it so its widest horizontal
        /// axis spans <paramref name="targetSpan"/> world units, and seats it on the ground centred on
        /// the slot. This is what makes a ten-metre hangar and a one-metre fence share one build grid.
        /// </summary>
        static GameObject PlaceFitted(
            GameObject source, Transform parent, Vector3 localPosition, float yaw, float targetSpan, string name) {
            if (source == null)
                throw new InvalidOperationException($"War Valley authoring: missing source art for '{name}'.");

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(source, parent);
            instance.name = name;
            instance.transform.localPosition = Vector3.zero;
            // Orientation is applied before anything is measured, because a rotated model has
            // different bounds and would otherwise be seated and centred against its unrotated ones.
            instance.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            instance.transform.localScale = Vector3.one;

            Bounds bounds = WorldBounds(instance);
            float widest = Mathf.Max(bounds.size.x, bounds.size.z);
            float scale = widest <= 0.0001f ? 1f : targetSpan / widest;
            instance.transform.localScale = Vector3.one * scale;

            // Re-measure after scaling, then offset so the model's base sits on the slot's floor and
            // its footprint is centred on the slot. Parents built here are at the origin with an
            // identity rotation, so local and world space coincide for this arithmetic.
            bounds = WorldBounds(instance);
            Vector3 pivotOffset = instance.transform.position - bounds.center;
            instance.transform.localPosition =
                localPosition + new Vector3(pivotOffset.x, pivotOffset.y + bounds.extents.y, pivotOffset.z);

            return instance;
        }

        /// <summary>
        /// <see cref="PlaceFitted"/> by height: scales the art uniformly until it stands
        /// <paramref name="targetHeight"/> tall, seated on the floor and centred on the slot.
        /// </summary>
        static GameObject PlaceFittedToHeight(
            GameObject source, Transform parent, float yaw, float targetHeight, string name) {
            GameObject instance = PlaceFitted(source, parent, Vector3.zero, yaw, 1f, name);
            float height = WorldBounds(instance).size.y;
            if (height > 0.0001f)
                instance.transform.localScale *= targetHeight / height;

            Bounds bounds = WorldBounds(instance);
            Vector3 pivotOffset = instance.transform.position - bounds.center;
            instance.transform.localPosition = new Vector3(pivotOffset.x, pivotOffset.y + bounds.extents.y, pivotOffset.z);
            return instance;
        }

        /// <summary>Fits a joint post to the adjoining run's depth, keeping its height and centering its base.</summary>
        public static void FitPostFootprint(GameObject post, float depth) {
            Bounds bounds = WorldBounds(post);
            float scale = depth / Mathf.Max(bounds.size.x, bounds.size.z);
            Vector3 localScale = post.transform.localScale;
            localScale.x *= scale;
            localScale.z *= scale;
            post.transform.localScale = localScale;
            bounds = WorldBounds(post);
            Vector3 localCenter = post.transform.parent.InverseTransformPoint(bounds.center);
            Vector3 position = post.transform.localPosition;
            position.x -= localCenter.x;
            position.z -= localCenter.z;
            post.transform.localPosition = position;
            bounds = WorldBounds(post);
            post.transform.position -= Vector3.up * bounds.min.y;
        }

        // Keep the grid span and depth while matching the adjoining wall's height.
        static void MatchModelHeight(GameObject model, float targetHeight) {
            Bounds bounds = WorldBounds(model);
            if (bounds.size.y <= 0.0001f)
                throw new InvalidOperationException("War Valley fence model has no measurable height.");
            Vector3 scale = model.transform.localScale;
            scale.y *= targetHeight / bounds.size.y;
            model.transform.localScale = scale;
            bounds = WorldBounds(model);
            model.transform.position -= Vector3.up * bounds.min.y;
        }

        static Bounds WorldBounds(GameObject instance) {
            Renderer[] renderers = instance.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
                return new Bounds(instance.transform.position, Vector3.one);

            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
                bounds.Encapsulate(renderers[i].bounds);
            return bounds;
        }

        /// <summary>
        /// Adds a component only when RequireComponent has not already supplied it, so a unit does not
        /// end up with two WV_Owned components.
        /// </summary>
        static T Ensure<T>(GameObject target) where T : Component {
            // Deliberately not the ?? operator: GetComponent returns a destroyed-object stand-in that
            // is only null through Unity's overloaded ==, which ?? does not consult.
            T existing = target.GetComponent<T>();
            return existing != null ? existing : target.AddComponent<T>();
        }

        static void SetLayerRecursive(GameObject target, int layer) {
            foreach (Transform child in target.GetComponentsInChildren<Transform>(true))
                child.gameObject.layer = layer;
        }

        /// <summary>
        /// Writes a serialized private field through SerializedObject so the value is recorded exactly
        /// as the Inspector would record it, including object references and enum backing values.
        /// </summary>
        static void SetPrivateField(UnityEngine.Object target, string fieldName, object value) {
            var serialized = new SerializedObject(target);
            SerializedProperty property = serialized.FindProperty(fieldName);
            if (property == null)
                throw new InvalidOperationException(
                    $"War Valley authoring: {target.GetType().Name} has no serialized field '{fieldName}'.");

            switch (value) {
                // A pattern match never binds null, so an intentionally cleared object reference has
                // to be handled before the typed cases or it would fall through and throw.
                case null: property.objectReferenceValue = null; break;
                case int i: property.intValue = i; break;
                case long l: property.longValue = l; break;
                case ulong ul: property.ulongValue = ul; break;
                case float f: property.floatValue = f; break;
                case bool b: property.boolValue = b; break;
                case Vector3 v: property.vector3Value = v; break;
                case string s: property.stringValue = s; break;
                case UnityEngine.Object o: property.objectReferenceValue = o; break;
                case TeamConfig team:
                    property.FindPropertyRelative("realTeam").intValue = (int)team.realTeam;
                    property.FindPropertyRelative("displayTeam").intValue = (int)team.displayTeam;
                    break;
                case Array array:
                    property.arraySize = array.Length;
                    for (int index = 0; index < array.Length; index++) {
                        SerializedProperty element = property.GetArrayElementAtIndex(index);
                        object item = array.GetValue(index);
                        // Enum arrays - a barracks' troop kinds - serialize their backing values,
                        // not object references; casting them to Object threw on every rebuild.
                        if (item is Enum)
                            element.intValue = Convert.ToInt32(item);
                        else
                            element.objectReferenceValue = (UnityEngine.Object)item;
                    }
                    break;
                default:
                    throw new InvalidOperationException(
                        $"War Valley authoring: unsupported field type {value?.GetType().Name} for '{fieldName}'.");
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        static T Load<T>(string path) where T : UnityEngine.Object {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null)
                throw new InvalidOperationException($"War Valley authoring: missing asset at {path}.");
            return asset;
        }

        /// <summary>
        /// Saves over any existing prefab at this path so the asset keeps its GUID, and with it every
        /// scene reference and FishNet spawnable-prefab registration already pointing at it.
        /// </summary>
        static GameObject SavePrefab(GameObject root, string path) {
            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, path);
            Stamp(path);
            return saved;
        }

        /// <summary>Records which version of this script generated the asset, in its import settings.</summary>
        static void Stamp(string path) {
            AssetImporter importer = AssetImporter.GetAtPath(path);
            if (importer == null || importer.userData == AuthoringVersion)
                return;
            importer.userData = AuthoringVersion;
            EditorUtility.SetDirty(importer);
            AssetDatabase.WriteImportSettingsIfDirty(path);
        }
    }

    /// <summary>
    /// Schedules <see cref="WV_Authoring.RebuildIfStale"/> for after the domain has loaded. Kept apart
    /// from the authoring class so loading the domain does not also initialise its definitions.
    /// </summary>
    [InitializeOnLoad]
    static class WV_AuthoringAutoRebuild {
        static WV_AuthoringAutoRebuild() {
            // Deferred: asset operations are not allowed while the domain is still loading.
            EditorApplication.delayCall += WV_Authoring.RebuildIfStale;
        }
    }
}
