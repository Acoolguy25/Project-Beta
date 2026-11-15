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

    /// <summary>One tier's look: its course blocks, islands, sky, and the accent the HUD uses for it.</summary>
    public sealed class OB_Theme {
        public OB_Tier tier;
        public Color accent;
        /// <summary>Platform styles, cycled per stage inside the tier.</summary>
        public OB_BlockStyle[] blocks;
        public OB_BlockStyle islandTop, islandBase;
        public OB_IslandProp prop;
        public Color propColor, propAccent;
        public OB_Mood mood;
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

        static Color Hex(int rgb) => OB_Palette.Hex(rgb);

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
                mood = new OB_Mood(OB_Palette.SkyZenith, OB_Palette.SkyHorizon, OB_Palette.SkyBelow, OB_Palette.Fog, OB_Palette.Sun, 1.2f,
                    Hex(0x8FB0E6), Hex(0xB5C2E6), Hex(0x8F9AC7), OB_Palette.Cloud, OB_Palette.CloudShade)
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
                mood = new OB_Mood(Hex(0x3A95F0), Hex(0xB4DDFF), Hex(0xC8DDF2), Hex(0xB6D8F4), Hex(0xFFF3D9), 1.25f,
                    Hex(0x8FB6E6), Hex(0xB8CFE2), Hex(0x8FA88F), OB_Palette.Cloud, OB_Palette.CloudShade)
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
                    Hex(0xA4B4D6), Hex(0xE6C9A0), Hex(0xC29A72), Hex(0xFFF4E2), Hex(0xD9B597))
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
                mood = new OB_Mood(Hex(0x6A9AD6), Hex(0xDCEAFA), Hex(0xE4EDF8), Hex(0xD3E2F3), Hex(0xEAF2FF), 1.1f,
                    Hex(0xA9C2EA), Hex(0xCFDCF0), Hex(0xA3B5D6), Hex(0xF6FAFF), Hex(0xB1C1E0), 120f, 800f)
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
                mood = new OB_Mood(Hex(0x3A2034), Hex(0xE5683A), Hex(0x5E2C2A), Hex(0x8E4A3A), Hex(0xFFB27A), 1.05f,
                    Hex(0x7A5060), Hex(0xB86A50), Hex(0x5A3030), Hex(0x7A6068), Hex(0x3C2E35), 90f, 620f)
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
                mood = new OB_Mood(Hex(0x07051A), Hex(0x3C1C70), Hex(0x130A2C), Hex(0x2A1652), Hex(0xCDB0FF), 0.85f,
                    Hex(0x55459A), Hex(0x6E52B0), Hex(0x24183F), Hex(0x4C3C80), Hex(0x1E1642), 80f, 560f)
            }
        };
    }
}
