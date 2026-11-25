using UnityEngine;

namespace Universes.UniverseData.obby {
    // Round-2 mechanics (2026-11-25): Lift Shaft, Ghost Path, Honey Swamp, Boost Rings, Swings, Rope Bridge.
    // Every one is fair at walk speed: plain jumps are recorded with CheckJump, launches and flights with
    // CheckSteer, and the tests replay their timing (ghost windows, ring flights, swing reach).
    public sealed partial class OB_CourseGenerator {
        // ---------------------------------------------------------------- lifts
        /// <summary>
        /// Lift Shaft: step onto a platform that rides up (then down) the shaft and step off at the other
        /// end. Lifts are ordinary movers travelling vertically; the ledges sit one short hop from the lift's stops.
        /// </summary>
        void LiftShaft(float d) {
            var t = Tune.lifts;
            int lifts = t.lifts.Count(d);
            for (int i = 0; i < lifts; i++) {
                bool up = i % 2 == 0;
                float travel = t.travel.At(d), speed = t.speed.At(d), size = t.size.At(d);
                float board = t.boardGap.At(d), exit = t.exitGap.At(d);
                Vector3 from = lastTop;
                float z = from.z + lastSize.y * 0.5f + board + size * 0.5f;
                float dy = up ? travel : -travel;
                Add(new OB_Piece {
                    kind = OB_PieceKind.Mover, position = World(new Vector3(from.x, from.y - t.thickness * 0.5f, z)), yaw = yaw,
                    size = new Vector3(size, t.thickness, size), vector = WorldDirection(new Vector3(0f, dy, 0f)),
                    speed = speed, phase = Range(0f, RandomPhaseSeconds)
                });
                Step(new Vector3(from.x, from.y, z), new Vector2(size, size));
                minTop = Mathf.Min(minTop, World(new Vector3(0f, from.y + Mathf.Min(0f, dy), 0f)).y);
                if (i == 0) Coin(new Vector3(from.x, from.y + dy * 0.5f + CoinLift, z));
                // Leaving the lift at the far stop: one short hop onto the ledge.
                Land(new Vector3(from.x, from.y + dy, z), new Vector2(size, size));
                Vector2 ledge = t.ledge;
                var ledgeTop = new Vector3(from.x, from.y + dy, z + size * 0.5f + exit + ledge.y * 0.5f);
                Platform(ledgeTop, new Vector3(ledge.x, 1f, ledge.y));
                Step(ledgeTop, ledge);
                Par(travel / speed * t.parShare + rules.parPerHop * 2f);
            }
        }

        // ---------------------------------------------------------------- ghosts
        /// <summary>
        /// Ghost Path: pale platforms that fade in one after another along the path, each solid for a
        /// window that overlaps the one before it. Hop on as the next appears (it flickers first); a ghost
        /// you wait on too long vanishes under you.
        /// </summary>
        void GhostPath(float d) {
            var t = Tune.ghosts;
            int blocks = t.blocks.Count(d);
            float window = t.window.At(d);
            float overlap = Mathf.Min(t.overlap.At(d), window * 0.9f);
            float period = Mathf.Max(window, t.period.At(d));
            float stride = window - overlap, size = t.size.At(d);
            for (int i = 0; i < blocks; i++) {
                int index = Hop(new Vector2(size, size), Range(t.riseShare.x, t.riseShare.y) * MaxRise(), Range(-1f, 1f) * t.jink,
                    Tension(t.tension), OB_PieceKind.Ghost, t.thickness);
                // A block is solid from its phase on (cycle = time - phase), so each later ghost's phase is one stride later.
                float phase = i * stride;
                Edit(index, p => { p.period = period; p.speed = window / period; p.phase = phase; return p; });
                Par(stride * t.parShare);
                if (i == blocks / 2) Coin(lastTop + Vector3.up * CoinLift);
            }
            Hop(new Vector2(NeutralSize, NeutralSize), 0f, 0f, Tension(Tune.entryTension));
        }

        // ---------------------------------------------------------------- honey
        /// <summary>
        /// Honey Swamp: long sticky sheets that slow a walker, each followed by firm ground to take off from.
        /// The jump off a sheet is sized for the slowed pace, and the firm pad after it gives the run-up for
        /// the next jump, so honey costs time and nerve but never fairness.
        /// </summary>
        void HoneySwamp(float d) {
            var t = Tune.honey;
            int sheets = t.sheets.Count(d);
            // A base platform first, so the checkpoint pad keeps room for its arch.
            Hop(new Vector2(NeutralSize, NeutralSize), 0f, 0f, Tension(Tune.entryTension));
            float width = t.width.At(d), keep = t.keep.At(d);
            for (int i = 0; i < sheets; i++) {
                float length = t.length.At(d) * Range(0.9f, 1.1f);
                int index = Hop(new Vector2(width, length), Range(t.riseShare.x, t.riseShare.y) * MaxRise(),
                    Range(-1f, 1f) * t.jink - lastTop.x * 0.3f, Tension(t.tension), OB_PieceKind.Honey, t.thickness);
                Edit(index, p => { p.speed = keep; return p; });
                Par(length / (m.walkSpeed * keep) * t.parShare);
                if (i == sheets / 2) Coin(lastTop + Vector3.up * CoinLift);
                // Leaving the sheet the player walks at the slowed pace: the hop onto firm ground is sized for it.
                reachCap = m.walkSpeed * keep;
                Hop(new Vector2(t.recoveryWidth, t.recoveryRun), 0f, 0f, Tension(Tune.restTension));
                reachCap = float.PositiveInfinity;
            }
        }

        // ---------------------------------------------------------------- rings
        /// <summary>
        /// Boost Rings: a gap far too wide to jump, crossed through hoops that each fire you on to the next
        /// along a fixed arc (the next ring always sits on the way down, where the launch pad logic fires
        /// again), ending on an island. Passing a ring off-centre shifts the flight, so the rings are big
        /// and the island deep; steering in the air corrects the rest.
        /// </summary>
        void BoostRings(float d) {
            var t = Tune.rings;
            int rings = t.rings.Count(d);
            float diameter = t.size.At(d), launch = t.launch.At(d), lateral = t.lateral.At(d), depth = t.depth;
            Vector3 centre = new(lastTop.x, lastTop.y + t.firstHeight, lastTop.z + lastSize.y * 0.5f + t.firstAhead + depth * 0.5f);
            CheckJump(t.firstAhead, 0f);
            float side = Sign();
            for (int i = 0; i < rings; i++) {
                bool last = i == rings - 1;
                float flight = Range(t.flight.x, t.flight.y);
                float dy = last ? Range(t.islandRise.x, t.islandRise.y) : Range(t.rise.x, t.rise.y);
                float lat = side * lateral * Range(0.5f, 1f);
                side = -side;
                // Horizontal speed carries Carry(1, flight) metres per m/s (launch protection, then air damping).
                float carry = m.Carry(1f, flight);
                var velocity = new Vector3(lat / carry, (dy + 0.5f * m.gravity * flight * flight) / flight, launch);
                Add(new OB_Piece {
                    kind = OB_PieceKind.Ring, position = World(centre), yaw = yaw, size = new Vector3(diameter, diameter, depth),
                    vector = WorldDirection(velocity), speed = flight
                });
                CheckSteer(diameter * 0.5f, flight, t.airControl);
                Par(flight + t.parPerRing);
                if (i == rings / 2) Coin(centre + new Vector3(lat * 0.5f, m.Apex(1f, velocity.y) * 0.5f, m.Carry(launch, flight * 0.5f)));
                centre += new Vector3(lat, dy, m.Carry(launch, flight));
            }
            float width = t.islandWidth.At(d), length = t.islandDepth.At(d);
            var island = new Vector3(centre.x, centre.y, centre.z);
            Platform(island, new Vector3(width, 1f, length));
            Land(island, new Vector2(width, length));
        }

        // ---------------------------------------------------------------- swings
        /// <summary>
        /// Swings: a trapeze platform swings across the gap between two ledges. Both ends of its swing are
        /// level with a ledge, so hop on at the near end and off at the far end, riding the dip between.
        /// </summary>
        void Swings(float d) {
            var t = Tune.swings;
            int swings = t.swings.Count(d);
            for (int i = 0; i < swings; i++) {
                float rope = t.rope.At(d), amplitude = t.amplitude.At(d), period = t.period.At(d);
                float width = t.width.At(d), depth = t.depth.At(d), board = t.boardGap.At(d), exit = t.exitGap.At(d);
                float radians = amplitude * Mathf.Deg2Rad;
                float reach = rope * Mathf.Sin(radians), dip = rope * (1f - Mathf.Cos(radians));
                Vector3 from = lastTop;
                float zc = from.z + lastSize.y * 0.5f + board + depth * 0.5f + reach;
                Add(new OB_Piece {
                    kind = OB_PieceKind.Swing, position = World(new Vector3(from.x, from.y + rope * Mathf.Cos(radians), zc)), yaw = yaw,
                    size = new Vector3(width, rope, depth), vector = new Vector3(amplitude, t.thickness, 0f),
                    period = period, phase = Range(0f, period)
                });
                Vector2 size = new(width, depth);
                Step(new Vector3(from.x, from.y, zc - reach), size);
                minTop = Mathf.Min(minTop, World(new Vector3(0f, from.y - dip, 0f)).y);
                if (i == 0) Coin(new Vector3(from.x, from.y - dip + CoinLift, zc));
                Land(new Vector3(from.x, from.y, zc + reach), size);
                Vector2 ledge = t.ledge;
                var ledgeTop = new Vector3(from.x, from.y, zc + reach + depth * 0.5f + exit + ledge.y * 0.5f);
                Platform(ledgeTop, new Vector3(ledge.x, 1f, ledge.y));
                Step(ledgeTop, ledge);
                Par(period * t.parShare + rules.parPerHop * 2f);
            }
        }

        // ---------------------------------------------------------------- rope bridge
        /// <summary>
        /// Rope Bridge: a long run of planks that fall away shortly after you step on them (never sooner than
        /// crossing them at walk speed takes); keep moving. Gaps between planks are small recorded hops.
        /// </summary>
        void RopeBridge(float d) {
            var t = Tune.rope;
            int planks = t.planks.Count(d);
            Hop(new Vector2(NeutralSize, NeutralSize), 0f, 0f, Tension(Tune.entryTension));
            for (int i = 0; i < planks; i++) {
                float width = t.width.At(d), depth = t.depth.At(d), gap = t.gap.At(d);
                var top = new Vector3(lastTop.x * 0.5f + Range(-1f, 1f) * t.sway, lastTop.y + Range(-1f, 1f) * t.bob,
                    lastTop.z + lastSize.y * 0.5f + gap + depth * 0.5f);
                int index = Box(OB_PieceKind.Plank, top, new Vector3(width, t.thickness, depth));
                float fuse = t.fuse.At(d);
                Edit(index, p => { p.speed = fuse; p.period = t.regrow; return p; });
                Par((depth + gap) / m.walkSpeed * t.parFactor);
                Step(top, new Vector2(width, depth));
                if (i == planks / 2) Coin(top + Vector3.up * CoinLift);
            }
            Hop(new Vector2(NeutralSize, NeutralSize), 0f, 0f, Tension(Tune.entryTension));
        }
    }
}
