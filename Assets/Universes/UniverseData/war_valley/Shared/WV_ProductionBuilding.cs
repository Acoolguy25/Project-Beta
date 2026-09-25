using System;
using System.Collections.Generic;
using FishNet;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using RyanAssets.Core;
using RyanAssets.Shared.Declarations;
using UnityEngine;

namespace Universes.UniverseData.war_valley.Shared {
    /// <summary>
    /// One entry in a production queue: what is being built and which commander paid for it.
    /// <para>
    /// Allies may queue at each other's buildings, so the building's owner is not necessarily who
    /// the finished unit belongs to or who a cancel refunds. Recording the payer on the entry is
    /// what lets a barracks train one commander's gunner and another's knifeman back to back.
    /// </para>
    /// </summary>
    public struct WV_QueueEntry {
        /// <summary>A <see cref="WV_ProductionItem.Encoded"/> byte.</summary>
        public byte item;
        /// <summary>Client id of the commander who paid, and who receives the finished unit.</summary>
        public int payerClientId;
    }

    /// <summary>
    /// A structure that trains units: barracks, vehicle hangar, helipad, or airfield.
    /// <para>
    /// The producible list is authored on the prefab as references to unit prefabs, and each unit
    /// carries its own cost and build time on its <see cref="WV_Unit"/>. That keeps a tank priced in
    /// exactly one place whether it is being queued, refunded, or drawn in the client's menu.
    /// </para>
    /// <para>
    /// A building may also train foot soldiers, which are not <see cref="WV_Unit"/>s at all: a troop
    /// is an ordinary character the server arms, and it has no prefab of its own to hang a price on.
    /// Those are authored as bare <see cref="WV_TroopKind"/> values and priced from
    /// <see cref="WV_Rules"/> instead. Both share one queue, keyed by <see cref="WV_ProductionItem"/>,
    /// so a barracks can hold a mixed order and the timer, the refund, and the cancel button do not
    /// each need to know which sort of thing they are looking at.
    /// </para>
    /// </summary>
    [RequireComponent(typeof(StructureComponent), typeof(WV_Constructable), typeof(WV_Owned))]
    public sealed class WV_ProductionBuilding : NetworkBehaviour {
        [Header("Production")]
        [Tooltip("Unit prefabs this building can train. Each prefab's WV_Unit holds its cost and build time.")]
        [SerializeField] WV_Unit[] producibleUnits = Array.Empty<WV_Unit>();
        [Tooltip("Foot soldiers this building can train. Troops have no prefab of their own, so they " +
                 "are priced and timed from WV_Rules rather than from an asset.")]
        [SerializeField] WV_TroopKind[] producibleTroops = Array.Empty<WV_TroopKind>();
        [Tooltip("Where a finished unit appears. Should sit clear of the building's own collider.")]
        [SerializeField] Transform spawnPoint;
        [Tooltip("Default gather point offset from the spawn point, in local space.")]
        [SerializeField] Vector3 defaultRallyOffset = new(0f, 0f, 6f);
        [SerializeField, Min(1)] int maxQueueLength = 6;

        /// <summary>
        /// Items waiting to be built, oldest first. Index 0 is the item currently in progress. Each
        /// entry carries a <see cref="WV_ProductionItem.Encoded"/> byte, so vehicles and troops
        /// share it, and the commander who paid for it.
        /// </summary>
        readonly SyncList<WV_QueueEntry> queue = new();
        readonly SyncVar<float> currentItemCompletionTime = new();
        readonly SyncVar<float> currentItemDuration = new();
        readonly SyncVar<Vector3> rallyPoint = new();

        static readonly List<WV_ProductionBuilding> all = new();

        public static IReadOnlyList<WV_ProductionBuilding> All => all;

        /// <summary>Raised on the client when any queue changes, so an open production panel can refresh.</summary>
        public static event Action QueueChanged;

        /// <summary>
        /// Raised on the server the moment a unit finishes and is spawned. The server assembly
        /// subscribes to attach the unit's brain, which cannot be referenced from shared code.
        /// </summary>
        public static event Action<WV_ProductionBuilding, WV_Unit> UnitProduced;

        /// <summary>
        /// Raised on the server when a troop finishes training, with the commander it belongs to.
        /// Unlike a vehicle, a troop cannot be spawned from here at all - it is a server-side
        /// character assembled by <c>WV_ServerTroops</c> - so this hands the finished order to the
        /// server assembly rather than reporting something already built.
        /// </summary>
        public static event Action<WV_ProductionBuilding, WV_TroopKind, int> TroopProduced;

        WV_Constructable constructable;
        WV_Owned owned;
        StructureComponent structure;

        public IReadOnlyList<WV_Unit> ProducibleUnits => producibleUnits;
        public IReadOnlyList<WV_TroopKind> ProducibleTroops => producibleTroops;
        public int QueueLength => queue.Count;
        public int MaxQueueLength => maxQueueLength;
        public WV_Owned Owned => owned;
        public Vector3 RallyPoint => rallyPoint.Value;
        public bool IsOperational => constructable.IsOperational && !structure.IsDead;

        /// <summary>True for a building that trains foot soldiers, so callers can check the squad cap.</summary>
        public bool TrainsTroops => producibleTroops.Length > 0;

        public Transform SpawnPoint => spawnPoint != null ? spawnPoint : transform;

        /// <summary>Seconds left on the item currently training, or 0 when the queue is idle.</summary>
        public float CurrentItemSecondsRemaining =>
            queue.Count == 0 ? 0f : Mathf.Max(0f, currentItemCompletionTime.Value - NetworkHelper.ServerTime);

        /// <summary>0 to 1 progress of the item currently training. Drives the client's queue bar.</summary>
        public float CurrentItemProgress {
            get {
                if (queue.Count == 0 || currentItemDuration.Value <= 0f)
                    return 0f;
                return Mathf.Clamp01(1f - CurrentItemSecondsRemaining / currentItemDuration.Value);
            }
        }

        /// <summary>What sits at this queue position, or a none-item when the index is past the end.</summary>
        public WV_ProductionItem GetQueuedItem(int index) =>
            index >= 0 && index < queue.Count
                ? WV_ProductionItem.Decode(queue[index].item)
                : WV_ProductionItem.Unit(WV_UnitKind.None);

        /// <summary>Who paid for the entry at this queue position, or <see cref="WV_Owned.NoOwner"/>.</summary>
        public int GetQueuedPayer(int index) =>
            index >= 0 && index < queue.Count ? queue[index].payerClientId : WV_Owned.NoOwner;

        /// <summary>How many copies of <paramref name="item"/> are waiting or in progress.</summary>
        public int CountQueued(WV_ProductionItem item) {
            byte encoded = item.Encoded;
            int count = 0;
            foreach (WV_QueueEntry entry in queue) {
                if (entry.item == encoded)
                    count++;
            }
            return count;
        }

        public bool CanProduce(WV_ProductionItem item) {
            if (item.IsNone)
                return false;
            if (!item.IsTroop)
                return FindPrefab(item.UnitKind) != null;

            foreach (WV_TroopKind kind in producibleTroops) {
                if (kind == item.TroopKind)
                    return true;
            }
            return false;
        }

        /// <summary>What this item costs the commander who queues it.</summary>
        public int GetCost(WV_ProductionItem item) {
            if (item.IsTroop)
                return WV_Rules.GetTroopCost(item.TroopKind);
            WV_Unit prefab = FindPrefab(item.UnitKind);
            return prefab != null ? prefab.Cost : 0;
        }

        /// <summary>How long this item occupies the queue's front slot.</summary>
        public float GetBuildSeconds(WV_ProductionItem item) {
            if (item.IsTroop)
                return WV_Rules.GetTroopBuildSeconds(item.TroopKind);
            WV_Unit prefab = FindPrefab(item.UnitKind);
            return prefab != null ? prefab.BuildSeconds : 1f;
        }

        public WV_Unit FindPrefab(WV_UnitKind kind) {
            foreach (WV_Unit prefab in producibleUnits) {
                if (prefab != null && prefab.Kind == kind)
                    return prefab;
            }
            return null;
        }

        void Awake() {
            constructable = GetComponent<WV_Constructable>();
            owned = GetComponent<WV_Owned>();
            structure = GetComponent<StructureComponent>();
        }

        public override void OnStartNetwork() {
            base.OnStartNetwork();
            all.Add(this);
            queue.OnChange += HandleQueueChanged;
        }

        public override void OnStopNetwork() {
            queue.OnChange -= HandleQueueChanged;
            all.Remove(this);
            QueueChanged?.Invoke();
            base.OnStopNetwork();
        }

        void HandleQueueChanged(
            SyncListOperation op, int index, WV_QueueEntry oldItem, WV_QueueEntry newItem, bool asServer) {
            if (!asServer)
                QueueChanged?.Invoke();
        }

#if UNITY_SERVER
        /// <summary>
        /// Debug multiplier on every queue timer, where 1 is the authored build time and 0.1 builds
        /// ten times faster.
        /// <para>
        /// Static, and set once by the universe's runner, because the alternative is the same debug
        /// field authored on all four production prefabs - which is four places to forget. The
        /// timer it scales is the replicated one, so a client's queue bar follows without knowing
        /// this exists.
        /// </para>
        /// </summary>
        public static float BuildDurationMultiplier { get; set; } = 1f;

        public override void OnStartServer() {
            base.OnStartServer();
            rallyPoint.Value = SpawnPoint.TransformPoint(defaultRallyOffset);
        }

        /// <summary>Moves the gather point. Callers validate ownership before reaching this.</summary>
        [Server]
        public void SetRallyPoint(Vector3 position) {
            rallyPoint.Value = position;
        }

        /// <summary>
        /// Adds an item to the queue on behalf of the commander who paid for it. The caller has
        /// already taken payment, so a false return means the funds must be given back.
        /// </summary>
        [Server]
        public bool TryEnqueue(WV_ProductionItem item, int payerClientId) {
            if (!IsOperational || queue.Count >= maxQueueLength || !CanProduce(item))
                return false;

            queue.Add(new WV_QueueEntry { item = item.Encoded, payerClientId = payerClientId });
            if (queue.Count == 1)
                StartFrontItem();
            return true;
        }

        /// <summary>
        /// Removes the most recently queued copy of this item and reports the refund and who is owed
        /// it. Callers decide who may cancel before reaching this.
        /// </summary>
        [Server]
        public bool TryCancel(WV_ProductionItem item, out int refund, out int payerClientId) {
            byte encoded = item.Encoded;
            for (int i = queue.Count - 1; i >= 0; i--) {
                if (queue[i].item == encoded)
                    return TryCancelAt(i, item, out refund, out payerClientId);
            }
            refund = 0;
            payerClientId = WV_Owned.NoOwner;
            return false;
        }

        /// <summary>
        /// Removes the entry at <paramref name="index"/>, provided it still holds
        /// <paramref name="expected"/>. The check matters because the queue moves on under a click:
        /// an entry finishing between the player's click and the server's handling would otherwise
        /// shift a different unit into the slot the player meant to cancel.
        /// </summary>
        [Server]
        public bool TryCancelAt(int index, WV_ProductionItem expected, out int refund, out int payerClientId) {
            refund = 0;
            payerClientId = WV_Owned.NoOwner;
            if (index < 0 || index >= queue.Count || queue[index].item != expected.Encoded)
                return false;

            payerClientId = queue[index].payerClientId;
            refund = Mathf.RoundToInt(GetCost(expected) * WV_Rules.ProductionRefundFraction);
            queue.RemoveAt(index);
            if (index == 0) {
                if (queue.Count > 0)
                    StartFrontItem();
                else
                    ClearFrontTimer();
            }
            return true;
        }

        /// <summary>
        /// Clears the queue when the building dies or is demolished, reporting each refund to the
        /// commander who paid for it.
        /// </summary>
        [Server]
        public void DrainQueue(Action<int, int> refundPayer) {
            foreach (WV_QueueEntry entry in queue) {
                int refund = Mathf.RoundToInt(
                    GetCost(WV_ProductionItem.Decode(entry.item)) * WV_Rules.ProductionRefundFraction);
                refundPayer?.Invoke(entry.payerClientId, refund);
            }
            queue.Clear();
            ClearFrontTimer();
        }

        void ClearFrontTimer() {
            currentItemCompletionTime.Value = 0f;
            currentItemDuration.Value = 0f;
        }

        void StartFrontItem() {
            // Floored rather than trusted: a multiplier small enough to round the duration to zero
            // would finish an item in the same frame it was queued, which reads as a free unit.
            float duration = Mathf.Max(
                0.1f, GetBuildSeconds(GetQueuedItem(0)) * Mathf.Max(0.001f, BuildDurationMultiplier));
            currentItemDuration.Value = duration;
            currentItemCompletionTime.Value = NetworkHelper.ServerTime + duration;
        }

        void Update() {
            if (!IsServerStarted || queue.Count == 0)
                return;

            if (!IsOperational) {
                // A building knocked back under construction stalls rather than losing the queue.
                currentItemCompletionTime.Value = NetworkHelper.ServerTime + CurrentItemSecondsRemaining;
                return;
            }

            if (NetworkHelper.ServerTime < currentItemCompletionTime.Value)
                return;

            WV_ProductionItem item = GetQueuedItem(0);
            int payerClientId = GetQueuedPayer(0);
            queue.RemoveAt(0);
            if (queue.Count > 0)
                StartFrontItem();
            else
                ClearFrontTimer();

            if (item.IsTroop)
                // Troops are assembled in the server assembly, which owns both the character prefab
                // and the squad roster this one has to join.
                TroopProduced?.Invoke(this, item.TroopKind, payerClientId);
            else
                SpawnProducedUnit(item.UnitKind, payerClientId);
        }

        void SpawnProducedUnit(WV_UnitKind kind, int payerClientId) {
            WV_Unit prefab = FindPrefab(kind);
            if (prefab == null) {
                Debug.LogError($"{name} finished {kind} but has no prefab for it; the unit was lost.");
                return;
            }

            Vector3 position = SpawnPoint.position;
            if (WV_Rules.IsAircraft(kind))
                position += Vector3.up * WV_Rules.GetCruiseAltitude(kind);

            GameObject clone = Instantiate(prefab.gameObject, position, SpawnPoint.rotation);
            InstanceFinder.ServerManager.Spawn(clone, null, gameObject.scene);

            // The unit belongs to whoever paid for it, which is not always the building's owner:
            // an ally queueing at this hangar gets their own tank, in their own colour.
            WV_Unit unit = clone.GetComponent<WV_Unit>();
            unit.Init(unit.UnitMaxHealth);
            unit.SetTeam(WV_Permissions.GetCommanderTeam(payerClientId));
            clone.GetComponent<WV_Owned>().SetOwnerClientId(payerClientId);

            // The brain lives in the server assembly and finishes setup, including walking the new
            // unit to this building's rally point.
            UnitProduced?.Invoke(this, unit);
        }
#endif
    }
}
