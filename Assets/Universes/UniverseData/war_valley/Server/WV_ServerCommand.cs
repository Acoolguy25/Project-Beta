using FishNet;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Transporting;
using RyanAssets.DataService;
using RyanAssets.Shared.Component;
using RyanAssets.Shared.Declarations;
using UnityEngine;
using Universes.UniverseData.war_valley.Shared;

namespace Universes.UniverseData.war_valley.Server {
    /// <summary>
    /// Authoritative receiver for the client's RTS input: group orders, production, rally points,
    /// demolition, selling, donations, and research.
    /// <para>
    /// Selection itself never crosses the wire - a client decides locally which of its own units are
    /// highlighted and then names them in an order. Every request here is therefore re-validated
    /// against the sender's rights rather than trusted, so a modified client cannot drive another
    /// player's army, spend their funds, or tear down their base.
    /// </para>
    /// <para>
    /// Buildings are checked at the two levels <see cref="WV_Permissions"/> defines: allies may
    /// <i>use</i> a building (queue at it, move its rally point, research at it), and only its
    /// owner may <i>manage</i> it (demolish it, cancel what is in its queue).
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
            InstanceFinder.ServerManager.RegisterBroadcast<WV_QueueCancelRequest>(OnQueueCancel, true);
            InstanceFinder.ServerManager.RegisterBroadcast<WV_DemolishRequest>(OnDemolish, true);
            InstanceFinder.ServerManager.RegisterBroadcast<WV_ResearchRequest>(OnResearch, true);
            InstanceFinder.ServerManager.RegisterBroadcast<WV_SellRequest>(OnSell, true);
            InstanceFinder.ServerManager.RegisterBroadcast<WV_DonateRequest>(OnDonate, true);
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
            InstanceFinder.ServerManager.UnregisterBroadcast<WV_QueueCancelRequest>(OnQueueCancel);
            InstanceFinder.ServerManager.UnregisterBroadcast<WV_DemolishRequest>(OnDemolish);
            InstanceFinder.ServerManager.UnregisterBroadcast<WV_ResearchRequest>(OnResearch);
            InstanceFinder.ServerManager.UnregisterBroadcast<WV_SellRequest>(OnSell);
            InstanceFinder.ServerManager.UnregisterBroadcast<WV_DonateRequest>(OnDonate);
            registered = false;
        }

        // --- Notices ------------------------------------------------------------

        /// <summary>Puts one line on a single commander's HUD.</summary>
        public static void Notify(NetworkConnection connection, string message) {
            if (connection != null && connection.IsValid && !string.IsNullOrEmpty(message))
                connection.Broadcast(new WV_Notice { message = message });
        }

        /// <summary>Puts one line on a commander's HUD, if they are still connected.</summary>
        public static void Notify(int clientId, string message) {
            if (clientId != WV_Owned.NoOwner
                && InstanceFinder.ServerManager != null
                && InstanceFinder.ServerManager.Clients.TryGetValue(clientId, out NetworkConnection connection))
                Notify(connection, message);
        }

        // --- Orders -------------------------------------------------------------

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

        // --- Production ---------------------------------------------------------

        /// <summary>
        /// Queues or cancels one item at a building the sender may use.
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

            if (request.cancel) {
                // A cancel that found nothing to cancel is not worth reporting; it means the queue
                // moved on under the player's click, which the refreshed queue already shows.
                CancelLatest(sender, request.buildingObjectId, item);
                return;
            }

            WV_TroopRefusal refusal = Produce(sender, request.buildingObjectId, item);
            sender.Broadcast(new WV_ProductionResult {
                queued = refusal == WV_TroopRefusal.None,
                refusal = (byte)refusal,
                item = request.unitKind
            });
        }

        static WV_TroopRefusal Produce(NetworkConnection sender, int buildingObjectId, WV_ProductionItem item) {
            if (item.IsNone || !TryGetUsable(sender, buildingObjectId, out WV_ProductionBuilding building))
                return WV_TroopRefusal.Unavailable;

            WV_Economy economy = WV_Economy.Instance;
            if (economy == null)
                return WV_TroopRefusal.Unavailable;

            if (!building.CanProduce(item))
                return WV_TroopRefusal.Unavailable;
            if (!building.IsOperational)
                return WV_TroopRefusal.NotOperational;

            WV_Tech required = WV_TechTree.GetRequirement(item);
            WV_Research research = WV_Research.Instance;
            if (required != WV_Tech.None
                && (research == null || !research.IsResearched(sender.ClientId, required)))
                return WV_TroopRefusal.Locked;

            if (building.QueueLength >= building.MaxQueueLength)
                return WV_TroopRefusal.QueueFull;

            // The force limits are checked before payment. Refusing here is what makes a limit
            // legible: the alternative is charging for a unit that is refunded silently several
            // seconds later. It is the payer's forces the unit will join, so theirs are counted.
            if (!WV_Limits.HasRoom(sender.ClientId, WV_Limits.GetCategory(item), CountTroops(sender.ClientId)))
                return WV_TroopRefusal.SquadFull;

            // Charge first, then queue. A failed enqueue hands the money straight back rather than
            // leaving the player short for a unit they never got.
            int cost = building.GetCost(item);
            if (!economy.TryDebit(sender.ClientId, cost))
                return WV_TroopRefusal.NotEnoughFunds;
            if (!building.TryEnqueue(item, sender.ClientId)) {
                economy.Credit(sender.ClientId, cost);
                return WV_TroopRefusal.Unavailable;
            }
            return WV_TroopRefusal.None;
        }

        /// <summary>The original cancel path: the most recent copy of an item, owner only.</summary>
        static void CancelLatest(NetworkConnection sender, int buildingObjectId, WV_ProductionItem item) {
            if (item.IsNone || !TryGetManaged(sender, buildingObjectId, out WV_ProductionBuilding building))
                return;
            if (building.TryCancel(item, out int refund, out int payerClientId))
                RefundCancelled(payerClientId, refund, item);
        }

        /// <summary>Cancels one specific queue slot. Only the building's owner may cancel anything in it.</summary>
        static void OnQueueCancel(NetworkConnection sender, WV_QueueCancelRequest request, Channel channel) {
            if (sender == null || !sender.IsValid)
                return;

            if (!TryGetUsable(sender, request.buildingObjectId, out WV_ProductionBuilding usable))
                return;
            if (!WV_Permissions.CanManage(sender.ClientId, usable.Owned)) {
                Notify(sender, "Only the building's owner can cancel its queue");
                return;
            }

            WV_ProductionItem item = WV_ProductionItem.Decode(request.item);
            if (item.IsNone || !usable.TryCancelAt(request.queueIndex, item, out int refund, out int payerClientId))
                return;

            RefundCancelled(payerClientId, refund, item);
            if (payerClientId != sender.ClientId)
                Notify(sender, $"Cancelled an ally's {item.DisplayName}; they were refunded");
        }

        /// <summary>Whoever paid for a cancelled entry gets it back, even when someone else cancelled it.</summary>
        static void RefundCancelled(int payerClientId, int refund, WV_ProductionItem item) {
            WV_Economy economy = WV_Economy.Instance;
            if (economy == null || refund <= 0)
                return;
            economy.Credit(payerClientId, refund);
            Notify(payerClientId, $"{item.DisplayName} cancelled - {refund:N0} refunded");
        }

        static void OnRallyPoint(NetworkConnection sender, WV_RallyPointRequest request, Channel channel) {
            if (sender == null
                || !sender.IsValid
                || !IsFinite(request.position)
                || !TryGetUsable(sender, request.buildingObjectId, out WV_ProductionBuilding building))
                return;

            building.SetRallyPoint(request.position);
        }

        // --- Demolition ---------------------------------------------------------

        /// <summary>
        /// Tears down a structure its owner no longer wants, refunding part of the price. The
        /// demolition is an ordinary death - the explosion, the rubble, the queue refunds to each
        /// payer and the research rebalance all follow from it the same way they would from enemy
        /// fire - so there is no second teardown path to keep in step.
        /// </summary>
        static void OnDemolish(NetworkConnection sender, WV_DemolishRequest request, Channel channel) {
            if (sender == null || !sender.IsValid)
                return;
            if (!TryGetUsable(sender, request.buildingObjectId, out StructureComponent structure)
                || !structure.TryGetComponent(out WV_Owned owned))
                return;
            if (!WV_Permissions.CanManage(sender.ClientId, owned)) {
                Notify(sender, $"Only its owner can demolish the {structure.DisplayName}");
                return;
            }
            if (structure.IsDead)
                return;

            // Measured before the kill: the refund shrinks with the damage the building has taken.
            float integrity = structure.TryGetComponent(out WV_Constructable constructable)
                ? constructable.Integrity
                : GetCondition(structure);
            long refund = WV_Rules.GetSellRefund(structure.Cost, integrity);

            structure.Kill(DamageType.Despawn);

            if (refund > 0)
                WV_Economy.Instance?.Credit(sender.ClientId, refund);
            Notify(sender, refund > 0
                ? $"{structure.DisplayName} demolished - {refund:N0} refunded"
                : $"{structure.DisplayName} demolished");
        }

        // --- Selling ------------------------------------------------------------

        /// <summary>
        /// Sells units and troops the sender commands, the way <see cref="OnDemolish"/> sells a
        /// building: part of what each cost to produce comes back, less for a damaged one, and the
        /// unit leaves the field through an ordinary death so every system that watches for one -
        /// the roster, the squad, the force limits - lets go of it the usual way.
        /// </summary>
        static void OnSell(NetworkConnection sender, WV_SellRequest request, Channel channel) {
            if (sender == null || !sender.IsValid || request.objectIds == null)
                return;

            int count = Mathf.Min(request.objectIds.Length, MaxUnitsPerOrder);
            int sold = 0;
            long refund = 0;
            for (int i = 0; i < count; i++) {
                if (!TryGetSellable(sender, request.objectIds[i], out EntityBase entity, out long cost))
                    continue;
                refund += WV_Rules.GetSellRefund(cost, GetCondition(entity));
                entity.Kill(DamageType.Despawn);
                sold++;
            }

            if (sold == 0)
                return;
            if (refund > 0)
                WV_Economy.Instance?.Credit(sender.ClientId, refund);
            Notify(sender, $"Sold {sold} {(sold == 1 ? "unit" : "units")} - {refund:N0} refunded");
        }

        /// <summary>
        /// A living unit or troop the sender commands, and what it cost to produce. A vehicle carries
        /// its price on its prefab; a troop is priced from its loadout, the same figure it was
        /// charged at the barracks.
        /// </summary>
        static bool TryGetSellable(NetworkConnection sender, int objectId, out EntityBase entity, out long cost) {
            entity = null;
            cost = 0;
            if (!TryGetSpawned(objectId, out NetworkObject networkObject))
                return false;

            if (networkObject.TryGetComponent(out WV_Unit unit)) {
                if (unit.IsDead || unit.Owned == null || !unit.Owned.IsOwnedBy(sender.ClientId))
                    return false;
                entity = unit;
                cost = unit.Cost;
                return true;
            }

            if (WV_ServerTroops.Instance != null
                && WV_ServerTroops.Instance.TryGetOwnedTroop(sender.ClientId, objectId, out WV_TroopBrain troop)
                && troop.Character != null
                && !troop.Character.IsDead) {
                entity = troop.Character;
                cost = WV_Rules.GetTroopCost(troop.Kind);
                return true;
            }
            return false;
        }

        /// <summary>Health as a fraction of maximum, the condition a sale is priced by.</summary>
        static float GetCondition(IEntity entity) {
            long max = entity.MaxHealth.Value;
            return max > 0 ? Mathf.Clamp01(entity.Health.Value / (float)max) : 1f;
        }

        // --- Donations ----------------------------------------------------------

        /// <summary>
        /// Moves funds from the sender to an allied commander. The sender must hold the whole amount:
        /// a donation is never partly paid, and never takes a balance below zero.
        /// </summary>
        static void OnDonate(NetworkConnection sender, WV_DonateRequest request, Channel channel) {
            if (sender == null || !sender.IsValid)
                return;

            WV_Economy economy = WV_Economy.Instance;
            int recipient = request.recipientClientId;
            if (economy == null || request.amount <= 0 || recipient == sender.ClientId)
                return;

            if (!PlayerData.TryGetPlayerData(recipient, out PlayerData recipientData)) {
                Notify(sender, "That commander has left");
                return;
            }
            if (!WV_Alliances.AreAllied(sender.ClientId, recipient)) {
                Notify(sender, "You can only donate to allies");
                return;
            }
            if (economy.HasInfiniteFunds) {
                Notify(sender, "Everyone has unlimited funds this round");
                return;
            }
            if (!economy.TryDebit(sender.ClientId, request.amount)) {
                Notify(sender, $"You do not have {request.amount:N0} to give");
                return;
            }

            economy.Credit(recipient, request.amount);
            string recipientName = recipientData.GetPlayerName();
            string senderName = PlayerData.TryGetPlayerData(sender.ClientId, out PlayerData senderData)
                ? senderData.GetPlayerName()
                : "An ally";
            Notify(sender, $"Donated {request.amount:N0} to {recipientName}");
            Notify(recipient, $"{senderName} donated {request.amount:N0} to you");
        }

        // --- Research -----------------------------------------------------------

        /// <summary>
        /// Starts or cancels research from a station the sender may use. Research belongs to the
        /// commander who pays for it and runs on the research stations they own.
        /// </summary>
        static void OnResearch(NetworkConnection sender, WV_ResearchRequest request, Channel channel) {
            if (sender == null || !sender.IsValid)
                return;

            var tech = (WV_Tech)request.tech;
            WV_ResearchRefusal refusal = request.cancel
                ? CancelResearch(sender, tech)
                : StartResearch(sender, request.stationObjectId, tech);

            sender.Broadcast(new WV_ResearchResult {
                tech = request.tech,
                refusal = (byte)refusal,
                cancel = request.cancel
            });
        }

        static WV_ResearchRefusal StartResearch(NetworkConnection sender, int stationObjectId, WV_Tech tech) {
            WV_Research research = WV_Research.Instance;
            WV_Economy economy = WV_Economy.Instance;
            WV_TechDefinition definition = WV_TechTree.Get(tech);
            if (research == null || economy == null || definition == null)
                return WV_ResearchRefusal.Unavailable;

            if (!TryGetUsable(sender, stationObjectId, out WV_ResearchBuilding station))
                return WV_ResearchRefusal.Unavailable;
            if (!station.IsOperational)
                return WV_ResearchRefusal.NoResearchStation;

            WV_ResearchRefusal refusal = research.GetStartRefusal(sender.ClientId, tech);
            if (refusal != WV_ResearchRefusal.None)
                return refusal;

            // With debug funds on nothing is charged, so nothing is owed back on a cancel either.
            int paid = economy.HasInfiniteFunds ? 0 : definition.Cost;
            if (!economy.TryDebit(sender.ClientId, definition.Cost))
                return WV_ResearchRefusal.NotEnoughFunds;

            research.Begin(sender.ClientId, tech, paid);
            return WV_ResearchRefusal.None;
        }

        static WV_ResearchRefusal CancelResearch(NetworkConnection sender, WV_Tech tech) {
            WV_Research research = WV_Research.Instance;
            if (research == null)
                return WV_ResearchRefusal.Unavailable;

            if (!research.TryCancel(sender.ClientId, tech, out int refund))
                return WV_ResearchRefusal.Unavailable;
            if (refund > 0)
                WV_Economy.Instance?.Credit(sender.ClientId, refund);
            return WV_ResearchRefusal.None;
        }

        // --- Resolution ---------------------------------------------------------

        /// <summary>Living troops a commander fields, from the server's authoritative squad roster.</summary>
        static int CountTroops(int clientId) =>
            WV_ServerTroops.Instance != null ? WV_ServerTroops.Instance.CountAlive(clientId) : 0;

        /// <summary>
        /// Resolves one commandable object the sender actually owns. Produced units carry their
        /// commander in a replicated <see cref="WV_Owned"/>; a trained troop is a server-owned
        /// character whose commander is recorded only on the server, so the two are checked through
        /// their own authorities rather than through one shared assumption. Allies may use each
        /// other's buildings but never command each other's armies.
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

        /// <summary>A component on an object the sender may use: their own, or an ally's.</summary>
        static bool TryGetUsable<T>(NetworkConnection sender, int objectId, out T component) where T : Component {
            component = null;
            if (!TryGetSpawned(objectId, out NetworkObject networkObject))
                return false;

            WV_Owned owned = networkObject.GetComponent<WV_Owned>();
            if (!WV_Permissions.CanUse(sender.ClientId, owned))
                return false;

            return networkObject.TryGetComponent(out component);
        }

        /// <summary>A component on an object the sender owns outright.</summary>
        static bool TryGetManaged<T>(NetworkConnection sender, int objectId, out T component) where T : Component {
            component = null;
            if (!TryGetSpawned(objectId, out NetworkObject networkObject))
                return false;

            WV_Owned owned = networkObject.GetComponent<WV_Owned>();
            if (!WV_Permissions.CanManage(sender.ClientId, owned))
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
