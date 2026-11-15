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
    // The build and research menus, and the icons they show.
    public sealed partial class WV_StructureInspector {
        // --- Menu options -------------------------------------------------------

        /// <summary>Reuses option objects across redraws; the grid rebinds cards in place.</summary>
        CommandOption NextOption(int index) {
            if (index < options.Count)
                return options[index];
            var option = new CommandOption();
            options.Add(option);
            return option;
        }

        void TrimOptions(int count) {
            if (options.Count > count)
                options.RemoveRange(count, options.Count - count);
        }

        void BuildProductionOptions() {
            int count = 0;
            WV_Economy economy = WV_Economy.Instance;
            WV_Research research = WV_Research.Instance;
            bool anyOperational = false;
            foreach (WV_ProductionBuilding building in productionGroup)
                anyOperational |= building.IsOperational;

            foreach (WV_TroopKind kind in production.ProducibleTroops) {
                WV_ProductionItem item = WV_ProductionItem.Troop(kind);
                FillProductionOption(NextOption(count++), item, "Infantry", WV_Rules.GetTroopDescription(kind),
                    GetItemIcon(production, item), economy, research, anyOperational);
            }

            foreach (WV_Unit unit in production.ProducibleUnits) {
                if (unit == null)
                    continue;
                WV_ProductionItem item = WV_ProductionItem.Unit(unit.Kind);
                FillProductionOption(NextOption(count++), item, unit.IsAircraft ? "Air" : "Ground",
                    DescribeUnit(unit), unit.Icon, economy, research, anyOperational);
            }

            TrimOptions(count);
        }

        void FillProductionOption(
            CommandOption option, WV_ProductionItem item, string role, string description, Sprite icon,
            WV_Economy economy, WV_Research research, bool operational) {
            int cost = production.GetCost(item);
            WV_Tech required = WV_TechTree.GetRequirement(item);
            bool unlocked = required == WV_Tech.None
                || (research != null && research.IsResearched(LocalClientId, required));

            int queued = 0;
            foreach (WV_ProductionBuilding building in productionGroup)
                queued += building.CountQueued(item);

            option.Id = item.Encoded;
            option.Title = item.DisplayName;
            option.Subtitle = role;
            option.Description = description;
            option.Icon = icon;
            option.Cost = cost;
            option.Seconds = production.GetBuildSeconds(item);
            option.Progress = -1f;
            option.Count = queued;

            WV_ForceCategory category = WV_Limits.GetCategory(item);
            int limit = WV_Limits.GetLimit(category);
            if (!operational) {
                option.State = CommandOptionState.Locked;
                option.StateText = "Building not finished";
            } else if (!unlocked) {
                option.State = CommandOptionState.Locked;
                option.StateText = $"Requires {WV_TechTree.GetDisplayName(required)}";
            } else if (!WV_Limits.HasRoom(LocalClientId, category, localTroopCount())) {
                option.State = CommandOptionState.Locked;
                option.StateText = $"{WV_Limits.GetDisplayName(category)} limit {limit}/{limit}";
            } else if (economy != null && !economy.CanAfford(LocalClientId, cost)) {
                option.State = CommandOptionState.Unaffordable;
                option.StateText = null;
            } else {
                option.State = CommandOptionState.Available;
                option.StateText = null;
            }
        }

        static string DescribeUnit(WV_Unit unit) =>
            $"{unit.UnitMaxHealth} health, {unit.AttackDamage} damage, {unit.AttackRange:0}m range.";

        void BuildResearchOptions() {
            WV_Research research = WV_Research.Instance;
            WV_Economy economy = WV_Economy.Instance;
            int clientId = LocalClientId;
            float rate = WV_ResearchBuilding.GetResearchRate(clientId);
            int running = research != null ? research.CountResearching(clientId) : 0;
            // What a newly started project would get: an equal share with everything already running.
            float newProjectRate = WV_TechTree.GetProjectRate(rate, running + 1);

            int count = 0;
            foreach (WV_TechDefinition definition in WV_TechTree.All) {
                CommandOption option = NextOption(count++);
                option.Id = (int)definition.Tech;
                option.Title = definition.DisplayName;
                option.Subtitle = definition.Category;
                option.Description = DescribeTech(definition);
                option.Icon = GetTechIcon(definition);
                option.Cost = definition.Cost;
                option.Seconds = newProjectRate > 0f ? definition.ResearchSeconds / newProjectRate : 0f;
                option.Progress = -1f;
                option.Count = 0;

                WV_ResearchPhase phase = research != null ? research.GetPhase(clientId, definition.Tech) : WV_ResearchPhase.None;
                if (phase == WV_ResearchPhase.Complete) {
                    option.State = CommandOptionState.Done;
                    option.StateText = "Researched";
                } else if (phase == WV_ResearchPhase.Researching) {
                    float progress = research.GetProgress(clientId, definition.Tech);
                    float remaining = research.GetSecondsRemaining(clientId, definition.Tech);
                    option.State = CommandOptionState.InProgress;
                    option.Progress = progress;
                    option.StateText = remaining < 0f
                        ? $"Stalled at {progress:P0} - no working station"
                        : $"{progress:P0} - {WV_Rules.FormatCountdown(remaining)}";
                } else if (research == null || !research.ArePrerequisitesMet(clientId, definition)) {
                    option.State = CommandOptionState.Locked;
                    option.StateText = "Needs earlier research";
                } else if (rate <= 0f) {
                    option.State = CommandOptionState.Locked;
                    option.StateText = OwnsResearchStation(clientId)
                        ? "Requires a Research Station - yours is still being built"
                        : "Requires a Research Station";
                } else if (economy != null && !economy.CanAfford(clientId, definition.Cost)) {
                    option.State = CommandOptionState.Unaffordable;
                    option.StateText = null;
                } else {
                    option.State = CommandOptionState.Available;
                    option.StateText = null;
                }
            }

            TrimOptions(count);
        }

        string DescribeTech(WV_TechDefinition definition) {
            builder.Clear();
            builder.Append(definition.Description);
            builder.Append("\nTakes ").Append(WV_Rules.FormatCountdown(definition.ResearchSeconds))
                .Append(" with one station. Each station you own adds speed; your projects running together share it.");
            return builder.ToString();
        }

        void HandleOptionClicked(int id) {
            if (InstanceFinder.ClientManager == null)
                return;

            if (menu == MenuKind.Production && structure != null && productionGroup.Count > 0) {
                // The server re-checks funds, research, queue length, and the squad cap, and answers
                // either way; the card only greys out what is certain to be refused.
                InstanceFinder.ClientManager.Broadcast(new WV_ProductionRequest {
                    buildingObjectId = PickProducer(WV_ProductionItem.Decode((byte)id)).NetworkObject.ObjectId,
                    unitKind = (byte)id,
                    cancel = false
                });
            } else if (menu == MenuKind.Research) {
                // Sent through any of the commander's finished stations, so research can be started
                // from its own menu without first finding and selecting one.
                WV_ResearchBuilding station = ResolveResearchStation();
                if (station == null) {
                    hud.SetHint(OwnsResearchStation(LocalClientId)
                        ? "Your Research Station is still being built - research starts once it is finished"
                        : "Build a Research Station first - research needs a finished station of your own");
                    return;
                }
                InstanceFinder.ClientManager.Broadcast(new WV_ResearchRequest {
                    stationObjectId = station.NetworkObject.ObjectId,
                    tech = (byte)id,
                    cancel = false
                });
            }
        }

        /// <summary>
        /// The building in the group that should take the next order: a finished one with room in its
        /// queue, preferring the shortest queue so a group of barracks trains in parallel.
        /// </summary>
        WV_ProductionBuilding PickProducer(WV_ProductionItem item) {
            WV_ProductionBuilding best = production;
            int bestLength = int.MaxValue;
            foreach (WV_ProductionBuilding building in productionGroup) {
                if (!building.IsOperational || !building.CanProduce(item) || building.QueueLength >= building.MaxQueueLength)
                    continue;
                if (building.QueueLength < bestLength) {
                    bestLength = building.QueueLength;
                    best = building;
                }
            }
            return best;
        }

        // --- Icons --------------------------------------------------------------

        Sprite GetItemIcon(WV_ProductionBuilding building, WV_ProductionItem item) {
            if (item.IsTroop)
                return hud.GetTroopIcon(item.TroopKind);
            WV_Unit prefab = building != null ? building.FindPrefab(item.UnitKind) : null;
            return prefab != null ? prefab.Icon : null;
        }

        /// <summary>A technology is pictured by the first structure it unlocks.</summary>
        Sprite GetTechIcon(WV_TechDefinition definition) =>
            definition.UnlockedStructureIds.Count > 0 ? GetStructureIcon(definition.UnlockedStructureIds[0]) : null;

        /// <summary>
        /// The build-menu icon authored on a structure prefab, looked up by its id among the
        /// structures the server offers. Only hits are cached, so a build list that arrives after the
        /// first lookup still resolves.
        /// </summary>
        Sprite GetStructureIcon(string structureId) {
            if (string.IsNullOrEmpty(structureId))
                return null;
            if (structureIcons.TryGetValue(structureId, out Sprite cached))
                return cached;

            SharedGlobalEvents shared = SharedGlobalEvents.Instance;
            var prefabs = InstanceFinder.NetworkManager != null ? InstanceFinder.NetworkManager.SpawnablePrefabs : null;
            if (shared == null || prefabs == null)
                return null;

            foreach (ushort prefabId in shared.Builds) {
                NetworkObject prefab = prefabs.GetObject(asServer: false, prefabId);
                if (prefab != null
                    && prefab.TryGetComponent(out StructureComponent candidate)
                    && candidate.StructureID == structureId) {
                    structureIcons[structureId] = candidate.Sprite;
                    return candidate.Sprite;
                }
            }
            return null;
        }
    }
}
