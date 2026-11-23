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
        [Tooltip("Private role card before the round starts. Everyone is invulnerable.")]
        [Min(1)] public int roleRevealSeconds = 6;
        [Tooltip("Length of the round. The innocents win if any of them are still alive when it runs out.")]
        [Min(5)] public int huntSeconds = 180;
        [Tooltip("End-of-round reveal of every role.")]
        [Min(1)] public int resultsSeconds = 12;

        [Header("Who plays")]
        [Tooltip("NPC townsfolk fill the house up to this many characters (players + NPCs).")]
        [Min(2)] public int fillToCharacters = 18;
        [Tooltip("At least this many NPCs join, however many players there are.")]
        [Min(0)] public int minNpcs = 4;
        [Tooltip("One murderer, and one more for every this many characters beyond it.")]
        [Min(2)] public int charactersPerMurderer = 24;
        [Tooltip("One sheriff, and one more for every this many characters beyond it.")]
        [Min(2)] public int charactersPerSheriff = 24;

        [Header("Role odds (players)")]
        [Tooltip("A player's starting weight in the role draw. NPCs weigh 1, so a higher value makes players likelier to get the big roles.")]
        [Min(0.1f)] public float playerRoleWeight = 3f;
        [Tooltip("Weight a player gains for every round they were not dealt the role, so everyone gets a turn.")]
        [Min(0f)] public float luckPerRound = 1.5f;
        [Tooltip("Luck stops growing after this many rounds.")]
        [Min(0)] public int maxLuckRounds = 12;
        [Tooltip("In a round filled up with NPCs, the players together get at least this share of the murderer and sheriff draws " +
                 "(before luck), so a small lobby does not spend most rounds as an innocent among bots. 0 = NPCs and players weigh as set above.")]
        [Range(0f, 0.9f)] public float playerShareOfBigRoles = 0.4f;

        [Header("Murderer")]
        [Tooltip("After the round starts nobody can kill for this long, so everyone can scatter.")]
        [Min(0f)] public float murdererGraceSeconds = 8f;
        [Tooltip("After a kill, a player murderer cannot kill again for this long.")]
        [Min(0f)] public float playerKillCooldownSeconds = 1.5f;
        [Tooltip("After a kill, an NPC murderer cannot kill again for this long, so NPCs do not clear the house in a minute.")]
        [Min(0f)] public float npcKillCooldownSeconds = 9f;
        [Tooltip("Seconds between knife throws.")]
        [Min(0.2f)] public float throwCooldownSeconds = 3f;
        [Tooltip("Speed of a thrown knife (m/s). A walking character covers 16 m/s.")]
        [Min(5f)] public float throwSpeed = 75f;
        [Tooltip("A thrown knife drops after flying this far (metres).")]
        [Min(5f)] public float throwRange = 90f;
        [Tooltip("Thickness of a thrown knife for hits (metres). The robots are about 2 m wide.")]
        [Min(0.05f)] public float throwRadius = 0.6f;
        [Tooltip("A knife leaves the hand this high above the thrower's feet (the robot is about 5.6 m tall).")]
        [Min(0f)] public float throwHeight = 3.6f;
        [Tooltip("A throw aimed at nobody (the cursor on the floor or a wall) flies level instead of diving into the floor short of the target.")]
        public bool levelThrowsAimedAtNobody = true;
        [Tooltip("A throw counts as aimed at someone when a living character stands this close to the aim point (metres).")]
        [Min(0f)] public float throwAimAssistRadius = 3f;
        [Tooltip("Townsfolk treat a murderer who just threw as a threat for this long (seconds).")]
        [Min(0f)] public float throwPanicSeconds = 2.5f;

        [Header("NPC murderers")]
        [Tooltip("Chance per second that an NPC murderer with a clear line throws at its target instead of running it down.")]
        [Range(0f, 1f)] public float npcThrowChancePerSecond = 0.25f;
        [Tooltip("NPC murderers only throw at targets between these distances (metres).")]
        public Vector2 npcThrowDistance = new(14f, 45f);
        [Tooltip("How long an NPC murderer holds the knife up before throwing, so a sharp sheriff can react.")]
        [Min(0f)] public float npcThrowWindupSeconds = 0.45f;
        [Tooltip("An NPC sheriff this close is rushed by NPC murderers that may kill (metres).")]
        [Min(0f)] public float sheriffNoticeDistance = 30f;
        [Tooltip("After this much of the round NPC murderers go looking for the sheriff anywhere on the map. Above the round length = never.")]
        [Min(0f)] public float sheriffManhuntAfterSeconds = 70f;
        [Tooltip("How far NPC murderers notice the sheriff once the manhunt is on (metres).")]
        [Min(0f)] public float manhuntNoticeDistance = 1000f;
        [Tooltip("Each further NPC murderer joins the manhunt this much later (seconds).")]
        [Min(0f)] public float manhuntStaggerSeconds = 35f;
        [Tooltip("Inside this distance of an armed sheriff an NPC murderer creeps instead of charging (metres). 0 = always charge.")]
        [Min(0f)] public float sheriffCreepDistance = 22f;
        [Tooltip("How fast an NPC murderer creeps up on an armed sheriff (m/s). Walking is 18 and a charge 21.")]
        [Min(0.5f)] public float sheriffCreepSpeed = 6f;
        [Tooltip("Multiplies an NPC's attack detection radius for an armed sheriff (1 = like anyone else).")]
        [Min(0f)] public float sheriffDetectionMultiplier = 1.5f;
        [Tooltip("Extra target priority an armed sheriff gets with NPC murderers (0 = none).")]
        [Min(0f)] public float sheriffTargetPriority = 1f;
        [Tooltip("NPC murderers carry a random knife skin up to this one (index into MM_KnifeSkins).")]
        [Min(0)] public int npcKnifeSkinMax = 2;

        [Header("Sheriff")]
        [Tooltip("Shots in the sheriff's gun before it reloads (MM2 style: one).")]
        [Min(1)] public int gunClip = 1;
        [Tooltip("Reload time after the clip is empty (seconds).")]
        [Min(0.1f)] public float gunReloadSeconds = 2.2f;
        [Tooltip("What happens to a sheriff who shoots an innocent.")]
        public MM_WrongShotPenalty wrongShotPenalty = MM_WrongShotPenalty.SheriffDies;
        [Tooltip("With the Jam penalty: how long the gun stays jammed (seconds).")]
        [Min(0f)] public float wrongShotJamSeconds = 20f;

        [Header("Dropped gun")]
        [Tooltip("A dropped gun floats this high above the floor where the sheriff fell (metres).")]
        [Min(0f)] public float gunFloatHeight = 1.6f;
        [Tooltip("How far from the body to look for floor to put the gun on (metres).")]
        [Min(0.5f)] public float gunFloorSearchRadius = 8f;
        [Tooltip("Players get this long to reach the gun first; then the nearest NPC townsperson goes for it and becomes the hero. Negative = NPCs never take it.")]
        public float npcHeroDelaySeconds = 10f;
        [Tooltip("How fast that NPC heads for the gun (m/s). Walking is 18.")]
        [Min(1f)] public float npcHeroSpeed = 20f;
        [Tooltip("The NPC takes the gun this close to it (metres).")]
        [Min(0.5f)] public float npcHeroReachDistance = 4f;
        [Tooltip("An NPC that cannot reach the gun in this long gives up and the next nearest one tries (seconds).")]
        [Min(1f)] public float npcHeroGiveUpSeconds = 20f;
        [Tooltip("An NPC that gets less than Npc Hero Stall Progress closer along its path in this long is stuck " +
                 "(on furniture, say) and gives up early (seconds).")]
        [Min(0.5f)] public float npcHeroStallSeconds = 3f;
        [Tooltip("How much closer to the gun an NPC must get in each Npc Hero Stall Seconds (metres).")]
        [Min(0f)] public float npcHeroStallProgress = 1f;
        [Tooltip("How often the round looks for the next NPC to send while the gun lies unclaimed (seconds).")]
        [Min(0.1f)] public float npcHeroSearchInterval = 1f;

        [Header("NPC sheriff")]
        [Tooltip("An NPC sheriff sees a drawn knife or a kill this far away (metres), given a clear line.")]
        [Min(1f)] public float npcSheriffSightDistance = 45f;
        [Tooltip("Half-angle of the NPC sheriff's view (degrees). Kills it hears behind it within the hearing distance also count.")]
        [Range(10f, 180f)] public float npcSheriffViewAngle = 75f;
        [Tooltip("An NPC sheriff hears a kill this close even without seeing it (metres).")]
        [Min(0f)] public float npcSheriffHearingDistance = 14f;
        [Tooltip("Seconds between spotting the murderer and the first shot (random between x and y).")]
        public Vector2 npcSheriffReactionSeconds = new(0.9f, 1.6f);
        [Tooltip("How far off an NPC sheriff's aim drifts per 10 m of distance (metres), so long shots can miss.")]
        [Min(0f)] public float npcSheriffAimErrorPer10m = 0.55f;
        [Tooltip("An NPC sheriff forgets a murderer it has not seen for this long (seconds).")]
        [Min(1f)] public float npcSheriffMemorySeconds = 12f;
        [Tooltip("NPC sheriffs open fire within this range (metres).")]
        [Min(5f)] public float npcSheriffFireRange = 38f;

        [Header("Townsfolk NPCs")]
        [Tooltip("Townsfolk run from a murderer with the knife out this close (metres).")]
        [Min(0f)] public float npcPanicDistance = 22f;
        [Tooltip("Townsfolk keep running from a murderer for this long after seeing a kill (seconds).")]
        [Min(0f)] public float npcPanicAfterKillSeconds = 6f;
        [Tooltip("Fleeing townsfolk stop running once the murderer is this many times the panic distance away.")]
        [Min(1f)] public float npcPanicClearMultiplier = 1.5f;

        [Header("Coins")]
        [Tooltip("Coins a player's bag holds per round.")]
        [Min(1)] public int coinBagSize = 40;
        [Tooltip("The map keeps at most this many coins lying around.")]
        [Min(0)] public int coinsOnMap = 45;
        [Tooltip("Seconds between coin spawns while the map is below its limit.")]
        [Min(0.1f)] public float coinSpawnInterval = 0.8f;
        [Tooltip("Coins spawned at once when the round starts.")]
        [Min(0)] public int coinsAtStart = 30;

        [Header("Rewards (XP)")]
        [Min(0)] public int xpPerKill = 10;
        [Tooltip("For the sheriff or hero who shoots a murderer.")]
        [Min(0)] public int xpForStoppingMurderer = 40;
        [Min(0)] public int xpForSurviving = 15;
        [Min(0)] public int xpForWinning = 25;
        [Tooltip("XP per coin bagged.")]
        [Min(0)] public int xpPerCoin = 1;

        [Header("Endgame")]
        [Tooltip("When no player is left alive the round is cut to at most this many seconds, and NPC murderers stop waiting between kills.")]
        [Min(0)] public int secondsLeftWhenPlayersOut = 25;
        [Tooltip("Players this close to a kill hear and see it (kill sting, sparks). The victim and the killer always do.")]
        [Min(0f)] public float killAudibleDistance = 45f;

        /// <summary>How many murderers and sheriffs a round with this many characters gets.</summary>
        public int MurderersFor(int characters) => MM_RoleOdds.RoleCount(characters, charactersPerMurderer);
        public int SheriffsFor(int characters) => MM_RoleOdds.RoleCount(characters, charactersPerSheriff);

        /// <summary>How many NPCs join a round with <paramref name="players"/> players.</summary>
        public int NpcsFor(int players) => Mathf.Max(minNpcs, fillToCharacters - players);

        /// <summary>
        /// How close an armed sheriff must be before an NPC murderer that may kill goes for them.
        /// <paramref name="hunterRank"/> is the murderer's place in the manhunt (0 = first to join).
        /// </summary>
        public float SheriffNoticeDistance(float huntSecondsElapsed, int hunterRank) =>
            huntSecondsElapsed >= sheriffManhuntAfterSeconds + hunterRank * manhuntStaggerSeconds
                ? Mathf.Max(sheriffNoticeDistance, manhuntNoticeDistance)
                : sheriffNoticeDistance;
    }

    public enum MM_WrongShotPenalty : byte {
        /// <summary>MM2: shooting an innocent kills the sheriff, and the gun drops.</summary>
        SheriffDies,
        /// <summary>The gun jams for a while.</summary>
        Jam,
    }
}
