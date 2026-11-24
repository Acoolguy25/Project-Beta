using System;
using System.Collections.Generic;
using System.Linq;

namespace RyanAssets.Retention {
    /// <summary>Where a player stands on a <see cref="LevelTrack"/>.</summary>
    public readonly struct LevelProgress : IEquatable<LevelProgress> {
        /// <summary>Current level (1..max).</summary>
        public readonly int Level;
        /// <summary>XP earned inside the current level.</summary>
        public readonly int LevelXp;
        /// <summary>XP the current level takes in total; 0 at the maximum level.</summary>
        public readonly int LevelXpNeeded;

        public LevelProgress(int level, int levelXp, int levelXpNeeded) {
            Level = level;
            LevelXp = levelXp;
            LevelXpNeeded = levelXpNeeded;
        }

        public bool IsMaxLevel => LevelXpNeeded <= 0;
        /// <summary>Bar fill 0..1 (1 at the maximum level).</summary>
        public float Fraction => IsMaxLevel ? 1f : Math.Clamp((float)LevelXp / LevelXpNeeded, 0f, 1f);

        public bool Equals(LevelProgress other) => Level == other.Level && LevelXp == other.LevelXp && LevelXpNeeded == other.LevelXpNeeded;
        public override bool Equals(object obj) => obj is LevelProgress other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(Level, LevelXp, LevelXpNeeded);
        public override string ToString() => IsMaxLevel ? $"Lv {Level} (max)" : $"Lv {Level} {LevelXp}/{LevelXpNeeded}";
    }

    /// <summary>
    /// A game's level track: XP to level through a <see cref="LevelCurve"/>, plus the table of
    /// <see cref="UnlockDefinition"/>s its levels unlock. Immutable once built; thresholds are
    /// precomputed so lookups are a binary search.
    /// </summary>
    public sealed class LevelTrack {
        /// <summary>Total XP needed to reach level (index + 1); index 0 is level 1 (0 XP).</summary>
        readonly long[] thresholds;
        readonly UnlockDefinition[] unlocks;
        readonly Dictionary<string, UnlockDefinition> unlocksById = new(StringComparer.Ordinal);

        /// <exception cref="ArgumentException">An unlock has no id, a duplicate id, or a level outside 1..max.</exception>
        public LevelTrack(LevelCurve curve, IEnumerable<UnlockDefinition> unlockTable) {
            LevelCurve c = (curve ?? new LevelCurve()).Clone();
            c.Validate();
            MaxLevel = c.maxLevel;
            thresholds = new long[MaxLevel];
            for (int level = 2; level <= MaxLevel; level++)
                thresholds[level - 1] = thresholds[level - 2] + c.XpToNext(level - 1);

            var list = new List<UnlockDefinition>();
            if (unlockTable != null) {
                foreach (UnlockDefinition unlock in unlockTable) {
                    if (unlock == null) throw new ArgumentException("The unlock table contains a null entry.", nameof(unlockTable));
                    if (string.IsNullOrWhiteSpace(unlock.id)) throw new ArgumentException($"Unlock '{unlock.title}' has no id.", nameof(unlockTable));
                    if (unlock.level < 1 || unlock.level > MaxLevel)
                        throw new ArgumentException($"Unlock '{unlock.id}' is at level {unlock.level}, outside 1..{MaxLevel}.", nameof(unlockTable));
                    if (!unlocksById.TryAdd(unlock.id, unlock)) throw new ArgumentException($"Unlock id '{unlock.id}' is used twice.", nameof(unlockTable));
                    list.Add(unlock);
                }
            }
            // Stable sort by level keeps the table's order within a level.
            unlocks = list.Select((u, i) => (u, i)).OrderBy(p => p.u.level).ThenBy(p => p.i).Select(p => p.u).ToArray();
        }

        public int MaxLevel { get; }

        /// <summary>Every unlock, sorted by level.</summary>
        public IReadOnlyList<UnlockDefinition> Unlocks => unlocks;

        /// <summary>Total XP needed to reach <paramref name="level"/> (clamped to 1..max; level 1 is 0).</summary>
        public long XpForLevel(int level) => thresholds[Math.Clamp(level, 1, MaxLevel) - 1];

        /// <summary>Total XP that reaches the maximum level.</summary>
        public long MaxXp => thresholds[MaxLevel - 1];

        public int LevelForXp(long xp) {
            if (xp <= 0) return 1;
            // Largest level whose threshold is <= xp.
            int lo = 0, hi = thresholds.Length - 1;
            while (lo < hi) {
                int mid = (lo + hi + 1) >> 1;
                if (thresholds[mid] <= xp) lo = mid; else hi = mid - 1;
            }
            return lo + 1;
        }

        public LevelProgress Progress(long xp) {
            int level = LevelForXp(xp);
            if (level >= MaxLevel) return new LevelProgress(MaxLevel, 0, 0);
            long start = thresholds[level - 1];
            int needed = (int)(thresholds[level] - start);
            return new LevelProgress(level, (int)Math.Clamp(xp - start, 0L, needed), needed);
        }

        public UnlockDefinition FindUnlock(string id) =>
            id != null && unlocksById.TryGetValue(id, out UnlockDefinition unlock) ? unlock : null;

        /// <summary>Whether <paramref name="unlockId"/> is owned at <paramref name="level"/>; unknown ids are never owned.</summary>
        public bool IsUnlocked(int level, string unlockId) {
            UnlockDefinition unlock = FindUnlock(unlockId);
            return unlock != null && level >= unlock.level;
        }

        /// <summary>Adds the unlocks earned going from <paramref name="fromLevel"/> (exclusive) to <paramref name="toLevel"/> (inclusive).</summary>
        public void UnlocksBetween(int fromLevel, int toLevel, List<UnlockDefinition> into) {
            if (into == null) throw new ArgumentNullException(nameof(into));
            foreach (UnlockDefinition unlock in unlocks)
                if (unlock.level > fromLevel && unlock.level <= toLevel) into.Add(unlock);
        }

        /// <summary>The first unlock above <paramref name="level"/>, the player's next goal; null when everything is owned.</summary>
        public UnlockDefinition NextUnlock(int level) {
            foreach (UnlockDefinition unlock in unlocks)
                if (unlock.level > level) return unlock;
            return null;
        }
    }
}
