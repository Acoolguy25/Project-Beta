using System.Collections.Generic;
using System.Text;
using RyanAssets.Characters.Shared;
using RyanAssets.Core;
using TMPro;
using UnityEngine;
using Universes.UniverseData.war_valley.Shared;

namespace Universes.UniverseData.war_valley.Client {
    /// <summary>
    /// Binds live match state to the authored War Valley HUD prefab.
    /// <para>
    /// Every label, panel, and the selection rectangle itself are authored in the prefab and wired in
    /// the Inspector; this component only writes text and toggles what the prefab already contains.
    /// </para>
    /// </summary>
    public sealed class WV_HUD : MonoBehaviour {
        [Header("Economy")]
        [SerializeField] TextMeshProUGUI fundsLabel;
        [SerializeField] TextMeshProUGUI incomeLabel;

        [Header("Selection")]
        [SerializeField] RectTransform selectionBox;
        [SerializeField] TextMeshProUGUI selectionLabel;
        [SerializeField] GameObject selectionPanel;

        [Header("Production")]
        [SerializeField] GameObject productionPanel;
        [SerializeField] TextMeshProUGUI productionTitle;
        [SerializeField] TextMeshProUGUI productionQueueLabel;
        [SerializeField] WV_ProductionMenu productionMenu;

        [Header("Commands")]
        [Tooltip("The command card: order and selection buttons. Stays open so every order is " +
                 "reachable without a shortcut.")]
        [SerializeField] GameObject commandPanel;
        [SerializeField] WV_CommandMenu commandMenu;

        [Header("Hints")]
        [SerializeField] TextMeshProUGUI hintLabel;

        readonly StringBuilder builder = new();
        readonly Dictionary<string, int> troopCounts = new();

        public WV_ProductionMenu ProductionMenu => productionMenu;
        public WV_CommandMenu CommandMenu => commandMenu;

        void Awake() {
            SetSelectionBoxVisible(false);
            if (selectionPanel != null)
                selectionPanel.SetActive(false);
            if (productionPanel != null)
                productionPanel.SetActive(false);
        }

        public void SetHint(string text) {
            if (hintLabel != null)
                hintLabel.text = text;
        }

        public void RefreshEconomy(int clientId) {
            WV_Economy economy = WV_Economy.Instance;
            long funds = economy != null ? economy.GetFunds(clientId) : 0;
            int income = economy != null ? economy.GetIncomePerMinute(clientId) : 0;

            if (fundsLabel != null)
                fundsLabel.text = MathHelper.AddCommas((ulong)Mathf.Max(0, funds));
            if (incomeLabel != null)
                incomeLabel.text = income > 0 ? $"+{income}/min" : "No income";
        }

        // --- Selection rectangle ---------------------------------------------

        public void SetSelectionBoxVisible(bool visible) {
            if (selectionBox != null)
                selectionBox.gameObject.SetActive(visible);
        }

        /// <summary>Stretches the authored rectangle across the drag, in screen space.</summary>
        public void UpdateSelectionBox(Vector2 screenStart, Vector2 screenEnd) {
            if (selectionBox == null)
                return;

            Vector2 min = Vector2.Min(screenStart, screenEnd);
            Vector2 max = Vector2.Max(screenStart, screenEnd);

            // The prefab anchors the box to the bottom-left so screen pixels map straight onto
            // anchoredPosition without a canvas-space conversion per frame.
            selectionBox.anchoredPosition = min / SelectionCanvasScale;
            selectionBox.sizeDelta = (max - min) / SelectionCanvasScale;
        }

        float SelectionCanvasScale {
            get {
                Canvas canvas = selectionBox != null ? selectionBox.GetComponentInParent<Canvas>() : null;
                return canvas != null && canvas.scaleFactor > 0f ? canvas.scaleFactor : 1f;
            }
        }

        // --- Selection summary -----------------------------------------------

        public void RefreshSelection(IReadOnlyList<WV_Unit> selection, IReadOnlyList<GameCharacter> troops) {
            int unitCount = selection?.Count ?? 0;
            int troopCount = troops?.Count ?? 0;
            int total = unitCount + troopCount;
            if (selectionPanel != null)
                selectionPanel.SetActive(total > 0);
            if (commandMenu != null)
                commandMenu.SetHasSelection(total > 0);
            if (total == 0 || selectionLabel == null)
                return;

            // Counted by kind rather than listed, so a thirty-unit push stays readable.
            var counts = new Dictionary<WV_UnitKind, int>();
            long health = 0;
            long maxHealth = 0;
            for (int i = 0; i < unitCount; i++) {
                WV_Unit unit = selection[i];
                if (unit == null)
                    continue;
                counts.TryGetValue(unit.Kind, out int existing);
                counts[unit.Kind] = existing + 1;
                health += unit.Health.Value;
                maxHealth += unit.MaxHealth.Value;
            }

            // A troop's loadout is not replicated as a field of its own. The server names the
            // character after it, which is the same string its name tag already shows.
            troopCounts.Clear();
            for (int i = 0; i < troopCount; i++) {
                GameCharacter troop = troops[i];
                if (troop == null)
                    continue;
                troopCounts.TryGetValue(troop.DisplayName, out int existing);
                troopCounts[troop.DisplayName] = existing + 1;
                health += troop.Health.Value;
                maxHealth += troop.MaxHealth.Value;
            }

            builder.Clear();
            builder.Append(total).Append(" selected");
            foreach (KeyValuePair<WV_UnitKind, int> entry in counts)
                builder.Append("\n").Append(entry.Value).Append("x ").Append(WV_Rules.GetUnitDisplayName(entry.Key));
            foreach (KeyValuePair<string, int> entry in troopCounts)
                builder.Append("\n").Append(entry.Value).Append("x ").Append(entry.Key);
            if (maxHealth > 0)
                builder.Append("\nHealth ").Append(health).Append(" / ").Append(maxHealth);
            selectionLabel.text = builder.ToString();
        }

        // --- Command card -----------------------------------------------------

        /// <summary>Opens the command card. It is never hidden: an empty card still teaches the keys.</summary>
        public void ShowCommands() {
            if (commandPanel != null)
                commandPanel.SetActive(true);
        }

        /// <summary>Highlights the order waiting for a click, or clears it when passed null.</summary>
        public void SetArmedOrder(WV_OrderType? armed) {
            if (commandMenu != null)
                commandMenu.SetArmed(armed);
        }

        // --- Production panel -------------------------------------------------

        public void ShowProduction(WV_ProductionBuilding building, int clientId) {
            if (productionPanel == null)
                return;

            bool show = building != null;
            productionPanel.SetActive(show);
            if (!show) {
                productionMenu?.Bind(null, clientId);
                return;
            }

            if (productionTitle != null)
                productionTitle.text = building.name;
            productionMenu?.Bind(building, clientId);
            RefreshProductionQueue(building);
        }

        public void RefreshProductionQueue(WV_ProductionBuilding building) {
            if (productionQueueLabel == null || building == null)
                return;

            if (building.QueueLength == 0) {
                productionQueueLabel.text = "Queue empty";
                return;
            }

            builder.Clear();
            builder.Append("Building ")
                .Append(building.GetQueuedItem(0).DisplayName)
                .Append(" - ")
                .Append(WV_Rules.FormatCountdown(building.CurrentItemSecondsRemaining));
            if (building.QueueLength > 1)
                builder.Append("\n+").Append(building.QueueLength - 1).Append(" queued");
            productionQueueLabel.text = builder.ToString();
        }
    }
}
