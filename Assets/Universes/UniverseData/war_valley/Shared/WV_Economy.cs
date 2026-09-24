using System;
using System.Collections.Generic;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

namespace Universes.UniverseData.war_valley.Shared {
    /// <summary>
    /// The per-match funds ledger, keyed by client id.
    /// <para>
    /// This is intentionally separate from <c>PlayerData.gold</c>. That field is the player's
    /// persistent account balance and is written back through <c>ServerPlayerSave</c>; spending it on
    /// a mineshaft would charge a round's economy against the player's saved progression.
    /// </para>
    /// </summary>
    public sealed class WV_Economy : NetworkBehaviour {
        public static WV_Economy Instance { get; private set; }

        /// <summary>Raised on the client whenever any player's balance or income changes.</summary>
        public static event Action LedgerChanged;

        readonly SyncDictionary<int, long> funds = new();
        readonly SyncDictionary<int, int> incomePerMinute = new();
        /// <summary>
        /// Debug: every purchase succeeds without touching the ledger. Replicated so each client's
        /// HUD and menus show everything as affordable instead of greying out what the server will
        /// in fact accept.
        /// </summary>
        readonly SyncVar<bool> infiniteFunds = new();

        public override void OnStartNetwork() {
            base.OnStartNetwork();
            Instance = this;
            funds.OnChange += HandleFundsChanged;
            incomePerMinute.OnChange += HandleIncomeChanged;
            infiniteFunds.OnChange += HandleInfiniteFundsChanged;
        }

        public override void OnStopNetwork() {
            funds.OnChange -= HandleFundsChanged;
            incomePerMinute.OnChange -= HandleIncomeChanged;
            infiniteFunds.OnChange -= HandleInfiniteFundsChanged;
            if (Instance == this)
                Instance = null;
            base.OnStopNetwork();
        }

        void HandleFundsChanged(SyncDictionaryOperation op, int key, long value, bool asServer) {
            if (!asServer)
                LedgerChanged?.Invoke();
        }

        void HandleIncomeChanged(SyncDictionaryOperation op, int key, int value, bool asServer) {
            if (!asServer)
                LedgerChanged?.Invoke();
        }

        void HandleInfiniteFundsChanged(bool previous, bool next, bool asServer) {
            if (!asServer)
                LedgerChanged?.Invoke();
        }

        public long GetFunds(int clientId) => funds.TryGetValue(clientId, out long value) ? value : 0;

        public int GetIncomePerMinute(int clientId) =>
            incomePerMinute.TryGetValue(clientId, out int value) ? value : 0;

        /// <summary>True while the debug infinite-funds switch is on for this round.</summary>
        public bool HasInfiniteFunds => infiniteFunds.Value;

        public bool CanAfford(int clientId, long cost) =>
            cost <= 0 || infiniteFunds.Value || GetFunds(clientId) >= cost;

#if UNITY_SERVER
        /// <summary>Debug: every purchase is free. Set once by the runner, applied as each round's ledger opens.</summary>
        public static bool DebugInfiniteFunds { get; set; }

        public override void OnStartServer() {
            base.OnStartServer();
            infiniteFunds.Value = DebugInfiniteFunds;
        }

        [Server]
        public void OpenAccount(int clientId, long startingFunds) {
            if (clientId == WV_Owned.NoOwner)
                return;
            funds[clientId] = startingFunds;
            incomePerMinute[clientId] = 0;
        }

        [Server]
        public void CloseAccount(int clientId) {
            funds.Remove(clientId);
            incomePerMinute.Remove(clientId);
        }

        [Server]
        public void ResetAccounts(long startingFunds) {
            // Rebuild rather than clear so every observer sees one coherent ledger after a restart.
            var clientIds = new List<int>(funds.Keys);
            foreach (int clientId in clientIds) {
                funds[clientId] = startingFunds;
                incomePerMinute[clientId] = 0;
            }
        }

        /// <summary>Credits income or a refund. Saturates rather than wrapping on absurd totals.</summary>
        [Server]
        public void Credit(int clientId, long amount) {
            if (clientId == WV_Owned.NoOwner || amount <= 0)
                return;
            long current = GetFunds(clientId);
            funds[clientId] = long.MaxValue - current < amount ? long.MaxValue : current + amount;
        }

        /// <summary>
        /// Deducts <paramref name="cost"/> only if the balance covers it in full. Returns false and
        /// leaves the ledger untouched otherwise, so callers can reject the purchase outright. With
        /// infinite funds on, every purchase succeeds and nothing is deducted.
        /// </summary>
        [Server]
        public bool TryDebit(int clientId, long cost) {
            if (cost <= 0)
                return true;
            if (clientId == WV_Owned.NoOwner)
                return false;
            if (infiniteFunds.Value)
                return true;

            long current = GetFunds(clientId);
            if (current < cost)
                return false;

            funds[clientId] = current - cost;
            return true;
        }

        /// <summary>
        /// Takes up to <paramref name="amount"/> from a balance, never below zero, and reports what
        /// was actually taken. For penalties, which cannot be refused the way a purchase can.
        /// </summary>
        [Server]
        public long DebitUpTo(int clientId, long amount) {
            if (amount <= 0 || clientId == WV_Owned.NoOwner || infiniteFunds.Value)
                return 0;
            long current = GetFunds(clientId);
            long taken = System.Math.Min(current, amount);
            if (taken > 0)
                funds[clientId] = current - taken;
            return taken;
        }

        /// <summary>Publishes the headline rate the HUD shows. Purely informational.</summary>
        [Server]
        public void SetIncomePerMinute(int clientId, int amount) {
            if (clientId == WV_Owned.NoOwner)
                return;
            incomePerMinute[clientId] = Mathf.Max(0, amount);
        }
#endif
    }
}
