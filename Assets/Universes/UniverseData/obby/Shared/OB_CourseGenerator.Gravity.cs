using UnityEngine;

namespace Universes.UniverseData.obby {
    // Gravity-direction segments. A field turns "down" toward a surface beside or above the path. Each
    // field ends flush with that surface so a player standing on it stays inside, and its far end opens
    // over a landing, so walking out of it drops the player back to normal gravity on solid ground.
    // Sizes allow for the character turning over about its middle (OB_Movement.bodyHeight tall, about a
    // third as wide). Flip pads (GravityFlip) need no field: their flip lasts until the next pad.
    public sealed partial class OB_CourseGenerator {
        /// <summary>Lava hanging under a ceiling (thickness), and lava strips standing out of a wall (depth), metres.</summary>
        const float LavaSheetThickness = 0.3f, LavaStripThickness = 0.4f;
        OB_GravityTuning GravityTune => Tune.gravity;
        float GravityFieldStart => GravityTune.fieldStart;

        /// <summary>Approach floor from the segment start a metre into the field.</summary>
        void GravityApproach(float halfWidth) {
            var approach = new Vector3(0f, 0f, (GravityFieldStart + 1f) * 0.5f);
            Platform(approach, new Vector3(halfWidth * 2f, 1f, GravityFieldStart + 1f));
            Step(approach, new Vector2(halfWidth * 2f, GravityFieldStart + 1f));
        }

        void WallSegment(float x, float top, float height, float thickness, float fromZ, float toZ) {
            if (toZ - fromZ < 0.01f) return;
            Box(OB_PieceKind.Platform, new Vector3(x, top, (fromZ + toZ) * 0.5f), new Vector3(thickness, height, toZ - fromZ), standable: false);
        }

        /// <summary>Forward drift (m) while falling <paramref name="distance"/> at a sprint (players hold sprint).</summary>
        float FallDrift(float distance) => m.sprintSpeed * Mathf.Sqrt(2f * Mathf.Max(0f, distance) / m.gravity);

        /// <summary>
        /// Length of the floor a field drops the player onto from up to <paramref name="drop"/> above it: long
        /// enough to catch a sprinting fall, with room to stop.
        /// </summary>
        float GravityLanding(float drop) => Mathf.Max(GravityTune.landingLength, FallDrift(drop) + GravityTune.landingStop);

        /// <summary>Wall Walk: gravity turns sideways, onto a tall wall that players run along.</summary>
        void WallWalk(float d) {
            var g = GravityTune;
            var t = Tune.wallWalk;
            float halfWidth = g.pathHalfWidth, thickness = g.thickness, overrun = g.overrun;
            float side = Sign();
            // The wall stands clear of the approach so a player turning over beside it does not
            // swing their feet into it; they fall the rest of the way.
            float wallGap = m.bodyHeight * t.wallGapShare;
            float face = side * (halfWidth + wallGap);
            float bottom = t.bottom, top = Mathf.Max(m.bodyHeight + t.topOverBody, t.top.At(d)), height = top - bottom;
            GravityApproach(halfWidth);

            // Falling across onto the wall carries the player forward, so the first obstacle comes after
            // room to land. Obstacles (gaps in the wall or lava strips across it) sit a running jump
            // apart, so clearing one never lands the player on the next.
            float settle = FallDrift(halfWidth * 2f + wallGap);
            float spacing = m.FlatJumpReach() + g.obstacleRoom;
            int obstacles = t.obstacles.Steps(d);
            float first = GravityFieldStart + settle + g.settleRoom;
            float wallEnd = first + (obstacles - 1) * spacing + g.tail.At(d) + overrun;
            float wallX = face + side * thickness * 0.5f;
            float z = GravityFieldStart;
            for (int i = 0; i < obstacles; i++) {
                float centre = first + i * spacing;
                if (Chance(t.gapChance)) {
                    float gap = t.gap.At(d);
                    WallSegment(wallX, top, height, thickness, z, centre - gap * 0.5f);
                    z = centre + gap * 0.5f;
                    CheckJump(gap, 0f);
                } else {
                    float strip = t.strip.At(d);
                    Box(OB_PieceKind.Lava, new Vector3(face - side * LavaStripThickness * 0.5f, top, centre), new Vector3(LavaStripThickness, height, strip), standable: false);
                    CheckJump(strip, 0f);
                }
            }
            WallSegment(wallX, top, height, thickness, z, wallEnd);
            // Between the first two obstacles (or after the only one), near the wall's high edge.
            float coinZ = obstacles > 1 ? first + spacing * 0.5f : first + t.coinAfter;
            Coin(new Vector3(face - side * CoinLift, top - t.coinBelowTop, coinZ));

            float fieldEnd = wallEnd - overrun;
            float near = -side * halfWidth;
            Field(new Vector3(Mathf.Min(near, face), bottom + 0.5f, GravityFieldStart),
                new Vector3(Mathf.Max(near, face), top, fieldEnd), new Vector3(side, 0f, 0f));

            // Leaving the field drops the player down the wall face onto this landing.
            float landingWidth = halfWidth + Mathf.Abs(face);
            float landingLength = GravityLanding(height);
            var landing = new Vector3((near + face) * 0.5f, bottom, fieldEnd + landingLength * 0.5f);
            Platform(landing, new Vector3(landingWidth, 1f, landingLength));
            Land(landing, new Vector2(landingWidth, landingLength));
            Par(fieldEnd / m.walkSpeed + g.parExtra);
        }

        /// <summary>Ceiling Run: gravity flips upward and players run upside down along a ceiling.</summary>
        void CeilingRun(float d) {
            var g = GravityTune;
            var t = Tune.ceilingRun;
            float thickness = g.thickness, overrun = g.overrun;
            float halfWidth = t.halfWidth.At(d);
            // High enough to walk under upright, low enough that the flip is a short fall.
            float height = m.bodyHeight + t.headroom.At(d);
            GravityApproach(halfWidth);

            // Lava hanging under the ceiling. Falling up onto the ceiling carries the player forward, so the
            // first patch comes after room to land; patches sit a full running jump apart, so a jump over
            // one never comes down on the next.
            int patches = t.patches.Steps(d);
            float patch = t.patch.At(d);
            float spacing = m.FlatJumpReach() + patch + g.obstacleRoom;
            float first = GravityFieldStart + Mathf.Max(t.firstPatchMin, FallDrift(height) + g.landingStop + patch * 0.5f);
            for (int i = 0; i < patches; i++) {
                Box(OB_PieceKind.Lava, new Vector3(0f, height, first + i * spacing), new Vector3(halfWidth * 2f, LavaSheetThickness, patch), standable: false);
                CheckJump(patch, 0f);
            }

            float start = GravityFieldStart - 0.5f;
            float fieldEnd = first + (patches - 1) * spacing + g.tail.At(d), end = fieldEnd + overrun;
            Box(OB_PieceKind.Platform, new Vector3(0f, height + thickness, (start + end) * 0.5f),
                new Vector3(halfWidth * 2f, thickness, end - start), standable: false);
            Coin(new Vector3(0f, height - CoinLift, fieldEnd - t.coinBeforeEnd)); // past the last patch, still under the ceiling
            Field(new Vector3(-halfWidth, -1f, GravityFieldStart), new Vector3(halfWidth, height, fieldEnd), Vector3.up);

            float landingLength = t.landingLength;
            var landing = new Vector3(0f, 0f, fieldEnd + landingLength * 0.5f);
            Platform(landing, new Vector3(t.landingWidth, 1f, landingLength));
            Land(landing, new Vector2(t.landingWidth, landingLength));
            Par(fieldEnd / m.walkSpeed + g.parExtra);
        }

        /// <summary>
        /// Corkscrew: an open square tube whose gravity rolls a quarter turn per section, from the floor to
        /// one wall, the ceiling and the other wall, before dropping you back onto a floor. Each section has
        /// only the face it pulls toward, so the camera sees in from the open sides; lava strips across the
        /// faces have to be jumped in the rolled frame.
        /// </summary>
        void Corkscrew(float d) {
            var t = Tune.corkscrew;
            var g = GravityTune;
            var shape = Tune.gravityShapes;
            float thickness = g.thickness, approachLength = shape.corkscrewApproach;
            // Room to stand on any face with the body's height to spare.
            float half = Mathf.Max(m.bodyHeight * 0.5f + shape.corkscrewFaceRoom, t.halfWidth.At(d));
            float spin = Sign();
            Hop(new Vector2(half * 2f, approachLength), 0f, 0f, Tension(Tune.entryTension));
            Vector3 floor = lastTop;
            float z = floor.z + approachLength * 0.5f;
            float cx = floor.x, y0 = floor.y;
            // Falling from one face to the next drifts the player forward; strips come after that.
            float drift = FallDrift(half * 2f - m.bodyHeight * 0.5f);
            int strips = d > t.twoStripsFromDifficulty ? 2 : 1;
            float strip = t.strip.At(d), spacing = m.FlatJumpReach() + strip + g.obstacleRoom;
            float sectionLength = drift + g.settleRoom + strip * 0.5f + (strips - 1) * spacing + strip * 0.5f + m.FlatJumpReach() + shape.corkscrewSectionRoom;
            Vector3[] downs = { new(spin, 0f, 0f), Vector3.up, new(-spin, 0f, 0f) };
            for (int s = 0; s < downs.Length; s++) {
                Vector3 down = downs[s];
                float zc = z + sectionLength * 0.5f;
                if (down.y > 0f) {
                    Box(OB_PieceKind.Platform, new Vector3(cx, y0 + half * 2f + thickness, zc), new Vector3(half * 2f, thickness, sectionLength), standable: false);
                    Coin(new Vector3(cx, y0 + half * 2f - CoinLift, zc));
                } else {
                    Box(OB_PieceKind.Platform, new Vector3(cx + down.x * (half + thickness * 0.5f), y0 + half * 2f, zc),
                        new Vector3(thickness, half * 2f, sectionLength), standable: false);
                }
                for (int i = 0; i < strips; i++) {
                    float centre = z + drift + g.settleRoom + strip * 0.5f + i * spacing;
                    if (down.y > 0f)
                        Box(OB_PieceKind.Lava, new Vector3(cx, y0 + half * 2f, centre), new Vector3(half * 2f, LavaSheetThickness, strip), standable: false);
                    else
                        Box(OB_PieceKind.Lava, new Vector3(cx + down.x * (half - LavaStripThickness * 0.5f), y0 + half * 2f, centre), new Vector3(LavaStripThickness, half * 2f, strip), standable: false);
                    CheckJump(strip, 0f);
                }
                Field(new Vector3(cx - half, y0, z), new Vector3(cx + half, y0 + half * 2f, z + sectionLength), down);
                z += sectionLength;
            }
            // Out of the last field gravity is normal again: drop onto the floor.
            float landingLength = GravityLanding(half * 2f);
            var landing = new Vector3(cx, y0, z + landingLength * 0.5f);
            Platform(landing, new Vector3(half * 2f, 1f, landingLength));
            Land(landing, new Vector2(half * 2f, landingLength));
            Par(sectionLength * downs.Length / m.walkSpeed + downs.Length * shape.corkscrewParPerTurn);
        }

        /// <summary>
        /// Flip Gap: a floor gap far too long to jump. A flip arrow throws you up onto a ceiling that
        /// spans it (with lava to hop on the hard tiers) and a second arrow on the ceiling drops you onto
        /// the far floor. Later, a floating orb flips you in mid-jump onto a second ceiling. Flips last
        /// until the next arrow (or a respawn), so the stage gets an upper kill height.
        /// </summary>
        void GravityFlip(float d) {
            var t = Tune.flip;
            var g = GravityTune;
            var shape = Tune.gravityShapes;
            float thickness = g.thickness, runway = shape.flipRunway, triggerDepth = shape.arrowDepth;
            float landingLength = shape.flipLanding, ceilingBack = shape.flipCeilingBack;
            float width = t.width.At(d);
            float height = m.bodyHeight + t.headroom.At(d);
            // The gap under the ceiling must be impossible to jump, even sprinting.
            float forcedGap = m.sprintSpeed * m.AirTime() * shape.flipForcedGap;
            Hop(new Vector2(width, runway), 0f, 0f, Tension(Tune.entryTension));
            Vector3 floor = lastTop;
            float floorEnd = floor.z + runway * 0.5f;
            FlipPad(new Vector3(floor.x, floor.y + 1f, floorEnd - 1.5f), width, Vector3.up, false);
            int ceilings = d > t.twoCeilingsFromDifficulty ? 2 : 1;
            for (int c = 0; c < ceilings; c++) {
                float y = floor.y;
                float start = floorEnd - ceilingBack;
                int patches = d > t.lavaFromDifficulty ? shape.flipPatches.Steps(d) : 0;
                float patch = t.patch.At(d), spacing = m.FlatJumpReach() + patch + g.obstacleRoom;
                float firstPatch = floorEnd + FallDrift(height - m.bodyHeight) + g.settleRoom + patch * 0.5f;
                for (int i = 0; i < patches; i++) {
                    Box(OB_PieceKind.Lava, new Vector3(floor.x, y + height, firstPatch + i * spacing), new Vector3(width, LavaSheetThickness, patch), standable: false);
                    CheckJump(patch, 0f);
                }
                float lastPatchEnd = patches > 0 ? firstPatch + (patches - 1) * spacing + patch * 0.5f : floorEnd;
                float end = Mathf.Max(lastPatchEnd + m.FlatJumpReach() + g.obstacleRoom, floorEnd + forcedGap + ceilingBack);
                Box(OB_PieceKind.Platform, new Vector3(floor.x, y + height + thickness, (start + end) * 0.5f),
                    new Vector3(width, thickness, end - start), standable: false);
                maxCeiling = Mathf.Max(maxCeiling, World(new Vector3(0f, y + height, 0f)).y);
                if (c == 0) Coin(new Vector3(floor.x, y + height - CoinLift, (start + end) * 0.5f));
                // The arrow back down, hanging under the far end of the ceiling.
                FlipPad(new Vector3(floor.x, y + height - 1f, end - triggerDepth), width, Vector3.down, false);
                var landing = new Vector3(floor.x, y, end - ceilingBack + landingLength * 0.5f);
                Platform(landing, new Vector3(width, 1f, landingLength));
                Land(landing, new Vector2(width, landingLength));
                Par((end - start) / m.walkSpeed + shape.flipParExtra);
                floor = landing;
                floorEnd = landing.z + landingLength * 0.5f;
                if (c + 1 < ceilings) {
                    // A floating orb just past the edge flips a jumper up in mid-air.
                    FlipPad(new Vector3(floor.x, floor.y + m.bodyHeight * 0.5f + m.jumpHeight * shape.orbJumpShare, floorEnd + shape.orbAhead), shape.orbSize, Vector3.up, true);
                }
            }
            flips = true;
        }

        /// <summary>A flip arrow trigger at a local point; <paramref name="orb"/> floats in the air instead of sitting on a surface.</summary>
        void FlipPad(Vector3 centre, float width, Vector3 localDown, bool orb) {
            float depth = Tune.gravityShapes.arrowDepth, height = Tune.gravityShapes.arrowHeight;
            Add(new OB_Piece {
                kind = OB_PieceKind.GravityFlip, position = World(centre), yaw = yaw,
                size = orb ? Vector3.one * width : new Vector3(width, height, depth),
                vector = WorldDirection(localDown), group = orb ? 1 : 0
            });
        }
    }
}
