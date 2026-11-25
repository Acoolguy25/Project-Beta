using System;
using System.Collections.Generic;
using RyanAssets.Core;
using UnityEngine;

namespace Universes.UniverseData.obby {
    public sealed partial class OB_CourseGenerator {
        // ---------------------------------------------------------------- stage plans
        /// <summary>Gravity mechanics: at most one per stage, so their fields never meet.</summary>
        public static bool IsGravity(OB_StageKind kind) => kind is OB_StageKind.MoonLeap or OB_StageKind.WallWalk
            or OB_StageKind.CeilingRun or OB_StageKind.Corkscrew or OB_StageKind.GravityFlip;

        /// <summary>The tier that introduces <paramref name="kind"/> (the last tier when no rule lists it).</summary>
        int IntroducedIn(OB_StageKind kind) {
            for (int t = 0; t < rules.tiers.Length; t++)
                if (rules.tiers[t].introduces != null && Array.IndexOf(rules.tiers[t].introduces, kind) >= 0) return t;
            return rules.tiers.Length - 1;
        }

        bool IsIntroduction(OB_StageKind kind, OB_Tier stageTier) => IntroducedIn(kind) == (int)stageTier && stageTier != OB_Tier.Basic;

        /// <summary>The segment a stage is named after: its newest mechanic, or its first.</summary>
        OB_StageKind Signature(OB_StageKind[] segments) {
            OB_StageKind best = segments[0];
            for (int i = 1; i < segments.Length; i++)
                if (IntroducedIn(segments[i]) > IntroducedIn(best)) best = segments[i];
            return best;
        }

        /// <summary>
        /// Picks every stage's segments up front. Mechanics used least so far come first, so a course
        /// shows its whole range; a tier's first stage opens with one of the mechanics it introduces; no
        /// stage repeats a mechanic or mixes two gravity mechanics, and no two stages in a row share a name.
        /// </summary>
        OB_StageKind[][] PlanSegments(int stageCount) {
            const int attempts = 8;
            var plan = new OB_StageKind[stageCount][];
            var uses = new Dictionary<OB_StageKind, int>();
            var pool = new List<OB_StageKind>();
            var picked = new List<OB_StageKind>();
            var best = new List<OB_StageKind>();
            OB_StageKind previous = (OB_StageKind)byte.MaxValue;
            for (int s = 0; s < stageCount; s++) {
                int t = (int)OB_Tiers.TierOf(s, stageCount);
                OB_TierRule rule = rules.tiers[t];
                pool.Clear();
                for (int k = 0; k <= t; k++)
                    if (rules.tiers[k].introduces != null) pool.AddRange(rules.tiers[k].introduces);
                int count = rule.segments.x + rng.Next(rule.segments.y - rule.segments.x + 1);
                bool opensTier = s == OB_Tiers.FirstStage(t, stageCount) && rule.introduces != null && rule.introduces.Length > 0;
                for (int attempt = 0; attempt < attempts; attempt++) {
                    picked.Clear();
                    if (s == 0) picked.Add(OB_StageKind.Hops); // the lobby run always starts with plain hops
                    else if (opensTier) picked.Add(LeastUsed(rule.introduces, picked, uses, previous));
                    // A retry leaves out the kind the last stage was named after.
                    var avoid = attempt > 0 ? previous : (OB_StageKind)byte.MaxValue;
                    while (picked.Count < count) {
                        var next = LeastUsed(pool, picked, uses, avoid);
                        if (picked.Contains(next)) break; // pool exhausted
                        picked.Add(next);
                    }
                    // Everything but an opening introduction plays in a shuffled order (which can change the name).
                    int fixedHead = s == 0 || opensTier ? 1 : 0;
                    for (int i = picked.Count - 1; i > fixedHead; i--) {
                        int j = fixedHead + rng.Next(i - fixedHead + 1);
                        (picked[i], picked[j]) = (picked[j], picked[i]);
                    }
                    if (Signature(picked.ToArray()) != previous || attempt == attempts - 1) break;
                }
                plan[s] = picked.ToArray();
                foreach (var kind in picked) uses[kind] = uses.TryGetValue(kind, out int n) ? n + 1 : 1;
                previous = Signature(plan[s]);
            }
            return plan;
        }

        /// <summary>The least-used kind of <paramref name="from"/> that fits beside <paramref name="picked"/> (random among ties).</summary>
        OB_StageKind LeastUsed(IReadOnlyList<OB_StageKind> from, List<OB_StageKind> picked, Dictionary<OB_StageKind, int> uses, OB_StageKind avoid) {
            var ties = new List<OB_StageKind>();
            int fewest = int.MaxValue;
            bool hasGravity = picked.Exists(IsGravity);
            foreach (var kind in from) {
                if (picked.Contains(kind) || kind == avoid || (hasGravity && IsGravity(kind)) || ties.Contains(kind)) continue;
                int n = uses.TryGetValue(kind, out int used) ? used : 0;
                if (n < fewest) { fewest = n; ties.Clear(); }
                if (n == fewest) ties.Add(kind);
            }
            if (ties.Count == 0) return picked.Count > 0 ? picked[0] : from[0];
            return ties[rng.Next(ties.Count)];
        }

        // ---------------------------------------------------------------- stars
        /// <summary>Spreads <see cref="OB_CourseRules.starsPerTier"/> stars over each tier's stages.</summary>
        sealed class StarPlan {
            readonly int perTier, stageCount;
            readonly int[] placed = new int[OB_Tiers.Count];

            public StarPlan(int perTier, int stageCount) {
                this.perTier = perTier;
                this.stageCount = stageCount;
            }

            int StagesIn(int tier) => OB_Tiers.FirstStage(tier + 1, stageCount) - OB_Tiers.FirstStage(tier, stageCount);

            /// <summary>Stars this tier should end up with: the rule's count, or one per stage on short courses.</summary>
            public int Target(OB_Tier tier) => Mathf.Min(perTier, StagesIn((int)tier));

            public bool IsFirstStageOfTier(int stage) => stage == OB_Tiers.FirstStage((int)OB_Tiers.TierOf(stage, stageCount), stageCount);

            /// <summary>Whether <paramref name="stage"/> should try for a star: always once the tier's remaining stages are needed.</summary>
            public bool Wants(OB_Tier tier, int stage, SeededStoryRandom rng) {
                int need = Target(tier) - placed[(int)tier];
                if (need <= 0) return false;
                int left = OB_Tiers.FirstStage((int)tier + 1, stageCount) - stage;
                return left <= need || rng.Next(left) < need;
            }

            public void Placed(OB_Tier tier) => placed[(int)tier]++;
        }

        /// <summary>
        /// Builds an optional pro route off the current surface: a sideways chain of small, high platforms
        /// at the tier's pro gap (harder than the main path), with a star on the last one. The player
        /// jumps back the same way. The chain stays beside the surface (never past its far edge, where the
        /// next segment starts) and clear of everything the stage has placed. Returns false when there is
        /// no room on either side.
        /// </summary>
        bool TryProRoute(out Vector3 star) {
            star = default;
            const float minimumBase = 3f, clearance = 1.2f, farEdgeKeepOut = SegmentBackReach + 1f;
            Vector3 baseTop = lastTop;
            Vector2 baseSize = lastSize;
            if (baseSize.x < minimumBase || baseSize.y < minimumBase) return false;
            int hops = tier.proGapFraction >= rules.Tier(OB_Tier.Hard).proGapFraction ? 3 : 2;
            float size = Mathf.Max(1.2f, tier.platformSize.x * 0.85f);
            float zMin = baseTop.z - baseSize.y * 0.5f + size * 0.5f;
            float zMax = baseTop.z + baseSize.y * 0.5f - farEdgeKeepOut - size * 0.5f;
            if (zMax < zMin) return false;
            var tops = new Vector3[hops];
            float side = Sign();
            for (int attempt = 0; attempt < 2; attempt++, side = -side) {
                Vector3 from = baseTop;
                Vector2 fromSize = baseSize;
                bool clear = true;
                for (int h = 0; h < hops && clear; h++) {
                    float rise = Range(0.3f, 0.9f) * MaxRise();
                    float gap = Mathf.Max(1f, Reach(rise) * tier.proGapFraction * Range(0.9f, 0.98f));
                    float x = from.x + side * (fromSize.x * 0.5f + gap + size * 0.5f);
                    float z = Mathf.Clamp(from.z + Range(-1f, 1f), zMin, zMax);
                    tops[h] = new Vector3(x, from.y + rise, z);
                    clear = IsClear(World(tops[h] - Vector3.up * 0.5f), new Vector3(size, 1f + 2f * m.bodyHeight, size), clearance);
                    from = tops[h];
                    fromSize = new Vector2(size, size);
                }
                if (!clear) continue;
                Land(baseTop, baseSize);
                foreach (var top in tops) {
                    Platform(top, new Vector3(size, 1f, size));
                    Step(top, new Vector2(size, size), pro: true);
                }
                star = World(tops[hops - 1] + Vector3.up * StarLift);
                Land(baseTop, baseSize);
                return true;
            }
            Land(baseTop, baseSize);
            return false;
        }

        /// <summary>Whether a box (world centre, local size, this stage's yaw) stays <paramref name="margin"/> clear of every solid piece placed so far.</summary>
        bool IsClear(Vector3 centre, Vector3 size, float margin) {
            var probe = new OB_Volume(centre, size, yaw);
            for (int i = layout.pieces.Count - 1; i >= 0; i--) {
                var piece = layout.pieces[i];
                if (piece.stage < stage - 1) break;
                if (OB_PieceVolumes.TryGet(piece, out OB_Volume volume) && probe.Overlaps(volume, margin)) return false;
            }
            return true;
        }
    }
}
