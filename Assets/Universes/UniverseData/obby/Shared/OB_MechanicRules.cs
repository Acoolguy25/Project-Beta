using System;
using UnityEngine;

namespace Universes.UniverseData.obby {
    /// <summary>
    /// A tunable that moves with a segment's difficulty: <see cref="easy"/> at difficulty 0 and
    /// <see cref="hard"/> at 1 (the hardest stage of the hardest tier).
    /// </summary>
    [Serializable]
    public struct OB_Span {
        public float easy, hard;

        public OB_Span(float easy, float hard) {
            this.easy = easy;
            this.hard = hard;
        }

        public float At(float difficulty) => easy + (hard - easy) * Mathf.Clamp01(difficulty);

        /// <summary>A whole count at <paramref name="difficulty"/> (rounded), never below <paramref name="minimum"/>.</summary>
        public int Count(float difficulty, int minimum = 1) => Mathf.Max(minimum, Mathf.RoundToInt(At(difficulty)));

        /// <summary>
        /// A whole count the way the classic segments grow: <see cref="easy"/> rounded, plus the span's growth
        /// at <paramref name="difficulty"/> rounded on its own.
        /// </summary>
        public int Steps(float difficulty) =>
            Mathf.RoundToInt(easy) + Mathf.RoundToInt(Mathf.Clamp01(difficulty) * (hard - easy));
    }

    /// <summary>ON/OFF Blocks: switch runs of red and blue blocks.</summary>
    [Serializable]
    public sealed class OB_OnOffTuning {
        [Tooltip("Runs of blocks, each behind its own switch.")]
        public OB_Span runs = new(2f, 3.5f);
        [Tooltip("Blocks per run before the random extra.")]
        public OB_Span blocks = new(2f, 3f);
        [Tooltip("A run gets 0 to this many extra blocks, at random.")]
        [Min(0)] public int extraBlocks = 1;
        [Tooltip("From this difficulty the switches can be \"!\" blocks bumped from below instead of floor buttons.")]
        [Range(0f, 1f)] public float bumpFromDifficulty = 0.45f;
        [Tooltip("Chance a stage past that difficulty uses \"!\" blocks (1 forces them, for tests and captures).")]
        [Range(0f, 1f)] public float bumpChance = 0.6f;
        [Tooltip("Par seconds per switch: stepping on a floor button, or stopping to jump into a \"!\" block.")]
        [Min(0f)] public float buttonPar = 0.8f, bumpPar = 1.4f;
        [Tooltip("Open platform (m) on every side of a \"!\" block, so the jump on can start from clear sky.")]
        [Min(0.5f)] public float bumpRoom = 1.5f;
        [Tooltip("Rise range of each block, and of each switch platform, as a share of the tier's highest rise.")]
        public Vector2 blockRise = new(-0.2f, 0.6f), padRise = new(-0.2f, 0.4f);
        [Tooltip("Sideways jink of each block either way, metres, and the blocks' thickness.")]
        [Min(0f)] public float blockJink = 2f;
        [Min(0.1f)] public float blockThickness = 0.8f;
    }

    /// <summary>Hammer Bridge: pendulum hammers over a bridge, later a split one.</summary>
    [Serializable]
    public sealed class OB_HammerTuning {
        public OB_Span hammers = new(2f, 4f);
        [Tooltip("Bridge width at the easiest; at the hardest it is the tier's beam width times hardWidthScale, never under minWidth.")]
        [Min(0.5f)] public float easyWidth = 3.6f;
        [Min(0.5f)] public float hardWidthScale = 1.6f, minWidth = 1.6f;
        [Tooltip("Metres between hammers along the bridge.")]
        public OB_Span spacing = new(7f, 5.5f);
        [Tooltip("Arm length (pivot to head), metres.")]
        public OB_Span arm = new(8f, 9.5f);
        [Tooltip("Swing amplitude either side of hanging straight down, degrees.")]
        public OB_Span amplitude = new(50f, 70f);
        [Tooltip("Seconds per full swing.")]
        public OB_Span period = new(3.4f, 2f);
        public OB_Span headWidth = new(1.6f, 2.2f);
        [Min(0.2f)] public float headDepth = 2.2f;
        [Tooltip("Bridge left clear before the first and after the last hammer.")]
        [Min(0f)] public float endRoom = 2.5f;
        [Tooltip("From this difficulty the bridge is split, a short piece under each hammer.")]
        [Range(0f, 1f)] public float splitFromDifficulty = 0.6f;
        [Tooltip("Split bridge: gap bias on the tier's tension, then scale (the hops under the hammers are short).")]
        public float splitTension = -0.4f;
        [Range(0.1f, 1f)] public float splitTensionScale = 0.5f;
        [Min(0.1f)] public float bridgeThickness = 0.8f;
        [Tooltip("Random extra phase per hammer, seconds.")]
        [Min(0f)] public float phaseJitter = 0.2f;
        [Tooltip("Par per hammer, as a share of its swing (waiting for it).")]
        [Min(0f)] public float parShare = 0.35f;
    }

    /// <summary>Thwomp Alley: crushers slamming across a corridor.</summary>
    [Serializable]
    public sealed class OB_CrusherTuning {
        public OB_Span crushers = new(2f, 4f);
        [Tooltip("Corridor width, metres.")]
        public OB_Span width = new(5f, 3.6f);
        [Tooltip("Crusher depth along the path, metres.")]
        public OB_Span depth = new(4f, 3.2f);
        [Tooltip("Safe floor between crushers, metres.")]
        public OB_Span safe = new(4.5f, 3f);
        [Tooltip("Seconds per slam cycle, and seconds it holds down.")]
        public OB_Span period = new(3.2f, 2f), hold = new(0.8f, 0.5f);
        [Tooltip("Floor before the first crusher, metres.")]
        [Min(0f)] public float lead = 3f;
        [Tooltip("How far a crusher overhangs the corridor's sides, metres.")]
        [Min(0f)] public float overhang = 0.5f;
        [Tooltip("Below this difficulty the crushers lift in a wave a steady runner can follow; above it each keeps its own beat.")]
        [Range(0f, 1f)] public float waveBelowDifficulty = 0.7f;
        [Tooltip("Floor past the last crusher, metres.")]
        [Min(0f)] public float tail = 1f;
        [Tooltip("Par added for the corridor, as a share of a slam cycle.")]
        [Min(0f)] public float parShare = 0.6f;
    }

    /// <summary>Laser Gates: blinking curtains and always-on low beams.</summary>
    [Serializable]
    public sealed class OB_LaserTuning {
        public OB_Span gates = new(3f, 6f);
        public OB_Span width = new(5f, 3.6f), spacing = new(6f, 4.5f);
        [Tooltip("Seconds per on/off cycle, and the share of it a curtain is on.")]
        public OB_Span period = new(2.8f, 1.6f), onFraction = new(0.4f, 0.55f);
        [Tooltip("From this difficulty every other gate may be a low beam to jump instead, with this chance.")]
        [Range(0f, 1f)] public float lowBeamFromDifficulty = 0.35f;
        [Range(0f, 1f)] public float lowBeamChance = 0.6f;
        [Tooltip("Below this difficulty the curtains switch in a wave; above it each has its own phase.")]
        [Range(0f, 1f)] public float waveBelowDifficulty = 0.65f;
        [Tooltip("Floor before the first gate, and how far gates overhang the corridor's sides, metres.")]
        [Min(0f)] public float lead = 3f, overhang = 0.3f;
        [Tooltip("Low beam: top above the floor, height and depth along the path, metres.")]
        [Min(0.1f)] public float lowBeamTop = 0.9f, lowBeamHeight = 0.6f, lowBeamDepth = 0.4f;
        [Tooltip("Par per curtain, as a share of the time it is on (waiting for it).")]
        [Min(0f)] public float parShare = 0.4f;
    }

    /// <summary>Log Roll: spinning logs along the path.</summary>
    [Serializable]
    public sealed class OB_LogTuning {
        public OB_Span logs = new(1f, 3f);
        public OB_Span radius = new(1.6f, 1.1f), length = new(9f, 14f);
        [Tooltip("Surface speed of the spin, m/s.")]
        public OB_Span spin = new(2.5f, 6f);
        [Tooltip("Rise either way and sideways jink either way, metres.")]
        [Min(0f)] public float rise = 0.4f, jink = 1f;
        [Tooltip("Par for a log, as a multiple of walking its length.")]
        [Min(0f)] public float parFactor = 1.4f;
    }

    /// <summary>Turntables: spinning discs with bumper posts.</summary>
    [Serializable]
    public sealed class OB_TurntableTuning {
        public OB_Span discs = new(1f, 3f);
        public OB_Span diameter = new(14f, 10f);
        [Tooltip("Spin, degrees per second.")]
        public OB_Span spin = new(25f, 60f);
        public OB_Span posts = new(2f, 4f);
        [Tooltip("Rise either way, as a share of the tier's highest rise.")]
        [Range(0f, 1f)] public float riseShare = 0.5f;
        [Tooltip("Gap bias on the tier's tension.")]
        public float tension = -0.1f;
        [Min(0.1f)] public float thickness = 1f;
        [Tooltip("Par seconds added per disc.")]
        [Min(0f)] public float parExtra = 0.6f;
    }

    /// <summary>Cannon Shot: barrels that fire you onto islands.</summary>
    [Serializable]
    public sealed class OB_CannonTuning {
        [Tooltip("From this difficulty a second cannon on the island fires you on.")]
        [Range(0f, 1f)] public float twoFromDifficulty = 0.5f;
        [Tooltip("Launch speed up and forward, m/s.")]
        public OB_Span up = new(14f, 17f), forward = new(16f, 24f);
        [Tooltip("Island's sideways offset from the no-steering landing, as a share of what steering can reach.")]
        public OB_Span offset = new(0.05f, 0.45f);
        [Tooltip("Island landing size, metres.")]
        public OB_Span target = new(8f, 4.5f);
        [Tooltip("Pad before the first cannon (width, depth), and each cannon's distance from its pad's back edge or island's far edge, metres.")]
        public Vector2 pad = new(5f, 6f);
        [Min(0f)] public float cannonBack = 1.5f;
        [Tooltip("Island height range relative to the cannon, metres.")]
        public Vector2 rise = new(-2f, 3f);
        [Tooltip("An island with a cannon on it is this much deeper, metres.")]
        [Min(0f)] public float islandExtra = 4f;
        [Tooltip("Par seconds added to each flight.")]
        [Min(0f)] public float parExtra = 1.2f;
    }

    /// <summary>Bounce Shrooms: a chain of bouncy caps.</summary>
    [Serializable]
    public sealed class OB_MushroomTuning {
        public OB_Span caps = new(3f, 5f);
        [Tooltip("Cap diameter, metres.")]
        public OB_Span cap = new(3.4f, 1.8f);
        [Tooltip("Bounce height, metres.")]
        public OB_Span bounce = new(4.5f, 6.5f);
        [Tooltip("Rise either way and sideways jink either way, metres.")]
        [Min(0f)] public float rise = 2f, jink = 3f;
        [Tooltip("Landing platform height range after the last cap, metres.")]
        public Vector2 landingRise = new(-1f, 2f);
        [Tooltip("Par per cap, as a share of a bounce's flight.")]
        [Min(0f)] public float parShare = 0.8f;
    }

    /// <summary>Balance Beams: narrow jinking beams.</summary>
    [Serializable]
    public sealed class OB_BeamTuning {
        public OB_Span beams = new(3f, 5f);
        [Tooltip("Beams start this much wider than the tier's beam width and narrow to it.")]
        [Min(0f)] public float extraWidth = 0.6f;
        [Tooltip("Beam length range, metres.")]
        public Vector2 length = new(6f, 10f);
        [Tooltip("Share of the tier's mix chance with which a beam crumbles.")]
        [Range(0f, 1f)] public float crumbleShare = 0.4f;
        [Tooltip("Rise range of each beam, and its sideways jink range (alternating sides), metres.")]
        public Vector2 rise = new(-0.5f, 0.8f), jink = new(2f, 4f);
        [Tooltip("Share of the last beam's offset pulled back toward the path's centre.")]
        [Range(0f, 1f)] public float centering = 0.3f;
        [Tooltip("Gaps as a share of a resting hop's.")]
        [Range(0.1f, 1f)] public float tensionScale = 0.75f;
        [Min(0.1f)] public float thickness = 0.6f;
        [Tooltip("Par for a beam, as a share of walking its length.")]
        [Min(0f)] public float parShare = 0.4f;
    }

    /// <summary>Lava Rise: a zigzag climb ahead of rising lava.</summary>
    [Serializable]
    public sealed class OB_LavaRiseTuning {
        public OB_Span steps = new(6f, 9f);
        [Tooltip("Lava speed leaves a clean climber this much time to spare (1.5 = half as long again).")]
        public OB_Span slack = new(1.7f, 1.3f);
        [Tooltip("Seconds before the lava starts rising.")]
        public OB_Span delay = new(1.5f, 0.8f);
        [Tooltip("Lava starts this far under the base platform, metres.")]
        [Min(0f)] public float startBelow = 2.5f;
        [Tooltip("Base and top platform size, room either side of the climb, and lava below its start, metres.")]
        [Min(1f)] public float baseSize = 6f, topSize = 5f;
        [Min(0f)] public float sideRoom = 4f, lavaFloor = 6f;
        [Tooltip("Zigzag either side of the path, metres; rise of each step and of the top, as a share of the tier's highest rise.")]
        public Vector2 zigzag = new(2f, 3.5f), stepRise = new(0.75f, 1f), topRise = new(0.5f, 0.9f);
        [Tooltip("Gap bias on the tier's tension for each step.")]
        public float tension = -0.1f;
    }

    /// <summary>Gust Gaps: hops across crosswinds.</summary>
    [Serializable]
    public sealed class OB_WindTuning {
        public OB_Span hops = new(4f, 7f);
        [Tooltip("Steady sideways push, as a share of walking speed.")]
        public OB_Span calm = new(0.22f, 0.38f);
        [Tooltip("Extra push at the peak of a gust, as a share of the steady push.")]
        public OB_Span gust = new(0.25f, 0.5f);
        [Tooltip("Seconds per gust cycle.")]
        public OB_Span period = new(3.6f, 2.4f);
        [Tooltip("Platform size, metres.")]
        public OB_Span size = new(3.6f, 2.6f);
        [Tooltip("Chance a hop steps up, and the share of a normal rise it takes.")]
        [Range(0f, 1f)] public float upChance = 0.5f, riseScale = 0.5f;
        [Tooltip("Sideways jink either way, metres.")]
        [Min(0f)] public float jink = 1.5f;
        [Tooltip("Par per gap, as a share of a gust cycle (timing the calm).")]
        [Min(0f)] public float parShare = 0.25f;
    }

    /// <summary>Ice Rink: slippery sheets.</summary>
    [Serializable]
    public sealed class OB_IceTuning {
        public OB_Span sheets = new(3f, 5f);
        public OB_Span width = new(5f, 3.2f);
        [Tooltip("Grip as a share of normal (1 = no ice).")]
        public OB_Span traction = new(0.2f, 0.1f);
        [Tooltip("Sheet length range, metres.")]
        public Vector2 length = new(8f, 12f);
        [Tooltip("A player stopped on a sheet runs up over its length less this, metres (room to turn round).")]
        [Min(0f)] public float runUpMargin = 1f;
        [Tooltip("Rise range of each sheet, and its sideways jink range (alternating sides), metres.")]
        public Vector2 rise = new(-0.8f, 0.3f), jink = new(1.5f, 3f);
        [Tooltip("Share of the last sheet's offset pulled back toward the path's centre.")]
        [Range(0f, 1f)] public float centering = 0.3f;
        [Tooltip("Gap bias on the tier's tension.")]
        public float tension = -0.25f;
        [Min(0.1f)] public float thickness = 0.8f;
        [Tooltip("Par for a sheet, as a share of walking its length (players carry speed across).")]
        [Min(0f)] public float parShare = 0.5f;
    }

    /// <summary>Flip Gap: flip arrows onto a ceiling over a gap too long to jump.</summary>
    [Serializable]
    public sealed class OB_FlipTuning {
        public OB_Span width = new(5f, 3.4f);
        [Tooltip("Ceiling height above the robot's head, metres.")]
        public OB_Span headroom = new(2.5f, 4f);
        [Tooltip("From this difficulty a mid-air orb flips you onto a second ceiling.")]
        [Range(0f, 1f)] public float twoCeilingsFromDifficulty = 0.55f;
        [Tooltip("From this difficulty lava patches hang under the ceiling.")]
        [Range(0f, 1f)] public float lavaFromDifficulty = 0.3f;
        [Tooltip("Lava patch length, metres.")]
        public OB_Span patch = new(1.4f, 3f);
    }

    /// <summary>Corkscrew: gravity rolls round a square tube.</summary>
    [Serializable]
    public sealed class OB_CorkscrewTuning {
        [Tooltip("Half the tube's width, metres (never less than the robot needs to stand on a face).")]
        public OB_Span halfWidth = new(5.5f, 4.6f);
        [Tooltip("From this difficulty each face has two lava strips.")]
        [Range(0f, 1f)] public float twoStripsFromDifficulty = 0.6f;
        [Tooltip("Lava strip length, metres.")]
        public OB_Span strip = new(1.4f, 3f);
    }

    /// <summary>
    /// Per-mechanic shapes, one block per mechanic the overhaul added. Each value is a design knob with
    /// the shipped value as its default; <see cref="Validate"/> rejects values that cannot build.
    /// </summary>
    [Serializable]
    public sealed class OB_MechanicRules {
        public OB_OnOffTuning onOff = new();
        public OB_HammerTuning hammers = new();
        public OB_CrusherTuning crushers = new();
        public OB_LaserTuning lasers = new();
        public OB_LogTuning logs = new();
        public OB_TurntableTuning turntables = new();
        public OB_CannonTuning cannons = new();
        public OB_MushroomTuning mushrooms = new();
        public OB_BeamTuning beams = new();
        public OB_LavaRiseTuning lavaRise = new();
        public OB_WindTuning wind = new();
        public OB_IceTuning ice = new();
        public OB_FlipTuning flip = new();
        public OB_CorkscrewTuning corkscrew = new();
        // The original segments and the gravity segments (OB_ClassicRules.cs).
        public OB_CrumbleMixTuning crumbleMix = new();
        public OB_HopTuning hops = new();
        public OB_SpringTuning springs = new();
        public OB_MoonLeapTuning moonLeap = new();
        public OB_ConveyorTuning conveyors = new();
        public OB_CrumbleTuning crumble = new();
        public OB_BeatTuning beat = new();
        public OB_WallJumpTuning wallJump = new();
        public OB_MoverTuning movers = new();
        public OB_SpinnerTuning spinner = new();
        public OB_LavaRunTuning lavaRun = new();
        public OB_BoostTuning boost = new();
        public OB_GravityTuning gravity = new();
        public OB_WallWalkTuning wallWalk = new();
        public OB_CeilingRunTuning ceilingRun = new();
        public OB_GravityShapeTuning gravityShapes = new();
        [Tooltip("Gap bias on the tier's tension for the hop onto a segment's first platform, and onto a resting platform (negative: shorter).")]
        public float entryTension = -0.2f, restTension = -0.3f;

        public void Validate() {
            Positive(nameof(hammers) + ".period", hammers.period);
            Positive(nameof(crushers) + ".period", crushers.period);
            Positive(nameof(lasers) + ".period", lasers.period);
            Positive(nameof(wind) + ".period", wind.period);
            Positive(nameof(mushrooms) + ".bounce", mushrooms.bounce);
            Positive(nameof(lavaRise) + ".slack", lavaRise.slack);
            if (ice.traction.easy <= 0f || ice.traction.hard <= 0f || ice.traction.easy > 1f || ice.traction.hard > 1f)
                throw new ArgumentException("Mechanics: ice.traction must lie in (0, 1].");
            if (ice.length.x < 2f || ice.length.y < ice.length.x)
                throw new ArgumentException("Mechanics: ice.length must be a range of 2 m or more.");
            if (beams.length.x < 1f || beams.length.y < beams.length.x)
                throw new ArgumentException("Mechanics: beams.length must be a range of 1 m or more.");

            // Ranges must run low to high.
            Ordered(nameof(onOff) + ".blockRise", onOff.blockRise);
            Ordered(nameof(onOff) + ".padRise", onOff.padRise);
            Ordered(nameof(cannons) + ".rise", cannons.rise);
            Ordered(nameof(mushrooms) + ".landingRise", mushrooms.landingRise);
            Ordered(nameof(beams) + ".rise", beams.rise);
            Ordered(nameof(beams) + ".jink", beams.jink);
            Ordered(nameof(lavaRise) + ".zigzag", lavaRise.zigzag);
            Ordered(nameof(lavaRise) + ".stepRise", lavaRise.stepRise);
            Ordered(nameof(lavaRise) + ".topRise", lavaRise.topRise);
            Ordered(nameof(ice) + ".rise", ice.rise);
            Ordered(nameof(ice) + ".jink", ice.jink);
            Ordered(nameof(moonLeap) + ".rise", moonLeap.rise);
            Ordered(nameof(crumble) + ".riseShare", crumble.riseShare);
            Ordered(nameof(beat) + ".riseShare", beat.riseShare);
            Ordered(nameof(lavaRun) + ".zigzag", lavaRun.zigzag);

            // Times, speeds and sizes a segment divides by or cannot build without.
            Positive(nameof(crumbleMix) + ".fuse", crumbleMix.fuse);
            Positive(nameof(crumble) + ".fuse", crumble.fuse);
            Positive(nameof(crumble) + ".size", crumble.size);
            Positive(nameof(beat) + ".period", beat.period);
            Positive(nameof(beat) + ".size", beat.size);
            Positive(nameof(springs) + ".rise", springs.rise);
            Positive(nameof(springs) + ".secondRise", springs.secondRise);
            Positive(nameof(wallJump) + ".height", wallJump.height);
            Positive(nameof(wallJump) + ".gap", wallJump.gap);
            Positive(nameof(movers) + ".ferrySpeed", movers.ferrySpeed);
            Positive(nameof(movers) + ".sliderSpeed", movers.sliderSpeed);
            Positive(nameof(conveyors) + ".width", conveyors.width);
            Positive(nameof(spinner) + ".speed", spinner.speed);
            Positive(nameof(boost) + ".landingWidth", boost.landingWidth);
            Positive(nameof(ceilingRun) + ".halfWidth", ceilingRun.halfWidth);
            Positive(nameof(gravity) + ".tail", gravity.tail);
            if (moonLeap.gravity.easy <= 0f || moonLeap.gravity.hard <= 0f || moonLeap.gravity.easy > 1f || moonLeap.gravity.hard > 1f)
                throw new ArgumentException("Mechanics: moonLeap.gravity must lie in (0, 1].");
            // Segments that need at least one of something.
            AtLeastOne(nameof(moonLeap) + ".leaps", moonLeap.leaps);
            AtLeastOne(nameof(conveyors) + ".belts", conveyors.belts);
            AtLeastOne(nameof(crumble) + ".blocks", crumble.blocks);
            AtLeastOne(nameof(beat) + ".blocks", beat.blocks);
            AtLeastOne(nameof(movers) + ".sliders", movers.sliders);
            AtLeastOne(nameof(lavaRun) + ".stones", lavaRun.stones);
            AtLeastOne(nameof(wallWalk) + ".obstacles", wallWalk.obstacles);
            AtLeastOne(nameof(ceilingRun) + ".patches", ceilingRun.patches);
            AtLeastOne(nameof(gravityShapes) + ".flipPatches", gravityShapes.flipPatches);
        }

        static void Positive(string name, OB_Span span) {
            if (span.easy <= 0f || span.hard <= 0f) throw new ArgumentException($"Mechanics: {name} must be positive.");
        }

        static void Ordered(string name, Vector2 range) {
            if (range.y < range.x) throw new ArgumentException($"Mechanics: {name} must run from low (x) to high (y).");
        }

        static void AtLeastOne(string name, OB_Span count) {
            if (count.Steps(0f) < 1 || count.Steps(1f) < 1) throw new ArgumentException($"Mechanics: {name} must be at least 1.");
        }
    }
}
