using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RyanAssets.Client.ClientUI.Command {
    /// <summary>
    /// A titled panel of <see cref="CommandOptionCard"/>s: the build menu of a barracks, the research
    /// menu of a lab, or any other "pick something to order" list a universe needs.
    /// <para>
    /// The panel owns layout and drawing only. It raises <see cref="OptionClicked"/> with the
    /// option's <see cref="CommandOption.Id"/> and leaves every rule - who may order, what it costs,
    /// whether the server accepts - to the game mode, which re-supplies the options whenever its own
    /// state changes. Cards are pooled and rebound in place, so redrawing on every change of funds
    /// or progress costs no allocation and keeps the hovered card under the cursor.
    /// </para>
    /// </summary>
    public sealed class CommandOptionGrid : MonoBehaviour {
        [Header("Authored Parts")]
        [Tooltip("Card cloned once per option. Its layout and colours come from the prefab.")]
        [SerializeField] CommandOptionCard cardPrefab;
        [Tooltip("Parent the cards are laid out under. Carries the grid layout.")]
        [SerializeField] RectTransform cardRoot;
        [SerializeField] TextMeshProUGUI titleLabel;
        [SerializeField] TextMeshProUGUI subtitleLabel;
        [Tooltip("Shown instead of the cards when there is nothing to offer.")]
        [SerializeField] TextMeshProUGUI emptyLabel;
        [SerializeField] Button closeButton;

        UIPool<CommandOptionCard> cards;

        // Created on first use rather than in Awake: the panel is authored inactive, and a mode may
        // supply options before the first time it is shown.
        UIPool<CommandOptionCard> Cards => cards ??= new UIPool<CommandOptionCard>(cardPrefab, cardRoot);

        /// <summary>Raised with the clicked option's id.</summary>
        public event Action<int> OptionClicked;

        /// <summary>Raised when the player closes the panel with its own button.</summary>
        public event Action CloseRequested;

        public bool IsOpen => gameObject.activeSelf;

        void Awake() {
            if (closeButton != null)
                closeButton.onClick.AddListener(HandleClose);
        }

        void OnDestroy() {
            if (closeButton != null)
                closeButton.onClick.RemoveListener(HandleClose);
        }

        void HandleClose() => CloseRequested?.Invoke();

        void HandleCardClicked(CommandOptionCard card) {
            if (card.Option != null)
                OptionClicked?.Invoke(card.Option.Id);
        }

        /// <summary>Opens the panel with a heading. Options are supplied separately and can be refreshed freely.</summary>
        public void Open(string title, string subtitle) {
            if (titleLabel != null)
                titleLabel.text = title ?? string.Empty;
            if (subtitleLabel != null) {
                subtitleLabel.text = subtitle ?? string.Empty;
                subtitleLabel.gameObject.SetActive(!string.IsNullOrEmpty(subtitle));
            }
            if (!gameObject.activeSelf)
                gameObject.SetActive(true);
        }

        public void Close() {
            if (gameObject.activeSelf)
                gameObject.SetActive(false);
        }

        /// <summary>Redraws the cards. Safe to call every frame; unchanged cards are simply rebound.</summary>
        public void SetOptions(IReadOnlyList<CommandOption> options, string emptyText = null) {
            int count = options?.Count ?? 0;
            if (!Cards.Resize(count)) {
                Debug.LogError($"{name} has no card prefab or card root assigned and cannot draw its options.", this);
                return;
            }

            for (int i = 0; i < count; i++)
                Cards[i].Bind(options[i], HandleCardClicked);

            if (emptyLabel != null) {
                emptyLabel.gameObject.SetActive(count == 0);
                emptyLabel.text = emptyText ?? string.Empty;
            }
        }
    }
}
