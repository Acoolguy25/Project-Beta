using System;
using System.Collections.Generic;
using UnityEngine;

namespace Universes.UniverseData.hide_and_seek {
    /// <summary>
    /// Round points: what each play is worth. Points are paid as game XP as they are earned (so leaving
    /// mid-round keeps them), name the round's MVP and feed the "best round" record. Serialized on the
    /// server runner.
    /// </summary>
    [Serializable]
    public sealed class HS_ScoreTuning {
        [Header("Hiders")]
        [Tooltip("Points every Survival Interval seconds a hider stays hidden while the seekers hunt.")]
        [Min(0)] public int survivalPoints = 5;
        [Min(1f)] public float survivalInterval = 10f;
        [Tooltip("A seeker came this close (world units) and the hider got away...")]
        [Min(0f)] public float closeCallRange = 9f;
        [Tooltip("...then got this far away again without being tagged: a close call.")]
        [Min(0f)] public float closeCallEscapeRange = 22f;
        [Min(0)] public int closeCallPoints = 15;
        [Tooltip("Seconds before the same hider can score another close call.")]
        [Min(0f)] public float closeCallCooldown = 8f;
        [Tooltip("Close calls one hider can score in a round, so stepping in and out of an idle seeker's reach cannot be farmed.")]
        [Min(1)] public int closeCallsPerRound = 4;
        [Min(0)] public int tauntPoints = 3;
        [Min(0)] public int braveTauntPoints = 10;
        [Tooltip("A seeker walked up to your live decoy.")]
        [Min(0)] public int decoyFooledPoints = 12;
        [Tooltip("Paid once to the last hider left (of at least two).")]
        [Min(0)] public int lastHiderPoints = 25;
        [Tooltip("Paid at the end to every hider nobody found, with Survive Gold.")]
        [Min(0)] public int survivedPoints = 40;
        [Min(0)] public int survivedGold = 15;
        [Tooltip("Paid during a Shuffle event to a hider who reaches a different room in time.")]
        [Min(0)] public int shufflePoints = 20;

        [Header("Seekers")]
        [Min(0)] public int tagPoints = 25;
        [Tooltip("Extra points for the round's first tag.")]
        [Min(0)] public int firstFindPoints = 15;
        [Tooltip("A seeker's flashlight dazzled a hider someone else then tagged.")]
        [Min(0)] public int flashAssistPoints = 10;
        [Tooltip("Seconds after a dazzle wears off that a tag still counts as a flashlight assist.")]
        [Min(0f)] public float flashAssistGrace = 3f;
        [Tooltip("Paid at the end to the starting seekers when everyone was found.")]
        [Min(0)] public int seekerWinPoints = 30;
        [Tooltip("A seeker searched a room or yard they had not been in this round (where they start or were found is free), so a hunt " +
                 "that finds nobody still pays for the ground covered.")]
        [Min(0)] public int searchPoints = 4;
        [Tooltip("Rooms one seeker can be paid for in a round.")]
        [Min(0)] public int searchesPerRound = 8;

        [Header("End of round")]
        [Tooltip("XP for everyone who played the round, win or lose, so a round never ends at +0 (a seeker who found nobody).")]
        [Min(0)] public int playedXp = 15;
        [Tooltip("Bonus XP for the player with the most points.")]
        [Min(0)] public int mvpXp = 30;
        [Min(0)] public int mvpGold = 10;
        [Tooltip("Bonus XP per consecutive win (from the second win on), up to Win Streak Cap wins.")]
        [Min(0)] public int winStreakXpPerWin = 10;
        [Min(1)] public int winStreakCap = 5;
        [Tooltip("Game XP paid per round point.")]
        [Min(0f)] public float xpPerPoint = 1f;

        public int Xp(int points) => Mathf.Max(0, Mathf.RoundToInt(points * xpPerPoint));

        /// <summary>The streak bonus for a win that makes the streak <paramref name="streak"/> long (0 for the first win).</summary>
        public int WinStreakXp(int streak) => streak < 2 ? 0 : winStreakXpPerWin * Mathf.Min(streak, winStreakCap);

        /// <summary>Points for <paramref name="hiddenSeconds"/> spent hidden, counted in whole intervals.</summary>
        public int SurvivalPointsFor(float hiddenSeconds) => survivalInterval <= 0f ? 0 : Mathf.FloorToInt(Mathf.Max(0f, hiddenSeconds) / survivalInterval) * survivalPoints;

        public void Validate() {
            survivalInterval = Mathf.Max(1f, survivalInterval);
            closeCallRange = Mathf.Max(0f, closeCallRange);
            closeCallEscapeRange = Mathf.Max(closeCallRange, closeCallEscapeRange);
            closeCallsPerRound = Mathf.Max(1, closeCallsPerRound);
            winStreakCap = Mathf.Max(1, winStreakCap);
            xpPerPoint = Mathf.Max(0f, float.IsNaN(xpPerPoint) ? 1f : xpPerPoint);
        }
    }

    /// <summary>
    /// The rooms a seeker has searched this round. The first area they are seen in (where they start, or
    /// where they were found) is free; each new one after that pays, up to a cap. One per seeker per round.
    /// </summary>
    public sealed class HS_SearchLog {
        readonly HashSet<string> seen = new();

        /// <summary>Rooms paid for so far.</summary>
        public int Paid { get; private set; }

        /// <summary>The seeker is in <paramref name="area"/> now; true when that visit pays.</summary>
        public bool Visit(string area, int cap) {
            if (string.IsNullOrEmpty(area)) return false;
            bool first = seen.Count == 0;
            if (!seen.Add(area) || first || Paid >= cap) return false;
            Paid++;
            return true;
        }
    }

    /// <summary>
    /// A hider's close-call state: armed when a seeker comes within range, paid when the hider gets clear
    /// again while still hidden, at most <see cref="HS_ScoreTuning.closeCallsPerRound"/> times. One per hider per round.
    /// </summary>
    public struct HS_CloseCall {
        public bool armed;
        public float lastPaidAt;
        /// <summary>Close calls paid this round.</summary>
        public int paid;

        public static HS_CloseCall Fresh => new() { lastPaidAt = float.NegativeInfinity };

        /// <summary>Feeds the nearest seeker's distance at time <paramref name="now"/>; true when a close call completes.</summary>
        public bool Step(float nearestSeeker, float now, HS_ScoreTuning tuning) {
            if (paid >= tuning.closeCallsPerRound) return armed = false;
            if (nearestSeeker <= tuning.closeCallRange) {
                if (now - lastPaidAt >= tuning.closeCallCooldown) armed = true;
                return false;
            }
            if (!armed || nearestSeeker < tuning.closeCallEscapeRange) return false;
            armed = false;
            lastPaidAt = now;
            paid++;
            return true;
        }
    }

    /// <summary>
    /// Round points per player and the MVP: most points, ties going to whoever reached the score first.
    /// </summary>
    public sealed class HS_RoundLedger<TKey> {
        readonly Dictionary<TKey, (int points, long reachedAt)> scores = new();
        long sequence;

        public int Count => scores.Count;

        public void Clear() {
            scores.Clear();
            sequence = 0;
        }

        /// <summary>Adds points (negative amounts are ignored) and returns the player's round total.</summary>
        public int Add(TKey player, int points) {
            scores.TryGetValue(player, out var entry);
            if (points > 0) entry = (entry.points + points, ++sequence);
            else if (!scores.ContainsKey(player)) entry = (0, long.MaxValue);
            scores[player] = entry;
            return entry.points;
        }

        public int PointsOf(TKey player) => scores.TryGetValue(player, out var entry) ? entry.points : 0;

        /// <summary>The player with the most points (above zero), or false when nobody scored.</summary>
        public bool TryGetMvp(out TKey mvp, out int points) {
            mvp = default;
            points = 0;
            long bestAt = long.MaxValue;
            bool found = false;
            foreach (var pair in scores) {
                (int p, long at) = pair.Value;
                if (p <= 0 || p < points || (p == points && at >= bestAt)) continue;
                mvp = pair.Key;
                points = p;
                bestAt = at;
                found = true;
            }
            return found;
        }
    }

    /// <summary>Mid-round map events: how often, when, and for how long. Serialized on the server runner.</summary>
    [Serializable]
    public sealed class HS_EventTuning {
        [Tooltip("Chance that a round gets an event.")]
        [Range(0f, 1f)] public float chance = 0.8f;
        [Tooltip("When it starts, as a fraction of the seek phase gone (a random point in this range).")]
        public Vector2 startWindow = new(0.25f, 0.55f);
        [Tooltip("Blackout: the house goes dark.")]
        [Min(1)] public int blackoutSeconds = 18;
        [Tooltip("Frenzy: the seekers run faster.")]
        [Min(1)] public int frenzySeconds = 15;
        [Range(1f, 2f)] public float frenzySpeedScale = 1.25f;
        [Tooltip("Shuffle: hiders must reach a different room in time, or the seekers get a ping on them.")]
        [Min(5)] public int shuffleSeconds = 20;
        [Tooltip("Relative weights of Blackout, Frenzy and Shuffle.")]
        public Vector3 weights = new(1f, 1f, 1f);
        [Tooltip("Today's featured event (HS_Daily) multiplies the points for its play by this.")]
        [Min(1)] public int featuredPayMultiplier = 2;
        [Tooltip("Share of today's rounds that get the featured event. The rest roll an ordinary event among the other two " +
                 "(with Chance), so the day has a theme but its rounds still differ.")]
        [Range(0f, 1f)] public float featuredShare = 0.6f;

        public int SecondsOf(HS_RoundEvent kind) => kind switch {
            HS_RoundEvent.Blackout => blackoutSeconds,
            HS_RoundEvent.Frenzy => frenzySeconds,
            HS_RoundEvent.Shuffle => shuffleSeconds,
            _ => 0,
        };

        public void Validate() {
            chance = Mathf.Clamp01(chance);
            float a = Mathf.Clamp01(Mathf.Min(startWindow.x, startWindow.y)), b = Mathf.Clamp01(Mathf.Max(startWindow.x, startWindow.y));
            startWindow = new Vector2(a, b);
            weights = Vector3.Max(weights, Vector3.zero);
            featuredPayMultiplier = Mathf.Max(1, featuredPayMultiplier);
            featuredShare = Mathf.Clamp01(featuredShare);
        }
    }

    /// <summary>Picks a round's event and its start time; pure so it can be tested.</summary>
    public static class HS_RoundEvents {
        static readonly HS_RoundEvent[] Kinds = { HS_RoundEvent.Blackout, HS_RoundEvent.Frenzy, HS_RoundEvent.Shuffle };

        /// <summary>
        /// This round's event (or None). Today's <paramref name="featured"/> event (<see cref="HS_Daily"/>) comes in
        /// <see cref="HS_EventTuning.featuredShare"/> of the rounds, whatever came before: it is the day's twist.
        /// Otherwise an ordinary event is rolled with <see cref="HS_EventTuning.chance"/> among the others, never
        /// the same as <paramref name="previous"/> when another one can be picked, so back-to-back rounds feel different.
        /// </summary>
        public static HS_RoundEvent Pick(System.Random random, HS_EventTuning tuning, HS_RoundEvent previous, HS_RoundEvent featured = HS_RoundEvent.None) {
            if (featured != HS_RoundEvent.None && random.NextDouble() < tuning.featuredShare) return featured;
            if (random.NextDouble() >= tuning.chance) return HS_RoundEvent.None;
            float total = 0f;
            foreach (HS_RoundEvent kind in Kinds) total += Weight(tuning, kind, previous, featured);
            if (total <= 0f) {
                // Only the previous event has weight: allow a repeat rather than nothing.
                previous = HS_RoundEvent.None;
                foreach (HS_RoundEvent kind in Kinds) total += Weight(tuning, kind, previous, featured);
                if (total <= 0f) return featured;
            }
            double roll = random.NextDouble() * total;
            foreach (HS_RoundEvent kind in Kinds) {
                roll -= Weight(tuning, kind, previous, featured);
                if (roll < 0d) return kind;
            }
            return HS_RoundEvent.None;
        }

        static float Weight(HS_EventTuning tuning, HS_RoundEvent kind, HS_RoundEvent previous, HS_RoundEvent featured) {
            if (kind == previous || kind == featured) return 0f;
            return kind switch {
                HS_RoundEvent.Blackout => tuning.weights.x,
                HS_RoundEvent.Frenzy => tuning.weights.y,
                HS_RoundEvent.Shuffle => tuning.weights.z,
                _ => 0f,
            };
        }

        /// <summary>Seconds into the seek phase when the event starts (inside the tuning's window, leaving room for it to finish).</summary>
        public static float StartAfter(System.Random random, HS_EventTuning tuning, HS_RoundEvent kind, float seekSeconds) {
            float latest = Mathf.Max(0f, seekSeconds - tuning.SecondsOf(kind) - 1f);
            float t = Mathf.Lerp(tuning.startWindow.x, tuning.startWindow.y, (float)random.NextDouble()) * seekSeconds;
            return Mathf.Clamp(t, 0f, latest);
        }
    }
}
