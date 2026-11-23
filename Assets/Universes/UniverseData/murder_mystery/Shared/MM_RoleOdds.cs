using System;
using System.Collections.Generic;

namespace Universes.UniverseData.murder_mystery {
    /// <summary>
    /// Who gets the big roles. Every character has a weight per role (NPCs 1, players more, and a player
    /// gains "luck" for every round they missed the role), murderers are drawn first and sheriffs from
    /// whoever is left. Pure rules (no Unity), so the server deals with them and the tests can prove the odds.
    /// </summary>
    public static class MM_RoleOdds {
        /// <summary>NPCs always weigh this much in the draw.</summary>
        public const float NpcWeight = 1f;

        /// <summary>
        /// One of the role, plus one more for every <paramref name="perRole"/> characters beyond the first,
        /// never so many that nobody is left to be innocent.
        /// </summary>
        public static int RoleCount(int characters, int perRole) {
            if (characters <= 1)
                return 0;
            if (perRole < 1)
                throw new ArgumentOutOfRangeException(nameof(perRole));
            return Math.Min(1 + (characters - 1) / perRole, characters - 1);
        }

        /// <summary>A character's weight in a role draw: NPCs <see cref="NpcWeight"/>, players their base plus luck.</summary>
        public static float Weight(bool isPlayer, int roundsWithoutRole, float playerWeight, float luckPerRound, int maxLuckRounds) {
            if (!isPlayer)
                return NpcWeight;
            int luck = Math.Clamp(roundsWithoutRole, 0, Math.Max(0, maxLuckRounds));
            return Math.Max(0.01f, playerWeight + luck * luckPerRound);
        }

        /// <summary>
        /// The weight each NPC gets so that <paramref name="players"/> players of base weight
        /// <paramref name="playerWeight"/> together hold at least <paramref name="playerShare"/> of a draw against
        /// <paramref name="npcs"/> NPCs. Never more than <see cref="NpcWeight"/>: a big lobby keeps the plain odds.
        /// </summary>
        public static float NpcWeightFor(int players, int npcs, float playerWeight, float playerShare) {
            if (players <= 0 || npcs <= 0 || playerShare <= 0f)
                return NpcWeight;
            playerShare = Math.Min(playerShare, 0.99f);
            float wanted = players * Math.Max(0f, playerWeight) * (1f - playerShare) / (playerShare * npcs);
            return Math.Max(0.01f, Math.Min(NpcWeight, wanted));
        }

        /// <summary>
        /// Deals the roles. <paramref name="murdererWeights"/> and <paramref name="sheriffWeights"/> hold one
        /// weight per character (same order); <paramref name="random"/> returns a uniform value in [0, 1).
        /// Returns each character's role.
        /// </summary>
        public static MM_Role[] Deal(IReadOnlyList<float> murdererWeights, IReadOnlyList<float> sheriffWeights,
            int murderers, int sheriffs, Func<float> random) {
            if (murdererWeights == null) throw new ArgumentNullException(nameof(murdererWeights));
            if (sheriffWeights == null) throw new ArgumentNullException(nameof(sheriffWeights));
            if (random == null) throw new ArgumentNullException(nameof(random));
            if (murdererWeights.Count != sheriffWeights.Count)
                throw new ArgumentException("Both weight lists need one entry per character.");

            int count = murdererWeights.Count;
            MM_Role[] roles = new MM_Role[count];
            for (int i = 0; i < count; i++)
                roles[i] = MM_Role.Innocent;
            for (int n = 0; n < murderers; n++) {
                int pick = DrawOne(murdererWeights, roles, random());
                if (pick < 0) break;
                roles[pick] = MM_Role.Murderer;
            }
            for (int n = 0; n < sheriffs; n++) {
                int pick = DrawOne(sheriffWeights, roles, random());
                if (pick < 0) break;
                roles[pick] = MM_Role.Sheriff;
            }
            return roles;
        }

        /// <summary>A weighted pick among the characters that are still innocent, or -1 if there are none.</summary>
        static int DrawOne(IReadOnlyList<float> weights, MM_Role[] roles, float roll) {
            float total = 0f;
            for (int i = 0; i < weights.Count; i++)
                if (roles[i] == MM_Role.Innocent)
                    total += Math.Max(0f, weights[i]);
            if (total <= 0f)
                return -1;
            float target = Math.Clamp(roll, 0f, 0.999999f) * total;
            int last = -1;
            for (int i = 0; i < weights.Count; i++) {
                if (roles[i] != MM_Role.Innocent)
                    continue;
                last = i;
                target -= Math.Max(0f, weights[i]);
                if (target < 0f)
                    return i;
            }
            return last;
        }

        /// <summary>
        /// Character <paramref name="index"/>'s chance (0..1) of being dealt murderer when
        /// <paramref name="murderers"/> are drawn: exact for one murderer, the expected share for more.
        /// </summary>
        public static float MurdererChance(IReadOnlyList<float> murdererWeights, int index, int murderers) {
            float total = Sum(murdererWeights);
            if (total <= 0f || murderers <= 0)
                return 0f;
            return Math.Min(1f, murderers * murdererWeights[index] / total);
        }

        /// <summary>
        /// Character <paramref name="index"/>'s chance (0..1) of being dealt sheriff, given that the murderer is
        /// drawn first and cannot also be the sheriff. Exact for one murderer and one sheriff.
        /// </summary>
        public static float SheriffChance(IReadOnlyList<float> murdererWeights, IReadOnlyList<float> sheriffWeights, int index,
            int murderers, int sheriffs) {
            float murdererTotal = Sum(murdererWeights);
            float sheriffTotal = Sum(sheriffWeights);
            if (sheriffs <= 0 || sheriffTotal <= 0f)
                return 0f;
            if (murderers <= 0 || murdererTotal <= 0f)
                return Math.Min(1f, sheriffs * sheriffWeights[index] / sheriffTotal);
            // Sum over who becomes the murderer: P(j is murderer) * P(i is sheriff among the rest).
            float chance = 0f;
            for (int j = 0; j < murdererWeights.Count; j++) {
                if (j == index)
                    continue;
                float rest = sheriffTotal - sheriffWeights[j];
                if (rest <= 0f)
                    continue;
                chance += murdererWeights[j] / murdererTotal * (sheriffWeights[index] / rest);
            }
            return Math.Min(1f, chance * sheriffs);
        }

        /// <summary>A chance as a whole percentage for the HUD (never shows 0 % for a real chance).</summary>
        public static byte Percent(float chance) {
            if (chance <= 0f)
                return 0;
            return (byte)Math.Clamp((int)Math.Round(chance * 100f), 1, 100);
        }

        static float Sum(IReadOnlyList<float> weights) {
            float total = 0f;
            for (int i = 0; i < weights.Count; i++)
                total += Math.Max(0f, weights[i]);
            return total;
        }
    }
}
