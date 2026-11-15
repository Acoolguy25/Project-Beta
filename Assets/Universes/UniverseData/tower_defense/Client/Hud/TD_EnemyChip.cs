using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Universes.UniverseData.tower_defense.Client {
    /// <summary>One enemy type in the next-wave preview: icon, count and an "air"/"armoured" hint.</summary>
    [Serializable]
    public sealed class TD_EnemyChip {
        public GameObject root;
        public Image icon;
        public TMP_Text count;
    }
}
