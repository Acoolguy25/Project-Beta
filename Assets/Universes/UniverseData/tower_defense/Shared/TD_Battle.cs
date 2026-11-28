using System.Collections.Generic;
using UnityEngine;

namespace Universes.UniverseData.tower_defense {
    /// <summary>One enemy on the road, as the server simulates it.</summary>
    public sealed class TD_SimEnemy {
        public ushort id;
        public TD_EnemyKind kind;
        public float distance, health, maxHealth, slowUntil, slowFactor = 1f;
        /// <summary>Energy shield left (soaks hits before health) and its size when the enemy spawned.</summary>
        public float shield, maxShield;
        /// <summary>Simulation time until which the enemy stands still (EMP).</summary>
        public float stunUntil;
        /// <summary>Simulation time until which every hit on it does <see cref="TD_Elites.BrittleBonus"/> more (Brittle Ice).</summary>
        public float brittleUntil;
        public bool Flying => TD_Rules.Enemy(kind).flying;
        public bool Shielded => shield > 0f;
        /// <summary>Current speed multiplier at simulation time <paramref name="now"/> (1 = not slowed, 0 = stunned).</summary>
        public float Slow(float now) => now < stunUntil ? 0f : now < slowUntil ? slowFactor : 1f;
    }

    /// <summary>What a single hit did to an enemy.</summary>
    public enum TD_HitResult : byte { Blocked, Shield, ShieldBroken, Health, Killed, Gone }

    /// <summary>
    /// The two ways an enemy leaves the road, kept apart: it reaches the base (a leak: base damage,
    /// no reward) or a tower destroys it (a kill: reward, no base damage). Plus the rules of shields
    /// and medics. Plain code so tests can run it; <see cref="TD_Simulation"/> owns the lists and money.
    /// </summary>
    public static class TD_Battle {
        /// <summary>
        /// Moves every enemy <paramref name="dt"/> seconds along the lane (slows applied). Enemies that
        /// reach the end are removed and added to <paramref name="leaked"/>; returns the base damage they do.
        /// <paramref name="speedScale"/> is the day's modifier on every enemy's speed.
        /// </summary>
        public static int Advance(List<TD_SimEnemy> enemies, float dt, float now, List<ushort> leaked, float speedScale = 1f) {
            float end = TD_Rules.LaneTrack.Length;
            int damage = 0;
            for (int i = enemies.Count - 1; i >= 0; i--) {
                var e = enemies[i];
                e.distance += TD_Rules.Enemy(e.kind).speed * speedScale * e.Slow(now) * dt;
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

        /// <summary>
        /// One hit of <paramref name="raw"/> damage (before armour): a shield soaks it first (hits weaker than
        /// <see cref="TD_Rules.ShieldBlockBelow"/> bounce off; the overflow of the breaking hit is lost),
        /// then armour (unless <paramref name="pierce"/>) and health. <paramref name="ignoreBlock"/> lets
        /// bombs through the bounce rule.
        /// </summary>
        public static TD_HitResult Strike(List<TD_SimEnemy> enemies, TD_SimEnemy e, float raw, bool pierce, List<ushort> killed, bool ignoreBlock = false) {
            if (e.health <= 0f) return TD_HitResult.Gone;
            if (e.shield > 0f) {
                if (!ignoreBlock && TD_Rules.BouncesOffShields(raw)) return TD_HitResult.Blocked;
                e.shield -= raw;
                if (e.shield > 0f) return TD_HitResult.Shield;
                e.shield = 0f;
                return TD_HitResult.ShieldBroken;
            }
            int before = killed.Count;
            Damage(enemies, e, TD_Rules.ArmorDamage(raw, e.kind, pierce), killed);
            return killed.Count > before ? TD_HitResult.Killed : e.health > 0f ? TD_HitResult.Health : TD_HitResult.Gone;
        }

        /// <summary>
        /// Medics heal every other enemy within <see cref="TD_Rules.HealRadius"/> of road distance by their
        /// heal rate (a share of the medic's own maximum health per second), never above full health.
        /// Healed enemies are added to <paramref name="healed"/> once each.
        /// </summary>
        public static void Heal(List<TD_SimEnemy> enemies, float dt, List<ushort> healed) {
            for (int m = 0; m < enemies.Count; m++) {
                var medic = enemies[m];
                float rate = TD_Rules.Enemy(medic.kind).heal;
                if (rate <= 0f) continue;
                float amount = rate * medic.maxHealth * dt;
                for (int i = 0; i < enemies.Count; i++) {
                    var e = enemies[i];
                    if (i == m || e.health >= e.maxHealth || Mathf.Abs(e.distance - medic.distance) > TD_Rules.HealRadius) continue;
                    e.health = Mathf.Min(e.maxHealth, e.health + amount);
                    if (!healed.Contains(e.id)) healed.Add(e.id);
                }
            }
        }
    }
}
