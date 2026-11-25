using System;
using UnityEngine;

namespace Universes.UniverseData.obby {
    // Tunables of the round-2 mechanics (Lift Shaft, Ghost Path, Honey Swamp, Boost Rings, Swings, Rope Bridge).
    // Same convention as OB_MechanicRules.cs: an OB_Span runs from the gentlest value at difficulty 0 to the
    // hardest at 1, and every shipped value is the field's default.

    /// <summary>Lift Shaft: platforms that ride up and down a shaft between ledges.</summary>
    [Serializable]
    public sealed class OB_LiftTuning {
        [Tooltip("Lifts in a row (they alternate up and down).")]
        public OB_Span lifts = new(1f, 3f);
        [Tooltip("Metres a lift travels, and its speed (m/s).")]
        public OB_Span travel = new(5f, 12f), speed = new(2.2f, 3.6f);
        [Tooltip("Lift platform size.")]
        public OB_Span size = new(3.6f, 2.6f);
        [Tooltip("Gap between the ledge and the lift at its stop: stepping on, and stepping off at the other end, metres.")]
        public OB_Span boardGap = new(0.6f, 2f), exitGap = new(0.8f, 2.4f);
        [Tooltip("Ledge a lift delivers you to: width and depth.")]
        public Vector2 ledge = new(5f, 5f);
        [Min(0.1f)] public float thickness = 0.6f;
        [Tooltip("Par per lift as a share of its ride (waiting for it at worst).")]
        [Min(0f)] public float parShare = 1f;
    }

    /// <summary>Ghost Path: platforms that blink in one after another along the path.</summary>
    [Serializable]
    public sealed class OB_GhostTuning {
        public OB_Span blocks = new(5f, 9f);
        public OB_Span size = new(2.8f, 1.9f);
        [Tooltip("Seconds each ghost stays solid, and the seconds of overlap with the one before it (the window to jump across).")]
        public OB_Span window = new(2.8f, 2.1f), overlap = new(1.5f, 1.0f);
        [Tooltip("Seconds from one appearance of a ghost to the next.")]
        public OB_Span period = new(5f, 4f);
        [Tooltip("Rise range of each ghost, as a share of the tier's highest rise.")]
        public Vector2 riseShare = new(-0.2f, 0.5f);
        [Min(0f)] public float jink = 1.5f;
        [Tooltip("Gap bias on the tier's tension (negative: shorter).")]
        public float tension = -0.15f;
        [Min(0.1f)] public float thickness = 0.8f;
        [Tooltip("Par per ghost as a share of the stride between appearances (waiting for the next one).")]
        [Min(0f)] public float parShare = 0.5f;
    }

    /// <summary>Honey Swamp: sticky sheets that slow you, each followed by firm ground to take off from.</summary>
    [Serializable]
    public sealed class OB_HoneyTuning {
        public OB_Span sheets = new(2f, 4f);
        public OB_Span width = new(5f, 3.2f), length = new(6f, 11f);
        [Tooltip("Share of its normal ground speed a walker keeps on honey (smaller = stickier).")]
        public OB_Span keep = new(0.6f, 0.42f);
        [Tooltip("Rise range of each sheet, as a share of the tier's highest rise.")]
        public Vector2 riseShare = new(-0.2f, 0.3f);
        [Min(0f)] public float jink = 1.5f;
        public float tension = -0.1f;
        [Min(0.1f)] public float thickness = 0.8f;
        [Tooltip("Firm pad after each sheet: its depth is the run-up to the next jump, metres, and its width.")]
        [Min(2f)] public float recoveryRun = 4f, recoveryWidth = 4f;
        [Tooltip("How far above a sheet the sticky field reaches, metres.")]
        [Min(0.5f)] public float reach = 1.8f;
        [Tooltip("Par per sheet as a share of the extra time its length takes at the slower pace.")]
        [Min(0f)] public float parShare = 1f;
    }

    /// <summary>Boost Rings: hoops that fire you along a flight path over a gap too wide to jump.</summary>
    [Serializable]
    public sealed class OB_RingTuning {
        public OB_Span rings = new(2f, 4f);
        [Tooltip("Forward launch speed out of a ring (m/s), and seconds of flight to the next ring.")]
        public OB_Span launch = new(14f, 17f);
        public Vector2 flight = new(1.0f, 1.35f);
        [Tooltip("Height change between rings, metres (a ring on the way down is relaunched; one on the way up is not).")]
        public Vector2 rise = new(-2.5f, 2f);
        [Tooltip("Sideways drift between rings, metres.")]
        public OB_Span lateral = new(1f, 4f);
        [Tooltip("Ring diameter, and its depth along the flight, metres.")]
        public OB_Span size = new(7.5f, 5.5f);
        [Min(0.5f)] public float depth = 1.2f;
        [Tooltip("Metres the ring's trigger reaches above its drawn hoop. A player holding forward through a flight adds walk speed to the launch and reaches the next ring sooner, higher up the arc; the funnel above the hoop still fires it.")]
        [Min(0f)] public float catchAbove = 9f;
        [Tooltip("Share of walking input kept while flying between hoops (holding forward otherwise adds walk speed to the launch). Steering checks use it.")]
        [Range(0.05f, 1f)] public float airControl = 0.5f;
        [Tooltip("Seconds past the planned flight that input stays limited (the last ring's flight ends on the island).")]
        [Min(0f)] public float flightMargin = 0.4f;
        [Tooltip("First ring: metres past the platform's edge, and height of its middle above the platform.")]
        [Min(0f)] public float firstAhead = 0.8f, firstHeight = 1.2f;
        [Tooltip("The island after the last ring: depth along the flight and width, metres (flights vary a little with where a ring is entered).")]
        public OB_Span islandDepth = new(11f, 8f), islandWidth = new(8f, 6f);
        [Tooltip("Height of the island top relative to the last ring's middle, metres.")]
        public Vector2 islandRise = new(-4f, -2f);
        [Tooltip("Par per ring: the flight itself is counted from its seconds; this is the reaction at each hoop.")]
        [Min(0f)] public float parPerRing = 0.4f;
    }

    /// <summary>Swings: trapeze platforms that swing across a gap between two ledges.</summary>
    [Serializable]
    public sealed class OB_SwingTuning {
        public OB_Span swings = new(1f, 3f);
        [Tooltip("Rope length (pivot to platform top), metres.")]
        public OB_Span rope = new(7f, 9f);
        [Tooltip("Swing either side of hanging straight down, degrees.")]
        public OB_Span amplitude = new(32f, 42f);
        [Tooltip("Seconds per full swing.")]
        public OB_Span period = new(4.4f, 3.2f);
        [Tooltip("Platform size: across the path and along it.")]
        public OB_Span width = new(4.2f, 3f), depth = new(3.6f, 2.8f);
        [Tooltip("Gap from the ledge to the platform at the end of its swing nearest to it, boarding and leaving, metres.")]
        public OB_Span boardGap = new(1f, 2.6f), exitGap = new(1.2f, 3f);
        [Tooltip("Ledge between swings: width and depth.")]
        public Vector2 ledge = new(5f, 5f);
        [Min(0.1f)] public float thickness = 0.6f;
        [Tooltip("Par per swing as a share of a full swing (waiting for it).")]
        [Min(0f)] public float parShare = 0.6f;
    }

    /// <summary>Rope Bridge: planks that fall away soon after you step on them.</summary>
    [Serializable]
    public sealed class OB_RopeTuning {
        public OB_Span planks = new(8f, 14f);
        [Tooltip("Plank width across the bridge and depth along it, metres.")]
        public OB_Span width = new(4.2f, 2.4f), depth = new(1.8f, 1.4f);
        [Tooltip("Gap between planks, metres (each is a recorded jump).")]
        public OB_Span gap = new(0.3f, 1.4f);
        [Tooltip("Seconds a plank holds once stepped on (never under the time to run across it) and before it regrows.")]
        public OB_Span fuse = new(1.4f, 0.8f);
        [Min(0.1f)] public float regrow = 3f;
        [Tooltip("Sideways sway and height change between planks, metres.")]
        [Min(0f)] public float sway = 0.7f, bob = 0.25f;
        [Min(0.1f)] public float thickness = 0.35f;
        [Tooltip("Par per plank is the run across it at walk speed times this (hurrying keeps ahead of the fall).")]
        [Min(0f)] public float parFactor = 1f;
    }
}
