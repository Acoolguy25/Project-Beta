using System;
using System.Collections.Generic;
using System.Linq;
using FishNet.Component.Animating;
using FishNet.Component.Transforming;
using FishNet.Object;
using RyanAssets.Shared.Component;
using RyanAssets.Shared.Declarations;
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
    /// </summary>
    public static class WV_Authoring {
        public const string Root = "Assets/Universes/UniverseData/war_valley";
        const string PackRoot = "Assets/CartoonMilitaryModelPack/Prefebs";
        const string StructuresFolder = Root + "/Structures";
        const string UnitsFolder = Root + "/Units";
        const string PresentationFolder = Root + "/Presentation";

        const string RobotModelPath = "Assets/UnityTechnologies/SpaceRobotKyle/Models/KyleRobot.fbx";
        const string RobotControllerPath = "Assets/RyanAssets/Characters/CustomAnimations/RobotNPC.controller";
        const string CircleTexturePath = "Assets/GabrielAguiarProductions/FreeQuickEffectsVol1/Textures/Circle01_v1.png";

        static readonly int StructureLayer = LayerMask.NameToLayer("Structure");
        static readonly int CharacterLayer = LayerMask.NameToLayer("Character");

        // --- Definitions -----------------------------------------------------

        enum StructureRole { Plain, Income, Production, Turret, Wall }

        sealed class StructureDef {
            public string Id, DisplayName, Description, Category, ModelPath;
            public int FootprintCells = 1;
            public ulong Cost;
            public float BuildSeconds;
            public long MaxHealth;
            public StructureRole Role = StructureRole.Plain;
            public int IncomePerTick;
            public WV_UnitKind[] Produces = Array.Empty<WV_UnitKind>();
            public float TurretRange, TurretCooldown;
            public long TurretDamage;
            public bool AntiAir;
        }

        sealed class UnitDef {
            public WV_UnitKind Kind;
            public string DisplayName, ModelPath;
            public int Cost;
            public float BuildSeconds, MoveSpeed, TurnSpeed, AttackRange, DetectionRadius, AttackCooldown;
            public long MaxHealth, AttackDamage;
            /// <summary>Radius and height of the unit's own collider and, for ground units, its agent.</summary>
            public float Radius = 1.2f, Height = 2.4f;
            /// <summary>Widest horizontal span the model is scaled to, in world units.</summary>
            public float ModelSpan = 4f;
            public bool UsesRobotRig;
            public string[] RotorPaths = Array.Empty<string>();
        }

        static readonly StructureDef[] Structures = {
            new() {
                Id = "wv_mineshaft", DisplayName = "Mineshaft", Category = "Economy",
                Description = "Extracts ore. The backbone of any War Valley war chest.",
                ModelPath = PackRoot + "/Building_Prefebs/OilTank_Prefeb.prefab",
                FootprintCells = 2, Cost = 400, BuildSeconds = 20f, MaxHealth = 600,
                Role = StructureRole.Income, IncomePerTick = 25
            },
            new() {
                Id = "wv_refinery", DisplayName = "Refinery", Category = "Economy",
                Description = "Processes ore at scale. Expensive, slow to raise, and worth it.",
                ModelPath = PackRoot + "/Building_Prefebs/MilitaryBase_Prefeb.prefab",
                FootprintCells = 3, Cost = 1100, BuildSeconds = 40f, MaxHealth = 1200,
                Role = StructureRole.Income, IncomePerTick = 60
            },
            new() {
                Id = "wv_barracks", DisplayName = "Barracks", Category = "Military",
                Description = "Trains infantry squads.",
                ModelPath = PackRoot + "/Building_Prefebs/PersonLivePlace_Prefeb.prefab",
                FootprintCells = 2, Cost = 500, BuildSeconds = 25f, MaxHealth = 800,
                Role = StructureRole.Production, Produces = new[] { WV_UnitKind.Infantry }
            },
            new() {
                Id = "wv_vehicle_hangar", DisplayName = "Vehicle Hangar", Category = "Military",
                Description = "Builds tanks, APCs and artillery.",
                ModelPath = PackRoot + "/Building_Prefebs/VehicleHangar_01_Prefeb.prefab",
                FootprintCells = 3, Cost = 1000, BuildSeconds = 40f, MaxHealth = 1400,
                Role = StructureRole.Production,
                Produces = new[] { WV_UnitKind.APC, WV_UnitKind.Tank, WV_UnitKind.Artillery }
            },
            new() {
                Id = "wv_helipad", DisplayName = "Helipad", Category = "Air",
                Description = "Launches attack choppers.",
                ModelPath = PackRoot + "/Building_Prefebs/HeliPad_Prefeb.prefab",
                FootprintCells = 2, Cost = 800, BuildSeconds = 30f, MaxHealth = 700,
                Role = StructureRole.Production, Produces = new[] { WV_UnitKind.Chopper }
            },
            new() {
                Id = "wv_airfield", DisplayName = "Airfield", Category = "Air",
                Description = "Directs jets, bombers and reconnaissance drones.",
                ModelPath = PackRoot + "/Building_Prefebs/ControlTower_Prefeb.prefab",
                FootprintCells = 3, Cost = 1400, BuildSeconds = 50f, MaxHealth = 1100,
                Role = StructureRole.Production,
                Produces = new[] { WV_UnitKind.UAV, WV_UnitKind.Jet, WV_UnitKind.Bomber }
            },
            new() {
                Id = "wv_radar", DisplayName = "Radar Station", Category = "Support",
                Description = "Watches the valley approaches.",
                ModelPath = PackRoot + "/Building_Prefebs/Radar_Prefeb.prefab",
                FootprintCells = 2, Cost = 600, BuildSeconds = 25f, MaxHealth = 600
            },
            new() {
                Id = "wv_missile_battery", DisplayName = "Missile Battery", Category = "Defense",
                Description = "Surface-to-air battery. Engages aircraft only.",
                ModelPath = PackRoot + "/Building_Prefebs/MissilePort_Prefeb.prefab",
                FootprintCells = 2, Cost = 750, BuildSeconds = 30f, MaxHealth = 900,
                Role = StructureRole.Turret, TurretRange = 40f, TurretDamage = 55, TurretCooldown = 2f,
                AntiAir = true
            },
            new() {
                Id = "wv_guard_post", DisplayName = "Guard Post", Category = "Defense",
                Description = "Manned emplacement covering a ground approach.",
                ModelPath = PackRoot + "/Building_Prefebs/GuardPoint_01_Prefeb.prefab",
                FootprintCells = 1, Cost = 300, BuildSeconds = 12f, MaxHealth = 500,
                Role = StructureRole.Turret, TurretRange = 26f, TurretDamage = 28, TurretCooldown = 1.3f
            },
            new() {
                Id = "wv_watchtower", DisplayName = "Watchtower", Category = "Defense",
                Description = "Cheap picket that shoots back.",
                ModelPath = PackRoot + "/Building_Prefebs/LightTower_Prefeb.prefab",
                FootprintCells = 1, Cost = 200, BuildSeconds = 10f, MaxHealth = 400,
                Role = StructureRole.Turret, TurretRange = 20f, TurretDamage = 16, TurretCooldown = 1.1f
            },
            new() {
                Id = "wv_fence", DisplayName = "Perimeter Fence", Category = "Defense",
                Description = "Slows an advance. Attackers must breach it to pass.",
                ModelPath = PackRoot + "/Building_Prefebs/Fence_01_Prefeb.prefab",
                FootprintCells = 1, Cost = 60, BuildSeconds = 4f, MaxHealth = 300,
                Role = StructureRole.Wall
            },
            new() {
                Id = "wv_fence_corner", DisplayName = "Fence Corner", Category = "Defense",
                Description = "Turns a perimeter line through ninety degrees.",
                ModelPath = PackRoot + "/Building_Prefebs/Fence_01_Cornor_Prefeb.prefab",
                FootprintCells = 1, Cost = 60, BuildSeconds = 4f, MaxHealth = 300,
                Role = StructureRole.Wall
            }
        };

        static readonly UnitDef[] Units = {
            new() {
                Kind = WV_UnitKind.Infantry, DisplayName = "Infantry", UsesRobotRig = true,
                Cost = 120, BuildSeconds = 8f, MaxHealth = 150, MoveSpeed = 14f, TurnSpeed = 420f,
                AttackRange = 12f, DetectionRadius = 22f, AttackDamage = 18, AttackCooldown = 1.2f,
                Radius = 0.6f, Height = 2.2f, ModelSpan = 1.8f
            },
            new() {
                Kind = WV_UnitKind.APC, DisplayName = "APC",
                ModelPath = PackRoot + "/APC_Prefebs/APC_01_Prefeb.prefab",
                Cost = 300, BuildSeconds = 12f, MaxHealth = 450, MoveSpeed = 16f, TurnSpeed = 200f,
                AttackRange = 16f, DetectionRadius = 26f, AttackDamage = 28, AttackCooldown = 1f,
                Radius = 1.6f, Height = 2.6f, ModelSpan = 5f
            },
            new() {
                Kind = WV_UnitKind.Tank, DisplayName = "Tank",
                ModelPath = PackRoot + "/Tank_Prefebs/Tank_01_Prefeb.prefab",
                Cost = 450, BuildSeconds = 18f, MaxHealth = 700, MoveSpeed = 11f, TurnSpeed = 140f,
                AttackRange = 22f, DetectionRadius = 30f, AttackDamage = 60, AttackCooldown = 2.2f,
                Radius = 1.8f, Height = 2.6f, ModelSpan = 6f
            },
            new() {
                Kind = WV_UnitKind.Artillery, DisplayName = "Artillery",
                ModelPath = PackRoot + "/SelfPropelledArtillery_Prefebs/SelfPropelledArtillery_01_Prefeb.prefab",
                Cost = 600, BuildSeconds = 24f, MaxHealth = 400, MoveSpeed = 8f, TurnSpeed = 110f,
                AttackRange = 45f, DetectionRadius = 50f, AttackDamage = 110, AttackCooldown = 4f,
                Radius = 1.8f, Height = 2.8f, ModelSpan = 6.5f
            },
            new() {
                Kind = WV_UnitKind.Chopper, DisplayName = "Chopper",
                ModelPath = PackRoot + "/Chopper_Prefebs/Chopper_01_Prefeb.prefab",
                Cost = 550, BuildSeconds = 20f, MaxHealth = 380, MoveSpeed = 22f, TurnSpeed = 150f,
                AttackRange = 24f, DetectionRadius = 32f, AttackDamage = 45, AttackCooldown = 1.4f,
                Radius = 2f, Height = 3f, ModelSpan = 7f
            },
            new() {
                Kind = WV_UnitKind.UAV, DisplayName = "UAV",
                ModelPath = PackRoot + "/UAV_Prefebs/UAV_01_Prefeb.prefab",
                Cost = 350, BuildSeconds = 14f, MaxHealth = 180, MoveSpeed = 26f, TurnSpeed = 170f,
                AttackRange = 18f, DetectionRadius = 45f, AttackDamage = 22, AttackCooldown = 1.6f,
                Radius = 1.5f, Height = 2f, ModelSpan = 5f
            },
            new() {
                Kind = WV_UnitKind.Jet, DisplayName = "Jet",
                ModelPath = PackRoot + "/Jet_Prefebs/Jet_01_Prefeb.prefab",
                Cost = 800, BuildSeconds = 26f, MaxHealth = 320, MoveSpeed = 46f, TurnSpeed = 110f,
                AttackRange = 28f, DetectionRadius = 40f, AttackDamage = 70, AttackCooldown = 2f,
                Radius = 2f, Height = 2.4f, ModelSpan = 8f
            },
            new() {
                Kind = WV_UnitKind.Bomber, DisplayName = "Bomber",
                ModelPath = PackRoot + "/Bomber_Prefebs/Bomber_01_Prefeb.prefab",
                Cost = 950, BuildSeconds = 32f, MaxHealth = 500, MoveSpeed = 30f, TurnSpeed = 80f,
                AttackRange = 20f, DetectionRadius = 34f, AttackDamage = 140, AttackCooldown = 5f,
                Radius = 2.6f, Height = 3f, ModelSpan = 10f
            }
        };

        // --- Entry points ----------------------------------------------------

        [MenuItem("Ryan/War Valley/Rebuild All Prefabs")]
        public static void RebuildAll() {
            EnsureFolders();
            BuildScaffold();
            BuildSiteBox();
            BuildSelectionIndicator();
            BuildUnits();
            BuildStructures();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"War Valley: rebuilt {Structures.Length} structures and {Units.Length} units.");
        }

        [MenuItem("Ryan/War Valley/Rebuild Structure Prefabs")]
        public static void RebuildStructures() {
            EnsureFolders();
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
            BuildUnits();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
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

        // --- Structures ------------------------------------------------------

        static void BuildStructures() {
            GameObject scaffoldPrefab = Load<GameObject>(PresentationFolder + "/WV_ConstructionScaffold.prefab");
            GameObject siteBoxPrefab = Load<GameObject>(PresentationFolder + "/WV_ConstructionSiteBox.prefab");
            Dictionary<WV_UnitKind, WV_Unit> unitPrefabs = LoadUnitPrefabs();

            foreach (StructureDef def in Structures)
                BuildStructure(def, scaffoldPrefab, siteBoxPrefab, unitPrefabs);
        }

        static void BuildStructure(
            StructureDef def,
            GameObject scaffoldPrefab,
            GameObject siteBoxPrefab,
            Dictionary<WV_UnitKind, WV_Unit> unitPrefabs) {
            // Baked before the prefab root exists, because the baker stages the model in its own
            // scene and there is no reason to have a half-built structure alive while it does.
            Sprite icon = WV_IconBaker.Bake(Load<GameObject>(def.ModelPath), PrefabName(def.DisplayName));

            var root = new GameObject(def.DisplayName);
            try {
                root.layer = StructureLayer;
                root.tag = "Structure";

                float span = def.FootprintCells * WV_Rules.GridSize;

                // Model sits under its own root so construction can scale it up out of the ground
                // without also scaling the collider, the scaffold, or the turret's aim transforms.
                var buildingRoot = new GameObject("Building");
                buildingRoot.transform.SetParent(root.transform, false);
                SetLayerRecursive(buildingRoot, StructureLayer);

                GameObject model = PlaceFitted(
                    Load<GameObject>(def.ModelPath), buildingRoot.transform, Vector3.zero, 0f, span, "Model");
                SetLayerRecursive(model, StructureLayer);
                // The pack models ship without colliders; the structure's own box is the collision.
                foreach (Collider collider in model.GetComponentsInChildren<Collider>(true))
                    UnityEngine.Object.DestroyImmediate(collider);

                float modelHeight = Mathf.Max(1f, WorldBounds(model).size.y);

                // Hoarding and site box share one parent so WV_Constructable can hide the whole site
                // with a single reference the moment the build finishes.
                var site = new GameObject("ConstructionSite");
                site.transform.SetParent(root.transform, false);

                GameObject scaffold = (GameObject)PrefabUtility.InstantiatePrefab(scaffoldPrefab, site.transform);
                scaffold.name = "ConstructionScaffold";
                scaffold.transform.localScale = new Vector3(def.FootprintCells, 1f, def.FootprintCells);

                GameObject siteBox = (GameObject)PrefabUtility.InstantiatePrefab(siteBoxPrefab, site.transform);
                siteBox.name = "ConstructionSiteBox";
                siteBox.transform.localScale =
                    new Vector3(span, WV_Rules.GetScaffoldHeight(def.FootprintCells), span);

                SetLayerRecursive(site, StructureLayer);

                BoxCollider box = root.AddComponent<BoxCollider>();
                box.size = new Vector3(span * 0.9f, modelHeight, span * 0.9f);
                box.center = new Vector3(0f, modelHeight * 0.5f, 0f);

                root.AddComponent<NetworkObject>();
                var effects = Ensure<EffectsComponent>(root);
                var health = Ensure<HealthComponent>(root);

                var structure = root.AddComponent<StructureComponent>();
                structure.StructureID = def.Id;
                structure.DisplayName = def.DisplayName;
                structure.Description = def.Description;
                structure.Category = def.Category;
                structure.Cost = def.Cost;
                structure.Sprite = icon;
                // Duration is the shared per-structure build time WV_Constructable reads.
                structure.Duration = def.BuildSeconds;
                SetPrivateField(structure, "team", new TeamConfig(TeamColor.Blue));
                SetPrivateField(structure, "effectsComponent", effects);
                SetPrivateField(structure, "healthComponent", health);

                Ensure<WV_Owned>(root);

                // Only the building's own renderers are tinted. The hoarding and the site box keep
                // their own colours, so a site under construction still reads as a site.
                var ownerColor = Ensure<WV_OwnerColor>(root);
                SetPrivateField(
                    ownerColor, "tintedRenderers", buildingRoot.GetComponentsInChildren<Renderer>(true));

                var constructable = root.AddComponent<WV_Constructable>();
                SetPrivateField(constructable, "footprintCells", def.FootprintCells);
                SetPrivateField(constructable, "maxHealth", def.MaxHealth);
                SetPrivateField(constructable, "buildingRoot", buildingRoot.transform);
                SetPrivateField(constructable, "constructionScaffold", site);

                NavMeshObstacle obstacle = Ensure<NavMeshObstacle>(root);
                obstacle.shape = NavMeshObstacleShape.Box;
                obstacle.size = box.size;
                obstacle.center = box.center;
                obstacle.carving = true;
                obstacle.carveOnlyStationary = true;

                ApplyRole(def, root, span, modelHeight, unitPrefabs);

                SavePrefab(root, $"{StructuresFolder}/{PrefabName(def.DisplayName)}.prefab");
            } finally {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        static void ApplyRole(
            StructureDef def,
            GameObject root,
            float span,
            float modelHeight,
            Dictionary<WV_UnitKind, WV_Unit> unitPrefabs) {
            switch (def.Role) {
                case StructureRole.Income: {
                    var income = root.AddComponent<WV_IncomeBuilding>();
                    SetPrivateField(income, "incomePerTick", def.IncomePerTick);
                    break;
                }
                case StructureRole.Production: {
                    // Units appear just outside the footprint so they never spawn inside the collider.
                    var spawnPoint = new GameObject("UnitSpawnPoint");
                    spawnPoint.transform.SetParent(root.transform, false);
                    spawnPoint.transform.localPosition = new Vector3(0f, 0f, span * 0.5f + 3f);

                    var production = root.AddComponent<WV_ProductionBuilding>();
                    WV_Unit[] producible = def.Produces
                        .Select(kind => unitPrefabs.TryGetValue(kind, out WV_Unit prefab) ? prefab : null)
                        .Where(prefab => prefab != null)
                        .ToArray();
                    if (producible.Length != def.Produces.Length) {
                        Debug.LogError(
                            $"War Valley: {def.DisplayName} is missing unit prefabs for " +
                            $"{string.Join(", ", def.Produces.Where(k => !unitPrefabs.ContainsKey(k)))}. " +
                            "Rebuild unit prefabs first.");
                    }
                    SetPrivateField(production, "producibleUnits", producible);
                    SetPrivateField(production, "spawnPoint", spawnPoint.transform);
                    break;
                }
                case StructureRole.Turret: {
                    // Aim transforms sit outside the building root so construction's vertical scaling
                    // does not distort the barrel while the site is still going up.
                    var yaw = new GameObject("TurretYaw");
                    yaw.transform.SetParent(root.transform, false);
                    yaw.transform.localPosition = new Vector3(0f, modelHeight * 0.75f, 0f);

                    var turret = root.AddComponent<WV_DefenseTurret>();
                    SetPrivateField(turret, "range", def.TurretRange);
                    SetPrivateField(turret, "damage", def.TurretDamage);
                    SetPrivateField(turret, "cooldown", def.TurretCooldown);
                    SetPrivateField(turret, "antiAir", def.AntiAir);
                    SetPrivateField(turret, "turretYaw", yaw.transform);
                    break;
                }
                case StructureRole.Wall: {
                    // Reuses the existing breach-link obstacle so a walled-off valley stays solvable:
                    // attackers stop at the link, destroy the section, and walk through the gap.
                    Ensure<NavMeshLink>(root);
                    Ensure<WV_DestructibleObstacle>(root);
                    break;
                }
            }
        }

        // --- Units -----------------------------------------------------------

        static Dictionary<WV_UnitKind, WV_Unit> LoadUnitPrefabs() {
            var result = new Dictionary<WV_UnitKind, WV_Unit>();
            foreach (UnitDef def in Units) {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(UnitPath(def));
                if (prefab != null && prefab.TryGetComponent(out WV_Unit unit))
                    result[def.Kind] = unit;
            }
            return result;
        }

        static string UnitPath(UnitDef def) => $"{UnitsFolder}/{def.Kind}.prefab";

        /// <summary>Asset-safe name shared by a structure's prefab and its baked icon.</summary>
        static string PrefabName(string displayName) => displayName.Replace(" ", string.Empty);

        static void BuildUnits() {
            GameObject selectionRing = Load<GameObject>(PresentationFolder + "/WV_SelectionRing.prefab");
            foreach (UnitDef def in Units)
                BuildUnit(def, selectionRing);
        }

        static void BuildUnit(UnitDef def, GameObject selectionRing) {
            // Infantry has no vehicle model of its own, so its icon comes from the rig it is built
            // from; everything else bakes from the pack model the prefab nests.
            Sprite icon = WV_IconBaker.Bake(
                Load<GameObject>(def.UsesRobotRig ? RobotModelPath : def.ModelPath), def.Kind.ToString());

            var root = new GameObject(def.DisplayName);
            try {
                root.layer = CharacterLayer;
                // Tagged so the runner's existing between-round NPC sweep clears leftover armies.
                root.tag = "NPC";

                GameObject model = def.UsesRobotRig
                    ? BuildRobotBody(root.transform, def)
                    : PlaceFitted(
                        Load<GameObject>(def.ModelPath), root.transform, Vector3.zero, 0f, def.ModelSpan, "Model");
                SetLayerRecursive(model, CharacterLayer);
                foreach (Collider collider in model.GetComponentsInChildren<Collider>(true))
                    UnityEngine.Object.DestroyImmediate(collider);

                var capsule = root.AddComponent<CapsuleCollider>();
                capsule.radius = def.Radius;
                capsule.height = def.Height;
                capsule.center = new Vector3(0f, def.Height * 0.5f, 0f);

                GameObject ring = (GameObject)PrefabUtility.InstantiatePrefab(selectionRing, root.transform);
                ring.name = "SelectionIndicator";
                float ringScale = Mathf.Max(def.Radius * 2.6f, def.ModelSpan);
                ring.transform.localScale = new Vector3(ringScale, ringScale, ringScale);
                ring.SetActive(false);

                var muzzle = new GameObject("Muzzle");
                muzzle.transform.SetParent(root.transform, false);
                muzzle.transform.localPosition = new Vector3(0f, def.Height * 0.6f, def.Radius);

                root.AddComponent<NetworkObject>();
                root.AddComponent<NetworkTransform>();
                var effects = Ensure<EffectsComponent>(root);
                var health = Ensure<HealthComponent>(root);

                var unit = root.AddComponent<WV_Unit>();
                SetPrivateField(unit, "kind", (int)def.Kind);
                SetPrivateField(unit, "displayName", def.DisplayName);
                SetPrivateField(unit, "team", new TeamConfig(TeamColor.Blue));
                SetPrivateField(unit, "cost", def.Cost);
                SetPrivateField(unit, "buildSeconds", def.BuildSeconds);
                SetPrivateField(unit, "maxHealth", def.MaxHealth);
                SetPrivateField(unit, "moveSpeed", def.MoveSpeed);
                SetPrivateField(unit, "turnSpeed", def.TurnSpeed);
                SetPrivateField(unit, "attackRange", def.AttackRange);
                SetPrivateField(unit, "detectionRadius", def.DetectionRadius);
                SetPrivateField(unit, "attackDamage", def.AttackDamage);
                SetPrivateField(unit, "attackCooldown", def.AttackCooldown);
                SetPrivateField(unit, "attackDamageType", (int)DamageType.Gun);
                SetPrivateField(unit, "icon", icon);
                SetPrivateField(unit, "weaponMuzzle", muzzle.transform);
                SetPrivateField(unit, "selectionIndicator", ring);
                SetPrivateField(unit, "effectsComponent", effects);
                SetPrivateField(unit, "healthComponent", health);

                Ensure<WV_Owned>(root);

                var animation = root.AddComponent<WV_UnitAnimation>();
                if (def.UsesRobotRig) {
                    SetPrivateField(animation, "animator", root.GetComponentInChildren<Animator>());
                    root.AddComponent<NetworkAnimator>();
                }
                Transform[] rotors = FindRotors(model, def);
                if (rotors.Length > 0)
                    SetPrivateField(animation, "rotors", rotors);

                if (!WV_Rules.IsAircraft(def.Kind)) {
                    NavMeshAgent agent = Ensure<NavMeshAgent>(root);
                    agent.agentTypeID = WV_Rules.NavMeshAgentTypeId;
                    agent.radius = def.Radius;
                    agent.height = def.Height;
                    agent.speed = def.MoveSpeed;
                    agent.angularSpeed = def.TurnSpeed;
                    agent.acceleration = def.MoveSpeed * 2f;
                    agent.stoppingDistance = def.Radius;
                }

                SavePrefab(root, UnitPath(def));
            } finally {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        /// <summary>
        /// Infantry use the project's existing Space Robot Kyle rig and its shared animator controller,
        /// because the military pack ships vehicles and buildings but no foot soldier.
        /// </summary>
        static GameObject BuildRobotBody(Transform parent, UnitDef def) {
            GameObject body = PlaceFitted(
                Load<GameObject>(RobotModelPath), parent, Vector3.zero, 0f, def.ModelSpan, "Body");
            Animator animator = Ensure<Animator>(body);
            animator.runtimeAnimatorController =
                AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(RobotControllerPath);
            animator.applyRootMotion = false;
            return body;
        }

        /// <summary>Finds spinning parts by name so rotorcraft read as running rather than gliding.</summary>
        static Transform[] FindRotors(GameObject model, UnitDef def) {
            if (def.Kind != WV_UnitKind.Chopper && def.Kind != WV_UnitKind.UAV)
                return Array.Empty<Transform>();

            return model.GetComponentsInChildren<Transform>(true)
                .Where(t => t.name.IndexOf("rotor", StringComparison.OrdinalIgnoreCase) >= 0
                            || t.name.IndexOf("propeller", StringComparison.OrdinalIgnoreCase) >= 0
                            || t.name.IndexOf("blade", StringComparison.OrdinalIgnoreCase) >= 0)
                .ToArray();
        }

        // --- Helpers ---------------------------------------------------------

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
                case string s: property.stringValue = s; break;
                case UnityEngine.Object o: property.objectReferenceValue = o; break;
                case TeamConfig team:
                    property.FindPropertyRelative("realTeam").intValue = (int)team.realTeam;
                    property.FindPropertyRelative("displayTeam").intValue = (int)team.displayTeam;
                    break;
                case Array array:
                    property.arraySize = array.Length;
                    for (int index = 0; index < array.Length; index++) {
                        property.GetArrayElementAtIndex(index).objectReferenceValue =
                            (UnityEngine.Object)array.GetValue(index);
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
        static GameObject SavePrefab(GameObject root, string path) =>
            PrefabUtility.SaveAsPrefabAsset(root, path);
    }
}
