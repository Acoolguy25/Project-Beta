using RyanAssets.Levels.Shared;
using RyanAssets.TweenService.TweenComponents;
using TMPro;
using UnityEngine;

namespace RyanAssets.Levels.Client {
    /// <summary>
    /// A level badge: the player's rank and a bar showing progress to the next rank. Subclasses
    /// decide where the XP comes from.
    /// </summary>
    public abstract class LevelsClient : MonoBehaviour {
        [SerializeField] TMP_Text levelText;
        [Tooltip("Stretched from the left by the progress to the next level.")]
        [SerializeField] RectTransform progressFill;
        [Tooltip("{0} is the level.")]
        [SerializeField] string levelFormat = "{0}";
        [SerializeField, Min(0f)] float fillDuration = 0.6f;

        public void UpdateLevel(ulong xp, bool instant = false) {
            levelText.text = string.Format(levelFormat, LevelsCalc.GetRank(xp));
            float progress = Mathf.Clamp01((float)LevelsCalc.GetRankProgress(xp));
            TweenRectTransform.AnchorTween(progressFill, instant ? 0f : fillDuration, Vector2.zero, new Vector2(progress, 1f));
        }
    }
}
