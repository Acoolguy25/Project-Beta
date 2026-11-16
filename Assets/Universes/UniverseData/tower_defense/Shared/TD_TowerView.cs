using TMPro;
using UnityEngine;

namespace Universes.UniverseData.tower_defense {
    /// <summary>
    /// Tower presentation: a head that turns to its target, a muzzle for shots, one model per level
    /// (each upgrade swaps the model, repaints it and grows the tower), level stars, an owner label,
    /// and range rings (current and next level) shown while a player looks at the tower.
    /// </summary>
    public sealed class TD_TowerView : MonoBehaviour {
        public Transform head, muzzle;
        [Tooltip("One model per level under the head (index 0 = level 1); only the current level's is active.")]
        public GameObject[] tiers;
        [Tooltip("Uniform scale of the whole tower per level (index 0 = level 1).")]
        public float[] levelScale = { 1f, 1.12f, 1.24f };
        public TMP_Text label;
        [Tooltip("One star per level, lit from the left.")]
        public GameObject[] levelStars;
        public LineRenderer rangeRing;
        [Tooltip("Fainter ring previewing the range after the next upgrade.")]
        public LineRenderer nextRangeRing;
        [Tooltip("Degrees per second the head turns toward its target.")]
        [Min(1f)] public float turnSpeed = 540f;

        [HideInInspector] public Quaternion aim = Quaternion.identity;
        [HideInInspector] public float recoil;
        [HideInInspector] public int shownLevel;

        /// <summary>Shows the model, stars and scale of <paramref name="level"/> (1-based).</summary>
        public void ShowLevel(int level) {
            shownLevel = level;
            if (tiers != null)
                for (int i = 0; i < tiers.Length; i++)
                    if (tiers[i] != null) tiers[i].SetActive(i == Mathf.Min(level, tiers.Length) - 1);
            if (levelStars != null)
                for (int i = 0; i < levelStars.Length; i++) levelStars[i].SetActive(i < level);
            float scale = levelScale != null && levelScale.Length > 0 ? levelScale[Mathf.Clamp(level - 1, 0, levelScale.Length - 1)] : 1f;
            transform.localScale = Vector3.one * scale;
        }
    }
}
