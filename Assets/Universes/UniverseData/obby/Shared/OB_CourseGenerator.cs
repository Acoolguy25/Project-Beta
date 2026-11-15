using System;
using System.Collections.Generic;
using RyanAssets.Core;
using UnityEngine;

namespace Universes.UniverseData.obby {
    public enum OB_PieceKind : byte {
        Platform, CheckpointPad, Finish, Lava, Spring, Boost, GravityZone, Conveyor,
        Crumble, Beat, WallJumpWall, Mover, SpinnerBar, SpinnerCross, Coin
    }

    public enum OB_StageKind : byte {
        Hops, Springs, MoonLeap, Conveyors, Crumble, Beat, WallJump, Movers, Spinner, LavaRun, Boost,
        // Added later: keep new kinds at the end so existing values (sent over the network) stay stable.
        WallWalk, CeilingRun
    }

    /// <summary>One placed course piece. Field meaning per kind is documented on <see cref="OB_CourseGenerator"/>.</summary>
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

        public static OB_Movement Default => new() {
            walkSpeed = 10f, jumpHeight = 2.6f, gravity = 30f, airMomentumDamping = 0.6f, bodyHeight = 5.6f
        };
        public float JumpSpeed => Mathf.Sqrt(2f * gravity * jumpHeight);
        public float AirTime(float gravityScale = 1f) => 2f * JumpSpeed / (gravity * gravityScale);
        public float FlatJumpReach(float gravityScale = 1f) => walkSpeed * AirTime(gravityScale);
    }

    /// <summary>A plain jump between two surfaces, recorded so tests can prove every course is jumpable.</summary>
    public struct OB_JumpCheck {
        public int stage;
        public float gap, rise, maxGap, maxRise;
    }

    public sealed class OB_Stage {
        public int index;
        public OB_StageKind kind;
        public float difficulty;
        /// <summary>Top centre of the checkpoint a player respawns on while this stage is next.</summary>
        public Vector3 spawn;
        /// <summary>Volume that awards this checkpoint. Unused for stage 0 (the lobby).</summary>
        public Bounds checkpoint;
        /// <summary>Below this height a player on this stage has fallen and goes back to <see cref="spawn"/>.</summary>
        public float killY;
        public Vector3 coin;
    }

    public sealed class OB_CourseLayout {
        public int seed;
        public OB_Movement movement;
        public readonly List<OB_Piece> pieces = new();
        public readonly List<OB_JumpCheck> jumpChecks = new();
        public OB_Stage[] stages;
        public Bounds finish;
        public Vector3 finishTop;
        public int StageCount => stages.Length;
    }

    /// <summary>
    /// Deterministic course generator. The same seed, stage count, movement and start always give the
    /// same pieces on every peer, so the server sends only the seed. Stages get harder from first to last.
    /// <para>
    /// Piece fields by kind: Spring/Boost <c>vector</c> = pad-space launch velocity; GravityZone
    /// <c>speed</c> = gravity scale, <c>vector</c> = world gravity direction (zero keeps it; strength
    /// only); Conveyor <c>vector</c> = local push direction, <c>speed</c>;
    /// Crumble <c>speed</c> = crumble delay, <c>period</c> = respawn delay; Beat <c>period</c>,
    /// <c>phase</c>, <c>group</c>; WallJumpWall <c>vector.x</c> = away speed, <c>vector.y</c> = up speed;
    /// Mover <c>vector</c> = world travel, <c>speed</c>, <c>phase</c>; SpinnerBar/Cross <c>speed</c> =
    /// signed degrees per second, <c>phase</c>.
    /// </para>
    /// </summary>
    public sealed class OB_CourseGenerator {
        public const float PadSize = 6f;
        public const float FinishSize = 10f;
        public const float KillDepth = 12f;
        public const int MaxStages = 60;
        /// <summary>Height of a coin above the surface it is collected from.</summary>
        const float CoinLift = 1.6f;

        readonly SeededStoryRandom rng;
        readonly OB_CourseLayout layout;
        readonly OB_Movement m;
        Vector3 origin;
        float yaw;
        Quaternion rotation;
        int stage;
        float minTop;
        Vector3 coin;
        Vector3 lastTop;
        Vector2 lastSize;

        OB_CourseGenerator(int seed, OB_Movement movement) {
            rng = new SeededStoryRandom(seed);
            m = movement;
            layout = new OB_CourseLayout { seed = seed, movement = movement };
        }

        public static OB_CourseLayout Generate(int seed, int stageCount, OB_Movement movement, Vector3 start, float startYaw, Vector3 lobbySpawn) {
            if (movement.gravity <= 0f || movement.jumpHeight <= 0f || movement.walkSpeed <= 0f)
                throw new ArgumentException("Course movement needs positive gravity, jump height and walk speed.", nameof(movement));
            if (movement.bodyHeight <= 0f) movement.bodyHeight = OB_Movement.Default.bodyHeight; // older scenes
            var generator = new OB_CourseGenerator(seed, movement);
            return generator.Build(Mathf.Clamp(stageCount, 1, MaxStages), start, startYaw, lobbySpawn);
        }

        // ---------------------------------------------------------------- randomness
        float Value() => rng.Next(1 << 24) / (float)(1 << 24);
        float Range(float min, float max) => min + (max - min) * Value();
        bool Chance(float probability) => Value() < probability;
        static float Lerp(float a, float b, float t) => a + (b - a) * t;

        // ---------------------------------------------------------------- frame helpers
        Vector3 World(Vector3 local) => origin + rotation * local;
        Vector3 World(float x, float y, float z) => World(new Vector3(x, y, z));

        int Add(OB_Piece piece) {
            piece.stage = stage;
            layout.pieces.Add(piece);
            return layout.pieces.Count - 1;
        }

        /// <summary>Adds a box whose top face is centred on <paramref name="localTop"/>.</summary>
        int Box(OB_PieceKind kind, Vector3 localTop, Vector3 size, bool standable = true) {
            if (standable) minTop = Mathf.Min(minTop, World(localTop).y);
            return Add(new OB_Piece {
                kind = kind, position = World(localTop - Vector3.up * (size.y * 0.5f)), yaw = yaw, size = size
            });
        }

        int Platform(Vector3 localTop, Vector3 size) => Box(OB_PieceKind.Platform, localTop, size);

        /// <summary>Records a plain jump from the last surface to this one and makes it the last surface.</summary>
        void Step(Vector3 top, Vector2 size, float gravityScale = 1f) {
            float dx = Mathf.Max(0f, Mathf.Abs(top.x - lastTop.x) - (size.x + lastSize.x) * 0.5f);
            float dz = Mathf.Max(0f, Mathf.Abs(top.z - lastTop.z) - (size.y + lastSize.y) * 0.5f);
            CheckJump(Mathf.Sqrt(dx * dx + dz * dz), top.y - lastTop.y, gravityScale);
            Land(top, size);
        }

        /// <summary>
        /// Records a jump of <paramref name="gap"/> across and <paramref name="rise"/> up, measured in the
        /// player's own frame (on a wall or ceiling "up" is away from that surface).
        /// </summary>
        void CheckJump(float gap, float rise, float gravityScale = 1f) {
            layout.jumpChecks.Add(new OB_JumpCheck {
                stage = stage, gap = gap, rise = rise,
                maxGap = m.FlatJumpReach(gravityScale) * 0.7f, maxRise = m.jumpHeight * 0.75f / gravityScale
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
                vector = localDown == Vector3.zero ? Vector3.zero : rotation * localDown.normalized
            });
        }

        /// <summary>Makes a surface reached some other way (spring, ride, wall jump) the last surface.</summary>
        void Land(Vector3 top, Vector2 size) {
            lastTop = top;
            lastSize = size;
        }

        /// <summary>Returns the local exit for the next checkpoint pad, checking the jump onto it.</summary>
        Vector3 Exit(float gap, float gravityScale = 1f) {
            Vector3 exit = new(0f, lastTop.y, lastTop.z + lastSize.y * 0.5f + gap);
            Step(exit + Vector3.forward * (PadSize * 0.5f), new Vector2(PadSize, PadSize), gravityScale);
            return exit;
        }

        // ---------------------------------------------------------------- course
        OB_CourseLayout Build(int stageCount, Vector3 start, float startYaw, Vector3 lobbySpawn) {
            layout.stages = new OB_Stage[stageCount];
            origin = start;
            yaw = startYaw;
            OB_StageKind[] kinds = PickKinds(stageCount);
            for (stage = 0; stage < stageCount; stage++) {
                rotation = Quaternion.Euler(0f, yaw, 0f);
                var info = new OB_Stage {
                    index = stage, kind = kinds[stage],
                    difficulty = stageCount > 1 ? stage / (float)(stageCount - 1) : 0f
                };
                minTop = float.PositiveInfinity;
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
                coin = new Vector3(0f, 1.5f, 2f);
                Vector3 exit = BuildStage(info.kind, info.difficulty);
                info.coin = World(coin);
                info.killY = minTop - KillDepth;
                layout.stages[stage] = info;
                Add(new OB_Piece { kind = OB_PieceKind.Coin, position = info.coin, yaw = yaw, size = Vector3.one });
                origin = World(exit);
                yaw = Mathf.Clamp(yaw + Range(-25f, 25f), startYaw - 50f, startYaw + 50f);
            }
            stage = stageCount;
            rotation = Quaternion.Euler(0f, yaw, 0f);
            Vector3 finishTop = new(0f, 0f, FinishSize * 0.5f);
            Box(OB_PieceKind.Finish, finishTop, new Vector3(FinishSize, 1f, FinishSize));
            layout.finishTop = World(finishTop);
            layout.finish = new Bounds(layout.finishTop + Vector3.up * 2f, new Vector3(FinishSize, 4f, FinishSize));
            return layout;
        }

        OB_StageKind[] PickKinds(int count) {
            var kinds = new OB_StageKind[count];
            var all = (OB_StageKind[])Enum.GetValues(typeof(OB_StageKind));
            var bag = new List<OB_StageKind>();
            kinds[0] = OB_StageKind.Hops;
            for (int i = 1; i < count; i++) {
                if (bag.Count == 0) { bag.AddRange(all); rng.Shuffle(bag); }
                int pick = bag[0] == kinds[i - 1] && bag.Count > 1 ? 1 : 0;
                kinds[i] = bag[pick];
                bag.RemoveAt(pick);
            }
            return kinds;
        }

        Vector3 BuildStage(OB_StageKind kind, float d) => kind switch {
            OB_StageKind.Hops => Hops(d),
            OB_StageKind.Springs => Springs(d),
            OB_StageKind.MoonLeap => MoonLeap(d),
            OB_StageKind.Conveyors => Conveyors(d),
            OB_StageKind.Crumble => Crumble(d),
            OB_StageKind.Beat => Beat(d),
            OB_StageKind.WallJump => WallJump(d),
            OB_StageKind.Movers => Movers(d),
            OB_StageKind.Spinner => Spinner(d),
            OB_StageKind.LavaRun => LavaRun(d),
            OB_StageKind.Boost => Boost(d),
            OB_StageKind.WallWalk => WallWalk(d),
            OB_StageKind.CeilingRun => CeilingRun(d),
            _ => Hops(d)
        };

        // ---------------------------------------------------------------- stages
        Vector3 Hops(float d) {
            int count = 4 + rng.Next(3) + Mathf.RoundToInt(d * 2f);
            float z = 0f, x = 0f, y = 0f;
            for (int i = 0; i < count; i++) {
                float w = Lerp(3.4f, 1.7f, d) * Range(0.9f, 1.1f);
                z += Lerp(2f, 4f, d) * Range(0.85f, 1f);
                x = Mathf.Clamp(x + Range(-1f, 1f) * Lerp(0.6f, 2f, d), -4f, 4f);
                y += Range(-0.6f, Lerp(0.5f, 1.2f, d));
                var top = new Vector3(x, y, z + w * 0.5f);
                Platform(top, new Vector3(w, 1f, w));
                Step(top, new Vector2(w, w));
                if (i == count / 2) coin = top + Vector3.up * 1.6f;
                z += w;
            }
            return Exit(Lerp(2f, 3.2f, d));
        }

        float SpringSpeed(float rise) => Mathf.Sqrt(2f * m.gravity * (rise + 2.5f));

        Vector3 Springs(float d) {
            var baseTop = new Vector3(0f, 0f, 1.5f + 3.5f);
            Platform(baseTop, new Vector3(6f, 1f, 7f));
            Step(baseTop, new Vector2(6f, 7f));
            float rise = Lerp(5f, 7.5f, d);
            Add(new OB_Piece { kind = OB_PieceKind.Spring, position = World(0f, 0f, 5f), yaw = yaw, vector = new Vector3(0f, SpringSpeed(rise), 0f) });
            coin = new Vector3(0f, rise * 0.8f, 5f);
            bool second = d > 0.45f;
            float ledgeDepth = second ? 7f : 5f;
            var ledge = new Vector3(Range(-1.5f, 1.5f), rise, 9.5f + ledgeDepth * 0.5f);
            Platform(ledge, new Vector3(5f, 1f, ledgeDepth));
            Land(ledge, new Vector2(5f, ledgeDepth));
            if (second) {
                float rise2 = Lerp(4f, 6.5f, d);
                Add(new OB_Piece { kind = OB_PieceKind.Spring, position = World(ledge.x, rise, 12f), yaw = yaw, vector = new Vector3(0f, SpringSpeed(rise2), 0f) });
                var top = new Vector3(Mathf.Clamp(ledge.x + Range(-2f, 2f), -3f, 3f), rise + rise2, 18f + 2.5f);
                Platform(top, new Vector3(5f, 1f, 5f));
                Land(top, new Vector2(5f, 5f));
            }
            return Exit(Lerp(2f, 3f, d));
        }

        Vector3 MoonLeap(float d) {
            float scale = Lerp(0.42f, 0.32f, d);
            int count = 3 + Mathf.RoundToInt(d);
            float z = 0f, y = 0f, highest = 0f;
            for (int i = 0; i < count; i++) {
                float w = Lerp(4f, 2.6f, d);
                z += m.FlatJumpReach(scale) * Lerp(0.33f, 0.5f, d);
                y += Range(-1f, 2.5f);
                highest = Mathf.Max(highest, y);
                var top = new Vector3(Range(-2f, 2f), y, z + w * 0.5f);
                Platform(top, new Vector3(w, 1f, w));
                Step(top, new Vector2(w, w), scale);
                if (i == count / 2) coin = top + Vector3.up * 4f;
                z += w;
            }
            Vector3 exit = Exit(3f, scale);
            Field(new Vector3(-11f, -KillDepth, -2f), new Vector3(11f, highest + 14f, exit.z + 1f), Vector3.zero, scale);
            return exit;
        }

        Vector3 Conveyors(float d) {
            int count = 3 + Mathf.RoundToInt(d * 2f);
            float width = Lerp(4.6f, 2.8f, d), length = 5f, z = 1.5f;
            Vector3 previous = Vector3.zero;
            for (int i = 0; i < count; i++) {
                Vector3 direction = i == 0 ? Vector3.back : rng.Next(3) switch { 0 => Vector3.back, 1 => Vector3.left, _ => Vector3.right };
                if (direction != Vector3.back && direction == previous) direction = -direction;
                previous = direction;
                float speed = direction == Vector3.back ? Lerp(3f, 6f, d) : Lerp(1.8f, 3.8f, d);
                var top = new Vector3(0f, 0f, z + length * 0.5f);
                int index = Box(OB_PieceKind.Conveyor, top, new Vector3(width, 0.6f, length));
                var piece = layout.pieces[index];
                piece.vector = direction;
                piece.speed = speed;
                layout.pieces[index] = piece;
                if (i == 0) Step(top, new Vector2(width, length)); else Land(top, new Vector2(width, length));
                if (i == count / 2) coin = top + new Vector3(width * 0.5f - 0.5f, 1.2f, 0f);
                z += length;
            }
            return Exit(2f);
        }

        Vector3 Crumble(float d) {
            int count = 5 + Mathf.RoundToInt(d * 4f);
            const float size = 2.2f;
            float z = 0f, x = 0f, y = 0f;
            for (int i = 0; i < count; i++) {
                z += Lerp(0.5f, 1.9f, d);
                x = Mathf.Clamp(x + Range(-1f, 1f) * Lerp(0f, 1.4f, d), -3f, 3f);
                y += Range(0f, 0.35f);
                var top = new Vector3(x, y, z + size * 0.5f);
                int index = Box(OB_PieceKind.Crumble, top, new Vector3(size, 0.6f, size));
                var piece = layout.pieces[index];
                piece.speed = Lerp(0.9f, 0.4f, d);
                piece.period = 3f;
                layout.pieces[index] = piece;
                Step(top, new Vector2(size, size));
                if (i == count / 2) coin = top + Vector3.up * 1.6f;
                z += size;
            }
            return Exit(Lerp(1.5f, 2.5f, d));
        }

        Vector3 Beat(float d) {
            int count = 6 + Mathf.RoundToInt(d * 3f);
            const float size = 2.6f;
            float period = Lerp(4.6f, 3.4f, d);
            float z = 0f, y = 0f;
            int group = 0;
            for (int i = 0; i < count; i++) {
                z += Lerp(0.8f, 2.2f, d);
                y += Range(-0.2f, 0.4f);
                var top = new Vector3(Range(-1.5f, 1.5f), y, z + size * 0.5f);
                bool rest = d < 0.5f && i % 3 == 2;
                if (rest) {
                    Platform(top, new Vector3(size, 0.8f, size));
                } else {
                    int index = Box(OB_PieceKind.Beat, top, new Vector3(size, 0.8f, size));
                    var piece = layout.pieces[index];
                    piece.group = group;
                    piece.period = period;
                    piece.phase = group == 0 ? 0f : period * 0.5f;
                    layout.pieces[index] = piece;
                    group = 1 - group;
                }
                Step(top, new Vector2(size, size));
                if (i == count / 2) coin = top + Vector3.up * 1.6f;
                z += size;
            }
            return Exit(2f);
        }

        Vector3 WallJump(float d) {
            float gapBetween = Lerp(4.2f, 3.4f, d), height = Lerp(6f, 9f, d), length = 5f;
            float floorStart = 1.5f, floorLength = length + 2f;
            var floor = new Vector3(0f, 0f, floorStart + floorLength * 0.5f);
            Platform(floor, new Vector3(gapBetween + 1.6f, 1f, floorLength));
            Step(floor, new Vector2(gapBetween + 1.6f, floorLength));
            float wallZ = floorStart + 1f + length * 0.5f;
            float upSpeed = Mathf.Sqrt(2f * m.gravity * 1.9f), awaySpeed = Lerp(6.5f, 7.5f, d);
            for (int side = -1; side <= 1; side += 2) {
                int index = Box(OB_PieceKind.WallJumpWall, new Vector3(side * (gapBetween * 0.5f + 0.3f), height + 1f, wallZ),
                    new Vector3(0.6f, height + 1f, length), standable: false);
                var piece = layout.pieces[index];
                piece.vector = new Vector3(awaySpeed, upSpeed, 0f);
                layout.pieces[index] = piece;
            }
            coin = new Vector3(0f, height * 0.6f, wallZ);
            var ledge = new Vector3(0f, height, floorStart + 1f + length + 0.6f + 2.5f);
            Platform(ledge, new Vector3(5f, 1f, 5f));
            Land(ledge, new Vector2(5f, 5f));
            return Exit(2.5f);
        }

        Vector3 Movers(float d) {
            if (Chance(0.5f)) {
                // Ferry: one platform shuttles across a wide gap.
                float gap = Lerp(9f, 15f, d);
                const float size = 3.2f;
                var start = new Vector3(0f, 0f, size * 0.5f + 0.3f);
                Add(new OB_Piece {
                    kind = OB_PieceKind.Mover, position = World(start - Vector3.up * 0.3f), yaw = yaw,
                    size = new Vector3(size, 0.6f, size), vector = rotation * new Vector3(0f, 0f, gap - size - 0.6f),
                    speed = Lerp(2.6f, 4.2f, d), phase = Range(0f, 10f)
                });
                coin = new Vector3(0f, 1.6f, gap * 0.5f);
                var landing = new Vector3(0f, 0f, gap + 2.5f);
                Platform(landing, new Vector3(6f, 1f, 5f));
                Land(landing, new Vector2(6f, 5f));
            } else {
                // Shuttles: platforms slide side to side across the path.
                int count = 2 + Mathf.RoundToInt(d);
                const float size = 3f;
                float z = 0f;
                for (int i = 0; i < count; i++) {
                    z += Lerp(2.2f, 3.2f, d);
                    float amplitude = Lerp(2.5f, 4.5f, d);
                    var top = new Vector3(-amplitude, 0f, z + size * 0.5f);
                    Add(new OB_Piece {
                        kind = OB_PieceKind.Mover, position = World(top - Vector3.up * 0.3f), yaw = yaw,
                        size = new Vector3(size, 0.6f, size), vector = rotation * new Vector3(amplitude * 2f, 0f, 0f),
                        speed = Lerp(2.2f, 4f, d), phase = Range(0f, 10f)
                    });
                    minTop = Mathf.Min(minTop, World(top).y);
                    // A rider can wait for the platform to pass the centre line.
                    Step(new Vector3(0f, 0f, top.z), new Vector2(size, size));
                    if (i == 0) coin = new Vector3(0f, 1.6f, top.z);
                    z += size;
                }
            }
            return Exit(2.5f);
        }

        Vector3 Spinner(float d) {
            var arena = new Vector3(0f, 0f, 1.5f + 6f);
            Platform(arena, new Vector3(12f, 1f, 12f));
            Step(arena, new Vector2(12f, 12f));
            float speed = Lerp(70f, 140f, d) * (Chance(0.5f) ? 1f : -1f);
            Add(new OB_Piece {
                kind = d > 0.55f ? OB_PieceKind.SpinnerCross : OB_PieceKind.SpinnerBar,
                position = World(0f, 0.55f, arena.z), yaw = yaw, size = new Vector3(11.4f, 0.5f, 0.5f),
                speed = speed, phase = Range(0f, 10f)
            });
            coin = new Vector3(0f, 2.4f, arena.z);
            return Exit(2.5f);
        }

        Vector3 LavaRun(float d) {
            const float stone = 1.8f;
            int count = 6 + Mathf.RoundToInt(d * 3f);
            float z = 0f, x = 0f;
            for (int i = 0; i < count; i++) {
                z += Lerp(1.4f, 3f, d) * Range(0.85f, 1f);
                x = (i % 2 == 0 ? 1f : -1f) * Range(0.5f, 2.2f);
                var top = new Vector3(x, 0.2f, z + stone * 0.5f);
                Platform(top, new Vector3(stone, 1.2f, stone));
                Step(top, new Vector2(stone, stone));
                if (i == count / 2) coin = top + Vector3.up * 1.6f;
                z += stone;
            }
            float lavaLength = z + 1.5f;
            Box(OB_PieceKind.Lava, new Vector3(0f, -0.6f, lavaLength * 0.5f), new Vector3(8f, 0.4f, lavaLength), standable: false);
            return Exit(Lerp(1.5f, 2.5f, d));
        }

        Vector3 Boost(float d) {
            var runway = new Vector3(0f, 0f, 1.5f + 4f);
            Platform(runway, new Vector3(4f, 1f, 8f));
            Step(runway, new Vector2(4f, 8f));
            const float up = 9f, forward = 24f, drop = 1.5f, padZ = 6.5f;
            Add(new OB_Piece { kind = OB_PieceKind.Boost, position = World(0f, 0f, padZ), yaw = yaw, vector = new Vector3(0f, up, forward) });
            // Distance the fading boost alone carries a player who lets go of the controls.
            float flight = (up + Mathf.Sqrt(up * up + 2f * m.gravity * drop)) / m.gravity;
            float k = Mathf.Max(0.01f, m.airMomentumDamping);
            float carry = forward * (1f - Mathf.Exp(-k * flight)) / k;
            float gap = Mathf.Min(Lerp(6.5f, 9f, d), carry - (9.5f - padZ) - 1f);
            float length = Mathf.Max(14f, carry + m.walkSpeed * flight - (9.5f - padZ) - gap + 2f);
            coin = new Vector3(0f, 2.5f, 9.5f + gap * 0.5f);
            var landing = new Vector3(Range(-1f, 1f), -drop, 9.5f + gap + length * 0.5f);
            Platform(landing, new Vector3(8f, 1f, length));
            Land(landing, new Vector2(8f, length));
            return Exit(2.5f);
        }
            // Gravity-direction stages. A field turns "down" toward a surface beside or above the path.
        // Each field ends flush with that surface so a player standing on it stays inside, and its far
        // end opens over a landing, so walking out of it drops the player back to normal gravity on
        // solid ground. Sizes allow for the character turning over about its middle
        // (OB_Movement.bodyHeight tall, about a third as wide).
        const float GravityPathHalfWidth = 2.5f;
        const float GravityFieldStart = 3f;

        /// <summary>Approach floor from the stage start into the start of the field.</summary>
        void GravityApproach(float halfWidth) {
            var approach = new Vector3(0f, 0f, (GravityFieldStart + 1f) * 0.5f);
            Platform(approach, new Vector3(halfWidth * 2f, 1f, GravityFieldStart + 1f));
            Step(approach, new Vector2(halfWidth * 2f, GravityFieldStart + 1f));
        }

        void WallSegment(float x, float top, float height, float thickness, float fromZ, float toZ) {
            if (toZ - fromZ < 0.01f) return;
            Box(OB_PieceKind.Platform, new Vector3(x, top, (fromZ + toZ) * 0.5f), new Vector3(thickness, height, toZ - fromZ), standable: false);
        }

        /// <summary>Wall Walk: gravity turns sideways, onto a tall wall that players run along.</summary>
        Vector3 WallWalk(float d) {
            const float halfWidth = GravityPathHalfWidth, thickness = 1f, overrun = 1.5f;
            float side = Chance(0.5f) ? 1f : -1f;
            // The wall stands clear of the approach so a player turning over beside it does not
            // swing their feet into it; they fall the rest of the way.
            float wallGap = m.bodyHeight * 0.35f;
            float face = side * (halfWidth + wallGap);
            float bottom = -1.5f, top = Mathf.Max(m.bodyHeight + 2f, Lerp(7f, 8f, d)), height = top - bottom;
            GravityApproach(halfWidth);

            // Falling across onto the wall carries the player forward, so the first obstacle comes after
            // room to land. Obstacles (gaps in the wall or lava strips across it) sit a running jump
            // apart, so clearing one never lands the player on the next.
            float settle = m.walkSpeed * Mathf.Sqrt(2f * (halfWidth * 2f + wallGap) / m.gravity);
            float spacing = m.FlatJumpReach() + 3f;
            int obstacles = 1 + Mathf.RoundToInt(d * 2f);
            float first = GravityFieldStart + settle + 3f;
            float wallEnd = first + (obstacles - 1) * spacing + Lerp(7f, 6f, d) + overrun;
            float wallX = face + side * thickness * 0.5f;
            float z = GravityFieldStart;
            for (int i = 0; i < obstacles; i++) {
                float centre = first + i * spacing;
                if (Chance(0.5f)) {
                    float gap = Lerp(2.2f, 3.2f, d);
                    WallSegment(wallX, top, height, thickness, z, centre - gap * 0.5f);
                    z = centre + gap * 0.5f;
                    CheckJump(gap, 0f);
                } else {
                    float strip = Lerp(1.4f, 2.4f, d);
                    Box(OB_PieceKind.Lava, new Vector3(face - side * 0.2f, top, centre), new Vector3(0.4f, height, strip), standable: false);
                    CheckJump(strip, 0f);
                }
            }
            WallSegment(wallX, top, height, thickness, z, wallEnd);
            // Between the first two obstacles (or after the only one), near the wall's high edge.
            float coinZ = obstacles > 1 ? first + spacing * 0.5f : first + 4f;
            coin = new Vector3(face - side * CoinLift, top - 2f, coinZ);

            float fieldEnd = wallEnd - overrun;
            float near = -side * halfWidth;
            Field(new Vector3(Mathf.Min(near, face), bottom + 0.5f, GravityFieldStart),
                new Vector3(Mathf.Max(near, face), top, fieldEnd), new Vector3(side, 0f, 0f));

            // Leaving the field drops the player down the wall face onto this landing.
            const float landingLength = 9f;
            float landingWidth = halfWidth + Mathf.Abs(face);
            var landing = new Vector3((near + face) * 0.5f, bottom, fieldEnd + landingLength * 0.5f);
            Platform(landing, new Vector3(landingWidth, 1f, landingLength));
            Land(landing, new Vector2(landingWidth, landingLength));
            return Exit(2.5f);
        }

        /// <summary>Ceiling Run: gravity flips upward and players run upside down along a ceiling.</summary>
        Vector3 CeilingRun(float d) {
            const float thickness = 1f, overrun = 1.5f;
            float halfWidth = Lerp(GravityPathHalfWidth, 1.75f, d);
            // High enough to walk under upright, low enough that the flip is a short fall.
            float height = m.bodyHeight + Lerp(2f, 3.5f, d);
            GravityApproach(halfWidth);

            // Lava hanging under the ceiling. Patches sit a full running jump apart, so a jump over one
            // never comes down on the next.
            int patches = 1 + Mathf.RoundToInt(d * 1.4f);
            float patch = Lerp(1.4f, 2.6f, d);
            float spacing = m.FlatJumpReach() + patch + 3f;
            float first = GravityFieldStart + 7f;
            for (int i = 0; i < patches; i++) {
                Box(OB_PieceKind.Lava, new Vector3(0f, height, first + i * spacing), new Vector3(halfWidth * 2f, 0.3f, patch), standable: false);
                CheckJump(patch, 0f);
            }

            float start = GravityFieldStart - 0.5f;
            float fieldEnd = first + (patches - 1) * spacing + Lerp(7f, 6f, d), end = fieldEnd + overrun;
            Box(OB_PieceKind.Platform, new Vector3(0f, height + thickness, (start + end) * 0.5f),
                new Vector3(halfWidth * 2f, thickness, end - start), standable: false);
            coin = new Vector3(0f, height - CoinLift, fieldEnd - 1.2f); // past the last patch, still under the ceiling
            Field(new Vector3(-halfWidth, -1f, GravityFieldStart), new Vector3(halfWidth, height, fieldEnd), Vector3.up);

            const float landingLength = 8f;
            var landing = new Vector3(0f, 0f, fieldEnd + landingLength * 0.5f);
            Platform(landing, new Vector3(6f, 1f, landingLength));
            Land(landing, new Vector2(6f, landingLength));
            return Exit(2.5f);
        }
    }
}
