using System;
using UnityEngine;

namespace Universes.UniverseData.tower_defense {
    /// <summary>
    /// Tower Defense's own effect prefabs: what the shared game feel kit has no equivalent for (shot
    /// lines and flying shells). Bursts, popups and sounds come from the kit. Any slot may be left
    /// empty; the effect is then skipped.
    /// </summary>
    [Serializable]
    public sealed class TD_EffectSet {
        [Tooltip("Line renderer reused for bullet tracers and sniper beams.")]
        public GameObject tracer;
        [Tooltip("Visible projectile for cannon shells and frost bolts (tinted per tower).")]
        public GameObject projectile;
    }
}
