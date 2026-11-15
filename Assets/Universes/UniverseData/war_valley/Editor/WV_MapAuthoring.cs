using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

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
        public float RoadBend = 34f;

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
        [Tooltip("Height above the valley floor where forests thin out into bare mountain.")]
        public float TreeLine = 150f;
        [Tooltip("Steepest slope in degrees a tree grows on.")]
        public float TreeMaxSlope = 30f;
        [Tooltip("Spacing of the grid boulders are scattered on.")]
        public float RockSpacing = 26f;
        [Tooltip("Trees and boulders keep at least this far from a valley floor so nothing blocks a path or a build spot.")]
        public float SceneryClearance = 18f;
    }

    /// <summary>
    /// Generates the War Valley map: a flat basin around the flag, four winding approach valleys out to
    /// the enemy spawns, rolling wooded hills between them and a mountain rim around the edge. It
    /// sculpts and paints <c>WarValleyTerrain</c>, sets up its ground layers and scenery, and tidies the
    /// scene's movement barriers. Run it from Ryan/War Valley/Rebuild Map, then re-bake the navmesh.
    /// <para>
    /// The valley floor stays at the height the flag and the server's spawn points were authored at, so
    /// rebuilding never moves gameplay.
    /// </para>
    /// </summary>
    public static class WV_MapAuthoring {
        const string MapFolder = "Assets/Universes/UniverseData/war_valley/start_map";
        const string TextureFolder = MapFolder + "/Textures";
        const string TerrainDataPath = MapFolder + "/WarValleyTerrain.asset";
        const string ScenePath = "Assets/Universes/UniverseData/war_valley/war_valley_start.unity";
        const string MapRootName = "War Valley Map";
        static readonly int SkyGroundColor = Shader.PropertyToID("_GroundColor");
        const string NaturePrefabs = "Assets/Universes/UniverseData/classic_horror/Flooded_Grounds/Prefabs/Nature";

        /// <summary>The flag, at the middle of the basin.</summary>
        static readonly Vector2 Centre = new(750f, 750f);

        /// <summary>
        /// Where each approach valley ends: the enemy spawn points of the server scene's runner
        /// (<c>NPCSpawnLocs</c>), in <see cref="Shared.WV_Approach"/> order West, North, South, East.
        /// </summary>
        static readonly Vector2[] ApproachEnds = {
            new(34.47f, 752.17f), new(754.44f, 1458.77f), new(749.95f, 30.75f), new(1470.08f, 737.49f)
        };

        /// <summary>Which way each road first swings, so the four do not bend in lockstep.</summary>
        static readonly float[] BendSigns = { 1f, -1f, -1f, 1f };

        /// <summary>
        /// The trees scattered over the hills. The source models are modelled at up to 47 m tall, so they
        /// are scaled down to read well beside soldiers. The densest models (Tall_B, Crinkly) are left out
        /// to keep a few thousand trees affordable.
        /// </summary>
        static readonly (string path, float weight, float minScale, float maxScale)[] TreeKinds = {
            (NaturePrefabs + "/Trees/TreeCreator_Tall_A.prefab", 4f, 0.45f, 0.65f),
            (NaturePrefabs + "/Trees/TreeCreator_Tall_C.prefab", 2f, 0.55f, 0.75f),
            (NaturePrefabs + "/Trees/TreeCreator_Small_A.prefab", 2f, 1.2f, 1.7f),
            (NaturePrefabs + "/Trees/TreeCreator_Small_B.prefab", 2f, 1.2f, 1.7f),
            (NaturePrefabs + "/Trees/TreeCreator_Bush_A.prefab", 1.5f, 1.2f, 2f),
        };

        static readonly (string path, float minScale, float maxScale)[] RockKinds = {
            (NaturePrefabs + "/Rocks/Rock_A.prefab", 1.6f, 3.2f),
            (NaturePrefabs + "/Rocks/Rock_B.prefab", 1.6f, 3.2f),
        };

        enum Layer { Meadow, DryMeadow, Dirt, Rock }

        [MenuItem("Ryan/War Valley/Rebuild Map")]
        public static void RebuildMap() => Rebuild(new WV_MapRecipe());

        public static void Rebuild(WV_MapRecipe recipe) {
            TerrainData data = AssetDatabase.LoadAssetAtPath<TerrainData>(TerrainDataPath);
            if (data == null)
                throw new InvalidOperationException($"No terrain data at {TerrainDataPath}.");

            var shape = new MapShape(recipe, data.size);
            ConfigureGroundTextures();
            ConfigureGroundLayers(data);
            Sculpt(data, shape);
            Paint(data, shape, recipe);

            Scene scene = SceneManager.GetActiveScene();
            if (scene.path != ScenePath)
                scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Terrain terrain = FindRoot(scene, MapRootName).GetComponentInChildren<Terrain>(true);
            int placed = Plant(scene, data, shape, recipe);
            EditorUtility.SetDirty(data);
            AssetDatabase.SaveAssets();
            TidyScene(scene, terrain);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"War Valley map rebuilt with {placed} trees and boulders. Re-bake the navmesh next.");
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

            /// <summary>Distance from the middle of the nearest approach road.</summary>
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
                    float t = Mathf.Min(u / length, 1f);
                    float bend = BendSigns[i] * Recipe.RoadBend;
                    float lateral = bend * Mathf.Sin(2f * Mathf.PI * t);
                    float slope = u <= length ? bend * 2f * Mathf.PI / length * Mathf.Cos(2f * Mathf.PI * t) : 0f;
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

            /// <summary>1 on the flat valley floors and basin, falling to 0 up in the hills.</summary>
            public float Floor(float x, float z, out float roadDistance, out float centreDistance) {
                roadDistance = RoadDistance(x, z);
                centreDistance = Vector2.Distance(new Vector2(x, z), Centre);
                float edge = BasinEdge(x, z);
                float basin = 1f - Smooth(edge, edge + Recipe.ShoulderWidth * 0.8f, centreDistance);
                float valley = 1f - Smooth(Recipe.ValleyHalfWidth, Recipe.ValleyHalfWidth + Recipe.ShoulderWidth, roadDistance);
                return Mathf.Max(basin, valley);
            }

            /// <summary>Height above the valley floor of the hills and mountains at this point.</summary>
            public float Relief(float x, float z) {
                float hills = Recipe.HillBase + Recipe.HillRange * Mathf.Pow(Noise(x, z, 0.0029f), 1.4f) * 1.6f;
                float edgeDistance = Mathf.Min(Mathf.Min(x, z), Mathf.Min(Size.x - x, Size.z - z));
                float rim = Mathf.Pow(1f - Smooth(30f, Recipe.RimWidth, edgeDistance), 1.6f);
                float mountains = rim * (Recipe.RimHeight + Recipe.RimRidgeHeight * Ridges(x, z, 0.0065f));
                return hills + mountains;
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

        static void Sculpt(TerrainData data, MapShape shape) {
            int resolution = data.heightmapResolution;
            // The floor stays exactly where the flag and spawn points sit.
            float floorHeight = data.GetInterpolatedHeight(0.5f, 0.5f);
            float maxHeight = data.size.y;
            var heights = new float[resolution, resolution];
            for (int row = 0; row < resolution; row++) {
                float z = row / (float)(resolution - 1) * data.size.z;
                for (int column = 0; column < resolution; column++) {
                    float x = column / (float)(resolution - 1) * data.size.x;
                    float floor = shape.Floor(x, z, out _, out _);
                    float height = floorHeight + (1f - floor) * shape.Relief(x, z);
                    heights[row, column] = Mathf.Clamp01(height / maxHeight);
                }
            }
            data.SetHeights(0, 0, heights);
        }

        static void Paint(TerrainData data, MapShape shape, WV_MapRecipe recipe) {
            int resolution = data.alphamapResolution;
            int layers = data.alphamapLayers;
            float floorHeight = data.GetInterpolatedHeight(0.5f, 0.5f);
            var alphas = new float[resolution, resolution, layers];
            for (int row = 0; row < resolution; row++) {
                float v = (row + 0.5f) / resolution;
                float z = v * data.size.z;
                for (int column = 0; column < resolution; column++) {
                    float u = (column + 0.5f) / resolution;
                    float x = u * data.size.x;
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
        /// Scatters trees and boulders as terrain trees, replacing the previous batch. Terrain trees keep
        /// thousands of instances compact in the terrain asset and draw them instanced.
        /// </summary>
        static int Plant(Scene scene, TerrainData data, MapShape shape, WV_MapRecipe recipe) {
            // An earlier version of this tool placed scenery as scene objects.
            Transform oldScenery = FindRoot(scene, MapRootName).transform.Find("Scenery");
            if (oldScenery != null)
                UnityEngine.Object.DestroyImmediate(oldScenery.gameObject);

            var prototypes = new List<TreePrototype>();
            var trees = LoadKinds(prototypes, TreeKinds.Length, i => (TreeKinds[i].path, TreeKinds[i].weight, TreeKinds[i].minScale, TreeKinds[i].maxScale));
            var rocks = LoadKinds(prototypes, RockKinds.Length, i => (RockKinds[i].path, 1f, RockKinds[i].minScale, RockKinds[i].maxScale));
            data.treeInstances = Array.Empty<TreeInstance>();
            data.treePrototypes = prototypes.ToArray();
            data.RefreshPrototypes();

            var random = new System.Random(recipe.Seed * 31 + 7);
            float floorHeight = data.GetInterpolatedHeight(0.5f, 0.5f);
            var instances = new List<TreeInstance>();

            // Forests: clustered by noise, thinning towards the tree line, kept off steep ground and paths.
            if (trees.Count > 0) {
                Scatter(data, recipe.TreeSpacing, random, (x, z, u, v) => {
                    if (!IsClear(shape, recipe, x, z))
                        return false;
                    float altitude = data.GetInterpolatedHeight(u, v) - floorHeight;
                    if (data.GetSteepness(u, v) > recipe.TreeMaxSlope || altitude > recipe.TreeLine)
                        return false;
                    float forest = Smooth(0.34f, 0.5f, shape.Noise(x - 2000f, z + 900f, 0.006f));
                    float chance = Mathf.Max(forest, 0.05f) * (1f - Smooth(recipe.TreeLine * 0.7f, recipe.TreeLine, altitude));
                    return random.NextDouble() < chance;
                }, (u, v) => instances.Add(Instance(PickWeighted(trees, random), u, v, random, boulder: false)));
            }

            // Boulders: mostly on rocky slopes, a few through the hills.
            if (rocks.Count > 0) {
                Scatter(data, recipe.RockSpacing, random, (x, z, u, v) => {
                    if (!IsClear(shape, recipe, x, z))
                        return false;
                    return random.NextDouble() < (data.GetSteepness(u, v) > recipe.RockSlopeStart ? 0.3 : 0.05);
                }, (u, v) => instances.Add(Instance(rocks[random.Next(rocks.Count)], u, v, random, boulder: true)));
            }

            data.SetTreeInstances(instances.ToArray(), snapToHeightmap: true);
            return instances.Count;
        }

        static List<(int prototype, float weight, float min, float max)> LoadKinds(List<TreePrototype> prototypes,
            int count, Func<int, (string path, float weight, float min, float max)> kind) {
            var loaded = new List<(int, float, float, float)>();
            for (int i = 0; i < count; i++) {
                var (path, weight, min, max) = kind(i);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null) {
                    Debug.LogWarning($"War Valley map: missing scenery prefab {path}; it is left out.");
                    continue;
                }
                loaded.Add((prototypes.Count, weight, min, max));
                prototypes.Add(new TreePrototype { prefab = prefab });
            }
            return loaded;
        }

        static (int prototype, float weight, float min, float max) PickWeighted(
            List<(int prototype, float weight, float min, float max)> kinds, System.Random random) {
            float total = 0f;
            foreach (var kind in kinds)
                total += kind.weight;
            float pick = (float)random.NextDouble() * total;
            foreach (var kind in kinds) {
                if ((pick -= kind.weight) <= 0f)
                    return kind;
            }
            return kinds[^1];
        }

        /// <param name="boulder">
        /// Boulders are squashed: the source rocks are tall blocks that would otherwise stand on the
        /// hills like monoliths.
        /// </param>
        static TreeInstance Instance((int prototype, float weight, float min, float max) kind, float u, float v,
            System.Random random, bool boulder) {
            float Range(float from, float to) => Mathf.Lerp(from, to, (float)random.NextDouble());
            float scale = Range(kind.min, kind.max);
            float tint = Range(0.85f, 1f);
            return new TreeInstance {
                prototypeIndex = kind.prototype,
                position = new Vector3(u, 0f, v),
                widthScale = boulder ? scale * Range(1f, 1.4f) : scale,
                heightScale = scale * (boulder ? Range(0.35f, 0.6f) : Range(0.9f, 1.1f)),
                rotation = Range(0f, Mathf.PI * 2f),
                color = new Color(tint, tint, tint, 1f),
                lightmapColor = Color.white
            };
        }

        /// <summary>Whether scenery may stand here without blocking a valley floor, the basin, or a spawn.</summary>
        static bool IsClear(MapShape shape, WV_MapRecipe recipe, float x, float z) {
            float floor = shape.Floor(x, z, out float road, out float centre);
            if (floor > 0.6f || road < recipe.ValleyHalfWidth + recipe.SceneryClearance)
                return false;
            return centre > shape.BasinEdge(x, z) + recipe.SceneryClearance;
        }

        static void Scatter(TerrainData data, float spacing, System.Random random,
            Func<float, float, float, float, bool> accept, Action<float, float> place) {
            for (float z = spacing * 0.5f; z < data.size.z; z += spacing)
            for (float x = spacing * 0.5f; x < data.size.x; x += spacing) {
                float jx = x + ((float)random.NextDouble() - 0.5f) * spacing * 0.9f;
                float jz = z + ((float)random.NextDouble() - 0.5f) * spacing * 0.9f;
                float u = Mathf.Clamp01(jx / data.size.x);
                float v = Mathf.Clamp01(jz / data.size.z);
                if (accept(jx, jz, u, v))
                    place(u, v);
            }
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
        /// Removes what the old canyon map needed: the see-through barrier hugging the canyon walls and
        /// the debug panels on the edge walls. The edge walls' colliders stay to keep everyone on the map.
        /// </summary>
        static void TidyScene(Scene scene, Terrain terrain) {
            foreach (GameObject root in scene.GetRootGameObjects()) {
                // A leftover marker cube at the east spawn.
                if (root.name == "Cube" && root.GetComponents<Component>().Length == 4) {
                    UnityEngine.Object.DestroyImmediate(root);
                    continue;
                }
                foreach (Transform child in root.GetComponentsInChildren<Transform>(true)) {
                    if (child == null)
                        continue;
                    if (child.name == "Terrain Contour Boundary" || child.name == "Temporary Barrier Visual")
                        UnityEngine.Object.DestroyImmediate(child.gameObject);
                }
            }

            terrain.heightmapPixelError = 3f;
            // The Tree Creator models use URP Lit materials, which have no billboard, so they are drawn
            // as meshes at any distance and culled by the camera's far plane instead.
            terrain.treeDistance = 2000f;
            terrain.treeBillboardDistance = 2000f;
            terrain.treeMaximumFullLODCount = 5000;
            EditorUtility.SetDirty(terrain);

            // Below the horizon, past the mountain rim, the sky shows its ground colour. Matching it to
            // the fog makes the far distance read as haze instead of an olive floor.
            Material sky = RenderSettings.skybox;
            if (sky != null && sky.HasProperty(SkyGroundColor)) {
                sky.SetColor(SkyGroundColor, RenderSettings.fogColor);
                EditorUtility.SetDirty(sky);
            }
            EditorSceneManager.MarkSceneDirty(scene);
        }
    }
}
