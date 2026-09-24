using System;
using System.Collections.Generic;
using FishNet;
using RyanAssets.Core;
using RyanAssets.UI.ButtonGrid;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Universes.UniverseData.war_valley.Shared;

namespace Universes.UniverseData.war_valley.Client {
    /// <summary>One icon authored for a troop, which has no prefab of its own to carry one.</summary>
    [Serializable]
    public sealed class WV_TroopIcon {
        public WV_TroopKind kind = WV_TroopKind.Knife;
        public Sprite icon;
    }

    /// <summary>
    /// One row in the build menu: everything the card needs to draw itself, flattened out of either
    /// a <see cref="WV_Unit"/> prefab or a <see cref="WV_TroopKind"/>.
    /// <para>
    /// The two are priced from different places - a vehicle from its own prefab, a troop from
    /// <see cref="WV_Rules"/> - and the grid should not have to know which it is holding, so they
    /// are resolved into this once when the building is bound.
    /// </para>
    /// </summary>
    public sealed class WV_ProductionOption {
        public WV_ProductionItem Item;
        public string Name;
        public string Category;
        public string Description;
        public int Cost;
        public float BuildSeconds;
        public Sprite Icon;
    }

    /// <summary>
    /// The build grid shown for a selected production building.
    /// <para>
    /// Built on the shared <see cref="ButtonGridUI{T}"/> that already backs the structure menu, so the
    /// row prefab, scrolling, and click plumbing are the project's existing ones rather than a second
    /// bespoke list.
    /// </para>
    /// <para>
    /// A barracks lists foot soldiers in this same grid. There is deliberately no separate troop
    /// panel any more: a troop is queued, timed, paid for, and cancelled exactly like a tank, and
    /// giving it its own parallel interface only hid that.
    /// </para>
    /// </summary>
    public sealed class WV_ProductionMenu : ButtonGridUI<WV_ProductionOption> {
        [Header("Row Layout")]
        // Defaults match the shared StructureItemCard prefab this grid reuses as its row:
        // 1 Image, 2 CategoryText, 3 ItemNameText, 4 DescriptionText, 5 CostText.
        [SerializeField] int iconChildIndex = 1;
        [SerializeField] int categoryChildIndex = 2;
        [SerializeField] int nameChildIndex = 3;
        [SerializeField] int durationChildIndex = 4;
        [SerializeField] int costChildIndex = 5;

        [Header("Troop Icons")]
        [Tooltip("Icons for foot soldiers. A troop has no prefab to carry one, so it is authored " +
                 "here. A kind left out simply draws no icon.")]
        [SerializeField] WV_TroopIcon[] troopIcons = Array.Empty<WV_TroopIcon>();

        [Header("Affordability")]
        [SerializeField] Color affordableColor = new(0.85f, 0.92f, 1f);
        [SerializeField] Color unaffordableColor = new(1f, 0.45f, 0.45f);

        readonly List<WV_ProductionOption> options = new();

        WV_ProductionBuilding boundBuilding;
        int localClientId = WV_Owned.NoOwner;

        public WV_ProductionBuilding BoundBuilding => boundBuilding;

        /// <summary>Points the grid at a building, or clears it when passed null.</summary>
        public void Bind(WV_ProductionBuilding building, int clientId) {
            localClientId = clientId;
            if (boundBuilding == building) {
                RefreshAffordability();
                return;
            }

            boundBuilding = building;
            options.Clear();
            if (building == null) {
                ClearPrefabs();
                return;
            }

            foreach (WV_Unit unit in building.ProducibleUnits) {
                if (unit == null)
                    continue;
                options.Add(new WV_ProductionOption {
                    Item = WV_ProductionItem.Unit(unit.Kind),
                    Name = WV_Rules.GetUnitDisplayName(unit.Kind),
                    Category = unit.IsAircraft ? "Air" : "Ground",
                    Description = string.Empty,
                    Cost = unit.Cost,
                    BuildSeconds = unit.BuildSeconds,
                    Icon = unit.Icon
                });
            }

            foreach (WV_TroopKind kind in building.ProducibleTroops) {
                options.Add(new WV_ProductionOption {
                    Item = WV_ProductionItem.Troop(kind),
                    Name = WV_Rules.GetTroopDisplayName(kind),
                    Category = "Infantry",
                    Description = WV_Rules.GetTroopDescription(kind),
                    Cost = WV_Rules.GetTroopCost(kind),
                    BuildSeconds = WV_Rules.GetTroopBuildSeconds(kind),
                    Icon = FindTroopIcon(kind)
                });
            }

            RefreshPrefabs(options.ToArray());
        }

        Sprite FindTroopIcon(WV_TroopKind kind) {
            foreach (WV_TroopIcon entry in troopIcons) {
                if (entry != null && entry.kind == kind)
                    return entry.icon;
            }
            return null;
        }

        protected override string GetItemName(WV_ProductionOption data) => data != null ? data.Name : "Unit";

        void OnAddRow(GameObject row, WV_ProductionOption option) {
            SetImage(row, iconChildIndex, option.Icon);
            SetText(row, categoryChildIndex, option.Category);
            SetText(row, nameChildIndex, option.Name);
            SetText(row, durationChildIndex, $"Builds in {WV_Rules.FormatCountdown(option.BuildSeconds)}");
            SetText(row, costChildIndex, MathHelper.AddCommas((ulong)Mathf.Max(0, option.Cost)));
            ApplyAffordability(row, option);
        }

        void OnClickRow(GameObject row, WV_ProductionOption option) {
            if (boundBuilding == null || option == null || InstanceFinder.ClientManager == null)
                return;

            // The server re-checks funds, ownership, queue length, and the squad cap; this is only a
            // fast local reject so an unaffordable click does not round-trip.
            WV_Economy economy = WV_Economy.Instance;
            if (economy != null && !economy.CanAfford(localClientId, option.Cost))
                return;

            InstanceFinder.ClientManager.Broadcast(new WV_ProductionRequest {
                buildingObjectId = boundBuilding.NetworkObject.ObjectId,
                unitKind = option.Item.Encoded,
                cancel = false
            });
        }

        /// <summary>Greys out anything the player cannot currently pay for.</summary>
        public void RefreshAffordability() {
            if (boundBuilding == null || contentTarget == null)
                return;

            foreach (Transform row in contentTarget) {
                WV_ProductionOption option = FindOptionByRowName(row.name);
                if (option != null)
                    ApplyAffordability(row.gameObject, option);
            }
        }

        WV_ProductionOption FindOptionByRowName(string rowName) {
            foreach (WV_ProductionOption option in options) {
                if (option.Name == rowName)
                    return option;
            }
            return null;
        }

        void ApplyAffordability(GameObject row, WV_ProductionOption option) {
            WV_Economy economy = WV_Economy.Instance;
            bool affordable = economy == null || economy.CanAfford(localClientId, option.Cost);
            if (row.transform.childCount > costChildIndex
                && row.transform.GetChild(costChildIndex).TryGetComponent(out TextMeshProUGUI costText))
                costText.color = affordable ? affordableColor : unaffordableColor;
        }

        static void SetText(GameObject row, int childIndex, string value) {
            if (row.transform.childCount > childIndex
                && row.transform.GetChild(childIndex).TryGetComponent(out TextMeshProUGUI text))
                text.text = value;
        }

        static void SetImage(GameObject row, int childIndex, Sprite sprite) {
            if (row.transform.childCount <= childIndex
                || !row.transform.GetChild(childIndex).TryGetComponent(out Image image))
                return;
            // Rows reuse the structure icon convention: a sprite authored on the asset. The row
            // prefab ships with no sprite, so an Image left enabled without one draws a plain white
            // box over the card.
            image.sprite = sprite;
            image.enabled = sprite != null;
        }

        protected override void Awake() {
            base.Awake();
            OnCreatePrefab += OnAddRow;
            OnClickPrefab += OnClickRow;
        }

        protected override void OnDestroy() {
            OnCreatePrefab -= OnAddRow;
            OnClickPrefab -= OnClickRow;
            base.OnDestroy();
        }
    }
}
