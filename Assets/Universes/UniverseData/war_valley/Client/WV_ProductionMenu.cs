using System.Linq;
using FishNet;
using RyanAssets.Core;
using RyanAssets.UI.ButtonGrid;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Universes.UniverseData.war_valley.Shared;

namespace Universes.UniverseData.war_valley.Client {
    /// <summary>
    /// The train-a-unit grid shown for a selected production building.
    /// <para>
    /// Built on the shared <see cref="ButtonGridUI{T}"/> that already backs the structure menu, so the
    /// row prefab, scrolling, and click plumbing are the project's existing ones rather than a second
    /// bespoke list.
    /// </para>
    /// </summary>
    public sealed class WV_ProductionMenu : ButtonGridUI<WV_Unit> {
        [Header("Row Layout")]
        // Defaults match the shared StructureItemCard prefab this grid reuses as its row:
        // 1 Image, 2 CategoryText, 3 ItemNameText, 4 DescriptionText, 5 CostText.
        [SerializeField] int iconChildIndex = 1;
        [SerializeField] int categoryChildIndex = 2;
        [SerializeField] int nameChildIndex = 3;
        [SerializeField] int durationChildIndex = 4;
        [SerializeField] int costChildIndex = 5;

        [Header("Affordability")]
        [SerializeField] Color affordableColor = new(0.85f, 0.92f, 1f);
        [SerializeField] Color unaffordableColor = new(1f, 0.45f, 0.45f);

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
            if (building == null) {
                ClearPrefabs();
                return;
            }

            RefreshPrefabs(building.ProducibleUnits.Where(unit => unit != null).ToArray());
        }

        protected override string GetItemName(WV_Unit data) =>
            data != null ? WV_Rules.GetUnitDisplayName(data.Kind) : "Unit";

        void OnAddRow(GameObject row, WV_Unit unit) {
            SetImage(row, iconChildIndex, unit);
            SetText(row, categoryChildIndex, unit.IsAircraft ? "Air" : "Ground");
            SetText(row, nameChildIndex, WV_Rules.GetUnitDisplayName(unit.Kind));
            SetText(row, durationChildIndex, $"Builds in {WV_Rules.FormatCountdown(unit.BuildSeconds)}");
            SetText(row, costChildIndex, MathHelper.AddCommas((ulong)Mathf.Max(0, unit.Cost)));
            ApplyAffordability(row, unit);
        }

        void OnClickRow(GameObject row, WV_Unit unit) {
            if (boundBuilding == null || unit == null || InstanceFinder.ClientManager == null)
                return;

            // The server re-checks funds, ownership, and queue length; this is only a fast local
            // reject so an unaffordable click does not round-trip.
            WV_Economy economy = WV_Economy.Instance;
            if (economy != null && !economy.CanAfford(localClientId, unit.Cost))
                return;

            InstanceFinder.ClientManager.Broadcast(new WV_ProductionRequest {
                buildingObjectId = boundBuilding.NetworkObject.ObjectId,
                unitKind = (byte)unit.Kind,
                cancel = false
            });
        }

        /// <summary>Greys out anything the player cannot currently pay for.</summary>
        public void RefreshAffordability() {
            if (boundBuilding == null || contentTarget == null)
                return;

            foreach (Transform row in contentTarget) {
                WV_Unit unit = FindUnitByRowName(row.name);
                if (unit != null)
                    ApplyAffordability(row.gameObject, unit);
            }
        }

        WV_Unit FindUnitByRowName(string rowName) {
            foreach (WV_Unit unit in boundBuilding.ProducibleUnits) {
                if (unit != null && WV_Rules.GetUnitDisplayName(unit.Kind) == rowName)
                    return unit;
            }
            return null;
        }

        void ApplyAffordability(GameObject row, WV_Unit unit) {
            WV_Economy economy = WV_Economy.Instance;
            bool affordable = economy == null || economy.CanAfford(localClientId, unit.Cost);
            if (row.transform.childCount > costChildIndex
                && row.transform.GetChild(costChildIndex).TryGetComponent(out TextMeshProUGUI costText))
                costText.color = affordable ? affordableColor : unaffordableColor;
        }

        static void SetText(GameObject row, int childIndex, string value) {
            if (row.transform.childCount > childIndex
                && row.transform.GetChild(childIndex).TryGetComponent(out TextMeshProUGUI text))
                text.text = value;
        }

        static void SetImage(GameObject row, int childIndex, WV_Unit unit) {
            if (row.transform.childCount <= childIndex
                || !row.transform.GetChild(childIndex).TryGetComponent(out Image image))
                return;
            // Units reuse the structure icon convention: a sprite authored on the prefab. The row
            // prefab ships with no sprite, so the icon has to come from the unit, and an Image left
            // enabled without one draws a plain white box over the card.
            image.sprite = unit.Icon;
            image.enabled = unit.Icon != null;
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
