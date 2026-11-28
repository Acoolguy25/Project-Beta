using System;
using System.Collections.Generic;
using UnityEngine;

namespace Universes.UniverseData.dot_invaders {
    /// <summary>Tuning of the skill rating, exposed on the server runner.</summary>
    [Serializable]
    public sealed class DI_RatingTuning {
        [Tooltip("Rating a new player starts at.")]
        [Min(0)] public int startRating = 1000;
        [Tooltip("Lowest rating a player can drop to.")]
        [Min(0)] public int minRating = 100;
        [Tooltip("Most rating one match can move a settled player (the Elo K factor).")]
        [Min(1f)] public float kFactor = 32f;
        [Tooltip("K factor for a player's first rated matches, so a new player finds their level quickly.")]
        [Min(1f)] public float provisionalKFactor = 64f;
        [Tooltip("Rated matches a player counts as provisional for.")]
        [Min(0)] public int provisionalMatches = 5;
        [Tooltip("A player who joins this many seconds after the match started plays it unrated.")]
        [Min(0f)] public float lateJoinSeconds = 20f;
        [Tooltip("Rating points between two players for the stronger one to be expected to win about 91% of the time (Elo uses 400).")]
        [Min(50f)] public float scale = 400f;

        public void Validate() {
            startRating = Mathf.Max(0, startRating);
            minRating = Mathf.Clamp(minRating, 0, startRating);
            kFactor = Mathf.Max(1f, kFactor);
            provisionalKFactor = Mathf.Max(kFactor, provisionalKFactor);
            provisionalMatches = Mathf.Max(0, provisionalMatches);
            lateJoinSeconds = Mathf.Max(0f, lateJoinSeconds);
            scale = Mathf.Max(50f, scale);
        }
    }

    /// <summary>One opponent in a rated match: their rating and how the player did against them.</summary>
    public readonly struct DI_RatedOpponent {
        public readonly int Rating;
        /// <summary>1 = finished ahead, 0.5 = level, 0 = finished behind.</summary>
        public readonly float Score;

        public DI_RatedOpponent(int rating, float score) {
            Rating = rating;
            Score = Mathf.Clamp01(score);
        }
    }

    /// <summary>Where a team finished: a higher <see cref="Standing"/> is a better finish.</summary>
    public readonly struct DI_TeamResult {
        public readonly int TeamId;
        public readonly long Standing;
        /// <summary>The rating the team is rated against (a player's own, or its NPC tier's).</summary>
        public readonly int Rating;

        public DI_TeamResult(int teamId, long standing, int rating) {
            TeamId = teamId;
            Standing = standing;
            Rating = rating;
        }
    }

    /// <summary>
    /// Free-for-all Elo: a match counts as one game against every other team on the board (players at
    /// their rating, NPC teams at their tier's), scored by who finished ahead; the change is the K factor
    /// times the average surprise, so one match moves a rating by at most K.
    /// </summary>
    public static class DI_Rating {
        /// <summary>Chance a <paramref name="rating"/> player finishes ahead of an <paramref name="opponent"/>-rated one.</summary>
        public static double Expected(int rating, int opponent, float scale = 400f) =>
            1d / (1d + Math.Pow(10d, (opponent - rating) / (double)Mathf.Max(1f, scale)));

        public static float KFactor(int ratedMatches, DI_RatingTuning tuning) =>
            ratedMatches < tuning.provisionalMatches ? tuning.provisionalKFactor : tuning.kFactor;

        /// <summary>The rating change for one match (0 without opponents).</summary>
        public static int Delta(int rating, int ratedMatches, IReadOnlyList<DI_RatedOpponent> opponents, DI_RatingTuning tuning) {
            if (opponents == null || opponents.Count == 0) return 0;
            double surprise = 0d;
            foreach (DI_RatedOpponent opponent in opponents)
                surprise += opponent.Score - Expected(rating, opponent.Rating, tuning.scale);
            return (int)Math.Round(KFactor(ratedMatches, tuning) * surprise / opponents.Count, MidpointRounding.AwayFromZero);
        }

        /// <summary>The new rating after a match, never below the floor.</summary>
        public static int Apply(int rating, int ratedMatches, IReadOnlyList<DI_RatedOpponent> opponents, DI_RatingTuning tuning) =>
            Mathf.Max(tuning.minRating, rating + Delta(rating, ratedMatches, opponents, tuning));

        /// <summary>Every other team as an opponent of <paramref name="teamId"/>, scored by the two standings.</summary>
        public static List<DI_RatedOpponent> Opponents(int teamId, IReadOnlyList<DI_TeamResult> results) {
            var opponents = new List<DI_RatedOpponent>();
            long mine = StandingOf(teamId, results);
            foreach (DI_TeamResult other in results) {
                if (other.TeamId == teamId) continue;
                float score = mine > other.Standing ? 1f : mine < other.Standing ? 0f : 0.5f;
                opponents.Add(new DI_RatedOpponent(other.Rating, score));
            }
            return opponents;
        }

        /// <summary>1-based finishing place (teams level with you share your place).</summary>
        public static int Placement(int teamId, IReadOnlyList<DI_TeamResult> results) {
            long mine = StandingOf(teamId, results);
            int ahead = 0;
            foreach (DI_TeamResult other in results)
                if (other.TeamId != teamId && other.Standing > mine) ahead++;
            return ahead + 1;
        }

        static long StandingOf(int teamId, IReadOnlyList<DI_TeamResult> results) {
            foreach (DI_TeamResult result in results)
                if (result.TeamId == teamId) return result.Standing;
            throw new ArgumentException($"Team {teamId} has no result.", nameof(teamId));
        }

        /// <summary>"1st", "2nd", "3rd", "4th", "11th"...</summary>
        public static string Ordinal(int place) {
            int tens = place % 100;
            string suffix = tens is >= 11 and <= 13 ? "th" : (place % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" };
            return place + suffix;
        }
    }

    /// <summary>A named band of ratings: "Silver II" from 1150.</summary>
    public readonly struct DI_Rank {
        public readonly int Index;
        public readonly string Name;
        public readonly int MinRating;
        public readonly Color Color;

        public DI_Rank(int index, string name, int minRating, Color color) {
            Index = index;
            Name = name;
            MinRating = minRating;
            Color = color;
        }
    }

    /// <summary>The rank ladder: four metal tiers of three divisions each, then Master and Grandmaster.</summary>
    public static class DI_Ranks {
        // Divisions are DivisionWidth apart; a tier spans three of them.
        const int FirstTierRating = 950;
        const int DivisionWidth = 50;
        static readonly (string name, Color color)[] Tiers = {
            ("Bronze", new Color(0.8f, 0.5f, 0.3f)),
            ("Silver", new Color(0.75f, 0.8f, 0.88f)),
            ("Gold", new Color(1f, 0.8f, 0.25f)),
            ("Platinum", new Color(0.35f, 0.9f, 0.85f)),
            ("Diamond", new Color(0.45f, 0.65f, 1f)),
        };
        static readonly string[] Divisions = { "III", "II", "I" };
        static readonly Color IronColor = new(0.55f, 0.58f, 0.62f);
        static readonly Color MasterColor = new(0.8f, 0.45f, 1f);
        static readonly Color GrandmasterColor = new(1f, 0.35f, 0.4f);
        const int GrandmasterGap = 150;

        static readonly DI_Rank[] ladder = Build();

        static DI_Rank[] Build() {
            var ranks = new List<DI_Rank> { new(0, "Iron", 0, IronColor) };
            int rating = FirstTierRating;
            foreach ((string name, Color color) in Tiers)
                foreach (string division in Divisions) {
                    ranks.Add(new DI_Rank(ranks.Count, $"{name} {division}", rating, color));
                    rating += DivisionWidth;
                }
            ranks.Add(new DI_Rank(ranks.Count, "Master", rating, MasterColor));
            ranks.Add(new DI_Rank(ranks.Count, "Grandmaster", rating + GrandmasterGap, GrandmasterColor));
            return ranks.ToArray();
        }

        public static IReadOnlyList<DI_Rank> All => ladder;

        public static DI_Rank Of(int rating) {
            DI_Rank rank = ladder[0];
            foreach (DI_Rank candidate in ladder)
                if (rating >= candidate.MinRating) rank = candidate;
            return rank;
        }

        /// <summary>The next rank up, or false at the top.</summary>
        public static bool TryNext(int rating, out DI_Rank next) {
            int index = Of(rating).Index + 1;
            next = index < ladder.Length ? ladder[index] : default;
            return index < ladder.Length;
        }
    }

    /// <summary>How hard the NPC teams play, and the rating a player is rated against them at.</summary>
    [Serializable]
    public struct DI_NpcTier {
        public string name;
        [Tooltip("Rating the NPC teams count as for the players' rating.")]
        public int rating;
        [Range(0f, 100f)] public float intelligence;
        [Range(0f, 100f)] public float aggression;

        public DI_NpcTier(string name, int rating, float intelligence, float aggression) {
            this.name = name;
            this.rating = rating;
            this.intelligence = intelligence;
            this.aggression = aggression;
        }
    }

    /// <summary>
    /// The NPC ladder. A match's bots play at the tier nearest the players' average rating, so a new
    /// player meets beatable bots and the bots sharpen as they climb.
    /// </summary>
    public static class DI_NpcTiers {
        public static DI_NpcTier[] CreateDefaults() => new[] {
            new DI_NpcTier("Rookie", 850, 25f, 30f),
            new DI_NpcTier("Regular", 1000, 40f, 40f),
            new DI_NpcTier("Veteran", 1150, 55f, 50f),
            new DI_NpcTier("Elite", 1300, 70f, 58f),
            new DI_NpcTier("Master", 1450, 82f, 66f),
            new DI_NpcTier("Legend", 1600, 95f, 75f),
        };

        /// <summary>The tier whose rating is nearest <paramref name="averageRating"/> (the lower one on a tie).</summary>
        public static int Pick(IReadOnlyList<DI_NpcTier> tiers, int averageRating) {
            if (tiers == null || tiers.Count == 0) return -1;
            int best = 0;
            for (int i = 1; i < tiers.Count; i++)
                if (Mathf.Abs(tiers[i].rating - averageRating) < Mathf.Abs(tiers[best].rating - averageRating))
                    best = i;
            return best;
        }
    }
}
