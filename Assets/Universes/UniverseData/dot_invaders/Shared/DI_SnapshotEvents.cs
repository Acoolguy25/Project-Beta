using System.Collections.Generic;
using UnityEngine;

namespace Universes.UniverseData.dot_invaders {
    /// <summary>A base that changed hands between two snapshots.</summary>
    public struct DI_Capture {
        public int baseId;
        public int previousTeamId;
        public int teamId;
    }

    /// <summary>
    /// Reads what happened between two consecutive state snapshots (captures, eliminations, troops
    /// that left the board) so the client can react to it. Pure functions of the snapshots: the
    /// server stays the only authority and nothing here changes the game.
    /// </summary>
    public static class DI_SnapshotEvents {
        /// <summary>True when both snapshots describe the same board of the same running match.</summary>
        public static bool SameBoard(in DI_StateBroadcast previous, in DI_StateBroadcast next) {
            if (previous.basePositions == null || next.basePositions == null ||
                previous.basePositions.Length != next.basePositions.Length ||
                next.revision <= previous.revision)
                return false;
            for (int i = 0; i < next.basePositions.Length; i++)
                if (previous.basePositions[i] != next.basePositions[i])
                    return false;
            return true;
        }

        /// <summary>Bases now held by a team that did not hold them before. Neutralised bases are not captures.</summary>
        public static void FindCaptures(in DI_StateBroadcast previous, in DI_StateBroadcast next, List<DI_Capture> results) {
            results.Clear();
            if (!SameBoard(previous, next) || previous.baseTeams == null || next.baseTeams == null)
                return;
            int count = Mathf.Min(previous.baseTeams.Length, next.baseTeams.Length);
            for (int i = 0; i < count; i++) {
                int before = previous.baseTeams[i], after = next.baseTeams[i];
                if (after >= 0 && after != before)
                    results.Add(new DI_Capture { baseId = i, previousTeamId = before, teamId = after });
            }
        }

        /// <summary>Teams that held a base or had troops on the board before, and have neither now.</summary>
        public static void FindEliminatedTeams(in DI_StateBroadcast previous, in DI_StateBroadcast next,
            HashSet<int> scratch, List<int> results) {
            results.Clear();
            if (!SameBoard(previous, next) || previous.matchEnded)
                return;
            CollectTeams(previous, scratch);
            foreach (int team in scratch)
                if (!HasPresence(next, team))
                    results.Add(team);
            results.Sort();
        }

        /// <summary>Indices into <paramref name="previous"/>'s dot arrays of troops missing from <paramref name="next"/>.</summary>
        public static void FindVanishedDots(in DI_StateBroadcast previous, in DI_StateBroadcast next,
            HashSet<int> scratch, List<int> results) {
            results.Clear();
            if (!SameBoard(previous, next) || previous.dotIds == null)
                return;
            scratch.Clear();
            if (next.dotIds != null)
                foreach (int id in next.dotIds)
                    scratch.Add(id);
            for (int i = 0; i < previous.dotIds.Length; i++)
                if (!scratch.Contains(previous.dotIds[i]))
                    results.Add(i);
        }

        /// <summary>Every team with a base or a troop in <paramref name="state"/>.</summary>
        public static void CollectTeams(in DI_StateBroadcast state, HashSet<int> results) {
            results.Clear();
            if (state.baseTeams != null)
                foreach (int team in state.baseTeams)
                    if (team >= 0)
                        results.Add(team);
            if (state.dotTeams != null)
                foreach (int team in state.dotTeams)
                    if (team >= 0)
                        results.Add(team);
        }

        public static bool HasPresence(in DI_StateBroadcast state, int teamId) {
            if (teamId < 0)
                return false;
            if (state.baseTeams != null && System.Array.IndexOf(state.baseTeams, teamId) >= 0)
                return true;
            return state.dotTeams != null && System.Array.IndexOf(state.dotTeams, teamId) >= 0;
        }

        /// <summary>The base nearest <paramref name="point"/> within <paramref name="radius"/>, or -1.</summary>
        public static int NearestBase(in DI_StateBroadcast state, Vector2 point, float radius) {
            if (state.basePositions == null)
                return -1;
            int best = -1;
            float bestDistance = radius * radius;
            for (int i = 0; i < state.basePositions.Length; i++) {
                float distance = (state.basePositions[i] - point).sqrMagnitude;
                if (distance <= bestDistance) {
                    bestDistance = distance;
                    best = i;
                }
            }
            return best;
        }
    }
}
