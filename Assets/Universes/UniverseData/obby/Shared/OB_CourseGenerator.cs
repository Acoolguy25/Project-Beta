using System;
using System.Collections.Generic;
using RyanAssets.Core;
using UnityEngine;

namespace Universes.UniverseData.obby {
    /// <summary>
    /// Deterministic course generator. The same seed, stage count, movement, rules and start always give
    /// the same pieces on every peer, so the server sends only the seed.
    /// <para>
    /// A course is split into <see cref="OB_Tiers"/> (Basic to Extreme). Each stage is a checkpoint pad
    /// followed by a chain of <em>segments</em>: small mechanic modules (a spring climb, a wall walk, a
    /// hammer bridge...) that each start at the far edge of the surface the player stands on and end on
    /// a surface the next one starts from, so a stage reads as a little level. The tier rule
    /// (<see cref="OB_CourseRules"/>) sets how many segments a stage chains, which mechanics it may use,
    /// how far and how high its jumps reach and how big its platforms are; difficulty climbs through each
    /// tier and eases off at the start of the next.
    /// </para>
    /// <para>
    /// Every plain jump is recorded with <see cref="CheckJump"/> against the tier's limits, and tests hold
    /// every recorded jump to a physical ceiling, so the hardest stages demand precision, never luck.
    /// Segment code works in a local frame: x across, y up, z along the path; z = 0 is the far edge of the
    /// surface the segment starts from, whose top is at y = 0.
    /// </para>
    /// </summary>
    public sealed partial class OB_CourseGenerator {
        public const float PadSize = 6f;
        public const float FinishSize = 10f;
        public const float KillDepth = 12f;
        public const int MaxStages = 60;
        /// <summary>
        /// The shipped course length: four stages per tier. Long enough to see every world and every
        /// mechanic, short enough to finish well inside the race timer: par (a clean WALKING run) is about 605 to
        /// 675 s (seeds 1 to 100) against the 1500 s race, so the race lasts about 2.2 times par at the least, and
        /// bronze (1.6x par) always fits (tests hold bronze inside the race on this length).
        /// </summary>
        public const int DefaultStageCount = 24;
        /// <summary>Height of a coin above the surface it is collected from.</summary>
        public const float CoinLift = 1.6f;
        /// <summary>Height of a star above the pro-route platform it waits on.</summary>
        public const float StarLift = 2.2f;
        /// <summary>Size of the rest platform between segments in the gentler tiers.</summary>
        const float RestSize = 4.5f;
        /// <summary>Size of the perch a stage adds when its star route found no room elsewhere.</summary>
        const float StarPerchSize = 7f;
        /// <summary>How far hops wander sideways from the stage's centre line.</summary>
        const float PathHalfWidth = 5f;
        /// <summary>Segments never place solid pieces further back than this behind their start edge.</summary>
        public const float SegmentBackReach = 2f;

        readonly SeededStoryRandom rng;
        readonly OB_CourseLayout layout;
        readonly OB_Movement m;
        readonly OB_CourseRules rules;
        Vector3 origin;
        float yaw;
        Quaternion rotation;
        int stage, segment, nextChannel;
        OB_TierRule tier;
        /// <summary>0..1 position of the current stage inside its tier.</summary>
        float tierProgress;
        float minTop, maxCeiling, par;
        bool flips;
        readonly List<Vector3> coinCandidates = new();
        readonly List<int> coinCandidateSegments = new();
        Vector3 lastTop;
        Vector2 lastSize;
        /// <summary>Upper limit on <see cref="ReachSpeed"/> while a segment builds jumps from a slow surface (ice, honey).</summary>
        float reachCap = float.PositiveInfinity;
        /// <summary>True while a pro route is being built: its jumps are designed for a dash (see <see cref="OB_CourseRules.proDashShare"/>).</summary>
        bool proRoute;
        /// <summary>Per-mechanic shapes.</summary>
        OB_MechanicRules Tune => rules.mechanics;

        OB_CourseGenerator(int seed, OB_Movement movement, OB_CourseRules courseRules) {
            rng = new SeededStoryRandom(seed);
            m = movement;
            rules = courseRules;
            layout = new OB_CourseLayout { seed = seed, movement = movement };
        }

        public static OB_CourseLayout Generate(int seed, int stageCount, OB_Movement movement, Vector3 start, float startYaw, Vector3 lobbySpawn) =>
            Generate(seed, stageCount, movement, start, startYaw, lobbySpawn, OB_CourseRules.Default);

        public static OB_CourseLayout Generate(int seed, int stageCount, OB_Movement movement, Vector3 start, float startYaw,
            Vector3 lobbySpawn, OB_CourseRules rules) {
            if (movement.gravity <= 0f || movement.jumpHeight <= 0f || movement.walkSpeed <= 0f)
                throw new ArgumentException("Course movement needs positive gravity, jump height and walk speed.", nameof(movement));
            if (rules == null) throw new ArgumentNullException(nameof(rules));
            rules.Validate();
            var generator = new OB_CourseGenerator(seed, movement.Sanitised(), rules);
            return generator.Build(Mathf.Clamp(stageCount, 1, MaxStages), start, startYaw, lobbySpawn);
        }

        // ---------------------------------------------------------------- randomness
        float Value() => rng.Next(1 << 24) / (float)(1 << 24);
        float Range(float min, float max) => min + (max - min) * Value();
        bool Chance(float probability) => Value() < probability;
        float Sign() => Chance(0.5f) ? 1f : -1f;
        static float Lerp(float a, float b, float t) => a + (b - a) * Mathf.Clamp01(t);

        // ---------------------------------------------------------------- frame helpers
        Vector3 World(Vector3 local) => origin + rotation * local;
        Vector3 World(float x, float y, float z) => World(new Vector3(x, y, z));
        Vector3 WorldDirection(Vector3 local) => rotation * local;

        int Add(OB_Piece piece) {
            piece.stage = stage;
            piece.segment = segment;
            layout.pieces.Add(piece);
            return layout.pieces.Count - 1;
        }

        /// <summary>Changes a placed piece in place.</summary>
        void Edit(int index, Func<OB_Piece, OB_Piece> change) => layout.pieces[index] = change(layout.pieces[index]);

        /// <summary>Adds a box whose top face is centred on <paramref name="localTop"/>. Platform boxes that are not standable (gravity walls and ceilings) carry <c>group</c> 1.</summary>
        int Box(OB_PieceKind kind, Vector3 localTop, Vector3 size, bool standable = true) {
            if (standable) minTop = Mathf.Min(minTop, World(localTop).y);
            return Add(new OB_Piece {
                kind = kind, position = World(localTop - Vector3.up * (size.y * 0.5f)), yaw = yaw, size = size,
                // A platform that is not a floor is a gravity wall or ceiling: only its inner face is meant to be stood on.
                group = !standable && kind == OB_PieceKind.Platform ? 1 : 0
            });
        }

        int Platform(Vector3 localTop, Vector3 size) => Box(OB_PieceKind.Platform, localTop, size);

        /// <summary>Adds a non-box piece at a local point.</summary>
        int Place(OB_PieceKind kind, Vector3 local, Vector3 size = default) =>
            Add(new OB_Piece { kind = kind, position = World(local), yaw = yaw, size = size });

        /// <summary>Records a plain jump from the last surface to this one and makes it the last surface.</summary>
        void Step(Vector3 top, Vector2 size, float gravityScale = 1f, bool pro = false) {
            CheckJump(EdgeGap(top, size), top.y - lastTop.y, gravityScale, pro);
            Land(top, size);
        }

        /// <summary>Edge-to-edge distance across from the last surface to a surface at <paramref name="top"/>.</summary>
        float EdgeGap(Vector3 top, Vector2 size) {
            float dx = Mathf.Max(0f, Mathf.Abs(top.x - lastTop.x) - (size.x + lastSize.x) * 0.5f);
            float dz = Mathf.Max(0f, Mathf.Abs(top.z - lastTop.z) - (size.y + lastSize.y) * 0.5f);
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        /// <summary>
        /// Records a jump of <paramref name="gap"/> across and <paramref name="rise"/> up, measured in the
        /// player's own frame (on a wall or ceiling "up" is away from that surface), against this tier's limits.
        /// </summary>
        void CheckJump(float gap, float rise, float gravityScale = 1f, bool pro = false, float launchSpeed = 0f) {
            float fraction = pro ? tier.proGapFraction : tier.gapFraction.y;
            layout.jumpChecks.Add(new OB_JumpCheck {
                stage = stage, segment = segment, gap = gap, rise = rise, pro = pro,
                gravityScale = gravityScale, speed = ReachSpeed, launchSpeed = launchSpeed,
                maxGap = Reach(rise, gravityScale, launchSpeed) * fraction + 0.001f,
                maxRise = launchSpeed > 0f ? m.Apex(gravityScale, launchSpeed) * tier.riseFraction : MaxRise(gravityScale)
            });
        }

        /// <summary>Records mid-air steering: <paramref name="offset"/> metres sideways during <paramref name="airTime"/> seconds of flight.</summary>
        void CheckSteer(float offset, float airTime, float share = 1f) {
            layout.jumpChecks.Add(new OB_JumpCheck {
                stage = stage, segment = segment, gap = offset, airTime = airTime, gravityScale = 1f, speed = m.walkSpeed * share,
                maxGap = m.walkSpeed * share * airTime * tier.gapFraction.y + 0.001f, maxRise = float.PositiveInfinity
            });
        }

        /// <summary>
        /// Adds a gravity field spanning the local box <paramref name="min"/>..<paramref name="max"/>.
        /// <paramref name="localDown"/> turns gravity (zero keeps its direction); <paramref name="scale"/>
        /// multiplies its strength.
        /// </summary>
        void Field(Vector3 min, Vector3 max, Vector3 localDown, float scale = 1f) {
            Add(new OB_Piece {
                kind = OB_PieceKind.GravityZone, yaw = yaw, speed = scale,
                position = World((min + max) * 0.5f), size = max - min,
                vector = localDown == Vector3.zero ? Vector3.zero : WorldDirection(localDown.normalized)
            });
        }

        /// <summary>Makes a surface reached some other way (spring, ride, wall jump) the last surface.</summary>
        void Land(Vector3 top, Vector2 size) {
            lastTop = top;
            lastSize = size;
        }

        /// <summary>Offers a coin spot (local); the stage picks one of its segments' offers.</summary>
        void Coin(Vector3 local) {
            coinCandidates.Add(World(local));
            coinCandidateSegments.Add(segment);
        }

        /// <summary>Adds clean-run seconds to the stage's par.</summary>
        void Par(float seconds) => par += Mathf.Max(0f, seconds);

        /// <summary>A new on/off switch channel, unique on the course.</summary>
        int NewChannel() => ++nextChannel;

        /// <summary>
        /// Moves the local frame so z = 0 is the far edge of the last surface and its top is at y = 0,
        /// centred on x = 0: every segment starts from there.
        /// </summary>
        void Rebase() {
            origin = World(new Vector3(lastTop.x, lastTop.y, lastTop.z + lastSize.y * 0.5f));
            lastTop = new Vector3(0f, 0f, -lastSize.y * 0.5f);
        }

        // ---------------------------------------------------------------- jump sizing
        /// <summary>
        /// Speed jumps are designed around: always WALK speed (the dash is an optional skill tool), except on
        /// pro routes to stars, which are designed for a dash. Capped while jumps leave a surface the player
        /// cannot reach that speed on (ice, honey).
        /// </summary>
        float ReachSpeed => Mathf.Min(proRoute ? Lerp(m.walkSpeed, m.sprintSpeed, rules.proDashShare) : m.walkSpeed, reachCap);

        /// <summary>How far across a jump at <see cref="ReachSpeed"/> carries when it lands <paramref name="rise"/> higher.</summary>
        float Reach(float rise, float gravityScale = 1f, float launchSpeed = 0f) => ReachSpeed * m.LandTime(rise, gravityScale, launchSpeed);

        /// <summary>The tier's highest step up.</summary>
        float MaxRise(float gravityScale = 1f) => m.jumpHeight * tier.riseFraction / gravityScale;

        /// <summary>A gap tension for this tier: later stages in a tier and a little randomness pull it toward the tier's hardest.</summary>
        float Tension(float bias = 0f) => Lerp(tier.gapFraction.x, tier.gapFraction.y, tierProgress * 0.6f + Value() * 0.4f + bias);

        /// <summary>A hop platform size for this tier (smaller later in the tier).</summary>
        float HopSize() => Lerp(tier.platformSize.y, tier.platformSize.x, tierProgress * 0.5f + Value() * 0.5f);

        /// <summary>
        /// Where the next hop's top goes: <paramref name="tension"/> of the reach from the last surface's
        /// edge, shifted <paramref name="lateral"/> sideways and <paramref name="rise"/> up.
        /// </summary>
        Vector3 NextHop(Vector2 size, float rise, float lateral, float tension, float gravityScale = 1f, float launchSpeed = 0f) {
            float gap = Mathf.Max(0.5f, Reach(rise, gravityScale, launchSpeed) * tension);
            float x = Mathf.Clamp(lastTop.x + lateral, -PathHalfWidth, PathHalfWidth);
            float halfWidths = (size.x + lastSize.x) * 0.5f;
            float dx = Mathf.Max(0f, Mathf.Abs(x - lastTop.x) - halfWidths);
            if (dx > gap * 0.75f) {
                // Too far sideways for the gap: keep the shift, but within the gap.
                dx = gap * 0.75f;
                x = lastTop.x + Mathf.Sign(x - lastTop.x) * (halfWidths + dx);
            }
            float dz = Mathf.Sqrt(Mathf.Max(0f, gap * gap - dx * dx));
            return new Vector3(x, lastTop.y + rise, lastTop.z + lastSize.y * 0.5f + dz + size.y * 0.5f);
        }

        /// <summary>Places a hop (a box of <paramref name="kind"/>), records the jump onto it and returns its piece index.</summary>
        int Hop(Vector2 size, float rise, float lateral, float tension, OB_PieceKind kind = OB_PieceKind.Platform, float height = 1f,
            float gravityScale = 1f) {
            Vector3 top = NextHop(size, rise, lateral, tension, gravityScale);
            int index = Box(kind, top, new Vector3(size.x, height, size.y));
            Par(HopPar(EdgeGap(top, size), rise, size.y, gravityScale));
            Step(top, size, gravityScale);
            return index;
        }

        /// <summary>
        /// Clean-run seconds for one hop: reacting and landing, the jump itself (the run across the gap, or
        /// the real flight when that is longer, as in low gravity) and running over the landing surface.
        /// </summary>
        float HopPar(float gap, float rise, float depth, float gravityScale = 1f, float launchSpeed = 0f) {
            float speed = Mathf.Max(0.1f, ReachSpeed);
            float flight = m.LandTime(Mathf.Min(rise, m.Apex(gravityScale, launchSpeed) * 0.95f), gravityScale, launchSpeed) * rules.parFlightShare;
            return rules.parPerHop + Mathf.Max(gap / speed, flight) + depth * rules.parRunShare / speed;
        }

        /// <summary>A rise for a hop: from a small drop up to the tier's highest step.</summary>
        float HopRise(float upChance = 0.6f) => Chance(upChance) ? Range(0.2f, 1f) * MaxRise() : -Range(0.2f, 1.6f);

        // ---------------------------------------------------------------- course
        OB_CourseLayout Build(int stageCount, Vector3 start, float startYaw, Vector3 lobbySpawn) {
            layout.stages = new OB_Stage[stageCount];
            origin = start;
            yaw = startYaw;
            OB_StageKind[][] plan = PlanSegments(stageCount);
            var stars = new StarPlan(rules.starsPerTier, stageCount);
            for (stage = 0; stage < stageCount; stage++) {
                int firstPiece = layout.pieces.Count;
                rotation = Quaternion.Euler(0f, yaw, 0f);
                var info = new OB_Stage {
                    index = stage, segments = plan[stage], kind = Signature(plan[stage]),
                    tier = OB_Tiers.TierOf(stage, stageCount),
                    difficulty = OB_Tiers.Difficulty(stage, stageCount)
                };
                tier = rules.Tier(info.tier);
                tierProgress = OB_Tiers.ProgressInTier(stage, stageCount);
                minTop = float.PositiveInfinity;
                maxCeiling = float.NegativeInfinity;
                flips = false;
                par = 0f;
                coinCandidates.Clear();
                coinCandidateSegments.Clear();
                segment = -1;
                if (stage == 0) {
                    info.spawn = lobbySpawn;
                    minTop = lobbySpawn.y;
                } else {
                    Vector3 padTop = new(0f, 0f, PadSize * 0.5f);
                    Box(OB_PieceKind.CheckpointPad, padTop, new Vector3(PadSize, 1f, PadSize));
                    info.spawn = World(padTop);
                    info.checkpoint = new Bounds(info.spawn + Vector3.up * 2f, new Vector3(PadSize, 4f, PadSize));
                    origin = World(0f, 0f, PadSize);
                }
                // Every stage starts at the far edge of the surface the player is standing on.
                Land(new Vector3(0f, 0f, -PadSize * 0.5f), new Vector2(PadSize, PadSize));

                bool wantStar = stars.Wants(info.tier, stage, rng);
                int starAfter = wantStar ? rng.Next(plan[stage].Length) : -1;
                for (int i = 0; i < plan[stage].Length; i++) {
                    segment = i;
                    Rebase();
                    // A tier's new mechanic is met first in a gentler form.
                    bool intro = IsIntroduction(plan[stage][i], info.tier) && stars.IsFirstStageOfTier(stage);
                    BuildSegment(plan[stage][i], Mathf.Max(0f, info.difficulty - (intro ? 0.1f : 0f)));
                    if (!info.hasStar && wantStar && i >= starAfter && TryProRoute(out Vector3 star)) {
                        info.hasStar = true;
                        info.star = star;
                        stars.Placed(info.tier);
                    }
                    if (i < plan[stage].Length - 1 && tier.restBetweenSegments) Rest();
                }
                // No segment left room for the pro route: it branches off a perch before the exit.
                if (wantStar && !info.hasStar) {
                    Rebase();
                    Hop(new Vector2(StarPerchSize, StarPerchSize), 0f, 0f, Tension(Tune.restTension));
                    if (TryProRoute(out Vector3 star)) {
                        info.hasStar = true;
                        info.star = star;
                        stars.Placed(info.tier);
                    }
                }
                segment = -1;
                Vector3 exit = Exit(Tension(Tune.entryTension) * Reach(0f));
                if (coinCandidates.Count == 0) Coin(new Vector3(exit.x, CoinLift, exit.z - 1f));
                int pick = rng.Next(coinCandidates.Count);
                info.coin = coinCandidates[pick];
                info.coinSegment = coinCandidateSegments[pick];
                info.killY = minTop - KillDepth;
                info.par = par;
                layout.stages[stage] = info;
                Add(new OB_Piece { kind = OB_PieceKind.Coin, position = info.coin, yaw = yaw, size = Vector3.one, segment = -1 });
                if (info.hasStar) Add(new OB_Piece { kind = OB_PieceKind.Star, position = info.star, yaw = yaw, size = Vector3.one, segment = -1 });
                info.region = Region(firstPiece, stage == 0 ? lobbySpawn : info.spawn);
                // A flipped player falls up, so a flip stage gets an upper kill height: clear of everything
                // in the stage (a wall kick shaft or a climb can rise past the flip ceiling), not just the ceiling.
                if (flips) info.killTop = Mathf.Max(maxCeiling, info.region.max.y) + KillDepth;
                layout.ceilingY = Mathf.Max(layout.ceilingY, info.region.max.y + KillDepth);
                origin = World(exit);
                yaw = Mathf.Clamp(yaw + Range(-25f, 25f), startYaw - 50f, startYaw + 50f);
            }
            stage = stageCount;
            segment = -1;
            rotation = Quaternion.Euler(0f, yaw, 0f);
            Vector3 finishTop = new(0f, 0f, FinishSize * 0.5f);
            Box(OB_PieceKind.Finish, finishTop, new Vector3(FinishSize, 1f, FinishSize));
            layout.finishTop = World(finishTop);
            layout.finish = new Bounds(layout.finishTop + Vector3.up * 2f, new Vector3(FinishSize, 4f, FinishSize));
            FuseCrumbles();
            PlacePowerBlocks();
            return layout;
        }

        /// <summary>World box around the pieces placed from <paramref name="firstPiece"/> on (this stage's), and <paramref name="spawn"/>.</summary>
        Bounds Region(int firstPiece, Vector3 spawn) {
            var region = new Bounds(spawn, Vector3.zero);
            for (int i = firstPiece; i < layout.pieces.Count; i++) {
                OB_Piece piece = layout.pieces[i];
                if (piece.kind == OB_PieceKind.OutOfBounds) continue;
                float c = Mathf.Abs(Mathf.Cos(piece.yaw * Mathf.Deg2Rad)), s = Mathf.Abs(Mathf.Sin(piece.yaw * Mathf.Deg2Rad));
                Vector3 half = piece.size * 0.5f;
                region.Encapsulate(new Bounds(piece.position, 2f * new Vector3(c * half.x + s * half.z, half.y, s * half.x + c * half.z)));
            }
            return region;
        }

        /// <summary>A rest platform: a calm hop onto room to stand between two ideas.</summary>
        void Rest() {
            Rebase();
            Hop(new Vector2(RestSize, RestSize), 0f, 0f, Tension(Tune.restTension));
        }

        /// <summary>Returns the local exit for the next checkpoint pad, checking the jump onto it.</summary>
        Vector3 Exit(float gap, float gravityScale = 1f) {
            Vector3 exit = new(lastTop.x, lastTop.y, lastTop.z + lastSize.y * 0.5f + gap);
            Par(HopPar(gap, 0f, 0f, gravityScale));
            Step(exit + Vector3.forward * (PadSize * 0.5f), new Vector2(PadSize, PadSize), gravityScale);
            return exit;
        }

        void BuildSegment(OB_StageKind kind, float d) {
            switch (kind) {
                case OB_StageKind.Hops: Hops(d); break;
                case OB_StageKind.Springs: Springs(d); break;
                case OB_StageKind.MoonLeap: MoonLeap(d); break;
                case OB_StageKind.Conveyors: Conveyors(d); break;
                case OB_StageKind.Crumble: Crumble(d); break;
                case OB_StageKind.Beat: Beat(d); break;
                case OB_StageKind.WallJump: WallJump(d); break;
                case OB_StageKind.Movers: Movers(d); break;
                case OB_StageKind.Spinner: Spinner(d); break;
                case OB_StageKind.LavaRun: LavaRun(d); break;
                case OB_StageKind.Boost: Boost(d); break;
                case OB_StageKind.WallWalk: WallWalk(d); break;
                case OB_StageKind.CeilingRun: CeilingRun(d); break;
                case OB_StageKind.OnOff: OnOff(d); break;
                case OB_StageKind.Hammers: Hammers(d); break;
                case OB_StageKind.Crushers: Crushers(d); break;
                case OB_StageKind.Lasers: Lasers(d); break;
                case OB_StageKind.LogRoll: LogRoll(d); break;
                case OB_StageKind.Turntables: Turntables(d); break;
                case OB_StageKind.Cannons: Cannons(d); break;
                case OB_StageKind.Mushrooms: Mushrooms(d); break;
                case OB_StageKind.BeamWalk: BeamWalk(d); break;
                case OB_StageKind.Corkscrew: Corkscrew(d); break;
                case OB_StageKind.GravityFlip: GravityFlip(d); break;
                case OB_StageKind.LavaRise: LavaRise(d); break;
                case OB_StageKind.WindGaps: WindGaps(d); break;
                case OB_StageKind.IceRink: IceRink(d); break;
                case OB_StageKind.LiftShaft: LiftShaft(d); break;
                case OB_StageKind.GhostPath: GhostPath(d); break;
                case OB_StageKind.HoneySwamp: HoneySwamp(d); break;
                case OB_StageKind.BoostRings: BoostRings(d); break;
                case OB_StageKind.Swings: Swings(d); break;
                case OB_StageKind.RopeBridge: RopeBridge(d); break;
                default: Hops(d); break;
            }
        }
    }
}
