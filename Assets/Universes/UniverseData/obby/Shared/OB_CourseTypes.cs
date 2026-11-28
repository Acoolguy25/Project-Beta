using System;
using System.Collections.Generic;
using UnityEngine;

namespace Universes.UniverseData.obby {
    /// <summary>
    /// What a generated piece is. Values are stable (keep new kinds at the end). Field meaning per kind
    /// is documented on <see cref="OB_Piece"/>.
    /// </summary>
    public enum OB_PieceKind : byte {
        Platform, CheckpointPad, Finish, Lava, Spring, Boost, GravityZone, Conveyor,
        Crumble, Beat, WallJumpWall, Mover, SpinnerBar, SpinnerCross, Coin,
        // Added by the 2026-11 overhaul.
        Beam, OnOffBlock, OnOffSwitch, Pendulum, Crusher, Laser, RollingLog, Turntable, Cannon, Mushroom,
        GravityFlip, RisingLava, Star,
        // Added in phase B of the overhaul.
        Ice, Wind,
        // Added in round 2 (2026-11-25).
        OutOfBounds, Ghost, Honey, Ring, Swing, Plank,
        // Added in round 4 (2026-11-28).
        PowerBlock
    }

    /// <summary>
    /// A course mechanic. A stage chains several of them (its segments); the stage is named after
    /// its signature segment. Values are stable: keep new kinds at the end.
    /// </summary>
    public enum OB_StageKind : byte {
        Hops, Springs, MoonLeap, Conveyors, Crumble, Beat, WallJump, Movers, Spinner, LavaRun, Boost,
        // Added later: keep new kinds at the end so existing values (sent over the network) stay stable.
        WallWalk, CeilingRun,
        // Added by the 2026-11 overhaul.
        OnOff, Hammers, Crushers, Lasers, LogRoll, Turntables, Cannons, Mushrooms, BeamWalk, Corkscrew,
        GravityFlip, LavaRise,
        // Added in phase B of the overhaul.
        WindGaps, IceRink,
        // Added in round 2 (2026-11-25).
        LiftShaft, GhostPath, HoneySwamp, BoostRings, Swings, RopeBridge
    }

    /// <summary>
    /// One placed course piece.
    /// <para>
    /// Field meaning by kind: Spring/Boost/Cannon/Mushroom <c>vector</c> = pad-space launch velocity
    /// (Mushroom <c>size.x</c> = cap diameter); GravityZone <c>speed</c> = gravity scale, <c>vector</c> =
    /// world gravity direction (zero keeps it: strength only); Conveyor <c>vector</c> = local push
    /// direction, <c>speed</c>; Crumble <c>speed</c> = crumble delay, <c>period</c> = respawn delay; Beat
    /// <c>period</c>, <c>phase</c>, <c>group</c>; WallJumpWall <c>vector.x</c> = away speed, <c>vector.y</c> =
    /// up speed; Mover <c>vector</c> = world travel, <c>speed</c>, <c>phase</c>; SpinnerBar/Cross
    /// <c>speed</c> = signed degrees per second, <c>phase</c>.
    /// </para>
    /// <para>
    /// Overhaul kinds: Beam = a scaled box like a platform. OnOffBlock <c>group</c> 0 = solid from the
    /// start, 1 = solid once its <c>channel</c> is flipped. OnOffSwitch <c>group</c> 0 = floor button
    /// (position on the surface), 1 = "!" block bumped from below (position = block centre),
    /// <c>channel</c>. Pendulum position = pivot, <c>size</c> = (head width, arm length, head depth),
    /// <c>vector.x</c> = amplitude in degrees, <c>period</c>, <c>phase</c>; it swings across the path (about
    /// the stage's forward axis). Crusher position = block centre when raised, <c>size</c> = block,
    /// <c>vector.y</c> = drop, <c>period</c>, <c>phase</c>, <c>speed</c> = hold seconds. Laser = scaled
    /// trigger box, <c>period</c> (0 = always on), <c>speed</c> = on fraction, <c>phase</c>. RollingLog
    /// position = axis centre, <c>size</c> = (diameter, diameter, length) along the stage's forward axis,
    /// <c>speed</c> = signed surface speed. Turntable position = disc top centre, <c>size</c> = (diameter,
    /// thickness, diameter), <c>speed</c> = signed degrees per second, <c>phase</c>, <c>group</c> = posts.
    /// GravityFlip position = trigger centre, <c>size</c> = trigger, <c>vector</c> = world gravity after
    /// the flip, <c>group</c> 0 = pad on a surface, 1 = floating orb. RisingLava = a scaled box at rest
    /// (its top is the lava surface before the chase; its x/z is the chase footprint),
    /// <c>speed</c> = rise speed, <c>period</c> = delay, <c>vector.y</c> = travel. Star like Coin.
    /// Ice = a scaled box like a platform, <c>speed</c> = traction (share of normal grip). Wind = a box
    /// trigger centred on <c>position</c>, <c>vector</c> = world push at the calm point (m/s), <c>speed</c> =
    /// extra push at the height of a gust (m/s), <c>period</c> = gust period, <c>phase</c>.
    /// </para>
    /// <para>
    /// Round-2 kinds: OutOfBounds = a box (<c>position</c> centre, <c>size</c>, <c>yaw</c>) the server never lets a racer's
    /// middle be inside (the far side of a gravity wall or ceiling); it has no collider and no look. Ghost = a scaled
    /// box that blinks solid like a Beat block: <c>period</c>, <c>speed</c> = solid fraction, <c>phase</c>. Honey = a scaled box
    /// like a platform whose sticky field slows walkers to <c>speed</c> (share of their speed). Ring = a trigger box centred
    /// on <c>position</c>, <c>size</c>, that launches whoever enters with the world velocity <c>vector</c>. Swing =
    /// pivot at <c>position</c>, <c>size</c> = (platform width, rope length, platform depth), <c>vector</c> = (amplitude in
    /// degrees, platform thickness, 0), <c>period</c>, <c>phase</c>; it swings along the stage's forward axis. Plank = a scaled box
    /// like Crumble: <c>speed</c> = fuse, <c>period</c> = regrow. Lift Shaft lifts are ordinary Mover pieces travelling up or down.
    /// </para>
    /// <para>
    /// Round-4 kinds: PowerBlock = a "?" block floating over a checkpoint pad, <c>position</c> = block centre, <c>size</c> = the
    /// block, <c>group</c> = the <see cref="OB_PowerUp"/> it gives when bumped from below (once per racer per race).
    /// </para>
    /// </summary>
    [Serializable]
    public struct OB_Piece {
        public OB_PieceKind kind;
        public int stage;
        /// <summary>World position of the piece origin (box centre for scaled boxes, base for pads).</summary>
        public Vector3 position;
        public float yaw;
        /// <summary>Full box size for scaled pieces.</summary>
        public Vector3 size;
        public Vector3 vector;
        public float speed, period, phase;
        public int group;
        // Appended fields.
        /// <summary>Index of the segment inside its stage that placed this piece (-1 for checkpoints, coins, the finish).</summary>
        public int segment;
        /// <summary>On/off switch channel (OnOffBlock, OnOffSwitch); unique per course.</summary>
        public int channel;
    }

    /// <summary>The character movement the course is designed around. Keep it equal to what the server applies.</summary>
    [Serializable]
    public struct OB_Movement {
        public float walkSpeed;
        public float jumpHeight;
        [Tooltip("Gravity magnitude (positive).")]
        public float gravity;
        [Tooltip("Matches CharacterMovement.ExternalAirDamping; used to size boost gaps.")]
        public float airMomentumDamping;
        [Tooltip("Height of the character's collider; gravity stages size their clearances from it.")]
        public float bodyHeight;
        // Appended fields.
        [Tooltip("Dash top speed (walk x 1.9, see CharacterDash). Only pro routes, gold pace and falls are sized for it; required jumps use walkSpeed. OB_Course fills it from its own sprintSpeed.")]
        public float sprintSpeed;
        [Tooltip("Matches CharacterMovement's launch protection time: seconds launch momentum is kept before it fades.")]
        public float launchProtection;
        [Tooltip("Matches CharacterMovement's Slip Acceleration: on a surface of traction t, speed builds at t times this (m/s per second).")]
        public float slipAcceleration;

        public static OB_Movement Default => new() {
            walkSpeed = 10f, jumpHeight = 2.6f, gravity = 30f, airMomentumDamping = 0.6f, bodyHeight = 5.6f,
            sprintSpeed = 19f, launchProtection = 0.2f, slipAcceleration = 60f
        };
        public float JumpSpeed => Mathf.Sqrt(2f * gravity * jumpHeight);

        /// <summary>
        /// Top ground speed a player standing still can reach over <paramref name="runUp"/> metres of a
        /// surface with <paramref name="traction"/> (1 = normal ground, which this treats as no limit).
        /// </summary>
        public float SpeedFromRest(float traction, float runUp) =>
            traction >= 1f ? float.PositiveInfinity : Mathf.Sqrt(2f * Mathf.Max(0f, traction) * slipAcceleration * Mathf.Max(0f, runUp));
        public float AirTime(float gravityScale = 1f) => 2f * JumpSpeed / (gravity * gravityScale);
        public float FlatJumpReach(float gravityScale = 1f) => walkSpeed * AirTime(gravityScale);

        /// <summary>
        /// Seconds from leaving the ground at <paramref name="launchSpeed"/> along up (a jump when 0) to
        /// coming down <paramref name="rise"/> higher; 0 when that height is out of reach.
        /// </summary>
        public float LandTime(float rise, float gravityScale = 1f, float launchSpeed = 0f) {
            float v = launchSpeed > 0f ? launchSpeed : JumpSpeed;
            float g = gravity * Mathf.Max(0.01f, gravityScale);
            float disc = v * v - 2f * g * rise;
            return disc < 0f ? 0f : (v + Mathf.Sqrt(disc)) / g;
        }

        /// <summary>Highest a jump (or a launch at <paramref name="launchSpeed"/>) climbs.</summary>
        public float Apex(float gravityScale = 1f, float launchSpeed = 0f) {
            float v = launchSpeed > 0f ? launchSpeed : JumpSpeed;
            return v * v / (2f * gravity * Mathf.Max(0.01f, gravityScale));
        }

        /// <summary>Distance launch momentum of <paramref name="speed"/> alone carries a player in <paramref name="seconds"/> of flight.</summary>
        public float Carry(float speed, float seconds) {
            float kept = Mathf.Min(seconds, launchProtection);
            float k = Mathf.Max(0.01f, airMomentumDamping);
            return speed * kept + speed * (1f - Mathf.Exp(-k * Mathf.Max(0f, seconds - kept))) / k;
        }

        /// <summary>Copy with unset (older scene) fields filled from <see cref="Default"/>.</summary>
        public OB_Movement Sanitised() {
            var d = Default;
            OB_Movement m = this;
            if (m.bodyHeight <= 0f) m.bodyHeight = d.bodyHeight;
            if (m.sprintSpeed < m.walkSpeed) m.sprintSpeed = m.walkSpeed;
            if (m.launchProtection < 0f) m.launchProtection = 0f;
            if (m.slipAcceleration <= 0f) m.slipAcceleration = d.slipAcceleration;
            return m;
        }
    }

    /// <summary>A required jump between two surfaces, recorded so tests can prove every course is jumpable.</summary>
    public struct OB_JumpCheck {
        public int stage;
        /// <summary>Gap across (edge to edge) and rise, in the player's own frame (on a wall "up" is away from it).</summary>
        public float gap, rise;
        /// <summary>Design limits for this jump (the tier's tightest allowed gap and rise).</summary>
        public float maxGap, maxRise;
        // Appended fields.
        public int segment;
        /// <summary>Part of an optional pro route to a star (a harder limit applies).</summary>
        public bool pro;
        public float gravityScale;
        /// <summary>Speed (m/s) the design expects across this jump: walk in the easy tiers, toward sprint later.</summary>
        public float speed;
        /// <summary>Launch speed along up for a bounce (mushroom); 0 for an ordinary jump.</summary>
        public float launchSpeed;
        /// <summary>
        /// Air steering instead of a jump: the player must move <see cref="gap"/> sideways during a launch
        /// that lasts <see cref="airTime"/> (a cannon onto an off-centre target). 0 for a jump.
        /// </summary>
        public float airTime;
    }

    public sealed class OB_Stage {
        public int index;
        /// <summary>Signature segment, the one the stage is named after.</summary>
        public OB_StageKind kind;
        /// <summary>Difficulty tier (and theme) of this stage; see <see cref="OB_Tiers"/>.</summary>
        public OB_Tier tier;
        /// <summary>0..1 intensity used to size the stage. Rises through each tier, easing off at the next.</summary>
        public float difficulty;
        /// <summary>Top centre of the checkpoint a player respawns on while this stage is next.</summary>
        public Vector3 spawn;
        /// <summary>Volume that awards this checkpoint. Unused for stage 0 (the lobby).</summary>
        public Bounds checkpoint;
        /// <summary>Below this height a player on this stage has fallen and goes back to <see cref="spawn"/>.</summary>
        public float killY;
        public Vector3 coin;
        // Appended fields.
        /// <summary>The segments in course order (the stage plays them one after another).</summary>
        public OB_StageKind[] segments = Array.Empty<OB_StageKind>();
        /// <summary>Whether a star waits on this stage's pro route, and where.</summary>
        public bool hasStar;
        public Vector3 star;
        /// <summary>Above this height a player has fallen "up" (after a gravity flip). Infinity on stages without flips.</summary>
        public float killTop = float.PositiveInfinity;
        /// <summary>Estimated seconds a clean, confident run of this stage takes (medals and the gold pace scale from it).</summary>
        public float par;
        /// <summary>Segment (index into <see cref="segments"/>) the coin belongs to.</summary>
        public int coinSegment;
        /// <summary>World box around every piece of the stage (its pad, route, pro route and pickups).</summary>
        public Bounds region;
    }

    public sealed class OB_CourseLayout {
        public int seed;
        public OB_Movement movement;
        public readonly List<OB_Piece> pieces = new();
        public readonly List<OB_JumpCheck> jumpChecks = new();
        public OB_Stage[] stages;
        public Bounds finish;
        public Vector3 finishTop;
        /// <summary>Highest point any stage reaches, plus <see cref="OB_CourseGenerator.KillDepth"/>: above it anyone has fallen up.</summary>
        public float ceilingY = float.NegativeInfinity;
        public int StageCount => stages.Length;

        /// <summary>Sideways room around a stage's pieces that still counts as being over that stage, metres.</summary>
        public const float RegionMargin = 4f;

        /// <summary>Whether <paramref name="position"/> is over stage <paramref name="stage"/> (its region, seen from above, grown by <see cref="RegionMargin"/>).</summary>
        public bool IsOver(int stage, Vector3 position) {
            Bounds r = stages[stage].region;
            return position.x >= r.min.x - RegionMargin && position.x <= r.max.x + RegionMargin
                && position.z >= r.min.z - RegionMargin && position.z <= r.max.z + RegionMargin;
        }

        /// <summary>
        /// Fall limits for a player whose furthest checkpoint is stage <paramref name="reached"/>, judged by
        /// where they physically are: the most lenient heights of the reached stages they are over (so going
        /// back for a missed star is safe on any earlier stage's ground), or their own stage's heights when
        /// they are over none. Stages not reached yet never count, so this opens no way past a checkpoint.
        /// </summary>
        public void KillHeights(int reached, Vector3 position, out float killY, out float killTop) {
            int own = Mathf.Clamp(reached, 0, stages.Length - 1);
            killY = float.PositiveInfinity;
            killTop = float.NegativeInfinity;
            for (int s = 0; s <= own; s++) {
                if (!IsOver(s, position)) continue;
                killY = Mathf.Min(killY, stages[s].killY);
                killTop = Mathf.Max(killTop, stages[s].killTop);
            }
            if (float.IsPositiveInfinity(killY)) {
                killY = stages[own].killY;
                killTop = stages[own].killTop;
            }
            killTop = Mathf.Min(killTop, ceilingY);
        }

        /// <summary>
        /// Stage whose pickups a player at <paramref name="position"/> can reach: the latest reached stage
        /// they are over, or <paramref name="reached"/> when they are over none.
        /// </summary>
        public int StageAt(int reached, Vector3 position) {
            int own = Mathf.Clamp(reached, 0, stages.Length - 1);
            for (int s = own; s >= 0; s--)
                if (IsOver(s, position)) return s;
            return own;
        }

        /// <summary>Sum of the stage pars before stage <paramref name="stage"/> (the clean-run time to reach its checkpoint).</summary>
        public float ParBefore(int stage) {
            float total = 0f;
            for (int i = 0; i < stage && i < stages.Length; i++) total += stages[i].par;
            return total;
        }

        public float TotalPar => ParBefore(stages.Length);

        public int StarCount {
            get {
                int count = 0;
                foreach (var s in stages) if (s.hasStar) count++;
                return count;
            }
        }

        /// <summary>Stars on the stages of <paramref name="tier"/>.</summary>
        public int StarsInTier(OB_Tier tier) {
            int count = 0;
            foreach (var s in stages) if (s.hasStar && s.tier == tier) count++;
            return count;
        }
    }
}
