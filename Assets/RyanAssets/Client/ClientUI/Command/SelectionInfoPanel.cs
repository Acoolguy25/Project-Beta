using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RyanAssets.Client.ClientUI.Command {
    /// <summary>
    /// The details card for whatever the player has selected: name, owner, health, current activity,
    /// a list of figures, a work queue, and the actions that apply to it.
    /// <para>
    /// Everything here is presentation. A game mode fills the panel from its own rules each time the
    /// selection or its state changes, and receives <see cref="ActionRequested"/> with the action's
    /// id. Actions marked with a <see cref="CommandAction.ConfirmLabel"/> only fire on a second click
    /// within <see cref="confirmWindowSeconds"/>, so demolishing a building takes intent.
    /// </para>
    /// </summary>
    public sealed class SelectionInfoPanel : MonoBehaviour {
        [Header("Header")]
        [SerializeField] TextMeshProUGUI titleLabel;
        [SerializeField] TextMeshProUGUI subtitleLabel;
        [SerializeField] Image icon;
        [SerializeField] Image ownerAccent;

        [Header("Health")]
        [SerializeField] GameObject healthRoot;
        [SerializeField] Image healthFill;
        [SerializeField] TextMeshProUGUI healthLabel;
        [SerializeField] Color healthyColor = new(0.35f, 0.85f, 0.4f, 1f);
        [SerializeField] Color woundedColor = new(0.95f, 0.75f, 0.2f, 1f);
        [SerializeField] Color criticalColor = new(0.85f, 0.25f, 0.2f, 1f);

        [Header("Status")]
        [SerializeField] GameObject statusRoot;
        [SerializeField] TextMeshProUGUI statusLabel;
        [SerializeField] Image statusFill;
        [Tooltip("The status bar's track, hidden with the fill. Leave unset to toggle the fill alone.")]
        [SerializeField] GameObject statusBar;

        [Header("Stats")]
        [SerializeField] CommandStatRow statRowPrefab;
        [SerializeField] RectTransform statRoot;

        [Header("Actions")]
        [SerializeField] CommandActionButton actionButtonPrefab;
        [SerializeField] RectTransform actionRoot;
        [Tooltip("Seconds a confirmed action waits for its second click before it resets.")]
        [SerializeField, Min(0.5f)] float confirmWindowSeconds = 3f;

        [Header("Queue")]
        [Tooltip("Optional work queue shown for selections that have one.")]
        [SerializeField] CommandQueueStrip queue;

        UIPool<CommandStatRow> statRows;
        UIPool<CommandActionButton> actionButtons;
        readonly List<CommandAction> actions = new();

        int? awaitingConfirmId;
        float confirmExpiresAt;

        UIPool<CommandStatRow> StatRows => statRows ??= new UIPool<CommandStatRow>(statRowPrefab, statRoot);
        UIPool<CommandActionButton> ActionButtons =>
            actionButtons ??= new UIPool<CommandActionButton>(actionButtonPrefab, actionRoot);

        /// <summary>Raised with an action's id once it has been clicked, and confirmed if it needs confirming.</summary>
        public event Action<int> ActionRequested;

        public CommandQueueStrip Queue => queue;

        public bool IsOpen => gameObject.activeSelf;

        /// <summary>Critical through wounded to healthy, so a failing building reads before its number does.</summary>
        Color GetHealthColor(float fraction) =>
            fraction < 0.5f
                ? Color.Lerp(criticalColor, woundedColor, fraction / 0.5f)
                : Color.Lerp(woundedColor, healthyColor, (fraction - 0.5f) / 0.5f);

        void Update() {
            // An unanswered confirmation lapses back to the plain label, so a half-finished
            // demolish does not sit armed waiting for a click meant for something else.
            if (awaitingConfirmId.HasValue && Time.unscaledTime > confirmExpiresAt) {
                awaitingConfirmId = null;
                RebindActions();
            }
        }

        public void Show(SelectionHeader header) {
            if (!gameObject.activeSelf)
                gameObject.SetActive(true);

            if (titleLabel != null)
                titleLabel.text = header.Title ?? string.Empty;
            if (subtitleLabel != null) {
                subtitleLabel.text = header.Subtitle ?? string.Empty;
                subtitleLabel.gameObject.SetActive(!string.IsNullOrEmpty(header.Subtitle));
            }
            if (icon != null) {
                icon.sprite = header.Icon;
                icon.enabled = header.Icon != null;
            }
            if (ownerAccent != null) {
                ownerAccent.enabled = header.Accent.a > 0.01f;
                ownerAccent.color = header.Accent;
            }

            bool hasHealth = header.MaxHealth > 0;
            if (healthRoot != null)
                healthRoot.SetActive(hasHealth);
            if (hasHealth) {
                float fraction = Mathf.Clamp01(header.Health / (float)header.MaxHealth);
                if (healthFill != null) {
                    healthFill.fillAmount = fraction;
                    healthFill.color = GetHealthColor(fraction);
                }
                if (healthLabel != null)
                    healthLabel.text = $"{Math.Max(0L, header.Health)} / {header.MaxHealth}";
            }

            bool hasStatus = !string.IsNullOrEmpty(header.Status);
            if (statusRoot != null)
                statusRoot.SetActive(hasStatus);
            if (statusLabel != null)
                statusLabel.text = header.Status ?? string.Empty;
            if (statusFill != null) {
                bool showFill = hasStatus && header.StatusProgress >= 0f;
                (statusBar != null ? statusBar : statusFill.gameObject).SetActive(showFill);
                if (showFill)
                    statusFill.fillAmount = Mathf.Clamp01(header.StatusProgress);
            }
        }

        public void Hide() {
            awaitingConfirmId = null;
            if (gameObject.activeSelf)
                gameObject.SetActive(false);
        }

        public void SetStats(IReadOnlyList<CommandStat> stats) {
            int count = stats?.Count ?? 0;
            if (!StatRows.Resize(count)) {
                Debug.LogError($"{name} has no stat row prefab or root assigned and cannot draw its stats.", this);
                return;
            }
            for (int i = 0; i < count; i++)
                StatRows[i].Bind(stats[i]);
        }

        public void SetActions(IReadOnlyList<CommandAction> newActions) {
            actions.Clear();
            if (newActions != null)
                actions.AddRange(newActions);

            // A pending confirmation only survives while its action is still offered and usable.
            if (awaitingConfirmId.HasValue && !IsOfferedAndInteractable(awaitingConfirmId.Value))
                awaitingConfirmId = null;
            RebindActions();
        }

        bool IsOfferedAndInteractable(int id) {
            foreach (CommandAction action in actions) {
                if (action.Id == id)
                    return action.Interactable;
            }
            return false;
        }

        void RebindActions() {
            if (!ActionButtons.Resize(actions.Count)) {
                Debug.LogError($"{name} has no action button prefab or root assigned and cannot draw its actions.", this);
                return;
            }
            for (int i = 0; i < actions.Count; i++) {
                CommandAction action = actions[i];
                ActionButtons[i].Bind(action, awaitingConfirmId == action.Id, HandleActionClicked);
            }
        }

        void HandleActionClicked(CommandActionButton button) {
            CommandAction action = button.Action;
            if (!action.Interactable)
                return;

            if (!string.IsNullOrEmpty(action.ConfirmLabel) && awaitingConfirmId != action.Id) {
                awaitingConfirmId = action.Id;
                confirmExpiresAt = Time.unscaledTime + confirmWindowSeconds;
                RebindActions();
                return;
            }

            awaitingConfirmId = null;
            RebindActions();
            ActionRequested?.Invoke(action.Id);
        }
    }
}
