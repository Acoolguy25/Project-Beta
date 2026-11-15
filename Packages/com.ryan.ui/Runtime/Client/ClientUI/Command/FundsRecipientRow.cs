using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RyanAssets.Client.ClientUI.Command {
    /// <summary>One selectable recipient in a <see cref="FundsTransferPanel"/>.</summary>
    public sealed class FundsRecipientRow : MonoBehaviour {
        [SerializeField] Button button;
        [SerializeField] Image background;
        [Tooltip("Swatch in the recipient's colour.")]
        [SerializeField] Image accent;
        [SerializeField] TextMeshProUGUI nameLabel;
        [SerializeField] TextMeshProUGUI detailLabel;

        [Header("Colours")]
        [SerializeField] Color normalColor = new(0.13f, 0.16f, 0.2f, 1f);
        [SerializeField] Color selectedColor = new(0.1f, 0.49f, 0.75f, 1f);

        Action<int> clicked;
        int recipientId;

        void Awake() {
            if (button != null)
                button.onClick.AddListener(HandleClick);
        }

        void OnDestroy() {
            if (button != null)
                button.onClick.RemoveListener(HandleClick);
        }

        void HandleClick() => clicked?.Invoke(recipientId);

        public void Bind(TransferRecipient recipient, bool selected, Action<int> onClick) {
            recipientId = recipient.Id;
            clicked = onClick;

            if (nameLabel != null)
                nameLabel.text = recipient.Name ?? string.Empty;
            if (detailLabel != null) {
                detailLabel.text = recipient.Detail ?? string.Empty;
                detailLabel.gameObject.SetActive(!string.IsNullOrEmpty(recipient.Detail));
            }
            if (accent != null)
                accent.color = recipient.Accent;
            if (background != null)
                background.color = selected ? selectedColor : normalColor;
        }
    }
}
