using System;
using UnityEngine;

namespace Universes.UniverseData.obby {
    // Tunables of the original segments (OB_CourseGenerator.Classic.cs) and the gravity segments
    // (OB_CourseGenerator.Gravity.cs). Each value is a design knob with the shipped value as its default;
    // OB_MechanicRules.Validate rejects values that cannot build. Spans go from difficulty 0 to 1.

    /// <summary>Crumbling pieces mixed into other segments (hops, beams, lava stones).</summary>
    [Serializable]
    public sealed class OB_CrumbleMixTuning {
        [Tooltip("Seconds a crumbling piece holds after it is touched (lengthened where crossing it takes longer).")]
        public OB_Span fuse = new(0.8f, 0.3f);
        [Tooltip("Seconds a crumbled piece stays gone.")]
        [Min(0.1f)] public float regrow = 3f;
    }

    /// <summary>Hops: plain jumps with steps up and down and sideways jinks.</summary>
    [Serializable]
    public sealed class OB_HopTuning {
        [Tooltip("Hops before the extras.")]
        [Min(1)] public int hops = 3;
        [Tooltip("A segment gets 0 to this many extra hops at random.")]
        [Min(0)] public int extraHops = 1;
        [Tooltip("Extra hops at the end of a tier (rounded), growing through it.")]
        [Min(0f)] public float hopsOverTier = 2f;
        [Tooltip("Sideways jink either way, metres.")]
        public OB_Span wander = new(1.5f, 4f);
        [Tooltip("Share of the tier's mix chance with which a hop crumbles.")]
        [Range(0f, 1f)] public float crumbleShare = 0.5f;
    }

    /// <summary>Spring Climb: a spring onto a high ledge, later a second spring onto a small one.</summary>
    [Serializable]
    public sealed class OB_SpringTuning {
        [Tooltip("Base platform width and depth, metres.")]
        public Vector2 baseSize = new(6f, 7f);
        [Tooltip("Chance the base platform steps up rather than down.")]
        [Range(0f, 1f)] public float baseUpChance = 0.3f;
        [Tooltip("Spring distance forward of the base platform's centre, metres.")]
        [Min(0f)] public float springForward = 1.5f;
        [Tooltip("A spring throws this far above the ledge it serves, metres.")]
        [Min(0f)] public float overshoot = 2.5f;
        [Tooltip("Ledge (and second ledge) edge past the spring, metres.")]
        [Min(0f)] public float padReach = 2.5f;
        [Tooltip("Height of the first ledge above the base, metres.")]
        public OB_Span rise = new(5f, 9f);
        [Tooltip("Coin height on the first spring's arc, as a share of its rise.")]
        [Range(0f, 1f)] public float coinShare = 0.8f;
        [Tooltip("From this difficulty a second spring follows.")]
        [Range(0f, 1f)] public float secondFromDifficulty = 0.35f;
        [Tooltip("First ledge depth alone, and when a second spring stands on it, metres.")]
        [Min(1f)] public float ledgeDepth = 5f, ledgeDepthWithSecond = 7f;
        public OB_Span ledgeWidth = new(5f, 3.2f);
        [Tooltip("First ledge sideways jink either way, metres (grows by half again at the hardest).")]
        [Min(0f)] public float ledgeJink = 1.5f;
        [Tooltip("Par seconds for each spring.")]
        [Min(0f)] public float firstPar = 2.4f, secondPar = 2.2f;
        [Tooltip("Height of the second ledge above the first, metres.")]
        public OB_Span secondRise = new(4f, 8f);
        public OB_Span secondSize = new(5f, 3f);
        [Tooltip("Second spring distance forward of the first ledge's centre, metres.")]
        [Min(0f)] public float secondSpringForward = 2.5f;
        [Tooltip("Second ledge sideways jink either way, and the furthest it may sit off the path's centre, metres.")]
        [Min(0f)] public float secondJink = 2f, secondMaxOffset = 3f;
    }

    /// <summary>Moon Leap: floaty low-gravity jumps across wide gaps.</summary>
    [Serializable]
    public sealed class OB_MoonLeapTuning {
        [Tooltip("Gravity inside the field, as a share of normal.")]
        public OB_Span gravity = new(0.42f, 0.28f);
        public OB_Span leaps = new(3f, 5f);
        [Tooltip("Platform size as a multiple of the tier's hop size.")]
        [Min(0.1f)] public float sizeScale = 1.2f;
        [Tooltip("Rise range of each leap, metres.")]
        public Vector2 rise = new(-1f, 2.5f);
        [Tooltip("Sideways jink either way, metres.")]
        [Min(0f)] public float jink = 2f;
        [Tooltip("Gap length as a share of a normal hop's (the field's lower gravity makes up the rest).")]
        [Range(0.1f, 1f)] public float tensionScale = 0.85f;
        [Tooltip("Par seconds added per leap (the slow float).")]
        [Min(0f)] public float parPerLeap = 1.2f;
        [Tooltip("Landing size, the field's half width and its headroom above the highest platform, metres.")]
        [Min(1f)] public float landing = 5f, sideHalf = 11f, headroom = 14f;
        [Tooltip("Coin height above the middle platform, metres.")]
        [Min(0f)] public float coinLift = 4f;
    }

    /// <summary>Belt Run: conveyor belts pushing back and sideways.</summary>
    [Serializable]
    public sealed class OB_ConveyorTuning {
        public OB_Span belts = new(3f, 6f);
        public OB_Span width = new(4.6f, 2.2f);
        [Min(1f)] public float length = 5f;
        [Tooltip("Belt speed pushing back, and pushing sideways, m/s.")]
        public OB_Span backSpeed = new(3f, 8f), sideSpeed = new(1.8f, 5f);
        [Min(0.1f)] public float thickness = 0.6f;
        [Tooltip("Par for a belt, as a multiple of walking its length.")]
        [Min(0f)] public float parFactor = 1.6f;
    }

    /// <summary>Crumble Bridge: cracked blocks that drop soon after you land.</summary>
    [Serializable]
    public sealed class OB_CrumbleTuning {
        public OB_Span blocks = new(5f, 10f);
        public OB_Span size = new(2.4f, 1.6f);
        [Tooltip("Rise range of each block, as a share of the tier's highest rise.")]
        public Vector2 riseShare = new(0f, 0.5f);
        [Tooltip("Sideways jink either way, metres.")]
        public OB_Span jink = new(0.5f, 2.5f);
        [Tooltip("Seconds a block holds after it is touched.")]
        public OB_Span fuse = new(0.9f, 0.25f);
        [Min(0.1f)] public float thickness = 0.6f;
    }

    /// <summary>Beat Blocks: two colours of blocks swapping on the beat.</summary>
    [Serializable]
    public sealed class OB_BeatTuning {
        public OB_Span blocks = new(6f, 11f);
        public OB_Span size = new(2.8f, 2f);
        [Tooltip("Seconds per beat cycle.")]
        public OB_Span period = new(4.6f, 2.2f);
        [Tooltip("Below this difficulty every third block is a plain resting platform.")]
        [Range(0f, 1f)] public float restBelowDifficulty = 0.35f;
        [Tooltip("Rise range of each block, as a share of the tier's highest rise.")]
        public Vector2 riseShare = new(-0.2f, 0.6f);
        [Tooltip("Sideways jink either way, metres.")]
        [Min(0f)] public float jink = 1.5f;
        [Tooltip("Gap bias on the tier's tension (negative: shorter).")]
        public float tension = -0.1f;
        [Min(0.1f)] public float thickness = 0.8f;
        [Tooltip("Par per beat block, as a share of the cycle (waiting for the beat).")]
        [Min(0f)] public float parShare = 0.25f;
    }

    /// <summary>Wall Kick Shaft: kick between two walls up to a ledge.</summary>
    [Serializable]
    public sealed class OB_WallJumpTuning {
        [Tooltip("Gap between the walls, metres.")]
        public OB_Span gap = new(4.2f, 3.2f);
        [Tooltip("Climb height, metres.")]
        public OB_Span height = new(6f, 16f);
        [Tooltip("Wall length along the path and thickness, metres.")]
        [Min(1f)] public float length = 5f;
        [Min(0.1f)] public float thickness = 0.6f;
        [Tooltip("Gap from the walls' far end to the ledge, metres.")]
        [Min(0f)] public float ledgeGap = 0.6f;
        [Tooltip("Floor at the bottom: longer than the walls by this, wider than the gap by this, metres.")]
        [Min(0f)] public float floorExtraLength = 2f, floorExtraWidth = 1.6f;
        [Tooltip("Height a kick lifts the robot, metres.")]
        [Min(0.1f)] public float kickHeight = 1.9f;
        [Tooltip("Speed a kick pushes away from the wall, m/s.")]
        public OB_Span awaySpeed = new(6.5f, 7.5f);
        public OB_Span ledgeSize = new(5f, 3.5f);
        [Tooltip("Coin height up the shaft, as a share of its height.")]
        [Range(0f, 1f)] public float coinShare = 0.6f;
        [Tooltip("Par: metres climbed per second, plus seconds.")]
        [Min(0.1f)] public float climbRate = 2.5f;
        [Min(0f)] public float parExtra = 1f;
    }

    /// <summary>Sky Ferry: one platform across a wide gap, or several sliding across the path.</summary>
    [Serializable]
    public sealed class OB_MoverTuning {
        [Tooltip("Chance of the single ferry rather than the sliders.")]
        [Range(0f, 1f)] public float ferryChance = 0.5f;
        [Tooltip("Ferry: gap, platform size, landing width and speed.")]
        public OB_Span ferryGap = new(9f, 20f), ferrySize = new(3.2f, 2.4f), landingWidth = new(6f, 4f), ferrySpeed = new(2.6f, 5.5f);
        [Tooltip("Ferry: room left between it and each end, landing depth and its distance past the gap, metres.")]
        [Min(0f)] public float boarding = 0.3f, landingDepth = 5f, landingBeyond = 2.5f;
        [Tooltip("Sliders: count, platform size, speed and how far either side of the path they slide.")]
        public OB_Span sliders = new(2f, 5f), sliderSize = new(3f, 2.2f), sliderSpeed = new(2.2f, 5.5f), amplitude = new(2.5f, 5.5f);
        [Tooltip("Slider gaps as a share of a normal hop's.")]
        [Range(0.1f, 1f)] public float sliderTensionScale = 0.7f;
        [Min(0.1f)] public float thickness = 0.6f;
    }

    /// <summary>Sweeper: an arena with a spinning bar, a cross later, then two arenas.</summary>
    [Serializable]
    public sealed class OB_SpinnerTuning {
        [Tooltip("Arena size, bar length and the bar's height above the floor, metres.")]
        [Min(2f)] public float arena = 12f, barLength = 11.4f;
        [Min(0f)] public float barHeight = 0.55f;
        [Tooltip("From these difficulties the bar is a cross, and there are two arenas spinning opposite ways.")]
        [Range(0f, 1f)] public float crossFromDifficulty = 0.45f, twoFromDifficulty = 0.75f;
        [Tooltip("Spin, degrees per second.")]
        public OB_Span speed = new(70f, 220f);
        [Tooltip("Seconds between two arms passing a spot, beyond a jump's air time, never less. A player hops each " +
                 "arm, lands and hops the next, so a faster spin (a cross on the hard tiers) is slowed to keep that rhythm.")]
        [Min(0f)] public float rhythmSlack = 0.1f;
        [Min(0f)] public float parPerArena = 1.8f;
        [Tooltip("The gap onto each arena as a share of an entry hop's.")]
        [Range(0.1f, 1f)] public float arenaTensionScale = 0.7f;
    }

    /// <summary>Lava Stones: small stones over a lava lake.</summary>
    [Serializable]
    public sealed class OB_LavaRunTuning {
        public OB_Span stones = new(5f, 11f);
        [Tooltip("Stone size as a share of the tier's hop size, never under the minimum, metres.")]
        [Min(0.1f)] public float sizeShare = 0.8f, minSize = 1.2f;
        [Tooltip("Zigzag either side of the path, metres.")]
        public Vector2 zigzag = new(1f, 3f);
        [Tooltip("Rise either way, metres.")]
        [Min(0f)] public float rise = 0.3f;
        [Tooltip("Share of the tier's mix chance with which a stone crumbles.")]
        [Range(0f, 1f)] public float crumbleShare = 0.6f;
        [Min(0.1f)] public float thickness = 1.2f;
        [Tooltip("The lake: width, depth, and how far under the lowest stone, metres.")]
        [Min(1f)] public float lakeWidth = 12f;
        [Min(0.1f)] public float lakeDepth = 0.4f, lakeBelow = 1.2f;
    }

    /// <summary>Boost Gap: a boost panel flings you across a gap too long to jump.</summary>
    [Serializable]
    public sealed class OB_BoostTuning {
        [Tooltip("Launch speed up and forward, m/s.")]
        [Min(0.1f)] public float up = 9f, forward = 24f;
        [Tooltip("Landing below the runway, runway length and width, and the panel's distance from the runway's end, metres.")]
        [Min(0f)] public float drop = 1.5f, runwayLength = 8f, runwayWidth = 4f, padFromEnd = 3f;
        public OB_Span gap = new(6.5f, 9f), landingWidth = new(8f, 4f);
        [Tooltip("Shortest landing, metres.")]
        [Min(1f)] public float minLanding = 14f;
        [Tooltip("Landing sideways jink either way, metres (doubles at the hardest).")]
        [Min(0f)] public float jink = 1f;
    }

    /// <summary>What every gravity-field segment shares.</summary>
    [Serializable]
    public sealed class OB_GravityTuning {
        [Tooltip("Half width of the approach path, metres.")]
        [Min(1f)] public float pathHalfWidth = 2.5f;
        [Tooltip("Distance from the segment start to the field, metres.")]
        [Min(1f)] public float fieldStart = 3f;
        [Tooltip("Shortest landing out of a field, and the room left to stop after a sprinting fall, metres.")]
        [Min(1f)] public float landingLength = 9f, landingStop = 3f;
        [Tooltip("Wall and ceiling thickness, and how far they reach past the field's end, metres.")]
        [Min(0.1f)] public float thickness = 1f, overrun = 1.5f;
        [Tooltip("Room after landing on the new surface before the first obstacle, and between obstacles beyond a running jump, metres.")]
        [Min(0f)] public float settleRoom = 3f, obstacleRoom = 3f;
        [Tooltip("Field length after the last obstacle, metres.")]
        public OB_Span tail = new(7f, 6f);
        [Tooltip("Par seconds added to walking a field's length.")]
        [Min(0f)] public float parExtra = 1.5f;
    }

    /// <summary>Wall Walk: gravity turns sideways onto a tall wall.</summary>
    [Serializable]
    public sealed class OB_WallWalkTuning {
        [Tooltip("Gap between the approach and the wall, as a share of the robot's height.")]
        [Range(0f, 1f)] public float wallGapShare = 0.35f;
        [Tooltip("Wall bottom relative to the path, metres, and its top (never under the robot's height plus topOverBody).")]
        public float bottom = -1.5f;
        public OB_Span top = new(7f, 8f);
        [Min(0f)] public float topOverBody = 2f;
        public OB_Span obstacles = new(1f, 4f);
        [Tooltip("Chance an obstacle is a gap in the wall rather than a lava strip.")]
        [Range(0f, 1f)] public float gapChance = 0.5f;
        public OB_Span gap = new(2.2f, 4.5f), strip = new(1.4f, 3.5f);
        [Tooltip("Coin distance past a lone obstacle, and below the wall's top, metres.")]
        [Min(0f)] public float coinAfter = 4f, coinBelowTop = 2f;
    }

    /// <summary>Ceiling Run: gravity flips upward onto a ceiling.</summary>
    [Serializable]
    public sealed class OB_CeilingRunTuning {
        public OB_Span halfWidth = new(2.5f, 1.6f);
        [Tooltip("Ceiling height above the robot's head, metres.")]
        public OB_Span headroom = new(2f, 3.5f);
        public OB_Span patches = new(1f, 3f), patch = new(1.4f, 3.5f);
        [Tooltip("Least distance from the field's start to the first patch, metres.")]
        [Min(0f)] public float firstPatchMin = 7f;
        [Tooltip("Landing length and width after the field, metres.")]
        [Min(1f)] public float landingLength = 8f, landingWidth = 6f;
        [Tooltip("Coin distance before the field's end, metres.")]
        [Min(0f)] public float coinBeforeEnd = 1.2f;
    }

    /// <summary>Shapes of the corkscrew and the flip gap that are not difficulty knobs (see OB_CorkscrewTuning, OB_FlipTuning).</summary>
    [Serializable]
    public sealed class OB_GravityShapeTuning {
        [Tooltip("Corkscrew: approach length, and room on a face beyond half the robot's height, metres.")]
        [Min(1f)] public float corkscrewApproach = 6f;
        [Min(0f)] public float corkscrewFaceRoom = 1.6f;
        [Tooltip("Corkscrew: room past the last strip of a section beyond a running jump, metres; par seconds per section turn.")]
        [Min(0f)] public float corkscrewSectionRoom = 2f, corkscrewParPerTurn = 0.8f;
        [Tooltip("Flip gap: runway, landing length, and how far the ceiling reaches back over the runway, metres.")]
        [Min(1f)] public float flipRunway = 7f, flipLanding = 10f;
        [Min(0f)] public float flipCeilingBack = 4f;
        [Tooltip("Flip gap: the gap is this many times a sprint jump, so it cannot be jumped.")]
        [Min(1f)] public float flipForcedGap = 1.15f;
        [Tooltip("Flip gap: lava patches on a ceiling (count grows from easy to hard).")]
        public OB_Span flipPatches = new(1f, 2f);
        [Min(0f)] public float flipParExtra = 1.6f;
        [Tooltip("Flip arrows: trigger depth and height; the mid-air orb's distance past the edge, its size, and its height as a share of a jump.")]
        [Min(0.1f)] public float arrowDepth = 2f, arrowHeight = 2f, orbAhead = 2.5f, orbSize = 3f;
        [Range(0f, 1f)] public float orbJumpShare = 0.55f;
    }
}
