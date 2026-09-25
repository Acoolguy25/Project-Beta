using System.Collections.Generic;
using RyanAssets.Shared.Declarations;
using UnityEngine;

namespace Universes.UniverseData.war_valley.Shared {
    /// <summary>What a troop fights with.</summary>
    public enum WV_TroopWeapon : byte {
        Knife = 0,
        Gun = 1
    }

    /// <summary>
    /// One kind of foot soldier: its weapon, its price and training time at a barracks, the body it
    /// is built with, and the first wave that sends it against the flag.
    /// </summary>
    public sealed class WV_TroopProfile {
        public WV_TroopKind Kind { get; }
        public string DisplayName { get; }
        public string Description { get; }
        public WV_TroopWeapon Weapon { get; }
        public int Cost { get; }
        public float TrainSeconds { get; }
        /// <summary>Proportions and stat multipliers, applied through the character's own build setting.</summary>
        public CharacterBuild Build { get; }
        /// <summary>How much of a wave's strength one of these is worth. A knifeman is 1.</summary>
        public float Threat { get; }
        /// <summary>The first wave, counting from 1, that fields this kind.</summary>
        public int FirstWave { get; }

        public WV_TroopProfile(
            WV_TroopKind kind, string displayName, string description, WV_TroopWeapon weapon, int cost,
            float trainSeconds, CharacterBuild build, float threat, int firstWave) {
            Kind = kind;
            DisplayName = displayName;
            Description = description;
            Weapon = weapon;
            Cost = cost;
            TrainSeconds = trainSeconds;
            Build = build;
            Threat = threat;
            FirstWave = firstWave;
        }

        public bool UsesGun => Weapon == WV_TroopWeapon.Gun;
    }

    /// <summary>
    /// Every kind of foot soldier in War Valley, in one table.
    /// <para>
    /// A commander hires these at a barracks and the waves are built from the same list, so a Punk
    /// on the flag and a Punk a player trained are the same soldier. Each kind is the one robot
    /// character with a different weapon and a different <see cref="CharacterBuild"/>: its shape is
    /// applied through the character's replicated scale, and its health, speed, and damage through
    /// the build's multipliers, so no variant needs a prefab of its own.
    /// </para>
    /// </summary>
    public static class WV_TroopCatalog {
        /// <summary>Health of a standard soldier before its build is applied.</summary>
        public const long BaseHealth = 100;

        static readonly WV_TroopProfile[] profiles = {
            new(WV_TroopKind.Knife, "Knifeman", "Melee rusher. Cheap and fast.",
                WV_TroopWeapon.Knife, cost: 80, trainSeconds: 6f,
                CharacterBuild.Standard, threat: 1f, firstWave: 1),
            new(WV_TroopKind.Gunner, "Gunner", "Rifleman. Holds range and shoots.",
                WV_TroopWeapon.Gun, cost: 150, trainSeconds: 10f,
                CharacterBuild.Standard, threat: 1.8f, firstWave: 2),
            new(WV_TroopKind.Shrimp, "Shrimp", "Short, cheap, and fragile. Best in numbers.",
                WV_TroopWeapon.Knife, cost: 45, trainSeconds: 4f,
                new CharacterBuild(new Vector3(0.9f, 0.6f, 0.9f), health: 0.6f, speed: 1.05f, damage: 0.7f),
                threat: 0.6f, firstWave: 3),
            new(WV_TroopKind.Speedy, "Speedy", "Very fast, but light on health and damage.",
                WV_TroopWeapon.Knife, cost: 110, trainSeconds: 6f,
                new CharacterBuild(new Vector3(0.85f, 0.95f, 0.85f), health: 0.6f, speed: 1.6f, damage: 0.6f),
                threat: 1.2f, firstWave: 4),
            new(WV_TroopKind.SkinnyLegend, "Skinny Legend", "Thin sharpshooter. Hard-hitting gunner, lighter frame.",
                WV_TroopWeapon.Gun, cost: 190, trainSeconds: 11f,
                new CharacterBuild(new Vector3(0.55f, 1.15f, 0.55f), health: 0.8f, speed: 1.1f, damage: 1.35f),
                threat: 2.2f, firstWave: 6),
            new(WV_TroopKind.Punk, "Punk", "Big, slow bruiser with a lot of health and a heavy blade.",
                WV_TroopWeapon.Knife, cost: 220, trainSeconds: 12f,
                new CharacterBuild(new Vector3(1.35f, 1.3f, 1.35f), health: 2.2f, speed: 0.85f, damage: 1.5f),
                threat: 3f, firstWave: 8)
        };

        public static IReadOnlyList<WV_TroopProfile> All => profiles;

        public static bool TryGet(WV_TroopKind kind, out WV_TroopProfile profile) {
            foreach (WV_TroopProfile candidate in profiles) {
                if (candidate.Kind == kind) {
                    profile = candidate;
                    return true;
                }
            }
            profile = null;
            return false;
        }

        /// <summary>The profile for <paramref name="kind"/>, or the knifeman's for a kind that does not exist.</summary>
        public static WV_TroopProfile Get(WV_TroopKind kind) => TryGet(kind, out WV_TroopProfile profile) ? profile : profiles[0];

        public static bool IsDefined(WV_TroopKind kind) => TryGet(kind, out _);
    }
}
