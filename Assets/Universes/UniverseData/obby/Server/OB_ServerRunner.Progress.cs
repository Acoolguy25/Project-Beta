using System;
using System.Collections.Generic;
using FishNet.Connection;
using RyanAssets.DataService;
using RyanAssets.Platformer;
using RyanAssets.Retention;
using RyanAssets.Server.ServerCore;
using UnityEngine;

namespace Universes.UniverseData.obby.Server {
    // Progress inside a race: pickups, the rising-lava chase, tier splits and the retention rewards.
    public sealed partial class OB_ServerRunner {
        /// <summary>A rising-lava segment of the course, as the server runs it for each player.</summary>
        readonly struct LavaZone {
            public readonly Vector3 centre;
            public readonly float yaw, restTop, speed, delay, travel;
            public readonly Vector2 footprint;

            public LavaZone(in OB_Piece piece) {
                centre = piece.position;
                yaw = piece.yaw;
                restTop = piece.position.y + piece.size.y * 0.5f;
                speed = piece.speed;
                delay = piece.period;
                travel = piece.vector.y;
                footprint = new Vector2(piece.size.x, piece.size.z);
            }

            public float TopAt(float elapsed) => restTop + RisingLava.Rise(elapsed, delay, speed, travel);
        }

        /// <summary>The rising lava of each stage (index = stage), empty for stages without one.</summary>
        readonly List<List<LavaZone>> lavaByStage = new();
        readonly List<RewardLine> lines = new();

        /// <summary>Reads what the server needs per stage from a freshly built course.</summary>
        void IndexCourse() {
            timedHazards.Collect(course.Root);
            lavaByStage.Clear();
            for (int s = 0; s < layout.StageCount; s++) lavaByStage.Add(new List<LavaZone>());
            foreach (var piece in layout.pieces)
                if (piece.kind == OB_PieceKind.RisingLava && piece.stage < layout.StageCount) lavaByStage[piece.stage].Add(new LavaZone(piece));
        }

        // ---------------------------------------------------------------- pickups
        /// <summary>
        /// Picks up the coins and stars the player touches. Only the stages they could be touching are
        /// tested: the reached stage they are over and its neighbours, and their own stage and the next.
        /// </summary>
        void CollectPickups(NetworkConnection connection, Runner runner, Vector3 centre) {
            int at = layout.StageAt(runner.checkpoint, centre), own = runner.checkpoint;
            for (int s = at - 1; s <= at + 1; s++) CollectStagePickups(connection, runner, centre, s);
            for (int s = own; s <= own + 1; s++)
                if (s < at - 1 || s > at + 1) CollectStagePickups(connection, runner, centre, s);
        }

        void CollectStagePickups(NetworkConnection connection, Runner runner, Vector3 centre, int s) {
            if (s < 0 || s >= layout.StageCount || s >= 64) return;
            ulong bit = 1UL << s;
            OB_Stage stage = layout.stages[s];
            if ((runner.coins & bit) == 0 && (centre - stage.coin).sqrMagnitude <= CoinRadius * CoinRadius) {
                runner.coins |= bit;
                runner.lastEventTime = RaceTime;
                retention.Count(connection, OB_Retention.CoinsStat);
                PlayerProgress.Modify<OB_SaveData>(connection, save => save.totalCoins++);
                if (PlayerData.TryGetPlayerData(connection, out var player)) WriteLeaderboard(player);
                SendProgress(connection, OB_ProgressReason.Coin);
            }
            if (stage.hasStar && (runner.stars & bit) == 0 && (centre - stage.star).sqrMagnitude <= StarRadius * StarRadius) {
                runner.stars |= bit;
                runner.lastEventTime = RaceTime;
                int runStars = OB_Scoring.CountCoins(runner.stars);
                retention.AddXp(connection, starXP, "Stars");
                retention.Count(connection, OB_Retention.StarsStat);
                PlayerProgress.Modify<OB_SaveData>(connection, save => save.RecordStar(runStars));
                SendProgress(connection, OB_ProgressReason.Star);
            }
        }

        // ---------------------------------------------------------------- rising lava
        /// <summary>
        /// Runs the player's lava chase on the stage they are playing: it starts when they are over the
        /// lava's footprint and restarts on a respawn or a new checkpoint, as on their client. True when
        /// the lava has caught them.
        /// </summary>
        bool InRisingLava(Runner runner, Bounds body) {
            if (runner.finished || runner.checkpoint >= lavaByStage.Count) return false;
            var zones = lavaByStage[runner.checkpoint];
            foreach (var zone in zones) {
                if (!RisingLava.IsOver(body.center, zone.centre, zone.yaw, zone.footprint)) continue;
                if (runner.lavaStart < 0f) {
                    runner.lavaStart = Time.time;
                    return false;
                }
                return body.min.y < zone.TopAt(Time.time - runner.lavaStart) - lavaMargin;
            }
            return false;
        }

        // ---------------------------------------------------------------- stages and tiers
        /// <summary>Challenge stats for clearing <paramref name="stage"/> (its mechanics).</summary>
        void CountStageCleared(NetworkConnection connection, OB_Stage stage) {
            if (Array.Exists(stage.segments, OB_CourseGenerator.IsGravity)) retention.Count(connection, OB_Retention.GravityStat);
            if (Array.IndexOf(stage.segments, OB_StageKind.LavaRise) >= 0) retention.Count(connection, OB_Retention.LavaStat);
        }

        /// <summary>
        /// Closes <paramref name="tier"/> for a runner who just left it: saves the tier time and stars,
        /// pays a tier record and counts clean and perfect tiers. The next progress message carries the split.
        /// </summary>
        void CloseTier(NetworkConnection connection, Runner runner, OB_Tier tier) {
            int first = OB_Tiers.FirstStage((int)tier, layout.StageCount), next = OB_Tiers.FirstStage((int)tier + 1, layout.StageCount);
            ulong mask = StageMask(first, next);
            int stars = OB_Scoring.CountCoins(runner.stars & mask);
            bool clean = runner.tierClean;
            bool perfect = (runner.coins & mask) == mask && stars == layout.StarsInTier(tier);
            float seconds = RaceTime - runner.tierStart;
            float previous = 0f;
            PlayerProgress.Modify<OB_SaveData>(connection, save => previous = save.RecordTier(tier, seconds, stars, clean, perfect));
            runner.closedTier = tier;
            runner.closedTierTime = seconds;
            runner.closedTierBest = previous;
            if (previous > 0f && seconds < previous) retention.AddXp(connection, tierRecordXP, "Tier records");
            if (clean) retention.Count(connection, OB_Retention.CleanTierStat);
            if (perfect) retention.Count(connection, OB_Retention.PerfectTierStat);
        }

        /// <summary>Bits for stages <paramref name="first"/> (inclusive) to <paramref name="next"/> (exclusive), within the 64 a mask holds.</summary>
        static ulong StageMask(int first, int next) {
            ulong mask = 0;
            for (int s = Mathf.Max(0, first); s < next && s < 64; s++) mask |= 1UL << s;
            return mask;
        }

        // ---------------------------------------------------------------- retention
        /// <summary>Opens the runner's end-of-race summary (once per race).</summary>
        void BeginMatch(NetworkConnection connection, Runner runner) {
            if (runner.inMatch) return;
            runner.inMatch = true;
            // A racer joining mid-race times their first tier from now.
            runner.tierStart = RaceTime;
            retention.BeginMatch(connection);
        }

        /// <summary>
        /// Wins and podiums go by the final results, after every bonus is known. A race alone is no
        /// contest: the finish reward and medal still count, places do not.
        /// </summary>
        void RecordPlaces(List<KeyValuePair<NetworkConnection, Runner>> final) {
            if (final.Count < 2) return;
            for (int i = 0; i < final.Count; i++) {
                if (!final[i].Value.finished) break;
                int place = i + 1;
                final[i].Value.place = place;
                if (place <= 3) retention.Count(final[i].Key, OB_Retention.PodiumStat);
                PlayerProgress.Modify<OB_SaveData>(final[i].Key, save => save.RecordPlace(place));
            }
        }

        /// <summary>Pays every racer's end-of-race rewards through the retention kit, which shows them on the summary card.</summary>
        void FinishMatches(List<KeyValuePair<NetworkConnection, Runner>> final) {
            bool contest = final.Count >= 2;
            foreach (var (connection, runner) in final) {
                if (!runner.inMatch) continue;
                runner.inMatch = false;
                lines.Clear();
                int coins = OB_Scoring.CountCoins(runner.coins), stars = OB_Scoring.CountCoins(runner.stars);
                lines.Add(runner.finished
                    ? RewardLine.Info("Time", OB_Scoring.Clock(runner.scoredTime, true) + (runner.personalBest ? "  (new best)" : ""))
                    : RewardLine.Info("Reached", $"stage {runner.checkpoint + 1} of {layout.StageCount}"));
                if (layout.StarCount > 0) lines.Add(RewardLine.Info("Stars", $"{stars} / {layout.StarCount}"));
                if (coins > 0) lines.Add(new RewardLine("Coins", 0, coinGold * coins, coins));
                if (runner.finished) lines.Add(new RewardLine("Course complete", finishXP, finishGold));
                if (runner.medal != OB_Medal.None) lines.Add(new RewardLine($"{runner.medal} medal", MedalXp(runner.medal)));
                if (contest && runner.finished && runner.place >= 1 && runner.place <= podiumGold.Length)
                    lines.Add(new RewardLine(runner.place == 1 ? "Race won" : $"{OB_Scoring.Ordinal(runner.place)} place", 0, podiumGold[runner.place - 1]));
                string title = !runner.finished ? $"Stage {runner.checkpoint + 1} of {layout.StageCount}"
                    : runner.medal != OB_Medal.None ? $"{runner.medal} medal!" : "Course complete!";
                retention.FinishMatch(connection, title, lines);
            }
        }

        int MedalXp(OB_Medal medal) {
            int index = OB_Medal.Gold - medal;
            return medalXP != null && index >= 0 && index < medalXP.Length ? medalXP[index] : 0;
        }
    }
}
