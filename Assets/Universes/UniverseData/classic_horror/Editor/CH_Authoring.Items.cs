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

        const string KayKitDungeon = "Assets/ThirdParty/KayKit/DungeonRemastered/Models/";
        const string KayKitFurniture = "Assets/ThirdParty/KayKit/FurnitureBits/Models/";
        const string MonsterPrefabPath = "Packages/com.ryan.gameframework/Runtime/Characters/Monster/Prefabs/Monster.prefab";
        const string PriestModelPath = Root + "/Cursed Priest/Prefab/Priest.prefab";
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
                var actor = root.AddComponent<CH_PriestActor>();
                actor.animator = animator;
                actor.footstep = Sound(0.55f, 0.7f, 2f, 30f, AudioFolder + "wood-thud.ogg");
                PrefabUtility.SaveAsPrefabAsset(root, PriestActorPath);
            } finally { Object.DestroyImmediate(root); }
            AssetDatabase.SaveAssets();
        }
    }
}
