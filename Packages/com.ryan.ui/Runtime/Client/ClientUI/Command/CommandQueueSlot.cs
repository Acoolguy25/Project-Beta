using System;
using RyanAssets.UI.Hover;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RyanAssets.Client.ClientUI.Command {
    /// <summary>One entry in a <see cref="CommandQueueStrip"/>: what is queued, how far along it is, and its cancel button.</summary>
    public sealed class CommandQueueSlot : MonoBehaviour {
        [SerializeField] Image icon;
        [SerializeField] TextMeshProUGUI titleLabel;
        [Tooltip("Filled image drawn over the slot for the entry in progress.")]
        [SerializeField] Image progressFill;
        [SerializeField] TextMeshProUGUI timeLabel;
        [Tooltip("Strip in the colour of whoever the entry belongs to.")]
        [SerializeField] Image ownerAccent;
        [SerializeField] Button cancelButton;
        [SerializeField] HoverItem hover;

        Action<int> cancelled;
        int index;

        void Awake() {
            if (cancelButton != null)
                cancelButton.onClick.AddListener(HandleCancel);
        }

        void OnDestroy() {
            if (cancelButton != null)
                cancelButton.onClick.RemoveListener(HandleCancel);
        }

        void HandleCancel() => cancelled?.Invoke(index);

        public void Bind(CommandQueueEntry entry, int slotIndex, Action<int> onCancel) {
            index = slotIndex;
            cancelled = onCancel;

            if (icon != null) {
                icon.sprite = entry.Icon;
                icon.enabled = entry.Icon != null;
            }
            if (titleLabel != null)
                titleLabel.text = entry.Title ?? string.Empty;

            bool inProgress = entry.Progress >= 0f;
            if (progressFill != null) {
                progressFill.gameObject.SetActive(inProgress);
                if (inProgress)
                    progressFill.fillAmount = Mathf.Clamp01(entry.Progress);
            }
            if (timeLabel != null) {
                timeLabel.gameObject.SetActive(!string.IsNullOrEmpty(entry.TimeText));
                timeLabel.text = entry.TimeText ?? string.Empty;
            }
            if (ownerAccent != null) {
                ownerAccent.enabled = entry.Accent.a > 0.01f;
                ownerAccent.color = entry.Accent;
            }
            // Shown but disabled rather than hidden, so a player can see the entry is cancellable in
            // principle and the tooltip can say whose permission it takes.
            if (cancelButton != null)
                cancelButton.interactable = entry.CanCancel;
            if (hover != null)
                hover.SetText(entry.Tooltip ?? entry.Title);
        }
    }
}
