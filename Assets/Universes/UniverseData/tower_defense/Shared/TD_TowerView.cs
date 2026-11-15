using TMPro;
using UnityEngine;

namespace Universes.UniverseData.tower_defense {
    /// <summary>
    /// Tower presentation: a head that turns to its target, a muzzle for shots, level stars, an owner
    /// label and a range ring shown while a player looks at the tower.
    /// </summary>
    public sealed class TD_TowerView : MonoBehaviour {
        public Transform head, muzzle;
        public TMP_Text label;
        [Tooltip("One star per level, lit from the left.")]
        public GameObject[] levelStars;
        public LineRenderer rangeRing;
        public AudioSource fireSound;
        [Tooltip("Degrees per second the head turns toward its target.")]
        [Min(1f)] public float turnSpeed = 540f;

        [HideInInspector] public Quaternion aim = Quaternion.identity;
        [HideInInspector] public float recoil;
    }
}
