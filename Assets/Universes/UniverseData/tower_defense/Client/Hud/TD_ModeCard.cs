using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Universes.UniverseData.tower_defense.Client {
    /// <summary>
    /// One mode on the vote screen before wave 1: name, what it changes, the player's best (stars, or
    /// the best Endless wave), the votes it has and, while locked, what opens it. Lifts while it leads
    /// the vote; outlined in the mode's colour when it is the player's own vote.
    /// </summary>
    public sealed class TD_ModeCard : MonoBehaviour {
        public RectTransform body;
        public CanvasGroup group;
        public Image outline, icon, accentBar;
        public TMP_Text title, blurb, best, votes, keyLabel;
        [Tooltip("Rating stars (best win on this mode); hidden on Endless, which shows its best wave instead.")]
        public Image[] stars;
        [Tooltip("Covers the card while the mode is locked.")]
        public GameObject lockGroup;
        public TMP_Text lockLabel;
        [Min(0f)] public float leadingLift = 12f;
        [Tooltip("How fast the leading card lifts (1/s).")]
        [Min(0.1f)] public float liftSharpness = 14f;
        [Range(0f, 1f)] public float lockedAlpha = 0.95f;
        [Tooltip("Title and icon opacity while locked (the lock and its hint stay at full strength).")]
        [Range(0f, 1f)] public float lockedTitleAlpha = 0.45f;
        public Color starOn = new(1f, 0.82f, 0.25f), starOff = new(1f, 1f, 1f, 0.16f);

        bool leading;

        /// <param name="bestStars">Best rating on this mode (0 = never won it).</param>
        /// <param name="bestEndlessWave">Best Endless wave (Endless only; 0 = never played).</param>
        public void Set(TD_Difficulty mode, bool unlocked, int bestStars, int bestEndlessWave, int voteCount, bool mine, bool isLeading) {
            var stats = TD_Modes.Get(mode);
            Color accent = TD_Palette.Mode(mode);
            leading = isLeading && unlocked;
            title.text = stats.name.ToUpperInvariant();
            blurb.text = stats.blurb;
            icon.color = Faded(accent, unlocked ? 1f : lockedTitleAlpha);
            title.alpha = unlocked ? 1f : lockedTitleAlpha;
            // The lock and its hint take the blurb's place: hiding (not removing) it keeps the layout still.
            blurb.alpha = unlocked ? 1f : 0f;
            if (accentBar != null) accentBar.color = accent;
            Color edge = accent;
            edge.a = mine ? 1f : 0f;
            outline.color = edge;
            group.alpha = unlocked ? 1f : lockedAlpha;
            if (lockGroup != null) lockGroup.SetActive(!unlocked);
            if (lockLabel != null) lockLabel.text = stats.lockedHint;
            bool endless = stats.Endless;
            for (int i = 0; i < stars.Length; i++) {
                stars[i].gameObject.SetActive(!endless);
                stars[i].color = unlocked ? i < bestStars ? starOn : starOff : Color.clear;
            }
            best.text = !unlocked ? ""
                : endless ? bestEndlessWave > 0 ? $"Best: wave {bestEndlessWave}" : "No record yet"
                : bestStars > 0 ? $"Best: {bestStars} / {TD_Rules.MaxStars}" : "Not won yet";
            votes.text = !unlocked ? "" : voteCount == 1 ? "1 vote" : $"{voteCount} votes";
        }

        static Color Faded(Color color, float alpha) {
            color.a *= alpha;
            return color;
        }

        void Update() {
            float y = leading ? leadingLift : 0f;
            Vector2 p = body.anchoredPosition;
            p.y = Mathf.Lerp(p.y, y, 1f - Mathf.Exp(-liftSharpness * Time.unscaledDeltaTime));
            body.anchoredPosition = p;
        }
    }
}
