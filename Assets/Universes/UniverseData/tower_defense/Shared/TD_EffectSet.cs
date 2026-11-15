using System;
using UnityEngine;

namespace Universes.UniverseData.tower_defense {
    /// <summary>Client-side effect prefabs. Any slot may be left empty; the effect is then skipped.</summary>
    [Serializable]
    public sealed class TD_EffectSet {
        [Tooltip("Spawned at a tower's muzzle on every shot.")]
        public GameObject muzzleFlash;
        [Tooltip("Spawned where a bullet or sniper round lands.")]
        public GameObject impact;
        [Tooltip("Spawned where a frost bolt lands.")]
        public GameObject frostImpact;
        [Tooltip("Cannon shell landings and destroyed enemies.")]
        public GameObject explosion;
        [Tooltip("Line renderer reused for bullet tracers and sniper beams.")]
        public GameObject tracer;
        [Tooltip("Visible projectile for cannon shells and frost bolts (tinted per tower).")]
        public GameObject projectile;
        [Tooltip("World-space text that rises from destroyed enemies (\"+$12\").")]
        public GameObject rewardPopup;
        [Min(0.05f)] public float effectLifetime = 1.5f;
    }
}
