using RyanAssets.Shared.Declarations;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using Universes.UniverseData.war_valley.Shared;

namespace Universes.UniverseData.war_valley.Editor {
    public static partial class WV_Authoring {
        static GameObject BuildRangeIndicator() {
            string materialPath = PresentationFolder + "/WV_AttackRange.mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (material == null) {
                material = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
                AssetDatabase.CreateAsset(material, materialPath);
            }
            material.shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            material.SetColor("_BaseColor", Color.white);
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_ZWrite", 0f);
            material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            EditorUtility.SetDirty(material);
            var root = new GameObject("AttackRange");
            try {
                root.layer = IgnoreRaycastLayer;
                LineRenderer line = root.AddComponent<LineRenderer>();
                line.sharedMaterial = material;
                line.useWorldSpace = false;
                line.loop = true;
                line.positionCount = 64;
                line.widthMultiplier = 0.12f;
                line.alignment = LineAlignment.View;
                line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                line.receiveShadows = false;
                for (int i = 0; i < line.positionCount; i++) {
                    float angle = i * Mathf.PI * 2f / line.positionCount;
                    line.SetPosition(i, new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)));
                }
                SetPrivateField(root.AddComponent<WV_RangeIndicator>(), "outline", line);
                return SavePrefab(root, PresentationFolder + "/WV_AttackRange.prefab");
            } finally {
                Object.DestroyImmediate(root);
            }
        }

        static void ConfigureRangeIndicator(GameObject root) {
            WV_Unit unit = root.GetComponent<WV_Unit>();
            WV_DefenseTurret turret = root.GetComponent<WV_DefenseTurret>();
            if (unit == null && turret == null)
                return;
            Transform existing = root.transform.Find("AttackRange");
            GameObject indicator = existing != null ? existing.gameObject :
                (GameObject)PrefabUtility.InstantiatePrefab(
                    Load<GameObject>(PresentationFolder + "/WV_AttackRange.prefab"), root.transform);
            indicator.name = "AttackRange";
            var view = indicator.GetComponent<WV_RangeIndicator>();
            SetPrivateField(view, "unit", unit);
            SetPrivateField(view, "turret", turret);
            SetPrivateField(view, "structure", root.GetComponent<StructureComponent>());
            SetPrivateField(view, "constructable", root.GetComponent<WV_Constructable>());
        }

        static void FitStructureHeight(StructureDef def, GameObject model) {
            if (def.Shape != StructureShape.Block) {
                MatchModelHeight(model, 3f);
                return;
            }
            Bounds bounds = WorldBounds(model);
            if (bounds.size.y <= def.HeightLimit)
                return;
            // Reduce uniformly so the source art keeps its proportions.
            model.transform.localScale *= def.HeightLimit / bounds.size.y;
            bounds = WorldBounds(model);
            model.transform.position += new Vector3(-bounds.center.x, -bounds.min.y, -bounds.center.z);
        }

        static void ConfigureStructureCollision(StructureDef def, GameObject model, BoxCollider box) {
            if (def.Shape != StructureShape.Block)
                return;
            // The trigger reserves/selects the footprint. Only visible meshes stop bodies and
            // bullets, so a tower's empty surroundings never become an invisible wall.
            box.isTrigger = true;
            foreach (MeshFilter filter in model.GetComponentsInChildren<MeshFilter>(true)) {
                if (filter.sharedMesh == null || filter.GetComponent<MeshRenderer>() == null)
                    continue;
                MeshCollider collider = Ensure<MeshCollider>(filter.gameObject);
                collider.sharedMesh = filter.sharedMesh;
                collider.convex = false;
            }
        }

        /// <summary>Updates gameplay fields in place, preserving GUIDs, nested art and references.</summary>
        [MenuItem("Ryan/War Valley/Upgrade Gameplay Prefabs")]
        public static void UpgradeGameplayPrefabs() {
            EnsureFolders();
            BuildRangeIndicator();
            foreach (StructureDef def in Structures) {
                string path = StructurePath(def);
                GameObject root = PrefabUtility.LoadPrefabContents(path);
                try {
                    GameObject model = root.transform.Find("Building/Model").gameObject;
                    FitStructureHeight(def, model);
                    if (def.Role == StructureRole.Gate) {
                        foreach (string postName in new[] { "PostLeft", "PostRight" }) {
                            Transform post = root.transform.Find("Building/" + postName);
                            if (post != null)
                                MatchModelHeight(post.gameObject, 3f);
                        }
                    }
                    Bounds bounds = WorldBounds(model);
                    BoxCollider box = root.GetComponent<BoxCollider>();
                    box.size = new Vector3(bounds.size.x, bounds.size.y,
                        def.Shape != StructureShape.Block ? Mathf.Max(MinWallDepth, bounds.size.z) : bounds.size.z);
                    box.center = root.transform.InverseTransformPoint(bounds.center);
                    Transform siteBox = root.transform.Find("ConstructionSite/ConstructionSiteBox");
                    if (siteBox != null) {
                        Vector3 scale = siteBox.localScale;
                        scale.y = bounds.size.y;
                        siteBox.localScale = scale;
                    }
                    ConfigureStructureCollision(def, model, box);
                    NavMeshObstacle obstacle = root.GetComponent<NavMeshObstacle>();
                    obstacle.size = box.size;
                    obstacle.center = box.center;
                    StructureComponent structure = root.GetComponent<StructureComponent>();
                    structure.Cost = def.Cost;
                    structure.Duration = def.BuildSeconds;
                    SetPrivateField(root.GetComponent<WV_Constructable>(), "maxHealth", def.MaxHealth);
                    Transform timer = root.transform.Find("BuildTimer");
                    if (timer != null)
                        timer.localPosition = new Vector3(0f, bounds.size.y + 1f, 0f);
                    var income = root.GetComponent<WV_IncomeBuilding>();
                    if (income != null)
                        SetPrivateField(income, "incomePerTick", def.IncomePerTick);
                    WV_DefenseTurret turret = root.GetComponent<WV_DefenseTurret>();
                    if (turret != null) {
                        SetPrivateField(turret, "range", def.TurretRange);
                        SetPrivateField(turret, "damage", def.TurretDamage);
                        SetPrivateField(turret, "cooldown", def.TurretCooldown);
                        root.transform.Find("TurretYaw").localPosition = new Vector3(0f, bounds.size.y * 0.75f, 0f);
                    }
                    WV_Gate gate = root.GetComponent<WV_Gate>();
                    if (gate != null)
                        SetPrivateField(gate, "doorOpenOffset", new Vector3(0f, -bounds.size.y * 1.05f, 0f));
                    ConfigureRangeIndicator(root);
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                    Stamp(path);
                } finally {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }
            foreach (UnitDef def in Units) {
                string path = UnitPath(def);
                GameObject root = PrefabUtility.LoadPrefabContents(path);
                try {
                    WV_Unit unit = root.GetComponent<WV_Unit>();
                    SetPrivateField(unit, "cost", def.Cost);
                    SetPrivateField(unit, "buildSeconds", def.BuildSeconds);
                    SetPrivateField(unit, "maxHealth", def.MaxHealth);
                    SetPrivateField(unit, "moveSpeed", def.MoveSpeed);
                    SetPrivateField(unit, "turnSpeed", def.TurnSpeed);
                    SetPrivateField(unit, "attackRange", def.AttackRange);
                    SetPrivateField(unit, "detectionRadius", def.DetectionRadius);
                    SetPrivateField(unit, "attackDamage", def.AttackDamage);
                    SetPrivateField(unit, "attackCooldown", def.AttackCooldown);
                    ConfigureRangeIndicator(root);
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                    Stamp(path);
                } finally {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }
            AssetDatabase.SaveAssets();
        }
    }
}
