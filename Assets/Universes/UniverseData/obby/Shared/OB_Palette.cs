using UnityEngine;

namespace Universes.UniverseData.obby {
    /// <summary>
    /// Sky Obby's art bible: the one place its colours are defined. Authoring (materials, sky, fog,
    /// HUD, effects) and runtime presentation read from here, so the look stays consistent.
    /// <para>
    /// Style: a chunky toy-block playground floating above a sunlit sea of clouds. Candy-coloured
    /// blocks with soft bevels, a warm late-morning sun with cool periwinkle bounce light, and
    /// everything you can collect glows gold.
    /// </para>
    /// </summary>
    public static class OB_Palette {
        // Sky and atmosphere
        public static readonly Color SkyZenith = Hex(0x3F8FEA);
        public static readonly Color SkyHorizon = Hex(0xA6D2FF);
        public static readonly Color SkyBelow = Hex(0xC8D5F5);
        public static readonly Color Sun = Hex(0xFFF1D6);
        public static readonly Color Fog = Hex(0xB2D1F6);

        // Clouds
        public static readonly Color Cloud = Hex(0xFFF8F0);
        public static readonly Color CloudShade = Hex(0x9DAEE2);

        // UI ink and accents
        public static readonly Color Ink = Hex(0x1E2A4A);
        public static readonly Color InkSoft = Hex(0x33416B);
        public static readonly Color Paper = Hex(0xF7F9FF);
        public static readonly Color Gold = Hex(0xFFC940);
        public static readonly Color Lava = Hex(0xFF4D2E);
        public static readonly Color Gravity = Hex(0x43E0FF);
        public static readonly Color Go = Hex(0x5BE38A);
        public static readonly Color Silver = Hex(0xD3DCEF);
        public static readonly Color Bronze = Hex(0xE89A5E);
        /// <summary>Stars (pro-route collectibles): a whiter gold than coins, so the two never read alike.</summary>
        public static readonly Color Star = Hex(0xFFE98A);

        /// <summary>The colour of a medal (gold, silver, bronze), or soft ink for none.</summary>
        public static Color Medal(OB_Medal medal) => medal switch {
            OB_Medal.Gold => Gold,
            OB_Medal.Silver => Silver,
            OB_Medal.Bronze => Bronze,
            _ => InkSoft
        };

        // Course blocks, cycled per stage
        public static readonly Color[] Blocks = {
            Hex(0x5AB8FF), // sky
            Hex(0x4FD1A5), // mint
            Hex(0xFFB547), // tangerine
            Hex(0xFF7A59), // coral
            Hex(0x9B7BFF), // grape
            Hex(0x3FD0D4), // lagoon
            Hex(0xFF6FB5), // bubblegum
            Hex(0xFFE066)  // lemon
        };

        // Mechanics: each has its own colour, the same in every theme, so a mechanic reads at a glance.
        public static readonly Color OnOffRed = Hex(0xFF4F6A);
        public static readonly Color OnOffBlue = Hex(0x3F7BFF);
        public static readonly Color Laser = Hex(0xFF2E6E);
        public static readonly Color Steel = Hex(0x6E7A96);
        public static readonly Color Warning = Hex(0xFFD23F);
        public static readonly Color Wood = Hex(0xB0743F);
        public static readonly Color WoodDark = Hex(0x7A4A24);
        public static readonly Color MushroomCap = Hex(0xFF5FA2);
        public static readonly Color MushroomSpots = Hex(0xFFF4F8);
        public static readonly Color MushroomStalk = Hex(0xF3E3C8);
        public static readonly Color Cannon = Hex(0x39415E);
        public static readonly Color Turntable = Hex(0x8B6CFF);
        public static readonly Color Flip = Hex(0xB36BFF);
        public static readonly Color Beam = Hex(0xF2F5FF);
        public static readonly Color Ice = Hex(0xBFEFFF);
        public static readonly Color IceDeep = Hex(0x7FCBF2);
        public static readonly Color Wind = Hex(0x73B8FF);
        public static readonly Color CrusherStone = Hex(0x8E97B0);
        public static readonly Color Ghost = Hex(0xD9F4FF);
        public static readonly Color Honey = Hex(0xFFB42E);
        public static readonly Color HoneyDeep = Hex(0xC9731A);
        public static readonly Color Ring = Hex(0x39E6C0);
        public static readonly Color Lift = Hex(0x7C8CFF);
        public static readonly Color Rope = Hex(0xD7B98A);

        // Neutral blocks (lobby, checkpoint pads, movers)
        public static readonly Color Stone = Hex(0xE4E9F5);
        public static readonly Color StoneDark = Hex(0xB8C2DC);
        public static readonly Color Grass = Hex(0x6BD66F);
        public static readonly Color Earth = Hex(0xB98A62);

        /// <summary>A colour from a 0xRRGGBB literal, with optional alpha.</summary>
        public static Color Hex(int rgb, float alpha = 1f) =>
            new(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, alpha);

        /// <summary><paramref name="color"/> with alpha <paramref name="alpha"/>.</summary>
        public static Color WithAlpha(this Color color, float alpha) => new(color.r, color.g, color.b, alpha);
    }
}
