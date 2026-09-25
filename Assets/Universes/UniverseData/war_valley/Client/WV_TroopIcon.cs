using System;
using UnityEngine;
using Universes.UniverseData.war_valley.Shared;

namespace Universes.UniverseData.war_valley.Client {
    /// <summary>One icon authored for a troop, which has no prefab of its own to carry one.</summary>
    [Serializable]
    public sealed class WV_TroopIcon {
        public WV_TroopKind kind = WV_TroopKind.Knife;
        public Sprite icon;
    }
}
