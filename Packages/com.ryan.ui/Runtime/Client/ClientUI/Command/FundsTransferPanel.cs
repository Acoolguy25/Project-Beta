using System;
using System.Collections.Generic;
using RyanAssets.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RyanAssets.Client.ClientUI.Command {
    /// <summary>
    /// A panel for giving funds to another player: pick who, pick how much from a preset or type it,
    /// and send.
    /// <para>
    /// Like the rest of the command UI it knows no game rules. The mode supplies the eligible
    /// recipients and the sender's balance, and receives <see cref="TransferRequested"/>; the server
    /// validates the transfer again regardless. The panel only refuses what is certain to fail - no
    /// recipient, nothing to send, more than the balance - and says why, so a Send that does nothing
    /// never looks like a broken button.
    /// </para>
    /// </summary>
    public sealed class FundsTransferPanel : MonoBehaviour {
        [SerializeField] TextMeshProUGUI titleLabel;
        [SerializeField] TextMeshProUGUI balanceLabel;

        [Header("Recipients")]
        [SerializeField] FundsRecipientRow recipientPrefab;
        [SerializeField] RectTransform recipientRoot;
        [Tooltip("Shown instead of the list while nobody can receive a transfer.")]
        [SerializeField] TextMeshProUGUI emptyLabel;

        [Header("Amount")]
        [Tooltip("One-click amounts, paired by index with presetButtons.")]
        [SerializeField] long[] presetAmounts = { 100, 250, 500, 1000 };
        [SerializeField] Button[] presetButtons = Array.Empty<Button>();
        [Tooltip("Fills in the whole balance.")]
        [SerializeField] Button maxButton;
        [SerializeField] TMP_InputField amountInput;

        [Header("Actions")]
        [SerializeField] Button sendButton;
        [SerializeField] TextMeshProUGUI sendLabel;
        [SerializeField] Button closeButton;
        [Tooltip("Why Send is unavailable, or what was just sent.")]
        [SerializeField] TextMeshProUGUI statusLabel;

        UIPool<FundsRecipientRow> rows;
        readonly List<TransferRecipient> recipients = new();
        int selectedId;
        bool hasSelection;
        long balance;
        bool unlimited;

        UIPool<FundsRecipientRow> Rows => rows ??= new UIPool<FundsRecipientRow>(recipientPrefab, recipientRoot);

        /// <summary>Raised with the recipient's id and the amount when the player sends.</summary>
        public event Action<int, long> TransferRequested;

        /// <summary>Raised when the player closes the panel.</summary>
        public event Action CloseRequested;

        public bool IsOpen => gameObject.activeSelf;

        void Awake() {
            for (int i = 0; i < presetButtons.Length; i++) {
                if (presetButtons[i] == null || i >= presetAmounts.Length)
                    continue;
                long amount = presetAmounts[i];
                TextMeshProUGUI caption = presetButtons[i].GetComponentInChildren<TextMeshProUGUI>(true);
                if (caption != null)
                    caption.text = MathHelper.AddCommas((ulong)Math.Max(0, amount));
                presetButtons[i].onClick.AddListener(() => SetAmount(amount));
            }
            if (maxButton != null)
                maxButton.onClick.AddListener(HandleMax);
            if (amountInput != null) {
                amountInput.contentType = TMP_InputField.ContentType.IntegerNumber;
                amountInput.onValueChanged.AddListener(HandleAmountChanged);
                amountInput.onSubmit.AddListener(HandleSubmit);
            }
            if (sendButton != null)
                sendButton.onClick.AddListener(Send);
            if (closeButton != null)
                closeButton.onClick.AddListener(HandleClose);
        }

        void OnDestroy() {
            foreach (Button button in presetButtons) {
                if (button != null)
                    button.onClick.RemoveAllListeners();
            }
            if (maxButton != null)
                maxButton.onClick.RemoveListener(HandleMax);
            if (amountInput != null) {
                amountInput.onValueChanged.RemoveListener(HandleAmountChanged);
                amountInput.onSubmit.RemoveListener(HandleSubmit);
            }
            if (sendButton != null)
                sendButton.onClick.RemoveListener(Send);
            if (closeButton != null)
                closeButton.onClick.RemoveListener(HandleClose);
        }

        public void Open(string title) {
            if (titleLabel != null)
                titleLabel.text = title ?? string.Empty;
            if (!gameObject.activeSelf)
                gameObject.SetActive(true);
            SetStatus(null);
            Redraw();
        }

        public void Close() {
            if (gameObject.activeSelf)
                gameObject.SetActive(false);
        }

        /// <summary>
        /// Replaces the list of eligible recipients. The current choice survives when that recipient
        /// is still listed; with a single recipient, they are chosen outright.
        /// </summary>
        public void SetRecipients(IReadOnlyList<TransferRecipient> eligible) {
            recipients.Clear();
            bool selectionListed = false;
            if (eligible != null) {
                foreach (TransferRecipient recipient in eligible) {
                    recipients.Add(recipient);
                    selectionListed |= hasSelection && recipient.Id == selectedId;
                }
            }

            hasSelection = selectionListed;
            if (!hasSelection && recipients.Count == 1) {
                hasSelection = true;
                selectedId = recipients[0].Id;
            }
            Redraw();
        }

        /// <summary>The sender's balance, which caps what may be sent unless funds are unlimited.</summary>
        public void SetBalance(long funds, bool isUnlimited) {
            balance = Math.Max(0, funds);
            unlimited = isUnlimited;
            if (balanceLabel != null)
                balanceLabel.text = unlimited ? "Unlimited" : MathHelper.AddCommas((ulong)balance);
            RefreshSend();
        }

        /// <summary>Puts a line under the Send button: the reason nothing was sent, or confirmation that it was.</summary>
        public void SetStatus(string message) {
            if (statusLabel == null)
                return;
            statusLabel.text = message ?? string.Empty;
            statusLabel.gameObject.SetActive(!string.IsNullOrEmpty(message));
        }

        public void SetAmount(long amount) {
            if (amountInput != null)
                amountInput.text = amount > 0 ? amount.ToString() : string.Empty;
            RefreshSend();
        }

        long Amount =>
            amountInput != null && long.TryParse(amountInput.text, out long amount) ? amount : 0;

        void HandleMax() => SetAmount(balance);

        void HandleAmountChanged(string text) => RefreshSend();

        void HandleSubmit(string text) => Send();

        void HandleClose() => CloseRequested?.Invoke();

        void HandleRecipientClicked(int id) {
            hasSelection = true;
            selectedId = id;
            Redraw();
        }

        void Redraw() {
            if (!Rows.Resize(recipients.Count)) {
                Debug.LogError($"{name} has no recipient prefab or root assigned and cannot list recipients.", this);
                return;
            }
            for (int i = 0; i < recipients.Count; i++)
                Rows[i].Bind(recipients[i], hasSelection && recipients[i].Id == selectedId, HandleRecipientClicked);
            if (emptyLabel != null)
                emptyLabel.gameObject.SetActive(recipients.Count == 0);
            RefreshSend();
        }

        bool TryGetSelected(out TransferRecipient selected) {
            foreach (TransferRecipient recipient in recipients) {
                if (hasSelection && recipient.Id == selectedId) {
                    selected = recipient;
                    return true;
                }
            }
            selected = default;
            return false;
        }

        /// <summary>The reason a send would certainly fail, or null when it can go.</summary>
        string GetRefusal(long amount) {
            if (!TryGetSelected(out _))
                return recipients.Count == 0 ? "Nobody to send to" : "Choose who to send to";
            if (amount <= 0)
                return "Choose an amount";
            if (!unlimited && amount > balance)
                return "More than you have";
            return null;
        }

        void RefreshSend() {
            long amount = Amount;
            bool canSend = GetRefusal(amount) == null;
            if (sendButton != null)
                sendButton.interactable = canSend;
            if (sendLabel != null)
                sendLabel.text = canSend && TryGetSelected(out TransferRecipient recipient)
                    ? $"Send {MathHelper.AddCommas((ulong)amount)} to {recipient.Name}"
                    : "Send";
        }

        void Send() {
            long amount = Amount;
            string refusal = GetRefusal(amount);
            if (refusal != null) {
                SetStatus(refusal);
                return;
            }

            TryGetSelected(out TransferRecipient recipient);
            TransferRequested?.Invoke(recipient.Id, amount);
            SetStatus($"Sending {MathHelper.AddCommas((ulong)amount)} to {recipient.Name}");
            SetAmount(0);
        }
    }
}
