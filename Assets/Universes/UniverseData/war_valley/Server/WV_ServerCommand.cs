using FishNet;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Transporting;
using RyanAssets.Shared.Declarations;
using UnityEngine;
using Universes.UniverseData.war_valley.Shared;

namespace Universes.UniverseData.war_valley.Server {
    /// <summary>
    /// Authoritative receiver for the client's RTS input: group orders, production, and rally points.
    /// <para>
    /// Selection itself never crosses the wire - a client decides locally which of its own units are
    /// highlighted and then names them in an order. Every request here is therefore re-validated
    /// against the sender's ownership rather than trusted, so a modified client cannot drive another
    /// player's army or spend their funds.
    /// </para>
    /// </summary>
    public static class WV_ServerCommand {
        /// <summary>Upper bound on a single order, so one broadcast cannot walk the whole spawned set.</summary>
        const int MaxUnitsPerOrder = 256;

        static bool registered;

        public static void Register() {
            if (registered || InstanceFinder.ServerManager == null)
                return;

            InstanceFinder.ServerManager.RegisterBroadcast<WV_UnitOrderRequest>(OnUnitOrder, true);
            InstanceFinder.ServerManager.RegisterBroadcast<WV_ProductionRequest>(OnProduction, true);
            InstanceFinder.ServerManager.RegisterBroadcast<WV_RallyPointRequest>(OnRallyPoint, true);
            registered = true;
        }

        public static void Unregister() {
            if (!registered || InstanceFinder.ServerManager == null) {
                registered = false;
                return;
            }

            InstanceFinder.ServerManager.UnregisterBroadcast<WV_UnitOrderRequest>(OnUnitOrder);
            InstanceFinder.ServerManager.UnregisterBroadcast<WV_ProductionRequest>(OnProduction);
            InstanceFinder.ServerManager.UnregisterBroadcast<WV_RallyPointRequest>(OnRallyPoint);
            registered = false;
        }

        static void OnUnitOrder(NetworkConnection sender, WV_UnitOrderRequest request, Channel channel) {
            if (sender == null || !sender.IsValid || request.unitObjectIds == null)
                return;

            var orderType = (WV_OrderType)request.orderType;
            if (!IsFinite(request.position) && orderType is WV_OrderType.Move or WV_OrderType.AttackMove)
                return;

            IEntity attackTarget = null;
            if (orderType == WV_OrderType.Attack) {
                if (!TryGetSpawned(request.targetObjectId, out NetworkObject targetObject))
                    return;
                attackTarget = targetObject.GetComponent<IEntity>();
                if (attackTarget == null)
                    return;
            }

            int count = Mathf.Min(request.unitObjectIds.Length, MaxUnitsPerOrder);
            // Counts only the units actually commanded, so the formation stays packed even when
            // some ids in the request are rejected.
            int commanded = 0;
            for (int i = 0; i < count; i++) {
                if (!TryGetOwnedBrain(sender, request.unitObjectIds[i], out WV_UnitBrain brain))
                    continue;

                switch (orderType) {
                    case WV_OrderType.Move:
                        brain.OrderMove(WV_Rules.GetGroupDestination(request.position, commanded, count));
                        break;
                    case WV_OrderType.AttackMove:
                        brain.OrderAttackMove(WV_Rules.GetGroupDestination(request.position, commanded, count));
                        break;
                    case WV_OrderType.Attack:
                        brain.OrderAttack(attackTarget);
                        break;
                    case WV_OrderType.HoldPosition:
                        brain.OrderHoldPosition();
                        break;
                    default:
                        brain.OrderStop();
                        break;
                }
                commanded++;
            }
        }

        static void OnProduction(NetworkConnection sender, WV_ProductionRequest request, Channel channel) {
            if (sender == null
                || !sender.IsValid
                || !TryGetOwnedComponent(sender, request.buildingObjectId, out WV_ProductionBuilding building))
                return;

            WV_Economy economy = WV_Economy.Instance;
            if (economy == null)
                return;

            var kind = (WV_UnitKind)request.unitKind;

            if (request.cancel) {
                if (building.TryCancel(kind, out int refund))
                    economy.Credit(sender.ClientId, refund);
                return;
            }

            WV_Unit prefab = building.FindPrefab(kind);
            if (prefab == null || building.QueueLength >= building.MaxQueueLength)
                return;

            // Charge first, then queue. A failed enqueue hands the money straight back rather than
            // leaving the player short for a unit they never got.
            if (!economy.TryDebit(sender.ClientId, prefab.Cost))
                return;
            if (!building.TryEnqueue(kind))
                economy.Credit(sender.ClientId, prefab.Cost);
        }

        static void OnRallyPoint(NetworkConnection sender, WV_RallyPointRequest request, Channel channel) {
            if (sender == null
                || !sender.IsValid
                || !IsFinite(request.position)
                || !TryGetOwnedComponent(sender, request.buildingObjectId, out WV_ProductionBuilding building))
                return;

            building.SetRallyPoint(request.position);
        }

        static bool TryGetOwnedBrain(NetworkConnection sender, int objectId, out WV_UnitBrain brain) {
            brain = null;
            if (!TryGetSpawned(objectId, out NetworkObject networkObject))
                return false;

            WV_Owned owned = networkObject.GetComponent<WV_Owned>();
            if (owned == null || !owned.IsOwnedBy(sender.ClientId))
                return false;

            brain = networkObject.GetComponent<WV_UnitBrain>();
            return brain != null;
        }

        static bool TryGetOwnedComponent<T>(NetworkConnection sender, int objectId, out T component)
            where T : Component {
            component = null;
            if (!TryGetSpawned(objectId, out NetworkObject networkObject))
                return false;

            WV_Owned owned = networkObject.GetComponent<WV_Owned>();
            if (owned == null || !owned.IsOwnedBy(sender.ClientId))
                return false;

            return networkObject.TryGetComponent(out component);
        }

        static bool TryGetSpawned(int objectId, out NetworkObject networkObject) {
            networkObject = null;
            return objectId != 0
                && InstanceFinder.ServerManager != null
                && InstanceFinder.ServerManager.Objects.Spawned.TryGetValue(objectId, out networkObject)
                && networkObject != null;
        }

        static bool IsFinite(Vector3 position) =>
            float.IsFinite(position.x) && float.IsFinite(position.y) && float.IsFinite(position.z);
    }
}
