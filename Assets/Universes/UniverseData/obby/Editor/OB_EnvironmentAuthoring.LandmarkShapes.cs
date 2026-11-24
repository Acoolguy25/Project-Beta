#if !UNITY_SERVER
using UnityEngine;
using Universes.UniverseData.obby.Client;
using B = Universes.UniverseData.obby.Editor.OB_EnvMeshBuilder;

namespace Universes.UniverseData.obby.Editor {
    // The thirteen landmarks, two or three per tier, each returning its footprint radius (m).
    // Sizes are in metres; the character is 5.6 m tall and landmarks sit 260 m or more from the course,
    // so they are built big (100 to 230 m tall) and simple enough to read as silhouettes.
    public static partial class OB_EnvironmentAuthoring {
        // ---------------------------------------------------------------- Basic: marble and gold in the clouds
        static float SkyCastle(LandmarkKit k) {
            const float island = 62f, wallRadius = 34f, wallHeight = 14f, keepSize = 26f, keepHeight = 38f;
            var b = k.body;
            var p = k.p;
            BaseIsland(k, island, 95f);
            Color roof = p.Cloth(1), window = p.glow;
            // Curtain wall: eight towers joined by walls with battlements.
            const int corners = 8;
            for (int i = 0; i < corners; i++) {
                float a0 = i * Mathf.PI * 2f / corners, a1 = (i + 1) * Mathf.PI * 2f / corners;
                Vector3 c0 = new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)) * wallRadius;
                Vector3 c1 = new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1)) * wallRadius;
                Vector3 mid = (c0 + c1) * 0.5f;
                float length = Vector3.Distance(c0, c1);
                b.Push(mid, Quaternion.LookRotation(c1 - c0));
                b.Box(Vector3.up * (wallHeight * 0.5f), new Vector3(4f, wallHeight, length), p.frame);
                for (int m = 0; m < 5; m++)
                    b.Box(new Vector3(0f, wallHeight + 0.9f, Mathf.Lerp(-length * 0.4f, length * 0.4f, m / 4f)), new Vector3(4.4f, 1.8f, 2f), p.frameDark);
                b.Pop();
                Tower(b, c0, 5f, wallHeight + 12f, 12f, p.frame, p.capBand, roof);
            }
            // Gatehouse facing the course.
            b.Box(new Vector3(0f, wallHeight * 0.75f, wallRadius), new Vector3(14f, wallHeight * 1.5f, 7f), p.frame);
            b.Box(new Vector3(0f, 5f, wallRadius + 3.6f), new Vector3(6f, 10f, 0.4f), p.frameDark);
            // The keep with corner towers, windows and the great spire.
            b.Box(new Vector3(0f, keepHeight * 0.5f, 0f), new Vector3(keepSize, keepHeight, keepSize), p.frame, p.frameDark);
            for (int row = 0; row < 3; row++)
                for (int col = -1; col <= 1; col++) {
                    float y = 10f + row * 10f;
                    b.Box(new Vector3(col * 7f, y, keepSize * 0.5f + 0.1f), new Vector3(2.4f, 4.5f, 0.4f), window);
                    b.Box(new Vector3(keepSize * 0.5f + 0.1f, y, col * 7f), new Vector3(0.4f, 4.5f, 2.4f), window);
                }
            for (int sx = -1; sx <= 1; sx += 2)
                for (int sz = -1; sz <= 1; sz += 2)
                    Tower(b, new Vector3(sx, 0f, sz) * (keepSize * 0.5f), 4.5f, keepHeight + 20f, 15f, p.frame, p.capBand, roof);
            Tower(b, new Vector3(0f, keepHeight, 0f), 7f, 50f, 34f, p.frame, p.capBand, p.capBand, 12);
            Flag(b, new Vector3(0f, keepHeight + 50f + 3.5f + 34f, 0f), 10f, 9f, p.capBand, p.Cloth(0));
            for (int sx = -1; sx <= 1; sx += 2)
                for (int sz = -1; sz <= 1; sz += 2)
                    Flag(b, new Vector3(sx, 0f, sz) * (keepSize * 0.5f) + Vector3.up * (keepHeight + 20f + 2.25f + 15f), 6f, 6f, p.capBand, p.Cloth(2));
            EdgeFalls(k, island, 9f, -20f, 25f);
            FloatingChunks(k, 4, new Vector2(island + 10f, island + 30f), new Vector2(-30f, 40f), new Vector2(5f, 9f), new Vector3(0f, 3f, 0f));
            return island + 25f;
        }

        static float WindmillIsle(LandmarkKit k) {
            const float island = 40f, towerHeight = 42f, hubHeight = 36f, sailLength = 28f;
            var b = k.body;
            var p = k.p;
            BaseIsland(k, island, 60f, 12);
            b.Prism(Vector3.zero, 10f, 6.5f, towerHeight, 8, p.frame);
            b.Prism(Vector3.up * towerHeight, 7.5f, 7.5f, 1.5f, 8, p.capBand);
            b.Prism(Vector3.up * (towerHeight + 1.5f), 8f, 0f, 13f, 8, p.Cloth(0));
            b.Box(new Vector3(0f, 3.5f, 9.6f), new Vector3(4f, 7f, 1f), p.frameDark);
            for (int i = 0; i < 3; i++) b.Box(new Vector3(0f, 14f + i * 8f, 9f - i * 0.9f), new Vector3(2.2f, 3f, 0.6f), p.glow);
            // Cottages and orchards round the mill.
            for (int i = 0; i < 4; i++) {
                float a = Mathf.PI * 2f * (i + 0.5f) / 4f;
                Vector3 at = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 24f;
                b.Box(at + Vector3.up * 3.5f, new Vector3(8f, 7f, 6f), p.frame);
                b.Prism(at + Vector3.up * 7f, 6.2f, 0f, 5f, 4, p.Cloth(i + 1), Mathf.PI * 0.25f);
                b.Box(at + new Vector3(0f, 3f, 3.05f), new Vector3(1.6f, 2f, 0.2f), p.glow);
                Tree(b, at * 1.35f + new Vector3(4f, 0f, 0f), k.random.Range(7f, 10f), p.frameDark, B.Solid(OB_Palette.Grass), ref k.random);
            }
            // Sails: four lattice blades with cloth, spinning about the hub's axis (+Z).
            var s = k.part;
            s.Prism(new Vector3(0f, 0f, -2f), 1.6f, 1.2f, 4f, 8, p.capBand);
            for (int blade = 0; blade < 4; blade++) {
                s.Push(Vector3.zero, Quaternion.Euler(0f, 0f, blade * 90f));
                s.Box(new Vector3(0f, sailLength * 0.5f, 0f), new Vector3(1f, sailLength, 0.6f), p.frameDark);
                s.Box(new Vector3(2.4f, sailLength * 0.58f, 0.1f), new Vector3(4.2f, sailLength * 0.78f, 0.25f), p.frame);
                s.Pop();
            }
            s.Push(Vector3.zero, Quaternion.Euler(-90f, 0f, 0f));
            s.Prism(Vector3.zero, 1.2f, 0.4f, 3f, 8, p.capBand);
            s.Pop();
            var sails = k.Part("Sails", new Vector3(0f, hubHeight, 11f));
            sails.spinAxis = Vector3.forward;
            sails.spinSpeed = 24f;
            EdgeFalls(k, island, 7f, 10f);
            return island + sailLength * 0.5f;
        }

        static float BalloonFleet(LandmarkKit k) {
            const int balloons = 5;
            const float spread = 55f;
            var p = k.p;
            for (int i = 0; i < balloons; i++) {
                var s = k.part;
                float size = k.random.Range(8f, 13f);
                float[] ys = { 2.4f, 2.15f, 1.75f, 1.25f, 0.7f, 0.25f, 0f };
                float[] radii = { 0f, 0.55f, 0.92f, 1f, 0.82f, 0.42f, 0.2f };
                for (int r = 0; r < ys.Length; r++) {
                    ys[r] *= size;
                    radii[r] *= size;
                }
                Color[] stripes = { p.Cloth(i), p.frame, p.Cloth(i + 1), p.frame, p.Cloth(i), p.capBand };
                s.Lathe(Vector3.zero, ys, radii, 12, stripes, p.frame, ref k.random);
                s.Prism(Vector3.down * 0.4f, size * 0.12f, size * 0.15f, 0.4f, 8, p.glow);
                float basket = size * 0.65f;
                for (int c = 0; c < 4; c++) {
                    float a = Mathf.PI * 0.5f * c + Mathf.PI * 0.25f;
                    Vector3 up = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * (size * 0.2f);
                    Vector3 down = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * (size * 0.12f) + Vector3.down * basket;
                    s.Push((up + down) * 0.5f, Quaternion.FromToRotation(Vector3.up, up - down));
                    s.Box(Vector3.zero, new Vector3(0.12f, (up - down).magnitude, 0.12f), p.frameDark);
                    s.Pop();
                }
                s.Box(Vector3.down * (basket + size * 0.1f), new Vector3(size * 0.3f, size * 0.22f, size * 0.3f), p.frameDark, p.frame);
                Vector3 at = k.random.OnCircle() * (spread * Mathf.Sqrt((i + 0.5f) / balloons)) + Vector3.up * k.random.Range(10f, 70f);
                var motion = k.Part($"Balloon{i}", at, Quaternion.Euler(0f, k.random.Range(0f, 360f), 0f));
                motion.bobOffset = new Vector3(0f, k.random.Range(2f, 4f), 0f);
                motion.bobPeriod = k.random.Range(7f, 12f);
                motion.rockDegrees = k.random.Range(1.5f, 3f);
                motion.rockPeriod = k.random.Range(5f, 8f);
                motion.spinSpeed = k.random.Range(-2f, 2f);
            }
            return spread + 15f;
        }

        // ---------------------------------------------------------------- Easy: a green, watery world
        static float WorldTree(LandmarkKit k) {
            const float island = 66f, trunkTop = 95f;
            var b = k.body;
            var p = k.p;
            BaseIsland(k, island, 100f);
            Color bark = p.frameDark, barkLight = p.frame, leaves = p.top;
            b.Lathe(Vector3.zero, new[] { trunkTop, 70f, 40f, 15f, 5f, -1f }, new[] { 7f, 9f, 10f, 12f, 17f, 24f }, 9,
                new[] { bark, barkLight, bark, barkLight, bark }, bark, ref k.random, 0.12f);
            // Buttress roots spreading over the island.
            for (int i = 0; i < 7; i++) {
                float a = i * Mathf.PI * 2f / 7f + k.random.Range(-0.2f, 0.2f);
                Vector3 outward = new(Mathf.Cos(a), 0f, Mathf.Sin(a));
                b.Push(outward * 20f, Quaternion.LookRotation(outward) * Quaternion.Euler(70f, 0f, 0f));
                b.Prism(Vector3.zero, 4.5f, 1f, 22f, 6, bark);
                b.Pop();
            }
            // Boughs out to the crown, then the crown itself.
            var crownAt = new Vector3[8];
            for (int i = 0; i < crownAt.Length; i++) {
                float a = i * Mathf.PI * 2f / crownAt.Length + k.random.Range(-0.3f, 0.3f);
                Vector3 outward = new(Mathf.Cos(a), 0f, Mathf.Sin(a));
                float reach = k.random.Range(22f, 38f), y = k.random.Range(105f, 135f);
                crownAt[i] = outward * reach + Vector3.up * y;
                Vector3 from = Vector3.up * k.random.Range(65f, trunkTop);
                Vector3 along = crownAt[i] - from;
                b.Push(from, Quaternion.FromToRotation(Vector3.up, along));
                b.Prism(Vector3.zero, 3.2f, 1.4f, along.magnitude, 6, bark);
                b.Pop();
            }
            b.Sphere(Vector3.up * 140f, new Vector3(38f, 26f, 38f), 5, 10, leaves, B.Shade(leaves, 1.12f));
            for (int i = 0; i < crownAt.Length; i++) {
                float r = k.random.Range(20f, 28f);
                b.Sphere(crownAt[i], new Vector3(r, r * 0.75f, r), 4, 8, B.Shade(leaves, k.random.Range(0.85f, 1.05f)), B.Shade(leaves, 1.15f));
                // Glowing fruit under the leaves.
                for (int f = 0; f < 3; f++)
                    b.Sphere(crownAt[i] + k.random.OnCircle() * r * 0.6f + Vector3.down * (r * 0.6f), Vector3.one * 1.4f, 3, 5, p.glow);
            }
            // Vines hanging from the crown.
            for (int i = 0; i < 14; i++) {
                Vector3 at = crownAt[i % crownAt.Length] + k.random.OnCircle() * k.random.Range(5f, 16f) + Vector3.down * 12f;
                b.Prism(at, 0.5f, 0.2f, -k.random.Range(12f, 30f), 4, B.Shade(leaves, 0.8f));
            }
            EdgeFalls(k, island, 10f, -25f, 15f);
            return island + 20f;
        }

        static float WaterfallCliffs(LandmarkKit k) {
            const float island = 60f, midTop = 45f, midRadius = 40f, highTop = 88f, highRadius = 22f;
            var b = k.body;
            var p = k.p;
            BaseIsland(k, island, 85f);
            Color[] strata = { p.rock, p.rockDark, p.rock, B.Shade(p.rock, 1.1f) };
            // Two stacked plateaus, each capped with grass, set back from the course so their falls show.
            var midAt = new Vector3(0f, 0f, -10f);
            var highAt = new Vector3(-6f, 0f, -16f);
            b.Lathe(midAt, new[] { midTop, midTop - 2f, midTop * 0.5f, 0f }, new[] { midRadius, midRadius * 1.02f, midRadius * 1.08f, midRadius * 1.15f }, 11,
                new[] { p.capBand, strata[0], strata[1] }, p.top, ref k.random, 0.08f);
            b.Lathe(highAt, new[] { highTop, highTop - 2f, (highTop + midTop) * 0.5f, midTop - 1f }, new[] { highRadius, highRadius * 1.02f, highRadius * 1.1f, highRadius * 1.2f }, 9,
                new[] { p.capBand, strata[2], strata[1] }, p.top, ref k.random, 0.08f);
            for (int i = 0; i < 9; i++) {
                Vector3 at = i < 3 ? highAt + k.random.OnCircle() * (highRadius * 0.6f) + Vector3.up * highTop
                    : i < 6 ? midAt + k.random.OnCircle() * (midRadius * 0.75f) + Vector3.up * midTop
                    : k.random.OnCircle() * (island * 0.8f);
                Tree(b, at, k.random.Range(8f, 13f), p.frameDark, p.top, ref k.random);
            }
            // Pools at each lip, then the cascade from top to bottom and off the island.
            Color pool = B.Glow(k.world.fallColor, 0.15f);
            b.Prism(highAt + new Vector3(0f, highTop + 0.05f, highRadius * 0.55f), 6f, 6f, 0.2f, 10, pool);
            b.Prism(midAt + new Vector3(4f, midTop + 0.05f, midRadius * 0.6f), 9f, 9f, 0.2f, 10, pool);
            k.Fall(highAt + new Vector3(0f, highTop - 0.5f, highRadius), Vector3.forward, 7f, 4f);
            k.Fall(midAt + new Vector3(4f, midTop - 0.5f, midRadius * 1.02f), Vector3.forward, 10f, 4f);
            k.Fall(midAt + new Vector3(-18f, midTop - 0.5f, midRadius * 0.85f), new Vector3(-0.5f, 0f, 1f), 6f, 4f);
            EdgeFalls(k, island, 12f, -30f, 0f, 30f);
            return island + 15f;
        }

        // ---------------------------------------------------------------- Medium: desert monuments at golden hour
        static float Pyramids(LandmarkKit k) {
            const float island = 88f;
            var b = k.body;
            var p = k.p;
            BaseIsland(k, island, 90f);
            Color gold = B.Glow(OB_Palette.Gold, 0.4f);
            void Pyramid(Vector3 at, float half, float height) {
                const int steps = 6;
                // A stepped body under smooth faces reads well from far and close.
                for (int i = 0; i < steps; i++) {
                    float t0 = i / (float)steps, t1 = (i + 1f) / steps;
                    b.Prism(at + Vector3.up * (t0 * height * 0.9f), half * (1f - t0 * 0.9f) * 1.414f, half * (1f - t1 * 0.9f) * 1.414f,
                        height * 0.9f / steps, 4, i % 2 == 0 ? p.frame : B.Shade(p.frame, 0.94f), Mathf.PI * 0.25f);
                }
                b.Prism(at + Vector3.up * (height * 0.9f), half * 0.1f * 1.414f, 0f, height * 0.1f, 4, gold, Mathf.PI * 0.25f);
                b.Box(at + new Vector3(0f, height * 0.08f, half * 0.98f), new Vector3(half * 0.18f, height * 0.16f, 1f), p.frameDark);
            }
            Pyramid(new Vector3(0f, 0f, -12f), 42f, 70f);
            Pyramid(new Vector3(-50f, 0f, -30f), 22f, 36f);
            Pyramid(new Vector3(48f, 0f, -36f), 17f, 28f);
            // An avenue of obelisks to the course-facing edge, with palms between.
            for (int side = -1; side <= 1; side += 2)
                for (int i = 0; i < 3; i++) {
                    Vector3 at = new(side * 14f, 0f, 40f + i * 14f);
                    b.Prism(at, 2.6f, 1.8f, 26f, 4, p.frame, Mathf.PI * 0.25f);
                    b.Prism(at + Vector3.up * 26f, 1.8f, 0f, 3f, 4, gold, Mathf.PI * 0.25f);
                    Palm(b, at + new Vector3(side * 8f, 0f, 6f), k.random.Range(10f, 14f), p.frameDark, B.Solid(OB_Palette.Grass), ref k.random);
                }
            EdgeFalls(k, island, 11f, -35f, 35f);
            return island + 15f;
        }

        static void Palm(B b, Vector3 at, float height, Color trunk, Color leaves, ref OB_EnvRandom random) {
            float lean = random.Range(-12f, 12f);
            b.Push(at, Quaternion.Euler(lean, random.Range(0f, 360f), 0f));
            b.Prism(Vector3.zero, 0.7f, 0.45f, height, 6, trunk);
            for (int i = 0; i < 6; i++) {
                b.Push(Vector3.up * height, Quaternion.Euler(0f, i * 60f, 0f) * Quaternion.Euler(-20f, 0f, 0f));
                b.Box(new Vector3(0f, 0f, 3.2f), new Vector3(1.6f, 0.2f, 6.5f), leaves);
                b.Pop();
            }
            b.Pop();
        }

        static float MesaArch(LandmarkKit k) {
            const float island = 74f, mesaTop = 110f, archRadius = 36f;
            var b = k.body;
            var p = k.p;
            BaseIsland(k, island, 85f);
            Color[] strata = { p.rock, p.capBand, p.rockDark, p.rock, p.capBand, p.rockDark };
            float[] ys = { mesaTop, mesaTop - 3f, 88f, 64f, 40f, 18f, -1f };
            float[] radii = { 15f, 16f, 18f, 19f, 21f, 24f, 28f };
            for (int side = -1; side <= 1; side += 2)
                b.Lathe(new Vector3(side * archRadius, 0f, -6f), ys, radii, 9, strata, p.capBand, ref k.random, 0.08f);
            // The natural arch: blocks along a half circle between the two mesas.
            const int blocks = 16;
            var centre = new Vector3(0f, mesaTop - 20f, -6f);
            for (int i = 0; i <= blocks; i++) {
                float a = Mathf.PI * i / blocks;
                Vector3 at = centre + new Vector3(-Mathf.Cos(a) * archRadius, Mathf.Sin(a) * archRadius * 0.55f, 0f);
                b.Push(at, Quaternion.Euler(0f, 0f, Mathf.Rad2Deg * Mathf.Atan2(Mathf.Cos(a) * 0.55f, Mathf.Sin(a))));
                b.Box(Vector3.zero, new Vector3(9f, 11f - Mathf.Sin(a) * 3f, 13f), i % 3 == 1 ? p.rockDark : p.rock, p.capBand);
                b.Pop();
            }
            // Cacti and boulders on the plateaus and the island.
            Color cactus = B.Solid(OB_Themes.Get(OB_Tier.Medium).propColor);
            for (int i = 0; i < 8; i++) {
                Vector3 at = i < 2 ? new Vector3((i * 2 - 1) * archRadius, mesaTop, -6f) + k.random.OnCircle() * 7f
                    : k.random.OnCircle() * k.random.Range(island * 0.35f, island * 0.85f);
                if (i % 3 == 2) b.Rock(at, Vector3.one * k.random.Range(3f, 6f), ref k.random, p.capBand, p.rock);
                else Cactus(b, at, k.random.Range(5f, 9f), cactus, p.Cloth(0), ref k.random);
            }
            EdgeFalls(k, island, 9f, 0f);
            return island + 12f;
        }

        // ---------------------------------------------------------------- Hard: polar twilight
        static float IceMountain(LandmarkKit k) {
            const float island = 86f;
            var b = k.body;
            var p = k.p;
            BaseIsland(k, island, 75f);
            Color snow = p.top, ice = B.Glow(p.frame, 0.15f), rock = p.rock, dark = p.rockDark;
            void Mountain(Vector3 at, float height, float radius) {
                float[] ys = { height, height * 0.86f, height * 0.68f, height * 0.48f, height * 0.26f, height * 0.08f, -1f };
                float[] radii = { 0f, radius * 0.18f, radius * 0.38f, radius * 0.58f, radius * 0.76f, radius * 0.92f, radius };
                b.Lathe(at, ys, radii, 11, new[] { snow, snow, ice, rock, dark, rock }, snow, ref k.random, 0.16f);
            }
            Mountain(new Vector3(0f, 0f, -10f), 230f, 78f);
            Mountain(new Vector3(-52f, 0f, -20f), 120f, 40f);
            Mountain(new Vector3(48f, 0f, -28f), 150f, 44f);
            for (int i = 0; i < 16; i++) {
                Vector3 at = k.random.OnCircle() * k.random.Range(island * 0.6f, island * 0.95f);
                if (at.z < 0f) at.z *= -1f;   // the forest is on the side that faces the course
                Pine(b, at, k.random.Range(10f, 18f), p.frameDark, B.Solid(OB_Palette.Grass * 0.6f), snow);
            }
            EdgeFalls(k, island, 12f, -25f, 20f);
            return island + 15f;
        }

        static float GlacierSpires(LandmarkKit k) {
            const float island = 52f;
            var b = k.body;
            var p = k.p;
            BaseIsland(k, island, 70f);
            Color ice = B.Glow(p.frame, 0.25f), deep = B.Glow(p.frameDark, 0.3f);
            for (int i = 0; i < 11; i++) {
                Vector3 at = i == 0 ? Vector3.zero : k.random.OnCircle() * k.random.Range(10f, island * 0.75f);
                float height = i == 0 ? 170f : k.random.Range(45f, 120f), radius = i == 0 ? 11f : k.random.Range(4f, 8f);
                b.Push(at, Quaternion.Euler(k.random.Range(-8f, 8f), k.random.Range(0f, 60f), k.random.Range(-8f, 8f)));
                Crystal(b, Vector3.zero, radius, height, i % 3 == 0 ? deep : ice);
                b.Pop();
            }
            // The same spires mirrored under the island, as if it froze from a single crystal.
            for (int i = 0; i < 6; i++) {
                Vector3 at = k.random.OnCircle() * k.random.Range(0f, island * 0.4f) + Vector3.down * 30f;
                b.Prism(at, k.random.Range(4f, 7f), 0f, -k.random.Range(40f, 80f), 6, ice);
            }
            FloatingChunks(k, 5, new Vector2(island + 5f, island + 25f), new Vector2(10f, 90f), new Vector2(4f, 8f), new Vector3(0f, 2.5f, 0f));
            EdgeFalls(k, island, 8f, -10f);
            return island + 22f;
        }

        // ---------------------------------------------------------------- Insane: fire under an eclipse
        static float Volcano(LandmarkKit k) {
            const float island = 92f, rim = 150f, crater = 24f;
            var b = k.body;
            var p = k.p;
            BaseIsland(k, island, 80f);
            Color rock = p.rock, dark = p.rockDark, magma = p.glow, ash = p.top;
            b.Lathe(Vector3.zero, new[] { rim, rim - 8f, 105f, 60f, 22f, -1f }, new[] { crater, crater * 1.2f, 48f, 68f, 82f, 90f }, 13,
                new[] { dark, rock, dark, rock, dark }, magma, ref k.random, 0.12f);
            b.Push(Vector3.up * (rim + 1f));
            b.Torus(Vector3.zero, crater + 1.5f, 3.5f, 14, 5, ash);
            b.Pop();
            // Lava rivers down the course-facing slope.
            for (int r = 0; r < 4; r++) {
                float a = Mathf.Deg2Rad * (60f + r * 22f + k.random.Range(-6f, 6f));
                Vector3 outward = new(Mathf.Cos(a), 0f, Mathf.Sin(a));
                for (int s = 0; s < 6; s++) {
                    float t0 = s / 6f, t1 = (s + 1f) / 6f;
                    Vector3 p0 = outward * Mathf.Lerp(crater, 84f, t0) + Vector3.up * Mathf.Lerp(rim, 18f, t0);
                    Vector3 p1 = outward * Mathf.Lerp(crater, 84f, t1) + Vector3.up * Mathf.Lerp(rim, 18f, t1);
                    Vector3 along = p1 - p0;
                    b.Push((p0 + p1) * 0.5f + outward * 2.2f, Quaternion.LookRotation(along, Vector3.up));
                    b.Box(Vector3.zero, new Vector3(Mathf.Lerp(4f, 8f, t0), 1.2f, along.magnitude * 1.05f), magma);
                    b.Pop();
                }
            }
            for (int i = 0; i < 6; i++) Vent(b, k.random.OnCircle() * k.random.Range(island * 0.7f, island * 0.92f), k.random.Range(4f, 7f), rock, magma);
            EdgeFalls(k, island, 10f, 50f, 70f);
            PlumeSmoke(k.root.transform, Vector3.up * rim, crater, B.Shade(dark, 1.4f));
            RisingSparks(k.root.transform, Vector3.up * rim, crater * 0.8f, 120f, k.world.weather.a, OB_Palette.Gold);
            return island + 15f;
        }

        static float BasaltSpires(LandmarkKit k) {
            const float island = 55f;
            var b = k.body;
            var p = k.p;
            BaseIsland(k, island, 75f);
            Color crack = p.glow;
            for (int i = 0; i < 26; i++) {
                float distance = Mathf.Sqrt(k.random.Value()) * island * 0.8f;
                Vector3 at = k.random.OnCircle() * distance;
                float height = Mathf.Lerp(130f, 20f, distance / (island * 0.8f)) * k.random.Range(0.7f, 1.1f), radius = k.random.Range(3.5f, 6f);
                b.Prism(at, radius, radius, height, 6, i % 2 == 0 ? p.rock : p.rockDark, p.capBand, p.rockDark);
                // A glowing seam up the column's course-facing side.
                b.Box(at + new Vector3(0f, height * 0.5f, radius * 0.87f), new Vector3(radius * 0.25f, height * 0.8f, 0.2f), crack);
            }
            FloatingChunks(k, 5, new Vector2(island + 5f, island + 25f), new Vector2(20f, 110f), new Vector2(5f, 10f), new Vector3(0f, 3f, 0f));
            EdgeFalls(k, island, 8f, -15f);
            RisingSparks(k.root.transform, Vector3.up * 20f, island * 0.7f, 90f, k.world.weather.a, crack);
            return island + 22f;
        }

        // ---------------------------------------------------------------- Extreme: the void
        static float ShatteredPlanet(LandmarkKit k) {
            const float core = 26f, shell = 52f, ring = 90f, height = 110f;
            var p = k.p;
            // The core never moves; the shell fragments and the debris ring turn about it.
            var c = k.part;
            c.Sphere(Vector3.zero, Vector3.one * core, 6, 12, p.glow);
            c.Sphere(Vector3.zero, Vector3.one * (core * 0.6f), 4, 8, B.Glow(p.Cloth(0), 1f));
            var coreMotion = k.Part("Core", Vector3.up * height);
            coreMotion.bobOffset = new Vector3(0f, 2f, 0f);
            coreMotion.bobPeriod = 9f;
            var s = k.part;
            for (int i = 0; i < 14; i++) {
                Vector3 direction = k.random.Rotation() * Vector3.forward;
                float size = k.random.Range(12f, 22f);
                s.Push(direction * shell, Quaternion.LookRotation(direction));
                s.Rock(Vector3.zero, new Vector3(size, size, size * 0.45f), ref k.random, p.rockTop, p.rockSide);
                s.Box(new Vector3(0f, 0f, -size * 0.45f), new Vector3(size * 0.15f, size * 1.1f, 0.5f), B.Glow(p.Cloth(i), 1f));
                s.Pop();
            }
            var shellMotion = k.Part("Shell", Vector3.up * height);
            shellMotion.spinAxis = new Vector3(0.2f, 1f, 0.1f).normalized;
            shellMotion.spinSpeed = 3f;
            var d = k.part;
            for (int i = 0; i < 40; i++) {
                float a = i * Mathf.PI * 2f / 40f + k.random.Range(-0.05f, 0.05f);
                Vector3 at = new Vector3(Mathf.Cos(a), k.random.Range(-0.04f, 0.04f), Mathf.Sin(a)) * (ring * k.random.Range(0.9f, 1.15f));
                float size = k.random.Range(2f, 7f);
                d.Rock(at, Vector3.one * size, ref k.random, p.rockTop, p.rockSide);
                if (i % 4 == 0) d.Sphere(at + Vector3.up * (size + 1.2f), Vector3.one * 1f, 3, 5, B.Glow(p.Cloth(i / 4), 1f));
            }
            var debris = k.Part("Debris", Vector3.up * height, Quaternion.Euler(18f, 0f, 9f));
            debris.spinSpeed = -4f;
            Motes(k.root.transform, Vector3.up * height, shell * 1.5f, k.world.weather.a, k.world.weather.b);
            return ring * 1.15f + 5f;
        }

        static float PortalRings(LandmarkKit k) {
            const float island = 34f, centre = 72f;
            float[] radii = { 46f, 35f, 25f };
            var b = k.body;
            var p = k.p;
            BaseIsland(k, island, 55f, 10);
            for (int i = 0; i < 9; i++) {
                Vector3 at = k.random.OnCircle() * k.random.Range(4f, island * 0.8f);
                b.Push(at, Quaternion.Euler(k.random.Range(-15f, 15f), 0f, k.random.Range(-15f, 15f)));
                Crystal(b, Vector3.zero, k.random.Range(1.5f, 3f), k.random.Range(8f, 22f), B.Glow(p.Cloth(i), 1f));
                b.Pop();
            }
            // Pylons holding the rings up.
            for (int side = -1; side <= 1; side += 2)
                Crystal(b, new Vector3(side * 20f, 0f, 0f), 3f, centre - radii[0] + 6f, p.capBand);
            // Three gimballed rings around a shimmering portal, each with studs so its turning shows.
            for (int r = 0; r < radii.Length; r++) {
                var ring = k.part;
                ring.Push(Vector3.zero, Quaternion.Euler(90f, 0f, 0f));
                ring.Torus(Vector3.zero, radii[r], 2.6f - r * 0.4f, 40, 6, B.Glow(p.Cloth(r), 1f));
                ring.Pop();
                for (int stud = 0; stud < 8; stud++) {
                    float a = stud * Mathf.PI * 0.25f;
                    ring.Box(new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * radii[r], Vector3.one * (5f - r), p.rock);
                }
                var motion = k.Part($"Ring{r}", Vector3.up * centre);
                motion.spinAxis = r == 1 ? Vector3.right : Vector3.up;
                motion.spinSpeed = r switch { 0 => 6f, 1 => -9f, _ => 14f };
            }
            var portal = k.part;
            portal.Sphere(Vector3.zero, new Vector3(radii[2] * 0.85f, radii[2] * 0.85f, 1.5f), 6, 16, B.Glow(p.glow, 0.8f));
            var portalMotion = k.Part("Portal", Vector3.up * centre);
            portalMotion.spinAxis = Vector3.forward;
            portalMotion.spinSpeed = 20f;
            EdgeFalls(k, island, 7f, -20f, 20f);
            Motes(k.root.transform, Vector3.up * centre, radii[0], k.world.weather.a, k.world.weather.b);
            return radii[0] + 20f;
        }
    }
}
#endif
