using RyanAssets.GameFeel;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Universes.UniverseData.tycoon.Client {
    /// <summary>
    /// The always-visible "next goal" card above the wallet: goal number, what to do, a progress bar and
    /// what it pays. Built by TY_HudAuthoring; <see cref="TY_Hud"/> binds the wallet's goal to it.
    /// </summary>
    public sealed class TY_GoalCard : MonoBehaviour {
        public TMP_Text number, text, progress, reward;
        [Tooltip("The pill around the reward text; hidden when the goal pays no cash.")]
        public GameObject rewardPill;
        public Image fill;
        [Tooltip("Shown when the goal pays perk tokens.")]
        public GameObject tokenBadge;
        public TMP_Text tokenText;
        [Tooltip("Punched when a goal is completed.")]
        public Transform punchTarget;
        [Range(0f, 1f)] public float completePunch = 0.25f;
        [Tooltip("How fast the bar follows the progress (fraction per second).")]
        [Min(0.1f)] public float fillSpeed = 2.5f;
        [Tooltip("{0} = goal number (1-based).")]
        public string numberFormat = "GOAL {0}";
        [Tooltip("{0} = progress, {1} = target.")]
        public string progressFormat = "{0} / {1}";

        int shownIndex = -1;
        long shownProgress = -1, shownReward = -1;
        float fillTarget;

        /// <summary>Shows goal <paramref name="index"/> at <paramref name="value"/> of its target, paying <paramref name="cash"/>.</summary>
        public void Show(int index, long value, long cash) {
            if (index == shownIndex && value == shownProgress && cash == shownReward) return;
            TY_Goal goal = TY_Goals.Get(index);
            bool newGoal = index != shownIndex;
            shownIndex = index;
            shownProgress = value;
            shownReward = cash;
            if (newGoal) {
                number.text = string.Format(numberFormat, index + 1);
                text.text = goal.Describe();
                // A new goal's bar starts from its own progress, not from the old goal's full bar.
                fill.fillAmount = 0f;
            }
            long clamped = System.Math.Min(value, goal.target);
            progress.text = string.Format(progressFormat, goal.Format(clamped), goal.Format(goal.target));
            fillTarget = goal.target > 0 ? Mathf.Clamp01((float)((double)clamped / goal.target)) : 1f;
            // Seconds of income read the same at any income; right after a rebirth the dollars would look tiny.
            reward.text = goal.PaysIncomeSeconds ? $"+{goal.incomeSeconds:0}s income"
                : cash > 0 ? $"+${TY_Catalog.Money(cash)}" : goal.tokens > 0 ? "" : $"+{goal.xp} XP";
            if (rewardPill != null) rewardPill.SetActive(reward.text.Length > 0);
            if (tokenBadge != null) tokenBadge.SetActive(goal.tokens > 0);
            if (tokenText != null) tokenText.text = $"+{goal.tokens}";
        }

        /// <summary>The previous goal was just completed.</summary>
        public void Completed() {
            if (punchTarget != null) Punch.Scale(punchTarget, completePunch);
        }

        void Update() {
            if (!Mathf.Approximately(fill.fillAmount, fillTarget))
                fill.fillAmount = Mathf.MoveTowards(fill.fillAmount, fillTarget, Time.unscaledDeltaTime * fillSpeed);
        }
    }
}
