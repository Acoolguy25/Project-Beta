using System;
using System.Collections.Generic;
using FishNet;
using FishNet.Connection;
using RyanAssets.Client.ClientUI.Command;
using RyanAssets.Core;
using RyanAssets.DataService;
using UnityEngine;
using Universes.UniverseData.war_valley.Shared;

namespace Universes.UniverseData.war_valley.Client {
    /// <summary>
    /// The HUD's Donate button and its panel: lists every other commander the local one is allied
    /// with (in Survival, everyone) and the balance the local one can give from, and sends the
    /// transfer the server validates and answers with a notice.
    /// </summary>
    public sealed class WV_DonationsPresenter : IDisposable {
        readonly WV_HUD hud;
        readonly Func<int> localClientId;
        readonly List<TransferRecipient> recipients = new();

        public WV_DonationsPresenter(WV_HUD hud, Func<int> localClientId) {
            this.hud = hud;
            this.localClientId = localClientId;

            hud.DonateClicked += Toggle;
            FundsTransferPanel panel = hud.DonatePanel;
            if (panel == null) {
                Debug.LogWarning(
                    "War Valley HUD has no donation panel. Re-run Ryan/War Valley/Rebuild HUD to author it.", hud);
                return;
            }
            panel.TransferRequested += Send;
            panel.CloseRequested += panel.Close;
        }

        public bool IsOpen => hud.DonatePanel != null && hud.DonatePanel.IsOpen;

        public void Dispose() {
            if (hud == null)
                return;
            hud.DonateClicked -= Toggle;
            FundsTransferPanel panel = hud.DonatePanel;
            if (panel == null)
                return;
            panel.TransferRequested -= Send;
            panel.CloseRequested -= panel.Close;
        }

        public void Close() {
            if (IsOpen)
                hud.DonatePanel.Close();
        }

        void Toggle() {
            FundsTransferPanel panel = hud.DonatePanel;
            if (panel == null)
                return;
            if (panel.IsOpen) {
                panel.Close();
                return;
            }
            panel.Open("Donate to an ally");
            Refresh();
        }

        /// <summary>Rewrites the recipients and balance while the panel is open; does nothing while it is closed.</summary>
        public void RefreshIfOpen() {
            if (IsOpen)
                Refresh();
        }

        void Refresh() {
            FundsTransferPanel panel = hud.DonatePanel;
            if (panel == null)
                return;

            int clientId = localClientId();
            WV_Economy economy = WV_Economy.Instance;
            recipients.Clear();
            if (PlayerData.Players != null) {
                foreach (KeyValuePair<NetworkConnection, PlayerData> entry in PlayerData.Players) {
                    if (entry.Key == null || entry.Value == null)
                        continue;
                    int recipientId = entry.Key.ClientId;
                    if (recipientId == clientId || !WV_Alliances.AreAllied(clientId, recipientId))
                        continue;
                    recipients.Add(new TransferRecipient {
                        Id = recipientId,
                        Name = entry.Value.GetPlayerName(),
                        Accent = WV_Rules.GetCommanderUIColor(recipientId),
                        Detail = economy != null ? MathHelper.AddCommas((ulong)Math.Max(0, economy.GetFunds(recipientId))) : null
                    });
                }
            }

            panel.SetRecipients(recipients);
            panel.SetBalance(economy != null ? economy.GetFunds(clientId) : 0, economy != null && economy.HasInfiniteFunds);
        }

        static void Send(int recipientClientId, long amount) {
            if (InstanceFinder.ClientManager == null)
                return;
            // The server answers with a notice either way, so the hint line reports the outcome.
            InstanceFinder.ClientManager.Broadcast(new WV_DonateRequest {
                recipientClientId = recipientClientId,
                amount = amount
            });
        }
    }
}
