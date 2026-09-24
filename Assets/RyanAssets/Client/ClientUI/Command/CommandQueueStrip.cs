using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace RyanAssets.Client.ClientUI.Command {
    /// <summary>
    /// A building's work queue drawn as a row of slots, the first one filling as it progresses.
    /// <para>
    /// Cancelling is reported by slot index through <see cref="CancelRequested"/>; whether the local
    /// player may cancel is decided by the game mode and arrives on each entry, and the server
    /// checks it again regardless.
    /// </para>
    /// </summary>
    public sealed class CommandQueueStrip : MonoBehaviour {
        [SerializeField] CommandQueueSlot slotPrefab;
        [SerializeField] RectTransform slotRoot;
        [SerializeField] TextMeshProUGUI headerLabel;
        [Tooltip("Shown instead of the slots while the queue is empty.")]
        [SerializeField] TextMeshProUGUI emptyLabel;

        UIPool<CommandQueueSlot> slots;

        UIPool<CommandQueueSlot> Slots => slots ??= new UIPool<CommandQueueSlot>(slotPrefab, slotRoot);

        /// <summary>Raised with the index of the entry the player asked to cancel.</summary>
        public event Action<int> CancelRequested;

        void HandleCancel(int index) => CancelRequested?.Invoke(index);

        public void SetEntries(IReadOnlyList<CommandQueueEntry> entries, string header, string emptyText) {
            int count = entries?.Count ?? 0;
            if (!Slots.Resize(count)) {
                Debug.LogError($"{name} has no slot prefab or slot root assigned and cannot draw its queue.", this);
                return;
            }

            for (int i = 0; i < count; i++)
                Slots[i].Bind(entries[i], i, HandleCancel);

            if (headerLabel != null)
                headerLabel.text = header ?? string.Empty;
            if (emptyLabel != null) {
                emptyLabel.gameObject.SetActive(count == 0);
                emptyLabel.text = emptyText ?? string.Empty;
            }
        }

        public void SetVisible(bool visible) {
            if (gameObject.activeSelf != visible)
                gameObject.SetActive(visible);
        }
    }
}
