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
        /// <summary>Seconds of full sprint an investigator's stamina allows (100 stamina at 10 per second).</summary>
        public const float InvestigatorSprintSeconds = 10f;
        /// <summary>How far from an investigator (metres) the Presence accepts a walkable approach point when they stand off its NavMesh.</summary>
        public const float OffMeshApproachRadius = 3f;
        /// <summary>How short of its destination (metres) the Presence's navigation agent stops.</summary>
        public const float AgentStoppingDistance = 0.65f;

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

        [Header("Sight (metres, with a clear line)")]
        [Tooltip("How far it notices an investigator, unless its temperament says otherwise.")]
        [Min(1f)] public float sightRange = 60f;
        [Tooltip("A light seeker's sight range for an investigator whose flashlight is on.")]
        [Min(1f)] public float lightSeekerLitSightRange = 85f;
        [Tooltip("A light seeker's sight range for an investigator in the dark. Kept well under loneSightRange so putting "
            + "the light out, as the journal says, still matters when playing alone.")]
        [Min(1f)] public float lightSeekerDarkSightRange = 30f;
        [Tooltip("A listener's sight range for an investigator moving faster than listenerMovingSpeed.")]
        [Min(1f)] public float listenerMovingSightRange = 85f;
        [Tooltip("Speed (m/s) above which a listener counts an investigator as moving.")]
        [Min(0f)] public float listenerMovingSpeed = 3f;
        [Tooltip("Sight range while enraged, for everyone.")]
        [Min(1f)] public float enragedSightRange = 85f;
        [Tooltip("How far it keeps seeing an investigator it is already chasing.")]
        [Min(1f)] public float pursuitSightRange = 85f;
        [Tooltip("No sight reaches further than this while a single investigator is left alive and it is not enraged, so a "
            + "lone player who opens a gap with a sprint can get out of sight by distance as well as by cover.")]
        [Min(1f)] public float loneSightRange = 45f;

        [Header("Hearing (metres, through cover)")]
        [Tooltip("Footsteps faster than this (m/s) are a sprint. Investigators walk at 4.5 and sprint at 9.5.")]
        [Min(0f)] public float sprintNoiseSpeed = 6f;
        [Tooltip("Footsteps faster than this (m/s) are heard at all; standing still or creeping is silent.")]
        [Min(0f)] public float walkNoiseSpeed = 1.25f;
        [Min(0f)] public float sprintHearingRange = 34f;
        [Min(0f)] public float walkHearingRange = 20f;
        [Tooltip("A listener hears sprinting this far.")]
        [Min(0f)] public float listenerSprintHearingRange = 55f;
        [Tooltip("A listener hears walking this far.")]
        [Min(0f)] public float listenerWalkHearingRange = 40f;

        [Header("Flashlight")]
        [Tooltip("A beam reaches a light-shy Presence within this distance (metres)...")]
        [Min(0f)] public float beamRepelRange = 32f;
        [Tooltip("...when it points this directly at it (dot product of the beam and the direction to it).")]
        [Range(0f, 1f)] public float beamRepelAlignment = 0.86f;
        [Tooltip("Seconds a light-shy Presence keeps retreating after the beam leaves it, so small flickers do not flip it back.")]
        [Min(0f)] public float lightHoldSeconds = 1.5f;

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

        /// <summary>The longest sight range any rule can give; the line-of-sight test reaches this far.</summary>
        public float MaxSightRange => Mathf.Max(Mathf.Max(sightRange, lightSeekerLitSightRange),
            Mathf.Max(Mathf.Max(lightSeekerDarkSightRange, listenerMovingSightRange), Mathf.Max(enragedSightRange, pursuitSightRange)));

        /// <summary>
        /// How far it sees an investigator: its temperament sets how far it notices them, a chase extends that, an
        /// enraged Presence sees the full range, and a lone investigator is never seen beyond <see cref="loneSightRange"/>
        /// unless it is enraged.
        /// </summary>
        public float SightRange(CH_Temperament temperament, bool lit, float investigatorSpeed, bool enraged, bool pursuing, int livingInvestigators) {
            if (enraged) return enragedSightRange;
            float range = temperament switch {
                CH_Temperament.LightSeeker => lit ? lightSeekerLitSightRange : lightSeekerDarkSightRange,
                CH_Temperament.Listener => investigatorSpeed > listenerMovingSpeed ? listenerMovingSightRange : sightRange,
                _ => sightRange
            };
            if (pursuing) range = Mathf.Max(range, pursuitSightRange);
            return livingInvestigators <= 1 ? Mathf.Min(range, loneSightRange) : range;
        }

        /// <summary>How far it hears an investigator moving at <paramref name="investigatorSpeed"/> (0 when they are silent).</summary>
        public float HearingRange(CH_Temperament temperament, float investigatorSpeed) {
            bool listener = temperament == CH_Temperament.Listener;
            if (investigatorSpeed > sprintNoiseSpeed) return listener ? listenerSprintHearingRange : sprintHearingRange;
            if (investigatorSpeed > walkNoiseSpeed) return listener ? listenerWalkHearingRange : walkHearingRange;
            return 0f;
        }

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
