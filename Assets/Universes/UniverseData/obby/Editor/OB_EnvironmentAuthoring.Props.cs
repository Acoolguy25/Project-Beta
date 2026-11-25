#if !UNITY_SERVER
using UnityEngine;
using UnityEngine.Rendering;
using Universes.UniverseData.obby.Client;
using B = Universes.UniverseData.obby.Editor.OB_EnvMeshBuilder;

namespace Universes.UniverseData.obby.Editor {
    public static partial class OB_EnvironmentAuthoring {
        const int RockVariants = 3, IslandVariants = 3, KeelVariants = 3, PennantVariants = 3;
        /// <summary>Radius of the small scenery islands at scale 1 (m); OB_SceneSettings.islandRadius matches it.</summary>
        const float SceneryIslandRadius = 18f;

        /// <summary>The colours a tier's islands, rocks and dressing share.</summary>
        readonly struct Palette {
            public readonly OB_BuildStyle build;
            public readonly Color top, capBand, rock, rockDark, frame, frameDark, glow, rope, rockTop, rockSide;
            public readonly Color[] cloth;

            public Palette(OB_WorldStyle world) {
                var d = world.dressing;
                build = d.build;
                rock = B.Solid(d.rock);
                rockDark = B.Solid(d.rockDark);
                frame = B.Solid(d.frame);
                frameDark = B.Solid(d.frameDark);
                glow = B.Glow(d.glow);
                rope = d.build == OB_BuildStyle.Crystal ? B.Glow(d.rope) : B.Solid(d.rope);
                cloth = d.cloth;
                rockTop = B.Solid(world.rockTop);
                rockSide = B.Solid(world.rockSide);
                (top, capBand) = d.build switch {
                    OB_BuildStyle.Marble => (frame, B.Solid(d.cap)),
                    OB_BuildStyle.Basalt => (rock, B.Glow(d.glow, 0.6f)),
                    OB_BuildStyle.Crystal => (rock, B.Glow(d.cap)),
                    _ => (B.Solid(d.cap), B.Solid(d.cap))
                };
            }

            public Color Cloth(int index, float glowAmount = 0f) =>
                cloth == null || cloth.Length == 0 ? frame : (glowAmount > 0f ? B.Glow(cloth[index % cloth.Length], glowAmount) : B.Solid(cloth[index % cloth.Length]));
        }

        // ---------------------------------------------------------------- scenery: rocks and small islands
        static OB_Backdrop.TierScenery[] BuildScenery(TierMaterials[] materials) {
            var sets = new OB_Backdrop.TierScenery[OB_Tiers.Count];
            var builder = new B();
            for (int t = 0; t < OB_Tiers.Count; t++) {
                var tier = (OB_Tier)t;
                var palette = new Palette(OB_Themes.Get(tier).world);
                var random = new OB_EnvRandom(ShapeSeed, 100u + (uint)t);
                sets[t].material = materials[t].scenery;
                sets[t].rocks = new Mesh[RockVariants];
                for (int v = 0; v < RockVariants; v++) {
                    builder.Clear();
                    FloatingRock(builder, palette, ref random);
                    sets[t].rocks[v] = SaveMesh(builder, $"Rock{tier}{v}", "Scenery");
                }
                sets[t].islands = new Mesh[IslandVariants];
                for (int v = 0; v < IslandVariants; v++) {
                    builder.Clear();
                    SceneryIsland(builder, palette, ref random);
                    sets[t].islands[v] = SaveMesh(builder, $"Island{tier}{v}", "Scenery");
                }
            }
            return sets;
        }

        /// <summary>A rock of radius about 1, with the tier's touch: icicles, a magma drip, a crystal.</summary>
        static void FloatingRock(B b, in Palette p, ref OB_EnvRandom random) {
            b.Rock(Vector3.zero, new Vector3(1f, random.Range(0.55f, 0.9f), random.Range(0.8f, 1.1f)), ref random, p.rockTop, p.rockSide);
            switch (p.build) {
                case OB_BuildStyle.Ice:
                    for (int i = 0; i < 3; i++)
                        b.Prism(new Vector3(random.Range(-0.5f, 0.5f), -0.45f, random.Range(-0.5f, 0.5f)), 0.15f, 0f, -random.Range(0.5f, 1.1f), 5, p.frame);
                    break;
                case OB_BuildStyle.Basalt:
                    b.Prism(new Vector3(0f, -0.5f, 0f), 0.22f, 0f, -0.8f, 5, p.glow);
                    break;
                case OB_BuildStyle.Crystal:
                    b.Push(new Vector3(0.2f, 0.4f, 0f), Quaternion.Euler(random.Range(-25f, 25f), 0f, random.Range(-25f, 25f)));
                    Crystal(b, Vector3.zero, 0.25f, 1.2f, p.glow);
                    b.Pop();
                    break;
                case OB_BuildStyle.Earth:
                    b.Sphere(new Vector3(0.3f, 0.55f, 0.1f), new Vector3(0.35f, 0.25f, 0.35f), 3, 5, p.top);
                    break;
            }
        }

        static void SceneryIsland(B b, in Palette p, ref OB_EnvRandom random) {
            float radius = SceneryIslandRadius * random.Range(0.85f, 1.15f);
            Island(b, Vector3.zero, radius, radius * random.Range(1.1f, 1.5f), p, ref random);
            Vector3 Spot(ref OB_EnvRandom r, float reach = 0.6f) {
                Vector3 at = r.OnCircle() * (radius * reach * Mathf.Sqrt(r.Value()));
                return at;
            }
            int count = 3 + random.Next(3);
            for (int i = 0; i < count; i++) {
                Vector3 at = Spot(ref random);
                switch (p.build) {
                    case OB_BuildStyle.Marble:
                        Column(b, at, random.Range(5f, 12f), 1f, p.frame, p.capBand);
                        break;
                    case OB_BuildStyle.Earth:
                        Tree(b, at, random.Range(6f, 11f), p.frameDark, p.top, ref random);
                        break;
                    case OB_BuildStyle.Sandstone:
                        if (i == 0) b.Prism(at, 6f, 0f, 6f, 4, p.capBand, p.capBand, p.rock, Mathf.PI * 0.25f);
                        else Cactus(b, at, random.Range(4f, 7f), B.Solid(OB_Themes.Get(OB_Tier.Medium).propColor), p.Cloth(0), ref random);
                        break;
                    case OB_BuildStyle.Ice:
                        if (i % 2 == 0) Pine(b, at, random.Range(7f, 12f), p.frameDark, p.frame, p.top);
                        else b.Prism(at, random.Range(0.8f, 1.4f), 0f, random.Range(5f, 10f), 5, p.frame);
                        break;
                    case OB_BuildStyle.Basalt:
                        if (i % 2 == 0) Vent(b, at, random.Range(2.5f, 4f), p.rock, p.glow);
                        else b.Prism(at, random.Range(1.2f, 2f), random.Range(1.2f, 2f), random.Range(4f, 9f), 6, p.rockDark, p.capBand, p.rockDark);
                        break;
                    case OB_BuildStyle.Crystal:
                        Crystal(b, at, random.Range(0.8f, 1.5f), random.Range(4f, 10f), p.Cloth(i, 1f));
                        break;
                }
            }
        }

        // ---------------------------------------------------------------- shared shapes
        /// <summary>A floating island: a capped top with a band under it, then jagged roots narrowing to a point.</summary>
        static void Island(B b, Vector3 top, float radius, float depth, in Palette p, ref OB_EnvRandom random, int sides = 9) {
            float band = Mathf.Max(1.2f, depth * 0.06f);
            b.Lathe(top, new[] { 0f, -band }, new[] { radius, radius * 1.02f }, sides, new[] { p.capBand }, p.top, ref random, 0f);
            b.Lathe(top, new[] { -band, -depth * 0.25f, -depth * 0.5f, -depth * 0.75f, -depth },
                new[] { radius * 1.02f, radius * 0.85f, radius * 0.58f, radius * 0.28f, 0f }, sides,
                new[] { p.rock, p.rockDark, p.rock, p.rockDark }, p.top, ref random, 0.12f);
        }

        static void Column(B b, Vector3 at, float height, float radius, Color stone, Color capital) {
            b.Box(at + Vector3.up * 0.3f, new Vector3(radius * 2.6f, 0.6f, radius * 2.6f), stone);
            b.Prism(at + Vector3.up * 0.6f, radius, radius * 0.9f, height, 10, stone);
            b.Box(at + Vector3.up * (height + 0.9f), new Vector3(radius * 2.4f, 0.7f, radius * 2.4f), capital);
        }

        static void Tree(B b, Vector3 at, float height, Color trunk, Color leaves, ref OB_EnvRandom random) {
            b.Prism(at, height * 0.09f, height * 0.06f, height * 0.65f, 6, trunk);
            float crown = height * 0.38f;
            b.Sphere(at + Vector3.up * (height * 0.7f), new Vector3(crown, crown * 0.85f, crown), 4, 6, leaves, B.Shade(leaves, 1.12f));
            b.Sphere(at + new Vector3(crown * 0.5f, height * 0.85f, random.Range(-0.3f, 0.3f) * crown), Vector3.one * (crown * 0.6f), 3, 5, B.Shade(leaves, 0.9f));
        }

        static void Pine(B b, Vector3 at, float height, Color trunk, Color needles, Color snow) {
            b.Prism(at, height * 0.06f, height * 0.04f, height * 0.25f, 5, trunk);
            for (int tier = 0; tier < 3; tier++) {
                float y = height * (0.2f + tier * 0.25f), r = height * (0.32f - tier * 0.08f);
                b.Prism(at + Vector3.up * y, r, 0f, height * 0.35f, 7, tier == 2 ? snow : needles);
            }
        }

        static void Cactus(B b, Vector3 at, float height, Color body, Color flower, ref OB_EnvRandom random) {
            b.Prism(at, 0.7f, 0.6f, height, 7, body);
            float side = random.Sign(), armY = height * random.Range(0.4f, 0.6f);
            b.Box(at + new Vector3(side * 1.1f, armY, 0f), new Vector3(1.4f, 0.8f, 0.8f), body);
            b.Prism(at + new Vector3(side * 1.6f, armY, 0f), 0.45f, 0.4f, height * 0.35f, 6, body);
            b.Sphere(at + Vector3.up * (height + 0.2f), new Vector3(0.45f, 0.3f, 0.45f), 3, 5, flower);
        }

        static void Vent(B b, Vector3 at, float size, Color rock, Color magma) {
            b.Prism(at, size, size * 0.6f, size * 0.9f, 7, rock);
            b.Prism(at + Vector3.up * (size * 0.9f), size * 0.42f, 0f, -size * 0.5f, 7, magma);
        }

        /// <summary>A crystal: a hexagonal shaft with a pointed tip.</summary>
        static void Crystal(B b, Vector3 at, float radius, float height, Color color) {
            b.Prism(at, radius, radius * 0.9f, height * 0.75f, 6, color);
            b.Prism(at + Vector3.up * (height * 0.75f), radius * 0.9f, 0f, height * 0.25f, 6, color);
        }

        // ---------------------------------------------------------------- course dressing
        static OB_Backdrop.TierDressing[] BuildDressing(TierMaterials[] materials, in OB_DressingSettings s) {
            var sets = new OB_Backdrop.TierDressing[OB_Tiers.Count];
            var b = new B();
            for (int t = 0; t < OB_Tiers.Count; t++) {
                var tier = (OB_Tier)t;
                var p = new Palette(OB_Themes.Get(tier).world);
                var random = new OB_EnvRandom(ShapeSeed, 200u + (uint)t);
                string folder = "Dressing";
                ref var set = ref sets[t];
                set.solid = materials[t].dressing;
                set.cloth = materials[t].cloth;
                set.keels = new Mesh[KeelVariants];
                for (int v = 0; v < KeelVariants; v++) {
                    b.Clear();
                    Keel(b, p, ref random);
                    set.keels[v] = SaveMesh(b, $"Keel{tier}{v}", folder);
                }
                b.Clear();
                b.Box(Vector3.zero, Vector3.one, p.build == OB_BuildStyle.Marble ? p.capBand : p.build >= OB_BuildStyle.Basalt ? p.capBand : p.frame);
                set.trim = SaveMesh(b, $"Trim{tier}", folder);
                b.Clear();
                PillarShaft(b, p, ref random);
                set.pillar = SaveMesh(b, $"Pillar{tier}", folder);
                b.Clear();
                PillarFoot(b, p, s, ref random);
                set.pillarCap = SaveMesh(b, $"PillarFoot{tier}", folder);
                b.Clear();
                Arch(b, p, s, ref random);
                set.arch = SaveMesh(b, $"Arch{tier}", folder);
                set.banners = new Mesh[2];
                for (int v = 0; v < set.banners.Length; v++) {
                    b.Clear();
                    Banner(b, p, s, v);
                    set.banners[v] = SaveMesh(b, $"Banner{tier}{v}", folder);
                }
                b.Clear();
                Lamp(b, p, s);
                set.lamp = SaveMesh(b, $"Lamp{tier}", folder);
                b.Clear();
                Rope(b, p, s);
                set.rope = SaveMesh(b, $"Rope{tier}", folder);
                set.pennants = new Mesh[PennantVariants];
                for (int v = 0; v < PennantVariants; v++) {
                    b.Clear();
                    Pennant(b, p, s, v);
                    set.pennants[v] = SaveMesh(b, $"Pennant{tier}{v}", folder);
                }
            }
            return sets;
        }

        /// <summary>
        /// Rock under a floor, in unit space (top face 1 x 1 at y 0, point at y -1): a cap band under the
        /// platform, strata narrowing to a point, and the style's hanging details.
        /// </summary>
        static void Keel(B b, in Palette p, ref OB_EnvRandom random) {
            const int sides = 8;
            const float top = 0.54f;   // an octagon this size sits inside the unit square with its flats on the edges
            float angle = Mathf.PI / sides;
            float band = 0.1f;
            b.Lathe(Vector3.zero, new[] { 0f, -band }, new[] { top, top * 0.98f }, sides, new[] { p.capBand }, p.top, ref random, 0f, angle);
            Color[] strata = p.build switch {
                OB_BuildStyle.Sandstone => new[] { p.rock, p.capBand, p.rockDark, p.rock },
                OB_BuildStyle.Basalt => new[] { p.rock, p.rockDark, B.Glow(p.glow, 0.7f), p.rockDark },
                OB_BuildStyle.Crystal => new[] { p.rock, p.rockDark, p.rock, p.rockDark },
                _ => new[] { p.rock, p.rockDark, p.rock, p.rockDark }
            };
            b.Lathe(Vector3.zero, new[] { -band, -0.35f, -0.6f, -0.82f, -1f }, new[] { top * 0.98f, top * 0.78f, top * 0.5f, top * 0.22f, 0f },
                sides, strata, p.top, ref random, 0.14f, angle);
            switch (p.build) {
                case OB_BuildStyle.Marble:
                    b.Sphere(new Vector3(0f, -1.02f, 0f), new Vector3(0.05f, 0.04f, 0.05f), 3, 6, p.capBand);
                    break;
                case OB_BuildStyle.Earth:
                    // Roots and vines dangling from under the grass.
                    for (int i = 0; i < 5; i++) {
                        Vector3 at = random.OnCircle() * random.Range(0.25f, 0.42f) + Vector3.up * -random.Range(0.15f, 0.35f);
                        b.Prism(at, 0.025f, 0.012f, -random.Range(0.3f, 0.55f), 4, i % 2 == 0 ? p.top : p.frameDark);
                    }
                    break;
                case OB_BuildStyle.Ice:
                    for (int i = 0; i < 7; i++) {
                        Vector3 at = random.OnCircle() * random.Range(0.3f, 0.48f) + Vector3.up * -band;
                        b.Prism(at, 0.04f, 0f, -random.Range(0.2f, 0.5f), 5, B.Glow(p.frame, 0.15f));
                    }
                    break;
                case OB_BuildStyle.Crystal:
                    for (int i = 0; i < 4; i++) {
                        Vector3 at = random.OnCircle() * random.Range(0.1f, 0.3f) + Vector3.up * -random.Range(0.5f, 0.8f);
                        b.Prism(at, 0.05f, 0f, -random.Range(0.3f, 0.5f), 6, p.Cloth(i, 1f));
                    }
                    break;
            }
        }

        /// <summary>A tower shaft in unit space: width 1, top at 0, bottom at -1 (stretched to its length).</summary>
        static void PillarShaft(B b, in Palette p, ref OB_EnvRandom random) {
            switch (p.build) {
                case OB_BuildStyle.Marble:
                    b.Prism(new Vector3(0f, -1f, 0f), 0.5f, 0.5f, 1f, 12, p.frame);
                    break;
                case OB_BuildStyle.Earth:
                    b.Lathe(Vector3.zero, new[] { 0f, -0.25f, -0.5f, -0.75f, -1f }, new[] { 0.5f, 0.42f, 0.47f, 0.38f, 0.32f }, 7,
                        new[] { p.rock, p.rockDark, p.rock, p.rockDark }, p.rock, ref random, 0.15f);
                    break;
                case OB_BuildStyle.Sandstone: {
                    var ys = new float[9];
                    var radii = new float[9];
                    for (int i = 0; i < ys.Length; i++) {
                        ys[i] = -i / 8f;
                        radii[i] = 0.5f;
                    }
                    b.Lathe(Vector3.zero, ys, radii, 4, new[] { p.rock, p.capBand, p.rock, p.rockDark }, p.rock, ref random, 0f, Mathf.PI * 0.25f);
                    break;
                }
                case OB_BuildStyle.Ice:
                    b.Prism(new Vector3(0f, -1f, 0f), 0.35f, 0.5f, 1f, 6, p.frame);
                    break;
                case OB_BuildStyle.Basalt:
                    for (int i = 0; i < 3; i++) {
                        float a = i * Mathf.PI * 2f / 3f;
                        b.Prism(new Vector3(Mathf.Cos(a) * 0.22f, -1f, Mathf.Sin(a) * 0.22f), 0.26f, 0.26f, 1f, 6, i == 0 ? p.rock : p.rockDark);
                    }
                    b.Prism(new Vector3(0f, -1f, 0f), 0.12f, 0.12f, 1f, 6, p.glow);
                    break;
                case OB_BuildStyle.Crystal: {
                    var ys = new float[11];
                    var radii = new float[11];
                    var bands = new Color[10];
                    for (int i = 0; i < ys.Length; i++) {
                        ys[i] = -i / 10f;
                        radii[i] = 0.5f;
                        if (i < bands.Length) bands[i] = i % 2 == 0 ? p.rock : B.Glow(p.Cloth(i / 2), 0.9f);
                    }
                    b.Lathe(Vector3.zero, ys, radii, 6, bands, p.rock, ref random, 0f);
                    break;
                }
            }
        }

        /// <summary>The finial at a tower's foot, at real size (origin at the foot, hanging down).</summary>
        static void PillarFoot(B b, in Palette p, in OB_DressingSettings s, ref OB_EnvRandom random) {
            float h = s.pillarCapHeight, r = s.pillarWidth * 0.55f;
            switch (p.build) {
                case OB_BuildStyle.Marble:
                    b.Box(new Vector3(0f, -0.4f, 0f), new Vector3(r * 2.2f, 0.8f, r * 2.2f), p.frame);
                    b.Prism(new Vector3(0f, -0.8f, 0f), r * 0.8f, 0f, -h * 0.8f, 8, p.capBand);
                    break;
                case OB_BuildStyle.Earth:
                    b.Rock(new Vector3(0f, -r, 0f), new Vector3(r * 1.3f, r, r * 1.3f), ref random, p.rock, p.rockDark);
                    for (int i = 0; i < 4; i++)
                        b.Prism(random.OnCircle() * r * 0.6f - Vector3.up * r, 0.15f, 0.04f, -random.Range(h * 0.5f, h), 4, i % 2 == 0 ? p.top : p.frameDark);
                    break;
                case OB_BuildStyle.Sandstone:
                    b.Prism(Vector3.zero, r * 1.2f, 0f, -h, 4, p.rock, p.rock, p.capBand, Mathf.PI * 0.25f);
                    break;
                case OB_BuildStyle.Ice:
                    b.Prism(Vector3.zero, r, 0f, -h * 1.3f, 6, B.Glow(p.frame, 0.15f));
                    break;
                case OB_BuildStyle.Basalt:
                    b.Prism(Vector3.zero, r, r * 0.4f, -h * 0.5f, 6, p.rockDark);
                    b.Prism(new Vector3(0f, -h * 0.5f, 0f), r * 0.4f, 0f, -h * 0.6f, 6, p.glow);
                    break;
                case OB_BuildStyle.Crystal:
                    Crystal(b, Vector3.zero, r * 0.8f, -h * 1.4f, p.Cloth(0, 1f));
                    break;
            }
        }

        /// <summary>
        /// The arch over an edge of a 6 m pad (origin at the middle of that edge, at the pad's top): two
        /// slim posts reaching below the pad, a lintel, and the style's crown, within the planner's parts.
        /// </summary>
        static void Arch(B b, in Palette p, in OB_DressingSettings s, ref OB_EnvRandom random) {
            float postX = s.archInnerWidth * 0.5f + s.archPost * 0.5f, top = s.archHeight, post = s.archPost, depth = s.archDepth, foot = s.archFoot;
            float lintelWidth = s.archInnerWidth + post * 2f;
            var lintelCentre = new Vector3(0f, top + post * 0.5f, 0f);
            for (int side = -1; side <= 1; side += 2) {
                var baseAt = new Vector3(side * postX, -foot, 0f);
                switch (p.build) {
                    case OB_BuildStyle.Marble:
                        b.Box(new Vector3(side * postX, 0.3f, 0f), new Vector3(post * 1.3f, 0.6f, depth), p.frame);
                        b.Prism(baseAt, post * 0.45f, post * 0.42f, top + foot, 10, p.frame);
                        b.Box(new Vector3(side * postX, top - 0.25f, 0f), new Vector3(post * 1.25f, 0.5f, depth), p.capBand);
                        break;
                    case OB_BuildStyle.Earth:
                        b.Box(new Vector3(side * postX, (top - foot) * 0.5f, 0f), new Vector3(post * 0.8f, top + foot, post * 0.8f), p.frameDark);
                        break;
                    case OB_BuildStyle.Sandstone:
                        b.Prism(baseAt, post * 0.7f, post * 0.5f, top + foot, 4, p.frame, p.frame, p.frameDark, Mathf.PI * 0.25f);
                        b.Box(new Vector3(side * postX, top * 0.35f, 0f), new Vector3(post * 1.05f, 0.5f, depth * 0.9f), p.Cloth(0));
                        b.Box(new Vector3(side * postX, top * 0.65f, 0f), new Vector3(post * 0.95f, 0.5f, depth * 0.85f), p.Cloth(1));
                        break;
                    case OB_BuildStyle.Ice:
                        b.Prism(baseAt, post * 0.6f, post * 0.4f, top + foot, 6, p.frame);
                        Crystal(b, new Vector3(side * postX, top + post, 0f), 0.3f, 1.8f, B.Glow(p.frame, 0.4f));
                        break;
                    case OB_BuildStyle.Basalt:
                        b.Box(new Vector3(side * postX, (top - foot) * 0.5f, 0f), new Vector3(post, top + foot, post), p.frame);
                        for (int rune = 0; rune < 3; rune++)
                            b.Box(new Vector3(side * postX, 2f + rune * 2.8f, -post * 0.5f - 0.02f), new Vector3(post * 0.35f, 1.4f, 0.05f), p.glow);
                        break;
                    case OB_BuildStyle.Crystal:
                        Crystal(b, baseAt, post * 0.45f, top + foot + post, B.Glow(p.Cloth(side < 0 ? 0 : 1), 0.25f));
                        break;
                }
            }
            switch (p.build) {
                case OB_BuildStyle.Marble:
                    b.Box(lintelCentre, new Vector3(lintelWidth + 0.6f, post, depth), p.frame);
                    b.Box(lintelCentre + Vector3.up * (post * 0.5f + 0.15f), new Vector3(lintelWidth + 1f, 0.3f, depth * 1.15f), p.frameDark);
                    b.Box(lintelCentre + Vector3.up * 0.9f, new Vector3(0.9f, 1.6f, depth * 1.1f), p.capBand);
                    for (int side = -1; side <= 1; side += 2)
                        b.Sphere(lintelCentre + new Vector3(side * (lintelWidth * 0.5f), post + 0.2f, 0f), Vector3.one * 0.45f, 3, 6, p.capBand);
                    break;
                case OB_BuildStyle.Earth:
                    b.Push(lintelCentre, Quaternion.Euler(0f, 0f, 90f));
                    b.Prism(new Vector3(0f, -lintelWidth * 0.5f - 0.6f, 0f), post * 0.5f, post * 0.5f, lintelWidth + 1.2f, 7, p.frame);
                    b.Pop();
                    // A leafy garland with flowers along the lintel.
                    for (int i = 0; i < 7; i++) {
                        float x = Mathf.Lerp(-postX, postX, i / 6f);
                        float sag = Mathf.Sin(i / 6f * Mathf.PI) * 0.6f;
                        b.Sphere(new Vector3(x, top - 0.1f - sag, -depth * 0.4f), Vector3.one * random.Range(0.45f, 0.65f), 3, 5, p.top);
                        if (i % 2 == 1) b.Sphere(new Vector3(x, top - 0.3f - sag, -depth * 0.75f), Vector3.one * 0.25f, 2, 4, p.Cloth(i / 2));
                    }
                    break;
                case OB_BuildStyle.Sandstone:
                    b.Box(lintelCentre, new Vector3(lintelWidth + 0.4f, post, depth), p.frame);
                    b.Box(lintelCentre + Vector3.up * (post * 0.5f + 0.25f), new Vector3(lintelWidth + 1.2f, 0.5f, depth * 1.2f), p.frameDark);
                    b.Sphere(lintelCentre + new Vector3(0f, 0f, -depth * 0.55f), new Vector3(1f, 1f, 0.2f), 3, 8, B.Glow(OB_Palette.Gold, 0.3f));
                    break;
                case OB_BuildStyle.Ice:
                    b.Box(lintelCentre, new Vector3(lintelWidth, post * 0.8f, depth), p.frameDark);
                    for (int i = 0; i < 7; i++)
                        b.Prism(new Vector3(Mathf.Lerp(-postX + 0.6f, postX - 0.6f, i / 6f), top, random.Range(-0.2f, 0.2f)), 0.15f, 0f, -random.Range(0.5f, 1.3f), 5, B.Glow(p.frame, 0.2f));
                    break;
                case OB_BuildStyle.Basalt:
                    b.Box(lintelCentre, new Vector3(lintelWidth + 0.4f, post, depth), p.frameDark);
                    for (int i = 0; i < 5; i++)
                        b.Prism(lintelCentre + new Vector3(Mathf.Lerp(-postX, postX, i / 4f), post * 0.5f, 0f), 0.35f, 0f, i % 2 == 0 ? 2.2f : 1.4f, 5, p.frame);
                    for (int side = -1; side <= 1; side += 2) {
                        var bowl = lintelCentre + new Vector3(side * postX, post * 0.5f, 0f);
                        b.Prism(bowl, 0.35f, 0.75f, 0.6f, 7, p.frameDark);
                        b.Prism(bowl + Vector3.up * 0.6f, 0.55f, 0f, 1.6f, 5, p.glow);
                    }
                    break;
                case OB_BuildStyle.Crystal:
                    // Floating shards for a lintel and a neon ring hovering over the middle.
                    for (int i = 0; i < 5; i++)
                        b.Box(lintelCentre + new Vector3(Mathf.Lerp(-postX * 0.8f, postX * 0.8f, i / 4f), random.Range(-0.2f, 0.2f), 0f),
                            new Vector3(1.3f, 0.5f, 0.5f), B.Glow(p.Cloth(i), 0.9f));
                    b.Push(lintelCentre + Vector3.up * 1.8f, Quaternion.Euler(90f, 0f, 0f));
                    b.Torus(Vector3.zero, 1.2f, 0.18f, 16, 5, B.Glow(p.Cloth(2), 1f));
                    b.Pop();
                    break;
            }
        }

        /// <summary>A banner hanging from its origin down the front of an arch post: a swallowtail with a stripe, on a rod.</summary>
        static void Banner(B b, in Palette p, in OB_DressingSettings s, int variant) {
            float w = s.bannerWidth * 0.5f, l = s.bannerLength;
            float glow = p.build == OB_BuildStyle.Crystal ? 0.3f : 0f;
            b.Sway = 0f;
            b.Box(new Vector3(0f, 0.05f, 0f), new Vector3(s.bannerWidth + 0.3f, 0.12f, 0.12f), p.build == OB_BuildStyle.Marble ? p.capBand : p.frameDark);
            b.SwayOf = local => Mathf.Clamp01(-local.y / l);
            Color cloth = p.Cloth(variant, glow), stripe = p.Cloth(variant + 2, glow);
            var tl = new Vector3(-w, 0f, 0f);
            var tr = new Vector3(w, 0f, 0f);
            var br = new Vector3(w, -l, 0f);
            var bl = new Vector3(-w, -l, 0f);
            var notch = new Vector3(0f, -l * 0.8f, 0f);
            b.TriRaw(tl, tr, notch, cloth);
            b.TriRaw(tr, br, notch, cloth);
            b.TriRaw(tl, notch, bl, cloth);
            const float stripeOffset = -0.02f;   // just in front of the cloth, so it does not z-fight
            b.Panel(new Vector3(-w, -l * 0.22f, stripeOffset), new Vector3(w, -l * 0.22f, stripeOffset),
                new Vector3(w, -l * 0.32f, stripeOffset), new Vector3(-w, -l * 0.32f, stripeOffset), stripe);
            b.SwayOf = null;
        }

        /// <summary>A lamp post standing beside a pad (origin at the pad's top level), with the style's light.</summary>
        static void Lamp(B b, in Palette p, in OB_DressingSettings s) {
            float h = s.lampHeight, foot = s.lampFoot, w = s.lampWidth;
            var bottom = new Vector3(0f, -foot, 0f);
            switch (p.build) {
                case OB_BuildStyle.Marble:
                    b.Prism(bottom, w * 0.2f, w * 0.17f, h + foot - 1.2f, 8, p.frame);
                    b.Box(new Vector3(0f, h - 0.7f, 0f), new Vector3(w * 0.55f, 0.9f, w * 0.55f), p.glow);
                    b.Prism(new Vector3(0f, h - 0.25f, 0f), w * 0.45f, 0f, 0.6f, 4, p.capBand, Mathf.PI * 0.25f);
                    break;
                case OB_BuildStyle.Earth:
                    b.Box(new Vector3(0f, (h - foot) * 0.5f - 0.6f, 0f), new Vector3(w * 0.25f, h + foot - 1.2f, w * 0.25f), p.frameDark);
                    b.Box(new Vector3(0f, h - 0.8f, 0f), new Vector3(w * 0.5f, 0.8f, w * 0.5f), p.glow);
                    b.Prism(new Vector3(0f, h - 0.4f, 0f), w * 0.45f, 0f, 0.5f, 4, p.frame, Mathf.PI * 0.25f);
                    break;
                case OB_BuildStyle.Sandstone:
                case OB_BuildStyle.Basalt:
                    // A torch or brazier: bowl on a post, flames licking up.
                    b.Prism(bottom, w * 0.18f, w * 0.14f, h + foot - 1.2f, 6, p.build == OB_BuildStyle.Basalt ? p.frameDark : p.frame);
                    b.Prism(new Vector3(0f, h - 1.2f, 0f), w * 0.15f, w * 0.45f, 0.6f, 7, p.frameDark);
                    b.Prism(new Vector3(0f, h - 0.6f, 0f), w * 0.35f, 0f, 1.2f, 5, p.glow);
                    b.Prism(new Vector3(w * 0.12f, h - 0.6f, 0.05f), w * 0.2f, 0f, 0.8f, 4, B.Glow(OB_Palette.Gold));
                    break;
                case OB_BuildStyle.Ice:
                    b.Prism(bottom, w * 0.18f, w * 0.12f, h + foot - 1.4f, 6, p.frame);
                    b.Prism(new Vector3(0f, h - 1.4f, 0f), 0f, w * 0.35f, 0.7f, 6, p.glow);
                    b.Prism(new Vector3(0f, h - 0.7f, 0f), w * 0.35f, 0f, 0.7f, 6, p.glow);
                    break;
                case OB_BuildStyle.Crystal:
                    b.Prism(bottom, w * 0.2f, w * 0.12f, h + foot - 1.5f, 6, p.rock);
                    for (int i = 0; i < 3; i++)
                        b.Box(new Vector3(0f, 1f + i * 1.6f, -w * 0.18f), new Vector3(w * 0.08f, 1f, 0.05f), B.Glow(p.Cloth(i), 1f));
                    b.Sphere(new Vector3(0f, h - 0.6f, 0f), Vector3.one * (w * 0.3f), 3, 6, p.glow);
                    break;
            }
        }

        /// <summary>One metre of rope or chain along +Z, stretched per segment.</summary>
        static void Rope(B b, in Palette p, in OB_DressingSettings s) {
            float t = s.ropeThickness;
            if (p.build is OB_BuildStyle.Marble or OB_BuildStyle.Basalt) {
                const int links = 4;
                for (int i = 0; i < links; i++) {
                    float z = (i + 0.5f) / links;
                    b.Box(new Vector3(0f, 0f, z), i % 2 == 0 ? new Vector3(t * 1.4f, t * 0.45f, 0.3f) : new Vector3(t * 0.45f, t * 1.4f, 0.3f), p.rope);
                }
                return;
            }
            b.Push(Vector3.zero, Quaternion.Euler(90f, 0f, 0f));
            b.Prism(Vector3.zero, t * 0.5f, t * 0.5f, 1f, 5, p.rope);
            b.Pop();
        }

        /// <summary>What hangs from the ropes: pennants, icicles, lanterns or crystal shards.</summary>
        static void Pennant(B b, in Palette p, in OB_DressingSettings s, int variant) {
            float l = s.pennantLength;
            switch (p.build) {
                case OB_BuildStyle.Ice:
                    b.Prism(Vector3.zero, 0.12f, 0f, -l * (0.6f + 0.3f * variant), 5, B.Glow(p.frame, 0.15f));
                    break;
                case OB_BuildStyle.Basalt:
                    b.Sway = 0.3f;
                    b.Box(new Vector3(0f, -0.2f, 0f), new Vector3(0.04f, 0.4f, 0.04f), p.frameDark);
                    b.Box(new Vector3(0f, -0.55f, 0f), new Vector3(0.3f, 0.36f, 0.3f), B.Glow(p.glow, 0.5f + 0.25f * variant));
                    b.Sway = 0f;
                    break;
                case OB_BuildStyle.Crystal:
                    b.Sway = 0.2f;
                    b.Prism(new Vector3(0f, -0.3f, 0f), 0f, 0.14f, 0.3f, 6, p.Cloth(variant, 1f));
                    b.Prism(new Vector3(0f, -0.3f, 0f), 0.14f, 0f, -l * 0.6f, 6, p.Cloth(variant, 1f));
                    b.Sway = 0f;
                    break;
                default:
                    b.SwayOf = local => Mathf.Clamp01(-local.y / l);
                    b.TriRaw(new Vector3(-0.45f, 0f, 0f), new Vector3(0.45f, 0f, 0f), new Vector3(0f, -l, 0f), p.Cloth(variant));
                    b.SwayOf = null;
                    break;
            }
        }

        // ---------------------------------------------------------------- birds
        static OB_BirdFlocks BuildBirds(Transform parent, TierMaterials[] materials) {
            var b = new B();
            var white = B.Glow(Color.white, 1f);   // tinted per tier by the material; alpha lets void wisps glow
            b.Box(Vector3.zero, new Vector3(0.22f, 0.16f, 0.7f), white);
            b.Prism(new Vector3(0f, 0f, 0.35f), 0.08f, 0f, 0.25f, 4, white);
            b.SwayOf = local => Mathf.Clamp01(Mathf.Abs(local.x) / 0.9f);
            for (int side = -1; side <= 1; side += 2) {
                b.TriRaw(new Vector3(side * 0.1f, 0.02f, 0.2f), new Vector3(side * 0.95f, 0.08f, -0.05f), new Vector3(side * 0.1f, 0.02f, -0.2f), white);
                b.TriRaw(new Vector3(side * 0.1f, 0.02f, -0.2f), new Vector3(side * 0.95f, 0.08f, -0.05f), new Vector3(side * 0.6f, 0.05f, -0.3f), white);
            }
            b.SwayOf = null;
            var birds = new GameObject("Birds").AddComponent<OB_BirdFlocks>();
            birds.transform.SetParent(parent, false);
            birds.birdMesh = SaveMesh(b, "Bird", "Fauna");
            birds.tierMaterials = new Material[OB_Tiers.Count];
            for (int t = 0; t < OB_Tiers.Count; t++)
                birds.tierMaterials[t] = OB_Themes.Get((OB_Tier)t).world.flocks > 0 ? materials[t].bird : null;
            return birds;
        }

        // ---------------------------------------------------------------- horizon
        /// <summary>Ring radii (m), nearest first, and how much taller farther rings stand so they show above nearer ones.</summary>
        static readonly float[] RingRadius = { 2400f, 2900f, 3400f };
        static readonly float[] RingHeightScale = { 1f, 1.35f, 1.7f };
        static readonly float[] RingParallax = { 0.12f, 0.06f, 0f };
        static readonly float[] RingHaze = { 0.15f, 0.35f, 0.55f };
        /// <summary>How far below the camera the rings reach (m), so their feet sink into the cloud sea's haze.</summary>
        const float RingFoot = 600f;
        const int RingSegments = 256;

        static OB_HorizonLayers BuildHorizon(Transform parent) {
            var material = SimpleMat($"{MaterialFolder}/Horizon.mat", "Universes/Obby/Horizon", m => m.enableInstancing = true);
            var horizon = new GameObject("Horizon").AddComponent<OB_HorizonLayers>();
            horizon.transform.SetParent(parent, false);
            horizon.tiers = new OB_HorizonLayers.TierRings[OB_Tiers.Count];
            for (int t = 0; t < OB_Tiers.Count; t++) {
                var tier = (OB_Tier)t;
                OB_HorizonStyle style = OB_Themes.Get(tier).world.horizon;
                Color[] tops = { style.near, style.mid, style.far };
                var rings = new OB_HorizonLayers.Ring[RingRadius.Length];
                for (int r = 0; r < rings.Length; r++) {
                    var mesh = SaveMesh(HorizonRing($"Horizon{tier}{r}", style.shape, RingRadius[r], style.height * RingHeightScale[r], (uint)(t * 7 + r)), "Horizon");
                    var renderer = MeshChild(horizon.transform, $"{tier} ring {r}", mesh, material, ShadowCastingMode.Off);
                    renderer.receiveShadows = false;
                    renderer.lightProbeUsage = LightProbeUsage.Off;
                    renderer.enabled = false;
                    rings[r] = new OB_HorizonLayers.Ring { renderer = renderer, parallax = RingParallax[r], top = tops[r], haze = RingHaze[r] };
                }
                horizon.tiers[t] = new OB_HorizonLayers.TierRings { rings = rings, foot = style.foot, crestGlow = style.crestGlow };
            }
            return horizon;
        }

        /// <summary>A ring of silhouette around the origin: smooth, unlit, UV0.y 0 at the foot and 1 at the crest.</summary>
        static Mesh HorizonRing(string name, OB_HorizonShape shape, float radius, float height, uint seed) {
            var random = new OB_EnvRandom(ShapeSeed, 300u + seed);
            float p1 = random.Range(0f, 10f), p2 = random.Range(0f, 10f), p3 = random.Range(0f, 10f);
            var vertices = new Vector3[(RingSegments + 1) * 2];
            var uvs = new Vector2[vertices.Length];
            var triangles = new int[RingSegments * 6];
            for (int i = 0; i <= RingSegments; i++) {
                float u = i / (float)RingSegments, a = u * Mathf.PI * 2f;
                float h = Mathf.Clamp01(Profile(shape, a, p1, p2, p3)) * height;
                var around = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * radius;
                vertices[i * 2] = around + Vector3.down * RingFoot;
                vertices[i * 2 + 1] = around + Vector3.up * h;
                uvs[i * 2] = new Vector2(u, 0f);
                uvs[i * 2 + 1] = new Vector2(u, 1f);
                if (i == RingSegments) continue;
                int k = i * 6, v = i * 2;
                // Faces inward (towards the camera at the centre).
                triangles[k] = v; triangles[k + 1] = v + 3; triangles[k + 2] = v + 1;
                triangles[k + 3] = v; triangles[k + 4] = v + 2; triangles[k + 5] = v + 3;
            }
            var mesh = new Mesh { name = name, vertices = vertices, uv = uvs, triangles = triangles };
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>Height 0..1 of a horizon silhouette at angle <paramref name="a"/> (radians).</summary>
        static float Profile(OB_HorizonShape shape, float a, float p1, float p2, float p3) {
            static float Tri(float x) => 1f - Mathf.Abs(Mathf.Repeat(x, 1f) * 2f - 1f);
            switch (shape) {
                case OB_HorizonShape.CloudBanks: {
                    float puffs = Mathf.Abs(Mathf.Sin(a * 9f + p1)) * 0.55f + Mathf.Abs(Mathf.Sin(a * 23f + p2)) * 0.3f + Mathf.Abs(Mathf.Sin(a * 47f + p3)) * 0.15f;
                    return 0.25f + 0.6f * Mathf.Pow(puffs, 0.8f) * (0.6f + 0.4f * Mathf.Sin(a * 3f + p1));
                }
                case OB_HorizonShape.Hills:
                    return 0.3f + 0.35f * (Mathf.Sin(a * 4f + p1) * 0.6f + Mathf.Sin(a * 11f + p2) * 0.3f + Mathf.Sin(a * 29f + p3) * 0.1f + 1f) * 0.5f * 2f;
                case OB_HorizonShape.Dunes: {
                    float x = a * 14f / (Mathf.PI * 2f) + p1;
                    float d = Mathf.Repeat(x, 1f);
                    float dune = d < 0.72f ? Mathf.SmoothStep(0f, 1f, d / 0.72f) : Mathf.SmoothStep(1f, 0f, (d - 0.72f) / 0.28f);
                    return 0.2f + 0.55f * dune * (0.6f + 0.4f * Mathf.Sin(a * 3f + p2)) + 0.08f * Mathf.Sin(a * 41f + p3);
                }
                case OB_HorizonShape.Peaks: {
                    float big = Mathf.Pow(Tri(a * 7f / (Mathf.PI * 2f) + p1), 1.6f);
                    float mid = Tri(a * 19f / (Mathf.PI * 2f) + p2) * 0.35f;
                    float jag = Tri(a * 71f / (Mathf.PI * 2f) + p3) * 0.08f;
                    return 0.12f + 0.75f * big + mid * (0.4f + 0.6f * big) + jag;
                }
                case OB_HorizonShape.Ridges: {
                    float ridge = Mathf.Pow(Tri(a * 9f / (Mathf.PI * 2f) + p1), 0.7f);
                    float plateau = Mathf.Min(1f, 0.4f + 0.6f * Tri(a * 4f / (Mathf.PI * 2f) + p2));
                    return 0.15f + 0.6f * ridge * plateau + 0.12f * Tri(a * 53f / (Mathf.PI * 2f) + p3);
                }
                default: {   // Shards: broken spires with low gaps between.
                    float spikes = Mathf.Pow(Mathf.Max(0f, Mathf.Sin(a * 17f + p1)), 6f);
                    float tall = Mathf.Pow(Mathf.Max(0f, Mathf.Sin(a * 5f + p2)), 2f);
                    return 0.05f + 0.85f * spikes * (0.4f + 0.6f * tall) + 0.06f * Tri(a * 37f / (Mathf.PI * 2f) + p3);
                }
            }
        }
    }
}
#endif
