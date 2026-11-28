using System;
using System.Collections.Generic;
using UnityEngine;

namespace Universes.UniverseData.capture_the_flag {
    /// <summary>A moment worth calling out. Sent over the wire: only ever append.</summary>
    public enum CTF_Medal : byte {
        FirstBlood, DoubleKill, TripleKill, Overkill, KillingSpree, Rampage, Unstoppable,
        CarrierDown, Defender, Bodyguard, Payback, FlagSaved, Assist, Mvp, HatTrick
    }

    /// <summary>How a medal looks and what it pays.</summary>
    public sealed class CTF_MedalDefinition {
        public readonly CTF_Medal medal;
        public readonly string title;
        /// <summary>Theme icon name.</summary>
        public readonly string icon;
        /// <summary>Game XP before <see cref="CTF_MedalTuning.xpMultiplier"/>.</summary>
        public readonly int xp;
        /// <summary>Everyone hears about it ("Rivet is on a RAMPAGE!"), not only the one who earned it.</summary>
        public readonly bool announce;
        /// <summary>Gold, warm colours for the big ones; cool for teamwork.</summary>
        public readonly bool major;

        public CTF_MedalDefinition(CTF_Medal medal, string title, string icon, int xp, bool announce, bool major) {
            this.medal = medal;
            this.title = title;
            this.icon = icon;
            this.xp = xp;
            this.announce = announce;
            this.major = major;
        }
    }

    /// <summary>Server tuning for medals (time windows, distances, XP scale).</summary>
    [Serializable]
    public sealed class CTF_MedalTuning {
        [Tooltip("Kills this close together (seconds) chain into Double, Triple and Overkill.")]
        [Min(0.5f)] public float multiKillWindow = 4.5f;
        [Tooltip("A kill this close (metres) to your own flag stand while your flag is home is a Defender medal.")]
        [Min(1f)] public float defendRadius = 22f;
        [Tooltip("A kill this close (metres) to a teammate carrying the enemy flag is a Bodyguard medal.")]
        [Min(1f)] public float bodyguardRadius = 16f;
        [Tooltip("Damage dealt this recently (seconds) before someone else finishes the enemy earns an Assist.")]
        [Min(0.5f)] public float assistWindow = 5f;
        [Tooltip("Kill streaks that earn a medal: Killing Spree, Rampage, Unstoppable.")]
        public int[] streakMedals = { 3, 5, 8 };
        [Tooltip("Captures by one player in one match for a Hat Trick.")]
        [Min(2)] public int hatTrickCaptures = 3;
        [Tooltip("Scales every medal's XP.")]
        [Min(0f)] public float xpMultiplier = 1f;
    }

    /// <summary>What was true when someone scored a kill (gathered by the server).</summary>
    public struct CTF_KillContext {
        /// <summary>No one had scored a kill yet this match.</summary>
        public bool firstOfMatch;
        /// <summary>The victim was carrying the killer's team's flag.</summary>
        public bool victimCarriedOurFlag;
        /// <summary>The victim was within the defend radius of the killer's stand while the killer's flag was home.</summary>
        public bool nearOwnStand;
        /// <summary>The victim was near a teammate of the killer who carries the enemy flag.</summary>
        public bool protectedCarrier;
        /// <summary>The victim was the last one to kill the killer.</summary>
        public bool revenge;
    }

    /// <summary>
    /// The medal table and one combatant's running tally: kill streak and multi-kill chain. Pure (time
    /// is passed in), so it is unit-tested; the server keeps one per player and bot.
    /// </summary>
    public sealed class CTF_MedalTracker {
        public static readonly CTF_MedalDefinition[] Definitions = {
            new(CTF_Medal.FirstBlood, "First Blood", "skull", 20, true, true),
            new(CTF_Medal.DoubleKill, "Double Kill", "star", 25, false, true),
            new(CTF_Medal.TripleKill, "Triple Kill", "star_medal", 50, true, true),
            new(CTF_Medal.Overkill, "Overkill", "crowned_skull", 80, true, true),
            new(CTF_Medal.KillingSpree, "Killing Spree", "local_fire_department", 30, true, true),
            new(CTF_Medal.Rampage, "Rampage", "warlord", 50, true, true),
            new(CTF_Medal.Unstoppable, "Unstoppable", "power", 80, true, true),
            new(CTF_Medal.CarrierDown, "Carrier Down", "flying_flag", 30, false, false),
            new(CTF_Medal.Defender, "Defender", "templar_shield", 20, false, false),
            new(CTF_Medal.Bodyguard, "Bodyguard", "shield", 20, false, false),
            new(CTF_Medal.Payback, "Payback", "replay", 15, false, false),
            new(CTF_Medal.FlagSaved, "Flag Saved", "home", 15, false, false),
            new(CTF_Medal.Assist, "Assist", "groups", 5, false, false),
            new(CTF_Medal.Mvp, "MVP", "laurels_trophy", 60, true, true),
            new(CTF_Medal.HatTrick, "Hat Trick", "emoji_events", 60, true, true),
        };

        public static CTF_MedalDefinition Get(CTF_Medal medal) {
            int i = (int)medal;
            return i >= 0 && i < Definitions.Length ? Definitions[i] : Definitions[0];
        }

        /// <summary>A medal's XP with the tuning's multiplier applied.</summary>
        public static int Xp(CTF_Medal medal, CTF_MedalTuning tuning) =>
            Mathf.RoundToInt(Get(medal).xp * (tuning != null ? Mathf.Max(0f, tuning.xpMultiplier) : 1f));

        float lastKillAt = float.NegativeInfinity;

        /// <summary>Kills since the last death.</summary>
        public int Streak { get; private set; }
        /// <summary>Longest streak this match.</summary>
        public int BestStreak { get; private set; }
        /// <summary>Kills in the current multi-kill chain.</summary>
        public int Chain { get; private set; }

        /// <summary>A death ends the streak and the chain.</summary>
        public void Died() {
            Streak = 0;
            Chain = 0;
            lastKillAt = float.NegativeInfinity;
        }

        /// <summary>Starts a new match: streaks and chains begin again.</summary>
        public void Reset() {
            Died();
            BestStreak = 0;
        }

        /// <summary>
        /// Records a kill at <paramref name="now"/> (seconds) and adds the medals it earns to
        /// <paramref name="into"/>, most important first. Returns true when it was a multi-kill.
        /// </summary>
        public bool Kill(float now, in CTF_KillContext context, CTF_MedalTuning tuning, List<CTF_Medal> into) {
            tuning ??= new CTF_MedalTuning();
            Chain = now - lastKillAt <= tuning.multiKillWindow ? Chain + 1 : 1;
            lastKillAt = now;
            Streak++;
            BestStreak = Math.Max(BestStreak, Streak);

            bool multi = Chain >= 2;
            if (Chain == 2) into.Add(CTF_Medal.DoubleKill);
            else if (Chain == 3) into.Add(CTF_Medal.TripleKill);
            else if (Chain >= 4) into.Add(CTF_Medal.Overkill);

            int[] streaks = tuning.streakMedals ?? Array.Empty<int>();
            if (streaks.Length > 0 && Streak == streaks[0]) into.Add(CTF_Medal.KillingSpree);
            else if (streaks.Length > 1 && Streak == streaks[1]) into.Add(CTF_Medal.Rampage);
            else if (streaks.Length > 2 && Streak == streaks[2]) into.Add(CTF_Medal.Unstoppable);

            if (context.victimCarriedOurFlag) into.Add(CTF_Medal.CarrierDown);
            else if (context.nearOwnStand) into.Add(CTF_Medal.Defender);
            if (context.protectedCarrier) into.Add(CTF_Medal.Bodyguard);
            if (context.firstOfMatch) into.Add(CTF_Medal.FirstBlood);
            if (context.revenge) into.Add(CTF_Medal.Payback);
            return multi;
        }
    }
}
