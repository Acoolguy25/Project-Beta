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
    public static class WV_Authoring {
        public const string Root = "Assets/Universes/UniverseData/war_valley";

        /// <summary>
        /// Stamp written into each generated prefab's import settings. Change it whenever the
        /// prefabs this script builds change, and every copy older than it is rebuilt on next load.
        /// </summary>
        const string AuthoringVersion = "war-valley-authoring-2";
        const string AutoRebuildSessionKey = "WV_Authoring.AutoRebuildAttempted";
        const string PackRoot = "Assets/CartoonMilitaryModelPack/Prefebs";
        const string StructuresFolder = Root + "/Structures";
        const string UnitsFolder = Root + "/Units";
        const string PresentationFolder = Root + "/Presentation";

        const string RobotModelPath = "Assets/UnityTechnologies/SpaceRobotKyle/Models/KyleRobot.fbx";
        const string RobotControllerPath = "Assets/RyanAssets/Characters/CustomAnimations/RobotNPC.controller";
        const string CircleTexturePath = "Assets/GabrielAguiarProductions/FreeQuickEffectsVol1/Textures/Circle01_v1.png";

        // Shared presentation every structure reuses rather than growing a War Valley copy of.
        const string WorldTimerBarPath = "Assets/RyanAssets/Shared/WorldUI/WorldTimerBar.prefab";
        const string EffectsRoot = "Assets/GabrielAguiarProductions/FreeQuickEffectsVol1/Prefabs";
        const string ExplosionVfxPath = EffectsRoot + "/vfx_Explosion_01.prefab";
        const string SmokeVfxPath = EffectsRoot + "/vfx_Smoke_01.prefab";
        const string MuzzleFlashVfxPath = EffectsRoot + "/vfx_MuzzleFlash_01.prefab";
        const string GunShotAudioPath = "Assets/RyanAssets/Tools/Audio/pistol-gun-shot.mp3";
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

        // --- Definitions -----------------------------------------------------

        enum StructureRole { Plain, Income, Production, Turret, Wall, Research, Gate, ShieldGenerator }

        /// <summary>How a structure's model is laid out on the build grid.</summary>
        enum StructureShape {
            /// <summary>A building: its widest side fills the footprint and it stands in a square box.</summary>
            Block,
            /// <summary>
            /// A wall run: as long as the footprint along X, as thin as its art, as tall as the art
            /// is at that length. An even run lies on a grid line, so runs turned through ninety
            /// degrees meet exactly at a grid point and close a perimeter without a gap.
            /// </summary>
            Run,
            /// <summary>
            /// A post as tall as a matching run, standing on the grid point the footprint centres it
            /// on, to cap the joint where runs meet.
            /// </summary>
            Post
        }

        sealed class StructureDef {
            public string Id, DisplayName, Description, Category, ModelPath;
            /// <summary>Prefab file name when it is not the display name, to rebuild a legacy asset in place.</summary>
            public string AssetName;
            public StructureShape Shape = StructureShape.Block;
            /// <summary>For a post: the run it is sized to stand as tall as.</summary>
            public string MatchHeightOf;
            public int FootprintCells = 1;
            public ulong Cost;
            public float BuildSeconds;
            public long MaxHealth;
            public StructureRole Role = StructureRole.Plain;
            public int IncomePerTick;
            public WV_UnitKind[] Produces = Array.Empty<WV_UnitKind>();
            /// <summary>Foot soldiers this structure trains. Priced from WV_Rules, not from a prefab.</summary>
            public WV_TroopKind[] Trains = Array.Empty<WV_TroopKind>();
            public float TurretRange, TurretCooldown;
            public long TurretDamage;
            public bool AntiAir;
            /// <summary>Research throughput a research station adds to its side.</summary>
            public float ResearchRate = 1f;
            /// <summary>Turn applied to the model before it is fitted, for art that runs along the wrong axis.</summary>
            public float ModelYaw;
            public float ShieldRadius = 18f;
            public long ShieldHealth = 1500;
            public float ShieldRegenerationSeconds = 30f;
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
                // Foot soldiers only: the Infantry vehicle duplicated the Knifeman and Gunner the
                // barracks already trains, so it is no longer offered here.
                Id = "wv_barracks", DisplayName = "Barracks", Category = "Military",
                Description = "Trains Knifemen, Gunners, Shrimp, Speedies, Skinny Legends and Punks. Click it to train.",
                ModelPath = PackRoot + "/Building_Prefebs/PersonLivePlace_Prefeb.prefab",
                FootprintCells = 2, Cost = 500, BuildSeconds = 25f, MaxHealth = 800,
                Role = StructureRole.Production,
                Trains = new[] {
                    WV_TroopKind.Knife, WV_TroopKind.Gunner, WV_TroopKind.Shrimp,
                    WV_TroopKind.Speedy, WV_TroopKind.SkinnyLegend, WV_TroopKind.Punk
                }
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
                // Keeps the radar's id: the structure is the same asset, given a job. It is the
                // research station every research project needs, and each extra one speeds research.
                Id = "wv_radar", DisplayName = "Research Station", Category = "Support",
                Description = "Runs research for your side. Every extra station adds research speed.",
                ModelPath = PackRoot + "/Building_Prefebs/Radar_Prefeb.prefab",
                FootprintCells = 2, Cost = 600, BuildSeconds = 25f, MaxHealth = 600,
                Role = StructureRole.Research, ResearchRate = 1f
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
                // A two-cell run: long and thin, and taller than a soldier. The pack's fence section
                // runs along its own Z axis, so it is turned to lie along X like every wall here. Turn
                // it with R to run the other way; runs meet at grid points, so they close a perimeter.
                Id = "wv_fence", DisplayName = "Perimeter Fence", Category = "Defense",
                Description = "An 8m fence run. Slows an advance: attackers must breach it to pass. " +
                              "Press R to turn it; runs join end to end and at corners.",
                ModelPath = PackRoot + "/Building_Prefebs/Fence_01_Prefeb.prefab",
                FootprintCells = 2, Cost = 60, BuildSeconds = 4f, MaxHealth = 300,
                Role = StructureRole.Wall, Shape = StructureShape.Run, ModelYaw = 90f
            },
            new() {
                // Declares a two-cell footprint so it snaps to a grid point - the corner two fence
                // runs meet at - rather than to the middle of a cell, which is off every run.
                Id = "wv_fence_corner", DisplayName = "Fence Corner", Category = "Defense",
                Description = "A fence post for the joint where two fence runs meet at a corner.",
                ModelPath = PackRoot + "/Building_Prefebs/Fence_01_Cornor_Prefeb.prefab",
                FootprintCells = 2, Cost = 60, BuildSeconds = 4f, MaxHealth = 300,
                Role = StructureRole.Wall, Shape = StructureShape.Post, MatchHeightOf = "wv_fence"
            },
            new() {
                // Rebuilds the old debug wall - a plain cube between two cylinders, with no owner,
                // so it could be neither selected nor demolished - as a real War Valley wall in its
                // place, from the pack's heavier fence. Same asset, so existing references keep it.
                Id = "basic_wall", DisplayName = "Basic Wall", Category = "Defense", AssetName = "BasicWall",
                Description = "An 8m heavy wall run. Tougher than a fence; attackers must break it down to pass.",
                ModelPath = PackRoot + "/Building_Prefebs/Fence_02_Prefeb.prefab",
                FootprintCells = 2, Cost = 100, BuildSeconds = 6f, MaxHealth = 600,
                Role = StructureRole.Wall, Shape = StructureShape.Run, ModelYaw = 90f
            },
            new() {
                // The pack has no gate, so one is assembled from its heavier fence: a Fence_02
                // section is the door that sinks into the ground, between two of its corner posts.
                // A two-cell run like the walls it is set into, wide enough for a tank to pass.
                Id = WV_Rules.GateId, DisplayName = "Gate", Category = "Defense",
                Description = "An 8m wall section that opens for your side. Allies walk up and it opens; " +
                              "your troops and vehicles use it when their route needs it. Its owner can " +
                              "hold it open or lock it. Enemies must break it down.",
                ModelPath = PackRoot + "/Building_Prefebs/Fence_02_Prefeb.prefab",
                FootprintCells = 2, Cost = 150, BuildSeconds = 8f, MaxHealth = 500,
                Role = StructureRole.Gate, Shape = StructureShape.Run, ModelYaw = 90f
            },
            new() {
                Id = WV_Rules.ShieldGeneratorId, DisplayName = "Shield Generator", Category = "Defense",
                Description = "Raises a circular shield. Allies pass through; enemies cannot enter or hurt " +
                              "anything inside until they break it, and it recharges 30s later while the " +
                              "generator stands. Each new generator must overlap one of your shields.",
                ModelPath = PackRoot + "/Building_Prefebs/SpeakerTower_Prefeb.prefab",
                FootprintCells = 2, Cost = 1200, BuildSeconds = 35f, MaxHealth = 900,
                Role = StructureRole.ShieldGenerator,
                ShieldRadius = 18f, ShieldHealth = 1500, ShieldRegenerationSeconds = 30f
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

        // --- Structures ------------------------------------------------------

        static void BuildStructures() {
            GameObject scaffoldPrefab = Load<GameObject>(PresentationFolder + "/WV_ConstructionScaffold.prefab");
            GameObject siteBoxPrefab = Load<GameObject>(PresentationFolder + "/WV_ConstructionSiteBox.prefab");
            Dictionary<WV_UnitKind, WV_Unit> unitPrefabs = LoadUnitPrefabs();

            foreach (StructureDef def in Structures)
                BuildStructure(def, scaffoldPrefab, siteBoxPrefab, unitPrefabs);
        }

        static string StructurePath(StructureDef def) =>
            $"{StructuresFolder}/{def.AssetName ?? PrefabName(def.DisplayName)}.prefab";

        static StructureDef FindStructure(string id) => Structures.First(def => def.Id == id);

        /// <summary>
        /// Height a run's model stands at once fitted to its run, measured on a throwaway copy, so a
        /// post can be sized to match it exactly.
        /// </summary>
        static float MeasureRunHeight(StructureDef run) {
            var probe = new GameObject("HeightProbe");
            try {
                GameObject model = PlaceFitted(
                    Load<GameObject>(run.ModelPath), probe.transform, Vector3.zero, run.ModelYaw,
                    run.FootprintCells * WV_Rules.GridSize, "Model");
                return WorldBounds(model).size.y;
            } finally {
                UnityEngine.Object.DestroyImmediate(probe);
            }
        }

        static void BuildStructure(
            StructureDef def,
            GameObject scaffoldPrefab,
            GameObject siteBoxPrefab,
            Dictionary<WV_UnitKind, WV_Unit> unitPrefabs) {
            // Baked before the prefab root exists, because the baker stages the model in its own
            // scene and there is no reason to have a half-built structure alive while it does.
            Sprite icon = WV_IconBaker.Bake(Load<GameObject>(def.ModelPath), def.AssetName ?? PrefabName(def.DisplayName));

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

                // A post stands as tall as the run it caps; everything else is fitted to its footprint.
                GameObject model = def.Shape == StructureShape.Post
                    ? PlaceFittedToHeight(
                        Load<GameObject>(def.ModelPath), buildingRoot.transform, def.ModelYaw,
                        MeasureRunHeight(FindStructure(def.MatchHeightOf)), "Model")
                    : PlaceFitted(
                        Load<GameObject>(def.ModelPath), buildingRoot.transform, Vector3.zero, def.ModelYaw, span, "Model");
                SetLayerRecursive(model, StructureLayer);
                // The pack models ship without colliders; the structure's own box is the collision.
                foreach (Collider collider in model.GetComponentsInChildren<Collider>(true))
                    UnityEngine.Object.DestroyImmediate(collider);

                Bounds modelBounds = WorldBounds(model);
                float modelHeight = Mathf.Max(1f, modelBounds.size.y);
                // A wall or post is as thin as its art, not a square block: its box, its carve in the
                // NavMesh, and its construction site all follow the model. The depth is floored so a
                // wire-thin fence still stops a body and a bullet.
                bool thin = def.Shape != StructureShape.Block;
                Vector3 footprintSize = thin
                    ? new Vector3(modelBounds.size.x, modelHeight, Mathf.Max(modelBounds.size.z, MinWallDepth))
                    : new Vector3(span * 0.9f, modelHeight, span * 0.9f);

                // Hoarding and site box share one parent so WV_Constructable can hide the whole site
                // with a single reference the moment the build finishes.
                var site = new GameObject("ConstructionSite");
                site.transform.SetParent(root.transform, false);

                // The hoarding is authored one cell square; a thin structure's site is fenced off
                // around the strip it stands on rather than around a whole square of cells.
                Vector2 siteSize = thin
                    ? new Vector2(footprintSize.x + SitePadding, footprintSize.z + SitePadding)
                    : new Vector2(span, span);
                GameObject scaffold = (GameObject)PrefabUtility.InstantiatePrefab(scaffoldPrefab, site.transform);
                scaffold.name = "ConstructionScaffold";
                scaffold.transform.localScale =
                    new Vector3(siteSize.x / WV_Rules.GridSize, 1f, siteSize.y / WV_Rules.GridSize);

                GameObject siteBox = (GameObject)PrefabUtility.InstantiatePrefab(siteBoxPrefab, site.transform);
                siteBox.name = "ConstructionSiteBox";
                siteBox.transform.localScale = new Vector3(
                    siteSize.x, thin ? modelHeight : WV_Rules.GetScaffoldHeight(def.FootprintCells), siteSize.y);

                SetLayerRecursive(site, StructureLayer);

                BoxCollider box = root.AddComponent<BoxCollider>();
                box.size = footprintSize;
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
                SetPrivateField(structure, "team", new TeamConfig(WV_Alliances.Defenders));
                SetPrivateField(structure, "effectsComponent", effects);
                SetPrivateField(structure, "healthComponent", health);

                Ensure<WV_Owned>(root);

                // Only the building's own renderers are tinted. The hoarding and the site box keep
                // their own colours, so a site under construction still reads as a site. The same
                // component corrodes those renderers as the structure is worn down, which is why it
                // is also given the structure's health to read.
                var ownerColor = Ensure<WV_OwnerColor>(root);
                SetPrivateField(
                    ownerColor, "tintedRenderers", buildingRoot.GetComponentsInChildren<Renderer>(true));
                SetPrivateField(ownerColor, "damageSource", structure);

                // The countdown floats clear of the finished model rather than on it, so it is still
                // readable while the building is rising out of the ground underneath it.
                GameObject timerBar = (GameObject)PrefabUtility.InstantiatePrefab(
                    Load<GameObject>(WorldTimerBarPath), root.transform);
                timerBar.name = "BuildTimer";
                timerBar.transform.localPosition = new Vector3(0f, modelHeight + span * 0.25f, 0f);

                var constructable = root.AddComponent<WV_Constructable>();
                SetPrivateField(constructable, "footprintCells", def.FootprintCells);
                SetPrivateField(constructable, "maxHealth", def.MaxHealth);
                SetPrivateField(constructable, "buildingRoot", buildingRoot.transform);
                SetPrivateField(constructable, "constructionScaffold", site);
                SetPrivateField(constructable, "buildTimer", timerBar.GetComponent<WorldTimerBar>());

                AddDemolitionExplosion(root, buildingRoot.transform, def);

                NavMeshObstacle obstacle = Ensure<NavMeshObstacle>(root);
                obstacle.shape = NavMeshObstacleShape.Box;
                obstacle.size = box.size;
                obstacle.center = box.center;
                obstacle.carving = true;
                obstacle.carveOnlyStationary = true;

                ApplyRole(def, root, span, modelHeight, unitPrefabs);

                SavePrefab(root, StructurePath(def));
            } finally {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        /// <summary>
        /// Gives a structure the shared explosion it goes out on.
        /// <para>
        /// A demolished building deals no damage to anything: the blast is the shared effect used
        /// with its damage set to zero, so a base collapsing under fire does not chain-detonate the
        /// buildings around it. The same component with a radius and a figure is what a bomb would
        /// use, which is the point of it being one component rather than two.
        /// </para>
        /// </summary>
        static void AddDemolitionExplosion(GameObject root, Transform buildingRoot, StructureDef def) {
            var explosion = Ensure<ExplosionEffect>(root);
            SetPrivateField(explosion, "damageRadius", 0f);
            SetPrivateField(explosion, "damage", 0L);
            SetPrivateField(explosion, "damageType", (int)DamageType.Explosion);
            SetPrivateField(explosion, "damageOwnTeam", false);

            SetPrivateField(explosion, "explosionVfxPrefab", Load<GameObject>(ExplosionVfxPath));
            SetPrivateField(explosion, "smokeVfxPrefab", Load<GameObject>(SmokeVfxPath));
            // One authored effect serves a fence and a hangar: the blast fits itself to the wreck at
            // detonation, standing up the way the effect pack authors it, so no per-slot multiplier.
            SetPrivateField(explosion, "vfxScale", 1f);
            SetPrivateField(explosion, "vfxEulerAngles", new Vector3(-90f, 0f, 0f));
            SetPrivateField(explosion, "fitVfxToDebris", true);
            // No explosion audio exists in the project yet. The hook is authored and left empty so
            // the clip can be dropped in without another rebuild.
            SetPrivateField(explosion, "explosionAudio", null);

            SetPrivateField(explosion, "debrisSource", buildingRoot);
            // Larger structures throw their wreckage further, so a hangar does not come apart with
            // the same little hop a fence post does.
            float mass = Mathf.Sqrt(def.FootprintCells);
            SetPrivateField(explosion, "debrisUpwardForce", 8f * mass);
            SetPrivateField(explosion, "debrisOutwardForce", 3.5f * mass);
            SetPrivateField(explosion, "debrisSpin", 200f);
            SetPrivateField(explosion, "debrisBurnSeconds", 3.5f);

            SetPrivateField(explosion, "detonateOnDeath", true);
            // Long enough for the observers RPC to reach every client before the sender despawns,
            // short enough that the wreck does not keep holding its build slot.
            SetPrivateField(explosion, "despawnDelay", 0.5f);
        }

        /// <summary>
        /// Gives a turret the shared muzzle flash, tracer, and report every client sees and hears.
        /// The muzzle hangs off the yaw part so the flash tracks whatever the barrel is pointed at.
        /// </summary>
        static NetworkedWeaponFire AddTurretWeaponFire(GameObject root, Transform yaw, float span) {
            var muzzle = new GameObject("Muzzle");
            muzzle.transform.SetParent(yaw, false);
            muzzle.transform.localPosition = new Vector3(0f, 0f, span * 0.5f);

            GameObject flash = (GameObject)PrefabUtility.InstantiatePrefab(
                Load<GameObject>(MuzzleFlashVfxPath), muzzle.transform);
            flash.name = "MuzzleFlash";
            flash.transform.localPosition = Vector3.zero;
            flash.transform.localRotation = Quaternion.identity;

            var audioSource = muzzle.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
            // Fully spatialised: a turret firing across the valley should be heard from where it is.
            audioSource.spatialBlend = 1f;
            audioSource.rolloffMode = AudioRolloffMode.Linear;
            audioSource.minDistance = span;
            audioSource.maxDistance = 90f;

            var weaponFire = Ensure<NetworkedWeaponFire>(root);
            SetPrivateField(weaponFire, "muzzle", muzzle.transform);
            SetPrivateField(weaponFire, "muzzleFlash", flash.GetComponent<ParticleSystem>());
            SetPrivateField(weaponFire, "audioSource", audioSource);
            // The project's only authored gun report. It is a pistol shot standing in for a
            // turret's, and should be replaced when heavier weapon audio is imported.
            SetPrivateField(weaponFire, "fireAudio", Load<AudioClip>(GunShotAudioPath));
            return weaponFire;
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
                    // Troops have no prefab to look up: the kind itself is the whole authored value.
                    SetPrivateField(production, "producibleTroops", def.Trains);
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
                    SetPrivateField(turret, "weaponFire", AddTurretWeaponFire(root, yaw.transform, span));
                    break;
                }
                case StructureRole.Wall: {
                    // Reuses the existing breach-link obstacle so a walled-off valley stays solvable:
                    // attackers stop at the link, destroy the section, and walk through the gap.
                    Ensure<NavMeshLink>(root);
                    Ensure<WV_DestructibleObstacle>(root);
                    break;
                }
                case StructureRole.Gate:
                    BuildGate(root, span, modelHeight);
                    break;
                case StructureRole.ShieldGenerator:
                    BuildShield(root, def);
                    break;
                case StructureRole.Research: {
                    var station = Ensure<WV_ResearchBuilding>(root);
                    SetPrivateField(station, "researchRate", def.ResearchRate);
                    break;
                }
            }
        }

        /// <summary>
        /// A gate is a wall - carved, breachable, with the breach link only the waves use - plus a
        /// door and a second link its own side paths through. The fitted model is the door; posts
        /// from the same fence set stand at either end and do not move.
        /// </summary>
        static void BuildGate(GameObject root, float span, float modelHeight) {
            Ensure<NavMeshLink>(root);
            Ensure<WV_DestructibleObstacle>(root);

            Transform buildingRoot = root.transform.Find("Building");
            Transform door = buildingRoot.Find("Model");
            GameObject postSource = Load<GameObject>(GatePostPath);
            // The posts stand as tall as the door at either end of the run, where the next wall or
            // fence run meets the gate.
            foreach (float side in new[] { -1f, 1f }) {
                GameObject post = PlaceFittedToHeight(
                    postSource, buildingRoot, 0f, modelHeight, side < 0f ? "PostLeft" : "PostRight");
                post.transform.localPosition += new Vector3(side * span * 0.5f, 0f, 0f);
                SetLayerRecursive(post, StructureLayer);
                foreach (Collider collider in post.GetComponentsInChildren<Collider>(true))
                    UnityEngine.Object.DestroyImmediate(collider);
            }

            var passage = new GameObject("Passage");
            passage.transform.SetParent(root.transform, false);
            var passageLink = passage.AddComponent<NavMeshLink>();

            var gate = Ensure<WV_Gate>(root);
            SetPrivateField(gate, "door", door);
            SetPrivateField(gate, "passageLink", passageLink);
            // Sinks the door fully into the ground, so the opening is clear top to bottom.
            SetPrivateField(gate, "doorOpenOffset", new Vector3(0f, -modelHeight * 1.05f, 0f));
        }

        /// <summary>
        /// The generator's shield sits on a child of its own: an entity with its own health, sharing
        /// the building's NetworkObject. Its hit volume is a trigger on Ignore Raycast, so it is
        /// measured and aimed at but never stops a body or a bullet, and the shared shield effect is
        /// scaled up to the shield's radius with its renderers authored off until the shield rises.
        /// </summary>
        static void BuildShield(GameObject root, StructureDef def) {
            var shield = new GameObject("Shield");
            shield.transform.SetParent(root.transform, false);
            shield.layer = IgnoreRaycastLayer;

            var effects = Ensure<EffectsComponent>(shield);
            var health = Ensure<HealthComponent>(shield);
            var hitVolume = shield.AddComponent<SphereCollider>();
            hitVolume.isTrigger = true;
            hitVolume.radius = def.ShieldRadius;
            hitVolume.enabled = false;

            GameObject dome = (GameObject)PrefabUtility.InstantiatePrefab(Load<GameObject>(ShieldVfxPath), shield.transform);
            dome.name = "Dome";
            dome.transform.localPosition = Vector3.zero;
            dome.transform.localScale = Vector3.one * (def.ShieldRadius / ShieldVfxRadius);
            SetLayerRecursive(dome, IgnoreRaycastLayer);
            Renderer[] domeRenderers = dome.GetComponentsInChildren<Renderer>(true);
            foreach (Renderer renderer in domeRenderers)
                renderer.enabled = false;

            var barrier = shield.AddComponent<WV_ShieldBarrier>();
            SetPrivateField(barrier, "radius", def.ShieldRadius);
            SetPrivateField(barrier, "maxHealth", def.ShieldHealth);
            SetPrivateField(barrier, "regenerationSeconds", def.ShieldRegenerationSeconds);
            SetPrivateField(barrier, "generator", root.GetComponent<StructureComponent>());
            SetPrivateField(barrier, "domeRenderers", domeRenderers);
            SetPrivateField(barrier, "hitVolume", hitVolume);
            SetPrivateField(barrier, "effectsComponent", effects);
            SetPrivateField(barrier, "healthComponent", health);
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
                SetPrivateField(unit, "team", new TeamConfig(WV_Alliances.Defenders));
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

                // A vehicle carries its commander's colour on its main body the way their buildings
                // do - the same component picks the body out of the pack's single-atlas model - so
                // whose tank it is reads at a glance. The robot rig is skinned and keeps its colours.
                if (!def.UsesRobotRig) {
                    var ownerColor = Ensure<WV_OwnerColor>(root);
                    SetPrivateField(ownerColor, "tintedRenderers", model.GetComponentsInChildren<Renderer>(true));
                }

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
