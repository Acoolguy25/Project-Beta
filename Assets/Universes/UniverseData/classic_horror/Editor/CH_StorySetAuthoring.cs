using System.Collections.Generic;
using TMPro;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using Shot = Universes.UniverseData.classic_horror.CH_CutsceneSet.Shot;
using ActorPose = Universes.UniverseData.classic_horror.CH_CutsceneSet.ActorPose;

namespace Universes.UniverseData.classic_horror.Editor {
    /// <summary>
    /// <b>Ryan > Classic Horror > Build Story Set</b>: lays The Drowned Choir over the Flooded Grounds map and wires
    /// <see cref="CH_Map"/>. It moors the ferry at a jetty on the village's south shore, chooses five graves in the chapel
    /// graveyard and names them, raises Lily's marker by the water, sets the bell's altar before the pulpit, turns the
    /// village's wardrobes into hiding places, picks where the music box can hide, and places every cutscene's camera
    /// marks. Everything lives under one "Story Set" object, which is rebuilt from scratch each run.
    /// </summary>
    public static class CH_StorySetAuthoring {
        const string ScenePath = CH_Authoring.Root + "/Scenes/classic_horror_start.unity";
        const string Pack = CH_Authoring.Root + "/Flooded_Grounds/Prefabs/";
        const string BoatPrefab = Pack + "Props/Prop_Boat_A.prefab";
        const string JettyPrefab = Pack + "Buildings/Structures1/Struct_Docking_A.prefab";
        const string WardrobePrefab = Pack + "Props/Prop_Cabinet_A.prefab";
        const string AltarPrefab = Pack + "Props/Prop_SmallTable_A.prefab";
        const string MarkerStonePrefab = Pack + "Props/Prop_Gravestone_E.prefab";
        const string SetName = "Story Set";

        // The chapel (Church1 in the pack) stands with its doors to the south; the graveyard lies west of it.
        static readonly Vector3 ChapelDoor = new(648.5f, 17.5f, 477f);
        static readonly Vector3 PulpitHint = new(648.5f, 17.5f, 512.5f);
        static readonly Rect Graveyard = new(588f, 425f, 58f, 100f);
        // Extra wardrobes in the vestry end of the chapel, so Act III has somewhere to hide.
        static readonly (Vector3 position, float yaw)[] ChapelWardrobes = { (new(641.2f, 17.5f, 509.6f), 90f), (new(655.8f, 17.5f, 509.6f), -90f) };
        // Bedrooms where Lily's music box can turn up, off the routes the acts send you along.
        static readonly (Vector3 position, string label)[] MusicBoxHints = {
            (new(531.04f, 26.51f, 537.48f), "upstairs bedroom, manor"),
            (new(456.68f, 24.02f, 439.12f), "attic bedroom, western cottages"),
            (new(406.5f, 17.5f, 530f), "dormitory, flooded arcade"),
            (new(507.39f, 21f, 363.16f), "bedroom, south row"),
            (new(475.58f, 26.51f, 517.58f), "nursery, garden house")
        };
        static readonly string[] SingerDates = { "1903 - 1962", "1898 - 1962", "1931 - 1962", "1940 - 1962", "1912 - 1962" };
        static readonly Color EngravingColour = new(0.62f, 0.6f, 0.55f);

        static int mask;
        static float waterLevel;
        // The lake's visible surface by the harbour (the pack's water planes sit half a metre under the map's drowning level).
        static float lakeSurface;

        [MenuItem("Ryan/Classic Horror/Build Story Set")]
        public static void Build() {
            if (Application.isPlaying) throw new System.InvalidOperationException("Stop play mode before building the story set.");
            Scene previous = SceneManager.GetActiveScene();
            Scene scene = SceneManager.GetSceneByPath(ScenePath);
            bool opened = !scene.IsValid() || !scene.isLoaded;
            if (opened) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            try {
                SceneManager.SetActiveScene(scene);
                CH_Map map = FindMap(scene);
                mask = ~LayerMask.GetMask("Character", "LocalCharacter", "UI", "Ignore Raycast");
                waterLevel = map.waterLevel;
                lakeSurface = WaterSurface(ShoreHint);
                Transform old = map.transform.Find(SetName);
                if (old != null) Object.DestroyImmediate(old.gameObject);
                Physics.SyncTransforms();
                var set = new GameObject(SetName).transform;
                set.SetParent(map.transform, false);

                Harbour harbour = BuildHarbour(set, map);
                map.boatSlots = harbour.slots;
                map.boardingPoint = harbour.boarding;
                map.arrival = harbour.arrival;
                map.lilyMarker = BuildLilyMarker(set, harbour);
                map.graves = BuildGraves(set);
                map.altar = BuildAltar(set);
                map.hidingSpots = BuildHidingSpots(set, map);
                map.musicBoxSpots = BuildMusicBoxSpots(set);
                map.cutscenes = BuildCutscenes(set, map, harbour);

                map.voices = AssetDatabase.LoadAssetAtPath<CH_VoiceBank>(CH_VoiceAuthoring.BankPath) ?? CH_VoiceAuthoring.Build();
                var prefabs = new GameObject[System.Enum.GetValues(typeof(CH_ItemKind)).Length];
                for (int i = 0; i < prefabs.Length; i++) prefabs[i] = Load(CH_Authoring.ItemPrefabPath((CH_ItemKind)i));
                map.itemPrefabs = prefabs;
                map.slotMarkerPrefab = Load(CH_Authoring.SlotMarkerPath);
                map.graveCandlesPrefab = Load(CH_Authoring.GraveCandlesPath);
                map.priestActorPrefab = Load(CH_Authoring.PriestActorPath);
                EditorUtility.SetDirty(map);
                if (!map.IsConfigured) throw new System.InvalidOperationException("The story set is incomplete; see the warnings above.");
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                Debug.Log($"Classic Horror: story set built. Boat at {harbour.hull.position}, {map.hidingSpots.Length} hiding places, "
                    + $"{map.musicBoxSpots.Length} music box spots.");
            } finally {
                if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
                if (opened) EditorSceneManager.CloseScene(scene, true);
            }
        }

        static CH_Map FindMap(Scene scene) {
            foreach (GameObject root in scene.GetRootGameObjects()) {
                CH_Map found = root.GetComponentInChildren<CH_Map>(true);
                if (found != null) return found;
            }
            throw new System.InvalidOperationException("The horror scene has no CH_Map.");
        }

        static GameObject Load(string path) {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (asset == null) throw new System.InvalidOperationException($"{path} is missing: run Ryan > Classic Horror > Rebuild Presentation Prefabs first.");
            return asset;
        }

        // ---- The harbour -------------------------------------------------------------------------------------------------

        struct Harbour {
            /// <summary>The middle of the moored boat, on the water line.</summary>
            public Transform hull;
            public Transform boarding, shore, stern, arrival;
            public Transform[] slots;
            /// <summary>Out over the water, and along the jetty's side, towards the boat, on the horizontal plane.</summary>
            public Vector3 outward, side;
        }

        /// <summary>
        /// Runs a jetty out from Hollowmere's south shore into the open lake, beside the fishing dock and in sight of the
        /// wreck, and moors the ferry alongside. The boat's four places (fuel at the engine, the toolbox, the lantern at
        /// the bow, the bell) sit on the hull's boards on the jetty side, within reach of someone standing on it.
        /// </summary>
        static Harbour BuildHarbour(Transform set, CH_Map map) {
            Transform group = Group(set, "Harbour");
            if (!FindShore(ShoreHint, out Vector3 shore, out Vector3 outward))
                throw new System.InvalidOperationException($"No open water within {MaxShoreDistance} m of {ShoreHint} for the ferry. " + DescribeShore(ShoreHint));
            Vector3 side = Vector3.Cross(Vector3.up, outward).normalized;
            float water = lakeSurface;

            GameObject jetty = Instance(JettyPrefab, group, "Ferry jetty");
            Vector2 jettySize = AlignLongAxis(jetty, outward, out Bounds jettyBounds);
            Vector3 jettyCentre = shore + outward * (jettySize.x * 0.5f - 1.2f);
            PlaceBounds(jetty, jettyBounds, jettyCentre, lakeSurface);
            // Lift the planks (not the posts, which stand proud of them) above the map's drowning line: the scene's
            // deep-water NavMesh exclusion removes everything under it, and Crane must be able to follow anyone out
            // along the jetty.
            Physics.SyncTransforms();
            float deck = Mathf.Max(GroundHeight(shore) + 0.05f, waterLevel + JettyFreeboard);
            if (SolidBelow(new Vector3(jettyCentre.x, lakeSurface + 4f, jettyCentre.z), 8f, out RaycastHit plank))
                jetty.transform.position += Vector3.up * (deck - plank.point.y);
            Physics.SyncTransforms();

            GameObject boat = Instance(BoatPrefab, group, "Ferry");
            // Its box collider's flat top would bake into an island of NavMesh Crane could rise onto.
            boat.AddComponent<NavMeshModifier>().ignoreFromBuild = true;
            Vector2 boatSize = AlignLongAxis(boat, outward, out Bounds boatBounds);
            float boatLength = boatSize.x, boatWidth = boatSize.y;
            Vector3 boatCentre = shore + outward * Mathf.Max(jettySize.x * 0.55f, boatLength * 0.6f) + side * (jettySize.y * 0.5f + boatWidth * 0.5f + 0.15f);
            // A third of the hull under water.
            PlaceBounds(boat, boatBounds, boatCentre, water + boatBounds.size.y * 0.65f);
            Physics.SyncTransforms();
            Bounds hull = WorldBounds(boat);
            if (TerrainHeight(boatCentre) > hull.min.y + 0.2f) Debug.LogWarning($"Classic Horror: the lake bed comes up through the ferry's hull at {boatCentre}; check the harbour.");

            // The places on the boat, nearest the jetty: the stern (engine) out on the lake, the bow towards the shore.
            Vector3 jettySide = boatCentre - side * (boatWidth * 0.22f);
            Vector3 Along(float t, float outboard = 0f) {
                Vector3 spot = jettySide + outward * (boatLength * t) + side * (boatWidth * outboard);
                return new Vector3(spot.x, HullFloor(boat, spot, hull, water + 0.25f), spot.z);
            }
            var slots = new Transform[4];
            slots[0] = Mark(group, "Boat: fuel (engine)", Along(0.3f), outward);
            slots[1] = Mark(group, "Boat: toolbox", Along(0.05f), outward);
            slots[2] = Mark(group, "Boat: lantern (bow)", Along(-0.28f), outward);
            slots[3] = Mark(group, "Boat: the bell", Along(0.17f, 0.18f), outward);
            Transform boarding = Mark(group, "Boarding point", new Vector3(jettyCentre.x, deck, jettyCentre.z), outward);
            Transform shoreMark = Mark(group, "Shore", shore, outward);
            Transform stern = Mark(group, "Engine", Along(0.4f) + Vector3.up * 0.2f, outward);
            // The survivors of the wreck start at the foot of the jetty, facing the village.
            Vector3 arrivalSpot = shore - outward * 2.5f;
            Transform arrival = Mark(group, "Arrival", new Vector3(arrivalSpot.x, GroundHeight(arrivalSpot), arrivalSpot.z), -outward);
            if (!NavMesh.SamplePosition(arrival.position, out _, 1.5f, NavMesh.AllAreas))
                Debug.LogWarning($"Classic Horror: the arrival at {arrival.position} is off the NavMesh; rebake it after building the set.");
            Debug.Log($"Classic Horror: ferry moored at {boatCentre} ({boatLength:0.0} x {boatWidth:0.0} m), jetty {jettySize.x:0.0} m from {shore} "
                + $"heading {outward}, water {water:0.00}.");
            Transform hullMark = Mark(group, "Hull centre", new Vector3(boatCentre.x, water, boatCentre.z), outward);
            return new Harbour { hull = hullMark, boarding = boarding, shore = shoreMark, stern = stern, slots = slots, arrival = arrival, outward = outward, side = side };
        }

        // The village's south-west shore, a few steps west of the fishing dock: the open lake and the wreck lie west of it.
        static readonly Vector3 ShoreHint = new(597f, 16.5f, 365f);
        const float MaxShoreDistance = 30f;
        /// <summary>Height of the jetty's planks above the map's drowning line (<see cref="CH_Map.waterLevel"/>).</summary>
        const float JettyFreeboard = 0.15f;

        /// <summary>The closest dry, open point to <paramref name="from"/> with deep water starting just beyond it.</summary>
        static bool FindShore(Vector3 from, out Vector3 shore, out Vector3 outward) {
            shore = default;
            outward = default;
            float best = float.MaxValue;
            for (int angle = 0; angle < 360; angle += 6) {
                Vector3 direction = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
                for (float distance = 2f; distance < MaxShoreDistance; distance += 1f) {
                    Vector3 point = from + direction * distance;
                    if (!IsWater(point)) continue;
                    Vector3 edge = from + direction * (distance - 1.5f);
                    // Room for the jetty and the hull beyond the edge, and dry, unbuilt land to step off from.
                    bool open = IsWater(point + direction * 6f) && IsWater(point + direction * 10f) && IsWater(point + direction * 14f)
                        && IsWater(point + direction * 6f + Vector3.Cross(Vector3.up, direction) * 3f)
                        && ClearForHarbour(edge, direction)
                        && GroundHeight(edge) > lakeSurface - 0.3f && !Blocked(edge) // the marshy bank may sit a hand under the surface
                        && NavMesh.SamplePosition(edge, out _, 2f, NavMesh.AllAreas); // players have to walk there
                    // A channel or puddle on the way is not the shore: keep looking further along this bearing.
                    if (!open) continue;
                    if (distance < best) {
                        best = distance;
                        shore = new Vector3(edge.x, GroundHeight(edge), edge.z);
                        outward = direction;
                    }
                    break;
                }
            }
            return best < float.MaxValue;
        }

        /// <summary>
        /// Nothing solid (a tree trunk, a post, a wall) stands where the jetty and the moored boat would go: a box from
        /// the water line to head height, 16 m out from <paramref name="edge"/> and wide enough for both.
        /// </summary>
        static bool ClearForHarbour(Vector3 edge, Vector3 direction) {
            Vector3 side = Vector3.Cross(Vector3.up, direction);
            Vector3 centre = new Vector3(edge.x, lakeSurface + 1.6f, edge.z) + direction * 7.5f + side * 1.5f;
            var halfExtents = new Vector3(3.6f, 1.5f, 8.5f);
            foreach (Collider hit in Physics.OverlapBox(centre, halfExtents, Quaternion.LookRotation(direction), mask, QueryTriggerInteraction.Ignore))
                if (hit is not TerrainCollider && !hit.name.StartsWith("WaterPlane")) return false;
            return true;
        }

        /// <summary>What the shore search saw around <paramref name="from"/>, for the error when it finds nowhere to moor.</summary>
        static string DescribeShore(Vector3 from) {
            float lowest = float.MaxValue;
            Vector3 lowestAt = from;
            var blockers = new HashSet<string>();
            for (int angle = 0; angle < 360; angle += 6) {
                Vector3 direction = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
                for (float distance = 2f; distance < MaxShoreDistance; distance += 1f) {
                    Vector3 point = from + direction * distance;
                    float height = TerrainHeight(point);
                    if (height < lowest) { lowest = height; lowestAt = point; }
                    Vector3 top = new(point.x, lakeSurface + 12f, point.z);
                    if (height < lakeSurface - LakeDraught && Physics.Raycast(top, Vector3.down, out RaycastHit hit, 11.85f, mask, QueryTriggerInteraction.Ignore)
                        && hit.collider.GetComponent<TerrainCollider>() == null && blockers.Count < 8) blockers.Add(hit.collider.name);
                }
            }
            return $"Terrains: {Terrain.activeTerrains.Length}; lowest terrain {lowest:0.0} m at {lowestAt} (lake surface {lakeSurface:0.0}, needs below {lakeSurface - LakeDraught:0.0}); "
                + $"water blocked by: {(blockers.Count > 0 ? string.Join(", ", blockers) : "nothing")}.";
        }

        /// <summary>The lake's visible surface under <paramref name="point"/> (the pack's water planes), or the map's water level.</summary>
        static float WaterSurface(Vector3 point) {
            foreach (Renderer renderer in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)) {
                if (!renderer.name.StartsWith("WaterPlane")) continue;
                Bounds bounds = renderer.bounds;
                if (point.x >= bounds.min.x && point.x <= bounds.max.x && point.z >= bounds.min.z && point.z <= bounds.max.z) return bounds.max.y;
            }
            return waterLevel;
        }

        /// <summary>
        /// The boards inside the hull under <paramref name="spot"/>, so items sit in the boat rather than on top of its
        /// box collider. The hull's own mesh is ray cast through a temporary mesh collider.
        /// </summary>
        static float HullFloor(GameObject boat, Vector3 spot, Bounds bounds, float fallback) {
            MeshFilter detail = null;
            foreach (MeshFilter part in boat.GetComponentsInChildren<MeshFilter>())
                if (part.sharedMesh != null && (detail == null || part.sharedMesh.vertexCount > detail.sharedMesh.vertexCount)) detail = part;
            if (detail == null) return fallback;
            var probe = detail.gameObject.AddComponent<MeshCollider>();
            try {
                probe.sharedMesh = detail.sharedMesh;
                Physics.SyncTransforms();
                var ray = new Ray(new Vector3(spot.x, bounds.max.y + 1f, spot.z), Vector3.down);
                return probe.Raycast(ray, out RaycastHit hit, bounds.size.y + 2f) ? hit.point.y : fallback;
            } finally {
                Object.DestroyImmediate(probe);
            }
        }

        /// <summary>Lily's marker by the water, a few steps from the jetty: a small stone and her name.</summary>
        static Transform BuildLilyMarker(Transform set, Harbour harbour) {
            Transform group = Group(set, "Lily's marker");
            Vector3 spot = harbour.shore.position - harbour.side * 3.2f - harbour.outward * 1.5f;
            // Up the bank until the stone stands on dry ground rather than in the reeds.
            for (int step = 0; step < 8 && GroundHeight(spot) < lakeSurface + 0.2f; step++) spot -= harbour.outward;
            spot = new Vector3(spot.x, GroundHeight(spot), spot.z);
            Vector3 facing = harbour.shore.position - spot;
            facing.y = 0f;
            GameObject stone = Instance(MarkerStonePrefab, group, "Lily's stone");
            stone.transform.SetPositionAndRotation(spot, Quaternion.LookRotation(-facing.normalized));
            Engrave(group, "LILY CRANE\n1954 - 1962\n<size=60%>she waited for the ferry</size>", spot, facing.normalized, 0.55f);
            return Mark(group, "Lily's marker (slot)", spot + facing.normalized * 0.7f, facing);
        }

        // ---- The graveyard -----------------------------------------------------------------------------------------------

        /// <summary>Five gravestones spread across the chapel graveyard, one per singer, each engraved with their name.</summary>
        static Transform[] BuildGraves(Transform set) {
            Transform group = Group(set, "Singers' graves");
            var stones = new List<Transform>();
            foreach (Transform candidate in Object.FindObjectsByType<Transform>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)) {
                if (!candidate.name.StartsWith("Prop_Gravestone") || PrefabUtility.GetNearestPrefabInstanceRoot(candidate) != candidate.gameObject) continue;
                Vector3 p = candidate.position;
                if (Graveyard.Contains(new Vector2(p.x, p.z)) && p.y > waterLevel - 0.2f) stones.Add(candidate);
            }
            if (stones.Count < CH_Story.SingerCount) throw new System.InvalidOperationException($"Only {stones.Count} gravestones in the chapel graveyard.");
            List<Transform> chosen = Spread(stones, CH_Story.SingerCount, ChapelDoor);
            var graves = new Transform[CH_Story.SingerCount];
            for (int singer = 0; singer < graves.Length; singer++) {
                Transform stone = chosen[singer];
                Vector3 front = OpenSide(stone.position + Vector3.up * 0.4f, stone.forward, stone.right);
                Vector3 foot = stone.position + front * 0.75f;
                foot.y = GroundHeight(foot);
                string name = CH_Story.Singers[singer];
                Engrave(group, $"{name.ToUpperInvariant()}\n{SingerDates[singer]}", stone.position + front * 0.2f + Vector3.up * 0.1f, front, 0.65f);
                graves[singer] = Mark(group, $"{name}'s grave", foot, -front);
            }
            return graves;
        }

        /// <summary>Carved lettering in front of a stone: dim, only readable up close or in the beam of a flashlight.</summary>
        static void Engrave(Transform parent, string text, Vector3 face, Vector3 front, float height) {
            var label = new GameObject("Engraving");
            label.transform.SetParent(parent, false);
            label.transform.SetPositionAndRotation(face + Vector3.up * height, Quaternion.LookRotation(-front));
            var tmp = label.AddComponent<TextMeshPro>();
            tmp.text = text;
            tmp.fontSize = 1.1f;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = EngravingColour;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            tmp.rectTransform.sizeDelta = new Vector2(1.2f, 0.6f);
            tmp.characterSpacing = 6f;
        }

        // ---- The chapel ----------------------------------------------------------------------------------------------------

        /// <summary>A small table before the pulpit, where the bell waits. Its forward points down the nave, to the doors.</summary>
        static Transform BuildAltar(Transform set) {
            Transform group = Group(set, "Altar");
            Transform pulpit = Nearest("Prop_Pulpit_A", PulpitHint, 6f);
            Vector3 origin = pulpit != null ? pulpit.position : PulpitHint;
            Vector3 nave = ChapelDoor - origin;
            nave.y = 0f;
            nave.Normalize();
            Vector3 spot = origin + nave * 1.7f;
            spot.y = GroundHeight(spot);
            GameObject table = Instance(AltarPrefab, group, "Altar table");
            table.transform.SetPositionAndRotation(spot, Quaternion.LookRotation(nave));
            AddObstacle(table);
            // The top of the table itself: a ray from above could stop on a beam or the chapel's ceiling instead.
            return Mark(group, "Altar (the bell)", new Vector3(spot.x, TopOf(table, spot.y), spot.z), nave);
        }

        static float TopOf(GameObject instance, float fallback) {
            float top = fallback;
            foreach (Renderer part in instance.GetComponentsInChildren<Renderer>()) top = Mathf.Max(top, part.bounds.max.y);
            return top;
        }

        // ---- Hiding places -------------------------------------------------------------------------------------------------

        /// <summary>
        /// Every ground-floor wardrobe in the village that a person can stand in front of, plus two in the chapel vestry.
        /// The investigator stands just inside the doors; the priest opens them from where they stepped in.
        /// </summary>
        static CH_HidingSpot[] BuildHidingSpots(Transform set, CH_Map map) {
            Transform group = Group(set, "Hiding places");
            var wardrobes = new List<Transform>();
            foreach (Transform candidate in Object.FindObjectsByType<Transform>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)) {
                if (!candidate.name.StartsWith("Prop_Cabinet") || PrefabUtility.GetNearestPrefabInstanceRoot(candidate) != candidate.gameObject) continue;
                if (candidate.position.y < waterLevel - 0.2f || candidate.position.y > waterLevel + 4.5f) continue;
                if (Vector3.Distance(candidate.position, map.arrival.position) > 260f) continue;
                wardrobes.Add(candidate);
            }
            foreach (var (position, yaw) in ChapelWardrobes) {
                GameObject added = Instance(WardrobePrefab, group, "Vestry wardrobe");
                added.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
                AddObstacle(added);
                wardrobes.Add(added.transform);
            }
            Physics.SyncTransforms();
            var spots = new List<CH_HidingSpot>();
            foreach (Transform wardrobe in wardrobes) {
                Bounds body = WorldBounds(wardrobe.gameObject);
                Vector3 centre = new(body.center.x, body.min.y, body.center.z);
                Vector3 front = OpenSide(centre + Vector3.up * 1f, wardrobe.forward, wardrobe.right, out float clearance);
                if (clearance < 1.3f) continue;
                Vector3 exit = centre + front * (Extent(body, front) + 0.75f);
                if (!NavMesh.SamplePosition(exit, out _, 1.5f, NavMesh.AllAreas)) continue;
                var spot = new GameObject("Hide: " + wardrobe.name).AddComponent<CH_HidingSpot>();
                spot.transform.SetParent(group, false);
                spot.transform.SetPositionAndRotation(centre, Quaternion.LookRotation(front));
                spot.inside = Mark(spot.transform, "Inside", centre + front * (Extent(body, front) - 0.15f), -front);
                spot.exit = Mark(spot.transform, "Exit", exit, -front);
                spot.label = "wardrobe";
                spots.Add(spot);
            }
            if (spots.Count < 4) Debug.LogWarning($"Classic Horror: only {spots.Count} hiding places were usable.");
            return spots.ToArray();
        }

        // ---- The music box -------------------------------------------------------------------------------------------------

        static Transform[] BuildMusicBoxSpots(Transform set) {
            Transform group = Group(set, "Music box spots");
            var spots = new List<Transform>();
            foreach (var (position, label) in MusicBoxHints) {
                if (!Physics.Raycast(position + Vector3.up * 1.6f, Vector3.down, out RaycastHit surface, 3f, mask, QueryTriggerInteraction.Ignore)) continue;
                if (!NavMesh.SamplePosition(surface.point, out _, 2.5f, NavMesh.AllAreas)) continue;
                spots.Add(Mark(group, "Music box: " + label, surface.point, Vector3.forward));
            }
            if (spots.Count == 0) Debug.LogWarning("Classic Horror: no music box spot was reachable; it will use a search location.");
            return spots.ToArray();
        }

        // ---- Cutscenes -----------------------------------------------------------------------------------------------------

        /// <summary>
        /// The camera marks for each scene, placed from the harbour, the chapel and the graveyard so they follow the set if
        /// it moves. Shot lengths match the voice cues in <see cref="CH_Story.CutsceneLines"/>.
        /// </summary>
        static CH_CutsceneSet BuildCutscenes(Transform set, CH_Map map, Harbour harbour) {
            Transform group = Group(set, "Cutscenes");
            var cutscenes = group.gameObject.AddComponent<CH_CutsceneSet>();
            Vector3 up = Vector3.up, outward = harbour.outward, side = harbour.side;
            Vector3 boat = harbour.hull.position, shore = harbour.shore.position, deck = harbour.boarding.position;
            // Diagonally away from the jetty, past the boat: the widest stretch of open lake, where the ferry leaves.
            Vector3 open = (outward + side).normalized;
            Vector3 village = Grounded(shore - outward * 38f);
            // Crane watches from out on the lake, waist deep, where nothing on the shore hides him.
            Vector3 watcher = new Vector3(deck.x, lakeSurface - 0.9f, deck.z) + outward * 18f + side * 14f;
            Vector3 chapel = ChapelDoor, altar = map.altar.position;
            Vector3 graves = Vector3.zero, graveFacing = Vector3.zero;
            foreach (Transform grave in map.graves) { graves += grave.position / map.graves.Length; graveFacing += grave.forward; }
            graveFacing.y = 0f;
            graveFacing = graveFacing.sqrMagnitude > 1e-4f ? graveFacing.normalized : Vector3.forward;
            Vector3 boatWater = new(boat.x, lakeSurface, boat.z);
            Vector3 riseFrom = boatWater - side * 1.9f - up * 2.2f, riseTo = boatWater - side * 1.9f;

            // A stand-in priest's mark faces the way he looks; camera marks are aimed by their shot's look-at instead.
            Transform M(string scene, string name, Vector3 position, Vector3? facing = null) => Mark(group, scene + ": " + name, position, facing ?? Vector3.forward);

            cutscenes.scenes = new[] {
                new CH_CutsceneSet.Scene {
                    phase = CH_Phase.Prologue,
                    titleCard = "THE DROWNED CHOIR\nHollowmere, the night of the flood",
                    shots = new[] {
                        Take(M("Prologue", "1 from", boatWater + open * 26f + up * 3f), M("Prologue", "1 to", boatWater + open * 14f + up * 2.2f), M("Prologue", "1 look", village + up * 5f), 8.2f, 50f),
                        Take(M("Prologue", "2 from", deck + up * 1.8f + side * 2.2f + outward * 1.5f), M("Prologue", "2 to", deck + up * 1.7f + side * 1.4f - outward * 0.5f), M("Prologue", "2 look", village + up * 3f), 7.2f, 55f),
                        Take(M("Prologue", "3 from", harbour.stern.position + up * 1.1f - side * 1.6f + outward * 0.8f), null, harbour.stern, 6.2f, 45f, shake: 0.7f, cue: "thud"),
                        Take(M("Prologue", "4 from", deck + up * 1.7f + outward * 1f), M("Prologue", "4 to", deck + up * 1.7f - outward * 2f), M("Prologue", "4 look", watcher + up * 1.6f), 6.2f, 28f,
                            actorMark: M("Prologue", "4 priest", watcher, deck - watcher), actorPose: ActorPose.Stand, cue: "bell"),
                        Take(M("Prologue", "5 from", deck + up * 2.2f + side * 3f + outward * 2f), M("Prologue", "5 to", deck + up * 7f + side * 6f + outward * 9f), M("Prologue", "5 look", deck + up * 1f), 9.2f, 55f)
                    }
                },
                new CH_CutsceneSet.Scene {
                    phase = CH_Phase.Taking,
                    shots = new[] {
                        Take(M("Taking", "1 from", boat + side * 4f + up * 2f), M("Taking", "1 to", boat + side * 3.4f + up * 1.8f + outward * 1f), M("Taking", "1 look", boat + up * 0.8f), 6f, 50f, shake: 0.35f, cue: "engine"),
                        Take(M("Taking", "2 from", deck + up * 1.6f + side * 0.8f), M("Taking", "2 to", deck + up * 1.5f + side * 1.4f), M("Taking", "2 look", riseTo + up * 1.6f), 5.2f, 38f,
                            actorMark: M("Taking", "2 priest from", riseFrom), actorTarget: M("Taking", "2 priest to", riseTo), actorPose: ActorPose.Rise, shake: 0.5f, cue: "splash"),
                        Take(M("Taking", "3 from", boat + side * 2.6f + up * 1.5f), null, M("Taking", "3 look", boat + up * 1f), 3.8f, 42f,
                            actorMark: M("Taking", "3 priest from", riseTo), actorTarget: M("Taking", "3 priest to", boatWater - side * 0.4f), actorPose: ActorPose.Lunge, shake: 0.8f, cue: "scream"),
                        Take(M("Taking", "4 from", chapel - Vector3.forward * 20f + up * 3f), M("Taking", "4 to", chapel - Vector3.forward * 12f + up * 2.5f), M("Taking", "4 look", chapel + up * 4f), 6f, 50f, cue: "bell"),
                        Take(M("Taking", "5 from", graves - graveFacing * 9f + up * 3f), M("Taking", "5 to", graves - graveFacing * 5f + up * 2f), M("Taking", "5 look", graves + up * 0.6f), 8f, 50f)
                    }
                },
                new CH_CutsceneSet.Scene {
                    phase = CH_Phase.ChapelOpens,
                    shots = new[] {
                        Take(M("Chapel", "from", chapel - Vector3.forward * 6f + up * 1.8f), M("Chapel", "to", chapel + Vector3.forward * 9f + up * 1.8f), M("Chapel", "look", altar + up * 0.6f), 10f, 55f, shake: 0.25f, cue: "doors")
                    }
                },
                new CH_CutsceneSet.Scene {
                    phase = CH_Phase.Departure,
                    shots = new[] {
                        Take(M("Departure", "1 from", boatWater + open * 5f + up * 1.5f), M("Departure", "1 to", boatWater + open * 24f + up * 3f), M("Departure", "1 look", deck + up * 2f), 7f, 50f, shake: 0.2f, cue: "engine"),
                        Take(M("Departure", "2 from", boatWater + open * 24f + up * 3f), M("Departure", "2 to", boatWater + open * 40f + up * 4f), M("Departure", "2 look", deck + up * 1.6f), 9f, 32f,
                            actorMark: M("Departure", "2 priest", deck, open), actorPose: ActorPose.Stand, cue: "bell")
                    }
                }
            };
            return cutscenes;
        }

        static Shot Take(Transform from, Transform to, Transform lookAt, float seconds, float fieldOfView, Transform actorMark = null,
            Transform actorTarget = null, ActorPose actorPose = ActorPose.Hidden, float shake = 0f, string cue = null) => new() {
            from = from, to = to, lookAt = lookAt, seconds = seconds, fieldOfView = fieldOfView, actorMark = actorMark,
            actorTarget = actorTarget, actorPose = actorMark != null ? actorPose : ActorPose.Hidden, shake = shake, cue = cue ?? string.Empty
        };

        // ---- Helpers -------------------------------------------------------------------------------------------------------

        static Transform Group(Transform parent, string name) {
            var group = new GameObject(name).transform;
            group.SetParent(parent, false);
            return group;
        }

        static Transform Mark(Transform parent, string name, Vector3 position, Vector3 forward) {
            var mark = new GameObject(name).transform;
            mark.SetParent(parent, false);
            forward.y = 0f;
            mark.SetPositionAndRotation(position, forward.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(forward) : Quaternion.identity);
            return mark;
        }

        static GameObject Instance(string path, Transform parent, string name) {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(Load(path), parent);
            instance.name = name;
            instance.transform.localScale = Vector3.one;
            return instance;
        }

        /// <summary>A carving NavMeshObstacle, so the priest walks round furniture added after the bake.</summary>
        static void AddObstacle(GameObject target) {
            Bounds body = WorldBounds(target);
            var obstacle = target.AddComponent<NavMeshObstacle>();
            obstacle.carving = true;
            obstacle.shape = NavMeshObstacleShape.Box;
            obstacle.center = target.transform.InverseTransformPoint(body.center);
            obstacle.size = target.transform.InverseTransformVector(body.size);
            obstacle.size = new Vector3(Mathf.Abs(obstacle.size.x), Mathf.Abs(obstacle.size.y), Mathf.Abs(obstacle.size.z));
        }

        /// <summary>
        /// Turns <paramref name="target"/> so its longest horizontal side runs along <paramref name="axis"/>. Returns its
        /// length and width (measured unrotated, so a diagonal heading does not inflate them) and its bounds at the origin.
        /// </summary>
        static Vector2 AlignLongAxis(GameObject target, Vector3 axis, out Bounds atOrigin) {
            target.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            Bounds local = WorldBounds(target);
            bool longX = local.size.x > local.size.z;
            float yaw = Mathf.Atan2(axis.x, axis.z) * Mathf.Rad2Deg + (longX ? -90f : 0f);
            target.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            atOrigin = WorldBounds(target);
            return longX ? new Vector2(local.size.x, local.size.z) : new Vector2(local.size.z, local.size.x);
        }

        /// <summary>Moves <paramref name="target"/> so its bounds are centred on <paramref name="centre"/> with their top at <paramref name="top"/>.</summary>
        static void PlaceBounds(GameObject target, Bounds atOrigin, Vector3 centre, float top) {
            Vector3 offset = atOrigin.center - target.transform.position;
            target.transform.position = new Vector3(centre.x - offset.x, top - atOrigin.max.y + target.transform.position.y, centre.z - offset.z);
        }

        static Bounds WorldBounds(GameObject root) {
            var renderers = root.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return new Bounds(root.transform.position, Vector3.one);
            Bounds bounds = renderers[0].bounds;
            foreach (Renderer part in renderers) bounds.Encapsulate(part.bounds);
            return bounds;
        }

        static float Extent(Bounds bounds, Vector3 direction) =>
            Mathf.Abs(direction.x) * bounds.extents.x + Mathf.Abs(direction.z) * bounds.extents.z;

        /// <summary>Of the four horizontal sides along <paramref name="forward"/> and <paramref name="right"/>, the one with the most open space.</summary>
        static Vector3 OpenSide(Vector3 from, Vector3 forward, Vector3 right) => OpenSide(from, forward, right, out _);

        static Vector3 OpenSide(Vector3 from, Vector3 forward, Vector3 right, out float clearance) {
            Vector3 best = forward;
            clearance = -1f;
            foreach (Vector3 direction in new[] { forward, -forward, right, -right }) {
                Vector3 flat = new Vector3(direction.x, 0f, direction.z).normalized;
                // Start a little outside the furniture itself.
                Vector3 start = from + flat * 0.6f;
                float free = Physics.Raycast(start, flat, out RaycastHit hit, 3f, mask, QueryTriggerInteraction.Ignore) ? hit.distance : 3f;
                if (free <= clearance) continue;
                clearance = free;
                best = flat;
            }
            clearance += 0.6f;
            return best;
        }

        static Transform Nearest(string prefix, Vector3 near, float radius) {
            Transform best = null;
            float bestDistance = radius;
            foreach (Transform candidate in Object.FindObjectsByType<Transform>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)) {
                if (!candidate.name.StartsWith(prefix)) continue;
                float distance = Vector3.Distance(candidate.position, near);
                if (distance >= bestDistance) continue;
                bestDistance = distance;
                best = candidate;
            }
            return best;
        }

        /// <summary><paramref name="count"/> of <paramref name="candidates"/>, each as far as possible from those already taken, starting nearest <paramref name="start"/>.</summary>
        static List<Transform> Spread(List<Transform> candidates, int count, Vector3 start) {
            var chosen = new List<Transform>();
            Transform first = candidates[0];
            foreach (Transform candidate in candidates)
                if (Vector3.Distance(candidate.position, start) < Vector3.Distance(first.position, start)) first = candidate;
            chosen.Add(first);
            while (chosen.Count < count) {
                Transform best = null;
                float bestScore = -1f;
                foreach (Transform candidate in candidates) {
                    if (chosen.Contains(candidate)) continue;
                    float nearest = float.MaxValue;
                    foreach (Transform taken in chosen) nearest = Mathf.Min(nearest, Vector3.Distance(candidate.position, taken.position));
                    if (nearest <= bestScore) continue;
                    bestScore = nearest;
                    best = candidate;
                }
                chosen.Add(best);
            }
            return chosen;
        }

        /// <summary>The lake bed is deeper than this under open water, so a hull floats clear of it.</summary>
        const float LakeDraught = 1f;

        /// <summary>The terrain under <paramref name="point"/> is below the lake by more than a hull's draught.</summary>
        static bool IsWater(Vector3 point) => TerrainHeight(point) < lakeSurface - LakeDraught && !Blocked(point);

        /// <summary>Something built (a pier, a wall, a house) stands at <paramref name="point"/> above the water line.</summary>
        static bool Blocked(Vector3 point) {
            Vector3 top = new(point.x, lakeSurface + 12f, point.z);
            return SolidBelow(top, 12f - 0.15f, out RaycastHit hit) && hit.collider.GetComponent<TerrainCollider>() == null;
        }

        static float TerrainHeight(Vector3 point) {
            foreach (Terrain terrain in Terrain.activeTerrains) {
                Vector3 local = point - terrain.transform.position;
                Vector3 size = terrain.terrainData.size;
                if (local.x < 0f || local.z < 0f || local.x > size.x || local.z > size.z) continue;
                return terrain.SampleHeight(point) + terrain.transform.position.y;
            }
            return float.MinValue;
        }

        /// <summary>The first surface below <paramref name="point"/> (from 3 m above it), or the terrain.</summary>
        static float GroundHeight(Vector3 point) =>
            SolidBelow(point + Vector3.up * 3f, 12f, out RaycastHit hit) ? hit.point.y : TerrainHeight(point);

        /// <summary>The first hit straight down from <paramref name="origin"/> that is not the lake's surface (its planes have colliders).</summary>
        static bool SolidBelow(Vector3 origin, float distance, out RaycastHit solid) {
            solid = default;
            float nearest = float.MaxValue;
            foreach (RaycastHit hit in Physics.RaycastAll(origin, Vector3.down, distance, mask, QueryTriggerInteraction.Ignore)) {
                if (hit.collider.name.StartsWith("WaterPlane") || hit.distance >= nearest) continue;
                nearest = hit.distance;
                solid = hit;
            }
            return nearest < float.MaxValue;
        }

        static Vector3 Grounded(Vector3 point) {
            float top = Mathf.Max(TerrainHeight(point), waterLevel) + 30f;
            return Physics.Raycast(new Vector3(point.x, top, point.z), Vector3.down, out RaycastHit hit, 60f, mask, QueryTriggerInteraction.Ignore)
                ? hit.point : new Vector3(point.x, Mathf.Max(TerrainHeight(point), waterLevel), point.z);
        }
    }
}
