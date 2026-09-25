using System;
using RyanAssets.Core;
using RyanAssets.Shared.WorldUI;
using RyanAssets.UI.Hover;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RyanAssets.Client.ClientUI.Command {
    /// <summary>
    /// One card in a <see cref="CommandOptionGrid"/>: icon, name, price, time, and whatever is
    /// stopping the player from ordering it.
    /// <para>
    /// Every part is authored on the card prefab and wired here, so a universe can restyle the card
    /// without touching code. The card only writes values; it never builds UI.
    /// </para>
    /// </summary>
    public sealed class CommandOptionCard : MonoBehaviour {
        [Header("Authored Parts")]
        [SerializeField] Button button;
        [SerializeField] Image background;
        [SerializeField] Image icon;
        [SerializeField] TextMeshProUGUI titleLabel;
        [SerializeField] TextMeshProUGUI subtitleLabel;
        [SerializeField] TextMeshProUGUI costLabel;
        [SerializeField] TextMeshProUGUI timeLabel;
        [Tooltip("Band across the card that explains a lock or shows research in progress.")]
        [SerializeField] GameObject stateBand;
        [SerializeField] TextMeshProUGUI stateLabel;
        [Tooltip("Filled image along the foot of the card for an option in progress.")]
        [SerializeField] Image progressFill;
        [Tooltip("The progress bar's track, hidden with it when there is no progress to show. " +
                 "Leave unset to toggle the fill alone.")]
        [SerializeField] GameObject progressRoot;
        [SerializeField] GameObject countBadge;
        [SerializeField] TextMeshProUGUI countLabel;
        [SerializeField] HoverItem hover;

        [Header("State Colours")]
        [SerializeField] Color availableBackground = new(0.13f, 0.16f, 0.2f, 1f);
        [SerializeField] Color lockedBackground = new(0.09f, 0.09f, 0.1f, 1f);
        [SerializeField] Color doneBackground = new(0.1f, 0.2f, 0.14f, 1f);
        [SerializeField] Color costColor = new(1f, 0.86f, 0.45f, 1f);
        [SerializeField] Color unaffordableCostColor = new(1f, 0.45f, 0.45f, 1f);
        [SerializeField] Color lockedTint = new(1f, 1f, 1f, 0.35f);

        Action<CommandOptionCard> clicked;

        /// <summary>The option this card is currently drawing.</summary>
        public CommandOption Option { get; private set; }

        void Awake() {
            if (button != null)
                button.onClick.AddListener(HandleClick);
        }

        void OnDestroy() {
            if (button != null)
                button.onClick.RemoveListener(HandleClick);
        }

        void HandleClick() {
            if (Option != null)
                clicked?.Invoke(this);
        }

        /// <summary>Draws <paramref name="option"/> and routes this card's clicks to <paramref name="onClick"/>.</summary>
        public void Bind(CommandOption option, Action<CommandOptionCard> onClick) {
            Option = option;
            clicked = onClick;
            if (option == null)
                return;

            bool locked = option.State == CommandOptionState.Locked;
            bool done = option.State == CommandOptionState.Done;

            if (icon != null) {
                icon.sprite = option.Icon;
                // An Image with no sprite draws a solid white box, so an option without art shows
                // an empty frame rather than a placeholder block.
                icon.enabled = option.Icon != null;
                icon.color = locked ? lockedTint : Color.white;
            }
            SetText(titleLabel, option.Title);
            SetText(subtitleLabel, option.Subtitle);

            if (costLabel != null) {
                costLabel.gameObject.SetActive(option.Cost > 0 && !done);
                costLabel.text = MathHelper.AddCommas((ulong)Math.Max(0L, option.Cost));
                costLabel.color = option.State == CommandOptionState.Unaffordable ? unaffordableCostColor : costColor;
            }
            if (timeLabel != null) {
                timeLabel.gameObject.SetActive(option.Seconds > 0f && !done);
                timeLabel.text = WorldTimerBar.FormatCountdown(option.Seconds);
            }

            bool showState = !string.IsNullOrEmpty(option.StateText);
            if (stateBand != null)
                stateBand.SetActive(showState);
            SetText(stateLabel, option.StateText);

            if (progressFill != null) {
                bool showProgress = option.Progress >= 0f;
                (progressRoot != null ? progressRoot : progressFill.gameObject).SetActive(showProgress);
                if (showProgress)
                    progressFill.fillAmount = Mathf.Clamp01(option.Progress);
            }

            if (countBadge != null)
                countBadge.SetActive(option.Count > 0);
            SetText(countLabel, option.Count > 0 ? option.Count.ToString() : string.Empty);

            if (background != null)
                background.color = locked ? lockedBackground : done ? doneBackground : availableBackground;
            // Locked, running, and finished options cannot be ordered. Unaffordable ones stay
            // clickable so the server's refusal - and the reason - reaches the player.
            if (button != null)
                button.interactable = option.State is CommandOptionState.Available or CommandOptionState.Unaffordable;

            if (hover != null)
                hover.SetText(BuildTooltip(option));
        }

        static string BuildTooltip(CommandOption option) {
            string tooltip = option.Title;
            if (!string.IsNullOrEmpty(option.Description))
                tooltip += "\n" + option.Description;
            if (!string.IsNullOrEmpty(option.StateText))
                tooltip += "\n" + option.StateText;
            return tooltip;
        }

        static void SetText(TextMeshProUGUI label, string value) {
            if (label == null)
                return;
            label.text = value ?? string.Empty;
            label.gameObject.SetActive(!string.IsNullOrEmpty(value));
        }
    }
}
