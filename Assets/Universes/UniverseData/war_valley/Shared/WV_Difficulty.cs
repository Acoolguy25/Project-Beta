using System;
using System.Collections.Generic;
using UnityEngine;

namespace Universes.UniverseData.war_valley.Shared {
    /// <summary>
    /// How hard a round is. Each tier is unlocked by winning the one before it, so a commander always
    /// has a next tier to beat; Endless opens once Veteran is beaten and never ends while the flag
    /// stands. The byte values travel on the wire and index the saved stars: append only.
    /// </summary>
    public enum WV_DifficultyTier : byte {
        Recruit = 0,
        Veteran = 1,
        Elite = 2,
        Nightmare = 3,
        Endless = 4
    }

    /// <summary>What one tier changes, as multipliers on the tuned game (1 is the Recruit balance).</summary>
    [Serializable]
    public sealed class WV_DifficultyProfile {
        [Tooltip("Shown in the vote, on the campaign card and on the summary.")]
        public string DisplayName = "Recruit";

        [Tooltip("One line for the vote: what this tier asks of the commanders.")]
        public string Description = "The standard war.";

        [Tooltip("Multiplies every wave's strength budget: more enemies, and tougher kinds sooner.")]
        [Min(0.1f)] public float StrengthMultiplier = 1f;

        [Tooltip("Multiplies every enemy's health.")]
        [Min(0.1f)] public float HealthMultiplier = 1f;

        [Tooltip("Multiplies the damage the flag takes and the most it can take each second.")]
        [Min(0.1f)] public float FlagDamageMultiplier = 1f;

        [Tooltip("Multiplies each commander's starting funds.")]
        [Min(0f)] public float StartingFundsMultiplier = 1f;

        [Tooltip("Multiplies the wave and victory bounties.")]
        [Min(0f)] public float BountyMultiplier = 1f;

        [Tooltip("Multiplies the game XP every line of the round's summary pays.")]
        [Min(0f)] public float XpMultiplier = 1f;

        public WV_DifficultyProfile() { }

        public WV_DifficultyProfile(string displayName, string description, float strength, float health,
            float flagDamage, float startingFunds, float bounty, float xp) {
            DisplayName = displayName;
            Description = description;
            StrengthMultiplier = strength;
            HealthMultiplier = health;
            FlagDamageMultiplier = flagDamage;
            StartingFundsMultiplier = startingFunds;
            BountyMultiplier = bounty;
            XpMultiplier = xp;
        }
    }

    /// <summary>
    /// Every tier's profile, the endless scaling, and the star thresholds. Serialized on the server
    /// runner so the tiers are tuned in the Inspector.
    /// </summary>
    [Serializable]
    public sealed class WV_DifficultyTuning {
        public WV_DifficultyProfile Recruit = new("Recruit", "The standard war. Win it to unlock Veteran.",
            strength: 1f, health: 1f, flagDamage: 1f, startingFunds: 1f, bounty: 1f, xp: 1f);

        public WV_DifficultyProfile Veteran = new("Veteran", "Bigger, tougher waves. Pays 30% more XP.",
            strength: 1.25f, health: 1.15f, flagDamage: 1.1f, startingFunds: 1f, bounty: 1.1f, xp: 1.3f);

        public WV_DifficultyProfile Elite = new("Elite", "Hard waves and a fragile flag. Pays 60% more XP.",
            strength: 1.5f, health: 1.3f, flagDamage: 1.25f, startingFunds: 1.1f, bounty: 1.2f, xp: 1.6f);

        public WV_DifficultyProfile Nightmare = new("Nightmare", "Only the best defences hold. Double XP.",
            strength: 1.8f, health: 1.5f, flagDamage: 1.4f, startingFunds: 1.2f, bounty: 1.3f, xp: 2f);

        public WV_DifficultyProfile Endless = new("Endless", "Veteran waves that never stop. How far can you go?",
            strength: 1.25f, health: 1.15f, flagDamage: 1.1f, startingFunds: 1f, bounty: 1.1f, xp: 1.4f);

        [Tooltip("Endless only: extra enemy health for every wave past the standard round's last, as a fraction.")]
        [Min(0f)] public float EndlessHealthPerExtraWave = 0.08f;

        [Tooltip("A win earns 3 stars if the flag never fell below this share of its health.")]
        [Range(0f, 1f)] public float ThreeStarFlagFraction = 0.75f;

        [Tooltip("A win earns 2 stars if the flag never fell below this share of its health; any other win earns 1.")]
        [Range(0f, 1f)] public float TwoStarFlagFraction = 0.35f;

        [Tooltip("Seconds the commanders have to vote for the round's tier.")]
        [Min(3)] public int VoteSeconds = 15;

        [Tooltip("Seconds after a round's summary card arrives before the next tier vote opens (the vote " +
                 "covers the card), so the card can count up and be read first.")]
        [Min(0)] public float VoteAfterSummarySeconds = 22f;

        public WV_DifficultyProfile Get(WV_DifficultyTier tier) => tier switch {
            WV_DifficultyTier.Veteran => Veteran ?? new WV_DifficultyProfile(),
            WV_DifficultyTier.Elite => Elite ?? new WV_DifficultyProfile(),
            WV_DifficultyTier.Nightmare => Nightmare ?? new WV_DifficultyProfile(),
            WV_DifficultyTier.Endless => Endless ?? new WV_DifficultyProfile(),
            _ => Recruit ?? new WV_DifficultyProfile()
        };
    }

    /// <summary>
    /// The campaign's rules, the same on server and client: which tiers a commander may pick, how
    /// many stars a win earns, and what to aim for next. Pure, so it is covered by EditMode tests.
    /// </summary>
    public static class WV_Campaign {
        public const int MaxStars = 3;

        /// <summary>Every tier, in the order they unlock.</summary>
        public static readonly WV_DifficultyTier[] Tiers = {
            WV_DifficultyTier.Recruit, WV_DifficultyTier.Veteran, WV_DifficultyTier.Elite,
            WV_DifficultyTier.Nightmare, WV_DifficultyTier.Endless
        };

        /// <summary>The tiers that end in a victory, and so earn stars. Endless has a best wave instead.</summary>
        public static bool HasStars(WV_DifficultyTier tier) => tier != WV_DifficultyTier.Endless;

        /// <summary>Best stars on <paramref name="tier"/> (0 = never won), from the saved list indexed by tier.</summary>
        public static int StarsOn(IReadOnlyList<int> tierStars, WV_DifficultyTier tier) {
            int index = (int)tier;
            return tierStars != null && index < tierStars.Count ? Mathf.Clamp(tierStars[index], 0, MaxStars) : 0;
        }

        /// <summary>
        /// Recruit is always open. Each further starred tier opens once the one before it has been
        /// won, and Endless opens once Veteran has.
        /// </summary>
        public static bool IsUnlocked(IReadOnlyList<int> tierStars, WV_DifficultyTier tier) => tier switch {
            WV_DifficultyTier.Recruit => true,
            WV_DifficultyTier.Endless => StarsOn(tierStars, WV_DifficultyTier.Veteran) > 0,
            _ => StarsOn(tierStars, tier - 1) > 0
        };

        /// <summary>One bit per unlocked tier, for the wire.</summary>
        public static byte UnlockedMask(IReadOnlyList<int> tierStars) {
            int mask = 0;
            foreach (WV_DifficultyTier tier in Tiers) {
                if (IsUnlocked(tierStars, tier))
                    mask |= 1 << (int)tier;
            }
            return (byte)mask;
        }

        public static bool IsInMask(byte mask, WV_DifficultyTier tier) => (mask & (1 << (int)tier)) != 0;

        /// <summary>
        /// Stars for a finished round: none for a loss; for a win, by how close the flag came to
        /// falling (the lowest share of its health it was ever left with).
        /// </summary>
        public static int Stars(bool victory, float lowestFlagFraction, WV_DifficultyTuning tuning) {
            if (!victory)
                return 0;
            if (lowestFlagFraction >= tuning.ThreeStarFlagFraction)
                return 3;
            return lowestFlagFraction >= tuning.TwoStarFlagFraction ? 2 : 1;
        }

        /// <summary>The tier this player should aim at next: the lowest starred tier not yet won, else the lowest without 3 stars.</summary>
        public static WV_DifficultyTier NextTarget(IReadOnlyList<int> tierStars) {
            foreach (WV_DifficultyTier tier in Tiers) {
                if (HasStars(tier) && StarsOn(tierStars, tier) == 0)
                    return tier;
            }
            foreach (WV_DifficultyTier tier in Tiers) {
                if (HasStars(tier) && StarsOn(tierStars, tier) < MaxStars)
                    return tier;
            }
            return WV_DifficultyTier.Endless;
        }

        /// <summary>
        /// The next goal, in words, for the campaign card: "Win Veteran for Elite + Endless", "3-star
        /// Elite (2/3)", or "Beat your best Endless wave (23)".
        /// </summary>
        public static string DescribeNextGoal(IReadOnlyList<int> tierStars, int bestEndlessWave, WV_DifficultyTuning tuning) {
            WV_DifficultyTier target = NextTarget(tierStars);
            int stars = StarsOn(tierStars, target);
            string name = tuning.Get(target).DisplayName;
            if (target == WV_DifficultyTier.Endless)
                return bestEndlessWave > 0 ? $"Beat your best Endless wave ({bestEndlessWave})" : "Try Endless: how far can you hold?";
            if (stars > 0)
                return $"3-star {name} ({stars}/{MaxStars} stars)";
            return target switch {
                WV_DifficultyTier.Recruit => $"Win {name} to unlock {tuning.Get(WV_DifficultyTier.Veteran).DisplayName}",
                WV_DifficultyTier.Veteran => $"Win {name} for {tuning.Get(WV_DifficultyTier.Elite).DisplayName} + Endless",
                WV_DifficultyTier.Elite => $"Win {name} to unlock {tuning.Get(WV_DifficultyTier.Nightmare).DisplayName}",
                _ => $"Win {name}"
            };
        }

        /// <summary>
        /// The tier a vote with no votes falls back to: the easiest offered tier that somebody
        /// present has not won yet (<paramref name="lowestProgress"/> holds, per tier, the fewest
        /// stars any of them has), else the hardest offered starred tier.
        /// </summary>
        public static WV_DifficultyTier DefaultChoice(IReadOnlyList<WV_DifficultyTier> offered, IReadOnlyList<int> lowestProgress) {
            if (offered == null || offered.Count == 0)
                return WV_DifficultyTier.Recruit;
            WV_DifficultyTier fallback = offered[0];
            foreach (WV_DifficultyTier tier in offered) {
                if (!HasStars(tier))
                    continue;
                fallback = tier;
                if (StarsOn(lowestProgress, tier) == 0)
                    return tier;
            }
            return fallback;
        }

        /// <summary>
        /// Most tiers a round's vote offers: every tier (War Valley's own tier panel lays out five), so a
        /// commander can always go back for the stars they are missing on any tier they have opened.
        /// </summary>
        public const int MaxVoteOptions = 5;

        /// <summary>
        /// The tiers a round's vote offers, from every tier somebody present has unlocked: at most
        /// <paramref name="max"/>, always the tier the group should aim at next (<see cref="DefaultChoice"/>),
        /// the hardest starred tier open, and Endless when it is open; then the next hardest. In tier order.
        /// </summary>
        public static List<WV_DifficultyTier> VoteOptions(IReadOnlyList<WV_DifficultyTier> unlocked, IReadOnlyList<int> lowestProgress,
            int max = MaxVoteOptions) {
            var options = new List<WV_DifficultyTier>();
            if (unlocked == null || unlocked.Count == 0) {
                options.Add(WV_DifficultyTier.Recruit);
                return options;
            }
            max = Mathf.Max(1, max);
            if (unlocked.Count <= max) {
                options.AddRange(unlocked);
                return options;
            }

            void Take(WV_DifficultyTier tier) {
                if (options.Count < max && !options.Contains(tier) && Contains(unlocked, tier))
                    options.Add(tier);
            }

            Take(DefaultChoice(unlocked, lowestProgress));
            for (int i = unlocked.Count - 1; i >= 0; i--) {
                if (HasStars(unlocked[i])) {
                    Take(unlocked[i]);
                    break;
                }
            }
            Take(WV_DifficultyTier.Endless);
            for (int i = unlocked.Count - 1; i >= 0 && options.Count < max; i--)
                Take(unlocked[i]);
            options.Sort();
            return options;
        }

        static bool Contains(IReadOnlyList<WV_DifficultyTier> tiers, WV_DifficultyTier tier) {
            foreach (WV_DifficultyTier t in tiers) {
                if (t == tier)
                    return true;
            }
            return false;
        }

        /// <summary>Health multiplier for an Endless wave past the standard round's last wave.</summary>
        public static float EndlessHealthMultiplier(int waveNumber, int standardWaveCount, WV_DifficultyTuning tuning) =>
            1f + Mathf.Max(0f, tuning.EndlessHealthPerExtraWave) * Mathf.Max(0, waveNumber - standardWaveCount);

        /// <summary>"2/3": stars as plain text, for fonts without star glyphs.</summary>
        public static string StarText(int stars) => $"{Mathf.Clamp(stars, 0, MaxStars)}/{MaxStars}";
    }
}
