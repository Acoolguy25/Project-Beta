using System;
using System.Collections.Generic;
using UnityEngine;

namespace Universes.UniverseData.capture_the_flag {
    /// <summary>What one player or bot did in one match.</summary>
    [Serializable]
    public struct CTF_MatchStats {
        public int kills;
        public int deaths;
        public int captures;
        public int returns;
        public int flagTakes;
        public int carrierKills;
        public int defends;
        public int assists;
        public int medals;
    }

    /// <summary>How much each deed counts toward the MVP.</summary>
    [Serializable]
    public sealed class CTF_MvpWeights {
        [Min(0f)] public float capture = 10f;
        [Min(0f)] public float flagTake = 2f;
        [Min(0f)] public float returnFlag = 3f;
        [Min(0f)] public float kill = 1.5f;
        [Min(0f)] public float carrierKill = 3f;
        [Min(0f)] public float defend = 1f;
        [Min(0f)] public float assist = 0.5f;
        [Min(0f)] public float death = 0.5f;
        [Tooltip("An MVP needs at least this score, so a quiet draw names nobody.")]
        [Min(0f)] public float minimumScore = 3f;
    }

    /// <summary>Pure match scoring: the MVP score and who earns it.</summary>
    public static class CTF_Scoring {
        public static float Score(in CTF_MatchStats s, CTF_MvpWeights w) {
            w ??= new CTF_MvpWeights();
            return s.captures * w.capture + s.flagTakes * w.flagTake + s.returns * w.returnFlag + s.kills * w.kill
                + s.carrierKills * w.carrierKill + s.defends * w.defend + s.assists * w.assist - s.deaths * w.death;
        }

        /// <summary>
        /// Index of the MVP in <paramref name="stats"/> (the highest score, ties to fewer deaths, then the
        /// winning team's, then the first), or -1 when nobody reaches the minimum score.
        /// </summary>
        public static int Mvp(IReadOnlyList<CTF_MatchStats> stats, IReadOnlyList<bool> onWinningTeam, CTF_MvpWeights w) {
            w ??= new CTF_MvpWeights();
            int best = -1;
            float bestScore = float.NegativeInfinity;
            for (int i = 0; i < stats.Count; i++) {
                float score = Score(stats[i], w);
                if (score < w.minimumScore) continue;
                bool better = best < 0 || score > bestScore + 1e-4f;
                if (!better && Mathf.Abs(score - bestScore) <= 1e-4f) {
                    if (stats[i].deaths != stats[best].deaths) better = stats[i].deaths < stats[best].deaths;
                    else better = Won(onWinningTeam, i) && !Won(onWinningTeam, best);
                }
                if (!better) continue;
                best = i;
                bestScore = score;
            }
            return best;
        }

        static bool Won(IReadOnlyList<bool> winners, int i) => winners != null && i < winners.Count && winners[i];
    }
}
