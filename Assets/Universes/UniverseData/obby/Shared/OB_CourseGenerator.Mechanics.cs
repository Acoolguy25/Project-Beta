using UnityEngine;

namespace Universes.UniverseData.obby {
    // The overhaul's mechanics (classic Roblox obby and Mario ideas), each a chainable segment.
    public sealed partial class OB_CourseGenerator {
        /// <summary>Standing room on the safe platforms of switch and hazard segments.</summary>
        const float NeutralSize = 4f;
        /// <summary>Gap above the robot's head to the underside of a "!" switch block: a standing jump reaches it, walking under does not.</summary>
        const float SwitchBlockClearance = 0.9f;
        public const float SwitchBlockSize = 2.2f;
        /// <summary>Height of a hammer head's centre above the bridge at the bottom of its swing (about the robot's middle).</summary>
        const float HammerHeadHeight = 2.6f;
        /// <summary>Crusher block height and how far its raised underside clears the robot's head.</summary>
        const float CrusherHeight = 3f, CrusherHeadroom = 1.5f;
        /// <summary>Laser curtains reach this far above the robot's head, so they cannot be jumped.</summary>
        const float LaserHeadroom = 3f;
        const float LaserThickness = 0.3f;
        /// <summary>Half-width of a rolling log's standable strip (its still collider) as a fraction of its radius.</summary>
        public const float LogStandFraction = 0.6f;
        /// <summary>Metres under the top platform's surface where rising lava stops.</summary>
        public const float LavaStopBelow = 1f;
        /// <summary>Metres a wind field reaches past the platforms either side of its gap, sideways and along the path.</summary>
        const float WindFieldSideRoom = 3f, WindFieldEdge = 1f;
        /// <summary>Metres a wind field reaches below the lower and above the higher platform's jump.</summary>
        const float WindFieldBelow = 2f, WindFieldAbove = 2f;

        // ---------------------------------------------------------------- ON/OFF switch blocks
        /// <summary>
        /// ON/OFF Blocks: red and blue blocks on one switch channel. Only one colour is solid at a time;
        /// a switch (a floor button early, a "!" block you jump into later) on each safe platform makes
        /// the next run solid and the one behind you vanish. Each run is the other colour, so every switch
        /// has to be hit. A respawn resets the channel.
        /// </summary>
        void OnOff(float d) {
            var t = Tune.onOff;
            int channel = NewChannel();
            int runs = t.runs.Count(d);
            bool bump = d > t.bumpFromDifficulty && Chance(t.bumpChance);
            // A "!" block hangs over the middle of its platform: leave open platform round it.
            float pad = bump ? Mathf.Max(NeutralSize, SwitchBlockSize + 2f * t.bumpRoom) : NeutralSize;
            Hop(new Vector2(pad, pad), 0f, 0f, Tension(Tune.restTension));
            for (int r = 0; r < runs; r++) {
                Switch(lastTop, channel, bump);
                // After r + 1 hits the channel is flipped when r is even: those runs are the second colour.
                int colour = (r + 1) % 2;
                int blocks = t.blocks.Count(d) + rng.Next(t.extraBlocks + 1);
                float size = HopSize();
                for (int b = 0; b < blocks; b++) {
                    int index = Hop(new Vector2(size, size), Range(t.blockRise.x, t.blockRise.y) * MaxRise(), Range(-1f, 1f) * t.blockJink, Tension(), OB_PieceKind.OnOffBlock, t.blockThickness);
                    Edit(index, p => { p.group = colour; p.channel = channel; return p; });
                    if (r == runs / 2 && b == blocks / 2) Coin(lastTop + Vector3.up * CoinLift);
                }
                Hop(new Vector2(pad, pad), Range(t.padRise.x, t.padRise.y) * MaxRise(), -lastTop.x * 0.5f, Tension(Tune.entryTension));
                Par(bump ? t.bumpPar : t.buttonPar);
            }
        }

        /// <summary>A switch on the surface whose top is <paramref name="top"/>: a floor button, or a "!" block above it.</summary>
        void Switch(Vector3 top, int channel, bool bump) {
            Vector3 at = bump ? top + Vector3.up * (m.bodyHeight + SwitchBlockClearance + SwitchBlockSize * 0.5f) : top;
            int index = Place(OB_PieceKind.OnOffSwitch, at, bump ? Vector3.one * SwitchBlockSize : new Vector3(2f, 0.3f, 2f));
            Edit(index, p => { p.group = bump ? 1 : 0; p.channel = channel; return p; });
        }

        // ---------------------------------------------------------------- hammers
        /// <summary>
        /// Hammer Bridge: a narrow bridge under swinging hammers that knock you off sideways. Time each
        /// crossing for the swing; later the bridge is split under the hammers, so you jump while they pass.
        /// </summary>
        void Hammers(float d) {
            var t = Tune.hammers;
            int hammers = t.hammers.Count(d);
            float width = Lerp(t.easyWidth, Mathf.Max(t.minWidth, tier.beamWidth * t.hardWidthScale), d);
            float spacing = t.spacing.At(d), armLength = t.arm.At(d);
            float amplitude = t.amplitude.At(d), period = t.period.At(d);
            float headWidth = t.headWidth.At(d);
            float headDepth = t.headDepth, endRoom = t.endRoom;
            bool split = d > t.splitFromDifficulty;
            float length = hammers * spacing + endRoom * 2f;
            // One long bridge, or (split) a landing with a short piece under each hammer after it.
            Hop(new Vector2(width, split ? endRoom * 2f : length), 0f, 0f, Tension(Tune.restTension), OB_PieceKind.Beam, t.bridgeThickness);
            float start = lastTop.z - length * 0.5f;
            float y = lastTop.y;
            for (int i = 0; i < hammers; i++) {
                float z;
                if (split) {
                    // Each hammer swings over its own short beam piece, a hop on from the last.
                    Hop(new Vector2(width, spacing), 0f, 0f, Tension(t.splitTension) * t.splitTensionScale, OB_PieceKind.Beam, t.bridgeThickness);
                    z = lastTop.z;
                } else {
                    z = start + endRoom + spacing * (i + 0.5f);
                }
                Add(new OB_Piece {
                    kind = OB_PieceKind.Pendulum, position = World(new Vector3(lastTop.x, y + HammerHeadHeight + armLength, z)), yaw = yaw,
                    size = new Vector3(headWidth, armLength, headDepth), vector = new Vector3(amplitude, 0f, 0f),
                    period = period, phase = period * (i % 2) * 0.5f + Range(0f, t.phaseJitter)
                });
                if (i == hammers / 2) Coin(new Vector3(lastTop.x, y + CoinLift, z));
                Par(period * t.parShare);
            }
            Par(length / m.walkSpeed);
        }

        // ---------------------------------------------------------------- crushers
        /// <summary>
        /// Thwomp Alley: crushers slam down across a corridor on a rhythm. They shake before they fall;
        /// wait in the gaps between them. Early on they lift in sequence for a steady runner; on the
        /// hardest tiers each has its own beat.
        /// </summary>
        void Crushers(float d) {
            var t = Tune.crushers;
            int count = t.crushers.Count(d);
            float width = t.width.At(d), depth = t.depth.At(d), safe = t.safe.At(d);
            float period = t.period.At(d), hold = t.hold.At(d);
            float drop = m.bodyHeight + CrusherHeadroom;
            float lead = t.lead, overhang = t.overhang;
            float length = lead + count * (depth + safe) + t.tail;
            Hop(new Vector2(width, length), 0f, 0f, Tension(Tune.restTension));
            Vector3 floor = lastTop;
            float start = floor.z - length * 0.5f;
            bool wave = d < t.waveBelowDifficulty;
            float travel = (depth + safe) / m.walkSpeed;
            for (int i = 0; i < count; i++) {
                float z = start + lead + i * (depth + safe) + depth * 0.5f;
                Add(new OB_Piece {
                    kind = OB_PieceKind.Crusher, position = World(new Vector3(floor.x, floor.y + drop + CrusherHeight * 0.5f, z)), yaw = yaw,
                    size = new Vector3(width + overhang * 2f, CrusherHeight, depth), vector = new Vector3(0f, drop, 0f),
                    period = period, speed = hold, phase = wave ? -i * travel : Range(0f, period)
                });
                if (i == count / 2) Coin(new Vector3(floor.x, floor.y + CoinLift, z + depth * 0.5f + safe * 0.5f));
            }
            Par(length / m.walkSpeed + period * t.parShare);
        }

        // ---------------------------------------------------------------- lasers
        /// <summary>
        /// Laser Gates: curtains of laser across a corridor blink on and off (they flicker before they
        /// switch on); later, low beams that are always on have to be jumped between them.
        /// </summary>
        void Lasers(float d) {
            var t = Tune.lasers;
            int gates = t.gates.Count(d);
            float width = t.width.At(d), spacing = t.spacing.At(d);
            float period = t.period.At(d), onFraction = t.onFraction.At(d);
            float lead = t.lead, overhang = t.overhang, lowBeamTop = t.lowBeamTop, lowBeamHeight = t.lowBeamHeight, lowBeamDepth = t.lowBeamDepth;
            float length = lead + gates * spacing;
            Hop(new Vector2(width, length), 0f, 0f, Tension(Tune.restTension));
            Vector3 floor = lastTop;
            float start = floor.z - length * 0.5f;
            bool wave = d < t.waveBelowDifficulty;
            float curtain = m.bodyHeight + LaserHeadroom;
            for (int i = 0; i < gates; i++) {
                float z = start + lead + i * spacing;
                bool low = d > t.lowBeamFromDifficulty && i % 2 == 1 && Chance(t.lowBeamChance);
                if (low) {
                    int index = Box(OB_PieceKind.Laser, new Vector3(floor.x, floor.y + lowBeamTop, z), new Vector3(width + overhang * 2f, lowBeamHeight, lowBeamDepth), standable: false);
                    Edit(index, p => { p.period = 0f; p.speed = 1f; return p; });
                    CheckJump(lowBeamDepth, 0f);
                } else {
                    int index = Box(OB_PieceKind.Laser, new Vector3(floor.x, floor.y + curtain, z), new Vector3(width + overhang * 2f, curtain, LaserThickness), standable: false);
                    float phase = wave ? -i * spacing / ReachSpeed : Range(0f, period);
                    Edit(index, p => { p.period = period; p.speed = onFraction; p.phase = phase; return p; });
                    Par(period * onFraction * t.parShare);
                }
                if (i == gates / 2) Coin(new Vector3(floor.x, floor.y + CoinLift, z + spacing * 0.5f));
            }
            Par(length / m.walkSpeed);
        }

        // ---------------------------------------------------------------- rolling logs
        /// <summary>
        /// Log Roll: logs lying along the path spin, so their tops carry you sideways; walk along them
        /// leaning against the roll. Each log turns the other way to the one before.
        /// </summary>
        void LogRoll(float d) {
            var t = Tune.logs;
            int logs = t.logs.Count(d);
            float radius = t.radius.At(d), length = t.length.At(d), speed = t.spin.At(d);
            float direction = Sign();
            float stand = radius * LogStandFraction * 2f;
            for (int i = 0; i < logs; i++) {
                Vector2 size = new(stand, length);
                Vector3 top = NextHop(size, Range(-t.rise, t.rise), Range(-t.jink, t.jink), Tension(Tune.entryTension));
                Add(new OB_Piece {
                    kind = OB_PieceKind.RollingLog, position = World(top - Vector3.up * radius), yaw = yaw,
                    size = new Vector3(radius * 2f, radius * 2f, length), speed = speed * direction
                });
                minTop = Mathf.Min(minTop, World(top).y);
                Par(rules.parPerHop + length / m.walkSpeed * t.parFactor);
                Step(top, size);
                if (i == logs / 2) Coin(top + Vector3.up * CoinLift);
                direction = -direction;
                if (i < logs - 1) {
                    float rest = HopSize();
                    Hop(new Vector2(rest, rest), 0f, 0f, Tension(Tune.entryTension));
                }
            }
        }

        // ---------------------------------------------------------------- turntables
        /// <summary>Turntables: spinning discs with bumper posts; cross each while it turns, hop disc to disc.</summary>
        void Turntables(float d) {
            var t = Tune.turntables;
            int discs = t.discs.Count(d);
            float diameter = t.diameter.At(d), speed = t.spin.At(d);
            int posts = t.posts.Count(d);
            float thickness = t.thickness;
            float direction = Sign();
            for (int i = 0; i < discs; i++) {
                Vector2 size = new(diameter, diameter);
                Vector3 top = NextHop(size, Range(-t.riseShare, t.riseShare) * MaxRise(), 0f, Tension(t.tension));
                Add(new OB_Piece {
                    kind = OB_PieceKind.Turntable, position = World(top), yaw = yaw, size = new Vector3(diameter, thickness, diameter),
                    speed = speed * direction, phase = Range(0f, RandomPhaseSeconds), group = posts
                });
                minTop = Mathf.Min(minTop, World(top).y);
                Par(rules.parPerHop + diameter / m.walkSpeed + t.parExtra);
                Step(top, size);
                if (i == 0) Coin(top + new Vector3(0f, CoinLift, -diameter * 0.25f));
                direction = -direction;
            }
        }

        // ---------------------------------------------------------------- cannons
        /// <summary>
        /// Cannons: step into the barrel and it fires you along a high arc toward a small island off to the
        /// side; steer in the air to land on it (let go and you fly straight). Later a second cannon on the
        /// island fires you on.
        /// </summary>
        void Cannons(float d) {
            var t = Tune.cannons;
            int cannons = d > t.twoFromDifficulty ? 2 : 1;
            float cannonBack = t.cannonBack;
            Hop(t.pad, 0f, 0f, Tension(Tune.restTension));
            Vector3 cannon = lastTop + Vector3.forward * cannonBack;
            for (int c = 0; c < cannons; c++) {
                float up = t.up.At(d), forward = t.forward.At(d);
                float rise = Range(t.rise.x, t.rise.y);
                float flight = m.LandTime(rise, 1f, up);
                float carry = m.Carry(forward, flight);
                float offset = Range(-1f, 1f) * t.offset.At(d) * m.walkSpeed * t.airControl * flight * tier.gapFraction.y;
                float target = t.target.At(d);
                bool last = c == cannons - 1;
                // Holding forward adds walk speed (kept to airControl) over the flight: the island spans the hands-off landing and that one.
                float heldShift = m.walkSpeed * t.airControl * flight * 0.9f;
                float islandDepth = Mathf.Max(last ? target : target + t.islandExtra, heldShift + 2f * t.landingSlack + 1f);
                Add(new OB_Piece { kind = OB_PieceKind.Cannon, position = World(cannon), yaw = yaw, vector = new Vector3(0f, up, forward), speed = flight });
                // The no-input landing is the island's near middle; reaching it means steering sideways by the offset.
                var island = new Vector3(cannon.x + offset, cannon.y + rise, cannon.z + carry + heldShift * 0.5f);
                Platform(island, new Vector3(target, 1f, islandDepth));
                Land(island, new Vector2(target, islandDepth));
                CheckSteer(Mathf.Max(0f, Mathf.Abs(offset) - target * 0.25f), flight, t.airControl);
                // A coin at the top of the arc, for those who fly true first and steer late.
                float apexTime = up / m.gravity;
                Coin(new Vector3(cannon.x, cannon.y + m.Apex(1f, up), cannon.z + m.Carry(forward, apexTime)));
                Par(flight + t.parExtra);
                cannon = island + Vector3.forward * (islandDepth * 0.5f - cannonBack);
            }
        }

        // ---------------------------------------------------------------- mushrooms
        /// <summary>
        /// Bounce Mushrooms: a chain of bouncy caps over the drop. Every cap throws you up again, so steer
        /// in the air from cap to cap; the caps shrink and spread out on the hard tiers.
        /// </summary>
        void Mushrooms(float d) {
            var t = Tune.mushrooms;
            int count = t.caps.Count(d);
            float cap = t.cap.At(d), bounce = t.bounce.At(d);
            float launch = Mathf.Sqrt(2f * m.gravity * bounce);
            for (int i = 0; i < count; i++) {
                Vector2 size = new(cap, cap);
                float rise = Range(-t.rise, t.rise);
                // The first cap is a plain jump; the rest are reached from a bounce.
                float from = i == 0 ? 0f : launch;
                if (i == 0) rise = Mathf.Min(rise, MaxRise());
                Vector3 top = NextHop(size, rise, Range(-1f, 1f) * t.jink, Tension(), 1f, from);
                Add(new OB_Piece {
                    kind = OB_PieceKind.Mushroom, position = World(top), yaw = yaw, size = new Vector3(cap, Mathf.Max(2f, top.y + KillDepth * 0.5f), cap),
                    vector = new Vector3(0f, launch, 0f)
                });
                minTop = Mathf.Min(minTop, World(top).y);
                CheckJump(EdgeGap(top, size), rise, 1f, false, from);
                Land(top, size);
                Par(m.LandTime(0f, 1f, launch) * t.parShare);
                if (i == count / 2) Coin(top + Vector3.up * (bounce * 0.8f));
            }
            Vector2 landing = new(NeutralSize, NeutralSize);
            float landRise = Range(t.landingRise.x, t.landingRise.y);
            Vector3 end = NextHop(landing, landRise, 0f, Tension(Tune.entryTension), 1f, launch);
            Platform(end, new Vector3(landing.x, 1f, landing.y));
            CheckJump(EdgeGap(end, landing), landRise, 1f, false, launch);
            Land(end, landing);
        }

        // ---------------------------------------------------------------- balance beams
        /// <summary>Balance Beams: narrow beams jinking sideways; later they crumble, and on Extreme they are barely wider than your feet.</summary>
        void BeamWalk(float d) {
            var t = Tune.beams;
            int beams = t.beams.Count(d);
            float width = Lerp(tier.beamWidth + t.extraWidth, tier.beamWidth, tierProgress * 0.5f + d * 0.5f);
            for (int i = 0; i < beams; i++) {
                float length = Range(t.length.x, t.length.y);
                float side = i % 2 == 0 ? 1f : -1f;
                int index = Hop(new Vector2(width, length), Range(t.rise.x, t.rise.y), side * Range(t.jink.x, t.jink.y) - lastTop.x * t.centering,
                    Tension(Tune.restTension) * t.tensionScale, OB_PieceKind.Beam, t.thickness);
                if (i > 0 && Chance(tier.mixChance * t.crumbleShare)) MakeCrumble(index, d);
                Par(length / m.walkSpeed * t.parShare);
                if (i == beams / 2) Coin(lastTop + Vector3.up * CoinLift);
            }
        }

        // ---------------------------------------------------------------- rising lava
        /// <summary>
        /// Lava Rise: a zigzag climb up small steps while lava rises from below. The chase starts when you
        /// reach the climb and restarts on a respawn; the lava stops just under the top platform.
        /// </summary>
        void LavaRise(float d) {
            var t = Tune.lavaRise;
            float baseSize = t.baseSize, topSize = t.topSize, sideRoom = t.sideRoom, lavaFloor = t.lavaFloor;
            float startBelow = t.startBelow;
            Hop(new Vector2(baseSize, baseSize), 0f, 0f, Tension(Tune.restTension));
            Vector3 baseTop = lastTop;
            float nearEdge = baseTop.z - baseSize * 0.5f;
            float minX = baseTop.x - baseSize * 0.5f, maxX = baseTop.x + baseSize * 0.5f;
            int steps = t.steps.Count(d);
            // The lava is timed on the climb's own clean-run time (each hop's par), so long zigzag hops
            // get the time they take to jump.
            float climbStart = par;
            for (int i = 0; i < steps; i++) {
                float size = HopSize();
                float target = (i % 2 == 0 ? 1f : -1f) * Range(t.zigzag.x, t.zigzag.y);
                Hop(new Vector2(size, size), Range(t.stepRise.x, t.stepRise.y) * MaxRise(), target - lastTop.x, Tension(t.tension));
                minX = Mathf.Min(minX, lastTop.x - size * 0.5f);
                maxX = Mathf.Max(maxX, lastTop.x + size * 0.5f);
                if (i == steps / 2) Coin(lastTop + Vector3.up * CoinLift);
            }
            Hop(new Vector2(topSize, topSize), Range(t.topRise.x, t.topRise.y) * MaxRise(), -lastTop.x * 0.5f, Tension(Tune.entryTension));
            Vector3 top = lastTop;
            float climbTime = par - climbStart;
            minX = Mathf.Min(minX, top.x - topSize * 0.5f);
            maxX = Mathf.Max(maxX, top.x + topSize * 0.5f);
            float farEdge = top.z + topSize * 0.5f;

            float start = baseTop.y - startBelow, travel = top.y - LavaStopBelow - start;
            // A clean climber stays ahead with this much time to spare.
            float slack = t.slack.At(d), delay = t.delay.At(d);
            float speed = travel / Mathf.Max(1f, climbTime * slack);
            var footprint = new Vector2(maxX - minX + sideRoom * 2f, farEdge - nearEdge);
            float depth = travel + lavaFloor;
            int index = Box(OB_PieceKind.RisingLava, new Vector3((minX + maxX) * 0.5f, start, (nearEdge + farEdge) * 0.5f),
                new Vector3(footprint.x, depth, footprint.y), standable: false);
            Edit(index, p => { p.speed = speed; p.period = delay; p.vector = new Vector3(0f, travel, 0f); return p; });
        }

        // ---------------------------------------------------------------- wind
        /// <summary>
        /// Gust Gaps: hops across gaps where a crosswind blows the jumper sideways. Each gap has its own
        /// field, blowing the other way to the last, and the gusts swell in turn on the shared clock, so
        /// aim upwind and time the calm. Walking against the wind holds you still, so every gap stays
        /// jumpable; the gaps are sized for the forward speed left after steering against the strongest gust.
        /// </summary>
        void WindGaps(float d) {
            var t = Tune.wind;
            int hops = t.hops.Count(d);
            float calm = t.calm.At(d) * m.walkSpeed, gust = calm * t.gust.At(d);
            float period = t.period.At(d);
            float strongest = calm + gust;
            // Forward speed left over once steering cancels the strongest sideways push.
            float headway = Mathf.Sqrt(Mathf.Max(0f, 1f - strongest * strongest / (ReachSpeed * ReachSpeed)));
            float size = t.size.At(d);
            for (int i = 0; i < hops; i++) {
                Vector3 from = lastTop;
                Vector2 fromSize = lastSize;
                Hop(new Vector2(size, size), HopRise(t.upChance) * t.riseScale, Range(-1f, 1f) * t.jink, Tension(Tune.entryTension) * headway);
                // The field spans the gap and the edges either side of it, with room for a full jump above.
                float side = i % 2 == 0 ? 1f : -1f;
                float halfWidth = Mathf.Max(fromSize.x, size) * 0.5f + WindFieldSideRoom;
                float minX = Mathf.Min(from.x, lastTop.x) - halfWidth, maxX = Mathf.Max(from.x, lastTop.x) + halfWidth;
                float minY = Mathf.Min(from.y, lastTop.y) - WindFieldBelow, maxY = Mathf.Max(from.y, lastTop.y) + m.bodyHeight + m.jumpHeight + WindFieldAbove;
                float minZ = from.z + fromSize.y * 0.5f - WindFieldEdge, maxZ = lastTop.z - size * 0.5f + WindFieldEdge;
                Vector3 min = new(minX, minY, minZ), max = new(maxX, maxY, maxZ);
                Add(new OB_Piece {
                    kind = OB_PieceKind.Wind, position = World((min + max) * 0.5f), yaw = yaw, size = max - min,
                    vector = WorldDirection(Vector3.right * side) * calm, speed = gust, period = period, phase = i * period * 0.5f
                });
                Par(period * t.parShare);
                if (i == hops / 2) Coin(lastTop + new Vector3(-side * (size * 0.5f - 0.6f), CoinLift, 0f));
            }
        }

        // ---------------------------------------------------------------- ice
        /// <summary>
        /// Ice Rink: long sheets of ice with gaps between them, jinking sideways. On ice you keep your
        /// speed and turn slowly, so line up early, carry speed into each jump and do not slide off the side.
        /// Every gap off a sheet is sized for the speed a player who stopped on it can build up again over
        /// its length (grip limits acceleration), so a respawn or a stop never strands anyone.
        /// </summary>
        void IceRink(float d) {
            var t = Tune.ice;
            int sheets = t.sheets.Count(d);
            float width = t.width.At(d), traction = t.traction.At(d);
            for (int i = 0; i < sheets; i++) {
                float length = Range(t.length.x, t.length.y);
                float side = i % 2 == 0 ? 1f : -1f;
                int index = Hop(new Vector2(width, length), Range(t.rise.x, t.rise.y), side * Range(t.jink.x, t.jink.y) - lastTop.x * t.centering,
                    Tension(t.tension), OB_PieceKind.Ice, t.thickness);
                Edit(index, p => { p.speed = traction; return p; });
                Par(length / m.walkSpeed * t.parShare);
                if (i == sheets / 2) Coin(lastTop + new Vector3(side * (width * 0.5f - 0.6f), CoinLift, length * 0.3f));
                // The next jump leaves this sheet: size it for the speed a standing start can reach on it.
                reachCap = m.SpeedFromRest(traction, length - t.runUpMargin);
            }
            // Solid ground to stop on before whatever comes next.
            Hop(new Vector2(NeutralSize, NeutralSize), 0f, 0f, Tension(Tune.restTension));
            reachCap = float.PositiveInfinity;
        }
    }
}
