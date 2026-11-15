using TMPro;
using UnityEngine;

namespace Universes.UniverseData.tower_defense {
    /// <summary>Tower presentation: a head that turns to its target, a muzzle for tracers, a level label.</summary>
    public sealed class TD_TowerView : MonoBehaviour {
        public Transform head, muzzle;
        public TMP_Text label;
        public GameObject[] levelPips;
        public AudioSource fireSound;
    }
}
