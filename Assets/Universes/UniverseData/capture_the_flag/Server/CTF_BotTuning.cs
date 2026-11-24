using System;
using UnityEngine;

namespace Universes.UniverseData.capture_the_flag.Server {
    /// <summary>How bots are fielded: team size, tier choice and their skill tiers.</summary>
    [Serializable]
    public sealed class CTF_BotSquadTuning {
        [Tooltip("Fill both teams with bots up to the team size, so one player gets a real match.")]
        public bool fillWithBots = true;
        [Tooltip("Players per team, humans and bots together. A team with more humans than this gets no bots.")]
        [Min(1)] public int teamSize = 4;
        [Tooltip("Pick the tier from the players' results (capped by their level). Off: always use fixedSkill.")]
        public bool autoSkill = true;
        public CTF_BotSkill fixedSkill = CTF_BotSkill.Regular;
        [Tooltip("Level at which each tier becomes possible (index = tier: Recruit, Regular, Veteran, Elite). Everyone starts at Recruit; results move the tier, level only caps it.")]
        public int[] skillLevels = { 1, 4, 9, 15 };
        [Tooltip("A win (or loss) by this many captures moves the player's tier one up (or down).")]
        [Min(1)] public int adjustMargin = 2;
        [Tooltip("Players below this level get the bots on their side one tier above the bots against them.")]
        [Min(0)] public int assistBelowLevel = 6;
        [Tooltip("Players at any level get the same help after this many lost matches in a row (0 = never).")]
        [Min(0)] public int assistAfterLosses = 2;
        [Tooltip("A player who has never won faces a team this many bots short of the team size.")]
        [Min(0)] public int newcomerEnemyShort = 1;
        [Tooltip("QA: bots play a full match on both teams while every player waits in the lobby (a balance baseline). Never on in a release.")]
        public bool qaBotsOnly;
        [Tooltip("Name format for bots: {0} = call-sign.")]
        public string nameFormat = "{0} [BOT]";
        public CTF_BotSkillTuning[] tiers = CTF_BotRules.DefaultTiers();

        public CTF_BotPlanTuning PlanTuning() => new() {
            skillLevels = skillLevels, assistBelowLevel = assistBelowLevel, assistAfterLosses = assistAfterLosses,
            newcomerEnemyShort = newcomerEnemyShort,
        };

        public CTF_BotSkillTuning Tier(CTF_BotSkill skill) {
            if (tiers != null)
                foreach (var tier in tiers)
                    if (tier != null && tier.skill == skill) return tier;
            return CTF_BotRules.DefaultTiers()[(int)skill];
        }
    }

    /// <summary>How a bot thinks and moves, the same for every tier.</summary>
    [Serializable]
    public sealed class CTF_BotBrainTuning {
        [Tooltip("Seconds between goal decisions.")]
        [Min(0.05f)] public float thinkSeconds = 0.3f;
        [Tooltip("Seconds between looks for enemies.")]
        [Min(0.05f)] public float scanSeconds = 0.2f;
        [Tooltip("Field of view in degrees: enemies behind a bot go unseen unless they are very close or just shot it.")]
        [Range(30f, 360f)] public float viewDegrees = 220f;
        [Tooltip("Enemies this close (metres) are noticed from any side (footsteps, bumping into them).")]
        [Min(0f)] public float nearbySense = 10f;
        [Tooltip("An enemy that hurt the bot this many seconds ago or less is noticed from any side.")]
        [Min(0f)] public float hurtMemorySeconds = 3f;
        [Tooltip("Seconds a bot keeps shooting at an enemy it lost sight of before giving up on it.")]
        [Min(0f)] public float targetMemorySeconds = 1.2f;
        [Tooltip("A new enemy must be this much closer (share of the current one's distance) to switch to it.")]
        [Range(0.1f, 1f)] public float switchTargetCloser = 0.6f;
        [Tooltip("Teammates nearest to a stolen flag who run down its carrier.")]
        [Min(1)] public int chasers = 2;
        [Tooltip("Teammates nearest to a dropped flag of theirs who go to return it.")]
        [Min(1)] public int returners = 1;
        [Tooltip("Defenders patrol this far around their stand (metres).")]
        [Min(1f)] public float defendRadius = 12f;
        [Tooltip("Midfielders patrol this far around the middle of the arena (metres).")]
        [Min(1f)] public float middleRadius = 24f;
        [Tooltip("Seconds before a patrolling bot picks a new spot.")]
        public Vector2 patrolSeconds = new(3f, 6f);
        [Tooltip("Escorts run this far beside their carrier (metres).")]
        [Min(1f)] public float escortDistance = 7f;
        [Tooltip("Sidestep length while fighting (metres) and how long each lasts (seconds).")]
        [Min(0f)] public float strafeDistance = 5f;
        public Vector2 strafeSeconds = new(0.7f, 1.4f);
        [Tooltip("Guns shorter than this (metres) close in on their target instead of sidestepping.")]
        [Min(0f)] public float closeInBelowRange = 35f;
        [Tooltip("A hurt bot detours to a ready health power-up within this many metres.")]
        [Min(0f)] public float healthSearchRange = 45f;
        [Tooltip("Height of a bot's eyes and of the point it aims at on a target (metres; the robot is about 5.6 m tall).")]
        [Min(0f)] public float aimHeight = 3.2f;
        [Tooltip("Degrees per second a bot turns to face what it shoots at.")]
        [Min(30f)] public float turnDegreesPerSecond = 540f;
        [Tooltip("Without moving this far (metres) for stuckSeconds, a bot steps aside and tries again.")]
        [Min(0.05f)] public float stuckDistance = 0.6f;
        [Min(0.5f)] public float stuckSeconds = 2f;
        [Tooltip("How far a stuck bot steps aside (metres).")]
        [Min(1f)] public float unstickDistance = 8f;
        [Tooltip("Snap a spawn point onto the NavMesh within this many metres.")]
        [Min(0.5f)] public float navMeshSnap = 6f;
    }
}
