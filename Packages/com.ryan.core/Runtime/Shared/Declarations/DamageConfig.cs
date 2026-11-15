using System;
using System.Collections;
using UnityEngine;

namespace RyanAssets.Shared.Declarations {
    [Serializable]
    public class DamageConfig {
        [field: SerializeField]
        public long damageAmount { get; set; } = 1;
        [field: SerializeField]
        public DamageType damageType { get; set; } = DamageType.None;
    }
}
