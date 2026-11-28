using System.Collections.Generic;
using UnityEngine;

namespace Universes.UniverseData.dot_invaders {
    /// <summary>
    /// Each team's share of the board's power (<see cref="DI_TeamPower.Power"/>) over one match, sampled from
    /// the snapshots the client receives, for the match timeline drawn when the match ends. Keeps at most
    /// <see cref="MaxSamples"/> samples: when full it halves its resolution, so a long match still fits.
    /// Pure data; nothing here changes the game.
    /// </summary>
    public sealed class DI_PowerHistory {
        public const int DefaultMaxSamples = 160;

        readonly List<float> times = new();
        // shares[sample][teamIndex]; teams[teamIndex] is the team id.
        readonly List<float[]> shares = new();
        readonly List<int> teams = new();
        float interval;
        float nextSampleAt;

        public DI_PowerHistory(float sampleSeconds = 2f, int maxSamples = DefaultMaxSamples) {
            interval = Mathf.Max(0.01f, sampleSeconds);
            MaxSamples = Mathf.Max(4, maxSamples);
        }

        public int MaxSamples { get; }
        public int Count => times.Count;
        /// <summary>Every team seen this match, in the order their bands stack.</summary>
        public IReadOnlyList<int> Teams => teams;
        /// <summary>Match seconds of the last sample (the timeline's length).</summary>
        public float Duration => times.Count == 0 ? 0f : times[times.Count - 1];

        public float TimeAt(int sample) => times[sample];

        /// <summary>Share (0-1) of <see cref="Teams"/>[<paramref name="teamIndex"/>] at <paramref name="sample"/>.</summary>
        public float ShareAt(int sample, int teamIndex) {
            float[] row = shares[sample];
            return teamIndex < row.Length ? row[teamIndex] : 0f;
        }

        public void Clear() {
            times.Clear();
            shares.Clear();
            teams.Clear();
            nextSampleAt = 0f;
        }

        /// <summary>
        /// Adds a sample at <paramref name="matchSeconds"/> when one is due (always the first one, and
        /// <paramref name="force"/> for the final snapshot). Returns true when a sample was added.
        /// </summary>
        public bool Record(float matchSeconds, IDictionary<int, DI_TeamPower> power, bool force = false) {
            if (power == null || !force && times.Count > 0 && matchSeconds < nextSampleAt)
                return false;
            if (times.Count > 0 && matchSeconds <= times[times.Count - 1])
                return false;
            long total = 0;
            foreach (KeyValuePair<int, DI_TeamPower> entry in power) {
                total += entry.Value.Power;
                if (!teams.Contains(entry.Key))
                    teams.Add(entry.Key);
            }
            var row = new float[teams.Count];
            if (total > 0)
                for (int i = 0; i < teams.Count; i++)
                    if (power.TryGetValue(teams[i], out DI_TeamPower team))
                        row[i] = (float)(team.Power / (double)total);
            times.Add(matchSeconds);
            shares.Add(row);
            nextSampleAt = matchSeconds + interval;
            if (times.Count > MaxSamples)
                Thin();
            return true;
        }

        /// <summary>Drops every other sample (keeping the first and last) and doubles the interval.</summary>
        void Thin() {
            int last = times.Count - 1;
            for (int i = last - 1; i > 0; i -= 2) {
                times.RemoveAt(i);
                shares.RemoveAt(i);
            }
            interval *= 2f;
        }

        /// <summary>The highest share <paramref name="team"/> reached and when; false if it never appears.</summary>
        public bool TryPeak(int team, out float share, out float time) {
            share = 0f;
            time = 0f;
            int index = teams.IndexOf(team);
            if (index < 0)
                return false;
            for (int i = 0; i < times.Count; i++) {
                float value = ShareAt(i, index);
                if (value > share) {
                    share = value;
                    time = times[i];
                }
            }
            return true;
        }

        /// <summary>Share a challenger must lead by before it counts as taking the lead, so two close teams trading
        /// the top spot back and forth by a troop or two do not count as dozens of lead changes.</summary>
        public const float DefaultLeadMargin = 0.03f;

        /// <summary>
        /// Times a different team took the lead: by more than <paramref name="margin"/> of the board's power over
        /// the current leader (ties and near-ties keep the previous leader).
        /// </summary>
        public int LeadChanges(float margin = DefaultLeadMargin) {
            int changes = 0, leader = -1;
            for (int i = 0; i < times.Count; i++) {
                int best = leader;
                float bestShare = leader >= 0 ? ShareAt(i, leader) + Mathf.Max(0f, margin) : -1f;
                for (int t = 0; t < teams.Count; t++)
                    if (ShareAt(i, t) > bestShare) {
                        best = t;
                        bestShare = ShareAt(i, t);
                    }
                if (leader >= 0 && best != leader)
                    changes++;
                leader = best;
            }
            return changes;
        }
    }
}
