using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Universes.UniverseData.war_valley.Shared;

namespace Universes.UniverseData.war_valley.Client {
    /// <summary>
    /// The commander's command card: every order and every way of selecting a squad, as buttons.
    /// <para>
    /// War Valley's orders used to be reachable only through the mouse and a handful of keys, which
    /// made the mode close to unplayable without knowing them in advance - and put orders on the
    /// right button, which the camera already owns. Everything here is therefore a button first and
    /// a shortcut second: the keys still work, but nothing is only a key.
    /// </para>
    /// <para>
    /// This component raises events and draws state. It deliberately holds no selection and sends no
    /// broadcast of its own; <see cref="WV_ClientController"/> owns both, so there is exactly one
    /// place that decides what an order means.
    /// </para>
    /// </summary>
    public sealed class WV_CommandMenu : MonoBehaviour {
        [Header("Order Buttons")]
        [SerializeField] Button moveButton;
        [SerializeField] Button attackMoveButton;
        [SerializeField] Button attackButton;
        [SerializeField] Button stopButton;
        [SerializeField] Button holdButton;

        [Header("Selection Buttons")]
        [SerializeField] Button selectUnitsButton;
        [SerializeField] Button selectTroopsButton;
        [SerializeField] Button selectAllButton;
        [Tooltip("Control group buttons, in order. Click recalls; the matching F-key still works.")]
        [SerializeField] Button[] groupButtons = Array.Empty<Button>();
        [Tooltip("Set-group buttons, in order, paired with groupButtons by index.")]
        [SerializeField] Button[] setGroupButtons = Array.Empty<Button>();

        [Header("Feedback")]
        [Tooltip("Tint applied to the order button currently waiting for a click.")]
        [SerializeField] Color armedTint = new(0.100f, 0.494f, 0.745f, 1f);
        [SerializeField] TextMeshProUGUI statusText;

        /// <summary>An order that needs a destination, so the next world click places it.</summary>
        public event Action<WV_OrderType> OrderArmed;

        /// <summary>Selects every unit, every troop, or both.</summary>
        public event Action<bool, bool> SelectRequested;

        /// <summary>Recalls the control group at this index.</summary>
        public event Action<int> GroupRecalled;

        /// <summary>Binds the current selection to the control group at this index.</summary>
        public event Action<int> GroupBound;

        Color[] orderButtonBaseColors;
        Button[] orderButtons;

        void Awake() {
            orderButtons = new[] { moveButton, attackMoveButton, attackButton, stopButton, holdButton };
            orderButtonBaseColors = new Color[orderButtons.Length];
            for (int i = 0; i < orderButtons.Length; i++) {
                orderButtonBaseColors[i] = orderButtons[i] != null && orderButtons[i].targetGraphic != null
                    ? orderButtons[i].targetGraphic.color
                    : Color.white;
            }

            Bind(moveButton, WV_OrderType.Move);
            Bind(attackMoveButton, WV_OrderType.AttackMove);
            Bind(attackButton, WV_OrderType.Attack);
            Bind(stopButton, WV_OrderType.Stop);
            Bind(holdButton, WV_OrderType.HoldPosition);

            if (selectUnitsButton != null)
                selectUnitsButton.onClick.AddListener(() => SelectRequested?.Invoke(true, false));
            if (selectTroopsButton != null)
                selectTroopsButton.onClick.AddListener(() => SelectRequested?.Invoke(false, true));
            if (selectAllButton != null)
                selectAllButton.onClick.AddListener(() => SelectRequested?.Invoke(true, true));

            for (int i = 0; i < groupButtons.Length; i++) {
                if (groupButtons[i] == null)
                    continue;
                int index = i;
                groupButtons[i].onClick.AddListener(() => GroupRecalled?.Invoke(index));
            }
            for (int i = 0; i < setGroupButtons.Length; i++) {
                if (setGroupButtons[i] == null)
                    continue;
                int index = i;
                setGroupButtons[i].onClick.AddListener(() => GroupBound?.Invoke(index));
            }

            SetArmed(null);
        }

        void Bind(Button button, WV_OrderType orderType) {
            if (button != null)
                button.onClick.AddListener(() => OrderArmed?.Invoke(orderType));
        }

        void OnDestroy() {
            foreach (Button button in orderButtons) {
                if (button != null)
                    button.onClick.RemoveAllListeners();
            }
            if (selectUnitsButton != null) selectUnitsButton.onClick.RemoveAllListeners();
            if (selectTroopsButton != null) selectTroopsButton.onClick.RemoveAllListeners();
            if (selectAllButton != null) selectAllButton.onClick.RemoveAllListeners();
            foreach (Button button in groupButtons) {
                if (button != null)
                    button.onClick.RemoveAllListeners();
            }
            foreach (Button button in setGroupButtons) {
                if (button != null)
                    button.onClick.RemoveAllListeners();
            }
        }

        /// <summary>
        /// Highlights the order waiting for a click, or clears the highlight when passed null. The
        /// armed order is the one piece of state the card shows that a player cannot otherwise see,
        /// since the cursor itself does not change.
        /// </summary>
        public void SetArmed(WV_OrderType? armed) {
            for (int i = 0; i < orderButtons.Length; i++) {
                Button button = orderButtons[i];
                if (button == null || button.targetGraphic == null)
                    continue;
                bool isArmed = armed.HasValue && OrderOf(i) == armed.Value;
                button.targetGraphic.color = isArmed ? armedTint : orderButtonBaseColors[i];
            }

            if (statusText == null)
                return;
            statusText.text = armed switch {
                WV_OrderType.Move => "Click a destination",
                WV_OrderType.AttackMove => "Click where to advance",
                WV_OrderType.Attack => "Click an enemy",
                _ => "M move · V attack-move · T attack · X stop · H hold"
            };
        }

        static WV_OrderType OrderOf(int index) => index switch {
            0 => WV_OrderType.Move,
            1 => WV_OrderType.AttackMove,
            2 => WV_OrderType.Attack,
            3 => WV_OrderType.Stop,
            _ => WV_OrderType.HoldPosition
        };

        /// <summary>Greys the order buttons out while nothing is selected to give them to.</summary>
        public void SetHasSelection(bool hasSelection) {
            foreach (Button button in orderButtons) {
                if (button != null)
                    button.interactable = hasSelection;
            }
            foreach (Button button in setGroupButtons) {
                if (button != null)
                    button.interactable = hasSelection;
            }
        }
    }
}
