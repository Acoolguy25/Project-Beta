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
        [Tooltip("Elite dressing per path (index 0 = TD_TowerPath.First) on top of the level 3 model: crown plate, orbiting cores, foot glow.")]
        public GameObject[] eliteParts;
        [Tooltip("Badge stars in each path's colour (index 0 = First), shown instead of the gold level stars once specialised.")]
        public GameObject[] eliteStars;
        [Tooltip("Pivots the client spins around the tower's axis on elite towers (the orbiting cores), index 0 = First.")]
        public Transform[] eliteSpinners;
        [Tooltip("Uniform scale of an elite tower (level 3 is levelScale[2]).")]
        public float eliteScale = TD_Elites.Scale;

        /// <summary>Where the current level's shots leave from.</summary>
        public Transform Muzzle => tierMuzzles != null && shownLevel >= 1 && shownLevel <= tierMuzzles.Length && tierMuzzles[shownLevel - 1] != null ? tierMuzzles[shownLevel - 1] : muzzle;

        [HideInInspector] public Quaternion aim = Quaternion.identity;
        [HideInInspector] public float recoil;
        [HideInInspector] public int shownLevel;
        [HideInInspector] public TD_TowerPath shownPath;

        /// <summary>The spinner of the elite form being shown, or null.</summary>
        public Transform ActiveSpinner => TD_Elites.IsElite(shownPath) && eliteSpinners != null && (int)shownPath - 1 < eliteSpinners.Length
            ? eliteSpinners[(int)shownPath - 1] : null;

        /// <summary>Shows the model, stars and scale of <paramref name="level"/> (1-based) and, at level 3, its elite form.</summary>
        public void ShowLevel(int level, TD_TowerPath path = TD_TowerPath.None) {
            shownLevel = level;
            shownPath = level >= TD_Elites.Level ? path : TD_TowerPath.None;
            bool elite = TD_Elites.IsElite(shownPath);
            Toggle(eliteParts, shownPath);
            Toggle(eliteStars, shownPath);
            if (tiers != null)
                for (int i = 0; i < tiers.Length; i++)
                    if (tiers[i] != null) tiers[i].SetActive(i == Mathf.Min(level, tiers.Length) - 1);
            if (levelStars != null)
                for (int i = 0; i < levelStars.Length; i++) levelStars[i].SetActive(!elite && i < level);
            if (badge != null && badgeHeight != null && badgeHeight.Length > 0) {
                Vector3 p = badge.localPosition;
                p.y = badgeHeight[Mathf.Clamp(level - 1, 0, badgeHeight.Length - 1)];
                badge.localPosition = p;
            }
            float scale = levelScale != null && levelScale.Length > 0 ? levelScale[Mathf.Clamp(level - 1, 0, levelScale.Length - 1)] : 1f;
            transform.localScale = Vector3.one * (elite ? eliteScale : scale);
        }

        /// <summary>Shows only the entry of <paramref name="parts"/> for <paramref name="path"/> (none for no path).</summary>
        static void Toggle(GameObject[] parts, TD_TowerPath path) {
            if (parts == null) return;
            for (int i = 0; i < parts.Length; i++)
                if (parts[i] != null) parts[i].SetActive(i == (int)path - 1);
        }
    }
}
