using System;
using UnityEngine;

namespace Universes.UniverseData.classic_horror {
    /// <summary>
    /// Every number that decides how hard the Presence hunts, in one serialized block on the server
    /// runner. Speeds are what the navigation agent actually moves at (metres per second); for scale,
    /// investigators walk at 4.5 and sprint at 9.5 for about ten seconds of stamina.
    /// </summary>
    [Serializable]
    public sealed class CH_MonsterTuning {
        /// <summary>How fast investigators move (m/s), for reference: the hunt speeds are set against these.</summary>
        public const float InvestigatorWalkSpeed = 4.5f, InvestigatorSprintSpeed = 9.5f;

        [Header("Speed (m/s)")]
        [Tooltip("Hunting speed during Chapter I.")]
        [Min(0.5f)] public float investigationHuntSpeed = 7.4f;
        [Tooltip("Hunting speed during Chapter II and the escape.")]
        [Min(0.5f)] public float ritualHuntSpeed = 8.2f;
        [Tooltip("Hunting speed while enraged (a story beat, the start of Chapter II, a wrong offering).")]
        [Min(0.5f)] public float enragedHuntSpeed = 10f;
        [Tooltip("Speed while it follows an investigator it has lost sight of.")]
        [Min(0.5f)] public float searchSpeed = 6f;
        [Tooltip("Speed between patrol points.")]
        [Min(0.5f)] public float patrolSpeed = 5.5f;
        [Tooltip("With a single investigator left alive no hunt is faster than this. Set against what a player can keep up, "
            + "not the sprint: a little above walking pace, so walking loses ground slowly and a sprint opens a real gap.")]
        [Min(0.5f)] public float loneInvestigatorSpeedCap = 5f;
        [Tooltip("The lone cap while it is enraged (a story beat, the start of Chapter II, a wrong offering), so those beats "
            + "still bite when playing alone. Keep it below the sprint speed.")]
        [Min(0.5f)] public float loneEnragedSpeedCap = 7f;

        [Header("Pursuit")]
        [Tooltip("Seconds it keeps tracking an investigator through cover after losing sight of them.")]
        [Min(0f)] public float pursuitMemorySeconds = 20f;
        [Tooltip("Pursuit memory when a single investigator is left alive: short enough that breaking its line of sight "
            + "(a corner, a wall, a building) actually shakes it off.")]
        [Min(0f)] public float lonePursuitMemorySeconds = 8f;
        [Tooltip("How far (metres) it keeps seeing an investigator it is already chasing.")]
        [Min(1f)] public float pursuitSightRange = 85f;
        [Tooltip("Pursuit sight range when a single investigator is left alive (enraged, it still sees the full range), "
            + "so a lone player who opens a gap with a sprint can get out of sight by distance as well as by cover.")]
        [Min(1f)] public float lonePursuitSightRange = 45f;

        [Header("Strike")]
        [Tooltip("How close (eye to eye, metres) it must be to strike.")]
        [Min(0.5f)] public float strikeDistance = 1.85f;
        [Tooltip("Its reach past small obstacles when it strikes (metres).")]
        [Min(0.5f)] public float strikeReach = 2.2f;
        [Tooltip("Seconds between two strikes.")]
        [Min(0.1f)] public float attackInterval = 2f;

        [Header("Stand-off")]
        [Tooltip("Seconds it may stand at its approach point, unable to step closer to an investigator just off the "
            + "walkable ground, before it lunges.")]
        [Min(0f)] public float standOffSeconds = 1.5f;
        [Tooltip("How far (eye to eye, metres) the lunge reaches. It must cover the approach ring around an off-mesh "
            + "investigator (3 m) plus the agent's stopping distance.")]
        [Min(0.5f)] public float standOffStrikeDistance = 3.8f;
        [Tooltip("Seconds of stand-off after which it gives up on an investigator it cannot reach or see, and leaves.")]
        [Min(0f)] public float standOffGiveUpSeconds = 4f;

        [Header("Whispers")]
        [Tooltip("It whispers a warning over the radio when hunting someone closer than this (metres).")]
        [Min(0f)] public float whisperDistance = 38f;
        [Tooltip("Seconds between two whispered warnings.")]
        [Min(1f)] public float whisperInterval = 35f;

        /// <summary>How far it keeps seeing the investigator it is chasing.</summary>
        public float PursuitSightRange(int livingInvestigators) =>
            livingInvestigators <= 1 ? Mathf.Min(pursuitSightRange, lonePursuitSightRange) : pursuitSightRange;

        /// <summary>How long it keeps tracking an investigator it has lost sight of.</summary>
        public float PursuitMemory(int livingInvestigators) =>
            livingInvestigators <= 1 ? Mathf.Min(pursuitMemorySeconds, lonePursuitMemorySeconds) : pursuitMemorySeconds;

        /// <summary>The hunting speed for this moment of the case.</summary>
        public float HuntSpeed(bool ritual, bool enraged, int livingInvestigators) {
            float speed = enraged ? enragedHuntSpeed : ritual ? ritualHuntSpeed : investigationHuntSpeed;
            if (livingInvestigators > 1) return speed;
            return Mathf.Min(speed, enraged ? loneEnragedSpeedCap : loneInvestigatorSpeedCap);
        }

        /// <summary>How close it must be to strike, once it has stood <paramref name="stalledSeconds"/> at its approach point.</summary>
        public float StrikeRange(float stalledSeconds) =>
            stalledSeconds >= standOffSeconds ? Mathf.Max(strikeDistance, standOffStrikeDistance) : strikeDistance;
    }
}
