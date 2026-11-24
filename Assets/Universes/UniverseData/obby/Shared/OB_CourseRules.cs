using System;
using UnityEngine;

namespace Universes.UniverseData.obby {
    /// <summary>
    /// How hard one difficulty tier is: how many segments its stages chain, how far and how high its
    /// jumps reach, how big its platforms are, and which mechanics it introduces. Every value is a design
    /// knob; <see cref="OB_CourseRules.Default"/> holds the shipped curve.
    /// </summary>
    [Serializable]
    public struct OB_TierRule {
        [Tooltip("Segments per stage (inclusive range).")]
        public Vector2Int segments;
        [Tooltip("Speed jumps are designed around: 0 = walking, 1 = sprinting (Hard and up expect sprint jumps).")]
        [Range(0f, 1f)] public float reachSpeed;
        [Tooltip("Gaps as a fraction of the jump's reach at that speed (easiest, hardest).")]
        public Vector2 gapFraction;
        [Tooltip("Highest step up, as a fraction of the jump height.")]
        [Range(0.1f, 0.9f)] public float riseFraction;
        [Tooltip("Top size of hop platforms (smallest, largest), metres.")]
        public Vector2 platformSize;
        [Tooltip("Narrowest balance beam, metres.")]
        [Min(0.4f)] public float beamWidth;
        [Tooltip("Chance a segment mixes in a second hazard (crumbling hops, lava below, bumpers).")]
        [Range(0f, 1f)] public float mixChance;
        [Tooltip("A rest platform between segments (the easy tiers breathe between ideas).")]
        public bool restBetweenSegments;
        [Tooltip("Gaps on optional pro routes to stars, as a fraction of reach.")]
        [Range(0.3f, 0.95f)] public float proGapFraction;
        [Tooltip("Mechanics this tier introduces; a tier can use its own and every earlier tier's.")]
        public OB_StageKind[] introduces;
    }

    /// <summary>
    /// The difficulty curve and reward placement of generated courses. Lives on <see cref="OB_Course"/>
    /// in the start scene, so the server and every client generate from the same values.
    /// </summary>
    [Serializable]
    public sealed class OB_CourseRules {
        [Tooltip("One rule per tier, Basic to Extreme.")]
        public OB_TierRule[] tiers = DefaultTiers();
        [Tooltip("Hidden stars per tier (Mario star-coin style), on optional pro routes.")]
        [Range(0, 5)] public int starsPerTier = 3;
        [Tooltip("The tightest gap any jump may ask for, as a fraction of a sprint jump's reach (tests hold every course to it).")]
        [Range(0.5f, 0.95f)] public float physicalGapCeiling = 0.92f;
        [Tooltip("The highest step any jump may ask for, as a fraction of the jump height.")]
        [Range(0.5f, 0.95f)] public float physicalRiseCeiling = 0.85f;
        [Tooltip("Seconds of a stage's par added for each hop: reacting, taking off and landing.")]
        [Min(0f)] public float parPerHop = 0.35f;
        // Appended fields.
        [Tooltip("Share of each landing surface's depth a clean run crosses before its next jump, timed at the tier's reach speed.")]
        [Range(0f, 1f)] public float parRunShare = 0.2f;
        [Tooltip("Share of a jump's real flight time counted in par when it is longer than the gap's run time (floaty and bouncing jumps).")]
        [Range(0f, 1f)] public float parFlightShare = 1f;
        [Tooltip("Shapes of the individual mechanics (counts, sizes, timings, thresholds).")]
        public OB_MechanicRules mechanics = new();
        [Tooltip("A crumbling piece holds at least this many seconds longer than crossing its longest side at the tier's reach speed takes, so it can always be run across.")]
        [Min(0f)] public float crumbleCrossMargin = 0.3f;

        public static OB_CourseRules Default => new();

        /// <summary>
        /// A fingerprint of every rule (FNV-1a over the serialized rules). The server sends it with the
        /// course; a client rebuilds when it changes and reports rules that differ from its own, since
        /// the same seed and stage count only make the same course from the same rules.
        /// </summary>
        public int Hash() {
            string json = JsonUtility.ToJson(this);
            unchecked {
                uint hash = 2166136261u;
                foreach (char c in json) hash = (hash ^ c) * 16777619u;
                return (int)hash;
            }
        }

        public OB_TierRule Tier(OB_Tier tier) => tiers[Mathf.Clamp((int)tier, 0, tiers.Length - 1)];

        /// <summary>Throws when the rules cannot build a course (checked before generating).</summary>
        public void Validate() {
            if (tiers == null || tiers.Length != OB_Tiers.Count)
                throw new ArgumentException($"OB_CourseRules needs exactly {OB_Tiers.Count} tier rules.");
            for (int i = 0; i < tiers.Length; i++) {
                var t = tiers[i];
                if (t.segments.x < 1 || t.segments.y < t.segments.x)
                    throw new ArgumentException($"Tier {i}: segments must be a range starting at 1 or more.");
                if (t.gapFraction.x <= 0f || t.gapFraction.y < t.gapFraction.x)
                    throw new ArgumentException($"Tier {i}: gapFraction must be a positive range.");
                if (t.platformSize.x < 1f || t.platformSize.y < t.platformSize.x)
                    throw new ArgumentException($"Tier {i}: platformSize must be a range of 1 m or more.");
            }
            if (tiers[0].introduces == null || tiers[0].introduces.Length == 0)
                throw new ArgumentException("The Basic tier must introduce at least one mechanic.");
            if (mechanics == null) throw new ArgumentException("OB_CourseRules needs its mechanic rules.");
            mechanics.Validate();
        }

        /// <summary>
        /// The shipped curve. Basic already asks for real jumps (about half a walking jump's reach); from
        /// Hard on, long gaps need a sprint; Extreme asks for up to about 87% of a full sprint jump on
        /// platforms barely wider than the robot, with no rest between segments.
        /// </summary>
        public static OB_TierRule[] DefaultTiers() => new[] {
            new OB_TierRule {
                segments = new Vector2Int(2, 2), reachSpeed = 0f, gapFraction = new Vector2(0.42f, 0.6f), riseFraction = 0.55f,
                platformSize = new Vector2(3f, 3.8f), beamWidth = 1.8f, mixChance = 0f, restBetweenSegments = true, proGapFraction = 0.72f,
                introduces = new[] { OB_StageKind.Hops, OB_StageKind.Springs, OB_StageKind.Conveyors, OB_StageKind.Mushrooms,
                    OB_StageKind.BeamWalk, OB_StageKind.LavaRun, OB_StageKind.Movers }
            },
            new OB_TierRule {
                segments = new Vector2Int(2, 3), reachSpeed = 0.1f, gapFraction = new Vector2(0.5f, 0.68f), riseFraction = 0.62f,
                platformSize = new Vector2(2.6f, 3.3f), beamWidth = 1.5f, mixChance = 0.1f, restBetweenSegments = true, proGapFraction = 0.78f,
                introduces = new[] { OB_StageKind.Crumble, OB_StageKind.Beat, OB_StageKind.WallJump, OB_StageKind.MoonLeap,
                    OB_StageKind.Boost, OB_StageKind.OnOff, OB_StageKind.Cannons, OB_StageKind.Turntables, OB_StageKind.WallWalk }
            },
            new OB_TierRule {
                segments = new Vector2Int(3, 3), reachSpeed = 0.3f, gapFraction = new Vector2(0.55f, 0.74f), riseFraction = 0.68f,
                platformSize = new Vector2(2.2f, 2.8f), beamWidth = 1.2f, mixChance = 0.25f, restBetweenSegments = true, proGapFraction = 0.82f,
                introduces = new[] { OB_StageKind.Spinner, OB_StageKind.Hammers, OB_StageKind.Lasers, OB_StageKind.LogRoll,
                    OB_StageKind.CeilingRun, OB_StageKind.GravityFlip, OB_StageKind.WindGaps }
            },
            new OB_TierRule {
                segments = new Vector2Int(3, 4), reachSpeed = 0.5f, gapFraction = new Vector2(0.6f, 0.8f), riseFraction = 0.72f,
                platformSize = new Vector2(1.8f, 2.4f), beamWidth = 1f, mixChance = 0.45f, restBetweenSegments = false, proGapFraction = 0.86f,
                introduces = new[] { OB_StageKind.Crushers, OB_StageKind.Corkscrew, OB_StageKind.IceRink }
            },
            new OB_TierRule {
                segments = new Vector2Int(4, 4), reachSpeed = 0.65f, gapFraction = new Vector2(0.66f, 0.87f), riseFraction = 0.76f,
                platformSize = new Vector2(1.5f, 2f), beamWidth = 0.8f, mixChance = 0.6f, restBetweenSegments = false, proGapFraction = 0.9f,
                introduces = new[] { OB_StageKind.LavaRise }
            },
            new OB_TierRule {
                segments = new Vector2Int(4, 4), reachSpeed = 0.8f, gapFraction = new Vector2(0.72f, 0.93f), riseFraction = 0.8f,
                platformSize = new Vector2(1.3f, 1.7f), beamWidth = 0.65f, mixChance = 0.75f, restBetweenSegments = false, proGapFraction = 0.96f,
                introduces = Array.Empty<OB_StageKind>()
            },
        };
    }
}
