using UnityEngine;

namespace Universes.UniverseData.tower_defense {
    /// <summary>Enemy presentation: a body to bank and bob, and a health bar.</summary>
    public sealed class TD_EnemyView : MonoBehaviour {
        public Transform body, healthFill;
        public Renderer slowTint;
        [HideInInspector] public float shownDistance, targetDistance, speed;
        [HideInInspector] public bool seen;
    }
}
