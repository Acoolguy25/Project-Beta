using UnityEngine;

namespace Universes.UniverseData.murder_mystery {
    /// <summary>A perk a player brings into Classic rounds. Values are saved: only append.</summary>
    public enum MM_Perk : byte {
        None,
        /// <summary>Murderer: the knife comes back sooner after a throw.</summary>
        Haste,
        /// <summary>Murderer: a drawn knife only gives you away up close, and the Heartbeat perk cannot hear you.</summary>
        Ninja,
        /// <summary>Murderer: every so often a ping shows where everyone is, through walls.</summary>
        SixthSense,
        /// <summary>Innocent and sheriff: a bigger coin bag.</summary>
        Lucky,
        /// <summary>Innocent: smoke bombs come back sooner and the burst of speed lasts longer.</summary>
        LightFeet,
        /// <summary>Innocent and sheriff: a heartbeat warns you when a murderer with the knife out is close.</summary>
        Heartbeat,
    }

    /// <summary>Which of a player's two perks applies: the one they bring as murderer, or as everyone else.</summary>
    public enum MM_PerkSlot : byte { Murderer, Innocent }

    /// <summary>
    /// MM2-style perks for Classic. Each player picks one murderer perk (G) and one innocent perk (H) between
    /// rounds; the perk for the role they are dealt applies for the whole round (an innocent who becomes the
    /// hero keeps theirs). Perks unlock as the player plays rounds, so a newcomer has something to work towards
    /// beyond knife skins. Pure rules shared by the server (who decides and saves) and the client (who shows
    /// the picker), so both agree and the tests cover them.
    /// </summary>
    public static class MM_Perks {
        public readonly struct Definition {
            public readonly MM_Perk perk;
            public readonly MM_PerkSlot slot;
            public readonly string title;
            /// <summary>One line on what it does, for the picker and the pick notice.</summary>
            public readonly string description;
            /// <summary>The UI theme icon (HudKit.Icon name).</summary>
            public readonly string icon;
            /// <summary>Murder mystery rounds a player must have started to pick it.</summary>
            public readonly int unlockRounds;
            public readonly Color color;

            public Definition(MM_Perk perk, MM_PerkSlot slot, string title, string description, string icon, int unlockRounds, Color color) {
                this.perk = perk;
                this.slot = slot;
                this.title = title;
                this.description = description;
                this.icon = icon;
                this.unlockRounds = unlockRounds;
                this.color = color;
            }
        }

        static readonly Color MurdererRed = new(0.95f, 0.3f, 0.3f);
        static readonly Color InnocentGreen = new(0.35f, 0.85f, 0.45f);
        static readonly Color Muted = new(0.62f, 0.66f, 0.72f);

        /// <summary>Every perk, indexed by <see cref="MM_Perk"/>. "None" is always available in both slots.</summary>
        public static readonly Definition[] All = {
            new(MM_Perk.None, MM_PerkSlot.Murderer, "No perk", "Nothing extra.", "cancel", 0, Muted),
            new(MM_Perk.Haste, MM_PerkSlot.Murderer, "Haste", "Your thrown knife comes back to your hand sooner.", "speed", 2, MurdererRed),
            new(MM_Perk.Ninja, MM_PerkSlot.Murderer, "Ninja", "A drawn knife only gives you away up close. Heartbeats can't hear you.", "ghost", 8, MurdererRed),
            new(MM_Perk.SixthSense, MM_PerkSlot.Murderer, "Sixth Sense", "Every so often, see where everyone is for a moment, through walls.", "visibility", 16, MurdererRed),
            new(MM_Perk.Lucky, MM_PerkSlot.Innocent, "Lucky", "Your coin bag holds more coins each round.", "paid", 1, InnocentGreen),
            new(MM_Perk.LightFeet, MM_PerkSlot.Innocent, "Light Feet", "Smoke bombs come back sooner and your dash lasts longer.", "directions_run", 5, InnocentGreen),
            new(MM_Perk.Heartbeat, MM_PerkSlot.Innocent, "Heartbeat", "Your heart races when a murderer with the knife out is near.", "favorite", 12, InnocentGreen),
        };

        public static Definition Get(MM_Perk perk) => All[Mathf.Clamp((int)perk, 0, All.Length - 1)];

        /// <summary>The slot a perk belongs to ("None" fits both).</summary>
        public static bool Fits(MM_Perk perk, MM_PerkSlot slot) => perk == MM_Perk.None || (int)perk < All.Length && Get(perk).slot == slot;

        public static bool IsUnlocked(MM_Perk perk, int roundsPlayed) => (int)perk < All.Length && roundsPlayed >= Get(perk).unlockRounds;

        /// <summary>The slot whose perk applies to a character dealt <paramref name="role"/> (the hero is an innocent who took the gun).</summary>
        public static MM_PerkSlot SlotFor(MM_Role role) => role == MM_Role.Murderer ? MM_PerkSlot.Murderer : MM_PerkSlot.Innocent;

        /// <summary>The perk that applies this round to a character dealt <paramref name="role"/>.</summary>
        public static MM_Perk ForRole(MM_Role role, MM_Perk murdererPerk, MM_Perk innocentPerk) => role switch {
            MM_Role.Murderer => murdererPerk,
            MM_Role.Innocent or MM_Role.Sheriff => innocentPerk,
            _ => MM_Perk.None,
        };

        /// <summary>A saved or requested pick made safe: a perk from the wrong slot, unknown or still locked becomes "None".</summary>
        public static MM_Perk Sanitize(MM_Perk perk, MM_PerkSlot slot, int roundsPlayed) =>
            Fits(perk, slot) && IsUnlocked(perk, roundsPlayed) ? perk : MM_Perk.None;

        /// <summary>
        /// The perk after <paramref name="current"/> in <paramref name="slot"/> (<paramref name="step"/> +1 forward,
        /// -1 back), skipping locked ones and wrapping round through "None".
        /// </summary>
        public static MM_Perk Step(MM_PerkSlot slot, MM_Perk current, int step, int roundsPlayed) {
            var choices = new System.Collections.Generic.List<MM_Perk>(4) { MM_Perk.None };
            foreach (Definition d in All)
                if (d.perk != MM_Perk.None && d.slot == slot && IsUnlocked(d.perk, roundsPlayed))
                    choices.Add(d.perk);
            int index = choices.IndexOf(current);
            if (index < 0)
                index = 0;
            int direction = step < 0 ? -1 : 1;
            return choices[((index + direction) % choices.Count + choices.Count) % choices.Count];
        }

        /// <summary>How many perks of <paramref name="slot"/> are unlocked, and how many there are (not counting "None").</summary>
        public static (int unlocked, int total) Count(MM_PerkSlot slot, int roundsPlayed) {
            int unlocked = 0, total = 0;
            foreach (Definition d in All) {
                if (d.perk == MM_Perk.None || d.slot != slot)
                    continue;
                total++;
                if (IsUnlocked(d.perk, roundsPlayed))
                    unlocked++;
            }
            return (unlocked, total);
        }

        /// <summary>The next perk of <paramref name="slot"/> to unlock, or false when all are.</summary>
        public static bool NextLocked(MM_PerkSlot slot, int roundsPlayed, out Definition next) {
            foreach (Definition d in All)
                if (d.perk != MM_Perk.None && d.slot == slot && !IsUnlocked(d.perk, roundsPlayed)) {
                    next = d;
                    return true;
                }
            next = default;
            return false;
        }

        /// <summary>The perk that became available when the player's rounds went from <paramref name="before"/> to <paramref name="after"/>, if any.</summary>
        public static bool JustUnlocked(int before, int after, out Definition unlocked) {
            foreach (Definition d in All)
                if (d.perk != MM_Perk.None && before < d.unlockRounds && after >= d.unlockRounds) {
                    unlocked = d;
                    return true;
                }
            unlocked = default;
            return false;
        }

        /// <summary>
        /// Heartbeat strength (0 = silent, 1 = the murderer is right there) for a murderer this far away with the
        /// knife out. Silent beyond <paramref name="range"/>, full at <paramref name="near"/> or closer.
        /// </summary>
        public static float HeartbeatStrength(float distance, float near, float range) {
            if (range <= 0f || distance >= range)
                return 0f;
            if (distance <= near)
                return 1f;
            return 1f - (distance - near) / Mathf.Max(0.01f, range - near);
        }
    }
}
