using RyanAssets.UI.GameHud;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RyanAssets.Retention.Client {
    /// <summary>One daily challenge in the <see cref="RetentionChallengesPanel"/>: text, progress bar, count and reward.</summary>
    [DisallowMultipleComponent]
    public sealed class RetentionChallengeRow : MonoBehaviour {
        public Image icon;
        public Sprite openIcon;
        public Sprite doneIcon;
        public TMP_Text title;
        public TMP_Text count;
        public HudBar bar;
        public TMP_Text xpReward;
        public GameObject goldGroup;
        public TMP_Text goldReward;
        [Tooltip("Faded while the challenge is done.")]
        public CanvasGroup body;
        [Range(0f, 1f)] public float doneAlpha = 0.6f;
        [Tooltip("Shown instead of the count once the challenge is done.")]
        public string doneText = "Done";

        Color countColor;
        bool initialised;

        public void Set(in RetentionChallengeView challenge, RetentionHudStyle style) {
            if (!initialised) {
                initialised = true;
                countColor = count != null ? count.color : Color.white;
            }
            bool done = challenge.done;
            if (title != null) title.text = challenge.text;
            if (count != null) {
                if (done) count.text = doneText;
                else count.SetText("{0}/{1}", (float)challenge.progress, challenge.target); // float args: two ints would be ambiguous with TMP's (span, start, length) overload
                count.color = done ? style.challenge : countColor;
            }
            if (bar != null) {
                bar.Value = challenge.target > 0 ? (float)challenge.progress / challenge.target : 0f;
                bar.SetColor(done ? style.muted : style.challenge);
            }
            if (icon != null) {
                icon.sprite = done && doneIcon != null ? doneIcon : openIcon != null ? openIcon : icon.sprite;
                icon.color = done ? style.challenge : style.muted;
            }
            if (xpReward != null) xpReward.SetText("+{0} XP", challenge.xpReward);
            if (goldGroup != null) goldGroup.SetActive(challenge.goldReward > 0);
            if (goldReward != null && challenge.goldReward > 0) goldReward.SetText("+{0}", challenge.goldReward);
            if (body != null) body.alpha = done ? doneAlpha : 1f;
        }
    }
}
