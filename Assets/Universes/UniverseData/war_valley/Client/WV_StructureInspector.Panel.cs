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
    // The details panel: header, stats, and action buttons for the selected buildings.
    public sealed partial class WV_StructureInspector {
        // --- Header -------------------------------------------------------------

        SelectionHeader BuildHeader() {
            int ownerId = owned.OwnerClientId;
            var header = new SelectionHeader {
                Title = structure.DisplayName,
                Subtitle = BuildOwnerLine(ownerId),
                Icon = structure.Sprite,
                Accent = WV_Rules.GetCommanderUIColor(ownerId),
                Health = structure.Health.Value,
                MaxHealth = structure.MaxHealth.Value,
                StatusProgress = -1f
            };

            if (constructable != null && !constructable.IsOperational) {
                header.Status = $"Under construction - {WV_Rules.FormatCountdown(constructable.SecondsRemaining)}";
                header.StatusProgress = constructable.Progress;
            } else if (production != null && production.QueueLength > 0) {
                header.Status = $"Training {production.GetQueuedItem(0).DisplayName} - " +
                                WV_Rules.FormatCountdown(production.CurrentItemSecondsRemaining);
                header.StatusProgress = production.CurrentItemProgress;
            } else if (researchStation != null) {
                header.Status = researchStation.Owned.IsOwnedBy(LocalClientId)
                    ? "Speeding up your research"
                    : "Speeds up its owner's research";
            } else if (production != null) {
                header.Status = "Idle";
            } else if (income != null) {
                header.Status = $"Earning +{income.IncomePerMinute}/min";
            } else if (turret != null) {
                header.Status = "Guarding";
            } else if (gate != null) {
                header.Status = $"{(gate.IsOpen ? "Open" : "Closed")} - {DescribeGateMode(gate.Mode)}";
            } else if (shield != null) {
                header.Status = shield.IsUp
                    ? $"Shield up - {MathHelper.AddCommas(shield.Health.Value)} / {MathHelper.AddCommas(shield.MaxHealth.Value)}"
                    : $"Shield down - back in {WV_Rules.FormatCountdown(shield.SecondsUntilRestored)}";
                if (shield.IsUp && shield.MaxHealth.Value > 0)
                    header.StatusProgress = shield.Health.Value / (float)shield.MaxHealth.Value;
            }
            return header;
        }

        /// <summary>The header for several buildings: what they are, whose, and their combined health.</summary>
        SelectionHeader BuildGroupHeader() {
            long health = 0;
            long maxHealth = 0;
            bool allMine = true;
            bool sameKind = true;
            foreach (StructureComponent member in selection) {
                health += member.Health.Value;
                maxHealth += member.MaxHealth.Value;
                allMine &= member.TryGetComponent(out WV_Owned memberOwned) && memberOwned.IsOwnedBy(LocalClientId);
                sameKind &= member.StructureID == structure.StructureID;
            }

            return new SelectionHeader {
                Title = sameKind ? $"{selection.Count} x {structure.DisplayName}" : $"{selection.Count} buildings",
                Subtitle = allMine ? "All yours" : "Yours and your allies'",
                Icon = sameKind ? structure.Sprite : null,
                Accent = allMine ? WV_Rules.GetCommanderUIColor(LocalClientId) : new Color(0f, 0f, 0f, 0f),
                Health = health,
                MaxHealth = maxHealth,
                StatusProgress = -1f,
                Status = productionGroup.Count > 1 ? $"{productionGroup.Count} producing together" : null
            };
        }

        string BuildOwnerLine(int ownerId) {
            string relation = ownerId == LocalClientId ? "Yours" : "Ally";
            string name = GetCommanderName(ownerId);
            string color = ColorUtility.ToHtmlStringRGB(WV_Rules.GetCommanderUIColor(ownerId));
            string category = string.IsNullOrWhiteSpace(structure.Category) ? string.Empty : $"{structure.Category}  |  ";
            return $"{category}{relation}: <color=#{color}>{name}</color>";
        }

        static string GetCommanderName(int clientId) =>
            PlayerData.TryGetPlayerData(clientId, out PlayerData player) && !string.IsNullOrEmpty(player.GetPlayerName())
                ? player.GetPlayerName()
                : "Departed commander";

        // --- Stats --------------------------------------------------------------

        void BuildStats() {
            stats.Clear();

            if (income != null)
                stats.Add(new CommandStat("Income", income.IsPaying ? $"+{income.IncomePerMinute}/min" : "When finished"));

            if (production != null) {
                stats.Add(new CommandStat("Trains", BuildProducibleList()));
                stats.Add(new CommandStat("Queue", $"{production.QueueLength} / {production.MaxQueueLength}"));
            }

            if (researchStation != null) {
                int ownerId = researchStation.Owned.OwnerClientId;
                float rate = WV_ResearchBuilding.GetResearchRate(ownerId);
                int stations = WV_ResearchBuilding.CountOperational(ownerId);
                stats.Add(new CommandStat("Station speed", $"+{researchStation.ResearchRate:0.#}"));
                stats.Add(new CommandStat(ownerId == LocalClientId ? "Your research" : "Owner's research",
                    $"{rate:0.#}x from {stations} station{(stations == 1 ? string.Empty : "s")}"));
            }

            if (turret != null) {
                stats.Add(new CommandStat("Targets", turret.IsAntiAir ? "Aircraft" : "Ground"));
                stats.Add(new CommandStat("Range", $"{turret.Range:0}m"));
                stats.Add(new CommandStat("Damage", $"{turret.Damage} every {turret.Cooldown:0.#}s"));
            }

            if (gate != null)
                stats.Add(new CommandStat("Gate", DescribeGateMode(gate.Mode)));

            if (shield != null) {
                stats.Add(new CommandStat("Shield radius", $"{shield.Radius:0}m"));
                stats.Add(new CommandStat("Shield strength", MathHelper.AddCommas(shield.MaxHealth.Value)));
                stats.Add(new CommandStat("Recharge", $"{shield.RegenerationSeconds:0}s after breaking"));
            }

            stats.Add(new CommandStat("Build cost", MathHelper.AddCommas(structure.Cost)));
        }

        static string DescribeGateMode(WV_GateMode mode) => mode switch {
            WV_GateMode.HeldOpen => "Held open for everyone",
            WV_GateMode.Locked => "Locked",
            _ => "Opens for allies"
        };

        /// <summary>For several buildings: how many of each kind, and what they earn together.</summary>
        void BuildGroupStats() {
            stats.Clear();
            kindCounts.Clear();
            int incomePerMinute = 0;
            foreach (StructureComponent member in selection) {
                kindCounts.TryGetValue(member.DisplayName, out int existing);
                kindCounts[member.DisplayName] = existing + 1;
                if (member.TryGetComponent(out WV_IncomeBuilding memberIncome) && memberIncome.IsPaying)
                    incomePerMinute += memberIncome.IncomePerMinute;
            }

            foreach (KeyValuePair<string, int> entry in kindCounts)
                stats.Add(new CommandStat(entry.Key, $"x{entry.Value}"));
            if (incomePerMinute > 0)
                stats.Add(new CommandStat("Combined income", $"+{incomePerMinute}/min"));
        }

        string BuildProducibleList() {
            builder.Clear();
            foreach (WV_TroopKind kind in production.ProducibleTroops)
                AppendListItem(WV_Rules.GetTroopDisplayName(kind));
            foreach (WV_Unit unit in production.ProducibleUnits) {
                if (unit != null)
                    AppendListItem(WV_Rules.GetUnitDisplayName(unit.Kind));
            }
            return builder.Length > 0 ? builder.ToString() : "Nothing";
        }

        void AppendListItem(string item) {
            if (builder.Length > 0)
                builder.Append(", ");
            builder.Append(item);
        }

        // --- Actions ------------------------------------------------------------

        void BuildActions() {
            actions.Clear();

            if (production != null || researchStation != null) {
                actions.Add(new CommandAction {
                    Id = ActionId.OpenMenu,
                    Label = researchStation != null ? "Research" : production.TrainsTroops ? "Train" : "Build",
                    Interactable = true,
                    Tooltip = "Reopen the menu. It also opens whenever you select the building."
                });
            }

            if (productionGroup.Count > 0) {
                actions.Add(new CommandAction {
                    Id = ActionId.SetRally,
                    Label = "Set Rally",
                    Interactable = true,
                    Tooltip = productionGroup.Count > 1
                        ? $"Then click the ground where units from all {productionGroup.Count} should gather."
                        : "Then click the ground where new units should gather."
                });
            }

            if (gate != null && gate.Owned != null && WV_Permissions.CanManage(LocalClientId, gate.Owned)) {
                AddGateAction(ActionId.GateAuto, "Auto", WV_GateMode.Auto,
                    "Opens by itself for allied players, and for your side's troops and vehicles when their route needs it.");
                AddGateAction(ActionId.GateHoldOpen, "Hold Open", WV_GateMode.HeldOpen,
                    "Stays open for everyone - the waves included - until you change it.");
                AddGateAction(ActionId.GateLock, "Lock", WV_GateMode.Locked,
                    "Stays shut, even for allies. Their troops and vehicles go around; with no other way, they wait.");
            }

            int demolishable = 0;
            long refund = 0;
            foreach (StructureComponent member in selection) {
                if (!member.TryGetComponent(out WV_Owned memberOwned) || !WV_Permissions.CanManage(LocalClientId, memberOwned))
                    continue;
                demolishable++;
                refund += GetSellRefund(member);
            }

            actions.Add(new CommandAction {
                Id = ActionId.Demolish,
                Label = demolishable > 1 ? $"Demolish {demolishable}" : "Demolish",
                Interactable = demolishable > 0,
                Destructive = true,
                ConfirmLabel = "Confirm?",
                Tooltip = demolishable > 0
                    ? $"Tear down {(demolishable > 1 ? $"the {demolishable} you own" : "it")} and recover " +
                      $"{MathHelper.AddCommas(refund)} - half the price, less for damage. Anything " +
                      "queued is refunded to whoever paid."
                    : "Only the commander who built a building can demolish it."
            });
        }

        void AddGateAction(int id, string label, WV_GateMode mode, string tooltip) {
            actions.Add(new CommandAction {
                Id = id,
                Label = label,
                Interactable = gate.Mode != mode,
                Tooltip = tooltip
            });
        }

        /// <summary>Sets every selected gate the local commander owns to <paramref name="mode"/>.</summary>
        void SetGateMode(WV_GateMode mode) {
            foreach (StructureComponent member in selection) {
                if (!member.TryGetComponent(out WV_Gate memberGate)
                    || !WV_Permissions.CanManage(LocalClientId, memberGate.Owned))
                    continue;
                InstanceFinder.ClientManager.Broadcast(new WV_GateModeRequest {
                    gateObjectId = member.NetworkObject.ObjectId,
                    mode = (byte)mode
                });
            }
            hud.SetHint($"Gate: {DescribeGateMode(mode)}");
        }

        /// <summary>The same figure the server will pay: part of the price, trimmed by damage.</summary>
        static long GetSellRefund(StructureComponent member) {
            float condition = member.TryGetComponent(out WV_Constructable memberConstructable)
                ? memberConstructable.Integrity
                : member.MaxHealth.Value > 0 ? Mathf.Clamp01(member.Health.Value / (float)member.MaxHealth.Value) : 1f;
            return WV_Rules.GetSellRefund(member.Cost, condition);
        }

        void HandleAction(int id) {
            if (structure == null)
                return;

            switch (id) {
                case ActionId.OpenMenu:
                    OpenMenu();
                    Refresh();
                    break;
                case ActionId.SetRally:
                    armRallyPoint?.Invoke();
                    break;
                case ActionId.GateAuto:
                    SetGateMode(WV_GateMode.Auto);
                    break;
                case ActionId.GateHoldOpen:
                    SetGateMode(WV_GateMode.HeldOpen);
                    break;
                case ActionId.GateLock:
                    SetGateMode(WV_GateMode.Locked);
                    break;
                case ActionId.Demolish:
                    int sent = 0;
                    foreach (StructureComponent member in selection) {
                        if (!member.TryGetComponent(out WV_Owned memberOwned)
                            || !WV_Permissions.CanManage(LocalClientId, memberOwned))
                            continue;
                        InstanceFinder.ClientManager.Broadcast(new WV_DemolishRequest {
                            buildingObjectId = member.NetworkObject.ObjectId
                        });
                        sent++;
                    }
                    hud.SetHint(sent > 1 ? $"Demolishing {sent} buildings" : $"Demolishing {structure.DisplayName}");
                    break;
            }
        }
    }
}
