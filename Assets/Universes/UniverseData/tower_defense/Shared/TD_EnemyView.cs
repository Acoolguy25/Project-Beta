using UnityEngine;

namespace Universes.UniverseData.tower_defense {
    /// <summary>
    /// Enemy presentation: a body to bank and bob (and tint while slowed), a health bar, a frost shell
    /// and, on shielded enemies, an energy bubble with its own bar (shown while the shield holds).
    /// </summary>
    public sealed class TD_EnemyView : MonoBehaviour {
        public Transform body;
        [Tooltip("Wheels, rotors, suspension and banking; null on a plain body.")]
        public TD_EnemyAnimator animator;
        [Tooltip("The vehicle's renderers: tinted while slowed and flashed when hit.")]
        public Renderer[] bodyRenderers;
        [Tooltip("Scaled on X from 0 to 1 with the enemy's health.")]
        public Transform healthFill;
        [Tooltip("Recoloured from healthy to critical as the enemy loses health.")]
        public Renderer healthRenderer;
        public Renderer slowTint;
        [Tooltip("Energy bubble shown while the enemy's shield holds (shielded enemies only).")]
        public GameObject shieldBubble;
        [Tooltip("Scaled on X from 0 to 1 with the shield left (above the health bar; shielded enemies only).")]
        public Transform shieldFill;
        [HideInInspector] public float shield, nextHealFx;
        [HideInInspector] public float shownDistance, targetDistance, speed, health = 1f, hitFlash;
        [HideInInspector] public bool seen, slowed, tinted;
    }
}
