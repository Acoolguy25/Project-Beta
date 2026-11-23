using System;
using UnityEngine;

namespace Universes.UniverseData.classic_horror {
    /// <summary>
    /// Every number that decides how Father Crane hunts, in one serialized block on the server runner. Speeds are what
    /// his navigation agent moves at (m/s); investigators walk at 4.5 and sprint at 9.5 for about ten seconds of
    /// stamina. He is faster than a walk and slower than a sprint: a sprint opens a gap, and only breaking his sight and
    /// getting out of earshot (or hiding) ends a chase.
    /// </summary>
    [Serializable]
    public sealed class CH_MonsterTuning {
        public const float InvestigatorWalkSpeed = 4.5f, InvestigatorSprintSpeed = 9.5f;
        /// <summary>How short of its destination (metres) the agent stops.</summary>
        public const float AgentStoppingDistance = 0.4f;

        [Header("Speed (m/s)")]
        [Min(0.5f)] public float patrolSpeed = 3.6f;
        [Tooltip("Walking to where he last sensed someone, and sweeping the area around it.")]
        [Min(0.5f)] public float searchSpeed = 5.4f;
        [Tooltip("Chasing, per act: I (stranded), II (the choir), III (the last sermon).")]
        public Vector3 chaseSpeedByAct = new(6.9f, 7.4f, 8.0f);
        [Tooltip("Chasing whoever carries his bell.")]
        [Min(0.5f)] public float bellChaseSpeed = 8.6f;
        [Tooltip("Chase speed multiplier while only one investigator is alive, so a solo night stays winnable.")]
        [Range(0.5f, 1f)] public float loneChaseFactor = 0.92f;

        [Header("Lunge")]
        [Tooltip("Close enough, with a clear line, he lunges at this speed for a moment.")]
        [Min(0.5f)] public float lungeSpeed = 11.5f;
        [Min(0f)] public float lungeRange = 5.5f;
        [Min(0f)] public float lungeSeconds = 0.5f;
        [Min(0f)] public float lungeCooldown = 3.5f;

        [Header("Sight")]
        [Tooltip("How far he sees an investigator whose flashlight is on.")]
        [Min(1f)] public float litSightRange = 55f;
        [Tooltip("How far he sees an investigator in the dark.")]
        [Min(1f)] public float darkSightRange = 22f;
        [Tooltip("He sees in a cone ahead of him (full angle, degrees); behind him he only hears.")]
        [Range(30f, 360f)] public float sightAngle = 130f;
        [Tooltip("Inside this distance he senses anyone, whichever way he faces.")]
        [Min(0f)] public float closeSenseRadius = 3.5f;
        [Tooltip("Once chasing, he keeps sight of his quarry this far.")]
        [Min(1f)] public float chaseSightRange = 65f;

        [Header("Hearing (through walls)")]
        [Tooltip("Footsteps faster than this (m/s) are a sprint.")]
        [Min(0f)] public float sprintNoiseSpeed = 6f;
        [Tooltip("Footsteps faster than this (m/s) are heard at all; standing still is silent.")]
        [Min(0f)] public float walkNoiseSpeed = 1.5f;
        [Min(0f)] public float sprintHearingRange = 30f;
        [Min(0f)] public float walkHearingRange = 11f;
        [Tooltip("His bell rings as it is carried: he hears its carrier this far, wherever they go.")]
        [Min(0f)] public float bellHearingRange = 80f;

        [Header("Pursuit")]
        [Tooltip("Seconds he keeps chasing after losing every sense of his quarry, heading where they were going.")]
        [Min(0f)] public float memorySeconds = 5f;
        [Tooltip("Seconds he then sweeps the area around where he lost them.")]
        [Min(0f)] public float searchSeconds = 14f;
        [Tooltip("How far around the last sighting the sweep goes (metres).")]
        [Min(1f)] public float searchRadius = 14f;
        [Tooltip("He aims where his quarry will be this far ahead (seconds), not where they are.")]
        [Min(0f)] public float predictionSeconds = 0.45f;
        [Min(0.05f)] public float repathInterval = 0.2f;

        [Header("Strike")]
        [Tooltip("How close (metres, body to body) he must be to grab someone.")]
        [Min(0.5f)] public float strikeDistance = 1.9f;
        [Min(0.1f)] public float attackCooldown = 1.2f;
        [Tooltip("Seconds he stands over a catch before moving on: the rest of the team's moment to get away.")]
        [Min(0f)] public float feedSeconds = 4f;

        [Header("Director")]
        [Tooltip("With nobody sensed for this long, he rises out of the water closer to someone.")]
        [Min(5f)] public float relocateAfterSeconds = 35f;
        [Tooltip("...but only if everyone is at least this far away (metres).")]
        [Min(5f)] public float relocateWhenFurtherThan = 50f;
        [Tooltip("He rises between these distances from the investigator he picks, out of their sight.")]
        public Vector2 relocateDistance = new(26f, 42f);
        [Min(0f)] public float relocateCooldown = 50f;
        [Tooltip("Chance a patrol leg heads for somewhere near an investigator rather than anywhere.")]
        [Range(0f, 1f)] public float patrolTowardInvestigators = 0.65f;

        [Header("Hiding")]
        [Tooltip("Someone who hides within this many seconds of being seen or heard was watched going in.")]
        [Min(0f)] public float sawHideSeconds = 1.75f;
        [Tooltip("Chance he opens a hiding place he passes while sweeping the area he lost someone in.")]
        [Range(0f, 1f)] public float checkHidingChance = 0.25f;
        [Tooltip("Seconds he keeps trying to reach a hiding place before he gives up and searches instead.")]
        [Min(1f)] public float openHidingGiveUpSeconds = 12f;

        [Header("Getting unstuck")]
        [Tooltip("Seconds without progress on a chase before he forces his way along the path.")]
        [Min(0.25f)] public float stuckSeconds = 1.2f;
        [Min(0.1f)] public float stuckNudge = 1.4f;

        [Header("Voice")]
        [Tooltip("Seconds between two taunts while he hunts.")]
        [Min(2f)] public float tauntInterval = 13f;

        /// <summary>Chase speed for the act (1, 2 or 3), against a bell carrier, alone or with a team.</summary>
        public float ChaseSpeed(int act, bool bellCarrier, int livingInvestigators) {
            float speed = bellCarrier ? bellChaseSpeed : act <= 1 ? chaseSpeedByAct.x : act == 2 ? chaseSpeedByAct.y : chaseSpeedByAct.z;
            return livingInvestigators <= 1 ? speed * loneChaseFactor : speed;
        }

        /// <summary>How far he sees an investigator, lit or in the dark, already chasing them or not.</summary>
        public float SightRange(bool lit, bool chasing) {
            float range = lit ? litSightRange : darkSightRange;
            return chasing ? Mathf.Max(range, chaseSightRange) : range;
        }

        /// <summary>Whether a point at <paramref name="direction"/> from him falls in his sight cone.</summary>
        public bool InSightCone(Vector3 forward, Vector3 direction) {
            if (sightAngle >= 359f) return true;
            forward.y = 0f;
            direction.y = 0f;
            if (direction.sqrMagnitude < 1e-4f || forward.sqrMagnitude < 1e-4f) return true;
            return Vector3.Angle(forward, direction) <= sightAngle * 0.5f;
        }

        /// <summary>How far he hears footsteps at <paramref name="speed"/>, or the bell (0 when they are silent).</summary>
        public float HearingRange(float speed, bool carryingBell) {
            float range = speed > sprintNoiseSpeed ? sprintHearingRange : speed > walkNoiseSpeed ? walkHearingRange : 0f;
            return carryingBell ? Mathf.Max(range, bellHearingRange) : range;
        }
    }
}
