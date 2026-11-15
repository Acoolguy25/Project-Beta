using System;
using System.Collections.Generic;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Universes.UniverseData.war_valley.Shared;

namespace Universes.UniverseData.war_valley.Editor {
    /// <summary>
    /// Every number that shapes the War Valley map. The defaults are the shipped map; change a field
    /// and run Ryan/War Valley/Rebuild Map to regenerate it. All distances are metres in terrain space.
    /// </summary>
    [Serializable]
    public sealed class WV_MapRecipe {
        [Header("Layout")]
        [Tooltip("Seed for every noise field and every scattered tree, so a rebuild is reproducible.")]
        public int Seed = 1847;
        [Tooltip("Radius of the flat basin around the flag where the main base is built.")]
        public float BasinRadius = 175f;
        [Tooltip("How far the basin's edge wanders in and out, so it is not a perfect circle.")]
        public float BasinWobble = 28f;
        [Tooltip("Half the width of each flat approach valley.")]
        public float ValleyHalfWidth = 42f;
        [Tooltip("Width of the slope from a valley floor up to the hills beside it.")]
        public float ShoulderWidth = 130f;
        [Tooltip("How far each approach road swings left and right on its way to the base.")]
        public float RoadBend = 48f;

        [Header("Relief")]
        [Tooltip("Lowest height of the rolling hills between the approaches.")]
        public float HillBase = 10f;
        [Tooltip("Extra height the tallest rolling hills reach.")]
        public float HillRange = 52f;
        [Tooltip("Distance from the map edge over which the mountain rim rises.")]
        public float RimWidth = 340f;
        [Tooltip("Height of the mountain rim at the map edge, before its ridges are added.")]
        public float RimHeight = 110f;
        [Tooltip("Extra height the rim's ridges add.")]
        public float RimRidgeHeight = 60f;

        [Header("Backdrop")]
        [Tooltip("Height of the mountain ranges beyond the map edge, before their ridges are added.")]
        public float BackdropHeight = 45f;
        [Tooltip("Extra height the backdrop's ridges add.")]
        public float BackdropRidgeHeight = 120f;
        [Tooltip("Distance past the map edge over which the rim blends into the backdrop ranges.")]
        public float BackdropBlend = 360f;

        [Header("Painting")]
        [Tooltip("Half the width of the dirt track down the middle of each approach valley.")]
        public float TrackHalfWidth = 11f;
        [Tooltip("Radius of the trampled dirt yard around the flag.")]
        public float YardRadius = 38f;
        [Tooltip("Slope in degrees at which grass starts giving way to rock.")]
        public float RockSlopeStart = 30f;
        [Tooltip("Slope in degrees above which the ground is bare rock.")]
        public float RockSlopeFull = 42f;

        [Header("Scenery")]
        [Tooltip("Spacing of the grid trees are scattered on; smaller means denser forests.")]
        public float TreeSpacing = 12f;
        [Tooltip("Tree spacing on the backdrop, which is only ever seen from afar.")]
        public float BackdropTreeSpacing = 17f;
        [Tooltip("Height above the valley floor where forests thin out into bare mountain.")]
        public float TreeLine = 150f;
        [Tooltip("Steepest slope in degrees a tree grows on.")]
        public float TreeMaxSlope = 30f;
        [Tooltip("Trees keep at least this far from a valley floor so nothing blocks a path or a build spot.")]
        public float SceneryClearance = 18f;
    }

    /// <summary>
    /// Generates the War Valley map: a flat basin around the flag, four winding approach valleys out to
    /// the enemy spawns, rolling wooded hills between them and a mountain rim around the edge. Beyond
    /// the edge, eight backdrop terrains carry the mountains and valleys on into the haze so the world
    /// looks endless, while invisible walls stop anyone walking out onto them.
    /// <para>
    /// It also lays out the map's <see cref="WV_PlayArea"/>: the walls at the map edge, and the
    /// frontier volumes that keep the commanders' forces out of the outer band. Run it from
    /// Ryan/War Valley/Rebuild Map, then re-bake the navmesh.
    /// </para>
    /// <para>
    /// The valley floor stays at the height the flag and the server's spawn points were authored at, so
    /// rebuilding never moves gameplay.
    /// </para>
    /// </summary>
    public static class WV_MapAuthoring {
        const string MapFolder = "Assets/Universes/UniverseData/war_valley/start_map";
        const string TextureFolder = MapFolder + "/Textures";
        const string BackdropFolder = MapFolder + "/Backdrop";
        const string TerrainDataPath = MapFolder + "/WarValleyTerrain.asset";
        const string ScenePath = "Assets/Universes/UniverseData/war_valley/war_valley_start.unity";
        const string MapRootName = "War Valley Map";
        const string BackdropName = "Backdrop";
        const string EdgeWallsName = "Terrain Edge Boundary";
        const string FrontierName = "Frontier";
        const string NaturePrefabs = "Assets/Universes/UniverseData/classic_horror/Flooded_Grounds/Prefabs/Nature";
        static readonly int SkyGroundColor = Shader.PropertyToID("_GroundColor");

        /// <summary>
        /// The four tiles that share an edge with the map match its heightmap resolution, so their
        /// edges meet it vertex for vertex with no cracks. The corner tiles only touch it at a point.
        /// </summary>
        const int CornerHeightmapResolution = 513;
        const int BackdropAlphamapResolution = 256;
        const float BackdropMaxHeight = 400f;

        /// <summary>The edge walls reach from below the valley floor to well above the highest peak.</summary>
        const float EdgeWallThickness = 12f;
        const float EdgeWallBottom = -60f;
        const float EdgeWallTop = 900f;

        /// <summary>Wave agents start a few metres inside the edge; the frontier volumes reach that far up and down.</summary>
        const float FrontierVolumeHeight = 600f;

        /// <summary>How much wider a valley floor, and its sides, get from the middle of the map to its edge and beyond.</summary>
        const float OuterValleyWidening = 1.5f;
        const float OuterShoulderWidening = 2.4f;

        /// <summary>The flag, at the middle of the basin.</summary>
        static readonly Vector2 Centre = new(750f, 750f);

        /// <summary>
        /// Where each approach valley ends: the enemy spawn points of the server scene's runner
        /// (<c>NPCSpawnLocs</c>), in <see cref="WV_Approach"/> order West, North, South, East.
        /// Past them the valleys carry straight on out through the backdrop.
        /// </summary>
        static readonly Vector2[] ApproachEnds = {
            new(34.47f, 752.17f), new(754.44f, 1458.77f), new(749.95f, 30.75f), new(1470.08f, 737.49f)
        };

        /// <summary>Which way each road first swings, so the four do not bend in lockstep.</summary>
        static readonly float[] BendSigns = { 1f, -1f, -1f, 1f };

        /// <summary>
        /// The trees scattered over the hills. The source models are modelled at up to 47 m tall, so they
        /// are scaled down to read well beside soldiers. The densest models (Tall_B, Crinkly) are left out
        /// to keep the tree count affordable.
        /// </summary>
        static readonly (string path, float weight, float minScale, float maxScale)[] TreeKinds = {
            (NaturePrefabs + "/Trees/TreeCreator_Tall_A.prefab", 4f, 0.45f, 0.65f),
            (NaturePrefabs + "/Trees/TreeCreator_Tall_C.prefab", 2f, 0.55f, 0.75f),
            (NaturePrefabs + "/Trees/TreeCreator_Small_A.prefab", 2f, 1.2f, 1.7f),
            (NaturePrefabs + "/Trees/TreeCreator_Small_B.prefab", 2f, 1.2f, 1.7f),
            (NaturePrefabs + "/Trees/TreeCreator_Bush_A.prefab", 1.5f, 1.2f, 2f),
        };

        /// <summary>The eight tiles around the map, as offsets in map widths, with the name of each.</summary>
        static readonly (int x, int z, string name)[] BackdropTiles = {
            (-1, -1, "SouthWest"), (0, -1, "South"), (1, -1, "SouthEast"),
            (-1, 0, "West"), (1, 0, "East"),
            (-1, 1, "NorthWest"), (0, 1, "North"), (1, 1, "NorthEast"),
        };

        enum Layer { Meadow, DryMeadow, Dirt, Rock }

        [MenuItem("Ryan/War Valley/Rebuild Map")]
        public static void RebuildMap() => Rebuild(new WV_MapRecipe());

        public static void Rebuild(WV_MapRecipe recipe) {
            TerrainData data = AssetDatabase.LoadAssetAtPath<TerrainData>(TerrainDataPath);
            if (data == null)
                throw new InvalidOperationException($"No terrain data at {TerrainDataPath}.");

            Scene scene = SceneManager.GetActiveScene();
            if (scene.path != ScenePath)
                scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            GameObject map = FindRoot(scene, MapRootName);
            Terrain terrain = map.GetComponentInChildren<Terrain>(true);

            terrain.heightmapPixelError = 3f;
            var shape = new MapShape(recipe, data.size);
            // The floor stays exactly where the flag and spawn points sit.
            float floorHeight = data.GetInterpolatedHeight(0.5f, 0.5f);

            ConfigureGroundTextures();
            ConfigureGroundLayers(data);
            Sculpt(data, Vector2.zero, (x, z) => floorHeight + (1f - shape.Floor(x, z, out _, out _)) * shape.Relief(x, z));
            Paint(data, Vector2.zero, floorHeight, shape, recipe);
            int trees = Plant(data, Vector2.zero, floorHeight, shape, recipe, recipe.TreeSpacing);
            EditorUtility.SetDirty(data);

            trees += BuildBackdrop(map, terrain, data, floorHeight, shape, recipe);
            AssetDatabase.SaveAssets();

            WV_PlayArea playArea = EnsurePlayArea(map, data);
            BuildEdgeWalls(map, playArea);
            BuildFrontier(map, playArea);
            TidyScene(scene, map, terrain);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"War Valley map rebuilt with {trees} trees. Re-bake the navmesh next.");
        }

        // ---------------------------------------------------------------- shape

        /// <summary>The map's layout as distance fields, shared by sculpting, painting and planting.</summary>
        sealed class MapShape {
            public readonly WV_MapRecipe Recipe;
            public readonly Vector3 Size;
            readonly Vector2 noiseOffset;

            public MapShape(WV_MapRecipe recipe, Vector3 size) {
                Recipe = recipe;
                Size = size;
                var random = new System.Random(recipe.Seed);
                noiseOffset = new Vector2(random.Next(1000, 9000), random.Next(1000, 9000));
            }

            /// <summary>Fractal noise in roughly 0..1.</summary>
            public float Noise(float x, float z, float frequency, int octaves = 4) {
                float sum = 0f, amplitude = 1f, total = 0f;
                for (int i = 0; i < octaves; i++) {
                    sum += amplitude * Mathf.PerlinNoise(noiseOffset.x + x * frequency, noiseOffset.y + z * frequency);
                    total += amplitude;
                    amplitude *= 0.5f;
                    frequency *= 2.03f;
                }
                return sum / total;
            }

            /// <summary>Sharp-crested noise in 0..1 for mountain ridges.</summary>
            public float Ridges(float x, float z, float frequency) {
                float n = Noise(x + 311f, z - 173f, frequency, 3);
                return 1f - Mathf.Abs(n * 2f - 1f);
            }

            /// <summary>Distance from the middle of the nearest approach road, which runs on straight past its spawn.</summary>
            public float RoadDistance(float x, float z) {
                float best = float.MaxValue;
                var point = new Vector2(x, z);
                for (int i = 0; i < ApproachEnds.Length; i++) {
                    Vector2 axis = ApproachEnds[i] - Centre;
                    float length = axis.magnitude;
                    Vector2 along = axis / length;
                    Vector2 across = new(-along.y, along.x);
                    Vector2 offset = point - Centre;
                    float u = Vector2.Dot(offset, along);
                    if (u < 0f)
                        continue;
                    // An S-bend whose sideways offset and heading both come back to zero at each end,
                    // so the road leaves the basin and runs on past its spawn straight, with no kink
                    // (a kink there left a step in the hills beside it).
                    float t = Mathf.Min(u / length, 1f);
                    float bend = BendSigns[i] * Recipe.RoadBend;
                    float a = 2f * Mathf.PI * t;
                    float b = Mathf.PI * t;
                    float lateral = bend * Mathf.Sin(a) * Mathf.Sin(b);
                    float slope = bend / length * (2f * Mathf.PI * Mathf.Cos(a) * Mathf.Sin(b) + Mathf.PI * Mathf.Sin(a) * Mathf.Cos(b));
                    float distance = Mathf.Abs(Vector2.Dot(offset, across) - lateral) / Mathf.Sqrt(1f + slope * slope);
                    best = Mathf.Min(best, distance);
                }
                return best;
            }

            public float BasinEdge(float x, float z) {
                float angle = Mathf.Atan2(z - Centre.y, x - Centre.x);
                float wobble = Mathf.PerlinNoise(noiseOffset.x + Mathf.Cos(angle) * 1.7f, noiseOffset.y + Mathf.Sin(angle) * 1.7f);
                return Recipe.BasinRadius + (wobble * 2f - 1f) * Recipe.BasinWobble;
            }

            /// <summary>
            /// 1 on the flat valley floors and basin, falling to 0 up in the hills. Towards the map edge
            /// each valley widens and its sides ease, so where it runs out through the mountains it
            /// opens into the country beyond instead of a gorge.
            /// </summary>
            public float Floor(float x, float z, out float roadDistance, out float centreDistance) {
                roadDistance = RoadDistance(x, z);
                centreDistance = Vector2.Distance(new Vector2(x, z), Centre);
                float edge = BasinEdge(x, z);
                float basin = 1f - Smooth(edge, edge + Recipe.ShoulderWidth * 0.8f, centreDistance);
                float opening = RimFactor(x, z);
                float halfWidth = Recipe.ValleyHalfWidth * Mathf.Lerp(1f, OuterValleyWidening, opening);
                float shoulder = Recipe.ShoulderWidth * Mathf.Lerp(1f, OuterShoulderWidening, opening);
                float valley = 1f - Smooth(halfWidth, halfWidth + shoulder, roadDistance);
                return Mathf.Max(basin, valley);
            }

            /// <summary>0 well inside the map, rising to 1 at its edge and beyond: how far into the mountain rim a point is.</summary>
            public float RimFactor(float x, float z) {
                float edgeDistance = Mathf.Min(Mathf.Min(x, z), Mathf.Min(Size.x - x, Size.z - z));
                return 1f - Smooth(30f, Recipe.RimWidth, edgeDistance);
            }

            /// <summary>Height above the valley floor of the hills and mountains inside the map.</summary>
            public float Relief(float x, float z) {
                float hills = Recipe.HillBase + Recipe.HillRange * Mathf.Pow(Noise(x, z, 0.0029f), 1.4f) * 1.6f;
                float rim = Mathf.Pow(RimFactor(x, z), 1.6f);
                float mountains = rim * (Recipe.RimHeight + Recipe.RimRidgeHeight * Ridges(x, z, 0.0065f));
                return hills + mountains;
            }

            /// <summary>Height above the valley floor of the ranges beyond the map, cut by the valleys running on.</summary>
            public float BackdropRelief(float x, float z) {
                // Out here the valleys open wider and their sides climb gently, so they read as
                // country carrying on rather than a corridor between walls.
                float halfWidth = Recipe.ValleyHalfWidth * OuterValleyWidening;
                float valley = 1f - Smooth(halfWidth, halfWidth + Recipe.ShoulderWidth * OuterShoulderWidening, RoadDistance(x, z));
                float ranges = Recipe.BackdropHeight + Recipe.BackdropRidgeHeight * Ridges(x, z, 0.0042f)
                    + Recipe.HillRange * Noise(x - 700f, z + 300f, 0.003f);
                return (1f - valley) * ranges;
            }

            /// <summary>Distance from a point outside the map to the map's edge; 0 inside it.</summary>
            public float DistanceOutside(float x, float z) {
                float dx = Mathf.Max(0f, Mathf.Max(-x, x - Size.x));
                float dz = Mathf.Max(0f, Mathf.Max(-z, z - Size.z));
                return Mathf.Sqrt(dx * dx + dz * dz);
            }
        }

        static GameObject FindRoot(Scene scene, string name) {
            foreach (GameObject root in scene.GetRootGameObjects()) {
                if (root.name == name)
                    return root;
            }
            throw new InvalidOperationException($"{scene.path} has no root object named {name}.");
        }

        static float Smooth(float from, float to, float value) {
            float t = Mathf.Clamp01((value - from) / (to - from));
            return t * t * (3f - 2f * t);
        }

        // ---------------------------------------------------------------- terrain

        /// <param name="origin">Where this terrain's corner sits in map coordinates, so tiles share one set of fields.</param>
        /// <param name="height">Height in metres above the terrain's base at a map coordinate.</param>
        static void Sculpt(TerrainData data, Vector2 origin, Func<float, float, float> height) {
            int resolution = data.heightmapResolution;
            var heights = new float[resolution, resolution];
            for (int row = 0; row < resolution; row++) {
                float z = origin.y + row / (float)(resolution - 1) * data.size.z;
                for (int column = 0; column < resolution; column++) {
                    float x = origin.x + column / (float)(resolution - 1) * data.size.x;
                    heights[row, column] = Mathf.Clamp01(height(x, z) / data.size.y);
                }
            }
            data.SetHeights(0, 0, heights);
        }

        static void Paint(TerrainData data, Vector2 origin, float floorHeight, MapShape shape, WV_MapRecipe recipe) {
            int resolution = data.alphamapResolution;
            int layers = data.alphamapLayers;
            var alphas = new float[resolution, resolution, layers];
            for (int row = 0; row < resolution; row++) {
                float v = (row + 0.5f) / resolution;
                float z = origin.y + v * data.size.z;
                for (int column = 0; column < resolution; column++) {
                    float u = (column + 0.5f) / resolution;
                    float x = origin.x + u * data.size.x;
                    float floor = shape.Floor(x, z, out float road, out float centre);
                    float slope = data.GetSteepness(u, v);
                    float altitude = data.GetInterpolatedHeight(u, v) - floorHeight;
                    float breakup = shape.Noise(x + 91f, z + 37f, 0.045f, 3);

                    float rock = Smooth(recipe.RockSlopeStart, recipe.RockSlopeFull, slope + (breakup - 0.5f) * 8f);
                    rock = Mathf.Max(rock, Smooth(recipe.TreeLine + 20f, recipe.TreeLine + 70f, altitude + breakup * 25f));

                    float track = 1f - Smooth(recipe.TrackHalfWidth * 0.6f, recipe.TrackHalfWidth, road + (breakup - 0.5f) * 6f);
                    float yard = 1f - Smooth(recipe.YardRadius * 0.7f, recipe.YardRadius, centre + (breakup - 0.5f) * 14f);
                    float worn = Smooth(0.62f, 0.72f, shape.Noise(x - 400f, z + 220f, 0.011f, 3)) * floor * 0.8f;
                    float dirt = Mathf.Max(Mathf.Max(track, yard), worn) * (1f - rock);

                    float dry = Smooth(0.42f, 0.62f, shape.Noise(x + 1200f, z - 800f, 0.0045f)) * (1f - rock) * (1f - dirt);
                    float meadow = Mathf.Max(0f, 1f - rock - dirt - dry);

                    alphas[row, column, (int)Layer.Meadow] = meadow;
                    alphas[row, column, (int)Layer.DryMeadow] = dry;
                    alphas[row, column, (int)Layer.Dirt] = dirt;
                    alphas[row, column, (int)Layer.Rock] = rock;
                }
            }
            data.SetAlphamaps(0, 0, alphas);
        }

        /// <summary>
        /// Scatters forests as terrain trees, replacing the previous batch: clustered by noise, thinning
        /// towards the tree line, and kept off steep ground, the valleys and the basin. Terrain trees keep
        /// thousands of instances compact in the terrain asset and draw them instanced.
        /// </summary>
        static int Plant(TerrainData data, Vector2 origin, float floorHeight, MapShape shape, WV_MapRecipe recipe, float spacing) {
            var prototypes = new List<TreePrototype>();
            var kinds = new List<(int prototype, float weight, float min, float max)>();
            foreach (var kind in TreeKinds) {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(kind.path);
                if (prefab == null) {
                    Debug.LogWarning($"War Valley map: missing tree prefab {kind.path}; it is left out.");
                    continue;
                }
                kinds.Add((prototypes.Count, kind.weight, kind.minScale, kind.maxScale));
                prototypes.Add(new TreePrototype { prefab = prefab });
            }
            data.treeInstances = Array.Empty<TreeInstance>();
            data.treePrototypes = prototypes.ToArray();
            data.RefreshPrototypes();
            if (kinds.Count == 0)
                return 0;

            float totalWeight = 0f;
            foreach (var kind in kinds)
                totalWeight += kind.weight;
            // Each tile seeds from its own position, so tiles do not repeat one another's forests.
            var random = new System.Random(recipe.Seed * 31 + 7 + Mathf.RoundToInt(origin.x * 3f + origin.y * 7f));
            var instances = new List<TreeInstance>();
            for (float gz = spacing * 0.5f; gz < data.size.z; gz += spacing)
            for (float gx = spacing * 0.5f; gx < data.size.x; gx += spacing) {
                float lx = gx + ((float)random.NextDouble() - 0.5f) * spacing * 0.9f;
                float lz = gz + ((float)random.NextDouble() - 0.5f) * spacing * 0.9f;
                float u = Mathf.Clamp01(lx / data.size.x);
                float v = Mathf.Clamp01(lz / data.size.z);
                float x = origin.x + lx;
                float z = origin.y + lz;
                if (!IsClear(shape, recipe, x, z))
                    continue;
                float altitude = data.GetInterpolatedHeight(u, v) - floorHeight;
                if (data.GetSteepness(u, v) > recipe.TreeMaxSlope || altitude > recipe.TreeLine)
                    continue;
                float forest = Smooth(0.34f, 0.5f, shape.Noise(x - 2000f, z + 900f, 0.006f));
                float chance = Mathf.Max(forest, 0.05f) * (1f - Smooth(recipe.TreeLine * 0.7f, recipe.TreeLine, altitude));
                if (random.NextDouble() >= chance)
                    continue;

                float pick = (float)random.NextDouble() * totalWeight;
                var chosen = kinds[^1];
                foreach (var kind in kinds) {
                    if ((pick -= kind.weight) <= 0f) {
                        chosen = kind;
                        break;
                    }
                }
                float scale = Mathf.Lerp(chosen.min, chosen.max, (float)random.NextDouble());
                float tint = Mathf.Lerp(0.85f, 1f, (float)random.NextDouble());
                instances.Add(new TreeInstance {
                    prototypeIndex = chosen.prototype,
                    position = new Vector3(u, 0f, v),
                    widthScale = scale,
                    heightScale = scale * Mathf.Lerp(0.9f, 1.1f, (float)random.NextDouble()),
                    rotation = (float)random.NextDouble() * Mathf.PI * 2f,
                    color = new Color(tint, tint, tint, 1f),
                    lightmapColor = Color.white
                });
            }
            data.SetTreeInstances(instances.ToArray(), snapToHeightmap: true);
            return instances.Count;
        }

        /// <summary>Whether a tree may stand here without blocking a valley floor or the basin.</summary>
        static bool IsClear(MapShape shape, WV_MapRecipe recipe, float x, float z) {
            float floor = shape.Floor(x, z, out float road, out float centre);
            if (floor > 0.6f || road < recipe.ValleyHalfWidth + recipe.SceneryClearance)
                return false;
            return centre > shape.BasinEdge(x, z) + recipe.SceneryClearance;
        }

        // ---------------------------------------------------------------- backdrop

        /// <summary>
        /// Builds the eight terrains around the map. At the map's edge each one takes the map's own
        /// height, so there is no seam, then blends into higher ranges with the valleys running on
        /// through them. They have no colliders and sit on the Ignore Raycast layer, so nothing can
        /// stand on them, clicks pass through them and the navmesh bake leaves them out.
        /// </summary>
        static int BuildBackdrop(GameObject map, Terrain mainTerrain, TerrainData mainData, float floorHeight,
            MapShape shape, WV_MapRecipe recipe) {
            if (!AssetDatabase.IsValidFolder(BackdropFolder))
                AssetDatabase.CreateFolder(MapFolder, BackdropName);

            Transform parent = map.transform.Find(BackdropName);
            if (parent == null) {
                parent = new GameObject(BackdropName).transform;
                parent.SetParent(map.transform, false);
            }
            parent.gameObject.layer = IgnoreRaycastLayer;

            Vector3 size = mainData.size;
            var tiles = new Dictionary<(int, int), Terrain> { [(0, 0)] = mainTerrain };
            int trees = 0;
            foreach (var tile in BackdropTiles) {
                bool corner = tile.x != 0 && tile.z != 0;
                TerrainData data = LoadOrCreateBackdropData(tile.name, mainData,
                    corner ? CornerHeightmapResolution : mainData.heightmapResolution);
                var origin = new Vector2(tile.x * size.x, tile.z * size.z);
                Sculpt(data, origin, (x, z) => BackdropHeight(x, z, mainData, floorHeight, shape, recipe));
                Paint(data, origin, floorHeight, shape, recipe);
                trees += Plant(data, origin, floorHeight, shape, recipe, recipe.BackdropTreeSpacing);
                EditorUtility.SetDirty(data);

                string objectName = $"Backdrop {tile.name}";
                Transform existing = parent.Find(objectName);
                GameObject go = existing != null ? existing.gameObject : new GameObject(objectName);
                go.transform.SetParent(parent, false);
                go.transform.position = mainTerrain.transform.position + new Vector3(origin.x, 0f, origin.y);
                go.layer = IgnoreRaycastLayer;
                GameObjectUtility.SetStaticEditorFlags(go,
                    StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic);
                if (go.TryGetComponent(out TerrainCollider collider))
                    UnityEngine.Object.DestroyImmediate(collider);
                if (!go.TryGetComponent(out Terrain terrain))
                    terrain = go.AddComponent<Terrain>();
                terrain.terrainData = data;
                terrain.materialTemplate = mainTerrain.materialTemplate;
                terrain.drawInstanced = true;
                // Same level of detail as the map, so their shared edges stitch without cracks.
                terrain.heightmapPixelError = mainTerrain.heightmapPixelError;
                terrain.basemapDistance = mainTerrain.basemapDistance;
                terrain.treeDistance = 2000f;
                terrain.treeBillboardDistance = 2000f;
                terrain.treeMaximumFullLODCount = 5000;
                terrain.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                EditorUtility.SetDirty(terrain);
                tiles[(tile.x, tile.z)] = terrain;
            }

            foreach (var entry in tiles) {
                (int x, int z) = entry.Key;
                entry.Value.SetNeighbors(
                    tiles.GetValueOrDefault((x - 1, z)), tiles.GetValueOrDefault((x, z + 1)),
                    tiles.GetValueOrDefault((x + 1, z)), tiles.GetValueOrDefault((x, z - 1)));
            }
            return trees;
        }

        /// <summary>
        /// The map's own height at its edge, blending into the backdrop ranges with distance, so the
        /// rim and the valley mouths carry on unbroken.
        /// </summary>
        static float BackdropHeight(float x, float z, TerrainData mainData, float floorHeight, MapShape shape, WV_MapRecipe recipe) {
            Vector3 size = mainData.size;
            float edgeU = Mathf.Clamp01(x / size.x);
            float edgeV = Mathf.Clamp01(z / size.z);
            float edgeHeight = mainData.GetInterpolatedHeight(edgeU, edgeV);
            float far = floorHeight + shape.BackdropRelief(x, z);
            float blend = Smooth(0f, recipe.BackdropBlend, shape.DistanceOutside(x, z));
            return Mathf.Clamp(Mathf.Lerp(edgeHeight, far, blend), 0f, BackdropMaxHeight);
        }

        static TerrainData LoadOrCreateBackdropData(string tileName, TerrainData mainData, int heightmapResolution) {
            string path = $"{BackdropFolder}/WarValleyBackdrop_{tileName}.asset";
            TerrainData data = AssetDatabase.LoadAssetAtPath<TerrainData>(path);
            if (data == null) {
                data = new TerrainData();
                AssetDatabase.CreateAsset(data, path);
            }
            data.heightmapResolution = heightmapResolution;
            data.alphamapResolution = BackdropAlphamapResolution;
            data.baseMapResolution = 512;
            data.size = new Vector3(mainData.size.x, BackdropMaxHeight, mainData.size.z);
            data.terrainLayers = mainData.terrainLayers;
            return data;
        }

        static int IgnoreRaycastLayer => LayerMask.NameToLayer("Ignore Raycast");

        // ---------------------------------------------------------------- boundaries

        static WV_PlayArea EnsurePlayArea(GameObject map, TerrainData data) {
            if (!map.TryGetComponent(out WV_PlayArea playArea))
                playArea = map.AddComponent<WV_PlayArea>();
            var serialized = new SerializedObject(playArea);
            serialized.FindProperty("mapSize").vector2Value = new Vector2(data.size.x, data.size.z);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return playArea;
        }

        /// <summary>
        /// Sizes the invisible walls along the map edge so no one can walk or climb out onto the
        /// backdrop. They sit just inside the edge and reach above the highest peak.
        /// </summary>
        static void BuildEdgeWalls(GameObject map, WV_PlayArea playArea) {
            Transform walls = FindDeep(map.transform, EdgeWallsName)
                ?? throw new InvalidOperationException($"{MapRootName} has no {EdgeWallsName}.");
            Rect area = playArea.MapArea;
            float half = EdgeWallThickness * 0.5f;
            float height = EdgeWallTop - EdgeWallBottom;
            float centreY = (EdgeWallTop + EdgeWallBottom) * 0.5f;
            SetWall(walls, "North Edge", new Vector3(area.center.x, centreY, area.yMax - half), new Vector3(area.width, height, EdgeWallThickness));
            SetWall(walls, "South Edge", new Vector3(area.center.x, centreY, area.yMin + half), new Vector3(area.width, height, EdgeWallThickness));
            SetWall(walls, "East Edge", new Vector3(area.xMax - half, centreY, area.center.y), new Vector3(EdgeWallThickness, height, area.height));
            SetWall(walls, "West Edge", new Vector3(area.xMin + half, centreY, area.center.y), new Vector3(EdgeWallThickness, height, area.height));
        }

        static void SetWall(Transform walls, string name, Vector3 centre, Vector3 size) {
            Transform wall = walls.Find(name) ?? throw new InvalidOperationException($"{EdgeWallsName} has no {name}.");
            wall.SetPositionAndRotation(centre, Quaternion.identity);
            wall.localScale = Vector3.one;
            if (!wall.TryGetComponent(out BoxCollider box))
                box = wall.gameObject.AddComponent<BoxCollider>();
            box.center = Vector3.zero;
            box.size = ToLocalSize(wall, size);
            EditorUtility.SetDirty(box);
        }

        /// <summary>
        /// Marks the band between the map edge and <see cref="WV_PlayArea.AlliedArea"/> as
        /// <see cref="WV_NavAreas.Frontier"/> with four modifier volumes under the navmesh surface.
        /// </summary>
        static void BuildFrontier(GameObject map, WV_PlayArea playArea) {
            EnsureFrontierAreaName();
            NavMeshSurface surface = map.GetComponentInChildren<NavMeshSurface>(true)
                ?? throw new InvalidOperationException($"{MapRootName} has no NavMeshSurface.");
            Transform frontier = surface.transform.Find(FrontierName);
            if (frontier != null)
                UnityEngine.Object.DestroyImmediate(frontier.gameObject);
            frontier = new GameObject(FrontierName).transform;
            frontier.SetParent(surface.transform, false);
            frontier.position = Vector3.zero;

            Rect map2D = playArea.MapArea;
            Rect allied = playArea.AlliedArea;
            float y = map.transform.position.y + FrontierVolumeHeight * 0.5f - 100f;
            AddFrontierVolume(frontier, "North", Rect.MinMaxRect(map2D.xMin, allied.yMax, map2D.xMax, map2D.yMax), y);
            AddFrontierVolume(frontier, "South", Rect.MinMaxRect(map2D.xMin, map2D.yMin, map2D.xMax, allied.yMin), y);
            AddFrontierVolume(frontier, "East", Rect.MinMaxRect(allied.xMax, allied.yMin, map2D.xMax, allied.yMax), y);
            AddFrontierVolume(frontier, "West", Rect.MinMaxRect(map2D.xMin, allied.yMin, allied.xMin, allied.yMax), y);
        }

        static void AddFrontierVolume(Transform parent, string name, Rect area, float centreY) {
            var go = new GameObject($"Frontier {name}");
            go.transform.SetParent(parent, false);
            go.transform.position = new Vector3(area.center.x, centreY, area.center.y);
            var volume = go.AddComponent<NavMeshModifierVolume>();
            volume.center = Vector3.zero;
            volume.size = ToLocalSize(go.transform, new Vector3(area.width, FrontierVolumeHeight, area.height));
            volume.area = WV_NavAreas.Frontier;
        }

        /// <summary>Names <see cref="WV_NavAreas.Frontier"/> in the project's navigation areas so it reads in the Inspector.</summary>
        static void EnsureFrontierAreaName() {
            UnityEngine.Object[] assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/NavMeshAreas.asset");
            if (assets.Length == 0)
                return;
            var settings = new SerializedObject(assets[0]);
            SerializedProperty areas = settings.FindProperty("areas");
            if (areas == null || areas.arraySize <= WV_NavAreas.Frontier)
                return;
            SerializedProperty areaName = areas.GetArrayElementAtIndex(WV_NavAreas.Frontier).FindPropertyRelative("name");
            if (areaName.stringValue == FrontierName)
                return;
            if (!string.IsNullOrEmpty(areaName.stringValue))
                throw new InvalidOperationException(
                    $"Navigation area {WV_NavAreas.Frontier} is already used as \"{areaName.stringValue}\"; move WV_NavAreas.Frontier.");
            areaName.stringValue = FrontierName;
            settings.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssets();
        }

        /// <summary>A size in world metres as the local size a component under <paramref name="transform"/> needs (the environment root is scaled).</summary>
        static Vector3 ToLocalSize(Transform transform, Vector3 worldSize) {
            Vector3 scale = transform.lossyScale;
            return new Vector3(worldSize.x / scale.x, worldSize.y / scale.y, worldSize.z / scale.z);
        }

        static Transform FindDeep(Transform root, string name) {
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true)) {
                if (child.name == name)
                    return child;
            }
            return null;
        }

        // ---------------------------------------------------------------- ground layers

        static void ConfigureGroundTextures() {
            foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { TextureFolder })) {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (AssetImporter.GetAtPath(path) is not TextureImporter importer)
                    continue;
                bool normal = path.EndsWith("_Normal.png", StringComparison.Ordinal);
                importer.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
                importer.sRGBTexture = !normal;
                importer.alphaSource = TextureImporterAlphaSource.None;
                importer.mipmapEnabled = true;
                importer.wrapMode = TextureWrapMode.Repeat;
                importer.anisoLevel = 4;
                importer.maxTextureSize = 1024;
                importer.textureCompression = TextureImporterCompression.Compressed;
                importer.SaveAndReimport();
            }
        }

        static void ConfigureGroundLayers(TerrainData data) {
            TerrainLayer[] layers = data.terrainLayers;
            if (layers.Length != 4)
                throw new InvalidOperationException("WarValleyTerrain should have four layers: meadow, dry meadow, dirt, rock.");
            // Two meadow layers share textures; the dry one is tinted straw-yellow and tiled differently
            // so large patches of it break up the green without a visible repeat.
            SetLayer(layers[(int)Layer.Meadow], "WV_Meadow", 24f, Vector2.zero, Color.white, 0.05f);
            SetLayer(layers[(int)Layer.DryMeadow], "WV_Meadow", 31f, new Vector2(7f, 3f), new Color(1.18f, 1.06f, 0.72f), 0.05f);
            SetLayer(layers[(int)Layer.Dirt], "WV_Dirt", 13f, Vector2.zero, Color.white, 0.1f);
            SetLayer(layers[(int)Layer.Rock], "WV_Rock", 22f, Vector2.zero, new Color(1.08f, 1.02f, 0.94f), 0.15f);
        }

        static void SetLayer(TerrainLayer layer, string textureName, float tile, Vector2 offset, Color tint, float smoothness) {
            layer.diffuseTexture = AssetDatabase.LoadAssetAtPath<Texture2D>($"{TextureFolder}/{textureName}_Albedo.png");
            layer.normalMapTexture = AssetDatabase.LoadAssetAtPath<Texture2D>($"{TextureFolder}/{textureName}_Normal.png");
            layer.normalScale = 1f;
            layer.tileSize = new Vector2(tile, tile);
            layer.tileOffset = offset;
            layer.diffuseRemapMin = Vector4.zero;
            layer.diffuseRemapMax = new Vector4(tint.r, tint.g, tint.b, 1f);
            layer.smoothness = smoothness;
            layer.metallic = 0f;
            EditorUtility.SetDirty(layer);
        }

        // ---------------------------------------------------------------- scene

        /// <summary>
        /// Removes what earlier maps left behind (the see-through canyon barrier, the debug panels on the
        /// edge walls, a stray marker cube, scene-object scenery) and sets up terrain drawing and the sky.
        /// </summary>
        static void TidyScene(Scene scene, GameObject map, Terrain terrain) {
            foreach (GameObject root in scene.GetRootGameObjects()) {
                if (root.name == "Cube" && root.GetComponents<Component>().Length == 4) {
                    UnityEngine.Object.DestroyImmediate(root);
                    continue;
                }
                foreach (Transform child in root.GetComponentsInChildren<Transform>(true)) {
                    if (child == null)
                        continue;
                    if (child.name is "Terrain Contour Boundary" or "Temporary Barrier Visual"
                        || (child.name == "Scenery" && child.parent == map.transform))
                        UnityEngine.Object.DestroyImmediate(child.gameObject);
                }
            }

            // The Tree Creator models use URP Lit materials, which have no billboard, so they are drawn
            // as meshes at any distance and culled by the camera's far plane instead.
            terrain.treeDistance = 2000f;
            terrain.treeBillboardDistance = 2000f;
            terrain.treeMaximumFullLODCount = 5000;
            EditorUtility.SetDirty(terrain);

            // Below the horizon, past the backdrop, the sky shows its ground colour. Matching it to the
            // fog makes the far distance read as haze instead of an olive floor.
            Material sky = RenderSettings.skybox;
            if (sky != null && sky.HasProperty(SkyGroundColor)) {
                sky.SetColor(SkyGroundColor, RenderSettings.fogColor);
                EditorUtility.SetDirty(sky);
            }
            EditorSceneManager.MarkSceneDirty(scene);
        }
    }
}
