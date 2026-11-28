using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Universes.UniverseData.classic_horror.Client;

namespace Universes.UniverseData.classic_horror.Editor {
    /// <summary>
    /// The night's items, the marks for where they go, the candles lit on a freed singer's grave, and the priest as a
    /// client-only puppet. Every item is one prefab that lies in the world, sits in a hand and is set down, so it can
    /// never turn into something else or disappear between those states.
    /// </summary>
    public static partial class CH_Authoring {
        public const string ItemFolder = Root + "/Prefabs/Items/";
        public const string SlotMarkerPath = ItemFolder + "SlotMarker.prefab";
        public const string GraveCandlesPath = ItemFolder + "GraveCandles.prefab";
        public const string PriestActorPath = Root + "/Prefabs/PriestActor.prefab";
        public const string MaraActorPath = Root + "/Prefabs/MaraActor.prefab";

        const string KayKitDungeon = "Assets/ThirdParty/KayKit/DungeonRemastered/Models/";
        const string KayKitFurniture = "Assets/ThirdParty/KayKit/FurnitureBits/Models/";
        const string MonsterPrefabPath = "Packages/com.ryan.gameframework/Runtime/Characters/Monster/Prefabs/Monster.prefab";
        const string PriestModelPath = Root + "/Cursed Priest/Prefab/Priest.prefab";
        // Mara is built on the investigators' body and walk, dressed as a skipper: sou'wester, long oilskin coat, dark overalls.
        const string CrewModelPath = "Assets/UnityTechnologies/SpaceRobotKyle/Models/KyleRobot.fbx";
        const string CrewControllerPath = "Assets/UnityTechnologies/SpaceRobotKyle/Animations/StarterAssetsThirdPerson.controller";
        static readonly Color MaraOilskin = new(0.56f, 0.4f, 0.09f), MaraOveralls = new(0.09f, 0.1f, 0.12f);
        static readonly Color MaraRubber = new(0.05f, 0.06f, 0.05f), MaraSkin = new(0.72f, 0.54f, 0.44f);
        const string SoftDotPath = "Packages/com.ryan.gameframework/Runtime/GameFeel/Generated/Textures/fx_soft_dot.png";
        const float ItemLightRange = 3f;

        static readonly Color FuelRed = new(0.5f, 0.07f, 0.05f), ToolboxGreen = new(0.14f, 0.22f, 0.18f), SteelGrey = new(0.25f, 0.26f, 0.27f);
        static readonly Color BellBronze = new(0.36f, 0.24f, 0.1f), BellYoke = new(0.2f, 0.12f, 0.07f), FlameColour = new(1f, 0.55f, 0.2f);
        static readonly Color HymnalLeather = new(0.32f, 0.1f, 0.08f), FrameWood = new(0.3f, 0.22f, 0.15f), MusicBoxPaint = new(0.78f, 0.72f, 0.82f);

        /// <summary>The file name of each item's prefab, in <see cref="CH_ItemKind"/> order.</summary>
        static readonly string[] ItemPrefabNames = { "FuelCan", "Toolbox", "FerryLantern", "Hymnal", "Photograph", "VigilCandle", "MusicBox", "CranesBell" };

        public static string ItemPrefabPath(CH_ItemKind kind) => ItemFolder + ItemPrefabNames[(int)kind] + ".prefab";

        [MenuItem("Ryan/Classic Horror/Rebuild Item Prefabs")]
        public static void BuildItemViews() {
            EnsureFolders();
            foreach (CH_ItemKind kind in System.Enum.GetValues(typeof(CH_ItemKind))) BuildItem(kind);
            BuildSlotMarker();
            BuildGraveCandles();
            AssetDatabase.SaveAssets();
        }

        static void BuildItem(CH_ItemKind kind) {
            var root = new GameObject(ItemPrefabNames[(int)kind]);
            try {
                var visual = new GameObject("Visual");
                visual.transform.SetParent(root.transform, false);
                switch (kind) {
                    case CH_ItemKind.Fuel: BuildFuelCan(visual.transform); FitHeight(visual, 0.46f); break;
                    case CH_ItemKind.Toolbox: BuildToolbox(visual.transform); FitHeight(visual, 0.3f); break;
                    case CH_ItemKind.Lantern: BuildFerryLantern(visual.transform); break;
                    case CH_ItemKind.Hymnal: FitLargest(Model(KayKitFurniture + "book_single.fbx", visual.transform, "ItemHymnal", HymnalLeather), 0.26f); break;
                    case CH_ItemKind.Photograph: FitLargest(Model(KayKitFurniture + "pictureframe_standing_A.fbx", visual.transform, "ItemPhotograph", FrameWood), 0.3f); break;
                    case CH_ItemKind.Candle: BuildVigilCandle(visual.transform); break;
                    case CH_ItemKind.MusicBox: FitLargest(Model(KayKitDungeon + "box_small_decorated.fbx", visual.transform, "ItemMusicBox", MusicBoxPaint), 0.28f); break;
                    case CH_ItemKind.Bell: BuildBell(visual.transform); FitHeight(visual, 0.5f); break;
                }
                foreach (var collider in root.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(collider);
                AddGlow(root, ItemLightRange);
                PrefabUtility.SaveAsPrefabAsset(root, ItemPrefabPath(kind));
            } finally { Object.DestroyImmediate(root); }
        }

        /// <summary>A pack model under <paramref name="parent"/>, with its atlas material copied and tinted.</summary>
        static GameObject Model(string path, Transform parent, string materialName, Color tint) {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) throw new System.InvalidOperationException("Missing Classic Horror item model: " + path);
            var model = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            UseMaterial(model, TintedCopy(model, materialName, tint, 0.2f));
            return model;
        }

        /// <summary>Scales a visual so its largest side is <paramref name="size"/>, standing on its parent's origin.</summary>
        static void FitLargest(GameObject visual, float size) {
            Bounds before = BoundsOf(visual);
            float largest = Mathf.Max(before.size.x, Mathf.Max(before.size.y, before.size.z));
            visual.transform.localScale *= size / Mathf.Max(0.001f, largest);
            Bounds after = BoundsOf(visual);
            visual.transform.position += Vector3.up * (visual.transform.parent.position.y - after.min.y);
        }

        /// <summary>A red jerrycan: pressed steel body with the cross emboss, three-bar handle and a capped spout.</summary>
        static void BuildFuelCan(Transform parent) {
            Material paint = Material("ItemFuelPaint", FuelRed);
            paint.SetFloat("_Metallic", 0.35f); paint.SetFloat("_Smoothness", 0.4f);
            Material dark = Material("ItemFuelDark", FuelRed * 0.55f);
            Material steel = Material("ItemSteel", SteelGrey);
            steel.SetFloat("_Metallic", 0.8f); steel.SetFloat("_Smoothness", 0.5f);
            Shape("Body", PrimitiveType.Cube, parent, new Vector3(0f, 0.22f, 0f), new Vector3(0.34f, 0.44f, 0.16f), paint);
            for (int side = -1; side <= 1; side += 2) {
                Shape("Emboss A", PrimitiveType.Cube, parent, new Vector3(0f, 0.2f, side * 0.082f), new Vector3(0.36f, 0.035f, 0.01f), dark, new Vector3(0f, 0f, 48f));
                Shape("Emboss B", PrimitiveType.Cube, parent, new Vector3(0f, 0.2f, side * 0.082f), new Vector3(0.36f, 0.035f, 0.01f), dark, new Vector3(0f, 0f, -48f));
            }
            for (int bar = -1; bar <= 1; bar++)
                Shape("Handle " + (bar + 2), PrimitiveType.Cube, parent, new Vector3(bar * 0.075f, 0.475f, 0f), new Vector3(0.03f, 0.07f, 0.035f), paint);
            Shape("Grip", PrimitiveType.Cylinder, parent, new Vector3(-0.015f, 0.51f, 0f), new Vector3(0.03f, 0.1f, 0.03f), paint, new Vector3(0f, 0f, 90f));
            Shape("Spout", PrimitiveType.Cylinder, parent, new Vector3(0.14f, 0.47f, 0f), new Vector3(0.05f, 0.04f, 0.05f), steel, new Vector3(0f, 0f, -30f));
            Shape("Cap", PrimitiveType.Cylinder, parent, new Vector3(0.165f, 0.51f, 0f), new Vector3(0.06f, 0.015f, 0.06f), dark, new Vector3(0f, 0f, -30f));
        }

        /// <summary>A ferryman's steel toolbox: hinged lid, folding handle, two latches and a spanner left on top.</summary>
        static void BuildToolbox(Transform parent) {
            Material paint = Material("ItemToolboxPaint", ToolboxGreen);
            paint.SetFloat("_Metallic", 0.45f); paint.SetFloat("_Smoothness", 0.3f);
            Material steel = Material("ItemSteel", SteelGrey);
            Shape("Box", PrimitiveType.Cube, parent, new Vector3(0f, 0.09f, 0f), new Vector3(0.5f, 0.18f, 0.22f), paint);
            Shape("Lid", PrimitiveType.Cube, parent, new Vector3(0f, 0.2f, 0f), new Vector3(0.52f, 0.05f, 0.23f), paint);
            for (int side = -1; side <= 1; side += 2) {
                Shape("Latch", PrimitiveType.Cube, parent, new Vector3(side * 0.16f, 0.17f, 0.116f), new Vector3(0.04f, 0.06f, 0.012f), steel);
                Shape("Upright", PrimitiveType.Cube, parent, new Vector3(side * 0.12f, 0.25f, 0f), new Vector3(0.02f, 0.06f, 0.02f), steel);
            }
            Shape("Handle", PrimitiveType.Cylinder, parent, new Vector3(0f, 0.28f, 0f), new Vector3(0.025f, 0.13f, 0.025f), steel, new Vector3(0f, 0f, 90f));
            Shape("Spanner", PrimitiveType.Cube, parent, new Vector3(0.08f, 0.232f, -0.05f), new Vector3(0.22f, 0.012f, 0.03f), steel, new Vector3(0f, 25f, 0f));
        }

        /// <summary>The ferry's storm lantern, unlit: the ember inside is the only sign it still works.</summary>
        static void BuildFerryLantern(Transform parent) {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(KayKitHalloween + "lantern_standing.fbx");
            if (model == null) throw new System.InvalidOperationException("KayKit lantern_standing.fbx is missing.");
            var lantern = (GameObject)PrefabUtility.InstantiatePrefab(model, parent);
            var atlas = lantern.GetComponentInChildren<Renderer>().sharedMaterial;
            foreach (var renderer in lantern.GetComponentsInChildren<Renderer>()) renderer.sharedMaterial = LanternPostMaterial(atlas);
            FitHeight(lantern, 0.55f);
            Bounds body = BoundsOf(lantern);
            Shape("Ember", PrimitiveType.Sphere, parent, new Vector3(0f, body.center.y - parent.position.y - body.size.y * 0.08f, 0f),
                Vector3.one * body.size.x * 0.22f, Material("ItemEmber", FlameColour, true));
        }

        /// <summary>A church vigil candle in a dish, still burning: it is the one thing in the village that is warm.</summary>
        static void BuildVigilCandle(Transform parent) {
            var model = Model(KayKitDungeon + "candle_thin.fbx", parent, "ItemCandleWax", new Color(0.86f, 0.82f, 0.7f));
            FitHeight(model, 0.26f);
            Shape("Dish", PrimitiveType.Cylinder, parent, new Vector3(0f, 0.008f, 0f), new Vector3(0.14f, 0.008f, 0.14f), Material("ItemSteel", SteelGrey));
            Bounds wax = BoundsOf(model);
            Shape("Flame", PrimitiveType.Sphere, parent, new Vector3(0f, wax.max.y - parent.position.y + 0.02f, 0f), new Vector3(0.025f, 0.045f, 0.025f),
                Material("ItemFlame", FlameColour, true));
        }

        /// <summary>Father Crane's hand bell: bronze, heavy, with a dark wooden yoke.</summary>
        static void BuildBell(Transform parent) {
            Material bronze = Material("ItemBellBronze", BellBronze);
            bronze.SetFloat("_Metallic", 0.85f); bronze.SetFloat("_Smoothness", 0.55f);
            Shape("Lip", PrimitiveType.Cylinder, parent, new Vector3(0f, 0.02f, 0f), new Vector3(0.3f, 0.02f, 0.3f), bronze);
            Shape("Mouth", PrimitiveType.Cylinder, parent, new Vector3(0f, 0.06f, 0f), new Vector3(0.27f, 0.04f, 0.27f), bronze);
            Shape("Waist", PrimitiveType.Cylinder, parent, new Vector3(0f, 0.15f, 0f), new Vector3(0.21f, 0.08f, 0.21f), bronze);
            Shape("Crown", PrimitiveType.Sphere, parent, new Vector3(0f, 0.24f, 0f), new Vector3(0.21f, 0.15f, 0.21f), bronze);
            Shape("Yoke", PrimitiveType.Cylinder, parent, new Vector3(0f, 0.37f, 0f), new Vector3(0.045f, 0.09f, 0.045f), Material("ItemBellYoke", BellYoke));
        }

        /// <summary>A soft pool of amber light on the ground: where the thing in your hands goes.</summary>
        static void BuildSlotMarker() {
            var root = new GameObject("SlotMarker");
            try {
                var pool = GameObject.CreatePrimitive(PrimitiveType.Quad);
                pool.name = "Pool";
                Object.DestroyImmediate(pool.GetComponent<Collider>());
                pool.transform.SetParent(root.transform, false);
                pool.transform.localPosition = new Vector3(0f, 0.03f, 0f);
                pool.transform.localEulerAngles = new Vector3(90f, 0f, 0f);
                pool.transform.localScale = Vector3.one * 1.4f;
                var renderer = pool.GetComponent<Renderer>();
                renderer.sharedMaterial = GlowPoolMaterial();
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                var light = new GameObject("Glow").AddComponent<Light>();
                light.transform.SetParent(root.transform, false);
                light.transform.localPosition = new Vector3(0f, 0.6f, 0f);
                light.type = LightType.Point; light.color = Amber; light.range = 2.6f; light.intensity = 0.9f; light.shadows = LightShadows.None;
                PrefabUtility.SaveAsPrefabAsset(root, SlotMarkerPath);
            } finally { Object.DestroyImmediate(root); }
        }

        static Material GlowPoolMaterial() {
            string path = Root + "/Materials/SlotMarkerPool.mat";
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null) throw new System.InvalidOperationException("URP particle shader is missing.");
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null) { material = new Material(shader) { name = "SlotMarkerPool" }; AssetDatabase.CreateAsset(material, path); }
            material.shader = shader;
            material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(SoftDotPath));
            material.SetColor("_BaseColor", new Color(1f, 0.62f, 0.25f, 0.55f));
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

        /// <summary>A soft, alpha-blended mist for the lake (the night sky's grey-blue, faint), used by the story set.</summary>
        public static Material LakeMistMaterial() => SoftParticleMaterial("LakeMist", "LakeMistPuff", LakeMistColour, 11);

        /// <summary>Pale foam for the ferry's wake (a little brighter than the mist, so the trail reads behind the hull).</summary>
        public static Material FerryWakeMaterial() => SoftParticleMaterial("FerryWake", "FerryWakeFoam", WakeFoamColour, 29);

        static readonly Color LakeMistColour = new(0.5f, 0.56f, 0.62f), WakeFoamColour = new(0.72f, 0.78f, 0.82f);

        /// <summary>An unlit, alpha-blended particle material on a generated cloud texture (<see cref="CloudTexture"/>).</summary>
        static Material SoftParticleMaterial(string name, string textureName, Color color, int seed) {
            string path = Root + "/Materials/" + name + ".mat";
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null) throw new System.InvalidOperationException("URP particle shader is missing.");
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null) { material = new Material(shader) { name = name }; AssetDatabase.CreateAsset(material, path); }
            material.shader = shader;
            material.SetTexture("_BaseMap", CloudTexture(textureName, seed));
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", 0f);
            material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_ZWrite", 0f);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            EditorUtility.SetDirty(material);
            return material;
        }

        /// <summary>
        /// A ragged puff of cloud: a soft falloff broken up by layered noise and a fine dither, so many of them overlap into
        /// drifting mist with no round edges and no colour bands (a smooth radial dot shows both at low alpha).
        /// </summary>
        static Texture2D CloudTexture(string name, int seed) {
            const int Size = 128;
            const float Raggedness = 0.75f, Dither = 1.5f / 255f;
            string path = Root + "/Materials/" + name + ".png";
            var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
            var random = new System.Random(seed);
            float ox = random.Next(1000), oy = random.Next(1000);
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++) {
                    float u = x / (Size - 1f) * 2f - 1f, v = y / (Size - 1f) * 2f - 1f;
                    float falloff = Mathf.Clamp01(1f - Mathf.Sqrt(u * u + v * v));
                    float noise = 0f, amplitude = 0.5f, frequency = 2.5f;
                    for (int octave = 0; octave < 4; octave++, amplitude *= 0.5f, frequency *= 2f)
                        noise += amplitude * Mathf.PerlinNoise(ox + u * frequency, oy + v * frequency);
                    float alpha = Mathf.Clamp01(falloff * falloff * (3f - 2f * falloff) * Mathf.Lerp(1f, noise * 1.9f, Raggedness));
                    alpha = Mathf.Clamp01(alpha + ((float)random.NextDouble() - 0.5f) * Dither);
                    texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
                }
            System.IO.File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            if (importer.wrapMode != TextureWrapMode.Clamp || !importer.alphaIsTransparency) {
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.alphaIsTransparency = true;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        /// <summary>Three vigil candles lit on a grave once its singer is freed: across the graveyard you can count them.</summary>
        static void BuildGraveCandles() {
            var root = new GameObject("GraveCandles");
            try {
                Vector3[] spots = { new(-0.22f, 0f, 0.05f), new(0.04f, 0f, -0.12f), new(0.24f, 0f, 0.08f) };
                float[] heights = { 0.3f, 0.2f, 0.25f };
                for (int i = 0; i < spots.Length; i++) {
                    var holder = new GameObject("Candle " + (i + 1));
                    holder.transform.SetParent(root.transform, false);
                    holder.transform.localPosition = spots[i];
                    BuildVigilCandle(holder.transform);
                    holder.transform.localScale = Vector3.one * (heights[i] / 0.26f);
                }
                foreach (var collider in root.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(collider);
                var light = new GameObject("Candlelight").AddComponent<Light>();
                light.transform.SetParent(root.transform, false);
                light.transform.localPosition = new Vector3(0f, 0.45f, 0f);
                light.type = LightType.Point; light.color = new Color(1f, 0.6f, 0.3f); light.range = 5f; light.intensity = 1.6f;
                light.shadows = LightShadows.None;
                light.renderMode = LightRenderMode.ForcePixel;
                PrefabUtility.SaveAsPrefabAsset(root, GraveCandlesPath);
            } finally { Object.DestroyImmediate(root); }
        }

        /// <summary>
        /// The priest without a body in the world: the real monster's model, materials and animator controller on an
        /// object with no networking, physics or AI, which cutscenes and apparitions pose.
        /// </summary>
        [MenuItem("Ryan/Classic Horror/Rebuild Priest Actor")]
        public static void BuildPriestActor() {
            var monster = AssetDatabase.LoadAssetAtPath<GameObject>(MonsterPrefabPath);
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(PriestModelPath);
            if (monster == null) throw new System.InvalidOperationException($"No monster prefab at {MonsterPrefabPath}: run Ryan > Characters > Rebuild Monster and NPC Catalog.");
            if (model == null) throw new System.InvalidOperationException($"The Cursed Priest model is missing at {PriestModelPath}.");
            var monsterAnimator = monster.GetComponent<Animator>();
            var root = (GameObject)PrefabUtility.InstantiatePrefab(model);
            try {
                PrefabUtility.UnpackPrefabInstance(root, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                root.name = "PriestActor";
                // The monster's project-owned URP materials, by renderer name.
                var materials = new System.Collections.Generic.Dictionary<string, Material[]>();
                foreach (var renderer in monster.GetComponentsInChildren<Renderer>(true)) materials[renderer.name] = renderer.sharedMaterials;
                foreach (var renderer in root.GetComponentsInChildren<Renderer>(true)) {
                    if (materials.TryGetValue(renderer.name, out Material[] owned)) renderer.sharedMaterials = owned;
                    renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                    if (renderer is SkinnedMeshRenderer skin) skin.updateWhenOffscreen = true;
                }
                foreach (var collider in root.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(collider);
                var animator = root.GetComponent<Animator>();
                animator.runtimeAnimatorController = monsterAnimator.runtimeAnimatorController;
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                root.transform.localScale = monster.transform.localScale;
                var actor = root.AddComponent<CH_Puppet>();
                actor.animator = animator;
                actor.footstep = Sound(0.55f, 0.7f, 2f, 30f, AudioFolder + "wood-thud.ogg");
                // A cold glint on his face, so he is more than a silhouette against the water.
                actor.faceLight = FaceLight(root.transform, new Color(0.62f, 0.72f, 1f), 1.4f);
                PrefabUtility.SaveAsPrefabAsset(root, PriestActorPath);
            } finally { Object.DestroyImmediate(root); }
            AssetDatabase.SaveAssets();
        }

        /// <summary>
        /// Mara as a client-only puppet for the cutscenes: the investigators' skeleton and walk, with a skipper built over
        /// it (<see cref="DressAsSkipper"/>) in place of the robot's own mesh.
        /// </summary>
        [MenuItem("Ryan/Classic Horror/Rebuild Mara Actor")]
        public static void BuildMaraActor() {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(CrewModelPath);
            var controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(CrewControllerPath);
            if (model == null || controller == null) throw new System.InvalidOperationException($"The crew model or its controller is missing ({CrewModelPath}).");
            var root = (GameObject)PrefabUtility.InstantiatePrefab(model);
            try {
                PrefabUtility.UnpackPrefabInstance(root, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                root.name = "MaraActor";
                Material oilskin = Material("MaraOilskin", MaraOilskin);
                oilskin.SetTexture("_BaseMap", WeatheringTexture("MaraOilskinWear"));
                oilskin.SetFloat("_Smoothness", 0.22f);
                foreach (var collider in root.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(collider);
                var animator = root.GetComponent<Animator>() ?? root.AddComponent<Animator>();
                animator.runtimeAnimatorController = controller;
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                DressAsSkipper(root, animator, oilskin);
                root.transform.localScale = Vector3.one * MaraScale; // the crew robot is small; a skipper stands nearly as tall as the priest
                foreach (var renderer in root.GetComponentsInChildren<Renderer>(true)) renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                var actor = root.AddComponent<CH_Puppet>();
                actor.animator = animator;
                actor.footstep = Sound(0.3f, 1.25f, 1.5f, 18f, AudioFolder + "wood-thud.ogg");
                // Her head torch.
                actor.faceLight = FaceLight(root.transform, new Color(1f, 0.85f, 0.6f), 1f);
                PrefabUtility.SaveAsPrefabAsset(root, MaraActorPath);
            } finally { Object.DestroyImmediate(root); }
            AssetDatabase.SaveAssets();
        }

        /// <summary>
        /// Proportions of Mara's skipper's costume, as fractions of her height. Widths are diameters; "Down" values are
        /// fractions of the bone they run along.
        /// </summary>
        static class SkipperCut {
            // Head: an oval skull with a face on it, ear flaps and a sou'wester over it, a braid down her back.
            public static readonly Vector3 Head = new(0.086f, 0.118f, 0.104f);
            public const float HeadAboveBone = 0.052f, Neck = 0.056f;
            public const float EyeApart = 0.0165f, EyeSize = 0.0085f, EyeRaise = 0.008f;
            public static readonly Vector3 Nose = new(0.0105f, 0.016f, 0.01f), Mouth = new(0.021f, 0.0045f, 0.006f), Brow = new(0.022f, 0.004f, 0.006f);
            public static readonly Vector3 EarFlap = new(0.026f, 0.07f, 0.056f), HatCrown = new(0.1f, 0.068f, 0.112f), HatBrim = new(0.2f, 0.004f, 0.23f);
            public const float BrimTilt = -12f, BrimBack = 0.022f, BraidBead = 0.03f;
            // Coat: a lathed body from hips to a standing collar, and a flared skirt to below the knee.
            public const float CoatHip = 0.104f, CoatChest = 0.112f, CoatShoulder = 0.118f, CoatCollar = 0.05f, CollarHeight = 0.032f, CoatDepth = 0.68f;
            public const float SkirtBelowKnee = 0.07f, SkirtHem = 0.15f, SkirtDepth = 1.3f, ButtonSize = 0.012f;
            // Arms: a shoulder cap where the sleeve meets the coat, sleeves, a cuff over the wrist.
            public const float ShoulderCap = 0.08f, ElbowCap = 0.07f, UpperSleeve = 0.068f, LowerSleeve = 0.062f, Cuff = 0.07f, CuffLength = 0.03f;
            // Bare hands: a palm, four fingers and a thumb.
            public static readonly Vector3 Palm = new(0.05f, 0.05f, 0.022f);
            public const float FingerWidth = 0.0125f, FingerSpacing = 0.0115f, FingerStart = 0.05f, FingerLength = 0.05f;
            // Legs: overalls under the coat, rubber boots to mid-calf.
            public const float Thigh = 0.08f, Shin = 0.064f, Boot = 0.072f, BootFoot = 0.058f, BootTopDown = 0.45f;
        }

        /// <summary>Her height against the crew robot's, so she stands about nine tenths as tall as the priest.</summary>
        const float MaraScale = 1.3f;

        static readonly Color MaraHair = new(0.2f, 0.1f, 0.06f), MaraLips = new(0.36f, 0.16f, 0.14f), MaraEyes = new(0.06f, 0.04f, 0.035f);

        /// <summary>
        /// Dresses the crew body as a skipper and takes the robot's own mesh away, so nothing of it shows: a lathed
        /// oilskin coat with a standing collar, buttons and a skirt to below the knee, shoulder caps, sleeves, cuffs and
        /// bare hands, overalls and rubber boots, a head with a face, ear flaps and a sou'wester, and a braid. Each piece
        /// is parented to the bone it covers so it moves with her walk. Sizes are fractions of her height
        /// (<see cref="SkipperCut"/>), measured in the model's bind pose.
        /// </summary>
        static void DressAsSkipper(GameObject root, Animator animator, Material oilskin) {
            Transform Bone(HumanBodyBones bone) => animator.isHuman ? animator.GetBoneTransform(bone) : null;
            Transform hips = Bone(HumanBodyBones.Hips), head = Bone(HumanBodyBones.Head);
            Transform chest = Bone(HumanBodyBones.Chest) ?? Bone(HumanBodyBones.Spine) ?? hips;
            Transform neck = Bone(HumanBodyBones.Neck) ?? head, knee = Bone(HumanBodyBones.LeftLowerLeg);
            if (hips == null || head == null || knee == null) throw new System.InvalidOperationException("Mara's model is not a humanoid rig; she cannot be dressed.");
            float height = 0f;
            foreach (var body in root.GetComponentsInChildren<SkinnedMeshRenderer>(true)) {
                height = Mathf.Max(height, body.bounds.max.y - root.transform.position.y);
                Object.DestroyImmediate(body); // the robot itself: everything she shows is built below
            }
            if (height <= 0f) throw new System.InvalidOperationException("Mara's model has no body mesh to measure.");
            Vector3 up = Vector3.up, forward = root.transform.forward, right = root.transform.right;
            Material rubber = Material("MaraRubber", MaraRubber), skin = Material("MaraSkin", MaraSkin), eyes = Material("MaraEyes", MaraEyes);
            Material overalls = Material("MaraOveralls", MaraOveralls), hair = Material("MaraHair", MaraHair), lips = Material("MaraLips", MaraLips);
            skin.SetFloat("_Smoothness", 0.3f);
            oilskin.SetFloat("_Cull", 0f); // the hem and collar are open: show their inside too
            oilskin.doubleSidedGI = true;

            // A capsule from a to b, parented to the bone it covers.
            void Piece(string name, Transform bone, Vector3 a, Vector3 b, float width, Material material) {
                var piece = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                piece.name = name;
                Object.DestroyImmediate(piece.GetComponent<Collider>());
                piece.GetComponent<Renderer>().sharedMaterial = material;
                Vector3 along = b - a;
                piece.transform.SetPositionAndRotation((a + b) * 0.5f, Quaternion.FromToRotation(up, along.normalized) * Quaternion.LookRotation(forward, up));
                piece.transform.localScale = new Vector3(width * height, along.magnitude * 0.5f + width * height * 0.5f, width * height);
                piece.transform.SetParent(bone, true);
            }
            // An ellipsoid (or a flat cylinder) at a point, parented to a bone and turned to face the way she faces.
            GameObject Ball(string name, Transform bone, Vector3 at, Vector3 size, Material material, float tilt = 0f, PrimitiveType type = PrimitiveType.Sphere) {
                var ball = Shape(name, type, null, at, size * height, material);
                ball.transform.rotation = Quaternion.AngleAxis(tilt, right) * Quaternion.LookRotation(forward, up);
                ball.transform.SetParent(bone, true);
                return ball;
            }
            // A lathed piece of coat: elliptical rings (height above base, half width, half depth, all fractions of her height).
            void Lathe(string name, Transform bone, Vector3 basePoint, Vector3[] rings) {
                var piece = new GameObject(name);
                piece.transform.SetPositionAndRotation(basePoint, Quaternion.LookRotation(forward, up));
                piece.AddComponent<MeshFilter>().sharedMesh = LatheMesh("Mara" + name.Replace(" ", ""), rings, height);
                var renderer = piece.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = oilskin;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.TwoSided;
                piece.transform.SetParent(bone, true);
            }
            Vector3 Ring(float above, float halfWidth) => new(above, halfWidth, halfWidth * SkipperCut.CoatDepth);
            // Part-way from the coat's oval to the skirt's circle, so the two meet without a ledge.
            Vector3 Flare(float above, float halfWidth, float t) => new(above, halfWidth, halfWidth * Mathf.Lerp(SkipperCut.CoatDepth, SkipperCut.SkirtDepth, t));
            // The skirt is round, so a stride has room inside it instead of pushing a leg through the front.
            Vector3 Round(float above, float halfWidth) => new(above, halfWidth, halfWidth * SkipperCut.SkirtDepth);

            // The coat: from the hips, out over the chest and shoulders, in to a standing collar round her neck.
            float torso = (neck.position.y - hips.position.y) / height;
            Lathe("Coat", chest, hips.position, new[] {
                Ring(0f, SkipperCut.CoatHip), Ring(torso * 0.45f, SkipperCut.CoatHip * 1.03f), Ring(torso * 0.7f, SkipperCut.CoatChest),
                Ring(torso * 0.84f, SkipperCut.CoatShoulder), Ring(torso * 0.95f, SkipperCut.CoatShoulder * 0.8f), Ring(torso + 0.005f, SkipperCut.CoatCollar),
                Ring(torso + SkipperCut.CollarHeight, SkipperCut.CoatCollar * 1.1f),
            });
            for (int i = 0; i < 4; i++) {
                float above = torso * Mathf.Lerp(0.12f, 0.7f, i / 3f);
                float depth = Mathf.Lerp(SkipperCut.CoatHip, SkipperCut.CoatChest, above / (torso * 0.7f)) * SkipperCut.CoatDepth;
                Ball("Button", chest, hips.position + up * (above * height) + forward * (depth * height), Vector3.one * SkipperCut.ButtonSize, rubber);
            }
            float skirt = (hips.position.y - knee.position.y) / height + SkipperCut.SkirtBelowKnee;
            Lathe("Coat skirt", hips, hips.position + up * (0.03f * height), new[] {
                Ring(0f, SkipperCut.CoatHip * 1.02f), Flare(-skirt * 0.4f, (SkipperCut.CoatHip + SkipperCut.SkirtHem) * 0.5f * 1.04f, 0.5f), Round(-skirt, SkipperCut.SkirtHem),
            });

            void Arm(HumanBodyBones upper, HumanBodyBones lower, HumanBodyBones hand) {
                Transform shoulder = Bone(upper), elbow = Bone(lower), wrist = Bone(hand);
                if (shoulder == null || elbow == null || wrist == null) return;
                Vector3 towardHand = (wrist.position - elbow.position).normalized;
                Ball(upper + " shoulder", shoulder, shoulder.position, Vector3.one * SkipperCut.ShoulderCap, oilskin);
                Ball(lower + " elbow", elbow, elbow.position, Vector3.one * SkipperCut.ElbowCap, oilskin);
                Piece(upper + " sleeve", shoulder, shoulder.position, elbow.position, SkipperCut.UpperSleeve, oilskin);
                Piece(lower + " sleeve", elbow, elbow.position, wrist.position - towardHand * (SkipperCut.CuffLength * height), SkipperCut.LowerSleeve, oilskin);
                var cuff = Ball(lower + " cuff", elbow, wrist.position - towardHand * (SkipperCut.CuffLength * 0.5f * height),
                    new Vector3(SkipperCut.Cuff, SkipperCut.CuffLength * 0.5f, SkipperCut.Cuff), oilskin, 0f, PrimitiveType.Cylinder);
                cuff.transform.rotation = Quaternion.FromToRotation(up, towardHand) * Quaternion.LookRotation(forward, up);
                // A bare hand, so she has fingers to hold a tiller with: palm, four fingers (the middle two longest) and a thumb.
                var palm = Ball(hand + " palm", wrist, wrist.position + towardHand * (SkipperCut.Palm.y * 0.5f * height), SkipperCut.Palm, skin);
                palm.transform.rotation = Quaternion.FromToRotation(up, towardHand) * Quaternion.LookRotation(right * (upper == HumanBodyBones.LeftUpperArm ? -1f : 1f), up);
                Vector3 across = Vector3.Cross(towardHand, forward).normalized;
                for (int finger = 0; finger < 4; finger++) {
                    float length = SkipperCut.FingerLength * (finger == 1 || finger == 2 ? 1f : 0.86f);
                    Vector3 knuckle = wrist.position + towardHand * (SkipperCut.FingerStart * height) + across * ((finger - 1.5f) * SkipperCut.FingerSpacing * height);
                    Piece($"{hand} finger {finger + 1}", wrist, knuckle, knuckle + towardHand * (length * height), SkipperCut.FingerWidth, skin);
                }
                Piece(hand + " thumb", wrist, wrist.position + forward * (0.012f * height) + towardHand * (0.018f * height),
                    wrist.position + forward * (0.03f * height) + towardHand * (0.05f * height), 0.017f, skin);
            }
            Arm(HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand);
            Arm(HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand);

            void Leg(HumanBodyBones upper, HumanBodyBones lower, HumanBodyBones foot, HumanBodyBones toes) {
                Transform hip = Bone(upper), shin = Bone(lower), ankle = Bone(foot), toe = Bone(toes);
                if (hip == null || shin == null || ankle == null) return;
                Piece(upper + " trouser", hip, hip.position, shin.position, SkipperCut.Thigh, overalls);
                Vector3 bootTop = Vector3.Lerp(ankle.position, shin.position, SkipperCut.BootTopDown);
                Piece(lower + " trouser", shin, shin.position, bootTop, SkipperCut.Shin, overalls);
                Piece(lower + " boot", shin, bootTop, ankle.position, SkipperCut.Boot, rubber);
                Vector3 toeTip = toe != null ? toe.position + forward * (0.035f * height) : ankle.position + forward * (0.13f * height);
                Vector3 heel = ankle.position - forward * (0.02f * height);
                toeTip.y = heel.y = Mathf.Min(ankle.position.y, root.transform.position.y + SkipperCut.BootFoot * 0.5f * height);
                Piece(foot + " boot", ankle, heel, toeTip, SkipperCut.BootFoot, rubber);
            }
            Leg(HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot, HumanBodyBones.LeftToes);
            Leg(HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot, HumanBodyBones.RightToes);

            // Head: neck, an oval head with eyes, brows, a nose and a mouth, the sou'wester's ear flaps, crown and brim, a braid.
            Vector3 skull = head.position + up * (SkipperCut.HeadAboveBone * height);
            float faceFront = SkipperCut.Head.z * 0.5f;
            Piece("Neck", neck, neck.position - up * (0.02f * height), skull, SkipperCut.Neck, skin);
            Ball("Head", head, skull, SkipperCut.Head, skin);
            foreach (float side in new[] { -1f, 1f }) {
                Vector3 eye = skull + up * (SkipperCut.EyeRaise * height) + right * (side * SkipperCut.EyeApart * height) + forward * ((faceFront - 0.006f) * height);
                Ball("Eye", head, eye, Vector3.one * SkipperCut.EyeSize, eyes);
                Ball("Brow", head, eye + up * (0.014f * height) + forward * (0.003f * height), SkipperCut.Brow, hair, side * 8f);
                Ball("Ear flap", head, skull + right * (side * SkipperCut.Head.x * 0.5f * height) - up * (0.008f * height) - forward * (0.016f * height), SkipperCut.EarFlap, oilskin);
            }
            Ball("Nose", head, skull - up * (0.008f * height) + forward * ((faceFront - 0.002f) * height), SkipperCut.Nose, skin, -20f);
            Ball("Mouth", head, skull - up * (0.032f * height) + forward * ((faceFront - 0.008f) * height), SkipperCut.Mouth, lips);
            for (int i = 0; i < 4; i++)
                Ball("Braid", i == 0 ? head : neck, skull - forward * ((faceFront + 0.004f) * height) - up * ((0.03f + i * SkipperCut.BraidBead * 0.8f) * height),
                    Vector3.one * (SkipperCut.BraidBead * (1f - i * 0.12f)), hair);
            Ball("Sou'wester crown", head, skull + up * (0.04f * height) - forward * (0.006f * height), SkipperCut.HatCrown, oilskin);
            Ball("Sou'wester brim", head, skull + up * (0.022f * height) - forward * (SkipperCut.BrimBack * height), SkipperCut.HatBrim, oilskin,
                SkipperCut.BrimTilt, PrimitiveType.Cylinder);
        }

        /// <summary>
        /// A ring-by-ring lathed tube (no caps), saved as a mesh asset so the prefab keeps it. Each ring is (height above
        /// the base, half width, half depth) as fractions of <paramref name="scale"/>. The seam runs down the back and its
        /// normals are shared, so it shades as one smooth surface.
        /// </summary>
        static Mesh LatheMesh(string name, Vector3[] rings, float scale) {
            const int Segments = 28;
            string path = Root + "/Materials/" + name + ".asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (mesh == null) { mesh = new Mesh { name = name }; AssetDatabase.CreateAsset(mesh, path); }
            mesh.Clear();
            var vertices = new List<Vector3>();
            var uvs = new List<Vector2>();
            var triangles = new List<int>();
            for (int r = 0; r < rings.Length; r++)
                for (int s = 0; s <= Segments; s++) {
                    float angle = Mathf.PI + s * Mathf.PI * 2f / Segments; // starts at the back
                    vertices.Add(new Vector3(Mathf.Sin(angle) * rings[r].y, rings[r].x, Mathf.Cos(angle) * rings[r].z) * scale);
                    uvs.Add(new Vector2(s / (float)Segments, rings[r].x * 1.3f)); // about as many texels along the cloth as round it, so wear reads as mottling, not stripes
                }
            int stride = Segments + 1;
            for (int r = 0; r < rings.Length - 1; r++)
                for (int s = 0; s < Segments; s++) {
                    int a = r * stride + s, b = a + 1, c = a + stride, d = c + 1;
                    bool down = rings[r + 1].x < rings[r].x; // keep the outside facing out whichever way the rings run
                    if (down) triangles.AddRange(new[] { a, c, b, b, c, d });
                    else triangles.AddRange(new[] { a, b, c, b, d, c });
                }
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            var normals = mesh.normals;
            for (int r = 0; r < rings.Length; r++) {
                int first = r * stride, last = first + Segments;
                normals[first] = normals[last] = (normals[first] + normals[last]).normalized;
            }
            mesh.normals = normals;
            mesh.RecalculateBounds();
            EditorUtility.SetDirty(mesh);
            return mesh;
        }

        /// <summary>
        /// A tileable grey wear map (creases and rubbed patches) saved as a texture asset, so a flat-coloured material
        /// reads as worn cloth instead of plastic. Multiplied by the material's colour.
        /// </summary>
        static Texture2D WeatheringTexture(string name) {
            const int Size = 256;
            const float Darkest = 0.84f;
            const int WeatheringSeed = 1903;
            string path = Root + "/Materials/" + name + ".png";
            var texture = new Texture2D(Size, Size, TextureFormat.RGB24, false);
            var random = new System.Random(WeatheringSeed);
            float ox = random.Next(1000), oy = random.Next(1000);
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++) {
                    float u = x / (float)Size, v = y / (float)Size;
                    // Tileable: sample the noise on a torus.
                    float Noise(float frequency) {
                        float a = u * Mathf.PI * 2f, b = v * Mathf.PI * 2f;
                        return Mathf.PerlinNoise(ox + Mathf.Cos(a) * frequency, oy + Mathf.Sin(a) * frequency + Mathf.Cos(b) * frequency * 1.7f)
                            * 0.5f + Mathf.PerlinNoise(ox + Mathf.Sin(b) * frequency * 2.3f, oy + Mathf.Cos(a) * frequency) * 0.5f;
                    }
                    float creases = 1f - Mathf.Abs(Noise(5f) * 2f - 1f);
                    float wear = Mathf.Lerp(Darkest, 1f, Mathf.Clamp01(Noise(2f) * 0.5f + (1f - Mathf.Pow(creases, 10f)) * 0.6f));
                    texture.SetPixel(x, y, new Color(wear, wear, wear));
                }
            System.IO.File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path);
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        /// <summary>A short-range light in front of a puppet's face (switched with the puppet's visibility).</summary>
        static Light FaceLight(Transform puppet, Color color, float intensity) {
            var holder = new GameObject("Face Light").transform;
            holder.SetParent(puppet, false);
            // In the puppet's own space, so it scales with the model: just in front of and above the eyes.
            float scale = Mathf.Max(0.01f, puppet.localScale.y);
            holder.localPosition = new Vector3(0f, 1.75f / scale, 0.55f / scale);
            var light = holder.gameObject.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = color;
            light.intensity = intensity;
            light.range = 2.4f;
            light.shadows = LightShadows.None;
            light.enabled = false;
            return light;
        }
    }
}
