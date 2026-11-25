using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using static RyanAssets.Authoring.UniverseAuthoringKit;

namespace Universes.UniverseData.tower_defense.Editor {
    /// <summary>
    /// Builds the Tower Defense battlefield: a flat meadow of KayKit hex tiles ringed by terraced
    /// hills and mountains, a worn dirt road along <see cref="TD_Rules.Lane"/>, the dark tunnel the
    /// enemies drive out of and the defended base, plus trees, rocks and camp props scattered clear
    /// of the road and the build cells. Placement is seeded, so rebuilding gives the same map.
    /// The map is laid out in the original design metres and the whole world is scaled by
    /// <see cref="TD_Rules.WorldScale"/> (the rules' lane, cells, ranges and speeds are scaled the same).
    /// </summary>
    static class TD_MapAuthoring {
        const string KayKit = "Assets/ThirdParty/KayKit/MedievalHexagonPack/";
        const string Nature = KayKit + "decoration/nature/";
        const string Props = KayKit + "decoration/props/";
        const string Military = "Assets/CartoonMilitaryModelPack/Prefebs/Building_Prefebs/";

        /// <summary>Hex tile width (corner to corner) in metres.</summary>
        const float HexWidth = 10f;
        /// <summary>The walkable meadow; outside it the ground rises in terraces.</summary>
        static readonly UnityEngine.Rect Field = UnityEngine.Rect.MinMaxRect(-100f, -76f, 100f, 66f);
        /// <summary>How far the hex ground and scenery extend past the field.</summary>
        const float Margin = 70f;
        const float RoadWidth = 9f;
        const int Seed = 1337;

        /// <summary>The lane and cells in design metres (the world root scales them down again).</summary>
        static Vector3[] DesignLane => System.Array.ConvertAll(TD_Rules.Lane, p => p / TD_Rules.WorldScale);

        /// <summary>Builds the map into the open scene and returns the defended base (it flashes when hit).</summary>
        public static Transform Build(TD_Authoring.Materials m) {
            var world = new GameObject("World");
            var random = new System.Random(Seed);
            Ground(world.transform, random);
            Road(world.transform);
            var baseModel = Landmarks(world.transform);
            Scenery(world.transform, random);
            Boundary(world.transform);
            // Scaled last: the pack models are fitted to their design sizes in unscaled space, then the whole world shrinks.
            world.transform.localScale = Vector3.one * TD_Rules.WorldScale;
            return baseModel;
        }

        // ---------------------------------------------------------------- ground
        /// <summary>A KayKit model placed so its largest side is <paramref name="size"/> metres; null if the pack is missing.</summary>
        static GameObject Kay(string path, Transform parent, Vector3 position, float size, float yaw, bool solid = false) {
            var anchor = Empty(Path.GetFileNameWithoutExtension(path), parent, position);
            var model = Model(path + ".fbx", anchor.transform, size, yaw: yaw);
            if (model == null) {
                Object.DestroyImmediate(anchor);
                return null;
            }
            if (solid) {
                // A trunk-sized box: players bump into trees and rocks but slip past their canopies.
                var b = RendererBounds(model);
                var box = anchor.AddComponent<BoxCollider>();
                box.center = anchor.transform.InverseTransformPoint(b.center);
                box.size = new Vector3(b.size.x * 0.45f, b.size.y, b.size.z * 0.45f);
            }
            GameObjectUtility.SetStaticEditorFlags(anchor, StaticEditorFlags.BatchingStatic);
            foreach (Transform t in anchor.GetComponentsInChildren<Transform>()) GameObjectUtility.SetStaticEditorFlags(t.gameObject, StaticEditorFlags.BatchingStatic);
            return anchor;
        }

        static void Ground(Transform world, System.Random random) {
            var parent = Empty("Ground", world).transform;
            // One invisible collider for the whole walkable meadow (tile meshes have none).
            var floor = Cube("Floor", parent, new Vector3(Field.center.x, -0.5f, Field.center.y), new Vector3(Field.width + 2f * Margin, 1f, Field.height + 2f * Margin), null);
            Object.DestroyImmediate(floor.GetComponent<MeshRenderer>());

            // Measure one tile to learn the pack's orientation and thickness.
            var probe = Kay(KayKit + "tiles/base/hex_grass", parent, Vector3.zero, HexWidth, 0f);
            if (probe == null) {
                Debug.LogWarning("[TowerDefense] KayKit hex tiles missing; using a flat ground.");
                floor.AddComponent<MeshRenderer>().sharedMaterial = TD_Authoring.Kit.Mat("Grass", new Color(0.45f, 0.7f, 0.3f));
                return;
            }
            var bounds = RendererBounds(probe);
            Object.DestroyImmediate(probe);
            bool cornersOnX = bounds.size.x >= bounds.size.z;
            float across = Mathf.Max(bounds.size.x, bounds.size.z); // corner to corner
            float flat = Mathf.Min(bounds.size.x, bounds.size.z);    // edge to edge
            float thickness = bounds.size.y;
            float colStep = across * 0.75f, rowStep = flat;

            // The pack's grass is a bright lime; a tint pulls it toward a meadow green that reads better under the HUD.
            var grass = TD_Authoring.Tinted("HexGrass", KayKit + "tiles/base/hexagons_medieval.png", new Color(0.6f, 0.7f, 0.56f));
            float minX = Field.xMin - Margin, maxX = Field.xMax + Margin, minZ = Field.yMin - Margin, maxZ = Field.yMax + Margin;
            int cols = Mathf.CeilToInt((cornersOnX ? maxX - minX : maxZ - minZ) / colStep) + 1;
            int rows = Mathf.CeilToInt((cornersOnX ? maxZ - minZ : maxX - minX) / rowStep) + 1;
            for (int c = 0; c < cols; c++)
                for (int r = 0; r < rows; r++) {
                    float a = c * colStep, b = r * rowStep + (c % 2 == 1 ? rowStep * 0.5f : 0f);
                    float x = cornersOnX ? minX + a : minX + b, z = cornersOnX ? minZ + b : minZ + a;
                    float outside = Outside(new Vector2(x, z));
                    float lift = outside <= 0f ? 0f : Mathf.Min(4, 1 + Mathf.FloorToInt(outside / 14f)) * 1.6f;
                    lift += outside > 0f ? (float)random.NextDouble() * 0.4f : 0f;
                    // Tiles stand on the floor: their top is at y = 0 inside the field.
                    var tile = Kay(KayKit + "tiles/base/hex_grass", parent, new Vector3(x, lift - thickness, z), HexWidth, 0f);
                    foreach (var renderer in tile.GetComponentsInChildren<MeshRenderer>()) renderer.sharedMaterial = grass;
                }
        }

        /// <summary>Metres a point lies outside the walkable field (0 or less inside).</summary>
        static float Outside(Vector2 p) {
            float dx = Mathf.Max(Field.xMin - p.x, p.x - Field.xMax);
            float dz = Mathf.Max(Field.yMin - p.y, p.y - Field.yMax);
            return Mathf.Max(dx, dz);
        }

        // ---------------------------------------------------------------- road
        static void Road(Transform world) {
            var lane = DesignLane;
            string meshes = TD_Authoring.Root + "/Meshes";
            var road = TD_Authoring.Kit.Mat("Road", Color.white, TD_Authoring.Kit.Tex("Road"), smoothness: 0.05f);
            var verge = TD_Authoring.Kit.Mat("RoadVerge", new Color(0.5f, 0.56f, 0.3f), smoothness: 0.05f);
            var parent = Empty("Road", world).transform;
            Piece("Verge", parent, TD_Meshes.Ribbon(meshes, "RoadVerge", lane, RoadWidth + 2.4f, 0.14f, 0.1f), verge);
            Piece("Road", parent, TD_Meshes.Ribbon(meshes, "Road", lane, RoadWidth, 0.2f, 0.06f), road);

            // Hazard stripe where enemies reach the base.
            Vector3 end = lane[^1], into = (end - lane[^2]).normalized;
            var stripe = Cube("FinishLine", parent, end + new Vector3(0f, 0.26f, 0f), new Vector3(RoadWidth, 0.06f, 1.4f), TD_Authoring.Kit.Mat("Hazard", Color.white, TD_Authoring.Kit.Tex("Hazard"), 0.2f), false);
            stripe.transform.rotation = Quaternion.LookRotation(into);
        }

        static void Piece(string name, Transform parent, Mesh mesh, Material material) {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);
        }

        static float DistanceToRoad(Vector3 p) {
            var lane = DesignLane;
            float best = float.MaxValue;
            for (int i = 0; i + 1 < lane.Length; i++) {
                Vector3 a = lane[i], b = lane[i + 1], ab = b - a;
                float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / ab.sqrMagnitude);
                Vector3 q = a + ab * t;
                best = Mathf.Min(best, Vector2.Distance(new Vector2(p.x, p.z), new Vector2(q.x, q.z)));
            }
            return best;
        }

        // ---------------------------------------------------------------- landmarks
        static Vector3 BaseSpot => DesignLane[^1] + (DesignLane[^1] - DesignLane[^2]).normalized * 12f;

        static Transform Landmarks(Transform world) {
            var lane = DesignLane;
            var parent = Empty("Landmarks", world).transform;
            Tunnel(parent, lane[0], lane[1]);
            var baseModel = MilitaryModel(parent, "MilitaryBase_Prefeb", BaseSpot, 22f, Yaw(lane[^2] - lane[^1]));
#if !UNITY_SERVER
            baseModel.AddComponent<RyanAssets.GameFeel.HitFlash>(); // flashed by the client when enemies get through
#endif
            // No floating "defend the base" sign: it sat behind the status bar, and the HUD's base bar says it.

            // Enemy camp: red flags, ammo and crates around the tunnel mouth.
            Vector3 h = lane[1] + new Vector3(0f, 0f, 2f);
            Kay(Props + "flag_red", parent, h + new Vector3(-9f, 0f, 6f), 5f, 0f);
            Kay(Props + "flag_red", parent, h + new Vector3(9f, 0f, 6f), 5f, 0f);
            Kay(Props + "crate_A_big", parent, h + new Vector3(-11f, 0f, -2f), 2.4f, 20f);
            Kay(Props + "crate_B_small", parent, h + new Vector3(-12.5f, 0f, 0.5f), 1.4f, 45f);
            Kay(Props + "barrel", parent, h + new Vector3(11f, 0f, -1f), 1.6f, 0f);
            Kay(Props + "barrel", parent, h + new Vector3(12.4f, 0f, 0.3f), 1.6f, 30f);
            Kay(Props + "weaponrack", parent, h + new Vector3(10.5f, 0f, -4f), 2.6f, 90f);

            // Our camp: blue flags, tents, supplies around the base.
            Vector3 b = BaseSpot;
            Kay(Props + "flag_blue", parent, b + new Vector3(-13f, 0f, -10f), 5.5f, 0f);
            Kay(Props + "flag_blue", parent, b + new Vector3(13f, 0f, -10f), 5.5f, 0f);
            Kay(Props + "tent", parent, b + new Vector3(-18f, 0f, 2f), 6f, 100f);
            Kay(Props + "tent", parent, b + new Vector3(18f, 0f, 4f), 6f, -80f);
            Kay(Props + "crate_long_A", parent, b + new Vector3(-15f, 0f, -14f), 3f, 15f);
            Kay(Props + "sack", parent, b + new Vector3(-16.5f, 0f, -12f), 1.4f, 0f);
            Kay(Props + "resource_stone", parent, b + new Vector3(16f, 0f, -14f), 3f, 40f);
            Kay(Props + "wheelbarrow", parent, b + new Vector3(19f, 0f, -9f), 2.4f, 210f);
            return baseModel.transform;
        }

        static GameObject MilitaryModel(Transform parent, string model, Vector3 position, float size, float yaw) {
            var anchor = Empty(model, parent, position);
            Model(Military + model + ".prefab", anchor.transform, size, yaw: yaw, keepColliders: true);
            return anchor;
        }

        /// <summary>
        /// A black concrete tunnel the enemies drive out of: they start deep inside, under its roof and out of
        /// sight, so the spawn point is never visible. <paramref name="inside"/> is where they start,
        /// <paramref name="mouth"/> where the road leaves the tunnel.
        /// </summary>
        static void Tunnel(Transform parent, Vector3 inside, Vector3 mouth) {
            var root = Empty("EnemyTunnel", parent, mouth);
            Vector3 out_ = (mouth - inside).normalized;
            root.transform.rotation = Quaternion.LookRotation(out_); // local +z drives out of the tunnel
            float length = Vector3.Distance(inside, mouth) + 4f, width = RoadWidth + 3f, height = 8f;
            var dark = TD_Authoring.Kit.Mat("TunnelDark", new Color(0.015f, 0.015f, 0.02f), smoothness: 0f);
            var stone = TD_Authoring.Kit.Mat("TunnelStone", new Color(0.36f, 0.37f, 0.4f), smoothness: 0.1f);
            var hazard = TD_Authoring.Kit.Mat("Hazard", Color.white, TD_Authoring.Kit.Tex("Hazard"), 0.2f);
            GameObject Part(string name, Vector3 centre, Vector3 size, Material material) {
                var part = Cube(name, root.transform, Vector3.zero, size, material);
                part.transform.localPosition = centre;
                GameObjectUtility.SetStaticEditorFlags(part, StaticEditorFlags.BatchingStatic);
                return part;
            }
            float wall = 2.5f, half = width * 0.5f;
            Part("WallLeft", new Vector3(-half - wall * 0.5f, height * 0.5f, -length * 0.5f), new Vector3(wall, height, length), dark);
            Part("WallRight", new Vector3(half + wall * 0.5f, height * 0.5f, -length * 0.5f), new Vector3(wall, height, length), dark);
            Part("Roof", new Vector3(0f, height + wall * 0.5f, -length * 0.5f), new Vector3(width + wall * 2f, wall, length), dark);
            Part("Back", new Vector3(0f, height * 0.5f, -length - wall * 0.5f), new Vector3(width + wall * 2f, height + wall, wall), dark);
            // Stone portal frame with a hazard band over the opening.
            float pillar = 3f;
            Part("PillarLeft", new Vector3(-half - pillar * 0.5f - 0.2f, (height + wall) * 0.5f, 0.6f), new Vector3(pillar, height + wall, pillar), stone);
            Part("PillarRight", new Vector3(half + pillar * 0.5f + 0.2f, (height + wall) * 0.5f, 0.6f), new Vector3(pillar, height + wall, pillar), stone);
            Part("Lintel", new Vector3(0f, height + wall * 0.5f, 0.6f), new Vector3(width + pillar * 2f + 0.4f, wall + 1f, pillar), stone);
            Part("HazardBand", new Vector3(0f, height - 0.9f, 2.2f), new Vector3(width, 1.6f, 0.2f), hazard);
            // A bank of earth and rock heaped over and behind the bunker, so it reads as a tunnel into a hill.
            var random = new System.Random(Seed + 7);
            string[] hills = { "hill_single_A", "hill_single_B", "hill_single_C" };
            // Beside and behind the bunker, clear of its interior.
            float side = half + wall + 11f;
            Vector3[] mounds = {
                new(-side, 0f, -length * 0.3f), new(side, 0f, -length * 0.3f), new(-side, 0f, -length * 0.85f), new(side, 0f, -length * 0.85f),
                new(-8f, 0f, -length - 12f), new(8f, 0f, -length - 14f),
            };
            for (int i = 0; i < mounds.Length; i++)
                Kay(Nature + hills[random.Next(hills.Length)], root.transform, mounds[i], 24f + i % 3 * 4f, (float)random.NextDouble() * 360f);
        }

        static float Yaw(Vector3 dir) => Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;

        // ---------------------------------------------------------------- scenery
        static void Scenery(Transform world, System.Random random) {
            var parent = Empty("Scenery", world).transform;
            var blocked = new List<(Vector3 centre, float radius)> {
                (DesignLane[0], 30f), (DesignLane[1], 20f), (BaseSpot, 24f), (DesignLane[^1] + new Vector3(-8f, 0f, -12f), 10f),
            };
            foreach (var slot in TD_Rules.Slots) blocked.Add((slot / TD_Rules.WorldScale, 4.5f));
            bool Clear(Vector3 p, float pad) {
                if (DistanceToRoad(p) < RoadWidth * 0.5f + 3f + pad) return false;
                foreach (var (c, r) in blocked) if (Vector2.Distance(new Vector2(p.x, p.z), new Vector2(c.x, c.z)) < r + pad) return false;
                return true;
            }
            float Range(float a, float b) => a + (float)random.NextDouble() * (b - a);
            T Pick<T>(T[] items) => items[random.Next(items.Length)];

            string[] trees = { "trees_A_large", "trees_A_medium", "trees_B_large", "trees_B_medium", "tree_single_A", "tree_single_B" };
            string[] small = { "trees_A_small", "trees_B_small", "tree_single_A", "tree_single_B" };
            string[] rocks = { "rock_single_A", "rock_single_B", "rock_single_C", "rock_single_D", "rock_single_E" };
            string[] hills = { "hills_A_trees", "hills_B_trees", "hills_C_trees", "hill_single_A", "hill_single_B", "hill_single_C" };
            string[] mountains = { "mountain_A_grass_trees", "mountain_B_grass_trees", "mountain_C_grass_trees", "mountain_A_grass", "mountain_B" };

            // Inside the meadow: copses and rocks away from the road and pads.
            for (int placed = 0, tries = 0; placed < 46 && tries < 2000; tries++) {
                var p = new Vector3(Range(Field.xMin + 4f, Field.xMax - 4f), 0f, Range(Field.yMin + 4f, Field.yMax - 4f));
                bool tree = placed % 3 != 2;
                if (!Clear(p, tree ? 3f : 1f)) continue;
                Kay(Nature + (tree ? Pick(placed % 2 == 0 ? trees : small) : Pick(rocks)), parent, p, tree ? Range(6f, 10f) : Range(1.6f, 3.2f), Range(0f, 360f), solid: true);
                placed++;
            }

            // Terraces: hills and forest on the first rings, mountains on the outer one.
            float LiftAt(Vector3 p) {
                float o = Outside(new Vector2(p.x, p.z));
                return o <= 0f ? 0f : Mathf.Min(4, 1 + Mathf.FloorToInt(o / 14f)) * 1.6f;
            }
            for (int i = 0; i < 160; i++) {
                var p = new Vector3(Range(Field.xMin - Margin + 6f, Field.xMax + Margin - 6f), 0f, Range(Field.yMin - Margin + 6f, Field.yMax + Margin - 6f));
                float o = Outside(new Vector2(p.x, p.z));
                if (o < 6f) continue;
                p.y = LiftAt(p);
                if (o > 42f) Kay(Nature + Pick(mountains), parent, p, Range(26f, 40f), Range(0f, 360f));
                else if (o > 20f) Kay(Nature + Pick(i % 2 == 0 ? hills : trees), parent, p, i % 2 == 0 ? Range(14f, 22f) : Range(8f, 12f), Range(0f, 360f));
                else Kay(Nature + Pick(i % 4 == 0 ? rocks : trees), parent, p, i % 4 == 0 ? Range(2f, 4f) : Range(7f, 11f), Range(0f, 360f));
            }

            // Clouds drifting high over the map.
            for (int i = 0; i < 14; i++) {
                var p = new Vector3(Range(-190f, 190f), Range(85f, 120f), Range(-150f, 150f));
                var cloud = Kay(Nature + (i % 2 == 0 ? "cloud_big" : "cloud_small"), parent, p, Range(16f, 30f), Range(0f, 360f));
                if (cloud != null) NoShadows(cloud);
            }
        }

        /// <summary>Invisible walls at the meadow's edge so players can't walk into the terraces.</summary>
        static void Boundary(Transform world) {
            var parent = Empty("Bounds", world).transform;
            float h = 12f, t = 2f;
            void Wall(string name, Vector3 centre, Vector3 size) {
                var wall = Cube(name, parent, centre, size, null);
                Object.DestroyImmediate(wall.GetComponent<MeshRenderer>());
            }
            Wall("North", new Vector3(Field.center.x, h * 0.5f, Field.yMax + t * 0.5f), new Vector3(Field.width + 2f * t, h, t));
            Wall("South", new Vector3(Field.center.x, h * 0.5f, Field.yMin - t * 0.5f), new Vector3(Field.width + 2f * t, h, t));
            Wall("East", new Vector3(Field.xMax + t * 0.5f, h * 0.5f, Field.center.y), new Vector3(t, h, Field.height));
            Wall("West", new Vector3(Field.xMin - t * 0.5f, h * 0.5f, Field.center.y), new Vector3(t, h, Field.height));
        }
    }
}
