using System.Collections.Generic;
using FishNet.Connection;
using RyanAssets.Core;
using RyanAssets.DataService;
using RyanAssets.Server.ServerFeatures;
using RyanAssets.Shared.Declarations;
using RyanAssets.Shared.Global;
using UnityEngine;
using Universes.UniverseData.war_valley.Shared;

namespace Universes.UniverseData.war_valley.Server {
    /// <summary>
    /// War Valley's build economy: who can afford what, who owns what they built, and when a site
    /// stops being scaffolding.
    /// <para>
    /// It drives the shared placement path through the hooks on <see cref="ServerStructure"/> rather
    /// than duplicating that validation, so grid snapping, ground probing, and overlap rejection stay
    /// in one place for every universe.
    /// </para>
    /// </summary>
    public sealed class WV_ServerEconomy : MonoBehaviour {
        /// <summary>How often the advertised income rate is recomputed. Purely a HUD figure.</summary>
        const float IncomePublishInterval = 1f;

        readonly List<int> activeClientIds = new();
        float nextIncomePublishTime;

        void OnEnable() {
            ServerStructure.CanPlaceFunction = CanPlaceStructure;
            ServerStructure.StructurePlaced += HandleStructurePlaced;
            PlayerData.OnPlayerAdded += HandlePlayerAdded;
            PlayerData.OnPlayerRemoved += HandlePlayerRemoved;
        }

        void OnDisable() {
            if (ServerStructure.CanPlaceFunction == CanPlaceStructure)
                ServerStructure.CanPlaceFunction = null;
            ServerStructure.StructurePlaced -= HandleStructurePlaced;
            PlayerData.OnPlayerAdded -= HandlePlayerAdded;
            PlayerData.OnPlayerRemoved -= HandlePlayerRemoved;
        }

        // --- Accounts --------------------------------------------------------

        void HandlePlayerAdded(PlayerData playerData) {
            int clientId = GetClientId(playerData);
            if (clientId == WV_Owned.NoOwner)
                return;

            if (!activeClientIds.Contains(clientId))
                activeClientIds.Add(clientId);
            WV_Economy.Instance?.OpenAccount(clientId, WV_Rules.StartingFunds);
        }

        void HandlePlayerRemoved(PlayerData playerData) {
            int clientId = GetClientId(playerData);
            activeClientIds.Remove(clientId);
            WV_Economy.Instance?.CloseAccount(clientId);
        }

        /// <summary>Re-seeds every live commander when the runner restarts a round.</summary>
        public void ResetAccounts() {
            WV_Economy economy = WV_Economy.Instance;
            if (economy == null)
                return;

            activeClientIds.Clear();
            foreach (KeyValuePair<NetworkConnection, PlayerData> entry in PlayerData.Players) {
                if (entry.Key == null || !entry.Key.IsValid)
                    continue;
                activeClientIds.Add(entry.Key.ClientId);
                economy.OpenAccount(entry.Key.ClientId, WV_Rules.StartingFunds);
            }
        }

        static int GetClientId(PlayerData playerData) =>
            playerData != null && playerData.Owner != null && playerData.Owner.IsValid
                ? playerData.Owner.ClientId
                : WV_Owned.NoOwner;

        // --- Placement -------------------------------------------------------

        /// <summary>
        /// Rejects a placement the sender cannot pay for. Runs before the structure is instantiated,
        /// so a refused build costs nothing and spawns nothing.
        /// </summary>
        bool CanPlaceStructure(NetworkConnection sender, StructureComponent prefabStructure) {
            if (sender == null || !sender.IsValid || prefabStructure == null)
                return false;

            WV_Economy economy = WV_Economy.Instance;
            return economy != null && economy.CanAfford(sender.ClientId, (long)prefabStructure.Cost);
        }

        /// <summary>
        /// Charges the build, records its commander, and starts the construction timer. The funds
        /// check in <see cref="CanPlaceStructure"/> is advisory; this debit is the authoritative one.
        /// </summary>
        void HandleStructurePlaced(NetworkConnection sender, StructureComponent structure) {
            if (structure == null)
                return;

            WV_Economy economy = WV_Economy.Instance;
            int clientId = sender != null && sender.IsValid ? sender.ClientId : WV_Owned.NoOwner;

            if (economy != null && !economy.TryDebit(clientId, (long)structure.Cost)) {
                // The balance moved between the gate and here (a second placement in the same frame,
                // or a queued unit charged in between). Refuse rather than hand out a free building.
                structure.Despawn();
                return;
            }

            if (structure.TryGetComponent(out WV_Owned owned))
                owned.SetOwnerClientId(clientId);

            if (structure.TryGetComponent(out WV_Constructable constructable))
                constructable.BeginConstruction();

            if (structure.TryGetComponent(out WV_ProductionBuilding production))
                WatchProductionRefund(production, structure, clientId);
        }

        /// <summary>
        /// A destroyed factory hands back whatever was still in its queue, so a player is not charged
        /// for units that will never arrive.
        /// </summary>
        void WatchProductionRefund(WV_ProductionBuilding production, StructureComponent structure, int clientId) {
            void HandleDied(DamageType source, IEntity attacker) {
                structure.OnDied -= HandleDied;
                int refund = production.DrainQueueRefund();
                if (refund > 0)
                    WV_Economy.Instance?.Credit(clientId, refund);
            }

            structure.OnDied += HandleDied;
        }

        // --- Income ----------------------------------------------------------

        void Update() {
            WV_Economy economy = WV_Economy.Instance;
            if (economy == null || activeClientIds.Count == 0)
                return;

            float now = NetworkHelper.ServerTime;
            if (now < nextIncomePublishTime)
                return;

            nextIncomePublishTime = now + IncomePublishInterval;
            foreach (int clientId in activeClientIds)
                economy.SetIncomePerMinute(clientId, WV_IncomeBuilding.GetIncomePerMinute(clientId));
            PublishBalances(economy);
        }

        /// <summary>
        /// Mirrors each commander's balance into the shared player-list leaderboard, so funds read
        /// next to the player who holds them instead of only in the local player's own HUD.
        /// </summary>
        void PublishBalances(WV_Economy economy) {
            int coinsIndex = SharedGlobalEvents.GetLeaderboardIndex(WV_Rules.CoinsLeaderboard);
            if (coinsIndex < 0)
                return;

            foreach (KeyValuePair<NetworkConnection, PlayerData> entry in PlayerData.Players) {
                NetworkConnection connection = entry.Key;
                PlayerData playerData = entry.Value;
                if (connection == null || !connection.IsValid || playerData == null)
                    continue;
                // A player who joined this frame has not been given their leaderboard row yet.
                if (coinsIndex >= playerData.leaderboard.Count)
                    continue;

                long funds = economy.GetFunds(connection.ClientId);
                int clamped = (int)System.Math.Clamp(funds, 0L, int.MaxValue);
                // SyncList writes replicate per assignment, so only publish an actual change.
                if (playerData.leaderboard[coinsIndex] != clamped)
                    playerData.leaderboard[coinsIndex] = clamped;
            }
        }
    }
}
