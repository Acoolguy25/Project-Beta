using System;
using UnityEngine;

namespace Universes.UniverseData.dot_invaders {
    /// <summary>
    /// Bounty timing and payout, serialized on the server runner. A bounty is a short mid-match objective:
    /// one contested plain base is marked, and the team that captures it before the timer runs out gets
    /// bonus troops in it (and its player XP on the summary card).
    /// </summary>
    [Serializable]
    public sealed class DI_BountyTuning {
        [Tooltip("Turn bounties off for every board.")]
        public bool enabled = true;
        [Tooltip("Seconds into a match before the first bounty is posted.")]
        [Min(0f)] public float firstAfterSeconds = 60f;
        [Tooltip("Seconds between one bounty ending (claimed or expired) and the next being posted.")]
        [Min(1f)] public float intervalSeconds = 70f;
        [Tooltip("Seconds a bounty stays up before it expires unclaimed.")]
        [Min(5f)] public float durationSeconds = 45f;
        [Tooltip("Troops added to the base when a team captures it during a bounty.")]
        [Min(0)] public int bonusTroops = 15;
        [Tooltip("No bounty is posted with less than this many seconds of the match left.")]
        [Min(0)] public int minSecondsLeft = 45;
        [Tooltip("A bounty goes on one of this many most contested bases, picked at random, so it is not predictable.")]
        [Range(1, 8)] public int candidatePool = 3;
    }

    /// <summary>The bounty rules shared by the server (placement) and tests. Pure functions only.</summary>
    public static class DI_Bounty {
        /// <summary>
        /// The base a bounty goes on, or -1. Only <paramref name="eligible"/> bases count, and only those two
        /// different teams can reach: each is scored by how far the second-nearest team's nearest base is
        /// (smaller is more contested, so the bounty starts a fight), and one of the best
        /// <paramref name="candidatePool"/> is picked with <paramref name="random"/>.
        /// </summary>
        /// <param name="teams">Team holding each base, or a negative number for neutral.</param>
        public static int PickBase(Vector2[] positions, int[] teams, bool[] eligible, int candidatePool, System.Random random) {
            if (positions == null || teams == null || eligible == null || random == null)
                return -1;
            int count = Mathf.Min(positions.Length, Mathf.Min(teams.Length, eligible.Length));
            int pool = Mathf.Max(1, candidatePool);
            var bestIds = new int[pool];
            var bestScores = new float[pool];
            int found = 0;
            for (int i = 0; i < count; i++) {
                if (!eligible[i])
                    continue;
                float score = ContestScore(positions, teams, count, i);
                if (float.IsPositiveInfinity(score))
                    continue;
                // Insertion into the small sorted pool (lowest score first).
                int slot = Mathf.Min(found, pool - 1);
                if (found == pool && score >= bestScores[slot])
                    continue;
                while (slot > 0 && bestScores[slot - 1] > score) {
                    bestScores[slot] = bestScores[slot - 1];
                    bestIds[slot] = bestIds[slot - 1];
                    slot--;
                }
                bestScores[slot] = score;
                bestIds[slot] = i;
                found = Mathf.Min(pool, found + 1);
            }
            return found == 0 ? -1 : bestIds[random.Next(found)];
        }

        /// <summary>
        /// Distance from <paramref name="baseId"/> to the nearest base of the second-nearest team (the holder,
        /// if any, counts as one of them at distance 0). Infinity when fewer than two teams are on the board.
        /// </summary>
        public static float ContestScore(Vector2[] positions, int[] teams, int count, int baseId) {
            int nearestTeam = teams[baseId] >= 0 ? teams[baseId] : -1;
            float nearest = nearestTeam >= 0 ? 0f : float.PositiveInfinity;
            float second = float.PositiveInfinity;
            // Per team, its nearest base; tracked as the two smallest distances of distinct teams.
            for (int j = 0; j < count; j++) {
                int team = teams[j];
                if (team < 0 || j == baseId)
                    continue;
                float distance = Vector2.Distance(positions[baseId], positions[j]);
                if (team == nearestTeam) {
                    nearest = Mathf.Min(nearest, distance);
                } else if (distance < nearest) {
                    second = nearest;
                    nearest = distance;
                    nearestTeam = team;
                } else if (distance < second) {
                    second = distance;
                }
            }
            return second;
        }
    }
}
