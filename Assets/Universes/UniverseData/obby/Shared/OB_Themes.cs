using UnityEngine;

namespace Universes.UniverseData.obby {
    /// <summary>How a themed course block is painted (see OB_Block.shader).</summary>
    public readonly struct OB_BlockStyle {
        public readonly Color top, side, glow;
        /// <summary>How far (m) the top colour runs down the sides: a grass overhang or a snow cap.</summary>
        public readonly float cap;
        /// <summary>Emission of the edges and seams; zero for none.</summary>
        public readonly float glowStrength;
        /// <summary>0..1 share of the glow that also lights the grid seams (lava cracks), not just the edges.</summary>
        public readonly float glowSeams;
        public readonly float smoothness, grid;

        public OB_BlockStyle(Color top, Color side, float cap = 0f, float smoothness = 0.42f, float grid = 0.1f,
            Color glow = default, float glowStrength = 0f, float glowSeams = 0f) {
            this.top = top;
            this.side = side;
            this.cap = cap;
            this.smoothness = smoothness;
            this.grid = grid;
            this.glow = glow;
            this.glowStrength = glowStrength;
            this.glowSeams = glowSeams;
        }
    }

    /// <summary>What decorates a tier's floating islands.</summary>
    public enum OB_IslandProp : byte { Columns, Trees, Cacti, IceSpikes, LavaVents, Crystals }

    /// <summary>The sky, light and fog around a tier.</summary>
    public readonly struct OB_Mood {
        public readonly Color zenith, horizon, below, fog, sun, ambientSky, ambientEquator, ambientGround, cloudTop, cloudShade;
        public readonly float sunIntensity, fogStart, fogEnd;

        public OB_Mood(Color zenith, Color horizon, Color below, Color fog, Color sun, float sunIntensity,
            Color ambientSky, Color ambientEquator, Color ambientGround, Color cloudTop, Color cloudShade,
            float fogStart = 160f, float fogEnd = 950f) {
            this.zenith = zenith;
            this.horizon = horizon;
            this.below = below;
            this.fog = fog;
            this.sun = sun;
            this.sunIntensity = sunIntensity;
            this.ambientSky = ambientSky;
            this.ambientEquator = ambientEquator;
            this.ambientGround = ambientGround;
            this.cloudTop = cloudTop;
            this.cloudShade = cloudShade;
            this.fogStart = fogStart;
            this.fogEnd = fogEnd;
        }
    }

    /// <summary>One tier's look: its course blocks, islands, sky, world, and the accent the HUD uses for it.</summary>
    public sealed class OB_Theme {
        public OB_Tier tier;
        public Color accent;
        /// <summary>Platform styles, cycled per stage inside the tier.</summary>
        public OB_BlockStyle[] blocks;
        public OB_BlockStyle islandTop, islandBase;
        public OB_IslandProp prop;
        public Color propColor, propAccent;
        public OB_Mood mood;
        /// <summary>Everything around the course: landmarks, horizon, sky details, dressing, weather.</summary>
        public OB_WorldStyle world;
    }

    /// <summary>
    /// The art bible for the difficulty tiers (<see cref="OB_Tier"/>), from bright white blocks to the
    /// neon void. Authoring builds materials, islands and the client's atmosphere from this table;
    /// pieces that carry a mechanic (lava, belts, crumble, beat, springs) keep their own colours in
    /// every tier so they always read the same.
    /// </summary>
    public static class OB_Themes {
        static readonly OB_Theme[] All = Build();

        public static OB_Theme Get(OB_Tier tier) => All[Mathf.Clamp((int)tier, 0, All.Length - 1)];

        static Color Hex(int rgb, float alpha = 1f) => OB_Palette.Hex(rgb, alpha);

        static OB_Theme[] Build() => new[] {
            new OB_Theme {
                tier = OB_Tier.Basic, accent = Hex(0xEEF2FB),
                blocks = new[] {
                    new OB_BlockStyle(Hex(0xF4F6FB), Hex(0xD3DAE9), grid: 0.12f),
                    new OB_BlockStyle(Hex(0xE8ECF5), Hex(0xC4CDE2), grid: 0.12f)
                },
                islandTop = new OB_BlockStyle(Hex(0xF1F3F8), Hex(0xCDD5E6), grid: 0f),
                islandBase = new OB_BlockStyle(Hex(0xC9D1E4), Hex(0xB3BDD6), grid: 0f, smoothness: 0.25f),
                prop = OB_IslandProp.Columns, propColor = Hex(0xFAFBFE), propAccent = OB_Palette.Blocks[0],
                // Bright sky over a deeper blue below, so the white marble course stands out instead of melting into the clouds.
                mood = new OB_Mood(Hex(0x3A86E8), OB_Palette.SkyHorizon, Hex(0x86AEE6), Hex(0xA2C6F2), OB_Palette.Sun, 1.2f,
                    Hex(0x8FB0E6), Hex(0xB5C2E6), Hex(0x8F9AC7), OB_Palette.Cloud, Hex(0x8FA3DC)),
                world = BasicWorld()
            },
            new OB_Theme {
                tier = OB_Tier.Easy, accent = OB_Palette.Grass,
                blocks = new[] {
                    new OB_BlockStyle(Hex(0x6BD66F), Hex(0xB98A62), cap: 0.38f, grid: 0.06f, smoothness: 0.3f),
                    new OB_BlockStyle(Hex(0x8BDE5A), Hex(0xA9774F), cap: 0.38f, grid: 0.06f, smoothness: 0.3f)
                },
                islandTop = new OB_BlockStyle(OB_Palette.Grass, OB_Palette.Earth, cap: 0.7f, grid: 0f, smoothness: 0.25f),
                islandBase = new OB_BlockStyle(OB_Palette.Earth, Hex(0x9C7150), grid: 0f, smoothness: 0.2f),
                prop = OB_IslandProp.Trees, propColor = Hex(0x8A5A3C), propAccent = OB_Palette.Blocks[1],
                // A deep, clear spring sky over a blue cloud sea: the green islands pop, and the haze is blue
                // rather than milky, closing in sooner so other tiers' set-pieces stay out of the picture.
                mood = new OB_Mood(Hex(0x2C86E8), Hex(0xA6D6FF), Hex(0x96BFEA), Hex(0x9CC7EE), Hex(0xFFF3D9), 1.25f,
                    Hex(0x8FB6E6), Hex(0xB8CFE2), Hex(0x8FA88F), OB_Palette.Cloud, Hex(0x8FA6DC), 140f, 820f),
                world = EasyWorld()
            },
            new OB_Theme {
                tier = OB_Tier.Medium, accent = Hex(0xFFB547),
                blocks = new[] {
                    new OB_BlockStyle(Hex(0xF2D38C), Hex(0xD9895B), cap: 0.22f, grid: 0.08f, smoothness: 0.25f),
                    new OB_BlockStyle(Hex(0xEBC47A), Hex(0xC9744A), cap: 0.22f, grid: 0.08f, smoothness: 0.25f)
                },
                islandTop = new OB_BlockStyle(Hex(0xF0CF86), Hex(0xD58556), cap: 0.6f, grid: 0f, smoothness: 0.2f),
                islandBase = new OB_BlockStyle(Hex(0xC9744A), Hex(0xB2623E), grid: 0f, smoothness: 0.15f),
                prop = OB_IslandProp.Cacti, propColor = Hex(0x5BB05B), propAccent = Hex(0xFF7A9A),
                mood = new OB_Mood(Hex(0x4C93D8), Hex(0xFFDDAA), Hex(0xF1CFA0), Hex(0xF0D6B0), Hex(0xFFE2B5), 1.3f,
                    Hex(0xA4B4D6), Hex(0xE6C9A0), Hex(0xC29A72), Hex(0xFFF4E2), Hex(0xD9B597)),
                world = MediumWorld()
            },
            new OB_Theme {
                tier = OB_Tier.Hard, accent = Hex(0x7FD3FF),
                blocks = new[] {
                    new OB_BlockStyle(Hex(0xF7FBFF), Hex(0x8FCBF0), cap: 0.45f, grid: 0.05f, smoothness: 0.72f),
                    new OB_BlockStyle(Hex(0xE2F2FF), Hex(0x6FB3E6), cap: 0.45f, grid: 0.05f, smoothness: 0.8f)
                },
                islandTop = new OB_BlockStyle(Hex(0xF7FBFF), Hex(0x9AB4CF), cap: 0.9f, grid: 0f, smoothness: 0.5f),
                islandBase = new OB_BlockStyle(Hex(0x8CA3C2), Hex(0x7088A8), grid: 0f, smoothness: 0.3f),
                prop = OB_IslandProp.IceSpikes, propColor = Hex(0xA8E2FA), propAccent = Hex(0xF2FAFF),
                // Polar twilight: a deep blue zenith over a rose band at the horizon and cold blue air, so the
                // white ice course stands out against everything behind it and the aurora reads.
                mood = new OB_Mood(Hex(0x16285E), Hex(0xC9A2CF), Hex(0x6F84BE), Hex(0x7E93C8), Hex(0xFFD6BE), 1.15f,
                    Hex(0x7F97D2), Hex(0xA9B2D8), Hex(0x6F82B0), Hex(0xE9ECFF), Hex(0x8C99C8), 180f, 1200f),
                world = HardWorld()
            },
            new OB_Theme {
                tier = OB_Tier.Insane, accent = Hex(0xFF6A2E),
                blocks = new[] {
                    new OB_BlockStyle(Hex(0x4A3F48), Hex(0x2E262E), grid: 0.1f, smoothness: 0.3f, glow: Hex(0xFF5A1F), glowStrength: 2.6f, glowSeams: 0.55f),
                    new OB_BlockStyle(Hex(0x3E3239), Hex(0x251D23), grid: 0.1f, smoothness: 0.3f, glow: Hex(0xFF8A2A), glowStrength: 2.6f, glowSeams: 0.55f)
                },
                islandTop = new OB_BlockStyle(Hex(0x4A3F48), Hex(0x2B2329), grid: 0.08f, smoothness: 0.25f, glow: Hex(0xFF5A1F), glowStrength: 1.6f, glowSeams: 0.6f),
                islandBase = new OB_BlockStyle(Hex(0x2B2329), Hex(0x1F1A1F), grid: 0f, smoothness: 0.2f),
                prop = OB_IslandProp.LavaVents, propColor = Hex(0x352B32), propAccent = Hex(0xFF6A1F),
                // Clouds are ash: the fog's rust greyed down, darker underneath, so the banks sink into the haze.
                mood = new OB_Mood(Hex(0x3A2034), Hex(0xE5683A), Hex(0x5E2C2A), Hex(0x8E4A3A), Hex(0xFFB27A), 1.05f,
                    Hex(0x7A5060), Hex(0xB86A50), Hex(0x5A3030), Hex(0x7E5446), Hex(0x3A2622), 90f, 620f),
                world = InsaneWorld()
            },
            new OB_Theme {
                tier = OB_Tier.Extreme, accent = Hex(0xD07BFF),
                blocks = new[] {
                    new OB_BlockStyle(Hex(0x261D44), Hex(0x15102A), grid: 0.12f, smoothness: 0.6f, glow: Hex(0xFF3DF2), glowStrength: 3f, glowSeams: 0.2f),
                    new OB_BlockStyle(Hex(0x1E1A3E), Hex(0x110D24), grid: 0.12f, smoothness: 0.6f, glow: Hex(0x3DF5FF), glowStrength: 3f, glowSeams: 0.2f)
                },
                islandTop = new OB_BlockStyle(Hex(0x221A3E), Hex(0x140F28), grid: 0.1f, smoothness: 0.6f, glow: Hex(0xB04DFF), glowStrength: 2f),
                islandBase = new OB_BlockStyle(Hex(0x140F28), Hex(0x0D0A1C), grid: 0f, smoothness: 0.4f),
                prop = OB_IslandProp.Crystals, propColor = Hex(0x3DF5FF), propAccent = Hex(0xFF3DF2),
                // A lit violet nebula below the near-black sky, so the dark glass course reads in silhouette against it.
                mood = new OB_Mood(Hex(0x07051A), Hex(0x3C1C70), Hex(0x4A2A8C), Hex(0x3A2272), Hex(0xCDB0FF), 1f,
                    Hex(0x6A58B8), Hex(0x8466C8), Hex(0x2C1E4C), Hex(0x8C6AE0), Hex(0x3C2A80), 80f, 560f),
                world = ExtremeWorld()
            }
        };

        // ---------------------------------------------------------------- worlds
        // What surrounds each tier's stretch of course. Landmarks float a few hundred metres off the route,
        // the horizon rings sit kilometres away, and the dressing builds the course itself.

        static OB_SkyStyle Sky(Vector2 sun, float sunRadius, float sunHalo, Color cirrus) => new() {
            sun = sun, sunRadius = sunRadius, sunHalo = sunHalo, cirrus = cirrus,
            moon = Color.clear, ring = Color.clear, auroraA = Color.clear, auroraB = Color.clear, nebulaA = Color.clear, nebulaB = Color.clear
        };

        static OB_WorldStyle BasicWorld() {
            var sky = Sky(new Vector2(42f, 145f), 1.6f, 0.55f, Hex(0xFFFFFF, 0.35f));
            sky.moon = Hex(0xF4F6FF, 0.35f); sky.moonAt = new Vector2(30f, 20f); sky.moonRadius = 1.4f;
            return new OB_WorldStyle {
                landmarks = new[] { OB_LandmarkKind.SkyCastle, OB_LandmarkKind.WindmillIsle, OB_LandmarkKind.BalloonFleet },
                sky = sky,
                horizon = new OB_HorizonStyle { shape = OB_HorizonShape.CloudBanks, near = Hex(0xE4ECFB), mid = Hex(0xC2D3F2), far = Hex(0xA8BFEA), foot = Hex(0x9EC2EF), crestGlow = Color.clear, height = 170f },
                dressing = new OB_DressingStyle {
                    build = OB_BuildStyle.Marble, rock = Hex(0xEEF1F8), rockDark = Hex(0xC9D1E4), cap = OB_Palette.Gold,
                    frame = Hex(0xFAFBFE), frameDark = Hex(0xD3DAE9), cloth = new[] { OB_Palette.Blocks[0], OB_Palette.Blocks[6], OB_Palette.Blocks[2] },
                    glow = Hex(0xFFE7A8), glowStrength = 1.6f, rope = OB_Palette.Gold
                },
                weather = new OB_WeatherStyle { kind = OB_WeatherKind.Pollen, a = Hex(0xFFFFFF), b = OB_Palette.Gold, rate = 14f },
                fall = OB_FallKind.Water, fallColor = Hex(0xBFE6FF), fallHighlight = Hex(0xFFFFFF),
                rockTop = Hex(0xF1F3F8), rockSide = Hex(0xC9D1E4), density = 1f,
                flocks = 3, birds = Hex(0xFFFFFF), field = Hex(0x6E7CFF)
            };
        }

        static OB_WorldStyle EasyWorld() => new() {
            landmarks = new[] { OB_LandmarkKind.WorldTree, OB_LandmarkKind.WaterfallCliffs },
            sky = Sky(new Vector2(55f, 110f), 1.6f, 0.6f, Hex(0xFFFFFF, 0.25f)),
            horizon = new OB_HorizonStyle { shape = OB_HorizonShape.Hills, near = Hex(0x6FAE7E), mid = Hex(0x86B6A8), far = Hex(0xA2C3D6), foot = Hex(0xB4DDFF), crestGlow = Color.clear, height = 220f },
            dressing = new OB_DressingStyle {
                build = OB_BuildStyle.Earth, rock = OB_Palette.Earth, rockDark = Hex(0x8A5A3C), cap = OB_Palette.Grass,
                frame = Hex(0x9C6B45), frameDark = Hex(0x6E4A30), cloth = new[] { Hex(0xFF6F5E), Hex(0xFFE066), Hex(0x5AB8FF) },
                glow = Hex(0xFFD27A), glowStrength = 1.8f, rope = Hex(0xC9A26B)
            },
            weather = new OB_WeatherStyle { kind = OB_WeatherKind.Leaves, a = OB_Palette.Grass, b = Hex(0xFFB547), rate = 10f },
            fall = OB_FallKind.Water, fallColor = Hex(0x5FC4F0), fallHighlight = Hex(0xEAFBFF),
            rockTop = OB_Palette.Grass, rockSide = OB_Palette.Earth, density = 1.1f,
            flocks = 3, birds = Hex(0x3A3036), field = Hex(0x8F6BFF)
        };

        static OB_WorldStyle MediumWorld() => new() {
            landmarks = new[] { OB_LandmarkKind.Pyramids, OB_LandmarkKind.MesaArch },
            // A big low golden sun off to the right of the route.
            sky = Sky(new Vector2(14f, 62f), 2.2f, 1.1f, Hex(0xFFE0B8, 0.45f)),
            horizon = new OB_HorizonStyle { shape = OB_HorizonShape.Dunes, near = Hex(0xE0A46E), mid = Hex(0xE9BC8C), far = Hex(0xF2D2AA), foot = Hex(0xFFDDAA), crestGlow = Color.clear, height = 180f },
            dressing = new OB_DressingStyle {
                build = OB_BuildStyle.Sandstone, rock = Hex(0xE3A66B), rockDark = Hex(0xC9744A), cap = Hex(0xF2D38C),
                frame = Hex(0xF0CF86), frameDark = Hex(0xB2623E), cloth = new[] { Hex(0xE8473C), Hex(0x2F8F9D), Hex(0xFFB547) },
                glow = Hex(0xFF9F40), glowStrength = 2f, rope = Hex(0x8A5A3C)
            },
            weather = new OB_WeatherStyle { kind = OB_WeatherKind.Sand, a = Hex(0xF2D38C), b = Hex(0xE3A66B), rate = 60f },
            fall = OB_FallKind.Sand, fallColor = Hex(0xE9BC8C), fallHighlight = Hex(0xFFE2B5),
            rockTop = Hex(0xF0CF86), rockSide = Hex(0xC9744A), density = 1f,
            flocks = 2, birds = Hex(0x4A3030), field = Hex(0x3D8BFF)
        };

        static OB_WorldStyle HardWorld() {
            var sky = Sky(new Vector2(8f, -70f), 1.8f, 0.9f, Hex(0xDDE8FF, 0.2f));
            sky.moon = Hex(0xEAF2FF, 0.8f); sky.moonAt = new Vector2(35f, 25f); sky.moonRadius = 2.4f;
            sky.stars = 0.25f;
            sky.auroraA = Hex(0x5CFFB0); sky.auroraB = Hex(0x6A7BFF); sky.aurora = 1.2f;
            return new OB_WorldStyle {
                landmarks = new[] { OB_LandmarkKind.IceMountain, OB_LandmarkKind.GlacierSpires },
                sky = sky,
                horizon = new OB_HorizonStyle { shape = OB_HorizonShape.Peaks, near = Hex(0xC4D2F2), mid = Hex(0x8E9FD3), far = Hex(0x6C7EBA), foot = Hex(0x7E93C8), crestGlow = Color.clear, height = 360f },
                dressing = new OB_DressingStyle {
                    build = OB_BuildStyle.Ice, rock = Hex(0x8CA3C2), rockDark = Hex(0x7088A8), cap = Hex(0xF7FBFF),
                    frame = Hex(0xCFEFFF), frameDark = Hex(0x8FCBF0), cloth = new[] { Hex(0x7FD3FF), Hex(0xF2FAFF), Hex(0x6A7BFF) },
                    glow = Hex(0x9FE8FF), glowStrength = 2.2f, rope = Hex(0xE2F2FF)
                },
                weather = new OB_WeatherStyle { kind = OB_WeatherKind.Snow, a = Hex(0xFFFFFF), b = Hex(0xDDEBFF), rate = 70f },
                fall = OB_FallKind.Ice, fallColor = Hex(0xBDEBFF), fallHighlight = Hex(0xFFFFFF), fallGlow = 0.3f,
                rockTop = Hex(0xF7FBFF), rockSide = Hex(0x8CA3C2), density = 1f,
                flocks = 1, birds = Hex(0xFFFFFF), field = Hex(0xA45CFF)
            };
        }

        static OB_WorldStyle InsaneWorld() {
            // An eclipse: a dark moon sits almost on the sun, leaving a burning corona.
            var sky = Sky(new Vector2(24f, 15f), 2.4f, 1.4f, Hex(0x3A2228, 0.5f));
            sky.moon = Hex(0x1A0A0A, 1f); sky.moonAt = new Vector2(24.3f, 15.4f); sky.moonRadius = 2f;
            sky.stars = 0.05f;
            return new OB_WorldStyle {
                landmarks = new[] { OB_LandmarkKind.Volcano, OB_LandmarkKind.BasaltSpires },
                sky = sky,
                horizon = new OB_HorizonStyle { shape = OB_HorizonShape.Ridges, near = Hex(0x2A1A20), mid = Hex(0x4A2A2C), far = Hex(0x7A3E34), foot = Hex(0xB85A3A), crestGlow = Hex(0xFF5A1F, 0.9f), height = 300f },
                dressing = new OB_DressingStyle {
                    build = OB_BuildStyle.Basalt, rock = Hex(0x3A3036), rockDark = Hex(0x241C21), cap = Hex(0x4A3F48),
                    frame = Hex(0x2E262E), frameDark = Hex(0x1F1A1F), cloth = new[] { Hex(0x8E1B1B), Hex(0xFF6A2E), Hex(0x2E262E) },
                    glow = Hex(0xFF5A1F), glowStrength = 3f, rope = Hex(0x4F4650)
                },
                weather = new OB_WeatherStyle { kind = OB_WeatherKind.Embers, a = Hex(0xFF8A2A), b = Hex(0x3A3036), rate = 50f },
                fall = OB_FallKind.Lava, fallColor = Hex(0xFF4D2E), fallHighlight = Hex(0xFFD34D), fallGlow = 2.5f,
                rockTop = Hex(0x4A3F48), rockSide = Hex(0x241C21), density = 2.2f,
                flocks = 0, birds = Color.clear, cloudCover = 0.75f, cloudSquash = 0.2f, field = Hex(0x5FE6FF)
            };
        }

        static OB_WorldStyle ExtremeWorld() {
            var sky = Sky(new Vector2(35f, 130f), 0.8f, 0.2f, Color.clear);
            sky.moon = Hex(0xB98CFF, 1f); sky.moonAt = new Vector2(22f, -25f); sky.moonRadius = 7f;
            sky.ring = Hex(0xFFB3F2, 0.8f);
            sky.stars = 0.6f;
            sky.auroraA = Hex(0xFF3DF2); sky.auroraB = Hex(0x3DF5FF); sky.aurora = 0.6f;
            sky.nebulaA = Hex(0x7A2BD9); sky.nebulaB = Hex(0x2BD9D9); sky.nebula = 0.9f;
            return new OB_WorldStyle {
                landmarks = new[] { OB_LandmarkKind.ShatteredPlanet, OB_LandmarkKind.PortalRings },
                sky = sky,
                horizon = new OB_HorizonStyle { shape = OB_HorizonShape.Shards, near = Hex(0x150F30), mid = Hex(0x231748), far = Hex(0x33205F), foot = Hex(0x3C1C70), crestGlow = Hex(0x9B5CFF, 0.5f), height = 300f },
                dressing = new OB_DressingStyle {
                    build = OB_BuildStyle.Crystal, rock = Hex(0x221A3E), rockDark = Hex(0x140F28), cap = Hex(0xB04DFF),
                    frame = Hex(0x261D44), frameDark = Hex(0x15102A), cloth = new[] { Hex(0xFF3DF2), Hex(0x3DF5FF), Hex(0xB04DFF) },
                    glow = Hex(0x3DF5FF), glowStrength = 3f, rope = Hex(0xFF3DF2)
                },
                weather = new OB_WeatherStyle { kind = OB_WeatherKind.VoidMotes, a = Hex(0xFF3DF2), b = Hex(0x3DF5FF), rate = 30f },
                fall = OB_FallKind.Void, fallColor = Hex(0x7A2BD9), fallHighlight = Hex(0x3DF5FF), fallGlow = 2f,
                rockTop = Hex(0x2A2050), rockSide = Hex(0x140F28), density = 2.2f,
                flocks = 3, birds = Hex(0x3DF5FF), birdGlow = 2f, cloudCover = 0f, field = Hex(0x3DF5FF)
            };
        }
    }
}
