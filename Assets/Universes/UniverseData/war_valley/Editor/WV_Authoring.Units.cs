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
    // Unit prefabs: vehicles and aircraft.
    public static partial class WV_Authoring {
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
                var networkTransform = root.AddComponent<NetworkTransform>();
                // The server moves every unit. Aircraft turn constantly, so extrapolating them made
                // clients overshoot each turn and snap back; they buffer one more tick instead.
                bool airborne = WV_Rules.IsAircraft(def.Kind);
                var transformSettings = new SerializedObject(networkTransform);
                transformSettings.FindProperty("_clientAuthoritative").boolValue = false;
                transformSettings.FindProperty("_extrapolation").intValue = airborne ? 0 : 1;
                transformSettings.FindProperty("_interpolation").intValue = airborne ? 3 : 2;
                transformSettings.ApplyModifiedPropertiesWithoutUndo();
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

                // Authored body materials carry the commander's colour while wheels, tracks and
                // rotors retain their original art. The robot rig keeps its existing colour path.
                if (!def.UsesRobotRig) {
                    var ownerColor = Ensure<WV_OwnerColor>(root);
                    SetPrivateField(ownerColor, "tintedRenderers", model.GetComponentsInChildren<Renderer>(true));
                    ConfigureUnitBodyColors(model, ownerColor);
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

                ConfigureRangeIndicator(root);
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
    }
}
