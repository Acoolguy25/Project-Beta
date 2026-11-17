using System.Collections.Generic;
using System.Linq;
using RyanAssets.GameFeel;
using RyanAssets.UI;
using RyanAssets.UI.Editor;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.UI;
using Universes.UniverseData.murder_mystery.Client;

namespace Universes.UniverseData.murder_mystery.Editor {
    /// <summary>
    /// Rebuilds the murder mystery presentation prefabs (HUD and body marker), makes sure the HUD is in the
    /// start scene, and applies the mansion's atmosphere (fog, ambient, warm lights) to that scene.
    /// Run from Ryan/Murder Mystery in a client (non-server) Editor.
    /// </summary>
    public static class MM_Authoring {
        public const string Root = "Assets/Universes/UniverseData/murder_mystery";
        const string HudPath = Root + "/Prefabs/MM_HUD.prefab";
        const string MarkerPath = Root + "/Prefabs/MM_BodyMarker.prefab";
        const string StartScene = Root + "/murder_mystery_start.unity";
        const string QuickEffects = "Assets/GabrielAguiarProductions/FreeQuickEffectsVol1/Prefabs/";
        const string Sounds = "Assets/Universes/UniverseData/classic_horror/Flooded_Grounds/Content/Sounds/";
        const string OwnSounds = Root + "/Audio/";

        static readonly Color Blood = new(0.9f, 0.08f, 0.06f, 0.85f);

        [MenuItem("Ryan/Murder Mystery/Rebuild HUD and Markers")]
        public static void BuildAll() {
            EnsureFolders();
            GameObject marker = BuildMarker();
            BuildHud(marker);
            AddHudToStartScene();
            AssetDatabase.SaveAssets();
        }

        // ---------- atmosphere ----------

        // A lamp-lit house at night rather than grey concrete under white light. Two hues: cool moonlight
        // (the directional "Global Visibility Fill", the ambient and the fog) for the shadows, and warm
        // tungsten from standing lamps spread through the rooms, so a warm source is in view from anywhere
        // in the house, with a pool of light around each and darker gaps between. The room fills stay a
        // dim, nearly white bounce with a short range, so they lift the corners without washing the pools out.
        // The post-processing look (MoodPreset.WarmIndoor; Night was tried and crushed the rooms to black) is
        // applied at runtime by MM_ClientHUD; judge all of this in Play mode. Values were tuned by eye in Play
        // mode from the camera spots in the README.
        //
        // Rendering limits (StarterAssetsURPAsset, the pipeline every game uses): Forward, 8 additional lights
        // per object and no additional-light shadows. So point lights cast no shadows (they are set to None
        // rather than claiming soft shadows that never render), the fill nearest each lamp is switched off
        // so a room does not stack lights on one floor mesh, and the only shadows come from the moonlight.
        static readonly Color FogColor = new(0.045f, 0.052f, 0.07f);
        const float FogDensity = 0.011f;
        // The ground term keeps the darkest walls a deep blue rather than black (black walls showed the
        // renderer's banding as blotches). The ambient is the only light most of a wall gets: the map's walls
        // and floors are a few huge meshes, and URP gives each object only its 8 most important lights, so a
        // wall far from those reads black (it looked like a hole in the house) unless the ambient lifts it.
        static readonly Color AmbientSky = new(0.32f, 0.36f, 0.48f);
        static readonly Color AmbientEquator = new(0.28f, 0.3f, 0.35f);
        static readonly Color AmbientGround = new(0.2f, 0.21f, 0.27f);
        static readonly Color TungstenLight = new(1f, 0.74f, 0.45f);
        // The fills are a bounce, not a source: almost white, so characters keep their own colours.
        static readonly Color FillLight = new(1f, 0.93f, 0.84f);
        const string FillLightPrefix = "Warm Fill";
        const string CeilingLightPrefix = "Ceiling Light";
        // Room lights authored in map-local coordinates. An earlier pass gave their containers a transform
        // that cancelled the Map's offset and scale, which parked every one of them near the world origin,
        // far outside the house; ApplyAtmosphere puts them back in the rooms.
        static readonly string[] RoomLightContainers = { "Dense Warm Fill Lights", "Additional Warm Lights" };
        // About one room across (the map is 3x scale, so a 4 m plan room is 12 m): the pools stay separate.
        const float RoomFillRange = 13f;
        const float RoomFillIntensity = 24f;
        // A fill this close to a wall paints a hard-edged disc on it; it is dimmed in proportion instead.
        const float FillWallClearance = 3f;
        // A fill right against a wall keeps this much of its intensity.
        const float MinWallFillFactor = 0.25f;
        // The ceiling lights were 80 / 50 m, one per room, flooding it; now a soft top light.
        const float CeilingLightRange = 24f;
        const float CeilingLightIntensity = 18f;
        const string VisibilityFillName = "Global Visibility Fill";
        // Cool moonlight. It is the one light that can cast shadows here (the main light).
        static readonly Color VisibilityFillColor = new(0.62f, 0.72f, 0.95f);
        const float VisibilityFillIntensity = 0.8f;

        // Standing lamps, placed from the room lights: each ceiling light and room fill marks a spot inside
        // a room, and a lamp goes against the nearest wall there unless another lamp is already close.
        const string LampModelPath = "Assets/ThirdParty/KayKit/FurnitureBits/Models/lamp_standing.fbx";
        const string FurnitureModels = "Assets/ThirdParty/KayKit/FurnitureBits/Models/";
        const string LampPrefabPath = Root + "/Prefabs/MM_StandingLamp.prefab";
        const string LampCornerPrefabPath = Root + "/Prefabs/MM_LampCorner.prefab";
        const string LampGlowMaterialPath = Root + "/Materials/MM_LampGlow.mat";
        const string LampsRoot = "MM_Lamps";
        // The model is 2.5 m tall; the robots are 5.6 m, so furniture at 2.2x is in proportion.
        const float FurnitureScale = 2.2f;
        // The bulb sits inside the shade, near the top of the 2.5 m model (model units).
        const float LampBulbHeight = 2.15f;
        // At 90 the armchair, the rug and anyone beside the lamp clipped to flat yellow-white under the
        // WarmIndoor grade; at this intensity they keep their own colour and shading 2-3 m from the bulb.
        const float LampLightRange = 24f;
        const float LampLightIntensity = 50f;
        // A thin pole and base: characters walk round the lamp, not through it (metres, after scaling).
        const float LampColliderRadius = 0.55f;
        // The shade glows: emission is the model's own palette texture tinted tungsten, so the light shade
        // glows brightly and the dark pole barely at all.
        const float LampGlowStrength = 3.5f;
        // Lamps stand this far from the wall they are put against, and look for one within this distance.
        const float LampWallGap = 2.5f;
        const float LampWallSearch = 60f;
        const float LampFloorSearch = 80f;
        // The wall is looked for in this many directions, from this fraction of the lamp's height.
        const int WallProbeDirections = 8;
        // A lamp's wall is at least this wide (metres): a pillar or a post in a hall does not count, so a lamp
        // does not end up in the middle of the floor next to one.
        const float LampWallMinWidth = 4f;
        const float WallProbeHeightFraction = 0.5f;
        // No two lamps on the same storey closer than this (metres), so each keeps its own pool.
        const float LampSpacing = 15f;
        // Lamps further apart in height than this are on different storeys.
        const float StoreyHeight = 6f;
        // The room fill nearest a new lamp, if this close, is switched off: the lamp replaces it.
        const float FillReplaceRadius = 10f;
        // Corner group: an armchair and a cabinet either side of the lamp and a rug in front, placed only
        // where this box (lamp at the origin, wall behind along -Z) is clear: the group itself plus about
        // 5 m of floor in front of it, so a group never narrows a corridor or a doorway.
        static readonly Vector3 CornerFootprintCentre = new(0f, 1.8f, 2f);
        static readonly Vector3 CornerFootprintHalfSize = new(4.6f, 1.4f, 4.5f);
        // Walkable spots with no room light above them (world positions, a little above the floor) and how far
        // from the wall that lamp stands: the landing at the top of the Secret Room stairs, whose room behind is
        // not built and has no lights, and the 30 m ramp corridor between Workshop 1 and the room south of it
        // (far end and middle; Lamp 01 stands at its north end). The corridor is 5 m wide, so its lamps stand
        // close to the side wall and come without the corner group.
        static readonly (Vector3 position, float wallGap)[] ExtraLampAnchors = {
            (new(969.5f, 43f, 1124f), LampWallGap),
            (new(959.2f, 30f, 924.5f), CorridorLampWallGap),
            (new(959.2f, 27.5f, 939.5f), CorridorLampWallGap),
        };
        const float CorridorLampWallGap = 1f;
        // After moving towards the wall there must be floor under the lamp within this height of the anchor's floor.
        const float MaxFloorStep = 1f;
        // Rooms the map has no light in at all: a room fill there (world position, range, intensity). The Secret
        // Room round the stair landing is a 27 x 14 m shaft, 30 m tall; without it its walls were black behind the
        // landing lamp. Its walls are part of one mesh that spans the house, which gets only 8 lights of its own,
        // so this fill is marked Important (picked before the others for that mesh).
        static readonly (Vector3 position, float range, float intensity)[] ExtraFills = { (new(966f, 38f, 1123f), 28f, 30f) };

        // Backdrop panels where the map shows the void: faces the map has only from the other side, so from a
        // playable spot the camera looks through them at nothing. The step down at the north end of the Lower
        // Hall (z 1192, 3 m high) is seen from behind from the lower floor and from the hall camera. Each is a
        // quad facing +Z after its yaw, in the map's own Cement2 look (MM_Backdrop.mat), with no collider (the
        // map's own collider is there).
        const string BackdropRoot = "MM_Backdrop";
        const string BackdropMaterialPath = Root + "/Materials/MM_Backdrop.mat";
        const string MapModelPath = Root + "/map1/Map.fbx";
        const string BackdropSourceMaterial = "Cement2";
        // Metres of wall per repeat of the Cement2 texture.
        const float BackdropTextureMetres = 3f;
        static readonly (Vector3 centre, Vector2 size, float yaw)[] BackdropPanels = {
            (new(1000.5f, 18.5f, 1192.2f), new(52f, 3.2f), 0f),
        };

        [MenuItem("Ryan/Murder Mystery/Apply Atmosphere")]
        public static void ApplyAtmosphere() {
            EnsureFolders();
            GameObject lampPrefab = BuildLampPrefab();
            GameObject cornerPrefab = BuildLampCornerPrefab(lampPrefab);
            EditInStartScene(scene => {
                RenderSettings.fog = true;
                RenderSettings.fogMode = FogMode.ExponentialSquared;
                RenderSettings.fogColor = FogColor;
                RenderSettings.fogDensity = FogDensity;
                RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
                RenderSettings.ambientSkyColor = AmbientSky;
                RenderSettings.ambientEquatorColor = AmbientEquator;
                RenderSettings.ambientGroundColor = AmbientGround;
                Physics.SyncTransforms();
                PhysicsScene physics = scene.GetPhysicsScene();
                List<Light> fills = new();
                List<Light> ceilingLights = new();
                foreach (GameObject rootObject in scene.GetRootGameObjects()) {
                    foreach (Transform container in rootObject.GetComponentsInChildren<Transform>(true)) {
                        if (System.Array.IndexOf(RoomLightContainers, container.name) < 0)
                            continue;
                        Undo.RecordObject(container, "Move murder mystery room lights");
                        container.localPosition = Vector3.zero;
                        container.localRotation = Quaternion.identity;
                        container.localScale = Vector3.one;
                        fills.AddRange(container.GetComponentsInChildren<Light>(true));
                    }
                    foreach (Light light in rootObject.GetComponentsInChildren<Light>(true)) {
                        string name = light.gameObject.name;
                        if (name == VisibilityFillName) {
                            Undo.RecordObject(light, "Murder mystery moonlight");
                            light.color = VisibilityFillColor;
                            light.intensity = VisibilityFillIntensity;
                            // The main light is the one that can cast shadows: characters and lamps get one.
                            light.shadows = LightShadows.Soft;
                        } else if (name.StartsWith(CeilingLightPrefix)) {
                            Undo.RecordObject(light, "Murder mystery ceiling lights");
                            light.color = FillLight;
                            light.range = CeilingLightRange;
                            light.intensity = CeilingLightIntensity;
                            // Additional lights cannot cast shadows in this pipeline (see above).
                            light.shadows = LightShadows.None;
                            ceilingLights.Add(light);
                        }
                    }
                }
                Physics.SyncTransforms();
                foreach (Light fill in fills) {
                    Undo.RecordObject(fill, "Murder mystery room fills");
                    fill.enabled = true;
                    fill.range = RoomFillRange;
                    float clearance = NearestWall(physics, fill.transform.position, FillWallClearance, 0f, out _);
                    fill.intensity = RoomFillIntensity * Mathf.Lerp(MinWallFillFactor, 1f, clearance / FillWallClearance);
                    fill.color = FillLight;
                    fill.shadows = LightShadows.None;
                }
                List<Transform> lamps = PlaceLamps(scene, physics, lampPrefab, cornerPrefab, ceilingLights, fills);
                ReplaceNearestFills(lamps, fills);
                AddExtraFills(scene.GetRootGameObjects().First(o => o.name == LampsRoot).transform);
                PlaceBackdrop(scene);
                Debug.Log($"[MM] Atmosphere: {lamps.Count} lamps, {fills.Count(f => !f.enabled)} of {fills.Count} room fills replaced by a lamp.");
                return true;
            });
        }

        /// <summary>The standing lamp: KayKit model with a glowing shade, its tungsten bulb and a thin collider.</summary>
        static GameObject BuildLampPrefab() {
            GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(LampModelPath);
            if (model == null)
                throw new System.InvalidOperationException("Missing lamp model: " + LampModelPath);
            Material glow = BuildLampGlowMaterial(model.GetComponentInChildren<MeshRenderer>().sharedMaterial);

            GameObject root = new("MM_StandingLamp");
            try {
                GameObject body = (GameObject)PrefabUtility.InstantiatePrefab(model, root.transform);
                body.name = "Model";
                body.transform.localScale = Vector3.one * FurnitureScale;
                foreach (MeshRenderer renderer in body.GetComponentsInChildren<MeshRenderer>())
                    renderer.sharedMaterial = glow;

                GameObject bulb = new("Bulb");
                bulb.transform.SetParent(root.transform, false);
                bulb.transform.localPosition = Vector3.up * (LampBulbHeight * FurnitureScale);
                Light light = bulb.AddComponent<Light>();
                light.type = LightType.Point;
                light.color = TungstenLight;
                light.range = LampLightRange;
                light.intensity = LampLightIntensity;
                light.shadows = LightShadows.None;

                float height = LampBulbHeight * FurnitureScale;
                CapsuleCollider pole = root.AddComponent<CapsuleCollider>();
                pole.radius = LampColliderRadius;
                pole.height = height;
                pole.center = Vector3.up * (height * 0.5f);
                AddObstacle(root, pole.center, new Vector3(LampColliderRadius * 2f, height, LampColliderRadius * 2f), NavMeshObstacleShape.Capsule);
                return PrefabUtility.SaveAsPrefabAsset(root, LampPrefabPath);
            } finally {
                Object.DestroyImmediate(root);
            }
        }

        /// <summary>
        /// The shade's material: the model's palette texture as the emission map, tinted tungsten. URP's
        /// material validation turns _EMISSION off again (on import, in Play, on Editor stop) for a material
        /// without an emissive GI flag, so the flag is what keeps the glow; MM_LampGlowTests checks the asset.
        /// </summary>
        static Material BuildLampGlowMaterial(Material source) {
            Material glow = AssetDatabase.LoadAssetAtPath<Material>(LampGlowMaterialPath);
            if (glow == null) {
                glow = new Material(source);
                AssetDatabase.CreateAsset(glow, LampGlowMaterialPath);
            } else {
                glow.CopyPropertiesFromMaterial(source);
            }
            glow.SetTexture("_EmissionMap", source.mainTexture);
            glow.SetColor("_EmissionColor", TungstenLight * LampGlowStrength);
            glow.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            glow.SetKeyword(new UnityEngine.Rendering.LocalKeyword(glow.shader, "_EMISSION"), true);
            EditorUtility.SetDirty(glow);
            AssetDatabase.SaveAssetIfDirty(glow);
            return glow;
        }

        /// <summary>One piece of the corner group: KayKit model name, position next to the lamp (local +Z faces into
        /// the room, the wall is LampWallGap behind the lamp), yaw in degrees, and whether it gets a collider.</summary>
        readonly struct CornerPiece {
            public readonly string model;
            public readonly Vector3 position;
            public readonly float yaw;
            public readonly bool solid;
            public CornerPiece(string model, Vector3 position, float yaw, bool solid) {
                this.model = model; this.position = position; this.yaw = yaw; this.solid = solid;
            }
        }

        // Armchair to the lamp's right, turned a little towards the room; a cabinet to its left, back to the
        // wall; a rug in front (walkable, no collider).
        static readonly CornerPiece[] CornerPieces = {
            new("armchair_pillows", new Vector3(2.7f, 0f, -0.6f), -25f, true),
            new("cabinet_medium_decorated", new Vector3(-2.9f, 0f, -1.4f), 0f, true),
            new("rug_oval_A", new Vector3(0.6f, 0.02f, 1.2f), 0f, false),
        };

        /// <summary>A lamp between an armchair and a cabinet, with a rug in front: a lived-in corner of a room.</summary>
        static GameObject BuildLampCornerPrefab(GameObject lampPrefab) {
            GameObject root = new("MM_LampCorner");
            try {
                PrefabUtility.InstantiatePrefab(lampPrefab, root.transform);
                foreach (CornerPiece piece in CornerPieces)
                    AddFurniture(root.transform, piece.model, piece.position, piece.yaw, piece.solid);
                return PrefabUtility.SaveAsPrefabAsset(root, LampCornerPrefabPath);
            } finally {
                Object.DestroyImmediate(root);
            }
        }

        static void AddFurniture(Transform parent, string model, Vector3 position, float yaw, bool solid) {
            string path = FurnitureModels + model + ".fbx";
            GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (asset == null)
                throw new System.InvalidOperationException("Missing furniture model: " + path);
            GameObject piece = (GameObject)PrefabUtility.InstantiatePrefab(asset, parent);
            piece.name = model;
            piece.transform.SetLocalPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
            piece.transform.localScale = Vector3.one * FurnitureScale;
            if (!solid)
                return;
            // A box round the model (its own axes), so nobody walks through the furniture.
            Bounds bounds = new();
            bool first = true;
            foreach (MeshFilter filter in piece.GetComponentsInChildren<MeshFilter>()) {
                Bounds mesh = filter.sharedMesh.bounds;
                Matrix4x4 toPiece = piece.transform.worldToLocalMatrix * filter.transform.localToWorldMatrix;
                foreach (Vector3 corner in Corners(mesh)) {
                    Vector3 local = toPiece.MultiplyPoint3x4(corner);
                    if (first) { bounds = new Bounds(local, Vector3.zero); first = false; }
                    else bounds.Encapsulate(local);
                }
            }
            BoxCollider box = piece.AddComponent<BoxCollider>();
            box.center = bounds.center;
            box.size = bounds.size;
            AddObstacle(piece, bounds.center, bounds.size, NavMeshObstacleShape.Box);
        }

        static IEnumerable<Vector3> Corners(Bounds b) {
            for (int i = 0; i < 8; i++)
                yield return new Vector3((i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z);
        }

        /// <summary>
        /// NPCs steer round the furniture: an avoidance obstacle the size of its collider. Not carving: a hole
        /// cut in the NavMesh at runtime can split it (a corner in a passage cut the Control Room off).
        /// </summary>
        static void AddObstacle(GameObject target, Vector3 centre, Vector3 size, NavMeshObstacleShape shape) {
            NavMeshObstacle obstacle = target.AddComponent<NavMeshObstacle>();
            obstacle.shape = shape;
            obstacle.center = centre;
            obstacle.size = size;
            if (shape == NavMeshObstacleShape.Capsule) {
                obstacle.radius = size.x * 0.5f;
                obstacle.height = size.y;
            }
            obstacle.carving = false;
        }

        /// <summary>
        /// Lamps from the room lights, ceiling lights first: straight down to the floor (from the nearest fill
        /// when a ceiling light has none under it), then against the nearest wall, so a lamp stands at the edge
        /// of the room rather than in the walkway. Skips spots too close to a lamp already placed. Where there
        /// is room, the lamp comes with its corner furniture. Re-running replaces the set.
        /// </summary>
        static List<Transform> PlaceLamps(UnityEngine.SceneManagement.Scene scene, PhysicsScene physics, GameObject lampPrefab,
            GameObject cornerPrefab, List<Light> ceilingLights, List<Light> fills) {
            foreach (GameObject rootObject in scene.GetRootGameObjects())
                if (rootObject.name == LampsRoot)
                    Undo.DestroyObjectImmediate(rootObject);
            GameObject lampsRoot = new(LampsRoot);
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(lampsRoot, scene);
            Physics.SyncTransforms();

            List<Transform> lamps = new();
            Collider[] footprintHits = new Collider[1];
            int corners = 0;
            var anchors = ceilingLights.Concat(fills).Select(l => (name: l.name, position: l.transform.position, wallGap: LampWallGap, context: (Object)l))
                .Concat(ExtraLampAnchors.Select(a => (name: $"Anchor {a.position}", a.position, a.wallGap, context: (Object)null)));
            int skipped = 0;
            foreach (var anchor in anchors) {
                Vector3 from = anchor.position;
                if (!physics.Raycast(from, Vector3.down, out RaycastHit floor, LampFloorSearch)) {
                    Light nearestFill = fills.OrderBy(f => (f.transform.position - from).sqrMagnitude).FirstOrDefault();
                    if (nearestFill == null || !physics.Raycast(nearestFill.transform.position, Vector3.down, out floor, LampFloorSearch)) {
                        Debug.LogWarning($"[MM] No floor under {anchor.name} or its nearest fill; no lamp there.", anchor.context);
                        continue;
                    }
                }
                float probeHeight = LampBulbHeight * FurnitureScale * WallProbeHeightFraction;
                // Closer than the lamp gap is the lamp's own spot against a pillar or doorframe: not a wall to use.
                float wallDistance = NearestWall(physics, floor.point + Vector3.up * probeHeight, LampWallSearch, anchor.wallGap, out Vector3 towardWall, LampWallMinWidth);
                Vector3 spot = floor.point + towardWall * (wallDistance - anchor.wallGap);
                // No wall in reach with floor under the lamp (a hall centre, a stair): a lamp there would stand in
                // the walkway, so there is none; the neighbouring lamps and the room's own lights cover the spot.
                // Moving to the wall must not walk the lamp off a landing or onto a step.
                if (towardWall == Vector3.zero || !physics.Raycast(spot + Vector3.up * MaxFloorStep, Vector3.down, 2f * MaxFloorStep)) {
                    Debug.Log($"[MM] No wall with floor in reach of {anchor.name}; no lamp there.", anchor.context);
                    skipped++;
                    continue;
                }
                if (lamps.Any(l => SameStorey(l.position, spot) && HorizontalDistance(l.position, spot) < LampSpacing))
                    continue;

                Quaternion facing = Quaternion.LookRotation(-towardWall);
                bool roomForCorner = anchor.wallGap >= LampWallGap
                    && physics.OverlapBox(spot + facing * CornerFootprintCentre, CornerFootprintHalfSize, footprintHits, facing, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) == 0;
                GameObject lamp = (GameObject)PrefabUtility.InstantiatePrefab(roomForCorner ? cornerPrefab : lampPrefab, lampsRoot.transform);
                lamp.name = $"Lamp {lamps.Count + 1:00} - {anchor.name}";
                lamp.transform.SetPositionAndRotation(spot, facing);
                lamps.Add(lamp.transform);
                if (roomForCorner)
                    corners++;
                // Later footprint checks see this group.
                Physics.SyncTransforms();
            }
            Undo.RegisterCreatedObjectUndo(lampsRoot, "Place murder mystery lamps");
            Debug.Log($"[MM] {lamps.Count} lamps placed, {corners} with corner furniture; {skipped} spots without a wall skipped.");
            return lamps;
        }

        static void AddExtraFills(Transform lampsRoot) {
            foreach (var extra in ExtraFills) {
                GameObject fill = new($"Extra Fill {extra.position}");
                fill.transform.SetParent(lampsRoot, false);
                fill.transform.position = extra.position;
                Light light = fill.AddComponent<Light>();
                light.type = LightType.Point;
                light.color = FillLight;
                light.range = extra.range;
                light.intensity = extra.intensity;
                light.renderMode = LightRenderMode.ForcePixel;
                light.shadows = LightShadows.None;
            }
        }

        static void PlaceBackdrop(UnityEngine.SceneManagement.Scene scene) {
            foreach (GameObject rootObject in scene.GetRootGameObjects())
                if (rootObject.name == BackdropRoot)
                    Undo.DestroyObjectImmediate(rootObject);
            GameObject root = new(BackdropRoot);
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, scene);
            Material material = BuildBackdropMaterial();
            Mesh quad = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
            foreach (var panel in BackdropPanels) {
                GameObject piece = new($"Backdrop {panel.centre}");
                piece.transform.SetParent(root.transform, false);
                // The built-in quad faces -Z; turn it round so it faces +Z before the panel's own yaw.
                piece.transform.SetPositionAndRotation(panel.centre, Quaternion.Euler(0f, panel.yaw + 180f, 0f));
                piece.transform.localScale = new Vector3(panel.size.x, panel.size.y, 1f);
                piece.AddComponent<MeshFilter>().sharedMesh = quad;
                MeshRenderer renderer = piece.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            Undo.RegisterCreatedObjectUndo(root, "Murder mystery backdrop");
        }

        /// <summary>The map's Cement2 wall material, tiled for the first backdrop panel (there is one; a panel of
        /// another size would want its own material).</summary>
        static Material BuildBackdropMaterial() {
            Material source = AssetDatabase.LoadAllAssetsAtPath(MapModelPath).OfType<Material>().FirstOrDefault(m => m.name == BackdropSourceMaterial);
            if (source == null)
                throw new System.InvalidOperationException($"Missing material {BackdropSourceMaterial} in {MapModelPath}");
            Material material = AssetDatabase.LoadAssetAtPath<Material>(BackdropMaterialPath);
            if (material == null) {
                material = new Material(source);
                AssetDatabase.CreateAsset(material, BackdropMaterialPath);
            } else {
                material.CopyPropertiesFromMaterial(source);
            }
            Vector2 size = BackdropPanels[0].size;
            material.SetTextureScale("_BaseMap", size / BackdropTextureMetres);
            EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssetIfDirty(material);
            return material;
        }

        /// <summary>Switches off the room fill nearest each lamp, so the lamp is that spot's light instead of another one on top.</summary>
        static void ReplaceNearestFills(List<Transform> lamps, List<Light> fills) {
            foreach (Transform lamp in lamps) {
                Vector3 bulb = lamp.position + Vector3.up * (LampBulbHeight * FurnitureScale);
                Light nearest = fills.Where(f => f.enabled)
                    .OrderBy(f => (f.transform.position - bulb).sqrMagnitude)
                    .FirstOrDefault();
                if (nearest != null && (nearest.transform.position - bulb).sqrMagnitude <= FillReplaceRadius * FillReplaceRadius)
                    nearest.enabled = false;
            }
        }

        /// <summary>
        /// Distance to the nearest wall around <paramref name="from"/> (at most <paramref name="search"/>), and its
        /// direction (zero if none). Hits closer than <paramref name="ignoreCloserThan"/> are skipped, and with
        /// <paramref name="minWidth"/> so is anything narrower than that (two rays that far apart, either side of
        /// the first, must hit it at about the same distance).
        /// </summary>
        static float NearestWall(PhysicsScene physics, Vector3 from, float search, float ignoreCloserThan, out Vector3 direction, float minWidth = 0f) {
            float nearest = search;
            direction = Vector3.zero;
            for (int i = 0; i < WallProbeDirections; i++) {
                Vector3 probe = Quaternion.Euler(0f, i * 360f / WallProbeDirections, 0f) * Vector3.forward;
                if (physics.Raycast(from, probe, out RaycastHit wall, nearest) && wall.distance > ignoreCloserThan
                    && (minWidth <= 0f || IsWide(physics, from, probe, wall.distance, minWidth))) {
                    nearest = wall.distance;
                    direction = probe;
                }
            }
            return nearest;
        }

        static bool IsWide(PhysicsScene physics, Vector3 from, Vector3 probe, float distance, float width) {
            Vector3 side = Vector3.Cross(Vector3.up, probe) * (width * 0.5f);
            float tolerance = width * 0.5f;
            return physics.Raycast(from + side, probe, out RaycastHit right, distance + tolerance) && Mathf.Abs(right.distance - distance) <= tolerance
                && physics.Raycast(from - side, probe, out RaycastHit left, distance + tolerance) && Mathf.Abs(left.distance - distance) <= tolerance;
        }

        static bool SameStorey(Vector3 a, Vector3 b) => Mathf.Abs(a.y - b.y) < StoreyHeight;

        static float HorizontalDistance(Vector3 a, Vector3 b) => Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));

        /// <summary>Runs <paramref name="edit"/> on the start scene (opening it if needed) and saves it when the edit says so.</summary>
        static void EditInStartScene(System.Func<UnityEngine.SceneManagement.Scene, bool> edit) {
            var scene = EditorSceneManager.GetSceneByPath(StartScene);
            bool opened = !scene.isLoaded;
            if (opened)
                scene = EditorSceneManager.OpenScene(StartScene, OpenSceneMode.Additive);
            var previousActive = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            try {
                // RenderSettings belong to the active scene.
                UnityEngine.SceneManagement.SceneManager.SetActiveScene(scene);
                if (edit(scene)) {
                    EditorSceneManager.MarkSceneDirty(scene);
                    EditorSceneManager.SaveScene(scene);
                }
            } finally {
                if (previousActive.IsValid() && previousActive.isLoaded && previousActive != scene)
                    UnityEngine.SceneManagement.SceneManager.SetActiveScene(previousActive);
                if (opened)
                    EditorSceneManager.CloseScene(scene, true);
            }
        }

        static void EnsureFolders() {
            foreach (string folder in new[] { "Prefabs", "Materials" })
                if (!AssetDatabase.IsValidFolder(Root + "/" + folder))
                    AssetDatabase.CreateFolder(Root, folder);
        }

        // ---------- body marker ----------

        static GameObject BuildMarker() {
            GameObject root = new("MM_BodyMarker");
            GameObject effect = PlaceEffect(QuickEffects + "vfx_LootDrop_01.prefab", root.transform, new Vector3(0, -0.9f, 0), 1.3f, Blood);
            effect.name = "Beacon";
            Light light = new GameObject("Glow").AddComponent<Light>();
            light.transform.SetParent(root.transform, false);
            light.transform.localPosition = new Vector3(0, 0.4f, 0);
            light.type = LightType.Point;
            light.color = new Color(1f, 0.15f, 0.1f);
            light.range = 5f;
            light.intensity = 2.5f;
            light.shadows = LightShadows.None;
            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, MarkerPath);
            Object.DestroyImmediate(root);
            return saved;
        }

        static GameObject PlaceEffect(string path, Transform parent, Vector3 position, float scale, Color tint) {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
                throw new System.InvalidOperationException("Missing murder mystery presentation asset: " + path);
            GameObject effect = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            effect.transform.localPosition = position;
            effect.transform.localRotation = Quaternion.identity;
            effect.transform.localScale = Vector3.one * scale;
            foreach (ParticleSystem particle in effect.GetComponentsInChildren<ParticleSystem>(true)) {
                ParticleSystem.MainModule main = particle.main;
                main.startColor = tint;
                main.loop = true;
                main.playOnAwake = true;
            }
            foreach (ParticleSystemRenderer renderer in effect.GetComponentsInChildren<ParticleSystemRenderer>(true)) {
                Material[] materials = renderer.sharedMaterials;
                for (int i = 0; i < materials.Length; i++)
                    if (materials[i] != null && !materials[i].shader.name.StartsWith("Universal Render Pipeline/"))
                        materials[i] = UrpParticleMaterial(materials[i]);
                renderer.sharedMaterials = materials;
            }
            return effect;
        }

        static Material UrpParticleMaterial(Material source) {
            string path = Root + "/Materials/VFX_" + source.name + ".mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null)
                throw new System.InvalidOperationException("URP particle shader is missing.");
            if (material == null) {
                material = new Material(shader) { name = "VFX_" + source.name };
                AssetDatabase.CreateAsset(material, path);
            }
            material.shader = shader;
            // A texture that failed to import (e.g. an LFS pointer that was never pulled) must not wipe the
            // one the material already has.
            if (source.mainTexture == null)
                throw new System.InvalidOperationException("Effect texture missing (run git lfs pull?): " + source.name);
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

        // ---------- HUD ----------

        // Role colours mirror MM_Text.RoleColor, so the HUD, name tags and results agree.
        static Color RoleColor(MM_Role role) => MM_Text.RoleColor(role);
        static readonly Color Alarm = new(0.95f, 0.3f, 0.25f);

        /// <summary>
        /// The HUD is made from the shared HudKit pieces so it matches the top bar and menus: a quiet
        /// role chip in the corner, and cards that pop in only when something happens (role reveal,
        /// body nearby, meeting, ejection, results).
        /// </summary>
        static void BuildHud(GameObject marker) {
            // Above the spectate bar (5) and vote window (20), below the top bar (50).
            RectTransform root = HudKit.Canvas("MM_HUD", 30);
            MM_ClientHUD hud = root.gameObject.AddComponent<MM_ClientHUD>();
            hud.innocentIcon = HudKit.Icon("innocent");
            hud.sheriffIcon = HudKit.Icon("sheriff");
            hud.murdererIcon = HudKit.Icon("assassin");
            hud.spectatorIcon = HudKit.Icon("visibility");
            hud.caughtIcon = HudKit.Icon("handcuffs");
            hud.wrongIcon = HudKit.Icon("tombstone");
            hud.nobodyIcon = HudKit.Icon("ballot");
            hud.diedIcon = HudKit.Icon("skull");
            hud.survivedIcon = HudKit.Icon("check_circle");
            hud.ejectedIcon = HudKit.Icon("gavel");

            // Role reveal, centre: who you are and what to do.
            hud.roleReveal = HudKit.Banner(root, "RoleReveal", new Vector2(0.5f, 0.5f), new Vector2(0f, 80f), 640f,
                "innocent", RoleColor(MM_Role.Innocent), 96f, MM_ClientHUD.RoundStartKicker);

            BuildRoleChip(root, hud);

            // Body prompt, lower centre.
            RectTransform prompt = HudKit.KeyPrompt(root, "ReportPrompt", "E", "Report the body", Alarm, out hud.reportText, out hud.reportKey, 48f);
            prompt.anchorMin = prompt.anchorMax = prompt.pivot = new Vector2(0.5f, 0f);
            prompt.anchoredPosition = new Vector2(0f, 230f);
            HudKit.Icon(prompt, "dead_head", 26f, new Color(1f, 1f, 1f, 0.7f)).transform.SetSiblingIndex(prompt.childCount - 2);
            hud.reportPrompt = HudKit.Popup(prompt, false);

            BuildMeetingCard(root, hud);

            // Ejection outcome, centre.
            hud.ejection = HudKit.Banner(root, "Ejection", new Vector2(0.5f, 0.5f), new Vector2(0f, 60f), 620f,
                "ballot", Alarm, 88f, "The town has voted");

            BuildResults(root, hud);

            hud.bodyMarkerPrefab = marker;
            hud.mood = MoodPreset.WarmIndoor;
            AssignSounds(hud);

            UIKit.SavePrefab(root.gameObject, HudPath);
        }

        /// <summary>The round's sounds (Kenney CC0, see CREDITS.md) played through the shared GameSfx kit.</summary>
        static void AssignSounds(MM_ClientHUD hud) {
            hud.roleRevealSound = Sound(0.8f, OwnSounds + "role_reveal.ogg");
            hud.killSting = Sound(0.75f, OwnSounds + "kill_sting.ogg");
            hud.killSting.minDistance = 8f;
            hud.killSting.maxDistance = 55f;
            hud.bodyFoundSound = Sound(0.6f, Sounds + "Horn.mp3");
            hud.gavelSound = Sound(1f, OwnSounds + "gavel_1.ogg", OwnSounds + "gavel_2.ogg", OwnSounds + "gavel_3.ogg");
            hud.gavelSound.minInterval = 0.05f;
            hud.drumSound = Sound(0.8f, OwnSounds + "drum_1.ogg", OwnSounds + "drum_2.ogg", OwnSounds + "drum_3.ogg", OwnSounds + "drum_4.ogg", OwnSounds + "drum_5.ogg");
            hud.drumSound.minInterval = 0.02f;
            hud.drumSound.pitchJitterSemitones = 0.4f;
            hud.ejectionBell = Sound(0.85f, OwnSounds + "bell_toll.ogg");
        }

        static SfxSound Sound(float volume, params string[] paths) {
            AudioClip[] clips = new AudioClip[paths.Length];
            for (int i = 0; i < paths.Length; i++) {
                clips[i] = AssetDatabase.LoadAssetAtPath<AudioClip>(paths[i]);
                if (clips[i] == null)
                    throw new System.InvalidOperationException("Missing murder mystery sound: " + paths[i]);
            }
            return new SfxSound { clips = clips, volume = volume };
        }

        /// <summary>Bottom-left: your role as a small emblem and name, and the meetings you have left.</summary>
        static void BuildRoleChip(RectTransform root, MM_ClientHUD hud) {
            RectTransform chip = UIKit.Pinned(root, "RoleChip", Vector2.zero, new Vector2(20f, 200f), new Vector2(260f, 64f));
            HudKit.Glass(chip, 16f);
            UIKit.Row(chip.gameObject, 12f, new RectOffset(10, 16, 0, 0), TextAnchor.MiddleLeft);
            UIKit.FitContent(chip.gameObject, true, false);
            HudKit.Emblem(chip, "Emblem", "innocent", 46f, RoleColor(MM_Role.Innocent), out hud.roleChipDisc, out hud.roleChipIcon);

            RectTransform text = UIKit.Node(chip, "Text");
            UIKit.Column(text.gameObject, 0f, null, TextAnchor.MiddleLeft);
            UIKit.Text(text, "Caption", "Your role", UIText.Caption, UIColorRole.TextMuted);
            hud.roleChipName = UIKit.Text(text, "Role", "Innocent", UIText.Title);
            hud.roleChipName.fontSize = 20f;

            RectTransform divider = UIKit.Node(chip, "Divider");
            UIKit.Size(divider, 1f, 36f);
            UIKit.Paint(divider.gameObject.AddComponent<Image>(), UIColorRole.Stroke).raycastTarget = false;

            hud.meetingsLeft = HudKit.Chip(chip, "Meetings", "megaphone", "2", Alarm, 30f);
            hud.meetingsLeft.background.enabled = false;
            // Murderers only: the dagger is not ready yet (grace period or kill cooldown).
            hud.killCooldown = HudKit.Chip(chip, "KillCooldown", "dagger", "25s", RoleColor(MM_Role.Murderer), 30f);
            hud.killCooldown.background.enabled = false;
            hud.killCooldown.SetVisible(false);
            hud.roleChip = HudKit.Popup(chip, false);
        }

        /// <summary>Top centre, under the top bar: who found whom, who was seen nearby and the vote countdown.</summary>
        static void BuildMeetingCard(RectTransform root, MM_ClientHUD hud) {
            RectTransform card = HudKit.Card(root, "MeetingCard", new Vector2(0.5f, 1f), new Vector2(0f, -76f), new Vector2(640f, 150f));
            HudKit.AccentStripe(card, Alarm);
            UIKit.Row(card.gameObject, 18f, new RectOffset(22, 22, 20, 20), TextAnchor.MiddleLeft);
            UIKit.FitContent(card.gameObject, false, true);
            HudKit.Emblem(card, "Emblem", "megaphone", 76f, Alarm, out _, out _);

            RectTransform text = UIKit.Node(card, "Text");
            UIKit.Size(text, -1f, -1f, 1f);
            UIKit.Column(text.gameObject, 4f, null, TextAnchor.MiddleLeft);
            TextMeshProUGUI title = UIKit.Text(text, "Title", "Emergency meeting", UIText.Title);
            title.color = Color.Lerp(Alarm, Color.white, 0.25f);
            Object.DestroyImmediate(title.GetComponent<UIThemeColor>());
            hud.meetingBody = UIKit.Text(text, "Body", string.Empty, UIText.Body);
            hud.meetingBody.richText = true;
            hud.meetingBody.textWrappingMode = TextWrappingModes.Normal;
            hud.meetingSuspects = UIKit.Text(text, "Suspects", string.Empty, UIText.Small, UIColorRole.TextSecondary);
            hud.meetingSuspects.richText = true;
            hud.meetingSuspects.textWrappingMode = TextWrappingModes.Normal;

            RectTransform footer = UIKit.Node(text, "Footer");
            UIKit.Row(footer.gameObject, 8f, new RectOffset(0, 0, 6, 0), TextAnchor.MiddleLeft);
            hud.meetingTimer = HudKit.Chip(footer, "Timer", "timer", "Voting in 15s", Alarm, 30f, UIText.Small);
            hud.meetingTimer.label.font = UIKit.Theme.semiBoldFont;
            HudKit.Chip(footer, "Hint", "chat_bubble", "Discuss in chat", UIKit.Theme.textSecondary, 30f, UIText.Small);
            hud.meetingCard = card.GetComponent<RyanAssets.UI.GameHud.HudPopup>();
        }

        /// <summary>The end-of-round reveal: the winner, how it ended, and everyone's role and fate.</summary>
        static void BuildResults(RectTransform root, MM_ClientHUD hud) {
            RectTransform card = HudKit.Card(root, "Results", new Vector2(0.5f, 0.5f), new Vector2(0f, 10f), new Vector2(880f, 700f));
            hud.resultsStripe = HudKit.AccentStripe(card, RoleColor(MM_Role.Innocent), 5f);
            UIKit.Column(card.gameObject, 8f, new RectOffset(36, 36, 28, 26), TextAnchor.UpperCenter);
            UIKit.FitContent(card.gameObject, false, true);

            HudKit.Emblem(card, "Emblem", "laurels_trophy", 84f, RoleColor(MM_Role.Innocent), out hud.resultsDisc, out _);
            UIKit.Text(card, "Kicker", "Round over · roles revealed", UIText.Caption, UIColorRole.TextMuted, TextAlignmentOptions.Center);
            hud.resultsTitle = UIKit.Text(card, "Winner", "Innocents win", UIText.Display, UIColorRole.TextPrimary, TextAlignmentOptions.Center);
            Object.DestroyImmediate(hud.resultsTitle.GetComponent<UIThemeColor>());
            hud.resultsSummary = UIKit.Text(card, "Summary", string.Empty, UIText.Body, UIColorRole.TextSecondary, TextAlignmentOptions.Center);
            hud.resultsSummary.textWrappingMode = TextWrappingModes.Normal;

            RectTransform gap = UIKit.Node(card, "Gap");
            UIKit.Size(gap, -1f, 6f);
            hud.resultsRows = HudKit.Table(card, "Table", new[] {
                new HudKit.Column("Name"),
                new HudKit.Column("Role", 150f),
                new HudKit.Column("Fate", 150f),
                new HudKit.Column("Kills", 70f, TextAlignmentOptions.MidlineRight),
            }, 36f);
            hud.resultsMore = UIKit.Text(card, "More", string.Empty, UIText.Small, UIColorRole.TextMuted, TextAlignmentOptions.Center);

            RectTransform footer = UIKit.Node(card, "Footer");
            UIKit.Row(footer.gameObject, 0f, new RectOffset(0, 0, 8, 0), TextAnchor.MiddleCenter);
            hud.resultsTimer = HudKit.Chip(footer, "NextRound", "hourglass_bottom", "Next round in 12s", UIKit.Theme.textSecondary, 32f, UIText.Small);
            hud.results = card.GetComponent<RyanAssets.UI.GameHud.HudPopup>();
        }

        static void AddHudToStartScene() {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(HudPath);
            EditInStartScene(scene => {
                foreach (GameObject rootObject in scene.GetRootGameObjects())
                    if (PrefabUtility.GetCorrespondingObjectFromSource(rootObject) == prefab)
                        return false;
                GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                instance.name = "MM_HUD";
                return true;
            });
        }
    }
}
