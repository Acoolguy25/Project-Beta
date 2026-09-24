using System.Collections;
using UnityEngine;

namespace RyanAssets.Shared.Declarations {
    public enum DamageType {
        None,
        Fall,
        Melee,
        Gun,
        Water,
        Lava,

        Despawn,
        Reset,

        Command,

        // Appended rather than slotted in beside the other weapon types: the ordinals of this enum
        // are what get serialized on prefabs and sent in damage RPCs, so an existing member must
        // never shift position.
        Explosion
    };
}