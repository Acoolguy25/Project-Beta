using System;
using UnityEngine;

namespace Universes.UniverseData.capture_the_flag {
    public enum CTF_Team : byte { Red, Blue }
    public enum CTF_FlagState : byte { Home, Carried, Dropped }
    /// <summary>What a mid-lane power-up gives. Stored in the map scene, so only ever append.</summary>
    public enum CTF_PowerUpKind : byte { Health, Speed }
    /// <summary>How someone died, for the kill feed icon. Only ever append.</summary>
    public enum CTF_KillCause : byte { Other, Gun, Melee, Fall }

    /// <summary>
    /// Pays a flag grab once per trip off its stand: the first pickup after the flag left home pays,
    /// pickups of the same flag dropped and picked up again do not, until it is home again.
    /// </summary>
    public struct CTF_GrabPay {
        bool paid;
        /// <summary>A pickup: true when it pays.</summary>
        public bool Grab() {
            if (paid) return false;
            paid = true;
            return true;
        }
        /// <summary>The flag is back on its stand (returned or captured).</summary>
        public void Home() => paid = false;
    }

    /// <summary>Numbers and pure rules both sides agree on.</summary>
    public static class CTF_Rules {
        public const int CapturesToWin = 3;
        public const float PickupRange = 2.6f;
        public const float CaptureRange = 4f;
        public const float DropReturnSeconds = 20f;
        /// <summary>
        /// Seconds one defender must stand on a dropped flag to send it home. A touch-return made every
        /// drop at the stand an instant reset (live run: 40 grabs, 1 capture in 8 minutes), so a fight over
        /// a dropped flag now lasts long enough for the attackers to pick it up again.
        /// </summary>
        public const float DefaultReturnHoldSeconds = 1.6f;
        /// <summary>Each extra defender on the flag adds this share of one defender's speed.</summary>
        public const float ExtraReturnerShare = 0.5f;
        public const float CarrierSpeedMultiplier = 0.85f;
        /// <summary>How close a character must be to take a power-up.</summary>
        public const float PowerUpRange = 3f;
        /// <summary>Power-up availability travels as one bit each in a <see cref="uint"/>.</summary>
        public const int MaxPowerUps = 32;
        /// <summary>Height difference that still counts as "the same floor" for reach checks.</summary>
        public const float ReachHeight = 3f;

        public static CTF_Team Other(CTF_Team team) => team == CTF_Team.Red ? CTF_Team.Blue : CTF_Team.Red;

        public static string Name(CTF_Team team) => team == CTF_Team.Red ? "RED" : "BLUE";
        public static string Hex(CTF_Team team) => team == CTF_Team.Red ? "#FF5A4E" : "#4EA6FF";
        public static Color Color(CTF_Team team) => team == CTF_Team.Red ? new Color(1f, 0.32f, 0.27f) : new Color(0.28f, 0.6f, 1f);

        /// <summary>
        /// Time is up with the score level: play sudden death (next capture wins, both flags reset) when
        /// it is on and not already being played.
        /// </summary>
        public static bool StartsSuddenDeath(int redScore, int blueScore, int secondsLeft, bool inSuddenDeath, int suddenDeathSeconds) =>
            secondsLeft <= 0 && redScore == blueScore && !inSuddenDeath && suddenDeathSeconds > 0;

        /// <summary>Whether play ends now: a team reached the target, time ran out, or a sudden-death capture broke the tie.</summary>
        public static bool MatchOver(int redScore, int blueScore, int capturesToWin, int secondsLeft, bool inSuddenDeath) =>
            redScore >= capturesToWin || blueScore >= capturesToWin || secondsLeft <= 0 || (inSuddenDeath && redScore != blueScore);

        /// <summary>
        /// One step of returning a dropped flag. <paramref name="returners"/> defenders in reach push it up
        /// (the first at 1/<paramref name="holdSeconds"/> a second, each extra one adding
        /// <see cref="ExtraReturnerShare"/> of that); with nobody on it, it falls back at the base rate. 1 = home.
        /// </summary>
        public static float ReturnStep(float progress, int returners, float deltaSeconds, float holdSeconds) {
            if (holdSeconds <= 0f) return returners > 0 ? 1f : 0f;
            float rate = 1f / holdSeconds;
            float speed = returners > 0 ? rate * (1f + ExtraReturnerShare * (returners - 1)) : -rate;
            return Mathf.Clamp01(progress + speed * deltaSeconds);
        }

        /// <summary>The kill feed's middle word: the gun's name for a gun kill, "eliminated" otherwise.</summary>
        public static string KillVerb(CTF_KillCause cause, string gunTitle) =>
            cause == CTF_KillCause.Gun && !string.IsNullOrEmpty(gunTitle) ? $"[{gunTitle}]" : "eliminated";

        /// <summary>Joiners go to the smaller team; ties go to whichever team is losing, then <paramref name="tie"/> (the server passes a random side).</summary>
        public static CTF_Team PickTeam(int red, int blue, int redScore, int blueScore, CTF_Team tie = CTF_Team.Red) {
            if (red != blue) return red < blue ? CTF_Team.Red : CTF_Team.Blue;
            if (redScore != blueScore) return redScore < blueScore ? CTF_Team.Red : CTF_Team.Blue;
            return tie;
        }

        /// <summary>
        /// Whether a player's share of their team's win counts (win XP, the win stat and challenges). A
        /// player counts who was on the field for the match from early enough, joining before
        /// <paramref name="lateJoinShare"/> of it had gone, or who made a flag play for the team (a grab,
        /// capture or return), so nobody banks a win by joining a match in its last minute.
        /// <paramref name="joinedAtShare"/> is how much of the match had gone when they joined (0 = from the start).
        /// </summary>
        public static bool WinCounts(float joinedAtShare, bool madeFlagPlay, float lateJoinShare) =>
            madeFlagPlay || joinedAtShare <= lateJoinShare;

        /// <summary>How much of a match had gone at <paramref name="time"/>, 0..1 (0 when the match has no length yet).</summary>
        public static float ShareGone(float start, float end, float time) =>
            end > start ? Mathf.Clamp01((time - start) / (end - start)) : 0f;

        /// <summary>Within reach on the ground plane, ignoring small height differences (stairs, jumping).</summary>
        public static bool InReach(Vector3 a, Vector3 b, float range) {
            if (Mathf.Abs(a.y - b.y) > ReachHeight) return false;
            a.y = b.y = 0f;
            return (a - b).sqrMagnitude <= range * range;
        }

        /// <summary>Who won when the clock runs out, or null for a draw.</summary>
        public static CTF_Team? Winner(int redScore, int blueScore) =>
            redScore == blueScore ? null : redScore > blueScore ? CTF_Team.Red : CTF_Team.Blue;

        /// <summary>Whether power-up <paramref name="index"/> is ready in an availability mask.</summary>
        public static bool IsReady(uint mask, int index) => index >= 0 && index < MaxPowerUps && (mask & (1u << index)) != 0;

        /// <summary>The mask with power-up <paramref name="index"/> marked ready or taken.</summary>
        public static uint WithReady(uint mask, int index, bool ready) {
            if (index < 0 || index >= MaxPowerUps) throw new ArgumentOutOfRangeException(nameof(index), index, $"Power-ups are limited to {MaxPowerUps}.");
            return ready ? mask | (1u << index) : mask & ~(1u << index);
        }

        /// <summary>
        /// Walk/sprint multiplier with perks: <paramref name="carrierMultiplier"/> while carrying (1 with
        /// Flag Runner), the speed boost and a perk's own multiplier (Fleet Foot) stack on top.
        /// </summary>
        public static float SpeedMultiplier(bool carrying, float carrierMultiplier, bool boosted, float boost, float perk) =>
            (carrying ? carrierMultiplier : 1f) * (boosted ? boost : 1f) * perk;

        /// <summary>
        /// Whether state <paramref name="sequence"/> is newer than <paramref name="last"/>. Counts wrap
        /// around; a jump far back (a restarted server) also counts as newer, so a client never gets stuck.
        /// </summary>
        public static bool IsNewer(uint sequence, uint last) {
            int ahead = unchecked((int)(sequence - last));
            return ahead > 0 || ahead < -SequenceRestartGap;
        }

        /// <summary>
        /// Strength (0..1) of the low-health edge vignette: 0 above <paramref name="lowAt"/> (and when
        /// dead), <paramref name="atLowAt"/> at it, rising to 1 as health reaches 0. With two-hit kills
        /// the only living values are full and one hit left.
        /// </summary>
        public static float LowHealthEdge(float health01, float lowAt, float atLowAt) {
            if (health01 <= 0f || health01 > lowAt || lowAt <= 0f) return 0f;
            return Mathf.Lerp(1f, Mathf.Clamp01(atLowAt), health01 / lowAt);
        }

        /// <summary>
        /// How a toast, banner or feed line names a player: "You" for the local player, otherwise their
        /// name, or "An enemy" / "A teammate" when the name is missing, so no line starts with a blank.
        /// </summary>
        public static string PlayerLabel(string name, bool isLocal, bool teammate, bool startsSentence = true) {
            if (isLocal) return "You";
            if (!string.IsNullOrWhiteSpace(name)) return name;
            if (teammate) return startsSentence ? "A teammate" : "a teammate";
            return startsSentence ? "An enemy" : "an enemy";
        }

        /// <summary>Who did a flag event: a return is by the flag's own team, anything else by the other team.</summary>
        public static CTF_Team ActorTeam(CTF_EventKind kind, CTF_Team flag) =>
            kind == CTF_EventKind.Returned ? flag : Other(flag);

        /// <summary>A state this far behind the last one comes from a restarted server, not a late packet.</summary>
        public const int SequenceRestartGap = 1000;

        /// <summary>
        /// Actor ids at or above this are bots (players use their client id, far below it). Every id on
        /// the wire that names a player, carrier, killer or victim is an actor id; -1 means nobody.
        /// </summary>
        public const int BotIdBase = 1_000_000;

        public static bool IsBotId(int actorId) => actorId >= BotIdBase;

        public static int BotId(int slot) => BotIdBase + Math.Max(0, slot);

        /// <summary>How much a health power-up actually restores (never above max; 0 means "don't take it").</summary>
        public static long HealAmount(long health, long maxHealth, long heal) =>
            health <= 0 ? 0 : Math.Max(0L, Math.Min(heal, maxHealth - health));
    }
}
