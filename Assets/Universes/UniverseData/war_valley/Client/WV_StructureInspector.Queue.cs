using System;
using System.Collections.Generic;
using System.Text;
using FishNet;
using FishNet.Object;
using RyanAssets.Client.ClientUI.Command;
using RyanAssets.Core;
using RyanAssets.DataService;
using RyanAssets.Shared.Declarations;
using RyanAssets.Shared.Global;
using UnityEngine;
using Universes.UniverseData.war_valley.Shared;

namespace Universes.UniverseData.war_valley.Client {
    // The queue strip: production and research in progress for the selected buildings.
    public sealed partial class WV_StructureInspector {
        // --- Queue --------------------------------------------------------------

        void RefreshQueue(CommandQueueStrip strip) {
            if (strip == null)
                return;

            queueEntries.Clear();
            queuedProduction.Clear();
            queuedResearch.Clear();

            if (productionGroup.Count > 0) {
                strip.SetVisible(true);
                int length = 0;
                int capacity = 0;
                foreach (WV_ProductionBuilding building in productionGroup) {
                    BuildProductionQueue(building);
                    length += building.QueueLength;
                    capacity += building.MaxQueueLength;
                }
                strip.SetEntries(queueEntries, $"Queue  {length}/{capacity}", "Queue empty");
            } else if (researchStation != null) {
                strip.SetVisible(true);
                BuildResearchQueue();
                strip.SetEntries(queueEntries, "Your research in progress", "No research running");
            } else {
                strip.SetVisible(false);
            }
        }

        void BuildProductionQueue(WV_ProductionBuilding building) {
            bool canCancel = WV_Permissions.CanManage(LocalClientId, building.Owned);
            for (int i = 0; i < building.QueueLength; i++) {
                WV_ProductionItem item = building.GetQueuedItem(i);
                int payer = building.GetQueuedPayer(i);
                string payerName = payer == LocalClientId ? "you" : GetCommanderName(payer);
                queuedProduction.Add((building, i));
                queueEntries.Add(new CommandQueueEntry {
                    Icon = GetItemIcon(building, item),
                    Title = item.DisplayName,
                    Progress = i == 0 ? building.CurrentItemProgress : -1f,
                    TimeText = i == 0 ? WV_Rules.FormatCountdown(building.CurrentItemSecondsRemaining) : string.Empty,
                    CanCancel = canCancel,
                    Accent = WV_Rules.GetCommanderUIColor(payer),
                    Tooltip = canCancel
                        ? $"{item.DisplayName} for {payerName}. Cancel to refund {payerName}."
                        : $"{item.DisplayName} for {payerName}. Only the building's owner can cancel it."
                });
            }
        }

        void BuildResearchQueue() {
            WV_Research research = WV_Research.Instance;
            if (research == null)
                return;

            int clientId = LocalClientId;
            foreach (WV_TechDefinition definition in WV_TechTree.All) {
                if (research.GetPhase(clientId, definition.Tech) != WV_ResearchPhase.Researching)
                    continue;

                float remaining = research.GetSecondsRemaining(clientId, definition.Tech);
                queuedResearch.Add(definition.Tech);
                queueEntries.Add(new CommandQueueEntry {
                    Icon = GetTechIcon(definition),
                    Title = definition.DisplayName,
                    Progress = research.GetProgress(clientId, definition.Tech),
                    TimeText = remaining < 0f ? "Stalled" : WV_Rules.FormatCountdown(remaining),
                    CanCancel = true,
                    Accent = WV_Rules.GetCommanderUIColor(clientId),
                    Tooltip = $"{definition.DisplayName}. Cancel to get your " +
                              $"{MathHelper.AddCommas(research.GetPaid(clientId, definition.Tech))} back."
                });
            }
        }

        void HandleQueueCancel(int index) {
            if (structure == null || InstanceFinder.ClientManager == null)
                return;

            if (index >= 0 && index < queuedProduction.Count) {
                (WV_ProductionBuilding building, int slot) = queuedProduction[index];
                if (building == null || slot >= building.QueueLength)
                    return;
                InstanceFinder.ClientManager.Broadcast(new WV_QueueCancelRequest {
                    buildingObjectId = building.NetworkObject.ObjectId,
                    queueIndex = slot,
                    item = building.GetQueuedItem(slot).Encoded
                });
                return;
            }

            if (researchStation != null && index >= 0 && index < queuedResearch.Count) {
                InstanceFinder.ClientManager.Broadcast(new WV_ResearchRequest {
                    stationObjectId = structure.NetworkObject.ObjectId,
                    tech = (byte)queuedResearch[index],
                    cancel = true
                });
            }
        }
    }
}
