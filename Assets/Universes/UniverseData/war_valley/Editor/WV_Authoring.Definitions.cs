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
    // What every structure and unit prefab is built from.
    public static partial class WV_Authoring {
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
            /// <summary>Placed anywhere it fits rather than on whole cells: it does not join up with walls.</summary>
            public bool FreePlacement;
            public float HeightLimit = 6f;
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
                FootprintCells = 2, Cost = 350, BuildSeconds = 12f, MaxHealth = 650,
                Role = StructureRole.Income, IncomePerTick = 35
            },
            new() {
                Id = "wv_refinery", DisplayName = "Refinery", Category = "Economy",
                Description = "Processes ore at scale. Expensive, slow to raise, and worth it.",
                ModelPath = PackRoot + "/Building_Prefebs/MilitaryBase_Prefeb.prefab",
                FootprintCells = 3, Cost = 900, BuildSeconds = 28f, MaxHealth = 1200,
                Role = StructureRole.Income, IncomePerTick = 90
            },
            new() {
                // Foot soldiers only: the Infantry vehicle duplicated the Knifeman and Gunner the
                // barracks already trains, so it is no longer offered here.
                Id = "wv_barracks", DisplayName = "Barracks", Category = "Military",
                Description = "Trains Knifemen, Gunners, Shrimp, Speedies, Skinny Legends and Punks. Click it to train.",
                ModelPath = PackRoot + "/Building_Prefebs/PersonLivePlace_Prefeb.prefab",
                FootprintCells = 2, Cost = 350, BuildSeconds = 15f, MaxHealth = 850,
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
                FootprintCells = 3, Cost = 700, BuildSeconds = 28f, MaxHealth = 1400,
                Role = StructureRole.Production,
                Produces = new[] { WV_UnitKind.APC, WV_UnitKind.Tank, WV_UnitKind.Artillery }
            },
            new() {
                Id = "wv_helipad", DisplayName = "Helipad", Category = "Air",
                Description = "Launches attack choppers.",
                ModelPath = PackRoot + "/Building_Prefebs/HeliPad_Prefeb.prefab",
                FreePlacement = true, FootprintCells = 2, Cost = 650, BuildSeconds = 22f, MaxHealth = 700,
                Role = StructureRole.Production, Produces = new[] { WV_UnitKind.Chopper }
            },
            new() {
                Id = "wv_airfield", DisplayName = "Airfield", Category = "Air",
                Description = "Directs jets, bombers and reconnaissance drones.",
                ModelPath = PackRoot + "/Building_Prefebs/ControlTower_Prefeb.prefab",
                FreePlacement = true, FootprintCells = 3, Cost = 1000, BuildSeconds = 32f, MaxHealth = 1100, HeightLimit = 8f,
                Role = StructureRole.Production,
                Produces = new[] { WV_UnitKind.UAV, WV_UnitKind.Jet, WV_UnitKind.Bomber }
            },
            new() {
                // Keeps the radar's id: the structure is the same asset, given a job. It is the
                // research station every research project needs, and each extra one speeds research.
                Id = "wv_radar", DisplayName = "Research Station", Category = "Support",
                Description = "Runs research for your side. Every extra station adds research speed.",
                ModelPath = PackRoot + "/Building_Prefebs/Radar_Prefeb.prefab",
                FreePlacement = true, FootprintCells = 2, Cost = 450, BuildSeconds = 18f, MaxHealth = 650, HeightLimit = 5f,
                Role = StructureRole.Research, ResearchRate = 1f
            },
            new() {
                Id = "wv_missile_battery", DisplayName = "Missile Battery", Category = "Defense",
                Description = "Surface-to-air battery. Engages aircraft only.",
                ModelPath = PackRoot + "/Building_Prefebs/MissilePort_Prefeb.prefab",
                FreePlacement = true, FootprintCells = 2, Cost = 600, BuildSeconds = 22f, MaxHealth = 900,
                Role = StructureRole.Turret, TurretRange = 44f, TurretDamage = 65, TurretCooldown = 2f,
                AntiAir = true
            },
            new() {
                Id = "wv_guard_post", DisplayName = "Guard Post", Category = "Defense",
                Description = "Manned emplacement covering a ground approach.",
                ModelPath = PackRoot + "/Building_Prefebs/GuardPoint_01_Prefeb.prefab",
                FreePlacement = true, FootprintCells = 1, Cost = 275, BuildSeconds = 10f, MaxHealth = 600, HeightLimit = 4f,
                Role = StructureRole.Turret, TurretRange = 28f, TurretDamage = 32, TurretCooldown = 1.2f
            },
            new() {
                Id = "wv_watchtower", DisplayName = "Watchtower", Category = "Defense",
                Description = "Cheap picket that shoots back.",
                ModelPath = PackRoot + "/Building_Prefebs/LightTower_Prefeb.prefab",
                FreePlacement = true, FootprintCells = 1, Cost = 200, BuildSeconds = 8f, MaxHealth = 450,
                Role = StructureRole.Turret, TurretRange = 22f, TurretDamage = 22, TurretCooldown = 1.1f
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
                Role = StructureRole.Wall, Shape = StructureShape.Run, ModelYaw = 90f, MatchHeightOf = "basic_wall"
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
                FreePlacement = true, FootprintCells = 2, Cost = 1000, BuildSeconds = 28f, MaxHealth = 900,
                Role = StructureRole.ShieldGenerator,
                ShieldRadius = 18f, ShieldHealth = 1500, ShieldRegenerationSeconds = 30f
            }
        };

        static readonly UnitDef[] Units = {
            new() {
                Kind = WV_UnitKind.Infantry, DisplayName = "Infantry", UsesRobotRig = true,
                Cost = 120, BuildSeconds = 8f, MaxHealth = 150, MoveSpeed = 14f, TurnSpeed = 420f,
                AttackRange = 16f, DetectionRadius = 22f, AttackDamage = 18, AttackCooldown = 1.2f,
                Radius = 0.6f, Height = 2.2f, ModelSpan = 1.8f
            },
            new() {
                Kind = WV_UnitKind.APC, DisplayName = "APC",
                ModelPath = PackRoot + "/APC_Prefebs/APC_01_Prefeb.prefab",
                Cost = 280, BuildSeconds = 12f, MaxHealth = 450, MoveSpeed = 14f, TurnSpeed = 200f,
                AttackRange = 20f, DetectionRadius = 26f, AttackDamage = 30, AttackCooldown = 1f,
                Radius = 1.6f, Height = 2.6f, ModelSpan = 5f
            },
            new() {
                Kind = WV_UnitKind.Tank, DisplayName = "Tank",
                ModelPath = PackRoot + "/Tank_Prefebs/Tank_01_Prefeb.prefab",
                Cost = 450, BuildSeconds = 18f, MaxHealth = 700, MoveSpeed = 11f, TurnSpeed = 140f,
                AttackRange = 28f, DetectionRadius = 34f, AttackDamage = 70, AttackCooldown = 2.2f,
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
                AttackRange = 26f, DetectionRadius = 34f, AttackDamage = 45, AttackCooldown = 1.4f,
                Radius = 2f, Height = 3f, ModelSpan = 7f
            },
            new() {
                Kind = WV_UnitKind.UAV, DisplayName = "UAV",
                ModelPath = PackRoot + "/UAV_Prefebs/UAV_01_Prefeb.prefab",
                Cost = 350, BuildSeconds = 14f, MaxHealth = 180, MoveSpeed = 26f, TurnSpeed = 170f,
                AttackRange = 18f, DetectionRadius = 40f, AttackDamage = 22, AttackCooldown = 1.6f,
                Radius = 1.5f, Height = 2f, ModelSpan = 5f
            },
            new() {
                Kind = WV_UnitKind.Jet, DisplayName = "Jet",
                ModelPath = PackRoot + "/Jet_Prefebs/Jet_01_Prefeb.prefab",
                Cost = 750, BuildSeconds = 24f, MaxHealth = 320, MoveSpeed = 34f, TurnSpeed = 150f,
                AttackRange = 30f, DetectionRadius = 40f, AttackDamage = 70, AttackCooldown = 2f,
                Radius = 2f, Height = 2.4f, ModelSpan = 8f
            },
            new() {
                Kind = WV_UnitKind.Bomber, DisplayName = "Bomber",
                ModelPath = PackRoot + "/Bomber_Prefebs/Bomber_01_Prefeb.prefab",
                Cost = 950, BuildSeconds = 32f, MaxHealth = 500, MoveSpeed = 30f, TurnSpeed = 80f,
                AttackRange = 24f, DetectionRadius = 34f, AttackDamage = 140, AttackCooldown = 5f,
                Radius = 2.6f, Height = 3f, ModelSpan = 10f
            }
        };
    }
}
