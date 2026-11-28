using System;
using System.Collections.Generic;
using FishNet;
using FishNet.Connection;
using FishNet.Transporting;
using RyanAssets.DataService;
using UnityEngine;
using Universes.UniverseData.war_valley.Shared;

namespace Universes.UniverseData.war_valley.Server {
    /// <summary>When doctrine hands are dealt, how big they are, and how often elite cards turn up.</summary>
    [Serializable]
    public sealed class WV_DoctrineTuning {
        [Tooltip("A hand is dealt to every commander each time this many more waves have been held.")]
        [Min(1)] public int EveryWaves = 2;

        [Tooltip("Cards in a hand.")]
        [Range(1, 4)] public int HandSize = 3;

        [Tooltip("Cards in a hand once the War Council unlock is earned.")]
        [Range(1, 4)] public int CouncilHandSize = 4;

        [Tooltip("Chance that each card of a hand is an elite one, once elite doctrines are unlocked.")]
        [Range(0f, 1f)] public float EliteChance = 0.15f;

        [Tooltip("Seconds between Field Medics heals.")]
        [Min(0.1f)] public float MedicTickSeconds = 1f;
    }

    /// <summary>
    /// The server half of doctrines: deals each commander their hands, takes their picks, and turns
    /// the picks into the bonuses the shared building code reads (<see cref="WV_CommanderBonuses"/>)
    /// and into the one-off effects (funds now, a stronger flag). Server-only, so it is added by the
    /// runner rather than serialized on its prefab.
    /// </summary>
    public sealed class WV_ServerDoctrines : MonoBehaviour {
        const byte NothingPicked = byte.MaxValue;

        sealed class Commander {
            public readonly byte[] Ranks = WV_DoctrineCatalog.NewRanks();
            public int Deals;
            public int RerollsUsed;
            public int OfferId;
            public int OfferWavesHeld;
            /// <summary>Hands of the round's schedule this commander has been dealt (catch-up hands included).</summary>
            public int ScheduledDeals;
            /// <summary>The player's saved id, so a commander who leaves and comes back keeps their doctrines.</summary>
            public string Key;
            public string OfferNote;
            /// <summary>The open hand's own note (a catch-up hand), without the featured card's line a redraw may lose.</summary>
            public string OfferBaseNote;
            /// <summary>Whether the open hand is one of the schedule's (dealt again on a return if it was left open).</summary>
            public bool OfferScheduled;
            public readonly List<WV_DoctrineId> Hand = new();
        }

        readonly Dictionary<int, Commander> commanders = new();
        /// <summary>Commanders who left this round, by player id, until they come back or the round ends.</summary>
        readonly Dictionary<string, Commander> departed = new();
        Func<int, string> keyOf;
        /// <summary>Hands the round's schedule has dealt so far, and the waves held at the last one.</summary>
        int scheduledDeals;
        int scheduledWavesHeld;
        WV_DoctrineTuning tuning = new();
        WV_ServerEconomy economy;
        Func<int, string, bool> isUnlocked;
        int roundSeed;
        int nextOfferId = 1;
        float nextMedicTime;
        bool registered;

        /// <summary>Raised after a commander picks a doctrine: their client id and the doctrine.</summary>
        public event Action<int, WV_DoctrineId> Picked;

        /// <summary>The day's featured card: every commander's first hand of a round holds it (if it is not maxed).</summary>
        public WV_DoctrineId? Featured { get; set; }

        /// <summary>Hands the round's schedule has dealt so far.</summary>
        public int ScheduledDeals => scheduledDeals;

        /// <summary>
        /// Wires the doctrines to the round: the tuning, the economy that pays War Chests and
        /// rebates, and the level check (<paramref name="unlockCheck"/>: client id, unlock id).
        /// </summary>
        public void Initialize(WV_DoctrineTuning doctrineTuning, WV_ServerEconomy serverEconomy, Func<int, string, bool> unlockCheck,
            Func<int, string> playerKey) {
            tuning = doctrineTuning ?? new WV_DoctrineTuning();
            economy = serverEconomy;
            isUnlocked = unlockCheck;
            keyOf = playerKey;
        }

        void OnEnable() {
            if (InstanceFinder.ServerManager != null) {
                InstanceFinder.ServerManager.RegisterBroadcast<WV_DoctrinePickRequest>(OnPickRequest, true);
                registered = true;
            }
            WV_ProductionBuilding.UnitProduced += HandleUnitProduced;
            PlayerData.OnPlayerRemoved += HandlePlayerRemoved;
        }

        void OnDisable() {
            if (registered && InstanceFinder.ServerManager != null)
                InstanceFinder.ServerManager.UnregisterBroadcast<WV_DoctrinePickRequest>(OnPickRequest);
            registered = false;
            WV_ProductionBuilding.UnitProduced -= HandleUnitProduced;
            PlayerData.OnPlayerRemoved -= HandlePlayerRemoved;
            WV_CommanderBonuses.Clear();
        }

        /// <summary>Called by the troop roster when a trained troop takes the field: pays the Quartermaster rebate.</summary>
        public void HandleTroopDelivered(int clientId, WV_TroopKind kind) =>
            PayRebate(clientId, WV_Rules.GetTroopCost(kind), vehicle: false);

        void HandleUnitProduced(WV_ProductionBuilding building, WV_Unit unit) {
            if (unit != null && unit.TryGetComponent(out WV_Owned owned))
                PayRebate(owned.OwnerClientId, unit.Cost, vehicle: true);
        }

        void PayRebate(int clientId, long cost, bool vehicle) {
            WV_CommanderModifiers bonuses = WV_CommanderBonuses.For(clientId);
            float rebate = bonuses.DeliveryRebate + (vehicle ? bonuses.VehicleRebate : 0f);
            if (rebate > 0f && economy != null)
                economy.Grant(clientId, (long)Mathf.Round(cost * Mathf.Min(1f, rebate)));
        }

        void HandlePlayerRemoved(PlayerData player) {
            if (player == null || player.Owner == null)
                return;
            int clientId = player.Owner.ClientId;
            if (commanders.Remove(clientId, out Commander commander)) {
                // Kept for the rest of the round: a commander who comes back has the doctrines they
                // picked, and the hand they left open is dealt again as a catch-up hand.
                if (commander.OfferId != 0 && commander.OfferScheduled)
                    commander.ScheduledDeals = Mathf.Max(0, commander.ScheduledDeals - 1);
                commander.OfferId = 0;
                commander.Hand.Clear();
                string key = !string.IsNullOrEmpty(commander.Key) ? commander.Key : player.player_id.Value;
                if (!string.IsNullOrEmpty(key))
                    departed[key] = commander;
            }
            WV_CommanderBonuses.Remove(clientId);
        }

        // --- Round -----------------------------------------------------------

        /// <summary>Forgets every pick: a new round starts every commander with no doctrines.</summary>
        public void BeginRound(int seed) {
            roundSeed = seed;
            commanders.Clear();
            departed.Clear();
            scheduledDeals = 0;
            scheduledWavesHeld = 0;
            Featured = null;
            WV_CommanderBonuses.Clear();
            nextMedicTime = 0f;
            if (InstanceFinder.ServerManager != null && InstanceFinder.ServerManager.Started)
                InstanceFinder.ServerManager.Broadcast(new WV_DoctrineState {
                    ranks = WV_DoctrineCatalog.NewRanks(), pendingOfferId = 0, picked = NothingPicked
                });
        }

        /// <summary>Whether a hand is due after <paramref name="wavesHeld"/> waves.</summary>
        public bool IsDue(int wavesHeld) => wavesHeld > 0 && wavesHeld % Mathf.Max(1, tuning.EveryWaves) == 0;

        /// <summary>Whether this commander has been dealt a hand yet this round.</summary>
        public bool HasBeenDealt(int clientId) => commanders.TryGetValue(clientId, out Commander c) && c.Deals > 0;

        /// <summary>
        /// Deals <paramref name="clientId"/> a hand. A hand still waiting for a pick is settled first
        /// with its first card, so nobody falls behind by leaving cards unpicked.
        /// </summary>
        public void Offer(int clientId, int wavesHeld, bool scheduled = false) {
            if (clientId == WV_Owned.NoOwner)
                return;
            Commander commander = GetOrAdd(clientId);
            if (commander.OfferId != 0 && commander.Hand.Count > 0) {
                WV_DoctrineId settled = commander.Hand[0];
                Apply(clientId, commander, settled, catchUp: false);
                WV_ServerCommand.Notify(clientId, $"Doctrine taken for you: {WV_DoctrineCatalog.Get(settled).Title}");
            }
            Deal(clientId, commander, wavesHeld, null);
            commander.OfferScheduled = scheduled;
        }

        /// <summary>
        /// The round's scheduled hand after <paramref name="wavesHeld"/> waves, for every commander in
        /// <paramref name="clientIds"/>. Commanders who joined late are dealt the hands they missed,
        /// one after another, as they pick.
        /// </summary>
        public void DealRound(int wavesHeld, IEnumerable<int> clientIds) {
            scheduledDeals++;
            scheduledWavesHeld = wavesHeld;
            foreach (int clientId in clientIds) {
                if (clientId == WV_Owned.NoOwner)
                    continue;
                Commander commander = GetOrAdd(clientId);
                Offer(clientId, wavesHeld, scheduled: true);
                commander.ScheduledDeals++;
            }
        }

        /// <summary>
        /// A commander joined (or came back to) a round under way, once their level is known: gives
        /// back the doctrines they held if they left, and deals the first hand they missed.
        /// </summary>
        public void Join(int clientId) {
            if (clientId == WV_Owned.NoOwner)
                return;
            string key = keyOf?.Invoke(clientId);
            if (!commanders.ContainsKey(clientId) && !string.IsNullOrEmpty(key) && departed.Remove(key, out Commander back)) {
                commanders[clientId] = back;
                WV_CommanderBonuses.Set(clientId, WV_DoctrineCatalog.Sum(back.Ranks));
                int held = CountRanks(back);
                if (held > 0)
                    WV_ServerCommand.Notify(clientId, (held == 1 ? "Welcome back: your doctrine still stands" : $"Welcome back: your {held} doctrines still stand"));
            }
            Commander commander = GetOrAdd(clientId);
            commander.Key ??= key;
            CatchUp(clientId, commander);
            if (TryGetConnection(clientId, out NetworkConnection connection))
                SendState(connection);
        }

        /// <summary>Deals a commander the next scheduled hand they missed, when they have no hand open.</summary>
        void CatchUp(int clientId, Commander commander) {
            if (commander.OfferId != 0 || commander.ScheduledDeals >= scheduledDeals)
                return;
            int missed = scheduledDeals - commander.ScheduledDeals;
            commander.ScheduledDeals++;
            Deal(clientId, commander, scheduledWavesHeld, missed > 1 ? $"catch-up hand, {missed - 1} more to come" : "catch-up hand");
            commander.OfferScheduled = true;
        }

        /// <summary>Re-sends a commander's doctrines and any waiting hand, for a client that just (re)joined.</summary>
        public void SendState(NetworkConnection connection) {
            if (connection == null || !connection.IsValid)
                return;
            commanders.TryGetValue(connection.ClientId, out Commander commander);
            SendState(connection.ClientId, commander, NothingPicked);
            if (commander != null && commander.OfferId != 0)
                SendOffer(connection.ClientId, commander);
        }

        /// <summary>Doctrines this commander holds this round, for the round summary.</summary>
        public int CountPicks(int clientId) =>
            commanders.TryGetValue(clientId, out Commander commander) ? CountRanks(commander) : 0;

        static int CountRanks(Commander commander) {
            int total = 0;
            foreach (byte rank in commander.Ranks)
                total += rank;
            return total;
        }

        void Update() {
            if (commanders.Count == 0 || Time.time < nextMedicTime)
                return;
            float interval = Mathf.Max(0.1f, tuning.MedicTickSeconds);
            nextMedicTime = Time.time + interval;
            WV_ServerTroops troops = WV_ServerTroops.Instance;
            if (troops == null)
                return;
            foreach (KeyValuePair<int, Commander> entry in commanders) {
                float regen = WV_CommanderBonuses.For(entry.Key).TroopRegenPerSecond;
                if (regen > 0f)
                    troops.HealSquad(entry.Key, regen * interval);
            }
        }

        // --- Dealing and picking ---------------------------------------------

        Commander GetOrAdd(int clientId) {
            if (!commanders.TryGetValue(clientId, out Commander commander)) {
                commander = new Commander();
                commanders[clientId] = commander;
            }
            return commander;
        }

        bool Unlocked(int clientId, string unlockId) => isUnlocked != null && isUnlocked(clientId, unlockId);

        int RerollsLeft(int clientId, Commander commander) =>
            Mathf.Max(0, (Unlocked(clientId, WV_Retention.DoctrineReroll) ? WV_Retention.RerollsPerRound : 0) - commander.RerollsUsed);

        void Deal(int clientId, Commander commander, int wavesHeld, string note) {
            int handSize = Unlocked(clientId, WV_Retention.FourthCard) ? tuning.CouncilHandSize : tuning.HandSize;
            float eliteChance = Unlocked(clientId, WV_Retention.EliteDoctrines) ? tuning.EliteChance : 0f;
            List<WV_DoctrineId> hand = WV_DoctrineCatalog.Deal(
                WV_DoctrineCatalog.Seed(roundSeed, clientId, commander.Deals), commander.Ranks, handSize,
                id => Unlocked(clientId, id), eliteChance);
            commander.OfferBaseNote = note;
            if (commander.Deals == 0 && Featured.HasValue && WV_DoctrineCatalog.Feature(hand, Featured.Value, commander.Ranks)) {
                string featured = "today's front features " + WV_DoctrineCatalog.Get(Featured.Value).Title;
                note = string.IsNullOrEmpty(note) ? featured : note + "; " + featured;
            }
            commander.OfferNote = note ?? string.Empty;
            commander.Deals++;
            commander.Hand.Clear();
            commander.Hand.AddRange(hand);
            commander.OfferWavesHeld = wavesHeld;
            commander.OfferId = hand.Count > 0 ? nextOfferId++ : 0;
            if (commander.OfferId != 0)
                SendOffer(clientId, commander);
        }

        void OnPickRequest(NetworkConnection sender, WV_DoctrinePickRequest request, Channel channel) {
            if (sender == null || !sender.IsValid || !commanders.TryGetValue(sender.ClientId, out Commander commander))
                return;
            if (commander.OfferId == 0 || request.offerId != commander.OfferId)
                return;

            int clientId = sender.ClientId;
            if (request.reroll) {
                if (RerollsLeft(clientId, commander) <= 0) {
                    WV_ServerCommand.Notify(sender, "No redraws left this round");
                    return;
                }
                commander.RerollsUsed++;
                Deal(clientId, commander, commander.OfferWavesHeld, commander.OfferBaseNote);
                return;
            }

            if (request.choice >= commander.Hand.Count)
                return;
            Apply(clientId, commander, commander.Hand[request.choice], catchUp: true);
        }

        void Apply(int clientId, Commander commander, WV_DoctrineId id, bool catchUp) {
            WV_DoctrineDefinition doctrine = WV_DoctrineCatalog.Get(id);
            commander.OfferId = 0;
            commander.Hand.Clear();
            if (doctrine == null)
                return;

            int index = (int)id;
            WV_CommanderModifiers before = WV_CommanderBonuses.For(clientId);
            commander.Ranks[index] = (byte)Mathf.Min(doctrine.MaxRank, commander.Ranks[index] + 1);
            WV_CommanderBonuses.Set(clientId, WV_DoctrineCatalog.Sum(commander.Ranks));
            // The pick reaches the army and base already standing, and every client sees where it landed.
            WV_LiveBonuses.Apply(clientId, doctrine, before, WV_CommanderBonuses.For(clientId), WV_ServerTroops.Instance);

            long funds = doctrine.GetInstantFunds(commander.OfferWavesHeld);
            if (funds > 0 && economy != null)
                economy.Grant(clientId, funds, $"{doctrine.Title}: +{funds:N0} funds");
            WV_Flag flag = WV_Flag.Instance;
            if (flag != null && flag.IsSpawned && !flag.IsDead) {
                long added = flag.Reinforce(doctrine.FlagHealthBonus);
                long repaired = flag.Repair(doctrine.FlagRepair);
                if (added > 0 || repaired > 0)
                    WV_ServerCommand.NotifyAll($"{WV_Rules.GetOwnedName(clientId, doctrine.Title)} strengthens the flag");
            }

            SendState(clientId, commander, (byte)id);
            Picked?.Invoke(clientId, id);
            // A late joiner's next missed hand follows their pick (not a settled one: a new hand follows that).
            if (catchUp)
                CatchUp(clientId, commander);
        }

        // --- Wire ------------------------------------------------------------

        static bool TryGetConnection(int clientId, out NetworkConnection connection) {
            connection = null;
            return InstanceFinder.ServerManager != null
                   && InstanceFinder.ServerManager.Clients.TryGetValue(clientId, out connection)
                   && connection != null && connection.IsValid;
        }

        void SendOffer(int clientId, Commander commander) {
            if (!TryGetConnection(clientId, out NetworkConnection connection))
                return;
            var doctrines = new byte[commander.Hand.Count];
            var ranks = new byte[commander.Hand.Count];
            for (int i = 0; i < doctrines.Length; i++) {
                doctrines[i] = (byte)commander.Hand[i];
                ranks[i] = commander.Ranks[(int)commander.Hand[i]];
            }
            connection.Broadcast(new WV_DoctrineOffer {
                offerId = commander.OfferId,
                doctrines = doctrines,
                ranks = ranks,
                rerolls = (byte)RerollsLeft(clientId, commander),
                wavesHeld = commander.OfferWavesHeld,
                note = commander.OfferNote ?? string.Empty,
                featuredSlot = (byte)(Featured.HasValue ? commander.Hand.IndexOf(Featured.Value) + 1 : 0)
            });
        }

        static void SendState(int clientId, Commander commander, byte picked) {
            if (!TryGetConnection(clientId, out NetworkConnection connection))
                return;
            connection.Broadcast(new WV_DoctrineState {
                ranks = commander != null ? (byte[])commander.Ranks.Clone() : WV_DoctrineCatalog.NewRanks(),
                pendingOfferId = commander?.OfferId ?? 0,
                picked = picked
            });
        }
    }
}
