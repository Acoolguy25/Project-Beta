using System;
using RyanAssets.UI.Hover;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RyanAssets.Client.ClientUI.Command {
    /// <summary>One action button on a <see cref="SelectionInfoPanel"/>.</summary>
    public sealed class CommandActionButton : MonoBehaviour {
        [SerializeField] Button button;
        [SerializeField] Image background;
        [SerializeField] TextMeshProUGUI label;
        [SerializeField] HoverItem hover;

        [Header("Colours")]
        [SerializeField] Color normalColor = new(0.1f, 0.49f, 0.75f, 1f);
        [SerializeField] Color destructiveColor = new(0.66f, 0.2f, 0.18f, 1f);
        [SerializeField] Color confirmColor = new(0.9f, 0.35f, 0.1f, 1f);

        Action<CommandActionButton> clicked;

        public CommandAction Action { get; private set; }

        void Awake() {
            if (button != null)
                button.onClick.AddListener(HandleClick);
        }

        void OnDestroy() {
            if (button != null)
                button.onClick.RemoveListener(HandleClick);
        }

        void HandleClick() => clicked?.Invoke(this);

        /// <param name="awaitingConfirm">True while the first click of a confirmed action is waiting for the second.</param>
        public void Bind(CommandAction action, bool awaitingConfirm, Action<CommandActionButton> onClick) {
            Action = action;
            clicked = onClick;

            if (label != null)
                label.text = awaitingConfirm && !string.IsNullOrEmpty(action.ConfirmLabel) ? action.ConfirmLabel : action.Label;
            if (background != null)
                background.color = awaitingConfirm ? confirmColor : action.Destructive ? destructiveColor : normalColor;
            if (button != null)
                button.interactable = action.Interactable;
            if (hover != null)
                hover.SetText(action.Tooltip ?? string.Empty);
        }
    }
}
