using System;
using UnityEngine;

namespace Universes.UniverseData.capture_the_flag.Server {
    /// <summary>
    /// Gun numbers and perk strengths, on the server runner so they can be balanced without code. Every
    /// gun is the shared pistol re-tuned: damage, clip and reload replicate through the tool, the firing
    /// pattern reaches the shooter in <see cref="CTF_LoadoutBroadcast.liveStats"/>.
    /// </summary>
    [Serializable]
    public sealed class CTF_LoadoutTuning {
        [Tooltip("Stats per gun, in CTF_Weapon order (Sidearm, Burst Rifle, Scattergun, Marksman). A missing entry uses the built-in table.")]
        public CTF_GunStats[] weapons = DefaultWeapons();
        [Tooltip("Second Wind: health restored on every elimination.")]
        [Min(0)] public long secondWindHeal = 50;
        [Tooltip("Fleet Foot: walk and sprint speed multiplier.")]
        [Min(1f)] public float fleetFootSpeed = 1.08f;
        [Tooltip("Flag Runner: speed multiplier while carrying (everyone else carries at CTF_Rules.CarrierSpeedMultiplier).")]
        [Range(0.1f, 1.2f)] public float flagRunnerCarrierSpeed = 1f;
        [Tooltip("Steady Aim: added to both accuracy values (0..360, higher is tighter).")]
        [Range(0, 120)] public int steadyAimAccuracy = 25;
        [Tooltip("Adrenaline: seconds of the speed burst every elimination gives (the speed power-up's multiplier).")]
        [Min(0.5f)] public float adrenalineSeconds = 4f;
        [Tooltip("Guardian: reach for sending your own dropped flag home, times the normal pickup reach.")]
        [Range(1f, 5f)] public float guardianReturnReach = 3f;
        [Tooltip("Shortest time between two loadout changes from one player (seconds).")]
        [Min(0f)] public float requestCooldown = 0.15f;

        public static CTF_GunStats[] DefaultWeapons() {
            var table = CTF_Loadouts.Weapons;
            var stats = new CTF_GunStats[table.Length];
            for (int i = 0; i < table.Length; i++) stats[i] = table[i].stats;
            return stats;
        }

        /// <summary>The tuned stats of <paramref name="weapon"/> with <paramref name="perk"/> applied.</summary>
        public CTF_GunStats StatsFor(CTF_Weapon weapon, CTF_Perk perk) {
            int i = (int)weapon;
            CTF_GunStats stats = weapons != null && i >= 0 && i < weapons.Length ? weapons[i] : CTF_Loadouts.Get(weapon).stats;
            if (perk == CTF_Perk.SteadyAim) stats = stats.WithAccuracyBonus(steadyAimAccuracy);
            return stats.Validated();
        }

        public float SpeedPerk(CTF_Perk perk) => perk == CTF_Perk.FleetFoot ? fleetFootSpeed : 1f;

        /// <summary>Reach for returning your own dropped flag (Guardian widens it).</summary>
        public float ReturnReach(CTF_Perk perk) => perk == CTF_Perk.Guardian ? CTF_Rules.PickupRange * guardianReturnReach : CTF_Rules.PickupRange;

        public float CarrierSpeed(CTF_Perk perk) => perk == CTF_Perk.FlagRunner ? flagRunnerCarrierSpeed : CTF_Rules.CarrierSpeedMultiplier;
    }
}
