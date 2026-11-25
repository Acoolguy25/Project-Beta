using UnityEngine;

namespace Universes.UniverseData.obby {
    // The original mechanics, rebuilt as chainable segments. Sizes lerp from a gentle value (d = 0)


    public sealed partial class OB_CourseGenerator {
        /// <summary>How far the wall-kick walls reach above their ledge, metres.</summary>
        const float WallAboveLedge = 1f;
        /// <summary>Movers, spinners and turntables start at a random point this many seconds into their motion.</summary>
        const float RandomPhaseSeconds = 10f;

        /// <summary>Turns the piece at <paramref name="index"/> into a block that crumbles shortly after it is touched.</summary>
        void MakeCrumble(int index, float d) => Edit(index, p => {
            p.kind = OB_PieceKind.Crumble;
            // Short fuses on the hard tiers; FuseCrumbles keeps each one long enough to run across.
            p.speed = Tune.crumbleMix.fuse.At(d);
            p.period = Tune.crumbleMix.regrow;
            return p;
        });

        /// <summary>
        /// Lengthens any crumble fuse shorter than crossing the piece's longest side at walk
        /// speed (plus a margin) takes, once every piece has its final size.
        /// </summary>
        void FuseCrumbles() {
            for (int i = 0; i < layout.pieces.Count; i++) {
                OB_Piece p = layout.pieces[i];
                if (p.kind is not (OB_PieceKind.Crumble or OB_PieceKind.Plank) || p.stage >= layout.stages.Length) continue;
                float cross = Mathf.Max(p.size.x, p.size.z) / Mathf.Max(0.1f, m.walkSpeed) + rules.crumbleCrossMargin;
                if (p.speed >= cross) continue;
                p.speed = cross;
                layout.pieces[i] = p;
            }
        }

        /// <summary>Plain hops: varied sizes, steps up and down, sideways jinks; crumbling ones later on.</summary>
        void Hops(float d) {
            var t = Tune.hops;
            int count = t.hops + rng.Next(t.extraHops + 1) + Mathf.RoundToInt(tierProgress * t.hopsOverTier);
            float wander = t.wander.At(d);
            for (int i = 0; i < count; i++) {
                float size = HopSize();
                int index = Hop(new Vector2(size, size), HopRise(), Range(-1f, 1f) * wander, Tension());
                if (i > 0 && Chance(tier.mixChance * t.crumbleShare)) MakeCrumble(index, d);
                if (i == count / 2) Coin(lastTop + Vector3.up * CoinLift);
            }
        }

        float SpringSpeed(float rise) => Mathf.Sqrt(2f * m.gravity * (rise + Tune.springs.overshoot));

        /// <summary>Spring Climb: a spring onto a high ledge, then (later) a second spring onto a small one.</summary>
        void Springs(float d) {
            var t = Tune.springs;
            float padReach = t.padReach;
            Hop(t.baseSize, HopRise(t.baseUpChance), 0f, Tension(Tune.entryTension));
            Vector3 baseTop = lastTop;
            float rise = t.rise.At(d);
            Vector3 spring = baseTop + Vector3.forward * t.springForward;
            Add(new OB_Piece { kind = OB_PieceKind.Spring, position = World(spring), yaw = yaw, vector = new Vector3(0f, SpringSpeed(rise), 0f) });
            Coin(spring + Vector3.up * (rise * t.coinShare));
            bool second = d > t.secondFromDifficulty;
            float ledgeDepth = second ? t.ledgeDepthWithSecond : t.ledgeDepth, ledgeWidth = t.ledgeWidth.At(d);
            var ledge = new Vector3(baseTop.x + Range(-t.ledgeJink, t.ledgeJink) * (1f + d), baseTop.y + rise, spring.z + padReach + ledgeDepth * 0.5f);
            Platform(ledge, new Vector3(ledgeWidth, 1f, ledgeDepth));
            Land(ledge, new Vector2(ledgeWidth, ledgeDepth));
            Par(t.firstPar);
            if (!second) return;
            float rise2 = t.secondRise.At(d), size = t.secondSize.At(d);
            Vector3 spring2 = ledge + Vector3.forward * t.secondSpringForward;
            Add(new OB_Piece { kind = OB_PieceKind.Spring, position = World(spring2), yaw = yaw, vector = new Vector3(0f, SpringSpeed(rise2), 0f) });
            var top = new Vector3(Mathf.Clamp(ledge.x + Range(-t.secondJink, t.secondJink), -t.secondMaxOffset, t.secondMaxOffset), ledge.y + rise2, spring2.z + padReach + size * 0.5f);
            Platform(top, new Vector3(size, 1f, size));
            Land(top, new Vector2(size, size));
            Par(t.secondPar);
        }

        /// <summary>Moon Leap: floaty low-gravity jumps across wide gaps, ending on a landing inside the field.</summary>
        void MoonLeap(float d) {
            var t = Tune.moonLeap;
            float landing = t.landing, sideHalf = t.sideHalf, headroom = t.headroom;
            float scale = t.gravity.At(d);
            int count = t.leaps.Steps(d);
            float highest = 0f;
            for (int i = 0; i < count; i++) {
                float w = HopSize() * t.sizeScale;
                Vector3 top = NextHop(new Vector2(w, w), Range(t.rise.x, t.rise.y), Range(-t.jink, t.jink), Tension() * t.tensionScale, scale);
                Platform(top, new Vector3(w, 1f, w));
                Par(rules.parPerHop + t.parPerLeap);
                Step(top, new Vector2(w, w), scale);
                highest = Mathf.Max(highest, top.y);
                if (i == count / 2) Coin(top + Vector3.up * t.coinLift);
            }
            Vector3 end = NextHop(new Vector2(landing, landing), 0f, 0f, Tension(Tune.restTension) * t.tensionScale, scale);
            Platform(end, new Vector3(landing, 1f, landing));
            Step(end, new Vector2(landing, landing), scale);
            Field(new Vector3(-sideHalf, -KillDepth, -SegmentBackReach), new Vector3(sideHalf, highest + headroom, end.z + landing * 0.5f), Vector3.zero, scale);
        }

        /// <summary>Belt Run: belts that push back and sideways; walk against them.</summary>
        void Conveyors(float d) {
            var t = Tune.conveyors;
            int count = t.belts.Steps(d);
            float width = t.width.At(d);
            float length = t.length;
            Vector3 previous = Vector3.zero;
            for (int i = 0; i < count; i++) {
                Vector3 direction = i == 0 ? Vector3.back : rng.Next(3) switch { 0 => Vector3.back, 1 => Vector3.left, _ => Vector3.right };
                if (direction != Vector3.back && direction == previous) direction = -direction;
                previous = direction;
                float speed = direction == Vector3.back ? t.backSpeed.At(d) : t.sideSpeed.At(d);
                int index;
                if (i == 0) {
                    index = Hop(new Vector2(width, length), 0f, 0f, Tension(Tune.entryTension), OB_PieceKind.Conveyor, t.thickness);
                } else {
                    var top = lastTop + Vector3.forward * length;
                    index = Box(OB_PieceKind.Conveyor, top, new Vector3(width, t.thickness, length));
                    Land(top, new Vector2(width, length));
                    Par(length / m.walkSpeed * t.parFactor);
                }
                Edit(index, p => { p.vector = direction; p.speed = speed; return p; });
                if (i == count / 2) Coin(lastTop + new Vector3(width * 0.5f - 0.5f, 1.2f, 0f));
            }
        }

        /// <summary>Crumble Bridge: cracked blocks that drop soon after you land. Keep moving.</summary>
        void Crumble(float d) {
            var t = Tune.crumble;
            int count = t.blocks.Steps(d);
            float size = t.size.At(d), fuse = t.fuse.At(d), regrow = Tune.crumbleMix.regrow;
            for (int i = 0; i < count; i++) {
                int index = Hop(new Vector2(size, size), Range(t.riseShare.x, t.riseShare.y) * MaxRise(), Range(-1f, 1f) * t.jink.At(d), Tension(), OB_PieceKind.Crumble, t.thickness);
                Edit(index, p => { p.speed = fuse; p.period = regrow; return p; });
                if (i == count / 2) Coin(lastTop + Vector3.up * CoinLift);
            }
        }

        /// <summary>Beat Blocks: pink and blue blocks swap on the beat; read the blink and jump in time.</summary>
        void Beat(float d) {
            var t = Tune.beat;
            int count = t.blocks.Steps(d);
            float size = t.size.At(d), period = t.period.At(d);
            int group = 0;
            for (int i = 0; i < count; i++) {
                bool rest = d < t.restBelowDifficulty && i % 3 == 2;
                int index = Hop(new Vector2(size, size), Range(t.riseShare.x, t.riseShare.y) * MaxRise(), Range(-t.jink, t.jink), Tension(t.tension),
                    rest ? OB_PieceKind.Platform : OB_PieceKind.Beat, t.thickness);
                if (!rest) {
                    int g = group;
                    Edit(index, p => { p.group = g; p.period = period; p.phase = g == 0 ? 0f : period * 0.5f; return p; });
                    group = 1 - group;
                    Par(period * t.parShare);
                }
                if (i == count / 2) Coin(lastTop + Vector3.up * CoinLift);
            }
        }

        /// <summary>Wall Kick Shaft: kick between two striped walls up to a ledge.</summary>
        void WallJump(float d) {
            var t = Tune.wallJump;
            float gapBetween = t.gap.At(d), height = t.height.At(d);
            float length = t.length, ledgeGap = t.ledgeGap;
            float floorLength = length + t.floorExtraLength;
            Hop(new Vector2(gapBetween + t.floorExtraWidth, floorLength), 0f, 0f, Tension(Tune.entryTension));
            Vector3 floor = lastTop;
            // The walls stand centred on the floor's length; they reach WallAboveLedge over the ledge.
            float wallZ = floor.z - floorLength * 0.5f + t.floorExtraLength * 0.5f + length * 0.5f;
            float upSpeed = Mathf.Sqrt(2f * m.gravity * t.kickHeight), awaySpeed = t.awaySpeed.At(d);
            for (int side = -1; side <= 1; side += 2) {
                int index = Box(OB_PieceKind.WallJumpWall, new Vector3(floor.x + side * (gapBetween * 0.5f + t.thickness * 0.5f), floor.y + height + WallAboveLedge, wallZ),
                    new Vector3(t.thickness, height + WallAboveLedge, length), standable: false);
                Edit(index, p => { p.vector = new Vector3(awaySpeed, upSpeed, 0f); return p; });
            }
            Coin(new Vector3(floor.x, floor.y + height * t.coinShare, wallZ));
            float ledgeSize = t.ledgeSize.At(d);
            var ledge = new Vector3(floor.x, floor.y + height, wallZ + length * 0.5f + ledgeGap + ledgeSize * 0.5f);
            Platform(ledge, new Vector3(ledgeSize, 1f, ledgeSize));
            Land(ledge, new Vector2(ledgeSize, ledgeSize));
            Par(height / t.climbRate + t.parExtra);
        }

        /// <summary>Sky Ferry: one platform shuttles across a wide gap, or several slide across the path.</summary>
        void Movers(float d) {
            var t = Tune.movers;
            float y = lastTop.y, sink = t.thickness * 0.5f;
            if (Chance(t.ferryChance)) {
                float gap = t.ferryGap.At(d), size = t.ferrySize.At(d), landingWidth = t.landingWidth.At(d), speed = t.ferrySpeed.At(d);
                float boarding = t.boarding;
                var start = new Vector3(0f, y, size * 0.5f + boarding);
                Add(new OB_Piece {
                    kind = OB_PieceKind.Mover, position = World(start - Vector3.up * sink), yaw = yaw,
                    size = new Vector3(size, t.thickness, size), vector = WorldDirection(new Vector3(0f, 0f, gap - size - boarding * 2f)),
                    speed = speed, phase = Range(0f, RandomPhaseSeconds)
                });
                Coin(new Vector3(0f, y + CoinLift, gap * 0.5f));
                var landing = new Vector3(0f, y, gap + t.landingBeyond);
                Platform(landing, new Vector3(landingWidth, 1f, t.landingDepth));
                Land(landing, new Vector2(landingWidth, t.landingDepth));
                Par(gap / speed * 2f); // there and back, waiting for it at worst
            } else {
                int count = t.sliders.Steps(d);
                float size = t.sliderSize.At(d), speed = t.sliderSpeed.At(d);
                for (int i = 0; i < count; i++) {
                    float amplitude = t.amplitude.At(d);
                    // A rider can wait for the platform to pass the centre line, so the jump is measured there.
                    Vector3 centre = NextHop(new Vector2(size, size), 0f, -lastTop.x, Tension(Tune.restTension) * t.sliderTensionScale);
                    var top = new Vector3(-amplitude, centre.y, centre.z);
                    Add(new OB_Piece {
                        kind = OB_PieceKind.Mover, position = World(top - Vector3.up * sink), yaw = yaw,
                        size = new Vector3(size, t.thickness, size), vector = WorldDirection(new Vector3(amplitude * 2f, 0f, 0f)),
                        speed = speed, phase = Range(0f, RandomPhaseSeconds)
                    });
                    minTop = Mathf.Min(minTop, World(top).y);
                    Step(new Vector3(0f, centre.y, centre.z), new Vector2(size, size));
                    Par(rules.parPerHop + amplitude / speed);
                    if (i == 0) Coin(new Vector3(0f, centre.y + CoinLift, centre.z));
                }
            }
        }

        /// <summary>Degrees between a spinner's arms: a bar has two, a cross four.</summary>
        public static float SpinnerArmSpacing(bool cross) => cross ? 90f : 180f;

        /// <summary>Sweeper: an arena with a spinning bar (a cross later, then two counter-rotating arenas).</summary>
        void Spinner(float d) {
            var t = Tune.spinner;
            float arena = t.arena, barLength = t.barLength, barHeight = t.barHeight;
            int arenas = d > t.twoFromDifficulty ? 2 : 1;
            float direction = Sign();
            bool cross = d > t.crossFromDifficulty;
            // Arms pass any spot every (360 / arms) / speed seconds: never sooner than a hop takes to land.
            float speed = Mathf.Min(t.speed.At(d), SpinnerArmSpacing(cross) / (m.AirTime() + t.rhythmSlack));
            for (int i = 0; i < arenas; i++) {
                Hop(new Vector2(arena, arena), 0f, 0f, Tension(Tune.entryTension) * t.arenaTensionScale);
                Vector3 floor = lastTop;
                Add(new OB_Piece {
                    kind = cross ? OB_PieceKind.SpinnerCross : OB_PieceKind.SpinnerBar,
                    position = World(floor + Vector3.up * barHeight), yaw = yaw, size = new Vector3(barLength, 0.5f, 0.5f),
                    speed = speed * direction, phase = Range(0f, RandomPhaseSeconds)
                });
                if (i == 0) Coin(floor + Vector3.up * 2.4f);
                direction = -direction;
                Par(t.parPerArena);
            }
        }

        /// <summary>Lava Stones: small stones over a lava lake, zigzagging; some crumble on the hard tiers.</summary>
        void LavaRun(float d) {
            var t = Tune.lavaRun;
            float stone = Mathf.Max(t.minSize, HopSize() * t.sizeShare);
            int count = t.stones.Steps(d);
            float lowest = 0f;
            for (int i = 0; i < count; i++) {
                float lateral = (i % 2 == 0 ? 1f : -1f) * Range(t.zigzag.x, t.zigzag.y) - lastTop.x * 0.5f;
                int index = Hop(new Vector2(stone, stone), Range(-t.rise, t.rise), lateral, Tension(), OB_PieceKind.Platform, t.thickness);
                if (Chance(tier.mixChance * t.crumbleShare)) MakeCrumble(index, d);
                if (i == count / 2) Coin(lastTop + Vector3.up * CoinLift);
                lowest = Mathf.Min(lowest, lastTop.y);
            }
            // The lake lies under every stone but stops short of where the next segment may build.
            float lakeWidth = t.lakeWidth, lakeDepth = t.lakeDepth, lakeBelow = t.lakeBelow;
            float lakeEnd = Mathf.Max(stone, lastTop.z + stone * 0.5f - SegmentBackReach);
            Box(OB_PieceKind.Lava, new Vector3(0f, lowest - lakeBelow, lakeEnd * 0.5f), new Vector3(lakeWidth, lakeDepth, lakeEnd), standable: false);
        }

        /// <summary>Boost Gap: a boost panel flings you across a gap too long to jump.</summary>
        void Boost(float d) {
            var t = Tune.boost;
            float up = t.up, forward = t.forward, drop = t.drop, runwayLength = t.runwayLength, padFromEnd = t.padFromEnd;
            Hop(new Vector2(t.runwayWidth, runwayLength), 0f, 0f, Tension(Tune.entryTension));
            Vector3 runway = lastTop;
            Vector3 pad = runway + Vector3.forward * (runwayLength * 0.5f - padFromEnd);
            Add(new OB_Piece { kind = OB_PieceKind.Boost, position = World(pad), yaw = yaw, vector = new Vector3(0f, up, forward) });
            float flight = (up + Mathf.Sqrt(up * up + 2f * m.gravity * drop)) / m.gravity;
            float carry = m.Carry(forward, flight);
            float runwayEnd = runway.z + runwayLength * 0.5f;
            float gap = Mathf.Min(t.gap.At(d), carry - (runwayEnd - pad.z) - 1f);
            float length = Mathf.Max(t.minLanding, carry + m.walkSpeed * flight - (runwayEnd - pad.z) - gap + 2f);
            float width = t.landingWidth.At(d);
            Coin(new Vector3(runway.x, runway.y + 2.5f, runwayEnd + gap * 0.5f));
            var landing = new Vector3(runway.x + Range(-t.jink, t.jink) * (1f + d), runway.y - drop, runwayEnd + gap + length * 0.5f);
            Platform(landing, new Vector3(width, 1f, length));
            Land(landing, new Vector2(width, length));
            Par(flight + length * 0.5f / m.walkSpeed);
        }
    }
}
