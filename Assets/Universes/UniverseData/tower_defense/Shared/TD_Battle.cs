using System.Collections.Generic;

namespace Universes.UniverseData.tower_defense {
    /// <summary>One enemy on the road, as the server simulates it.</summary>
    public sealed class TD_SimEnemy {
        public ushort id;
        public TD_EnemyKind kind;
        public float distance, health, maxHealth, slowUntil, slowFactor = 1f;
        public bool Flying => TD_Rules.Enemy(kind).flying;
        /// <summary>Current speed multiplier at simulation time <paramref name="now"/> (1 = not slowed).</summary>
        public float Slow(float now) => now < slowUntil ? slowFactor : 1f;
    }

    /// <summary>
    /// The two ways an enemy leaves the road, kept apart: it reaches the base (a leak: base damage,
    /// no reward) or a tower destroys it (a kill: reward, no base damage). Plain code so tests can
    /// run it; <c>TD_ServerRunner</c> owns the lists and the money.
    /// </summary>
    public static class TD_Battle {
        /// <summary>
        /// Moves every enemy <paramref name="dt"/> seconds along the lane (slows applied). Enemies that
        /// reach the end are removed and added to <paramref name="leaked"/>; returns the base damage they do.
        /// </summary>
        public static int Advance(List<TD_SimEnemy> enemies, float dt, float now, List<ushort> leaked) {
            float end = TD_Rules.LaneTrack.Length;
            int damage = 0;
            for (int i = enemies.Count - 1; i >= 0; i--) {
                var e = enemies[i];
                e.distance += TD_Rules.Enemy(e.kind).speed * e.Slow(now) * dt;
                if (e.distance < end) continue;
                damage += TD_Rules.Enemy(e.kind).leakDamage;
                leaked.Add(e.id);
                enemies.RemoveAt(i);
            }
            return damage;
        }

        /// <summary>
        /// Takes <paramref name="amount"/> health from <paramref name="e"/>. When that destroys it, it is
        /// removed, added to <paramref name="killed"/> and the reward is returned; otherwise 0.
        /// </summary>
        public static int Damage(List<TD_SimEnemy> enemies, TD_SimEnemy e, float amount, List<ushort> killed) {
            // Gone already (leaked or killed): a late hit must not pay a reward.
            int index = e.health > 0f ? enemies.IndexOf(e) : -1;
            if (index < 0) return 0;
            e.health -= amount;
            if (e.health > 0f) return 0;
            enemies.RemoveAt(index);
            killed.Add(e.id);
            return TD_Rules.Enemy(e.kind).reward;
        }
    }
}
