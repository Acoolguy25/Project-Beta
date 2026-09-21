using System.Collections.Generic;
using FishNet;
using FishNet.Object;
using RyanAssets.Input;
using RyanAssets.Shared.Declarations;
using UnityEngine;
using UnityEngine.InputSystem;
using Universes.UniverseData.war_valley.Shared;

namespace Universes.UniverseData.war_valley.Client {
    /// <summary>
    /// War Valley's commander input: box selection, movement and attack orders, rally points, and the
    /// production panel.
    /// <para>
    /// Selection is deliberately local. Nothing about which units are highlighted crosses the wire -
    /// only the resulting order does, naming the units it applies to, and the server re-checks that
    /// the sender actually commands each one.
    /// </para>
    /// </summary>
    public sealed class WV_ClientController : MonoBehaviour {
        /// <summary>Pixels of travel before a click becomes a box drag.</summary>
        const float DragThresholdPixels = 8f;

        [Header("Authored References")]
        [Tooltip("The War Valley HUD prefab. Instantiated once and bound to live match state.")]
        [SerializeField] WV_HUD hudPrefab;

        [Header("Selection")]
        [Tooltip("Maximum units a single box selection can pick up.")]
        [SerializeField, Min(1)] int maxSelection = 120;

        readonly List<WV_Unit> selection = new();
        readonly List<WV_Unit> ownedScratch = new();
        readonly List<int> orderScratch = new();

        WV_HUD hud;
        WV_ProductionBuilding selectedBuilding;
        Vector2 dragStart;
        bool dragging;
        bool pointerDown;
        bool economyDirty = true;

        int LocalClientId =>
            InstanceFinder.ClientManager != null && InstanceFinder.ClientManager.Connection.IsValid
                ? InstanceFinder.ClientManager.Connection.ClientId
                : WV_Owned.NoOwner;

        void Awake() {
            if (hudPrefab == null) {
                Debug.LogError(
                    $"{nameof(WV_ClientController)} has no HUD prefab assigned; War Valley cannot show " +
                    "funds, selection, or production.", this);
                enabled = false;
                return;
            }

            hud = Instantiate(hudPrefab);
            hud.name = "War Valley HUD";
            hud.SetHint("Drag to select • Right click to move • Right click an enemy to attack");
        }

        void OnEnable() {
            WV_Economy.LedgerChanged += MarkEconomyDirty;
            WV_Unit.RosterChanged += HandleRosterChanged;
            WV_ProductionBuilding.QueueChanged += HandleQueueChanged;
        }

        void OnDisable() {
            WV_Economy.LedgerChanged -= MarkEconomyDirty;
            WV_Unit.RosterChanged -= HandleRosterChanged;
            WV_ProductionBuilding.QueueChanged -= HandleQueueChanged;
        }

        void OnDestroy() {
            if (hud != null)
                Destroy(hud.gameObject);
        }

        void MarkEconomyDirty() => economyDirty = true;

        void HandleRosterChanged() {
            PruneSelection();
            hud.RefreshSelection(selection);
        }

        void HandleQueueChanged() {
            if (selectedBuilding != null)
                hud.RefreshProductionQueue(selectedBuilding);
        }

        void Update() {
            if (economyDirty) {
                economyDirty = false;
                hud.RefreshEconomy(LocalClientId);
                hud.ProductionMenu?.RefreshAffordability();
            }

            if (selectedBuilding != null)
                hud.RefreshProductionQueue(selectedBuilding);

            HandlePointer();
        }

        // --- Pointer ----------------------------------------------------------

        void HandlePointer() {
            Mouse mouse = Mouse.current;
            Camera camera = Camera.main;
            if (mouse == null || camera == null || !Application.isFocused) {
                CancelDrag();
                return;
            }

            // While the structure menu is placing a building, left click belongs to placement.
            bool inputBlocked = TopbarControls.IsMenuOpen || !ToolControls.IsCursorFree();
            Vector2 screenPosition = mouse.position.ReadValue();

            if (mouse.rightButton.wasPressedThisFrame && !inputBlocked)
                IssueContextOrder(camera, screenPosition);

            if (mouse.leftButton.wasPressedThisFrame && !inputBlocked) {
                pointerDown = true;
                dragStart = screenPosition;
            }

            if (pointerDown && !dragging
                && (screenPosition - dragStart).sqrMagnitude >= DragThresholdPixels * DragThresholdPixels) {
                dragging = true;
                hud.SetSelectionBoxVisible(true);
            }

            if (dragging)
                hud.UpdateSelectionBox(dragStart, screenPosition);

            if (!mouse.leftButton.wasReleasedThisFrame || !pointerDown)
                return;

            if (dragging)
                SelectInBox(camera, dragStart, screenPosition, AdditiveHeld());
            else
                SelectAtPoint(camera, screenPosition, AdditiveHeld());

            CancelDrag();
        }

        static bool AdditiveHeld() =>
            Keyboard.current != null
            && (Keyboard.current.leftShiftKey.isPressed || Keyboard.current.rightShiftKey.isPressed);

        static bool AttackMoveHeld() =>
            Keyboard.current != null
            && (Keyboard.current.leftCtrlKey.isPressed || Keyboard.current.rightCtrlKey.isPressed);

        void CancelDrag() {
            pointerDown = false;
            dragging = false;
            hud.SetSelectionBoxVisible(false);
        }

        // --- Selection --------------------------------------------------------

        void SelectInBox(Camera camera, Vector2 start, Vector2 end, bool additive) {
            if (!additive)
                ClearSelection();

            Vector2 min = Vector2.Min(start, end);
            Vector2 max = Vector2.Max(start, end);

            WV_Unit.CollectOwnedBy(LocalClientId, ownedScratch);
            foreach (WV_Unit unit in ownedScratch) {
                if (selection.Count >= maxSelection)
                    break;

                Vector3 viewport = camera.WorldToViewportPoint(unit.transform.position);
                // Anything behind the camera projects to a mirrored point in front of it, so the
                // depth test has to come before the rectangle test.
                if (viewport.z <= 0f)
                    continue;

                Vector2 screen = camera.WorldToScreenPoint(unit.transform.position);
                if (screen.x >= min.x && screen.x <= max.x && screen.y >= min.y && screen.y <= max.y)
                    AddToSelection(unit);
            }

            if (selection.Count > 0)
                SelectBuilding(null);
            hud.RefreshSelection(selection);
        }

        void SelectAtPoint(Camera camera, Vector2 screenPosition, bool additive) {
            Ray ray = camera.ScreenPointToRay(screenPosition);
            bool hitSomething = Physics.Raycast(
                ray, out RaycastHit hit, float.MaxValue, WV_Combat.TargetMask, QueryTriggerInteraction.Ignore);

            if (!hitSomething) {
                if (!additive) {
                    ClearSelection();
                    SelectBuilding(null);
                    hud.RefreshSelection(selection);
                }
                return;
            }

            WV_Unit unit = hit.collider.GetComponentInParent<WV_Unit>();
            if (unit != null && unit.Owned != null && unit.Owned.IsOwnedBy(LocalClientId)) {
                if (!additive)
                    ClearSelection();
                AddToSelection(unit);
                SelectBuilding(null);
                hud.RefreshSelection(selection);
                return;
            }

            WV_ProductionBuilding building = hit.collider.GetComponentInParent<WV_ProductionBuilding>();
            if (building != null && building.Owned != null && building.Owned.IsOwnedBy(LocalClientId)) {
                ClearSelection();
                hud.RefreshSelection(selection);
                SelectBuilding(building);
                return;
            }

            if (!additive) {
                ClearSelection();
                SelectBuilding(null);
                hud.RefreshSelection(selection);
            }
        }

        void AddToSelection(WV_Unit unit) {
            if (unit == null || unit.IsDead || selection.Contains(unit))
                return;
            unit.SelectedLocally = true;
            selection.Add(unit);
        }

        void ClearSelection() {
            foreach (WV_Unit unit in selection) {
                if (unit != null)
                    unit.SelectedLocally = false;
            }
            selection.Clear();
        }

        /// <summary>Drops units that despawned or died while selected.</summary>
        void PruneSelection() {
            for (int i = selection.Count - 1; i >= 0; i--) {
                WV_Unit unit = selection[i];
                if (unit == null || unit.IsDead || !unit.IsSpawned)
                    selection.RemoveAt(i);
            }

            if (selectedBuilding != null && (!selectedBuilding.IsSpawned || selectedBuilding.Owned == null
                || !selectedBuilding.Owned.IsOwnedBy(LocalClientId)))
                SelectBuilding(null);
        }

        void SelectBuilding(WV_ProductionBuilding building) {
            selectedBuilding = building;
            hud.ShowProduction(building, LocalClientId);
        }

        // --- Orders -----------------------------------------------------------

        void IssueContextOrder(Camera camera, Vector2 screenPosition) {
            Ray ray = camera.ScreenPointToRay(screenPosition);

            // An enemy under the cursor turns the click into an attack order; otherwise it is a
            // move, which also means a click on empty ground never silently does nothing.
            if (Physics.Raycast(ray, out RaycastHit entityHit, float.MaxValue, WV_Combat.TargetMask,
                    QueryTriggerInteraction.Ignore)) {
                IEntity target = entityHit.collider.GetComponentInParent<IEntity>();
                if (target != null && IsHostileToLocalPlayer(target)) {
                    SendOrder(WV_OrderType.Attack, Vector3.zero, ((Component)target).GetComponent<NetworkObject>());
                    return;
                }
            }

            if (!Physics.Raycast(ray, out RaycastHit groundHit, float.MaxValue, WV_Rules.OrderGroundMask,
                    QueryTriggerInteraction.Ignore))
                return;

            if (selectedBuilding != null && selection.Count == 0) {
                InstanceFinder.ClientManager.Broadcast(new WV_RallyPointRequest {
                    buildingObjectId = selectedBuilding.NetworkObject.ObjectId,
                    position = groundHit.point
                });
                hud.SetHint("Rally point set");
                return;
            }

            SendOrder(AttackMoveHeld() ? WV_OrderType.AttackMove : WV_OrderType.Move, groundHit.point, null);
        }

        bool IsHostileToLocalPlayer(IEntity target) {
            // Compare against a unit the player actually commands rather than against the player's own
            // character, so the answer matches what the ordered units will do on the server.
            foreach (WV_Unit unit in selection) {
                if (unit != null)
                    return WV_Combat.AreEnemies(unit.Team, target.Team);
            }
            return false;
        }

        void SendOrder(WV_OrderType orderType, Vector3 position, NetworkObject target) {
            if (selection.Count == 0 || InstanceFinder.ClientManager == null)
                return;

            orderScratch.Clear();
            foreach (WV_Unit unit in selection) {
                if (unit != null && unit.IsSpawned && !unit.IsDead)
                    orderScratch.Add(unit.NetworkObject.ObjectId);
            }
            if (orderScratch.Count == 0)
                return;

            InstanceFinder.ClientManager.Broadcast(new WV_UnitOrderRequest {
                unitObjectIds = orderScratch.ToArray(),
                orderType = (byte)orderType,
                position = position,
                targetObjectId = target != null ? target.ObjectId : 0
            });

            hud.SetHint(orderType switch {
                WV_OrderType.Attack => "Attacking",
                WV_OrderType.AttackMove => "Advancing under fire",
                _ => "Moving out"
            });
        }
    }
}
