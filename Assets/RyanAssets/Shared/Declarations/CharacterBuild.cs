using System;
using UnityEngine;

namespace RyanAssets.Shared.Declarations {
    /// <summary>
    /// A character's build: its proportions and how its body changes what it can take and do.
    /// <para>
    /// One character prefab serves every body type a game wants - a lanky sniper, a hulking
    /// brawler, a runt - by carrying one of these rather than by being duplicated per variant. It is
    /// replicated on <c>GameCharacter</c>, which applies the proportions on every machine; the
    /// multipliers are read by whatever owns each stat (health when the character is initialised,
    /// speed by the NPC mover, damage by the weapon).
    /// </para>
    /// <para>
    /// Plain public fields so FishNet serializes it as a SyncVar value without a custom writer.
    /// </para>
    /// </summary>
    [Serializable]
    public struct CharacterBuild : IEquatable<CharacterBuild> {
        /// <summary>Width, height, and depth relative to the authored body. (1, 1, 1) is unchanged.</summary>
        public Vector3 Proportions;
        public float HealthMultiplier;
        public float SpeedMultiplier;
        public float DamageMultiplier;

        public CharacterBuild(Vector3 proportions, float health = 1f, float speed = 1f, float damage = 1f) {
            Proportions = proportions;
            HealthMultiplier = health;
            SpeedMultiplier = speed;
            DamageMultiplier = damage;
        }

        /// <summary>The authored body, unchanged.</summary>
        public static CharacterBuild Standard => new(new Vector3(1f, 1f, 1f));

        /// <summary>
        /// True for a value that was never set - every field zero, as a default struct is. Readers
        /// treat it as <see cref="Standard"/>, so a character spawned before its build is assigned
        /// is neither shrunk to nothing nor frozen in place.
        /// </summary>
        public bool IsUnset =>
            Proportions.x == 0f && Proportions.y == 0f && Proportions.z == 0f
            && HealthMultiplier == 0f && SpeedMultiplier == 0f && DamageMultiplier == 0f;

        /// <summary>This build, or <see cref="Standard"/> when it was never set.</summary>
        public CharacterBuild OrStandard => IsUnset ? Standard : this;

        public long ScaleHealth(long baseHealth) =>
            Math.Max(1L, (long)Math.Round(baseHealth * (double)Mathf.Max(0.01f, OrStandard.HealthMultiplier)));

        public int ScaleDamage(int baseDamage) =>
            Math.Max(1, Mathf.RoundToInt(baseDamage * Mathf.Max(0f, OrStandard.DamageMultiplier)));

        public float Speed => Mathf.Max(0.05f, OrStandard.SpeedMultiplier);

        public bool Equals(CharacterBuild other) =>
            Proportions == other.Proportions
            && HealthMultiplier.Equals(other.HealthMultiplier)
            && SpeedMultiplier.Equals(other.SpeedMultiplier)
            && DamageMultiplier.Equals(other.DamageMultiplier);

        public override bool Equals(object obj) => obj is CharacterBuild other && Equals(other);

        public override int GetHashCode() =>
            HashCode.Combine(Proportions, HealthMultiplier, SpeedMultiplier, DamageMultiplier);
    }
}
