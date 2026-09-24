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
    /// Drives the building side of the War Valley HUD: the details panel for the selected
    /// structures and the build or research menu that opens with them.
    /// <para>
    /// The panels themselves are the shared command UI from RyanAssets and know nothing about War
    /// Valley. This class is the translation: it reads the structures' replicated state and the match
    /// rules, decides what the local commander may do - allies may use a building, only its owner may
    /// demolish it or cancel its queue (<see cref="WV_Permissions"/>) - and turns the panels' clicks
    /// into the same requests the server validates again.
    /// </para>
    /// <para>
    /// Several buildings can be selected at once. The first one selected is the <see cref="Primary"/>
    /// the panel describes in detail; buildings of the same kind form its group, whose queues are
    /// shown together and share its menu - an order goes to whichever of them has the shortest
    /// queue, and a rally point or a demolition applies to all of them.
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
            public const int OpenMenu = 1;
            public const int SetRally = 2;
            public const int Demolish = 3;
        }

        /// <summary>How often a shown panel redraws on its own, for countdowns and progress bars.</summary>
        const float RefreshInterval = 0.1f;

        readonly WV_HUD hud;
        readonly Func<int> localClientId;
        readonly Action armRallyPoint;

        readonly List<StructureComponent> selection = new();
        /// <summary>Selected production buildings of the primary's kind: the ones its menu and queue cover.</summary>
        readonly List<WV_ProductionBuilding> productionGroup = new();

        readonly List<CommandStat> stats = new();
        readonly List<CommandAction> actions = new();
        readonly List<CommandQueueEntry> queueEntries = new();
        /// <summary>Where each production queue slot on the strip lives, parallel to <see cref="queueEntries"/>.</summary>
        readonly List<(WV_ProductionBuilding building, int index)> queuedProduction = new();
        /// <summary>Research shown on the strip, parallel to <see cref="queueEntries"/>.</summary>
        readonly List<WV_Tech> queuedResearch = new();
        readonly List<CommandOption> options = new();
        readonly Dictionary<string, int> kindCounts = new();
        readonly Dictionary<string, Sprite> structureIcons = new();
        readonly StringBuilder builder = new();

        // The primary building and its roles.
        StructureComponent structure;
        WV_Owned owned;
        WV_Constructable constructable;
        WV_ProductionBuilding production;
        WV_ResearchBuilding researchStation;
        WV_IncomeBuilding income;
        WV_DefenseTurret turret;

        MenuKind menu;
        float nextRefreshTime;

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

        public bool HasSelection => selection.Count > 0;

        public int Count => selection.Count;

        /// <summary>The building the panel describes in detail: the first one selected.</summary>
        public StructureComponent Primary => structure;

        /// <summary>Selected production buildings that a rally point applies to.</summary>
        public IReadOnlyList<WV_ProductionBuilding> RallyTargets => productionGroup;

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

        /// <summary>Selects exactly one building and opens its menu, if it has one.</summary>
        public void Select(StructureComponent target) {
            selection.Clear();
            if (CanUse(target))
                selection.Add(target);
            // Clicking a building is how its menu is reached, so a click reopens a menu closed earlier.
            OnSelectionChanged(reopenMenu: true);
        }

        /// <summary>Replaces the selection with every usable building in <paramref name="targets"/>.</summary>
        public void SetSelection(IEnumerable<StructureComponent> targets) {
            selection.Clear();
            foreach (StructureComponent target in targets) {
                if (CanUse(target) && !selection.Contains(target))
                    selection.Add(target);
            }
            OnSelectionChanged(reopenMenu: false);
        }

        /// <summary>Adds every usable building in <paramref name="targets"/> to the selection, keeping its primary.</summary>
        public void Add(IEnumerable<StructureComponent> targets) {
            foreach (StructureComponent target in targets) {
                if (CanUse(target) && !selection.Contains(target))
                    selection.Add(target);
            }
            OnSelectionChanged(reopenMenu: false);
        }

        /// <summary>Adds a building to the selection, or takes it out again if it was already in.</summary>
        public void Toggle(StructureComponent target) {
            if (!selection.Remove(target) && CanUse(target))
                selection.Add(target);
            OnSelectionChanged(reopenMenu: false);
        }

        public void Clear() {
            selection.Clear();
            OnSelectionChanged(reopenMenu: false);
        }

        void OnSelectionChanged(bool reopenMenu) {
            StructureComponent previousPrimary = structure;
            BindPrimary(selection.Count > 0 ? selection[0] : null);

            if (structure == null) {
                CloseMenu();
                if (hud.StructurePanel != null)
                    hud.StructurePanel.Hide();
                return;
            }

            // The menu opens with a newly selected building, and a new primary of a different kind
            // swaps it rather than leaving the old one up. An open menu is re-titled for the new
            // group; one the player closed stays closed while the selection merely changes size.
            if (previousPrimary != structure || reopenMenu || IsMenuOpen)
                OpenMenu();
            MarkDirty();
            Refresh();
        }

        void BindPrimary(StructureComponent primary) {
            structure = primary;
            owned = primary != null ? primary.GetComponent<WV_Owned>() : null;
            constructable = primary != null ? primary.GetComponent<WV_Constructable>() : null;
            production = primary != null ? primary.GetComponent<WV_ProductionBuilding>() : null;
            researchStation = primary != null ? primary.GetComponent<WV_ResearchBuilding>() : null;
            income = primary != null ? primary.GetComponent<WV_IncomeBuilding>() : null;
            turret = primary != null ? primary.GetComponent<WV_DefenseTurret>() : null;

            productionGroup.Clear();
            if (production == null)
                return;
            foreach (StructureComponent candidate in selection) {
                if (candidate.StructureID == primary.StructureID
                    && candidate.TryGetComponent(out WV_ProductionBuilding member))
                    productionGroup.Add(member);
            }
        }

        /// <summary>Opens the primary building's menu: its build menu, or the research menu of a station.</summary>
        public bool OpenMenu() {
            if (structure == null || hud.OptionMenu == null)
                return false;

            if (production != null)
                menu = MenuKind.Production;
            else if (researchStation != null)
                menu = MenuKind.Research;
            else {
                CloseMenu();
                return false;
            }

            string title = menu == MenuKind.Research
                ? "Research"
                : $"{(production.TrainsTroops ? "Train" : "Build")} at {GroupName()}";
            string subtitle = menu == MenuKind.Research
                ? "Your own research - every station you own speeds it up"
                : productionGroup.Count > 1
                    ? "Each order goes to the building with the shortest queue"
                    : "Click to queue - units belong to whoever pays";
            hud.OptionMenu.Open(title, subtitle);
            MarkDirty();
            return true;
        }

        public void CloseMenu() {
            menu = MenuKind.None;
            if (hud.OptionMenu != null)
                hud.OptionMenu.Close();
        }

        /// <summary>
        /// Keeps the panel honest: drops selected buildings that were destroyed, despawned, or are no
        /// longer usable, and redraws on the throttle. Returns false when nothing is selected.
        /// </summary>
        public bool Tick() {
            if (selection.Count == 0)
                return false;

            string lostName = null;
            for (int i = selection.Count - 1; i >= 0; i--) {
                StructureComponent candidate = selection[i];
                if (CanUse(candidate))
                    continue;
                lostName ??= candidate != null ? candidate.DisplayName : "Building";
                selection.RemoveAt(i);
            }

            if (lostName != null) {
                bool emptied = selection.Count == 0;
                OnSelectionChanged(reopenMenu: false);
                if (emptied)
                    hud.SetHint($"{lostName} destroyed");
                return !emptied;
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
                panel.Show(selection.Count == 1 ? BuildHeader() : BuildGroupHeader());
                if (selection.Count == 1)
                    BuildStats();
                else
                    BuildGroupStats();
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

        string GroupName() =>
            productionGroup.Count > 1 ? $"{productionGroup.Count} {structure.DisplayName}s" : structure.DisplayName;

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

            stats.Add(new CommandStat("Build cost", MathHelper.AddCommas(structure.Cost)));
        }

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

            int demolishable = 0;
            long refund = 0;
            foreach (StructureComponent member in selection) {
                if (!member.TryGetComponent(out WV_Owned memberOwned) || !WV_Permissions.CanManage(LocalClientId, memberOwned))
                    continue;
                demolishable++;
                refund += GetDemolishRefund(member);
            }

            actions.Add(new CommandAction {
                Id = ActionId.Demolish,
                Label = demolishable > 1 ? $"Demolish {demolishable}" : "Demolish",
                Interactable = demolishable > 0,
                Destructive = true,
                ConfirmLabel = "Confirm?",
                Tooltip = demolishable > 0
                    ? $"Tear down {(demolishable > 1 ? $"the {demolishable} you own" : "it")} and recover " +
                      $"{MathHelper.AddCommas(refund)}. Damage reduces the refund. Anything queued is " +
                      "refunded to whoever paid."
                    : "Only the commander who built a building can demolish it."
            });
        }

        /// <summary>The same figure the server will pay: part of the price, scaled by how intact it is.</summary>
        static long GetDemolishRefund(StructureComponent member) {
            if (!member.TryGetComponent(out WV_Constructable memberConstructable))
                return 0;
            return WV_Rules.GetDemolishRefund(
                member.Cost, memberConstructable.IsOperational, memberConstructable.Integrity);
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
                    option.StateText = "Needs a finished research station of your own";
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
            if (structure == null || InstanceFinder.ClientManager == null)
                return;

            if (menu == MenuKind.Production && productionGroup.Count > 0) {
                // The server re-checks funds, research, queue length, and the squad cap, and answers
                // either way; the card only greys out what is certain to be refused.
                InstanceFinder.ClientManager.Broadcast(new WV_ProductionRequest {
                    buildingObjectId = PickProducer(WV_ProductionItem.Decode((byte)id)).NetworkObject.ObjectId,
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
