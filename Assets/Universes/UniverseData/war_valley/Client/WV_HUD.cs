using System;
using System.Collections.Generic;
using System.Text;
using RyanAssets.Characters.Shared;
using RyanAssets.Client.ClientUI.Command;
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
    /// The building panel and the build/research menu are the shared command UI from RyanAssets,
    /// nested into the prefab: this HUD hands them to <see cref="WV_StructureInspector"/>, which fills
    /// them from War Valley's rules.
    /// </para>
    /// </summary>
    public sealed class WV_HUD : MonoBehaviour {
        [Header("Economy")]
        [SerializeField] TextMeshProUGUI fundsLabel;
        [SerializeField] TextMeshProUGUI incomeLabel;
        [Tooltip("One line summarising the side's research in progress.")]
        [SerializeField] TextMeshProUGUI researchLabel;

        [Header("Selection")]
        [SerializeField] RectTransform selectionBox;
        [SerializeField] TextMeshProUGUI selectionLabel;
        [SerializeField] GameObject selectionPanel;

        [Header("Structures")]
        [Tooltip("Details, queue, and actions for the selected building.")]
        [SerializeField] SelectionInfoPanel structurePanel;
        [Tooltip("The build and research menu opened by right-clicking a building.")]
        [SerializeField] CommandOptionGrid optionMenu;
        [Tooltip("Icons for foot soldiers, which have no prefab of their own to carry one.")]
        [SerializeField] WV_TroopIcon[] troopIcons = Array.Empty<WV_TroopIcon>();
        [Tooltip("Gap kept between the building panel and the build menu stacked above it.")]
        [SerializeField, Min(0f)] float dockSpacing = 12f;

        [Header("Commands")]
        [Tooltip("The command card: order and selection buttons. Stays open so every order is " +
                 "reachable without a shortcut.")]
        [SerializeField] GameObject commandPanel;
        [SerializeField] WV_CommandMenu commandMenu;

        [Header("Hints")]
        [SerializeField] TextMeshProUGUI hintLabel;

        readonly StringBuilder builder = new();
        readonly Dictionary<string, int> troopCounts = new();
        readonly Dictionary<WV_UnitKind, int> unitCounts = new();

        RectTransform structureRect;
        RectTransform optionRect;
        float optionRestingY;

        public WV_CommandMenu CommandMenu => commandMenu;
        public SelectionInfoPanel StructurePanel => structurePanel;
        public CommandOptionGrid OptionMenu => optionMenu;

        void Awake() {
            if (structurePanel != null)
                structureRect = (RectTransform)structurePanel.transform;
            if (optionMenu != null) {
                optionRect = (RectTransform)optionMenu.transform;
                optionRestingY = optionRect.anchoredPosition.y;
            }

            SetSelectionBoxVisible(false);
            if (selectionPanel != null)
                selectionPanel.SetActive(false);
            if (structurePanel != null)
                structurePanel.Hide();
            if (optionMenu != null)
                optionMenu.Close();
            if (researchLabel != null)
                researchLabel.gameObject.SetActive(false);
        }

        /// <summary>
        /// Keeps the build menu docked on top of the building panel. Both are authored on the same
        /// bottom-right corner; the panel grows with its contents, so the menu is lifted clear of
        /// whatever height the panel has this frame rather than by a fixed offset that one day
        /// overlaps it.
        /// </summary>
        void LateUpdate() {
            if (optionRect == null || !optionMenu.IsOpen)
                return;

            float y = structureRect != null && structurePanel.IsOpen
                ? structureRect.anchoredPosition.y + structureRect.rect.height + dockSpacing
                : optionRestingY;
            Vector2 position = optionRect.anchoredPosition;
            if (!Mathf.Approximately(position.y, y))
                optionRect.anchoredPosition = new Vector2(position.x, y);
        }

        public void SetHint(string text) {
            if (hintLabel != null)
                hintLabel.text = text;
        }

        public Sprite GetTroopIcon(WV_TroopKind kind) {
            foreach (WV_TroopIcon entry in troopIcons) {
                if (entry != null && entry.kind == kind)
                    return entry.icon;
            }
            return null;
        }

        public void RefreshEconomy(int clientId) {
            WV_Economy economy = WV_Economy.Instance;
            long funds = economy != null ? economy.GetFunds(clientId) : 0;
            int income = economy != null ? economy.GetIncomePerMinute(clientId) : 0;

            if (fundsLabel != null)
                fundsLabel.text = economy != null && economy.HasInfiniteFunds
                    ? "Unlimited"
                    : MathHelper.AddCommas((ulong)Mathf.Max(0, funds));
            if (incomeLabel != null)
                incomeLabel.text = income > 0 ? $"+{income}/min" : "No income";
        }

        /// <summary>Shows the research line, or hides it when nothing is being researched.</summary>
        public void SetResearchSummary(string summary) {
            if (researchLabel == null)
                return;
            bool show = !string.IsNullOrEmpty(summary);
            if (researchLabel.gameObject.activeSelf != show)
                researchLabel.gameObject.SetActive(show);
            if (show)
                researchLabel.text = summary;
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
            unitCounts.Clear();
            long health = 0;
            long maxHealth = 0;
            for (int i = 0; i < unitCount; i++) {
                WV_Unit unit = selection[i];
                if (unit == null)
                    continue;
                unitCounts.TryGetValue(unit.Kind, out int existing);
                unitCounts[unit.Kind] = existing + 1;
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
            builder.Append("<b>").Append(total).Append(" selected</b>");
            foreach (KeyValuePair<WV_UnitKind, int> entry in unitCounts)
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
    }
}
