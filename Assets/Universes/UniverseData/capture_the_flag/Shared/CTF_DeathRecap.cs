using System.Collections.Generic;

namespace Universes.UniverseData.capture_the_flag {
    /// <summary>One opponent's side of a fight during your current life: hits and damage each way.</summary>
    public struct CTF_Duel {
        public int hitsTaken, damageTaken, hitsDealt, damageDealt;

        public bool Any => hitsTaken > 0 || hitsDealt > 0;
    }

    /// <summary>
    /// What the respawn card says about the fight that killed you: how many hits each of you landed
    /// this life, how far away they were, and the Payback hook. Kept apart from the HUD so the wording
    /// is tested.
    /// </summary>
    public sealed class CTF_DeathRecap {
        readonly Dictionary<int, CTF_Duel> duels = new();

        /// <summary>A new life: forget the last one's fights.</summary>
        public void Clear() => duels.Clear();

        /// <summary>A confirmed hit between you (<paramref name="me"/>) and someone else; anything else is ignored.</summary>
        public void Hit(int me, int attackerId, int victimId, int damage) {
            if (me < 0 || attackerId == victimId) return;
            if (victimId == me && attackerId >= 0) {
                var duel = Get(attackerId);
                duel.hitsTaken++;
                duel.damageTaken += damage;
                duels[attackerId] = duel;
            } else if (attackerId == me && victimId >= 0) {
                var duel = Get(victimId);
                duel.hitsDealt++;
                duel.damageDealt += damage;
                duels[victimId] = duel;
            }
        }

        public CTF_Duel Get(int opponentId) => duels.TryGetValue(opponentId, out var duel) ? duel : default;

        /// <summary>
        /// The card's fight line: "They hit you 2× (200) · You hit them 1× (100)", or "You never landed
        /// a hit" when it was one-sided. Empty when no hits were seen (a fall, or a join mid-fight).
        /// </summary>
        public static string FightLine(CTF_Duel duel) {
            if (!duel.Any) return "";
            string dealt = duel.hitsDealt > 0 ? $"You hit them {duel.hitsDealt}× ({duel.damageDealt})" : "you never landed a hit";
            if (duel.hitsTaken == 0) return dealt.Substring(0, 1).ToUpperInvariant() + dealt.Substring(1);
            return $"They hit you {duel.hitsTaken}× ({duel.damageTaken}) · {dealt}";
        }

        /// <summary>"from 34 m" for a kill at range, nothing up close (under <paramref name="closeMetres"/>).</summary>
        public static string RangeText(float metres, float closeMetres) =>
            metres >= closeMetres ? $"from {UnityEngine.Mathf.RoundToInt(metres)} m" : "up close";

        /// <summary>
        /// The nudge under it: get even for the Payback medal's XP, sharper when you had them nearly
        /// beaten (you dealt at least <paramref name="nearlyShare"/> of their health).
        /// </summary>
        public static string PaybackLine(CTF_Duel duel, long maxHealth, float nearlyShare, int paybackXp) {
            bool nearly = maxHealth > 0 && duel.damageDealt >= maxHealth * nearlyShare;
            string lead = nearly ? "So close!" : "Get even:";
            return $"{lead} eliminate them for <b>Payback</b> +{paybackXp} XP";
        }
    }
}
