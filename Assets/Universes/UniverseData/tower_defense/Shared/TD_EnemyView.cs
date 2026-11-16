using UnityEngine;

namespace Universes.UniverseData.tower_defense {
    /// <summary>Enemy presentation: a body to bank and bob (and tint while slowed), a health bar and a frost shell.</summary>
    public sealed class TD_EnemyView : MonoBehaviour {
        public Transform body;
        [Tooltip("The vehicle's renderers: tinted while slowed and flashed when hit.")]
        public Renderer[] bodyRenderers;
        [Tooltip("Scaled on X from 0 to 1 with the enemy's health.")]
        public Transform healthFill;
        [Tooltip("Recoloured from healthy to critical as the enemy loses health.")]
        public Renderer healthRenderer;
        public Renderer slowTint;
        [HideInInspector] public float shownDistance, targetDistance, speed, health = 1f, hitFlash;
        [HideInInspector] public bool seen, slowed, tinted;
    }
}
