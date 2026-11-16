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

        [Header("Sheriff")]
        [Tooltip("Shooting a non-murderer jams the sheriff's pistol for this long.")]
        [Min(0f)] public float wrongShotJamSeconds = 20f;
        [Tooltip("When a murderer dies their dagger drops too, and an empty-handed innocent player who takes it becomes a murderer.")]
        public bool dropDaggerOnMurdererDeath = true;
    }
}
