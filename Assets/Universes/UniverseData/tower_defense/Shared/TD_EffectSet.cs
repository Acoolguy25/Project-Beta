using System;
using UnityEngine;

namespace Universes.UniverseData.tower_defense {
    /// <summary>
    /// Tower Defense's own effect prefabs: what the shared game feel kit has no equivalent for (shot
    /// lines, flying shells, lightning arcs, the airstrike jet). Bursts, popups and sounds come from the
    /// kit. Any slot may be left empty; the effect is then skipped (or falls back as noted).
    /// </summary>
    [Serializable]
    public sealed class TD_EffectSet {
        [Tooltip("Line renderer reused for bullet tracers and sniper beams.")]
        public GameObject tracer;
        [Tooltip("Visible projectile for cannon shells and frost bolts (tinted per tower).")]
        public GameObject projectile;
        [Tooltip("Many-point line renderer for Tesla lightning (jagged and re-jittered while it lives); falls back to the tracer.")]
        public GameObject arc;
        [Tooltip("The jet that flies over the road for an Airstrike (a Cartoon Military jet, nose along +Z).")]
        public GameObject jet;
    }
}
