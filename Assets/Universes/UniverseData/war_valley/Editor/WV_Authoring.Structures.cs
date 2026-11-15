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
    // Structure prefabs: buildings, turrets, gates, and shields.
    public static partial class WV_Authoring {
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
        static float MeasureRunHeight(StructureDef run) => MeasureRunBounds(run).size.y;

        static Bounds MeasureRunBounds(StructureDef run) {
            var probe = new GameObject("HeightProbe");
            try {
                GameObject model = PlaceFitted(
                    Load<GameObject>(run.ModelPath), probe.transform, Vector3.zero, run.ModelYaw,
                    run.FootprintCells * WV_Rules.GridSize, "Model");
                MatchModelHeight(model, 3f);
                return WorldBounds(model);
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
                GameObject model = PlaceFitted(
                    Load<GameObject>(def.ModelPath), buildingRoot.transform, Vector3.zero, def.ModelYaw, span, "Model");
                if (!string.IsNullOrEmpty(def.MatchHeightOf)) {
                    StructureDef reference = FindStructure(def.MatchHeightOf);
                    if (def.Shape == StructureShape.Post)
                        FitPostFootprint(model, Mathf.Max(MinWallDepth, MeasureRunBounds(reference).size.z));
                    if (!string.IsNullOrEmpty(reference.MatchHeightOf))
                        reference = FindStructure(reference.MatchHeightOf);
                    MatchModelHeight(model, MeasureRunHeight(reference));
                }
                SetLayerRecursive(model, StructureLayer);
                // The pack models ship without colliders; the structure's own box is the collision.
                foreach (Collider collider in model.GetComponentsInChildren<Collider>(true))
                    UnityEngine.Object.DestroyImmediate(collider);

                FitStructureHeight(def, model);

                Bounds modelBounds = WorldBounds(model);
                float modelHeight = Mathf.Max(1f, modelBounds.size.y);
                // A wall or post is as thin as its art, not a square block: its box, its carve in the
                // NavMesh, and its construction site all follow the model. The depth is floored so a
                // wire-thin fence still stops a body and a bullet.
                bool thin = def.Shape != StructureShape.Block;
                Vector3 footprintSize = thin
                    ? new Vector3(modelBounds.size.x, modelHeight, Mathf.Max(modelBounds.size.z, MinWallDepth))
                    : modelBounds.size;

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
                ConfigureStructureCollision(def, model, box);

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
                SetPrivateField(constructable, "snapToGrid", !def.FreePlacement);
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
                ConfigureRangeIndicator(root);

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
            // Every child must wait for the replicated shot, including nested flash particles.
            foreach (ParticleSystem particle in flash.GetComponentsInChildren<ParticleSystem>(true)) {
                var main = particle.main;
                main.playOnAwake = false;
            }

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
                FitPostFootprint(post, root.GetComponent<BoxCollider>().size.z);
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
    }
}
