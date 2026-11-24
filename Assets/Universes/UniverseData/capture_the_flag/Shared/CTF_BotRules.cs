using System;
using UnityEngine;

namespace Universes.UniverseData.capture_the_flag {
    /// <summary>How good the bots are. Sent over the wire: only ever append.</summary>
    public enum CTF_BotSkill : byte { Recruit, Regular, Veteran, Elite }

    /// <summary>What a bot is for when nothing urgent is happening.</summary>
    public enum CTF_BotRole : byte {
        /// <summary>Goes for the enemy flag.</summary>
        Attacker,
        /// <summary>Holds near its own stand and returns its own flag.</summary>
        Defender,
        /// <summary>Fights in the middle and escorts its carrier.</summary>
        Midfielder,
    }

    /// <summary>What a bot is doing right now (chosen by <see cref="CTF_BotRules.GoalFor"/>).</summary>
    public enum CTF_BotGoal : byte { Wait, TakeFlag, CarryHome, ReturnFlag, ChaseCarrier, Escort, Defend, HoldMiddle, Heal }

    /// <summary>What a bot knows when it picks a goal.</summary>
    public struct CTF_BotSituation {
        public CTF_BotRole role;
        /// <summary>It carries the enemy flag.</summary>
        public bool carrying;
        public CTF_FlagState ourFlag;
        public CTF_FlagState theirFlag;
        /// <summary>Teammates (players and bots) nearer than it to its own flag: 0 = it is the nearest.</summary>
        public int rankToOurFlag;
        /// <summary>Teammates nearer than it to the enemy flag.</summary>
        public int rankToTheirFlag;
        /// <summary>Hurt, with a health power-up ready nearby.</summary>
        public bool wantsHealth;
    }

    /// <summary>One skill tier: how fast a bot notices, how well it aims, how fast it moves, what it carries.</summary>
    [Serializable]
    public sealed class CTF_BotSkillTuning {
        public CTF_BotSkill skill;
        [Tooltip("Seconds from first seeing an enemy to the first shot.")]
        [Min(0f)] public float reactionSeconds = 0.6f;
        [Tooltip("Extra random wait between shots, 0..this many seconds (players do not fire on the exact frame either).")]
        [Min(0f)] public float shotHesitation = 0.3f;
        [Tooltip("Aim error cone, in degrees, on top of the gun's own spread.")]
        [Min(0f)] public float aimErrorDegrees = 1.8f;
        [Tooltip("Extra aim error, in degrees, against a target crossing the line of fire at sprint speed.")]
        [Min(0f)] public float movingTargetErrorDegrees = 1.5f;
        [Tooltip("Metres at which it spots enemies (never farther than its gun reaches plus a little).")]
        [Min(5f)] public float sightRange = 55f;
        [Tooltip("Run speed in metres per second (players walk at 14 and sprint at 21).")]
        [Min(1f)] public float moveSpeed = 15.5f;
        [Tooltip("Sidesteps while fighting instead of standing still.")]
        public bool strafes = true;
        [Tooltip("Under this share of health it detours to a ready health power-up nearby.")]
        [Range(0f, 1f)] public float seekHealthBelow = 0.5f;
        [Tooltip("Guns bots of this tier carry; each bot keeps one for the match.")]
        public CTF_Weapon[] weapons = { CTF_Weapon.Sidearm };

        public CTF_BotSkillTuning() { }

        public CTF_BotSkillTuning(CTF_BotSkill skill, float reaction, float hesitation, float aimError, float movingError, float sight, float speed, bool strafes, float seekHealth, params CTF_Weapon[] weapons) {
            this.skill = skill;
            reactionSeconds = reaction;
            shotHesitation = hesitation;
            aimErrorDegrees = aimError;
            movingTargetErrorDegrees = movingError;
            sightRange = sight;
            moveSpeed = speed;
            this.strafes = strafes;
            seekHealthBelow = seekHealth;
            this.weapons = weapons;
        }
    }

    /// <summary>What the players on one team bring to the bots' tier choice (see <see cref="CTF_BotRules.Plan"/>).</summary>
    public struct CTF_TeamStanding {
        /// <summary>Players on the team (0 = an all-bot team).</summary>
        public int humans;
        /// <summary>Their average Flag Wars level (the tier's ceiling).</summary>
        public float level;
        /// <summary>The lowest saved tier among them (see <see cref="CTF_BotRules.NextTier"/>).</summary>
        public int tier;
        /// <summary>The longest run of lost matches among them.</summary>
        public int lossStreak;
        /// <summary>Someone on the team lost their last match.</summary>
        public bool lostLast;
        /// <summary>Someone on the team has never won a match.</summary>
        public bool newcomer;
    }

    /// <summary>The bots' setup for a match: each team's tier and how many bots short of the team size it plays.</summary>
    public struct CTF_BotPlan {
        public CTF_BotSkill red, blue;
        public int redShort, blueShort;
        /// <summary>The tier both teams start from (before a team's help).</summary>
        public CTF_BotSkill baseSkill;
        public CTF_BotSkill SkillOf(CTF_Team team) => team == CTF_Team.Red ? red : blue;
        public int ShortOf(CTF_Team team) => team == CTF_Team.Red ? redShort : blueShort;
    }

    /// <summary>How the tier is chosen (from the server's squad tuning, passed to <see cref="CTF_BotRules.Plan"/>).</summary>
    public struct CTF_BotPlanTuning {
        /// <summary>Level at which each tier becomes possible (index = tier).</summary>
        public int[] skillLevels;
        /// <summary>Players below this level get the bots on their side one tier above the bots against them.</summary>
        public int assistBelowLevel;
        /// <summary>...and so does a player at any level after this many lost matches in a row (0 = never).</summary>
        public int assistAfterLosses;
        /// <summary>Players who have never won face a team this many bots short of the team size.</summary>
        public int newcomerEnemyShort;
    }

    /// <summary>
    /// Pure rules for bots: how many each team needs, which tier plays, which role each slot takes and
    /// how a bot aims. The server's <c>CTF_BotSquad</c> and <c>CTF_Bot</c> use them; EditMode tests
    /// cover them.
    /// </summary>
    public static class CTF_BotRules {
        /// <summary>
        /// Bots on a team with <paramref name="humans"/> players so that it fields
        /// <paramref name="teamSize"/>; none when filling is off.
        /// </summary>
        public static int BotsFor(int humans, int teamSize, bool fill) => fill ? Math.Max(0, teamSize - Math.Max(0, humans)) : 0;

        /// <summary>As <see cref="BotsFor(int, int, bool)"/>, <paramref name="shortBy"/> bots fewer (never below none).</summary>
        public static int BotsFor(int humans, int teamSize, bool fill, int shortBy) => Math.Max(0, BotsFor(humans, teamSize, fill) - Math.Max(0, shortBy));

        /// <summary>
        /// Whether a bot with <paramref name="goal"/> keeps walking to it while it shoots. Goals about a flag
        /// (take, carry home, return, chase its carrier, escort ours) push on under fire, so a match is not
        /// a stand-off in the middle; defending, holding the middle or healing stop (or sidestep) to fight.
        /// </summary>
        public static bool ShootsOnTheMove(CTF_BotGoal goal) =>
            goal is CTF_BotGoal.TakeFlag or CTF_BotGoal.CarryHome or CTF_BotGoal.ReturnFlag or CTF_BotGoal.ChaseCarrier or CTF_BotGoal.Escort;

        /// <summary>The default tiers, from a bot that mostly misses to one that rarely does.</summary>
        public static CTF_BotSkillTuning[] DefaultTiers() => new[] {
            // Recruit: slow to react and loose (try-1 shakedown: at 0.95 s / 3.2 degrees a level-1 player's team lost 0-3 twice).
            new CTF_BotSkillTuning(CTF_BotSkill.Recruit, 1.2f, 0.8f, 4.5f, 3.5f, 42f, 13.5f, false, 0.35f, CTF_Weapon.Sidearm),
            new CTF_BotSkillTuning(CTF_BotSkill.Regular, 0.65f, 0.35f, 2f, 1.8f, 55f, 15.5f, true, 0.45f, CTF_Weapon.Sidearm, CTF_Weapon.BurstRifle),
            new CTF_BotSkillTuning(CTF_BotSkill.Veteran, 0.45f, 0.2f, 1.2f, 1.2f, 65f, 17f, true, 0.5f, CTF_Weapon.Sidearm, CTF_Weapon.BurstRifle, CTF_Weapon.Scattergun),
            new CTF_BotSkillTuning(CTF_BotSkill.Elite, 0.3f, 0.08f, 0.7f, 0.8f, 75f, 18.5f, true, 0.55f, CTF_Weapon.BurstRifle, CTF_Weapon.Scattergun, CTF_Weapon.Marksman),
        };

        /// <summary>
        /// The tier for players of average level <paramref name="averageLevel"/>: the highest tier whose
        /// threshold it reaches (<paramref name="levelThresholds"/>[i] is the level tier i starts at),
        /// moved by <paramref name="adjust"/> tiers (the last match's result), clamped to the tiers.
        /// </summary>
        public static CTF_BotSkill SkillFor(float averageLevel, int[] levelThresholds, int adjust) {
            int tier = 0;
            if (levelThresholds != null)
                for (int i = 0; i < levelThresholds.Length; i++)
                    if (averageLevel >= levelThresholds[i]) tier = i;
            int max = Enum.GetValues(typeof(CTF_BotSkill)).Length - 1;
            return (CTF_BotSkill)Math.Clamp(tier + adjust, 0, max);
        }

        /// <summary>Tiers there are (Recruit .. Elite).</summary>
        public const int TierCount = 4;

        /// <summary>The toughest tier <paramref name="level"/> allows (the tier whose threshold it last reached).</summary>
        public static int Ceiling(float level, int[] levelThresholds) => (int)SkillFor(level, levelThresholds, 0);

        /// <summary>
        /// A player's tier after a match won (or lost) by <paramref name="margin"/> captures: one up after
        /// a clear win (by <paramref name="clearMargin"/> or more), one down after a clear loss, never
        /// above <paramref name="ceiling"/> (their level's) or below Recruit. New players start at Recruit.
        /// </summary>
        public static int NextTier(int tier, int margin, int clearMargin, int ceiling) {
            int cap = Math.Clamp(ceiling, 0, TierCount - 1);
            int t = Math.Clamp(tier, 0, cap);
            int clear = Math.Max(1, clearMargin);
            if (margin >= clear) t++;
            else if (margin <= -clear) t--;
            return Math.Clamp(t, 0, cap);
        }

        /// <summary>Lost matches in a row after one ending <paramref name="margin"/> (a draw leaves the run as it was).</summary>
        public static int NextLossStreak(int streak, int margin) => margin < 0 ? Math.Max(0, streak) + 1 : margin > 0 ? 0 : Math.Max(0, streak);

        /// <summary>Whether a team's players get the stronger bots beside them (new players, or a losing run).</summary>
        public static bool Assisted(in CTF_TeamStanding team, in CTF_BotPlanTuning tuning) =>
            team.humans > 0 && (team.level < tuning.assistBelowLevel || (tuning.assistAfterLosses > 0 && team.lossStreak >= tuning.assistAfterLosses));

        /// <summary>
        /// The bots' tiers and team sizes for a match. Both teams start from the lowest tier the players
        /// have earned (by results, capped by level), plus the Tough Bots twist unless a player lost
        /// their last match. A team whose players are new or on a losing run gets its bots one tier up
        /// (when the other team's players are not in the same spot), and a team facing a player who has
        /// never won plays <see cref="CTF_BotPlanTuning.newcomerEnemyShort"/> bots short.
        /// </summary>
        public static CTF_BotPlan Plan(in CTF_TeamStanding red, in CTF_TeamStanding blue, in CTF_BotPlanTuning tuning, int toughBonus) {
            int max = TierCount - 1;
            bool anyHumans = red.humans > 0 || blue.humans > 0;
            int baseTier = 0;
            if (anyHumans) {
                baseTier = max;
                if (red.humans > 0) baseTier = Math.Min(baseTier, Math.Min(red.tier, Ceiling(red.level, tuning.skillLevels)));
                if (blue.humans > 0) baseTier = Math.Min(baseTier, Math.Min(blue.tier, Ceiling(blue.level, tuning.skillLevels)));
            }
            bool lostLast = (red.humans > 0 && red.lostLast) || (blue.humans > 0 && blue.lostLast);
            if (anyHumans && !lostLast) baseTier = Math.Clamp(baseTier + Math.Max(0, toughBonus), 0, max);
            bool redHelp = Assisted(red, tuning), blueHelp = Assisted(blue, tuning);
            bool redNew = red.humans > 0 && red.newcomer, blueNew = blue.humans > 0 && blue.newcomer;
            int shortBy = Math.Max(0, tuning.newcomerEnemyShort);
            return new CTF_BotPlan {
                baseSkill = (CTF_BotSkill)baseTier,
                red = (CTF_BotSkill)Math.Clamp(baseTier + (redHelp && !blueHelp ? 1 : 0), 0, max),
                blue = (CTF_BotSkill)Math.Clamp(baseTier + (blueHelp && !redHelp ? 1 : 0), 0, max),
                redShort = blueNew && !redNew ? shortBy : 0,
                blueShort = redNew && !blueNew ? shortBy : 0,
            };
        }

        /// <summary>
        /// The HUD chip for the bots from one player's side: "4v3 · Recruit bots · Regular with you" when
        /// the sides differ, "4v4 · Regular bots" when they do not.
        /// </summary>
        public static string ChipText(int teamSize, CTF_BotSkill mine, CTF_BotSkill theirs, int myShort, int theirShort) {
            int us = Math.Max(1, teamSize - Math.Max(0, myShort)), them = Math.Max(1, teamSize - Math.Max(0, theirShort));
            string text = $"{us}v{them} · {theirs} bots";
            return mine != theirs ? $"{text} · {mine} with you" : text;
        }

        /// <summary>
        /// The side a lone player (or the first of an even split) starts on: random, so neither colour's
        /// base is the only one a solo player ever plays from.
        /// </summary>
        public static CTF_Team FirstSide(int coin) => (coin & 1) == 0 ? CTF_Team.Red : CTF_Team.Blue;

        /// <summary>
        /// Role of the bot in slot <paramref name="index"/> of a team fielding <paramref name="bots"/>
        /// bots beside <paramref name="humans"/> players: two thirds attack, a defender once the team is
        /// three or more strong, the rest hold the middle.
        /// </summary>
        public static CTF_BotRole RoleFor(int index, int bots, int humans) {
            int size = bots + humans;
            // A lone bot or a pair always goes for the flag: someone has to score.
            if (size <= 2) return CTF_BotRole.Attacker;
            // The last slot defends (humans tend to attack); a big team gets a midfielder too.
            if (index == bots - 1) return CTF_BotRole.Defender;
            if (size >= 5 && index == bots - 2) return CTF_BotRole.Midfielder;
            return CTF_BotRole.Attacker;
        }

        /// <summary>
        /// The goal for a bot in <paramref name="s"/>. The flags come first: carry it home, send a dropped
        /// flag of ours home (the nearest <paramref name="returners"/> teammates, and defenders), run down
        /// our carrier (the nearest <paramref name="chasers"/>, and defenders); then health; then the role:
        /// attackers take the enemy flag or escort their carrier, midfielders hold the middle and escort,
        /// defenders guard the stand. Anyone grabs a dropped enemy flag if nobody is nearer.
        /// </summary>
        public static CTF_BotGoal GoalFor(in CTF_BotSituation s, int chasers, int returners) {
            if (s.carrying) return CTF_BotGoal.CarryHome;
            bool defender = s.role == CTF_BotRole.Defender;
            if (s.ourFlag == CTF_FlagState.Dropped && (defender || s.rankToOurFlag < returners)) return CTF_BotGoal.ReturnFlag;
            if (s.ourFlag == CTF_FlagState.Carried && (defender || s.rankToOurFlag < chasers)) return CTF_BotGoal.ChaseCarrier;
            if (s.wantsHealth) return CTF_BotGoal.Heal;
            if (s.theirFlag == CTF_FlagState.Dropped && (!defender || s.rankToTheirFlag == 0)) return CTF_BotGoal.TakeFlag;
            switch (s.role) {
                case CTF_BotRole.Attacker:
                    return s.theirFlag == CTF_FlagState.Carried ? CTF_BotGoal.Escort : CTF_BotGoal.TakeFlag;
                case CTF_BotRole.Midfielder:
                    return s.theirFlag == CTF_FlagState.Carried ? CTF_BotGoal.Escort : CTF_BotGoal.HoldMiddle;
                default:
                    return CTF_BotGoal.Defend;
            }
        }

        /// <summary>
        /// Aim error in degrees: the tier's base error plus its moving-target error scaled by how fast the
        /// target crosses the line of fire (<paramref name="crossingSpeed"/> over <paramref name="sprintSpeed"/>).
        /// </summary>
        public static float AimErrorDegrees(CTF_BotSkillTuning tier, float crossingSpeed, float sprintSpeed) {
            if (tier == null) return 0f;
            float crossing = sprintSpeed > 0f ? Mathf.Clamp01(Mathf.Abs(crossingSpeed) / sprintSpeed) : 0f;
            return tier.aimErrorDegrees + tier.movingTargetErrorDegrees * crossing;
        }

        /// <summary>How far an aim error of <paramref name="degrees"/> lands from the target at <paramref name="distance"/> metres.</summary>
        public static float MissRadius(float degrees, float distance) => Mathf.Tan(Mathf.Max(0f, degrees) * Mathf.Deg2Rad) * Mathf.Max(0f, distance);

        /// <summary>The weapon of the bot at place <paramref name="slot"/> in its team (cycles through the tier's list).</summary>
        public static CTF_Weapon WeaponFor(CTF_BotSkillTuning tier, int slot) {
            if (tier?.weapons == null || tier.weapons.Length == 0) return CTF_Weapon.Sidearm;
            return tier.weapons[Math.Abs(slot) % tier.weapons.Length];
        }

        /// <summary>Bot call-signs; a slot keeps its name for the match.</summary>
        public static readonly string[] Names = {
            "Rivet", "Sparky", "Bolt", "Gizmo", "Widget", "Servo", "Piston", "Cog",
            "Ratchet", "Diode", "Turbo", "Socket", "Gasket", "Flux", "Volt", "Torque",
        };

        /// <summary>The name for slot <paramref name="slot"/>: a call-sign with a bot tag, so nobody mistakes it for a person.</summary>
        public static string NameFor(int slot, string format) {
            string callSign = Names[Math.Abs(slot) % Names.Length];
            return string.IsNullOrEmpty(format) ? callSign : string.Format(format, callSign);
        }
    }
}
