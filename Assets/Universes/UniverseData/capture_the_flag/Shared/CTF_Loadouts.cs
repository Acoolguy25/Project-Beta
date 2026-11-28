using System;
using UnityEngine;

namespace Universes.UniverseData.capture_the_flag {
    /// <summary>The gun a player carries. Saved by id and sent over the wire: only ever append.</summary>
    public enum CTF_Weapon : byte { Sidearm, BurstRifle, Scattergun, Marksman }

    /// <summary>A passive bonus a player picks for each life. Saved by id and sent over the wire: only ever append.</summary>
    public enum CTF_Perk : byte { None, SecondWind, FleetFoot, FlagRunner, SteadyAim, Adrenaline, Guardian, FieldMedic }

    /// <summary>Which half of the loadout a request changes.</summary>
    public enum CTF_LoadoutSlot : byte { Weapon, Perk }

    /// <summary>
    /// What a gun does. Every Flag Wars gun is the shared pistol tool re-tuned: the server sets damage,
    /// clip and reload on the tool (they replicate), and the one who fires it (the owner's client, or the
    /// server for a bot) applies the firing pattern. Also the wire form in <see cref="CTF_LoadoutBroadcast"/>,
    /// so plain fields only, and only ever append.
    /// </summary>
    [Serializable]
    public struct CTF_GunStats {
        [Tooltip("Damage of each round (characters have 200 health).")]
        [Min(1)] public int damage;
        [Tooltip("Shots before a reload. A scattergun shell is one shot of several rounds.")]
        [Min(1)] public int clipShots;
        [Min(0.1f)] public float reloadSeconds;
        [Tooltip("Seconds from one trigger pull to the next.")]
        [Min(0.05f)] public float fireInterval;
        [Tooltip("Rounds each trigger pull fires: 3 for a burst, 6 pellets for a scattergun.")]
        [Min(1)] public int roundsPerShot;
        [Tooltip("Seconds between the rounds of one pull (0 = one per frame, a scattergun blast).")]
        [Min(0f)] public float roundInterval;
        [Tooltip("Accuracy range in the tool's units, 0..360 (360 fires down the exact line; 300 spreads up to 2.5 degrees).")]
        [Range(0, 360)] public int worstAccuracy;
        [Range(0, 360)] public int bestAccuracy;
        [Min(1f)] public float range;

        /// <summary>Rounds in a full clip (what the tool counts).</summary>
        public int ClipRounds => Math.Max(1, clipShots) * Math.Max(1, roundsPerShot);

        /// <summary>Damage if every round of one pull hits.</summary>
        public int PullDamage => Math.Max(0, damage) * Math.Max(1, roundsPerShot);

        /// <summary>Widest spread of one round, in degrees (the shared hitscan cone is 15 degrees at accuracy 0).</summary>
        public float WorstSpreadDegrees => (1f - Mathf.Clamp01(Math.Min(worstAccuracy, bestAccuracy) / 360f)) * CTF_Loadouts.ToolMaxSpreadDegrees;

        /// <summary>A copy with both accuracy bounds raised by <paramref name="bonus"/> (capped at 360).</summary>
        public CTF_GunStats WithAccuracyBonus(int bonus) {
            CTF_GunStats copy = this;
            copy.worstAccuracy = Math.Clamp(worstAccuracy + bonus, 0, 360);
            copy.bestAccuracy = Math.Clamp(bestAccuracy + bonus, 0, 360);
            return copy;
        }

        /// <summary>Clamps values a hand edit in the Inspector could break.</summary>
        public CTF_GunStats Validated() {
            CTF_GunStats v = this;
            v.damage = Math.Max(1, damage);
            v.clipShots = Math.Max(1, clipShots);
            v.reloadSeconds = Math.Max(0.1f, reloadSeconds);
            v.fireInterval = Math.Max(0.05f, fireInterval);
            v.roundsPerShot = Math.Max(1, roundsPerShot);
            v.roundInterval = Math.Max(0f, roundInterval);
            v.worstAccuracy = Math.Clamp(worstAccuracy, 0, 360);
            v.bestAccuracy = Math.Clamp(bestAccuracy, v.worstAccuracy, 360);
            v.range = Math.Max(1f, range);
            return v;
        }
    }

    /// <summary>A gun's name, look on the HUD, the unlock that grants it and its default stats.</summary>
    public sealed class CTF_WeaponDefinition {
        public readonly CTF_Weapon weapon;
        /// <summary>Stable id saved with the player's choice; never rename a shipped one.</summary>
        public readonly string id;
        public readonly string title;
        public readonly string description;
        /// <summary>Theme icon name.</summary>
        public readonly string icon;
        /// <summary>Retention unlock id, or null when every player owns it.</summary>
        public readonly string unlockId;
        public readonly CTF_GunStats stats;

        public CTF_WeaponDefinition(CTF_Weapon weapon, string id, string title, string description, string icon, string unlockId, CTF_GunStats stats) {
            this.weapon = weapon;
            this.id = id;
            this.title = title;
            this.description = description;
            this.icon = icon;
            this.unlockId = unlockId;
            this.stats = stats;
        }
    }

    /// <summary>A perk's name, look on the HUD and the unlock that grants it.</summary>
    public sealed class CTF_PerkDefinition {
        public readonly CTF_Perk perk;
        public readonly string id;
        public readonly string title;
        public readonly string description;
        public readonly string icon;
        /// <summary>Retention unlock id, or null when every player owns it.</summary>
        public readonly string unlockId;

        public CTF_PerkDefinition(CTF_Perk perk, string id, string title, string description, string icon, string unlockId) {
            this.perk = perk;
            this.id = id;
            this.title = title;
            this.description = description;
            this.icon = icon;
            this.unlockId = unlockId;
        }
    }

    /// <summary>
    /// The loadout table both sides agree on: every gun and perk, what it is called and which level
    /// unlock grants it (<see cref="CTF_Retention"/>). Stats here are the defaults; the server's
    /// tuning (<c>CTF_LoadoutTuning</c>) may re-tune them and sends the result with every loadout.
    /// </summary>
    public static class CTF_Loadouts {
        /// <summary>The shared hitscan's spread cone at accuracy 0 (HitscanShot.MaxSpreadDegrees).</summary>
        public const float ToolMaxSpreadDegrees = 15f;

        public static readonly CTF_WeaponDefinition[] Weapons = {
            new(CTF_Weapon.Sidearm, "sidearm", "Sidearm", "One heavy round. Two hits drop anyone.", "pistol", null,
                new CTF_GunStats { damage = 100, clipShots = 1, reloadSeconds = 1.5f, fireInterval = 0.35f, roundsPerShot = 1, roundInterval = 0.1f,
                    worstAccuracy = 240, bestAccuracy = 300, range = 60f }),
            new(CTF_Weapon.BurstRifle, "burst_rifle", "Burst Rifle", "Three-round bursts with a long, tight reach. Land all six to drop a full-health enemy.", "my_location", CTF_Retention.UnlockBurstRifle,
                new CTF_GunStats { damage = 35, clipShots = 2, reloadSeconds = 1.6f, fireInterval = 0.45f, roundsPerShot = 3, roundInterval = 0.08f,
                    worstAccuracy = 300, bestAccuracy = 330, range = 85f }),
            new(CTF_Weapon.Scattergun, "scattergun", "Scattergun", "Six pellets. Deletes anyone at arm's length.", "local_fire_department", CTF_Retention.UnlockScattergun,
                new CTF_GunStats { damage = 36, clipShots = 2, reloadSeconds = 1.4f, fireInterval = 0.6f, roundsPerShot = 6, roundInterval = 0f,
                    worstAccuracy = 160, bestAccuracy = 230, range = 26f }),
            // Two rounds a clip (try 1 had one and a 2.3 s reload, so its two-shot kill was slower than the
            // sidearm's everywhere the sidearm reaches; see the time-to-kill table in the tests).
            new(CTF_Weapon.Marksman, "marksman", "Marksman Rifle", "Two dead-on shots drop anyone, across the whole arena.", "crosshair", CTF_Retention.UnlockMarksman,
                new CTF_GunStats { damage = 150, clipShots = 2, reloadSeconds = 2.3f, fireInterval = 0.85f, roundsPerShot = 1, roundInterval = 0.1f,
                    worstAccuracy = 350, bestAccuracy = 360, range = 140f }),
        };

        public static readonly CTF_PerkDefinition[] Perks = {
            new(CTF_Perk.None, "none", "No perk", "Unlock perks by levelling up.", "star", null),
            new(CTF_Perk.SecondWind, "second_wind", "Second Wind", "Every elimination patches you up.", "favorite", CTF_Retention.UnlockSecondWind),
            new(CTF_Perk.FleetFoot, "fleet_foot", "Fleet Foot", "Run faster everywhere.", "sprint", CTF_Retention.UnlockFleetFoot),
            new(CTF_Perk.FlagRunner, "flag_runner", "Flag Runner", "Carry the flag at full speed.", "run", CTF_Retention.UnlockFlagRunner),
            new(CTF_Perk.SteadyAim, "steady_aim", "Steady Aim", "Tighter spread on every gun.", "visibility", CTF_Retention.UnlockSteadyAim),
            new(CTF_Perk.Adrenaline, "adrenaline", "Adrenaline", "Every elimination gives you a burst of speed.", "bolt", CTF_Retention.UnlockAdrenaline),
            new(CTF_Perk.Guardian, "guardian", "Guardian", "Send your dropped flag home from three times as far.", "shield", CTF_Retention.UnlockGuardian),
            new(CTF_Perk.FieldMedic, "field_medic", "Field Medic", "Taking, returning or capturing a flag heals you to full.", "flag", CTF_Retention.UnlockFieldMedic),
        };

        public static CTF_WeaponDefinition Get(CTF_Weapon weapon) {
            int i = (int)weapon;
            return i >= 0 && i < Weapons.Length ? Weapons[i] : Weapons[0];
        }

        public static CTF_PerkDefinition Get(CTF_Perk perk) {
            int i = (int)perk;
            return i >= 0 && i < Perks.Length ? Perks[i] : Perks[0];
        }

        /// <summary>
        /// Chance that one round hits a target <paramref name="radius"/> wide at <paramref name="distance"/>:
        /// the shared hitscan turns each round by an angle drawn evenly from 0 to the spread (1 - accuracy)
        /// times 15 degrees, accuracy drawn between the gun's worst and best. 0 beyond the gun's range.
        /// </summary>
        public static float HitChance(in CTF_GunStats stats, float distance, float radius) {
            var s = stats.Validated();
            if (distance > s.range) return 0f;
            if (distance <= 0f) return 1f;
            float needed = Mathf.Atan(radius / distance) * Mathf.Rad2Deg;
            const int Steps = 16;
            float sum = 0f;
            for (int i = 0; i < Steps; i++) {
                float accuracy = Mathf.Lerp(s.worstAccuracy, s.bestAccuracy, (i + 0.5f) / Steps) / 360f;
                float spread = (1f - accuracy) * ToolMaxSpreadDegrees;
                sum += spread <= needed ? 1f : needed / spread;
            }
            return sum / Steps;
        }

        /// <summary>
        /// Expected seconds from the first trigger pull to a kill on <paramref name="health"/> at
        /// <paramref name="distance"/> (<see cref="HitChance"/> per round; a reload whenever the clip is
        /// empty). Infinity out of range.
        /// </summary>
        public static float TimeToKill(in CTF_GunStats stats, int health, float distance, float radius = 1f) {
            var s = stats.Validated();
            float perPull = s.damage * s.roundsPerShot * HitChance(s, distance, radius);
            if (perPull <= 0f) return float.PositiveInfinity;
            int pulls = Mathf.CeilToInt(health / perPull - 1e-4f);
            float time = (s.roundsPerShot - 1) * s.roundInterval;
            for (int pull = 1; pull < pulls; pull++) time += pull % s.clipShots == 0 ? Math.Max(s.reloadSeconds, s.fireInterval) : s.fireInterval;
            return time;
        }

        /// <summary>
        /// The centre notice for a changed pick: its name ("(next life)" while this life wears the old one)
        /// and, on the next line at <paramref name="descriptionSize"/> of the name's size, what it does.
        /// </summary>
        public static string PickNotice(string title, string description, bool nextLife, float descriptionSize) {
            string name = nextLife ? $"{title}  (next life)" : title;
            if (string.IsNullOrEmpty(description)) return name;
            int percent = (int)Math.Round(Math.Clamp(descriptionSize, 0.1f, 1f) * 100f);
            return $"{name}\n<size={percent}%>{description}</size>";
        }

        /// <summary>The gun saved under <paramref name="id"/>, or the sidearm for unknown or missing ids.</summary>
        public static CTF_Weapon WeaponById(string id) {
            foreach (var w in Weapons) if (w.id == id) return w.weapon;
            return CTF_Weapon.Sidearm;
        }

        /// <summary>The perk saved under <paramref name="id"/>, or none for unknown or missing ids.</summary>
        public static CTF_Perk PerkById(string id) {
            foreach (var p in Perks) if (p.id == id) return p.perk;
            return CTF_Perk.None;
        }

        /// <summary>Bit i set = weapon (or perk) i is owned, given an "is this unlock owned" test.</summary>
        public static byte OwnedWeapons(Func<string, bool> owns) {
            byte mask = 0;
            for (int i = 0; i < Weapons.Length; i++)
                if (Weapons[i].unlockId == null || (owns != null && owns(Weapons[i].unlockId))) mask |= (byte)(1 << i);
            return mask;
        }

        /// <inheritdoc cref="OwnedWeapons"/>
        public static byte OwnedPerks(Func<string, bool> owns) {
            byte mask = 0;
            for (int i = 0; i < Perks.Length; i++)
                if (Perks[i].unlockId == null || (owns != null && owns(Perks[i].unlockId))) mask |= (byte)(1 << i);
            return mask;
        }

        public static string[] WeaponUnlockIds() {
            var ids = new string[Weapons.Length];
            for (int i = 0; i < ids.Length; i++) ids[i] = Weapons[i].unlockId;
            return ids;
        }

        public static string[] PerkUnlockIds() {
            var ids = new string[Perks.Length];
            for (int i = 0; i < ids.Length; i++) ids[i] = Perks[i].unlockId;
            return ids;
        }

        public static bool Has(byte mask, int index) => index >= 0 && index < 8 && (mask & (1 << index)) != 0;

        /// <summary>How many of the <paramref name="count"/> entries a mask owns.</summary>
        public static int CountOwned(byte mask, int count) {
            int owned = 0;
            for (int i = 0; i < count; i++) if (Has(mask, i)) owned++;
            return owned;
        }

        /// <summary>
        /// The next owned entry after <paramref name="current"/> going <paramref name="step"/> (+1 or -1),
        /// wrapping around; <paramref name="current"/> itself when nothing else is owned.
        /// </summary>
        public static int Cycle(int current, int step, byte owned, int count) {
            if (count <= 0) return 0;
            int direction = step < 0 ? -1 : 1;
            for (int i = 1; i <= count; i++) {
                int candidate = ((current + direction * i) % count + count) % count;
                if (Has(owned, candidate)) return candidate;
            }
            return current;
        }
    }
}
