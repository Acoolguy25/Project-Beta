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
    /// <summary>
    /// Drives the building side of the War Valley HUD: the details panel for a selected structure
    /// and the build or research menu a right click opens on it.
    /// <para>
    /// The panels themselves are the shared command UI from RyanAssets and know nothing about War
    /// Valley. This class is the translation: it reads the structure's replicated state and the
    /// match rules, decides what the local commander may do - allies may use a building, only its
    /// owner may demolish it or cancel its queue (<see cref="WV_Permissions"/>) - and turns the
    /// panels' clicks into the same requests the server validates again.
    /// </para>
    /// <para>
    /// Redraws are throttled to a few per second and forced immediately by the events that matter
    /// (a queue change, a ledger change, research moving), so countdowns tick visibly without the
    /// panel being rebuilt every frame.
    /// </para>
    /// </summary>
    public sealed class WV_StructureInspector : IDisposable {
        enum MenuKind { None, Production, Research }

        /// <summary>Ids the building panel hands back for its action buttons.</summary>
        static class ActionId {
            public const int OpenProduction = 1;
            public const int OpenResearch = 2;
            public const int SetRally = 3;
            public const int Demolish = 4;
        }

        /// <summary>How often a shown panel redraws on its own, for countdowns and progress bars.</summary>
        const float RefreshInterval = 0.1f;

        readonly WV_HUD hud;
        readonly Func<int> localClientId;
        readonly Action armRallyPoint;

        readonly List<CommandStat> stats = new();
        readonly List<CommandAction> actions = new();
        readonly List<CommandQueueEntry> queueEntries = new();
        /// <summary>Research shown in a station's queue strip, parallel to <see cref="queueEntries"/>.</summary>
        readonly List<WV_Tech> queuedResearch = new();
        readonly List<CommandOption> options = new();
        readonly Dictionary<string, Sprite> structureIcons = new();
        readonly StringBuilder builder = new();

        StructureComponent structure;
        WV_Owned owned;
        WV_Constructable constructable;
        WV_ProductionBuilding production;
        WV_ResearchBuilding researchStation;
        WV_IncomeBuilding income;
        WV_DefenseTurret turret;

        MenuKind menu;
        float nextRefreshTime;
        /// <summary>
        /// Tracked separately from <see cref="structure"/>, which reads as null through Unity's
        /// overloaded equality the moment the building is destroyed - exactly when the panel still
        /// has to be taken down.
        /// </summary>
        bool hasSelection;

        public WV_StructureInspector(WV_HUD hud, Func<int> localClientId, Action armRallyPoint) {
            this.hud = hud;
            this.localClientId = localClientId;
            this.armRallyPoint = armRallyPoint;

            if (hud.StructurePanel != null) {
                hud.StructurePanel.ActionRequested += HandleAction;
                if (hud.StructurePanel.Queue != null)
                    hud.StructurePanel.Queue.CancelRequested += HandleQueueCancel;
            } else {
                Debug.LogError(
                    "War Valley HUD has no structure panel, so buildings cannot be inspected. Re-run " +
                    "Ryan/War Valley/Rebuild HUD.", hud);
            }

            if (hud.OptionMenu != null) {
                hud.OptionMenu.OptionClicked += HandleOptionClicked;
                hud.OptionMenu.CloseRequested += CloseMenu;
            } else {
                Debug.LogError(
                    "War Valley HUD has no build menu, so nothing can be trained or researched. Re-run " +
                    "Ryan/War Valley/Rebuild HUD.", hud);
            }
        }

        public void Dispose() {
            if (hud == null)
                return;
            if (hud.StructurePanel != null) {
                hud.StructurePanel.ActionRequested -= HandleAction;
                if (hud.StructurePanel.Queue != null)
                    hud.StructurePanel.Queue.CancelRequested -= HandleQueueCancel;
            }
            if (hud.OptionMenu != null) {
                hud.OptionMenu.OptionClicked -= HandleOptionClicked;
                hud.OptionMenu.CloseRequested -= CloseMenu;
            }
        }

        int LocalClientId => localClientId();

        public StructureComponent Selected => structure;

        /// <summary>The selected building when it trains units, for rally orders.</summary>
        public WV_ProductionBuilding SelectedProduction => production;

        public bool IsMenuOpen => menu != MenuKind.None && hud.OptionMenu != null && hud.OptionMenu.IsOpen;

        /// <summary>Whether the local commander may select and use this structure at all.</summary>
        public bool CanUse(StructureComponent candidate) =>
            candidate != null
            && candidate.IsSpawned
            && !candidate.IsDead
            && candidate.TryGetComponent(out WV_Owned candidateOwned)
            && WV_Permissions.CanUse(LocalClientId, candidateOwned);

        /// <summary>Forces a redraw on the next tick, for events that change what the panel shows.</summary>
        public void MarkDirty() => nextRefreshTime = 0f;

        // --- Selection ----------------------------------------------------------

        /// <summary>Shows <paramref name="target"/> in the building panel. The menu closes unless it belongs to the same building.</summary>
        public void Select(StructureComponent target) {
            if (target == structure) {
                MarkDirty();
                return;
            }

            CloseMenu();
            hasSelection = true;
            structure = target;
            owned = target.GetComponent<WV_Owned>();
            constructable = target.GetComponent<WV_Constructable>();
            production = target.GetComponent<WV_ProductionBuilding>();
            researchStation = target.GetComponent<WV_ResearchBuilding>();
            income = target.GetComponent<WV_IncomeBuilding>();
            turret = target.GetComponent<WV_DefenseTurret>();
            MarkDirty();
            Refresh();
        }

        public void Clear() {
            CloseMenu();
            hasSelection = false;
            structure = null;
            owned = null;
            constructable = null;
            production = null;
            researchStation = null;
            income = null;
            turret = null;
            if (hud.StructurePanel != null)
                hud.StructurePanel.Hide();
        }

        /// <summary>
        /// Selects <paramref name="target"/> and opens its menu: the build menu of a production
        /// building, the research menu of a research station. Returns false for a structure that has
        /// no menu, so the caller can say so.
        /// </summary>
        public bool OpenMenu(StructureComponent target) {
            Select(target);
            if (production != null)
                menu = MenuKind.Production;
            else if (researchStation != null)
                menu = MenuKind.Research;
            else
                return false;

            if (hud.OptionMenu == null)
                return false;
            hud.OptionMenu.Open(
                menu == MenuKind.Production
                    ? (production.TrainsTroops ? $"Train at {structure.DisplayName}" : $"Build at {structure.DisplayName}")
                    : "Research",
                menu == MenuKind.Production
                    ? "Click to queue - units belong to whoever pays"
                    : "Shared with your allies - more stations research faster");
            MarkDirty();
            Refresh();
            return true;
        }

        public void CloseMenu() {
            menu = MenuKind.None;
            if (hud.OptionMenu != null)
                hud.OptionMenu.Close();
        }

        /// <summary>
        /// Keeps the panel honest: drops a selection that was destroyed, despawned, or is no longer
        /// usable, and redraws on the throttle. Returns false when the selection was dropped.
        /// </summary>
        public bool Tick() {
            if (!hasSelection)
                return false;

            if (!CanUse(structure)) {
                string name = structure != null ? structure.DisplayName : "Building";
                bool destroyed = structure == null || !structure.IsSpawned || structure.IsDead;
                Clear();
                hud.SetHint(destroyed ? $"{name} destroyed" : $"{name} is no longer yours to use");
                return false;
            }

            if (Time.unscaledTime >= nextRefreshTime)
                Refresh();
            return true;
        }

        void Refresh() {
            if (structure == null)
                return;
            nextRefreshTime = Time.unscaledTime + RefreshInterval;

            SelectionInfoPanel panel = hud.StructurePanel;
            if (panel != null) {
                panel.Show(BuildHeader());
                BuildStats();
                panel.SetStats(stats);
                BuildActions();
                panel.SetActions(actions);
                RefreshQueue(panel.Queue);
            }

            if (IsMenuOpen) {
                if (menu == MenuKind.Production)
                    BuildProductionOptions();
                else
                    BuildResearchOptions();
                hud.OptionMenu.SetOptions(options, menu == MenuKind.Production
                    ? "This building has nothing to build"
                    : "Nothing left to research");
            }
        }

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
                int running = WV_Research.Instance != null ? WV_Research.Instance.CountResearching(LocalSide) : 0;
                header.Status = running > 0
                    ? $"Researching {running} project{(running == 1 ? string.Empty : "s")}"
                    : "Idle - right-click to research";
            } else if (production != null) {
                header.Status = "Idle - right-click to train";
            } else if (income != null) {
                header.Status = $"Earning +{income.IncomePerMinute}/min";
            } else if (turret != null) {
                header.Status = "Guarding";
            }
            return header;
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

        TeamColor LocalSide => WV_Permissions.GetSide(LocalClientId);

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
                TeamColor side = LocalSide;
                float sideRate = WV_ResearchBuilding.GetResearchRate(side);
                int stations = WV_ResearchBuilding.CountOperational(side);
                stats.Add(new CommandStat("Station speed", $"+{researchStation.ResearchRate:0.#}"));
                stats.Add(new CommandStat("Side research", $"{sideRate:0.#}x from {stations} station{(stations == 1 ? string.Empty : "s")}"));
            }

            if (turret != null) {
                stats.Add(new CommandStat("Targets", turret.IsAntiAir ? "Aircraft" : "Ground"));
                stats.Add(new CommandStat("Range", $"{turret.Range:0}m"));
                stats.Add(new CommandStat("Damage", $"{turret.Damage} every {turret.Cooldown:0.#}s"));
            }

            stats.Add(new CommandStat("Build cost", MathHelper.AddCommas(structure.Cost)));
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

            if (production != null) {
                actions.Add(new CommandAction {
                    Id = ActionId.OpenProduction,
                    Label = production.TrainsTroops ? "Train" : "Build",
                    Interactable = true,
                    Tooltip = "Open this building's menu. Right-clicking the building does the same."
                });
                actions.Add(new CommandAction {
                    Id = ActionId.SetRally,
                    Label = "Set Rally",
                    Interactable = true,
                    Tooltip = "Then click the ground where new units should gather."
                });
            }

            if (researchStation != null) {
                actions.Add(new CommandAction {
                    Id = ActionId.OpenResearch,
                    Label = "Research",
                    Interactable = true,
                    Tooltip = "Open the research menu. Right-clicking the station does the same."
                });
            }

            bool canManage = WV_Permissions.CanManage(LocalClientId, owned);
            bool operational = constructable == null || constructable.IsOperational;
            long refund = WV_Rules.GetDemolishRefund(structure.Cost, operational);
            actions.Add(new CommandAction {
                Id = ActionId.Demolish,
                Label = "Demolish",
                Interactable = canManage,
                Destructive = true,
                ConfirmLabel = "Confirm?",
                Tooltip = canManage
                    ? $"Tear it down and recover {MathHelper.AddCommas(refund)}. Anything queued is refunded to whoever paid."
                    : "Only the commander who built this can demolish it."
            });
        }

        void HandleAction(int id) {
            if (structure == null)
                return;

            switch (id) {
                case ActionId.OpenProduction:
                case ActionId.OpenResearch:
                    OpenMenu(structure);
                    break;
                case ActionId.SetRally:
                    armRallyPoint?.Invoke();
                    break;
                case ActionId.Demolish:
                    InstanceFinder.ClientManager.Broadcast(new WV_DemolishRequest {
                        buildingObjectId = structure.NetworkObject.ObjectId
                    });
                    hud.SetHint($"Demolishing {structure.DisplayName}");
                    break;
            }
        }

        // --- Queue --------------------------------------------------------------

        void RefreshQueue(CommandQueueStrip strip) {
            if (strip == null)
                return;

            queueEntries.Clear();
            queuedResearch.Clear();

            if (production != null) {
                strip.SetVisible(true);
                BuildProductionQueue();
                strip.SetEntries(queueEntries, $"Queue  {production.QueueLength}/{production.MaxQueueLength}", "Queue empty");
            } else if (researchStation != null) {
                strip.SetVisible(true);
                BuildResearchQueue();
                strip.SetEntries(queueEntries, "Research in progress", "No research running");
            } else {
                strip.SetVisible(false);
            }
        }

        void BuildProductionQueue() {
            bool canCancel = WV_Permissions.CanManage(LocalClientId, owned);
            for (int i = 0; i < production.QueueLength; i++) {
                WV_ProductionItem item = production.GetQueuedItem(i);
                int payer = production.GetQueuedPayer(i);
                string payerName = payer == LocalClientId ? "you" : GetCommanderName(payer);
                queueEntries.Add(new CommandQueueEntry {
                    Icon = GetItemIcon(item),
                    Title = item.DisplayName,
                    Progress = i == 0 ? production.CurrentItemProgress : -1f,
                    TimeText = i == 0 ? WV_Rules.FormatCountdown(production.CurrentItemSecondsRemaining) : string.Empty,
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

            TeamColor side = LocalSide;
            foreach (WV_TechDefinition definition in WV_TechTree.All) {
                if (research.GetPhase(side, definition.Tech) != WV_ResearchPhase.Researching)
                    continue;

                int starter = research.GetStarter(side, definition.Tech);
                bool mine = starter == LocalClientId;
                float remaining = research.GetSecondsRemaining(side, definition.Tech);
                queuedResearch.Add(definition.Tech);
                queueEntries.Add(new CommandQueueEntry {
                    Icon = GetTechIcon(definition),
                    Title = definition.DisplayName,
                    Progress = research.GetProgress(side, definition.Tech),
                    TimeText = remaining < 0f ? "Stalled" : WV_Rules.FormatCountdown(remaining),
                    CanCancel = mine,
                    Accent = WV_Rules.GetCommanderUIColor(starter),
                    Tooltip = mine
                        ? $"{definition.DisplayName}. Cancel to get your {MathHelper.AddCommas(definition.Cost)} back."
                        : $"{definition.DisplayName}, started by {GetCommanderName(starter)}. Only they can cancel it."
                });
            }
        }

        void HandleQueueCancel(int index) {
            if (structure == null)
                return;

            if (production != null) {
                if (index < 0 || index >= production.QueueLength)
                    return;
                InstanceFinder.ClientManager.Broadcast(new WV_QueueCancelRequest {
                    buildingObjectId = structure.NetworkObject.ObjectId,
                    queueIndex = index,
                    item = production.GetQueuedItem(index).Encoded
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
            TeamColor side = LocalSide;
            bool operational = production.IsOperational;

            foreach (WV_TroopKind kind in production.ProducibleTroops) {
                WV_ProductionItem item = WV_ProductionItem.Troop(kind);
                FillProductionOption(NextOption(count++), item, "Infantry", WV_Rules.GetTroopDescription(kind),
                    GetItemIcon(item), economy, research, side, operational);
            }

            foreach (WV_Unit unit in production.ProducibleUnits) {
                if (unit == null)
                    continue;
                WV_ProductionItem item = WV_ProductionItem.Unit(unit.Kind);
                FillProductionOption(NextOption(count++), item, unit.IsAircraft ? "Air" : "Ground",
                    DescribeUnit(unit), unit.Icon, economy, research, side, operational);
            }

            TrimOptions(count);
        }

        void FillProductionOption(
            CommandOption option, WV_ProductionItem item, string role, string description, Sprite icon,
            WV_Economy economy, WV_Research research, TeamColor side, bool operational) {
            int cost = production.GetCost(item);
            WV_Tech required = WV_TechTree.GetRequirement(item);
            bool unlocked = required == WV_Tech.None || (research != null && research.IsResearched(side, required));

            option.Id = item.Encoded;
            option.Title = item.DisplayName;
            option.Subtitle = role;
            option.Description = description;
            option.Icon = icon;
            option.Cost = cost;
            option.Seconds = production.GetBuildSeconds(item);
            option.Progress = -1f;
            option.Count = production.CountQueued(item);

            if (!operational) {
                option.State = CommandOptionState.Locked;
                option.StateText = "Building not finished";
            } else if (!unlocked) {
                option.State = CommandOptionState.Locked;
                option.StateText = $"Requires {WV_TechTree.GetDisplayName(required)}";
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
            TeamColor side = LocalSide;
            float sideRate = WV_ResearchBuilding.GetResearchRate(side);
            int running = research != null ? research.CountResearching(side) : 0;
            // What a newly started project would get: an equal share with everything already running.
            float newProjectRate = WV_TechTree.GetProjectRate(sideRate, running + 1);

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

                WV_ResearchPhase phase = research != null ? research.GetPhase(side, definition.Tech) : WV_ResearchPhase.None;
                if (phase == WV_ResearchPhase.Complete) {
                    option.State = CommandOptionState.Done;
                    option.StateText = "Researched";
                } else if (phase == WV_ResearchPhase.Researching) {
                    float progress = research.GetProgress(side, definition.Tech);
                    float remaining = research.GetSecondsRemaining(side, definition.Tech);
                    option.State = CommandOptionState.InProgress;
                    option.Progress = progress;
                    option.StateText = remaining < 0f
                        ? $"Stalled at {progress:P0} - no working station"
                        : $"{progress:P0} - {WV_Rules.FormatCountdown(remaining)}";
                } else if (research == null || !research.ArePrerequisitesMet(side, definition)) {
                    option.State = CommandOptionState.Locked;
                    option.StateText = "Needs earlier research";
                } else if (sideRate <= 0f) {
                    option.State = CommandOptionState.Locked;
                    option.StateText = "Station not finished";
                } else if (economy != null && !economy.CanAfford(LocalClientId, definition.Cost)) {
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
                .Append(" with one station. Every extra station adds speed; projects running together share it.");
            return builder.ToString();
        }

        void HandleOptionClicked(int id) {
            if (structure == null || InstanceFinder.ClientManager == null)
                return;

            if (menu == MenuKind.Production && production != null) {
                // The server re-checks funds, research, queue length, and the squad cap, and answers
                // either way; the card only greys out what is certain to be refused.
                InstanceFinder.ClientManager.Broadcast(new WV_ProductionRequest {
                    buildingObjectId = structure.NetworkObject.ObjectId,
                    unitKind = (byte)id,
                    cancel = false
                });
            } else if (menu == MenuKind.Research && researchStation != null) {
                InstanceFinder.ClientManager.Broadcast(new WV_ResearchRequest {
                    stationObjectId = structure.NetworkObject.ObjectId,
                    tech = (byte)id,
                    cancel = false
                });
            }
        }

        // --- Icons --------------------------------------------------------------

        Sprite GetItemIcon(WV_ProductionItem item) {
            if (item.IsTroop)
                return hud.GetTroopIcon(item.TroopKind);
            WV_Unit prefab = production != null ? production.FindPrefab(item.UnitKind) : null;
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
