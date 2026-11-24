using System;
using System.Collections.Generic;
using RyanAssets.Retention;
using UnityEngine;

namespace Universes.UniverseData.war_valley.Shared {
    /// <summary>The game XP and account gold a round pays, line by line on the summary card.</summary>
    [Serializable]
    public sealed class WV_CampaignRewards {
        [Tooltip("XP for every wave held, paid as it is held.")]
        [Min(0)] public int XpPerWaveHeld = 15;

        [Tooltip("XP for each wave enemy your forces defeat, paid at the end of the round.")]
        [Min(0)] public int XpPerKill = 1;

        [Tooltip("XP and gold for winning a round.")]
        [Min(0)] public int VictoryXp = 250;
        [Min(0)] public int VictoryGold = 40;

        [Tooltip("XP for each star a win earns.")]
        [Min(0)] public int XpPerStar = 40;

        [Tooltip("XP and gold the first time a tier is won.")]
        [Min(0)] public int FirstWinXp = 200;
        [Min(0)] public int FirstWinGold = 60;

        [Tooltip("XP for each wave held in an Endless round, paid at its end on top of the per-wave XP.")]
        [Min(0)] public int EndlessXpPerWave = 10;

        [Tooltip("Share of a round's waves a commander must be present for (held while they were in the " +
                 "game) for its win, stars, tier unlock and Endless record to count for them. A late " +
                 "joiner still earns the XP of the waves they fought.")]
        [Range(0f, 1f)] public float WinAttendanceShare = 0.5f;
    }

    /// <summary>
    /// Who was there: the waves a commander was present for decide whether a round's win counts for
    /// them, so nobody earns a tier, stars or a record by joining for the last wave, and a commander
    /// who leaves and comes back keeps the waves they fought. Pure, so the rule is tested.
    /// </summary>
    public static class WV_Attendance {
        /// <summary>Waves of a <paramref name="roundWaves"/>-wave round a commander must be present for.</summary>
        public static int WavesNeeded(int roundWaves, float share) =>
            Mathf.Max(1, Mathf.CeilToInt(Mathf.Max(0, roundWaves) * Mathf.Clamp01(share) - 1e-4f));

        /// <summary>Whether a commander present for <paramref name="wavesPresent"/> waves gets the round's win.</summary>
        public static bool WinCounts(int wavesPresent, int roundWaves, float share) =>
            wavesPresent >= WavesNeeded(roundWaves, share);
    }

    /// <summary>One commander's finished round, as the summary card needs it.</summary>
    public struct WV_RoundOutcome {
        public WV_DifficultyTier Tier;
        public bool Victory;
        /// <summary>The wave the round ended on.</summary>
        public int WaveReached;
        /// <summary>Waves in a standard (not Endless) round.</summary>
        public int WaveCount;
        /// <summary>Waves the round held in all.</summary>
        public int Held;
        /// <summary>Waves this commander was present for.</summary>
        public int WavesPresent;
        public int Kills;
        public int Stars;
        public float LowestFlagFraction;
        public bool FirstWin;
        public int BestEndless;
        public int Doctrines;
        public string NextGoal;
        public WV_Front Front;

        public bool IsEndless => Tier == WV_DifficultyTier.Endless;
    }

    /// <summary>
    /// The lines of a commander's summary card, built from the round's outcome: a pinned headline
    /// (the win, the Endless wave or the wave the flag fell on), the stars, the enemies they
    /// defeated, a first win, the tier and front bonuses, their doctrines and their next goal. Pure,
    /// so the pacing tests read the same numbers the server pays.
    /// </summary>
    public static class WV_RoundSummary {
        /// <summary>XP the round paid while it was played (per wave held while present), before the card.</summary>
        public static int LiveXp(int held, WV_CampaignRewards rewards) => Mathf.Max(0, held) * rewards.XpPerWaveHeld;

        /// <summary>Whether the round's win, stars and records count for this commander.</summary>
        public static bool Counts(in WV_RoundOutcome outcome, WV_CampaignRewards rewards) =>
            WV_Attendance.WinCounts(outcome.WavesPresent, outcome.IsEndless ? outcome.WaveReached : outcome.WaveCount,
                rewards.WinAttendanceShare);

        public static string Title(in WV_RoundOutcome outcome, WV_DifficultyTuning tiers, bool counts) {
            string name = tiers.Get(outcome.Tier).DisplayName;
            if (outcome.Victory)
                return counts ? $"Victory on {name}!" : "Victory (joined late)";
            return outcome.IsEndless ? $"Endless: held {outcome.Held} waves" : "The flag has fallen";
        }

        public static List<RewardLine> Build(in WV_RoundOutcome outcome, WV_CampaignRewards rewards, WV_DifficultyTuning tiers,
            WV_FrontTuning fronts) {
            var lines = new List<RewardLine>();
            WV_DifficultyProfile profile = tiers.Get(outcome.Tier);
            bool counts = Counts(outcome, rewards);
            // Per-wave XP was paid live for the waves this commander was there for.
            int earned = LiveXp(outcome.WavesPresent, rewards);

            if (outcome.Victory && counts) {
                lines.Add(RewardLine.Headline("Victory", rewards.VictoryXp, rewards.VictoryGold, profile.DisplayName));
                earned += rewards.VictoryXp;
            } else if (outcome.Victory) {
                int needed = WV_Attendance.WavesNeeded(outcome.WaveCount, rewards.WinAttendanceShare);
                lines.Add(RewardLine.Info("Joined late", $"{outcome.WavesPresent} of {outcome.WaveCount} waves; a win needs {needed}")
                    .AsHeadline());
            } else if (outcome.IsEndless) {
                int xp = rewards.EndlessXpPerWave * Mathf.Max(0, outcome.WavesPresent);
                string best = counts ? $"Best: wave {outcome.BestEndless}" : "Joined late: no record";
                lines.Add(RewardLine.Headline($"Endless: reached wave {outcome.WaveReached}", xp, 0, best));
                earned += xp;
            } else {
                lines.Add(RewardLine.Info("Fell on wave", $"{outcome.WaveReached} of {outcome.WaveCount}").AsHeadline());
            }

            if (outcome.Victory && counts && outcome.Stars > 0) {
                int xp = outcome.Stars * rewards.XpPerStar;
                // Short of 3 stars, the line says what the third needs, so the next round has a target.
                string toThree = outcome.Stars < WV_Campaign.MaxStars
                    ? $"; 3 stars: keep it above {Mathf.RoundToInt(tiers.ThreeStarFlagFraction * 100f)}%"
                    : string.Empty;
                lines.Add(RewardLine.Headline("Stars", xp, 0,
                    $"{WV_Campaign.StarText(outcome.Stars)} - the flag kept {Mathf.FloorToInt(outcome.LowestFlagFraction * 100f)}%{toThree}"));
                earned += xp;
            }

            if (outcome.Kills > 0) {
                int xp = outcome.Kills * rewards.XpPerKill;
                lines.Add(new RewardLine("Enemies you defeated", xp, 0, outcome.Kills));
                earned += xp;
            }

            if (outcome.FirstWin && counts) {
                string unlocked = outcome.Tier switch {
                    WV_DifficultyTier.Recruit => $"Unlocked {tiers.Veteran.DisplayName}",
                    WV_DifficultyTier.Veteran => $"Unlocked {tiers.Elite.DisplayName} and Endless",
                    WV_DifficultyTier.Elite => $"Unlocked {tiers.Nightmare.DisplayName}",
                    _ => "Top of the campaign"
                };
                lines.Add(new RewardLine($"First {profile.DisplayName} win", rewards.FirstWinXp, rewards.FirstWinGold)
                    .WithDetail(unlocked));
                earned += rewards.FirstWinXp;
            }

            int tierBonus = Mathf.RoundToInt(earned * Mathf.Max(0f, profile.XpMultiplier - 1f));
            if (tierBonus > 0)
                lines.Add(new RewardLine($"{profile.DisplayName} bonus", tierBonus)
                    .WithDetail($"+{Mathf.RoundToInt((profile.XpMultiplier - 1f) * 100f)}% XP"));

            WV_FrontEffect front = WV_DailyFront.Effect(outcome.Front, fronts);
            int frontBonus = Mathf.RoundToInt(earned * Mathf.Max(0f, front.XpBonus));
            if (frontBonus > 0)
                lines.Add(new RewardLine($"{WV_DailyFront.Title(outcome.Front)} bonus", frontBonus)
                    .WithDetail($"+{Mathf.RoundToInt(front.XpBonus * 100f)}% XP today"));

            if (outcome.Doctrines > 0)
                lines.Add(RewardLine.Info("Doctrines", $"{outcome.Doctrines} picked this round"));
            if (!string.IsNullOrEmpty(outcome.NextGoal))
                lines.Add(RewardLine.Info("Next goal", outcome.NextGoal));
            return lines;
        }

        /// <summary>All the game XP a round pays: the live per-wave XP plus every line of its card.</summary>
        public static int TotalXp(in WV_RoundOutcome outcome, WV_CampaignRewards rewards, WV_DifficultyTuning tiers, WV_FrontTuning fronts) {
            int total = LiveXp(outcome.WavesPresent, rewards);
            foreach (RewardLine line in Build(outcome, rewards, tiers, fronts))
                total += line.Xp;
            return total;
        }
    }
}
