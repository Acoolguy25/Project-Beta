using UnityEngine;

namespace Universes.UniverseData.tower_defense {
    /// <summary>
    /// Tower presentation: a head that turns to its target, a muzzle for shots, one model per level
    /// (each upgrade swaps the model, repaints it and grows the tower), a level-star badge
    /// and range rings (current and next level) shown while a player looks at the tower.
    /// </summary>
    public sealed class TD_TowerView : MonoBehaviour {
        public Transform head, muzzle;
        [Tooltip("One model per level under the head (index 0 = level 1); only the current level's is active.")]
        public GameObject[] tiers;
        [Tooltip("Uniform scale of the whole tower per level (index 0 = level 1).")]
        public float[] levelScale = { 1f, 1.12f, 1.24f };
        [Tooltip("The level-star badge above the tower (a camera-plane Billboard); the client hides it near the camera and the screen edges.")]
        public Transform badge;
        [Tooltip("Local height of the badge per level (index 0 = level 1), just above that level's model.")]
        public float[] badgeHeight;
        [Tooltip("One star per level, lit from the left.")]
        public GameObject[] levelStars;
        public LineRenderer rangeRing;
        [Tooltip("Fainter ring previewing the range after the next upgrade.")]
        public LineRenderer nextRangeRing;
        [Tooltip("Degrees per second the head turns toward its target.")]
        [Min(1f)] public float turnSpeed = 540f;
        [Tooltip("Gold trim shown on towers built by a player who owns the Gold Trim unlock.")]
        public GameObject goldTrim;
        [Tooltip("Optional muzzle per level (index 0 = level 1), for towers whose models put the muzzle at different heights (Tesla coils); else muzzle.")]
        public Transform[] tierMuzzles;

        /// <summary>Where the current level's shots leave from.</summary>
        public Transform Muzzle => tierMuzzles != null && shownLevel >= 1 && shownLevel <= tierMuzzles.Length && tierMuzzles[shownLevel - 1] != null ? tierMuzzles[shownLevel - 1] : muzzle;

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
            if (badge != null && badgeHeight != null && badgeHeight.Length > 0) {
                Vector3 p = badge.localPosition;
                p.y = badgeHeight[Mathf.Clamp(level - 1, 0, badgeHeight.Length - 1)];
                badge.localPosition = p;
            }
            float scale = levelScale != null && levelScale.Length > 0 ? levelScale[Mathf.Clamp(level - 1, 0, levelScale.Length - 1)] : 1f;
            transform.localScale = Vector3.one * scale;
        }
    }
}
