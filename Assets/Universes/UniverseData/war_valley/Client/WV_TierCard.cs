using System;
using RyanAssets.GameFeel;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Universes.UniverseData.war_valley.Shared;

namespace Universes.UniverseData.war_valley.Client {
    /// <summary>
    /// One tier on the tier choice panel: its name and emblem in the tier's colour, the commander's own
    /// stars on it (or "NEW" on a tier they have not won, or their best Endless wave), what it changes,
    /// the XP it pays, a "Your next goal" ribbon on the tier their campaign card names, and the votes it
    /// has. Authored by the HUD builder; <see cref="WV_TierChoicePanel"/> fills it.
    /// </summary>
    public sealed class WV_TierCard : MonoBehaviour {
        [SerializeField] Button button;
        [SerializeField] Image background;
        [Tooltip("Drawn round the card the commander voted for.")]
        [SerializeField] Outline selectedOutline;
        [SerializeField] Image accent;
        [SerializeField] Image emblem;
        [SerializeField] TextMeshProUGUI nameLabel;
        [SerializeField] TextMeshProUGUI statusLabel;
        [Tooltip("One image per star, left to right; hidden on Endless.")]
        [SerializeField] Image[] stars = Array.Empty<Image>();
        [Tooltip("The best Endless wave, in place of the stars.")]
        [SerializeField] TextMeshProUGUI bestWaveLabel;
        [SerializeField] GameObject newBadge;
        [SerializeField] TextMeshProUGUI descriptionLabel;
        [SerializeField] TextMeshProUGUI xpLabel;
        [SerializeField] TextMeshProUGUI votesLabel;
        [Tooltip("\"Your next goal\" ribbon over the card's top edge.")]
        [SerializeField] GameObject goalRibbon;
        [Tooltip("Shown on the tier the vote chose, in place of the votes line.")]
        [SerializeField] GameObject chosenStamp;
        [Tooltip("Fades the cards the vote did not choose.")]
        [SerializeField] CanvasGroup group;

        [SerializeField] Color starOn = new(1f, 0.8f, 0.3f, 1f);
        [SerializeField] Color starOff = new(1f, 1f, 1f, 0.16f);
        [SerializeField] Color cardFill = new(0.10f, 0.11f, 0.14f, 0.97f);
        [SerializeField, Range(0f, 0.4f)] float fillTint = 0.12f;
        [SerializeField] Color mutedText = new(0.72f, 0.76f, 0.82f, 1f);
        [SerializeField] Color goldText = new(1f, 0.8f, 0.32f, 1f);
        [SerializeField, Range(0f, 1f)] float dimmedAlpha = 0.4f;

        int shownVotes = -1;
        bool shownMine;

        /// <summary>The card was clicked.</summary>
        public event Action Clicked;

        public Button Button => button;

        void Awake() {
            if (button != null)
                button.onClick.AddListener(() => Clicked?.Invoke());
        }

        /// <summary>
        /// Fills the card. <paramref name="stars"/> is the commander's best on the tier (Endless ignores
        /// it and shows <paramref name="bestWave"/>); <paramref name="unlockedForMe"/> is false on a tier
        /// another commander opened.
        /// </summary>
        public void Show(WV_DifficultyTier tier, string tierName, string description, int xpBonusPercent, int stars,
            int bestWave, bool unlockedForMe, bool isGoal) {
            Color tint = WV_TierStyle.Accent(tier);
            bool endless = !WV_Campaign.HasStars(tier);
            bool isNew = unlockedForMe && (endless ? bestWave <= 0 : stars <= 0) && tier != WV_DifficultyTier.Recruit;

            if (background != null) {
                Color fill = Color.Lerp(cardFill, tint, fillTint);
                fill.a = cardFill.a;
                background.color = fill;
            }
            if (selectedOutline != null) {
                selectedOutline.effectColor = tint;
                selectedOutline.enabled = false;
            }
            if (accent != null)
                accent.color = tint;
            if (emblem != null)
                emblem.color = tint;
            if (nameLabel != null)
                nameLabel.text = tierName.ToUpperInvariant();
            if (statusLabel != null)
                statusLabel.text = !unlockedForMe ? "Opened by an ally"
                    : endless ? (bestWave > 0 ? "Your best run" : "Never tried")
                    : stars >= WV_Campaign.MaxStars ? "Mastered"
                    : stars > 0 ? $"Best: {stars}/{WV_Campaign.MaxStars} stars"
                    : "Not won yet";

            for (int i = 0; i < this.stars.Length; i++) {
                if (this.stars[i] == null)
                    continue;
                this.stars[i].gameObject.SetActive(!endless);
                this.stars[i].color = i < stars ? starOn : starOff;
            }
            if (bestWaveLabel != null) {
                // With no run yet the status line already says "Never tried", and the row is left to
                // the NEW badge that shares it.
                bestWaveLabel.gameObject.SetActive(endless && bestWave > 0);
                bestWaveLabel.text = $"Best wave {bestWave}";
            }
            if (newBadge != null)
                newBadge.SetActive(isNew);
            if (descriptionLabel != null)
                descriptionLabel.text = description;
            if (xpLabel != null) {
                xpLabel.text = xpBonusPercent > 0 ? $"+{xpBonusPercent}% XP" : "Standard XP";
                xpLabel.color = xpBonusPercent > 0 ? goldText : mutedText;
            }
            if (goalRibbon != null)
                goalRibbon.SetActive(isGoal);
            if (chosenStamp != null)
                chosenStamp.SetActive(false);
            if (group != null)
                group.alpha = 1f;
            if (button != null)
                button.interactable = true;
            shownVotes = -1;
            SetVotes(0, false);
            gameObject.SetActive(true);
        }

        /// <summary>The votes this tier has, and whether one of them is the commander's own.</summary>
        public void SetVotes(int votes, bool mine) {
            if (votes == shownVotes && mine == shownMine)
                return;
            bool justPicked = mine && !shownMine;
            shownVotes = votes;
            shownMine = mine;
            if (votesLabel != null)
                votesLabel.text = votes <= 0 ? "<alpha=#88>Click to vote" : mine
                    ? (votes == 1 ? "Your vote" : $"Your vote + {votes - 1}")
                    : (votes == 1 ? "1 vote" : $"{votes} votes");
            if (selectedOutline != null)
                selectedOutline.enabled = mine;
            if (justPicked)
                Punch.Pop(transform, 0.9f, 0.25f);
        }

        /// <summary>The vote is over: stamps the chosen tier and fades the rest.</summary>
        public void SetResult(bool chosen) {
            if (button != null)
                button.interactable = false;
            if (chosenStamp != null)
                chosenStamp.SetActive(chosen);
            if (votesLabel != null)
                votesLabel.gameObject.SetActive(!chosen);
            if (group != null)
                group.alpha = chosen ? 1f : dimmedAlpha;
            if (chosen)
                Punch.Pop(transform, 0.85f, 0.3f);
        }

        public void Hide() {
            if (votesLabel != null)
                votesLabel.gameObject.SetActive(true);
            gameObject.SetActive(false);
        }
    }
}
