using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Universes.UniverseData.tower_defense.Client {
    /// <summary>One enemy type in the next-wave preview: icon, count and, the first wave it comes, "NEW" and its trait.</summary>
    [Serializable]
    public sealed class TD_EnemyChip {
        public GameObject root;
        public Image icon;
        public TMP_Text count;
        [Tooltip("\"NEW · Shielded\": shown only on the wave that introduces this enemy.")]
        public TMP_Text trait;
    }
}
