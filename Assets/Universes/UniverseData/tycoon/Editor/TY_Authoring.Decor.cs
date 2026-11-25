using UnityEngine;
using RyanAssets.Platformer;
using RyanAssets.Rounds;
using static RyanAssets.Authoring.UniverseAuthoringKit;

namespace Universes.UniverseData.tycoon.Editor {
    /// <summary>
    /// The decoration lane: scenery that earns nothing. Every piece is built from the CC0 KayKit models and a
    /// few primitives, carries no colliders (a decoration is never in the way) and keeps clear of the belt,
    /// the pads or the walkway: all but the fairy lights stand on the lawn outside the front wall (see TY_LayoutTests).
    /// </summary>
    public static partial class TY_Authoring {
        const string NatureDir = "nature/", PropDir = "props/";

        static readonly Color[] FlowerColors = {
            new(1f, 0.45f, 0.65f), new(1f, 0.85f, 0.25f), new(0.95f, 0.3f, 0.3f), new(0.7f, 0.45f, 1f), new(1f, 1f, 1f),
        };

        static void BuildDecorations(Prefabs p) {
            var wood = Kit.Mat("DecorWood", new Color(0.52f, 0.34f, 0.2f), smoothness: 0.2f);
            var soil = Kit.Mat("DecorSoil", new Color(0.28f, 0.2f, 0.14f), smoothness: 0.05f);
            var water = Kit.Mat("DecorWater", new Color(0.3f, 0.7f, 1f), emission: 0.25f, smoothness: 0.95f);
            var rim = Kit.Mat("DecorRim", new Color(0.62f, 0.64f, 0.68f), smoothness: 0.2f);
            var bulb = Kit.Mat("DecorBulb", new Color(1f, 0.85f, 0.45f), emission: 2.4f);
            p.items[Index("Flower Beds")] = FlowerBeds(wood, soil);
            p.items[Index("Garden Trees")] = GardenTrees(p);
            p.items[Index("Park Benches")] = ParkBenches(p);
            p.items[Index("Gate Flags")] = GateFlags(p);
            p.items[Index("Duck Pond")] = DuckPond(p, water, rim);
            p.items[Index("Fairy Lights")] = FairyLights(wood, bulb);
            p.items[Index("Hot-Air Balloon")] = HotAirBalloon(wood);
        }

        /// <summary>A decoration's box or ball: a primitive without a collider.</summary>
        static GameObject Bit(PrimitiveType type, string name, Transform parent, Vector3 position, Vector3 scale, Material material) =>
            Primitive(type, name, parent, position, scale, material, false);

        static Material FlowerMat(int i) => Kit.Mat($"Flower{i % FlowerColors.Length}", FlowerColors[i % FlowerColors.Length], emission: 0.15f, smoothness: 0.3f);

        /// <summary>Four planter boxes full of flowers along the front wall, two either side of the gate.</summary>
        static GameObject FlowerBeds(Material wood, Material soil) {
            var root = new GameObject("FlowerBeds");
            var random = new System.Random(3);
            var stem = Kit.Mat("Stem", new Color(0.25f, 0.6f, 0.25f));
            float[] xs = { -13.5f, -8.5f, 8.5f, 13.5f };
            for (int bed = 0; bed < xs.Length; bed++) {
                var box = Empty($"Bed{bed}", root.transform, new Vector3(xs[bed], 0f, 0f));
                Bit(PrimitiveType.Cube, "Box", box.transform, new Vector3(0f, 0.25f, 0f), new Vector3(3.2f, 0.5f, 1f), wood);
                Bit(PrimitiveType.Cube, "Soil", box.transform, new Vector3(0f, 0.51f, 0f), new Vector3(3f, 0.06f, 0.82f), soil);
                for (int i = 0; i < 16; i++) {
                    float x = ((float)random.NextDouble() - 0.5f) * 2.8f, z = ((float)random.NextDouble() - 0.5f) * 0.6f, h = 0.35f + (float)random.NextDouble() * 0.35f;
                    Bit(PrimitiveType.Cylinder, "Stem", box.transform, new Vector3(x, 0.54f + h * 0.5f, z), new Vector3(0.04f, h * 0.5f, 0.04f), stem);
                    Bit(PrimitiveType.Sphere, "Bloom", box.transform, new Vector3(x, 0.56f + h, z), Vector3.one * 0.3f, FlowerMat(bed * 5 + i));
                }
            }
            return Kit.Save(root, "Items", "FlowerBeds");
        }

        /// <summary>A grove either side of the gate: two trees, a boulder and a bush each.</summary>
        static GameObject GardenTrees(Prefabs p) {
            var root = new GameObject("GardenTrees");
            for (int side = -1; side <= 1; side += 2) {
                float x = side * 16f;
                Kay(p, Pack.Medieval, NatureDir + "tree_single_A", root.transform, 6.5f, new Vector3(x - side * 0.9f, 0f, 0.5f), 20f * side);
                Kay(p, Pack.Medieval, NatureDir + "tree_single_B", root.transform, 5f, new Vector3(x + side * 2.2f, 0f, -0.8f), 140f);
                Kay(p, Pack.Medieval, NatureDir + "rock_single_B", root.transform, 1.2f, new Vector3(x + side * 0.4f, 0f, 2.2f), 60f);
                Kay(p, Pack.City, "bush", root.transform, 1.8f, new Vector3(x - side * 2.6f, 0f, -1.8f));
            }
            return Kit.Save(root, "Items", "GardenTrees");
        }

        /// <summary>A bench either side of the path, facing the plaza, each with a bush beside it.</summary>
        static GameObject ParkBenches(Prefabs p) {
            var root = new GameObject("ParkBenches");
            for (int side = -1; side <= 1; side += 2) {
                Kay(p, Pack.City, "bench", root.transform, 2.8f, new Vector3(side * 6.2f, 0f, 0f), BenchYaw);
                Kay(p, Pack.City, "bush", root.transform, 1.5f, new Vector3(side * 8.4f, 0f, -0.2f));
            }
            return Kit.Save(root, "Items", "ParkBenches");
        }

        /// <summary>Yaw that turns the KayKit bench to face +Z (the plaza side of a plot).</summary>
        const float BenchYaw = 0f;

        /// <summary>A flagpole either side of the gate opening, banners flying across the path.</summary>
        static GameObject GateFlags(Prefabs p) {
            var root = new GameObject("GateFlags");
            var pole = Kit.Mat("DecorPole", new Color(0.8f, 0.82f, 0.86f), smoothness: 0.6f, metallic: 0.6f);
            var gold = Kit.Mat("Gold", new Color(1f, 0.78f, 0.2f), emission: 0.35f, smoothness: 0.85f, metallic: 0.95f);
            Color[] banners = { new(0.95f, 0.3f, 0.3f), new(0.3f, 0.55f, 1f) };
            for (int i = 0; i < 2; i++) {
                float side = i == 0 ? -1f : 1f;
                var post = Empty($"Pole{i}", root.transform, new Vector3(side * 4.6f, 0f, 0f));
                Bit(PrimitiveType.Cylinder, "Pole", post.transform, new Vector3(0f, 3.2f, 0f), new Vector3(0.16f, 3.2f, 0.16f), pole);
                Bit(PrimitiveType.Sphere, "Finial", post.transform, new Vector3(0f, 6.5f, 0f), Vector3.one * 0.38f, gold);
                var cloth = Bit(PrimitiveType.Cube, "Banner", post.transform, new Vector3(-side * 1.1f, 5.5f, 0f), new Vector3(2f, 1.3f, 0.05f), Kit.Mat($"DecorBanner{i}", banners[i], emission: 0.2f, smoothness: 0.2f));
                // A gentle sway, so the banners look like they catch a breeze.
                var sway = cloth.AddComponent<Spinner>();
                sway.degreesPerSecond = Vector3.zero;
                sway.bobHeight = 0.06f;
                sway.bobSpeed = 1.3f + i * 0.4f;
            }
            return Kit.Save(root, "Items", "GateFlags");
        }

        static GameObject DuckPond(Prefabs p, Material water, Material rim) {
            var root = new GameObject("DuckPond");
            Bit(PrimitiveType.Cylinder, "Rim", root.transform, new Vector3(0f, 0.08f, 0f), new Vector3(5f, 0.08f, 5f), rim);
            Bit(PrimitiveType.Cylinder, "Water", root.transform, new Vector3(0f, 0.18f, 0f), new Vector3(4.4f, 0.04f, 4.4f), water);
            Kay(p, Pack.Medieval, NatureDir + "waterlily_A", root.transform, 1.2f, new Vector3(-0.9f, 0.2f, 0.5f), 30f);
            Kay(p, Pack.Medieval, NatureDir + "waterlily_B", root.transform, 1.0f, new Vector3(0.7f, 0.2f, -0.9f), 110f);
            Kay(p, Pack.Medieval, NatureDir + "rock_single_C", root.transform, 1.1f, new Vector3(2.0f, 0.1f, 1.0f), 200f);
            Kay(p, Pack.Medieval, NatureDir + "rock_single_A", root.transform, 0.9f, new Vector3(-1.9f, 0.1f, -1.1f), 10f);
            var body = Kit.Mat("DuckBody", new Color(1f, 0.88f, 0.2f), smoothness: 0.4f);
            var beak = Kit.Mat("DuckBeak", new Color(1f, 0.5f, 0.1f), smoothness: 0.4f);
            for (int i = 0; i < 2; i++) {
                var duck = Empty($"Duck{i}", root.transform, new Vector3(i == 0 ? 0.5f : -0.7f, 0.2f, i == 0 ? 0.7f : -0.3f));
                duck.transform.rotation = Quaternion.Euler(0f, i == 0 ? 40f : 250f, 0f);
                Bit(PrimitiveType.Sphere, "Body", duck.transform, new Vector3(0f, 0.2f, 0f), new Vector3(0.6f, 0.42f, 0.78f), body);
                Bit(PrimitiveType.Sphere, "Head", duck.transform, new Vector3(0f, 0.52f, 0.3f), Vector3.one * 0.36f, body);
                Bit(PrimitiveType.Cube, "Beak", duck.transform, new Vector3(0f, 0.48f, 0.52f), new Vector3(0.18f, 0.07f, 0.18f), beak);
                // A slow drift on the water: the Spinner bobs, and turns just a touch.
                var drift = duck.AddComponent<Spinner>();
                drift.degreesPerSecond = new Vector3(0f, i == 0 ? 12f : -9f, 0f);
                drift.bobHeight = 0.02f;
                drift.bobSpeed = 1.6f + i * 0.5f;
            }
            return Kit.Save(root, "Items", "DuckPond");
        }

        /// <summary>Strings of warm bulbs along the back wall and both side walls, over the wall tops.</summary>
        static GameObject FairyLights(Material wood, Material bulb) {
            var root = new GameObject("FairyLights");
            float half = TY_Catalog.PlotSize * 0.5f - 0.9f, top = 4.1f;
            // Back wall (x runs along it), then the side walls (z runs along them); the root sits on the back wall's line.
            StringOfLights(root.transform, "Back", new Vector3(-half, top, 0f), new Vector3(half, top, 0f), 17, wood, bulb);
            StringOfLights(root.transform, "Left", new Vector3(-half, top, 0f), new Vector3(-half, top, 2f * half - 0.5f), 17, wood, bulb);
            StringOfLights(root.transform, "Right", new Vector3(half, top, 0f), new Vector3(half, top, 2f * half - 0.5f), 17, wood, bulb);
            return Kit.Save(root, "Items", "FairyLights");
        }

        /// <summary>A drooping line of bulbs from <paramref name="from"/> to <paramref name="to"/>.</summary>
        static void StringOfLights(Transform parent, string name, Vector3 from, Vector3 to, int bulbs, Material wire, Material bulb) {
            Transform line = Empty(name, parent).transform;
            for (int i = 0; i < bulbs; i++) {
                float t = i / (float)(bulbs - 1);
                float sag = 4f * t * (1f - t) * 0.55f;
                Vector3 at = Vector3.Lerp(from, to, t) + Vector3.down * sag;
                var light = Bit(PrimitiveType.Sphere, "Bulb", line, at, Vector3.one * 0.22f, bulb);
                NoShadows(light);
                if (i > 0) {
                    float tp = (i - 1) / (float)(bulbs - 1), sp = 4f * tp * (1f - tp) * 0.55f;
                    Vector3 previous = Vector3.Lerp(from, to, tp) + Vector3.down * sp;
                    var wireSegment = Bit(PrimitiveType.Cube, "Wire", line, (at + previous) * 0.5f, new Vector3(0.03f, 0.03f, Vector3.Distance(at, previous)), wire);
                    wireSegment.transform.rotation = Quaternion.LookRotation(at - previous);
                    NoShadows(wireSegment);
                }
            }
        }

        /// <summary>A striped balloon tethered to the back strip, bobbing gently over the factory.</summary>
        static GameObject HotAirBalloon(Material wood) {
            var root = new GameObject("HotAirBalloon");
            var float_ = Empty("Float", root.transform, new Vector3(0f, 0.5f, 0f));
            var bob = float_.AddComponent<Spinner>();
            bob.degreesPerSecond = new Vector3(0f, 8f, 0f);
            bob.bobHeight = 0.35f;
            bob.bobSpeed = 0.9f;
            Bit(PrimitiveType.Cube, "Basket", float_.transform, new Vector3(0f, 4.2f, 0f), new Vector3(1.1f, 0.8f, 1.1f), wood);
            var envelope = Empty("Envelope", float_.transform, new Vector3(0f, 8.4f, 0f));
            // Stacked discs, widest at the middle, read as a striped envelope.
            const int bands = 6;
            for (int i = 0; i < bands; i++) {
                float y = (i - (bands - 1) * 0.5f) * 0.62f;
                float diameter = 3.4f * Mathf.Sqrt(Mathf.Max(0.05f, 1f - (y / 1.95f) * (y / 1.95f)));
                Bit(PrimitiveType.Cylinder, "Band", envelope.transform, new Vector3(0f, y, 0f), new Vector3(diameter, 0.32f, diameter), BalloonMat(i));
            }
            for (int corner = 0; corner < 4; corner++) {
                float x = corner % 2 == 0 ? -0.45f : 0.45f, z = corner < 2 ? -0.45f : 0.45f;
                var rope = Bit(PrimitiveType.Cylinder, "Rope", float_.transform, new Vector3(x * 0.8f, 5.6f, z * 0.8f), new Vector3(0.04f, 1.3f, 0.04f), wood);
                rope.transform.localRotation = Quaternion.Euler(z * 8f, 0f, -x * 8f);
            }
            // Tether to the ground so it reads as moored, not floating.
            Bit(PrimitiveType.Cylinder, "Tether", root.transform, new Vector3(0f, 2.3f, 0f), new Vector3(0.05f, 2.3f, 0.05f), Kit.Mat("DecorRope", new Color(0.8f, 0.7f, 0.5f)));
            Bit(PrimitiveType.Cube, "Anchor", root.transform, new Vector3(0f, 0.15f, 0f), new Vector3(0.8f, 0.3f, 0.8f), Kit.Mat("DecorRim", new Color(0.62f, 0.64f, 0.68f), smoothness: 0.2f));
            return Kit.Save(root, "Items", "HotAirBalloon");
        }

        static Material BalloonMat(int i) => Kit.Mat($"Balloon{i % 2}", i % 2 == 0 ? new Color(0.95f, 0.3f, 0.3f) : new Color(1f, 0.95f, 0.85f), smoothness: 0.35f);
    }
}
