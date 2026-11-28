using System;
using System.Collections.Generic;
using RyanAssets.GameFeel;
using RyanAssets.Retention.Client;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Universes.UniverseData.war_valley.Shared;

namespace Universes.UniverseData.war_valley.Client {
    /// <summary>
    /// The campaign at a glance, on the left edge: the round's tier and the commander's best stars
    /// on it, their next goal, and the doctrines they hold this round. Authored by the HUD builder;
    /// filled by <see cref="WV_DoctrinePresenter"/> from the server's broadcasts.
    /// </summary>
    public sealed class WV_CampaignCard : MonoBehaviour {
        [SerializeField] TextMeshProUGUI tierLabel;
        [Tooltip("One image per star, left to right.")]
        [SerializeField] Image[] stars = Array.Empty<Image>();
        [SerializeField] Color starOn = new(1f, 0.8f, 0.3f, 1f);
        [SerializeField] Color starOff = new(1f, 1f, 1f, 0.18f);
        [SerializeField] TextMeshProUGUI recordLabel;
        [SerializeField] TextMeshProUGUI goalLabel;
        [Tooltip("The day's front; hidden on a day without one.")]
        [SerializeField] TextMeshProUGUI frontLabel;
        [SerializeField] Image frontIcon;
        [Tooltip("One slot per doctrine the strip can show, in pick order.")]
        [SerializeField] Image[] doctrineIcons = Array.Empty<Image>();
        [Tooltip("Rank badges, parallel to the icons.")]
        [SerializeField] TextMeshProUGUI[] doctrineRanks = Array.Empty<TextMeshProUGUI>();
        [SerializeField] TextMeshProUGUI doctrinesEmptyLabel;
        [Tooltip("Theme icons by name; the HUD builder points this at the retention HUD's table.")]
        [SerializeField] RetentionIcons icons;
        [Tooltip("Fades the card out while the challenges panel or the round summary uses the same edge.")]
        [SerializeField] CanvasGroup group;
        [SerializeField, Min(0.01f)] float fadeSeconds = 0.15f;
        [SerializeField] string goalFormat = "Next: {0}";
        [SerializeField] string recordFormat = "Best wave {0}  -  {1} wins";
        [SerializeField] string recordFormatOneWin = "Best wave {0}  -  1 win";
        [SerializeField] string endlessRecordFormat = "Best Endless wave {0}";

        /// <summary>Doctrines in the order they were first picked, so a new pick joins the end of the strip.</summary>
        readonly List<WV_DoctrineId> order = new();
        bool covered;

        /// <summary>Steps the card aside (fades it out) while another panel uses the left edge.</summary>
        public void SetCovered(bool value) => covered = value;

        void Update() {
            if (group == null)
                return;
            float target = covered ? 0f : 1f;
            group.alpha = Mathf.MoveTowards(group.alpha, target, Time.unscaledDeltaTime / fadeSeconds);
        }

        public void SetStatus(WV_CampaignStatus status) {
            var tier = (WV_DifficultyTier)status.difficulty;
            if (tierLabel != null)
                tierLabel.text = (string.IsNullOrEmpty(status.difficultyName) ? tier.ToString() : status.difficultyName).ToUpperInvariant();

            bool starred = WV_Campaign.HasStars(tier);
            int best = starred && status.tierStars != null && status.difficulty < status.tierStars.Length
                ? status.tierStars[status.difficulty] : 0;
            for (int i = 0; i < stars.Length; i++) {
                if (stars[i] == null)
                    continue;
                stars[i].gameObject.SetActive(starred);
                stars[i].color = i < best ? starOn : starOff;
            }
            if (recordLabel != null)
                recordLabel.text = starred
                    ? string.Format(status.victories == 1 ? recordFormatOneWin : recordFormat, status.bestWave, status.victories)
                    : string.Format(endlessRecordFormat, status.bestEndlessWave);
            if (goalLabel != null)
                goalLabel.text = string.IsNullOrEmpty(status.nextGoal) ? string.Empty : string.Format(goalFormat, status.nextGoal);
            bool hasFront = !string.IsNullOrEmpty(status.frontText);
            if (frontLabel != null) {
                frontLabel.text = hasFront ? status.frontText : string.Empty;
                frontLabel.gameObject.SetActive(hasFront);
            }
            if (frontIcon != null)
                frontIcon.gameObject.SetActive(hasFront);
            gameObject.SetActive(true);
        }

        /// <summary>Whether the card is on screen and not stepped aside, so the wave arrows keep clear of it.</summary>
        public bool Showing => gameObject.activeInHierarchy && !covered;

        /// <summary>
        /// The slot <paramref name="doctrine"/> shows in (or will, once its pick arrives: the end of the
        /// strip), for a picked card to fly to. Null when the strip is full or the card is hidden.
        /// </summary>
        public RectTransform SlotFor(byte doctrine) {
            if (!gameObject.activeInHierarchy || covered)
                return null;
            int slot = order.IndexOf((WV_DoctrineId)doctrine);
            if (slot < 0)
                slot = order.Count;
            return slot < doctrineIcons.Length && doctrineIcons[slot] != null ? doctrineIcons[slot].rectTransform : null;
        }

        /// <summary>Shows the doctrines held this round. <paramref name="picked"/> (if valid) is punched as the newest.</summary>
        public void SetDoctrines(byte[] ranks, byte picked) {
            if (ranks == null || ranks.Length == 0 || Sum(ranks) == 0)
                order.Clear();
            if (ranks != null) {
                for (int i = 0; i < ranks.Length; i++) {
                    var id = (WV_DoctrineId)i;
                    if (ranks[i] > 0 && !order.Contains(id))
                        order.Add(id);
                }
            }

            for (int slot = 0; slot < doctrineIcons.Length; slot++) {
                Image icon = doctrineIcons[slot];
                TextMeshProUGUI rank = slot < doctrineRanks.Length ? doctrineRanks[slot] : null;
                WV_DoctrineDefinition doctrine = slot < order.Count ? WV_DoctrineCatalog.Get(order[slot]) : null;
                bool show = doctrine != null && icon != null;
                if (icon != null) {
                    icon.gameObject.SetActive(show);
                    if (show) {
                        icon.sprite = icons != null ? icons.Get(doctrine.Icon) : null;
                        icon.color = WV_DoctrineCard.AccentFor(doctrine);
                        if ((byte)doctrine.Id == picked)
                            Punch.Pop(icon.transform);
                    }
                }
                if (rank != null) {
                    int value = show ? ranks[(int)doctrine.Id] : 0;
                    rank.text = value > 1 ? value.ToString() : string.Empty;
                }
            }
            if (doctrinesEmptyLabel != null)
                doctrinesEmptyLabel.gameObject.SetActive(order.Count == 0);
        }

        static int Sum(byte[] values) {
            int total = 0;
            foreach (byte value in values)
                total += value;
            return total;
        }
    }
}
