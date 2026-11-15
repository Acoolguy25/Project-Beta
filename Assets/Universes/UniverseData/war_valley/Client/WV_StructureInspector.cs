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
    public sealed partial class WV_StructureInspector : IDisposable {
        enum MenuKind { None, Production, Research }

        /// <summary>Ids the building panel hands back for its action buttons.</summary>
        static class ActionId {
            public const int OpenMenu = 1;
            public const int SetRally = 2;
            public const int Demolish = 3;
            public const int GateAuto = 4;
            public const int GateHoldOpen = 5;
            public const int GateLock = 6;
        }

        /// <summary>How often a shown panel redraws on its own, for countdowns and progress bars.</summary>
        const float RefreshInterval = 0.1f;

        readonly WV_HUD hud;
        readonly Func<int> localClientId;
        /// <summary>Living troops the local commander fields, which count against their soldier limit.</summary>
        readonly Func<int> localTroopCount;
        readonly Action armRallyPoint;

        readonly List<StructureComponent> selection = new();
        readonly List<WV_RangeIndicator> selectedRanges = new();
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
        WV_Gate gate;
        WV_ShieldBarrier shield;

        MenuKind menu;
        /// <summary>
        /// True while the research menu was opened from the HUD rather than from a selected station.
        /// It then stays up as the selection changes, the way the build menu of a selected barracks
        /// would not, because nothing about the selection opened it.
        /// </summary>
        bool standaloneResearch;
        float nextRefreshTime;

        public WV_StructureInspector(WV_HUD hud, Func<int> localClientId, Func<int> localTroopCount, Action armRallyPoint) {
            this.hud = hud;
            this.localClientId = localClientId;
            this.localTroopCount = localTroopCount;
            this.armRallyPoint = armRallyPoint;

            if (hud.StructurePanel != null) {
                hud.StructurePanel.ActionRequested += HandleAction;
                hud.StructurePanel.CloseRequested += Clear;
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
            ClearSelectedRanges();
            if (hud == null)
                return;
            if (hud.StructurePanel != null) {
                hud.StructurePanel.ActionRequested -= HandleAction;
                hud.StructurePanel.CloseRequested -= Clear;
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

        /// <summary>True while the research menu is up, however it was opened.</summary>
        public bool IsResearchMenuOpen => menu == MenuKind.Research && IsMenuOpen;

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
            ClearSelectedRanges();
            foreach (StructureComponent selected in selection) {
                WV_RangeIndicator range = selected.GetComponentInChildren<WV_RangeIndicator>(true);
                if (range == null)
                    continue;
                range.SetSelected(true);
                selectedRanges.Add(range);
            }
            StructureComponent previousPrimary = structure;
            BindPrimary(selection.Count > 0 ? selection[0] : null);

            if (structure == null) {
                // Research opened from the HUD belongs to no building, so losing the building
                // selection leaves it where it is.
                if (!standaloneResearch)
                    CloseMenu();
                if (hud.StructurePanel != null)
                    hud.StructurePanel.Hide();
                return;
            }

            // The menu opens with a newly selected building, and a new primary of a different kind
            // swaps it rather than leaving the old one up. An open menu is re-titled for the new
            // group; one the player closed stays closed while the selection merely changes size. A
            // building with no menu of its own - a wall - leaves research opened from the HUD up.
            bool hasOwnMenu = production != null || researchStation != null;
            if (hasOwnMenu && (previousPrimary != structure || reopenMenu || IsMenuOpen))
                OpenMenu();
            else if (!hasOwnMenu && !standaloneResearch)
                CloseMenu();
            MarkDirty();
            Refresh();
        }

        void ClearSelectedRanges() {
            foreach (WV_RangeIndicator range in selectedRanges) {
                if (range != null)
                    range.SetSelected(false);
            }
            selectedRanges.Clear();
        }

        void BindPrimary(StructureComponent primary) {
            structure = primary;
            owned = primary != null ? primary.GetComponent<WV_Owned>() : null;
            constructable = primary != null ? primary.GetComponent<WV_Constructable>() : null;
            production = primary != null ? primary.GetComponent<WV_ProductionBuilding>() : null;
            researchStation = primary != null ? primary.GetComponent<WV_ResearchBuilding>() : null;
            income = primary != null ? primary.GetComponent<WV_IncomeBuilding>() : null;
            turret = primary != null ? primary.GetComponent<WV_DefenseTurret>() : null;
            gate = primary != null ? primary.GetComponent<WV_Gate>() : null;
            shield = primary != null ? primary.GetComponentInChildren<WV_ShieldBarrier>(true) : null;

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

            if (production != null) {
                menu = MenuKind.Production;
                standaloneResearch = false;
                hud.OptionMenu.Open(
                    $"{(production.TrainsTroops ? "Train" : "Build")} at {GroupName()}",
                    productionGroup.Count > 1
                        ? "Each order goes to the building with the shortest queue"
                        : "Click to queue - units belong to whoever pays");
            } else if (researchStation != null) {
                menu = MenuKind.Research;
                standaloneResearch = false;
                hud.OptionMenu.Open("Research", ResearchSubtitle());
            } else {
                CloseMenu();
                return false;
            }
            MarkDirty();
            return true;
        }

        /// <summary>
        /// Opens the research menu from the HUD, with or without a research station selected. Research
        /// belongs to the commander rather than to a building, so it has a menu of its own; without a
        /// finished station of their own the menu still opens and says that one is needed.
        /// </summary>
        public void OpenResearchMenu() {
            if (hud.OptionMenu == null)
                return;
            menu = MenuKind.Research;
            standaloneResearch = true;
            hud.OptionMenu.Open("Research", ResearchSubtitle());
            MarkDirty();
            RefreshMenu();
        }

        /// <summary>Opens the research menu, or closes it when it is already up.</summary>
        public void ToggleResearchMenu() {
            if (IsResearchMenuOpen)
                CloseMenu();
            else
                OpenResearchMenu();
        }

        public void CloseMenu() {
            menu = MenuKind.None;
            standaloneResearch = false;
            if (hud.OptionMenu != null)
                hud.OptionMenu.Close();
        }

        /// <summary>
        /// The research menu's second line: what research needs when the commander cannot yet run
        /// any, otherwise how it runs.
        /// </summary>
        string ResearchSubtitle() {
            int clientId = LocalClientId;
            if (WV_ResearchBuilding.CountOperational(clientId) > 0)
                return "Your own research - every Research Station you own speeds it up";
            return OwnsResearchStation(clientId)
                ? "Requires a Research Station - yours is still being built"
                : "Requires a Research Station - build one (Support tab) to research";
        }

        /// <summary>Whether the commander owns a research station at all, finished or still going up.</summary>
        static bool OwnsResearchStation(int clientId) {
            foreach (WV_ResearchBuilding station in WV_ResearchBuilding.All) {
                if (station != null && station.Owned != null && station.Owned.IsOwnedBy(clientId))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// The station a research order is sent through: the selected one when it can run it,
        /// otherwise any finished station the commander owns. Research runs on the commander's own
        /// stations whichever one carries the order, so which one is picked changes nothing else.
        /// </summary>
        WV_ResearchBuilding ResolveResearchStation() {
            int clientId = LocalClientId;
            if (researchStation != null && researchStation.IsOperational && CanUse(structure))
                return researchStation;
            foreach (WV_ResearchBuilding station in WV_ResearchBuilding.All) {
                if (station != null && station.IsOperational && station.Owned != null && station.Owned.IsOwnedBy(clientId))
                    return station;
            }
            return null;
        }

        /// <summary>
        /// Keeps the panel honest: drops selected buildings that were destroyed, despawned, or are no
        /// longer usable, and redraws on the throttle. Returns false when nothing is selected.
        /// </summary>
        public bool Tick() {
            if (selection.Count == 0) {
                // The research menu can be up with nothing selected; its progress still has to tick.
                if (IsMenuOpen && Time.unscaledTime >= nextRefreshTime)
                    Refresh();
                return false;
            }

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
            nextRefreshTime = Time.unscaledTime + RefreshInterval;

            SelectionInfoPanel panel = hud.StructurePanel;
            if (structure != null && panel != null) {
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

            RefreshMenu();
        }

        void RefreshMenu() {
            if (!IsMenuOpen)
                return;

            if (menu == MenuKind.Production) {
                if (production == null) {
                    CloseMenu();
                    return;
                }
                BuildProductionOptions();
                hud.OptionMenu.SetOptions(options, "This building has nothing to build");
                return;
            }

            // Re-titled every redraw: a station finishing or falling changes what research needs.
            hud.OptionMenu.Open("Research", ResearchSubtitle());
            BuildResearchOptions();
            hud.OptionMenu.SetOptions(options, "Nothing left to research");
        }

        string GroupName() =>
            productionGroup.Count > 1 ? $"{productionGroup.Count} {structure.DisplayName}s" : structure.DisplayName;
    }
}
