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
                if (!TryGetOwnedCommandable(sender, request.unitObjectIds[i], out WV_ICommandable commandable))
                    continue;

                switch (orderType) {
                    case WV_OrderType.Move:
                        commandable.OrderMove(WV_Rules.GetGroupDestination(request.position, commanded, count));
                        break;
                    case WV_OrderType.AttackMove:
                        commandable.OrderAttackMove(WV_Rules.GetGroupDestination(request.position, commanded, count));
                        break;
                    case WV_OrderType.Attack:
                        commandable.OrderAttack(attackTarget);
                        break;
                    case WV_OrderType.HoldPosition:
                        commandable.OrderHoldPosition();
                        break;
                    default:
                        commandable.OrderStop();
                        break;
                }
                commanded++;
            }
        }

        /// <summary>
        /// Queues or cancels one item at a building the sender owns.
        /// <para>
        /// A barracks queues foot soldiers through exactly this path, so the cost, the refund, and
        /// the cap all live here rather than in a second training route. Every outcome is answered:
        /// a button that silently does nothing is indistinguishable from a broken one.
        /// </para>
        /// </summary>
        static void OnProduction(NetworkConnection sender, WV_ProductionRequest request, Channel channel) {
            if (sender == null || !sender.IsValid)
                return;

            WV_ProductionItem item = WV_ProductionItem.Decode(request.unitKind);
            WV_TroopRefusal refusal = Produce(sender, request, item);

            // A cancel that found nothing to cancel is not worth reporting; it means the queue moved
            // on under the player's click, which the refreshed queue already shows.
            if (request.cancel)
                return;

            sender.Broadcast(new WV_ProductionResult {
                queued = refusal == WV_TroopRefusal.None,
                refusal = (byte)refusal,
                item = request.unitKind
            });
        }

        static WV_TroopRefusal Produce(
            NetworkConnection sender, WV_ProductionRequest request, WV_ProductionItem item) {
            if (item.IsNone
                || !TryGetOwnedComponent(sender, request.buildingObjectId, out WV_ProductionBuilding building))
                return WV_TroopRefusal.Unavailable;

            WV_Economy economy = WV_Economy.Instance;
            if (economy == null)
                return WV_TroopRefusal.Unavailable;

            if (request.cancel) {
                if (building.TryCancel(item, out int refund))
                    economy.Credit(sender.ClientId, refund);
                return WV_TroopRefusal.None;
            }

            if (!building.CanProduce(item))
                return WV_TroopRefusal.Unavailable;
            if (!building.IsOperational)
                return WV_TroopRefusal.NotOperational;
            if (building.QueueLength >= building.MaxQueueLength)
                return WV_TroopRefusal.QueueFull;

            // The squad cap is checked before payment as well as on delivery. Refusing here is what
            // makes the cap legible: the alternative is charging for a troop that is refunded
            // silently several seconds later.
            if (item.IsTroop
                && WV_ServerTroops.Instance != null
                && WV_ServerTroops.Instance.IsSquadFull(sender.ClientId))
                return WV_TroopRefusal.SquadFull;

            // Charge first, then queue. A failed enqueue hands the money straight back rather than
            // leaving the player short for a unit they never got.
            int cost = building.GetCost(item);
            if (!economy.TryDebit(sender.ClientId, cost))
                return WV_TroopRefusal.NotEnoughFunds;
            if (!building.TryEnqueue(item)) {
                economy.Credit(sender.ClientId, cost);
                return WV_TroopRefusal.Unavailable;
            }
            return WV_TroopRefusal.None;
        }

        static void OnRallyPoint(NetworkConnection sender, WV_RallyPointRequest request, Channel channel) {
            if (sender == null
                || !sender.IsValid
                || !IsFinite(request.position)
                || !TryGetOwnedComponent(sender, request.buildingObjectId, out WV_ProductionBuilding building))
                return;

            building.SetRallyPoint(request.position);
        }

        /// <summary>
        /// Resolves one commandable object the sender actually owns. Produced units carry their
        /// commander in a replicated <see cref="WV_Owned"/>; a trained troop is a server-owned
        /// character whose commander is recorded only on the server, so the two are checked through
        /// their own authorities rather than through one shared assumption.
        /// </summary>
        static bool TryGetOwnedCommandable(
            NetworkConnection sender, int objectId, out WV_ICommandable commandable) {
            commandable = null;
            if (!TryGetSpawned(objectId, out NetworkObject networkObject))
                return false;

            WV_Owned owned = networkObject.GetComponent<WV_Owned>();
            if (owned != null && owned.IsOwnedBy(sender.ClientId)) {
                commandable = networkObject.GetComponent<WV_UnitBrain>();
                if (commandable != null)
                    return true;
            }

            if (WV_ServerTroops.Instance != null
                && WV_ServerTroops.Instance.TryGetOwnedTroop(sender.ClientId, objectId, out WV_TroopBrain troop)) {
                commandable = troop;
                return true;
            }

            return false;
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
