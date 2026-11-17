using System;
using UnityEngine;

namespace Universes.UniverseData.murder_mystery {
    /// <summary>
    /// Every number that shapes a Classic round, in one serialized block on the server runner so a
    /// designer can tune pacing and balance in the Inspector. Read only by the server.
    /// </summary>
    [Serializable]
    public sealed class MM_ClassicTuning {
        [Header("Phases (seconds)")]
        [Tooltip("Private role card before the hunt starts. Everyone is invulnerable.")]
        [Min(1)] public int roleRevealSeconds = 6;
        [Tooltip("Length of the hunt. Meetings pause it.")]
        [Min(5)] public int huntSeconds = 180;
        [Tooltip("Discussion before the accusation vote opens.")]
        [Min(1)] public int discussionSeconds = 15;
        [Tooltip("Longest the accusation vote stays open (it ends early once everyone voted).")]
        [Min(1)] public int voteSeconds = 25;
        [Tooltip("How long the ejection outcome is shown before the hunt resumes.")]
        [Min(1)] public int ejectionSeconds = 6;
        [Tooltip("End-of-round reveal of every role.")]
        [Min(1)] public int resultsSeconds = 12;

        [Header("Body reports")]
        [Min(0)] public int meetingsPerRound = 3;
        [Tooltip("No report is accepted this long after the hunt starts.")]
        [Min(0)] public int firstReportDelaySeconds = 10;
        [Tooltip("No report is accepted this long after a meeting ends.")]
        [Min(0)] public int reportCooldownSeconds = 20;
        [Tooltip("A living player this close to an unreported body can report it.")]
        [Min(1f)] public float reportDistance = 6f;
        [Tooltip("Unreported bodies this close to a player get a floor marker on their screen.")]
        [Min(1f)] public float markerDistance = 30f;
        [Tooltip("Living characters this close to a death are remembered as the suspects.")]
        [Min(1f)] public float witnessDistance = 40f;

        [Header("Murderer balance")]
        [Tooltip("After the hunt starts, murderers (players and NPCs) cannot kill for this long, so the sheriff gets a chance to read the room.")]
        [Min(0f)] public float murdererGraceSeconds = 25f;
        [Tooltip("After a kill, that murderer cannot kill again for this long.")]
        [Min(0f)] public float killCooldownSeconds = 25f;
        [Tooltip("When a meeting ends, every murderer waits at least this long before the next kill.")]
        [Min(0f)] public float postMeetingKillCooldownSeconds = 10f;
        [Tooltip("Health a murderer gets back for killing a player.")]
        [Min(0)] public int murdererHealOnPlayerKill = 60;
        [Tooltip("Health a murderer gets back for killing an NPC.")]
        [Min(0)] public int murdererHealOnNpcKill = 20;

        [Header("NPC murderers and the sheriff")]
        // Measured with a solo sheriff (README, "Balance"). Proximity alone either let a sheriff who stays put
        // win on time (12 m: nobody came; 45 m: about one approach per round) or got them killed early, so the
        // murderers go looking for the sheriff once the hunt is under way, one at a time, and creep up on the
        // gun rather than charge it.
        [Tooltip("An NPC murderer that may kill rushes an armed sheriff closer than this (metres). Further away the sheriff is just another face in the crowd, until the manhunt starts.")]
        [Min(0f)] public float sheriffNoticeDistance = 45f;
        [Tooltip("For this long after the sheriff shoots someone, NPC murderers notice them from much further away.")]
        [Min(0f)] public float sheriffExposedSeconds = 8f;
        [Tooltip("How far NPC murderers notice a sheriff who has just shot someone (metres).")]
        [Min(0f)] public float exposedSheriffNoticeDistance = 60f;
        [Tooltip("Once the hunt has run this long (seconds, meetings not counted), NPC murderers go looking for the sheriff instead of waiting to bump into them. Above the hunt length = never.")]
        [Min(0f)] public float sheriffManhuntAfterSeconds = 60f;
        [Tooltip("How far NPC murderers notice the sheriff once the manhunt is on (metres). The map is about 450 m across, so the default finds them anywhere.")]
        [Min(0f)] public float manhuntNoticeDistance = 1000f;
        [Tooltip("Each further NPC murderer joins the manhunt this much later (seconds), so they come for the sheriff one at a time.")]
        [Min(0f)] public float manhuntStaggerSeconds = 35f;
        [Tooltip("Inside this distance of an armed sheriff (metres) an NPC murderer creeps instead of charging, so the sheriff can spot it and shoot first. 0 = always charge.")]
        [Min(0f)] public float sheriffCreepDistance = 25f;
        [Tooltip("How fast an NPC murderer creeps up on an armed sheriff (m/s). Walking is 18 and a charge 20.")]
        [Min(0.5f)] public float sheriffCreepSpeed = 4f;
        [Tooltip("Multiplies an NPC's attack detection radius for an armed sheriff (1 = like anyone else).")]
        [Min(0f)] public float sheriffDetectionMultiplier = 1.5f;
        [Tooltip("Extra target priority an armed sheriff gets with NPC murderers (0 = none).")]
        [Min(0f)] public float sheriffTargetPriority = 1f;

        /// <summary>
        /// How close an armed sheriff must be before an NPC murderer that may kill goes for them: the widest of
        /// the quiet distance, the exposed distance (just fired) and the manhunt distance (late in the hunt).
        /// <paramref name="hunterRank"/> is the murderer's place in the manhunt (0 = first to join).
        /// </summary>
        public float SheriffNoticeDistance(bool exposed, float huntSecondsElapsed, int hunterRank) {
            float distance = sheriffNoticeDistance;
            if (exposed)
                distance = Mathf.Max(distance, exposedSheriffNoticeDistance);
            if (huntSecondsElapsed >= sheriffManhuntAfterSeconds + hunterRank * manhuntStaggerSeconds)
                distance = Mathf.Max(distance, manhuntNoticeDistance);
            return distance;
        }

        [Header("Kill feedback")]
        [Tooltip("Players this close to a kill hear and see it (kill sting, sparks). The victim and the killer always do.")]
        [Min(0f)] public float killAudibleDistance = 45f;

        [Header("Sheriff")]
        [Tooltip("Shooting a non-murderer jams the sheriff's pistol for this long.")]
        [Min(0f)] public float wrongShotJamSeconds = 20f;
        [Tooltip("When a murderer is killed (never when voted out) their dagger drops too, and an empty-handed innocent player who walks over it becomes a murderer. Off by default: it turns a catch into a trap.")]
        public bool dropDaggerOnMurdererDeath;
    }
}
