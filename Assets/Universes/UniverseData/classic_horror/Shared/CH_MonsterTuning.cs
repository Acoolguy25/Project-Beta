using System;
using UnityEngine;

namespace Universes.UniverseData.classic_horror {
    /// <summary>
    /// Every number that decides how hard the Presence hunts, in one serialized block on the server
    /// runner. Speeds are what the navigation agent actually moves at (metres per second); for scale,
    /// investigators walk at 4.5 and sprint at 9.5 until their stamina runs out.
    /// </summary>
    [Serializable]
    public sealed class CH_MonsterTuning {
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
        [Tooltip("With a single investigator left alive no hunt is faster than this, so a lone sprinter can always open a gap. "
            + "Keep it below the investigators' sprint speed.")]
        [Min(0.5f)] public float loneInvestigatorSpeedCap = 6.5f;

        [Header("Pursuit")]
        [Tooltip("Seconds it keeps tracking an investigator through cover after losing sight of them.")]
        [Min(0f)] public float pursuitMemorySeconds = 20f;
        [Tooltip("Pursuit memory when a single investigator is left alive: short enough that breaking its line of sight "
            + "(a corner, a wall, a building) actually shakes it off.")]
        [Min(0f)] public float lonePursuitMemorySeconds = 8f;

        [Header("Strike")]
        [Tooltip("How close (eye to eye, metres) it must be to strike.")]
        [Min(0.5f)] public float strikeDistance = 1.85f;
        [Tooltip("Its reach past small obstacles when it strikes (metres).")]
        [Min(0.5f)] public float strikeReach = 2.2f;
        [Tooltip("Seconds between two strikes.")]
        [Min(0.1f)] public float attackInterval = 2f;

        [Header("Whispers")]
        [Tooltip("It whispers a warning over the radio when hunting someone closer than this (metres).")]
        [Min(0f)] public float whisperDistance = 38f;
        [Tooltip("Seconds between two whispered warnings.")]
        [Min(1f)] public float whisperInterval = 35f;

        /// <summary>How long it keeps tracking an investigator it has lost sight of.</summary>
        public float PursuitMemory(int livingInvestigators) =>
            livingInvestigators <= 1 ? Mathf.Min(pursuitMemorySeconds, lonePursuitMemorySeconds) : pursuitMemorySeconds;

        /// <summary>The hunting speed for this moment of the case.</summary>
        public float HuntSpeed(bool ritual, bool enraged, int livingInvestigators) {
            float speed = enraged ? enragedHuntSpeed : ritual ? ritualHuntSpeed : investigationHuntSpeed;
            return livingInvestigators <= 1 ? Mathf.Min(speed, loneInvestigatorSpeedCap) : speed;
        }
    }
}
