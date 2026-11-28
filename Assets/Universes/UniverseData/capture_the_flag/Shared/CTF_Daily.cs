using System;
using RyanAssets.Retention;

namespace Universes.UniverseData.capture_the_flag {
    /// <summary>A day's twist on the rules. Sent in <see cref="CTF_StateBroadcast.twist"/>: only ever append.</summary>
    public enum CTF_DailyTwist : byte { None, FlagFrenzy, PowerSurge, ToughBots, BurstRifleDay, ScattergunDay, MarksmanDay }

    /// <summary>
    /// The day's twist: every UTC day (<see cref="RetentionDay"/>, the daily challenges' calendar, so every
    /// server agrees) plays one rule change, so tomorrow's matches play differently from today's and not
    /// only the challenge list changes. Three days lend a locked gun to everyone (a taste of an unlock,
    /// and its eliminations pay double), the others pay double for flag play, bring power-ups back twice
    /// as fast, or sharpen the bots for more XP. Days walk through the twists in a shuffled order, never
    /// the same one two days running. Shared: the server plays it, the HUD names it.
    /// </summary>
    public static class CTF_Daily {
        static readonly CTF_DailyTwist[] Twists = {
            CTF_DailyTwist.FlagFrenzy, CTF_DailyTwist.PowerSurge, CTF_DailyTwist.ToughBots,
            CTF_DailyTwist.BurstRifleDay, CTF_DailyTwist.ScattergunDay, CTF_DailyTwist.MarksmanDay,
        };

        /// <summary>Today's twist.</summary>
        public static CTF_DailyTwist Today(DateTime utcNow) => Featured(RetentionDay.Of(utcNow));

        /// <summary>
        /// The twist of <paramref name="day"/>. Days go in blocks of six, each a shuffle of the twists seeded
        /// by the block; a block never starts with the twist the block before ended on. Changing this
        /// reshuffles the days, so only in a release.
        /// </summary>
        public static CTF_DailyTwist Featured(int day) {
            int n = Twists.Length;
            int block = FloorDiv(day, n), index = day - block * n;
            int[] order = Shuffle(block);
            if (order[0] == Shuffle(block - 1)[n - 1]) (order[0], order[1]) = (order[1], order[0]);
            return Twists[order[index]];
        }

        /// <summary>The gun everyone may use today, or false when today lends none.</summary>
        public static bool LoanedWeapon(CTF_DailyTwist twist, out CTF_Weapon weapon) {
            weapon = twist switch {
                CTF_DailyTwist.BurstRifleDay => CTF_Weapon.BurstRifle,
                CTF_DailyTwist.ScattergunDay => CTF_Weapon.Scattergun,
                CTF_DailyTwist.MarksmanDay => CTF_Weapon.Marksman,
                _ => CTF_Weapon.Sidearm,
            };
            return weapon != CTF_Weapon.Sidearm;
        }

        /// <summary><paramref name="owned"/> with today's loaned gun added.</summary>
        public static byte WithLoan(byte owned, CTF_DailyTwist twist) =>
            LoanedWeapon(twist, out var weapon) ? (byte)(owned | (1 << (int)weapon)) : owned;

        /// <summary>
        /// XP for an elimination: <paramref name="multiplier"/> times when it was made with today's loaned gun.
        /// </summary>
        public static int KillXp(int xp, CTF_Weapon used, CTF_DailyTwist twist, int multiplier) =>
            LoanedWeapon(twist, out var weapon) && used == weapon ? xp * Math.Max(1, multiplier) : xp;

        /// <summary>XP for a flag grab or capture: <paramref name="multiplier"/> times on Flag Frenzy.</summary>
        public static int FlagXp(int xp, CTF_DailyTwist twist, int multiplier) =>
            twist == CTF_DailyTwist.FlagFrenzy ? xp * Math.Max(1, multiplier) : xp;

        /// <summary>Seconds a power-up takes to come back: scaled by <paramref name="surgeScale"/> on Power Surge.</summary>
        public static float PowerUpRespawn(float seconds, CTF_DailyTwist twist, float surgeScale) =>
            twist == CTF_DailyTwist.PowerSurge ? seconds * Math.Clamp(surgeScale, 0.1f, 1f) : seconds;

        /// <summary>Tiers the bots move up today (Tough Bots), applied on top of the automatic choice.</summary>
        public static int BotTierBonus(CTF_DailyTwist twist, int toughTiers) => twist == CTF_DailyTwist.ToughBots ? Math.Max(0, toughTiers) : 0;

        /// <summary>The extra XP the match result pays today (Tough Bots), as a share of the result's XP, rounded.</summary>
        public static int ResultBonusXp(int resultXp, CTF_DailyTwist twist, float bonusShare) =>
            twist == CTF_DailyTwist.ToughBots ? (int)Math.Round(Math.Max(0, resultXp) * Math.Max(0f, bonusShare)) : 0;

        /// <summary>The twist's name for the HUD ("Flag Frenzy").</summary>
        public static string Title(CTF_DailyTwist twist) => twist switch {
            CTF_DailyTwist.FlagFrenzy => "Flag Frenzy",
            CTF_DailyTwist.PowerSurge => "Power Surge",
            CTF_DailyTwist.ToughBots => "Tough Bots",
            CTF_DailyTwist.BurstRifleDay => "Burst Rifle Day",
            CTF_DailyTwist.ScattergunDay => "Scattergun Day",
            CTF_DailyTwist.MarksmanDay => "Marksman Day",
            _ => "",
        };

        /// <summary>What the twist does, as a short clause for a chip ("grabs and captures pay 2x XP").</summary>
        public static string Effect(CTF_DailyTwist twist, int multiplier, float bonusShare) {
            int m = Math.Max(1, multiplier);
            return twist switch {
                CTF_DailyTwist.FlagFrenzy => $"grabs and captures pay {m}x XP",
                CTF_DailyTwist.PowerSurge => "power-ups come back twice as fast",
                CTF_DailyTwist.ToughBots => $"sharper bots, +{(int)Math.Round(bonusShare * 100f)}% match XP",
                CTF_DailyTwist.BurstRifleDay or CTF_DailyTwist.ScattergunDay or CTF_DailyTwist.MarksmanDay =>
                    $"everyone gets the {GunTitle(twist)} (G), its kills pay {m}x XP",
                _ => "",
            };
        }

        /// <summary>"Today: Flag Frenzy - grabs and captures pay 2x XP", or "" when there is no twist.</summary>
        public static string Describe(CTF_DailyTwist twist, int multiplier, float bonusShare) =>
            twist == CTF_DailyTwist.None ? "" : $"Today: {Title(twist)} - {Effect(twist, multiplier, bonusShare)}";

        static string GunTitle(CTF_DailyTwist twist) => LoanedWeapon(twist, out var weapon) ? CTF_Loadouts.Get(weapon).title : "";

        static int FloorDiv(int a, int b) => a >= 0 ? a / b : -((-a + b - 1) / b);

        static int[] Shuffle(int block) {
            int n = Twists.Length;
            var order = new int[n];
            for (int i = 0; i < n; i++) order[i] = i;
            ulong state = ((ulong)DailyChallenges.StableHash(CTF_Retention.Universe + ":twist") << 32) ^ (uint)block ^ 0x9E3779B97F4A7C15UL;
            for (int i = n - 1; i > 0; i--) {
                int j = (int)(Next(ref state) % (ulong)(i + 1));
                (order[i], order[j]) = (order[j], order[i]);
            }
            return order;
        }

        /// <summary>SplitMix64, as the daily challenges use.</summary>
        static ulong Next(ref ulong state) {
            ulong z = state += 0x9E3779B97F4A7C15UL;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }
    }
}
