using System.Collections.Generic;
using System.Text;
using FishNet;
using FishNet.Object;
using RyanAssets.Characters.Shared;
using RyanAssets.Client.ClientUI.Build;
using RyanAssets.Input;
using RyanAssets.Shared.Declarations;
using FishNet.Transporting;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using Universes.UniverseData.war_valley.Shared;

namespace Universes.UniverseData.war_valley.Client {
    /// <summary>
    /// War Valley's commander input: box selection, movement and attack orders, rally points, and the
    /// production panel.
    /// <para>
    /// Selection is deliberately local. Nothing about which units are highlighted crosses the wire -
    /// only the resulting order does, naming the units it applies to, and the server re-checks that
    /// the sender actually commands each one.
    /// </para>
    /// <para>
    /// Orders are given through one set of bindings rather than through the mouse alone: a
    /// selection can be parked in a control group and recalled, and Stop and Hold Position are
    /// reachable as keys. Those two orders already existed on the wire and in every brain but no
    /// client could send them, so a selected squad had no way to be told to stand still.
    /// </para>
    /// <para>
    /// The keys deliberately avoid the number row and WASD/Space/Shift, which belong to the toolbar
    /// and to the player's own character. Control groups therefore live on F1-F4 rather than on the
    /// 1-9 an RTS would normally use.
    /// </para>
    /// <para>
    /// A commander's own foot troops are selected and ordered through the same path as their
    /// vehicles. They are ordinary characters rather than <see cref="WV_Unit"/>s, and their
    /// ownership is recorded only on the server, so this client recognises its own by the commander
    /// colour the server already replicates on every troop's team - the same colour their buildings
    /// and their name tags carry.
    /// </para>
    /// <para>
    /// Everything is selected with the left button alone; the right button belongs to the camera.
    /// Clicking a building selects it and opens its menu - the build menu of a barracks, airfield, or
    /// helipad, the research menu of a research station. Shift-click adds or removes buildings, a
    /// double click takes every building of that kind on screen, and a drag box picks up units and
    /// troops, or buildings when it holds no units. Everything about the selected buildings is
    /// delegated to <see cref="WV_StructureInspector"/>.
    /// </para>
    /// </summary>
    public sealed class WV_ClientController : MonoBehaviour {
        /// <summary>Pixels of travel before a click becomes a box drag.</summary>
        const float DragThresholdPixels = 8f;

        /// <summary>Seconds within which a second click on the same unit means "select all of these".</summary>
        const float DoubleClickSeconds = 0.3f;

        /// <summary>
        /// How many control groups are bound. Four fits on F1-F4, which is as far as the function
        /// row can be used without running into keys the browser and the Editor take.
        /// </summary>
        const int ControlGroupCount = 4;

        [Header("Authored References")]
        [Tooltip("The War Valley HUD prefab. Instantiated once and bound to live match state.")]
        [SerializeField] WV_HUD hudPrefab;
        [Tooltip("Ring shown under a troop this client has selected. Leave unset to select troops " +
                 "without a ring rather than to disable troop selection.")]
        [SerializeField] GameObject troopSelectionIndicator;

        [Header("Selection")]
        [Tooltip("Maximum units a single box selection can pick up.")]
        [SerializeField, Min(1)] int maxSelection = 120;

        /// <summary>How often the economy card's research line is rewritten while research runs.</summary>
        const float ResearchSummaryInterval = 0.25f;

        readonly List<WV_Unit> selection = new();
        readonly List<WV_Unit> ownedScratch = new();
        readonly List<int> orderScratch = new();
        readonly List<StructureComponent> structureScratch = new();
        /// <summary>Technologies the local commander already had, so only new completions are announced.</summary>
        readonly HashSet<WV_Tech> knownResearched = new();
        readonly StringBuilder researchSummary = new();

        // Every character this client can see. Troops have no client-visible roster of their own, so
        // the shared character events are the cheapest source of one that stays correct.
        readonly List<GameCharacter> characters = new();
        readonly List<GameCharacter> troopSelection = new();
        readonly Dictionary<GameCharacter, GameObject> troopIndicators = new();

        /// <summary>
        /// Selections parked on F1-F4. A group holds the things themselves rather than their ids, so
        /// recalling one costs no lookup; members that died in the meantime are dropped on recall.
        /// </summary>
        readonly ControlGroup[] controlGroups = new ControlGroup[ControlGroupCount];

        WV_HUD hud;
        WV_StructureInspector inspector;
        /// <summary>The research ledger <see cref="knownResearched"/> was taken from; a new round brings a new one.</summary>
        WV_Research trackedResearch;
        float nextResearchSummaryTime;
        /// <summary>
        /// The order waiting for a click to tell it where to land, or null while the left button
        /// still means selection. Move, AttackMove and Attack need a point or a target; Stop and
        /// Hold are issued outright and never arm.
        /// </summary>
        WV_OrderType? armedOrder;
        Object lastClicked;
        float lastClickTime;
        Vector2 dragStart;
        bool dragging;
        bool pointerDown;
        bool economyDirty = true;
        bool researchDirty = true;
        bool broadcastsRegistered;

        /// <summary>One parked selection. Kept as a class so an empty slot is simply null.</summary>
        sealed class ControlGroup {
            public readonly List<WV_Unit> Units = new();
            public readonly List<GameCharacter> Troops = new();
        }

        int LocalClientId =>
            InstanceFinder.ClientManager != null && InstanceFinder.ClientManager.Connection.IsValid
                ? InstanceFinder.ClientManager.Connection.ClientId
                : WV_Owned.NoOwner;

        void Awake() {
            if (hudPrefab == null) {
                Debug.LogError(
                    $"{nameof(WV_ClientController)} has no HUD prefab assigned; War Valley cannot show " +
                    "funds, selection, or production.", this);
                enabled = false;
                return;
            }

            hud = Instantiate(hudPrefab);
            hud.name = "War Valley HUD";
            hud.ShowCommands();
            BindCommandMenu();
            inspector = new WV_StructureInspector(hud, () => LocalClientId, ArmRallyPoint);
            hud.SetHint(
                "Click a building to open its menu • Shift-click to add • Drag to select units, or buildings\n"
                + "M move • V attack-move • T attack • X stop • H hold • Ctrl+F1-F4 set group • F1-F4 recall");
        }

        /// <summary>
        /// Points the command card at this controller. Every button goes through the same methods
        /// the keys do, so a card click and a shortcut cannot drift apart.
        /// </summary>
        void BindCommandMenu() {
            WV_CommandMenu menu = hud.CommandMenu;
            if (menu == null) {
                Debug.LogWarning(
                    $"{nameof(WV_ClientController)}: the HUD prefab has no command card, so orders are " +
                    "reachable only by key. Re-run Ryan/War Valley/Rebuild HUD to author it.", this);
                return;
            }

            menu.OrderArmed += ArmOrder;
            menu.SelectRequested += SelectAllOwned;
            menu.GroupRecalled += RecallControlGroup;
            menu.GroupBound += BindControlGroup;
            menu.SetHasSelection(false);
        }

        void UnbindCommandMenu() {
            WV_CommandMenu menu = hud != null ? hud.CommandMenu : null;
            if (menu == null)
                return;
            menu.OrderArmed -= ArmOrder;
            menu.SelectRequested -= SelectAllOwned;
            menu.GroupRecalled -= RecallControlGroup;
            menu.GroupBound -= BindControlGroup;
        }

        /// <summary>
        /// Selects everything of the requested sort this commander owns. This is the card's answer to
        /// box selection: a player who cannot find their army on screen can still take hold of it.
        /// </summary>
        void SelectAllOwned(bool units, bool troops) {
            ClearSelection();

            if (units) {
                WV_Unit.CollectOwnedBy(LocalClientId, ownedScratch);
                foreach (WV_Unit unit in ownedScratch) {
                    if (SelectionCount >= maxSelection)
                        break;
                    AddToSelection(unit);
                }
            }

            if (troops) {
                foreach (GameCharacter character in characters) {
                    if (SelectionCount >= maxSelection)
                        break;
                    if (IsCommandableTroop(character))
                        AddToSelection(character);
                }
            }

            if (SelectionCount > 0)
                SelectBuilding(null);
            RefreshSelectionUI();
            hud.SetHint(SelectionCount > 0 ? $"{SelectionCount} selected" : "Nothing to select");
        }

        void OnEnable() {
            WV_Economy.LedgerChanged += MarkEconomyDirty;
            WV_Research.Changed += MarkResearchDirty;
            WV_Unit.RosterChanged += HandleRosterChanged;
            WV_ProductionBuilding.QueueChanged += HandleQueueChanged;
            GameCharacter.GameCharacterAdded += HandleCharacterAdded;
            GameCharacter.GameCharacterRemoved += HandleCharacterRemoved;

            // The shared build menu knows nothing about research or funds; War Valley answers for it.
            StructureMenu.AvailabilityProvider = GetStructureAvailability;
            StructureMenu.AffordabilityProvider = CanAffordStructure;
            StructureMenu.NotifyAvailabilityChanged();
        }

        void OnDisable() {
            WV_Economy.LedgerChanged -= MarkEconomyDirty;
            WV_Research.Changed -= MarkResearchDirty;
            WV_Unit.RosterChanged -= HandleRosterChanged;
            WV_ProductionBuilding.QueueChanged -= HandleQueueChanged;
            GameCharacter.GameCharacterAdded -= HandleCharacterAdded;
            GameCharacter.GameCharacterRemoved -= HandleCharacterRemoved;

            if (StructureMenu.AvailabilityProvider == GetStructureAvailability)
                StructureMenu.AvailabilityProvider = null;
            if (StructureMenu.AffordabilityProvider == CanAffordStructure)
                StructureMenu.AffordabilityProvider = null;
            StructureMenu.NotifyAvailabilityChanged();

            if (broadcastsRegistered && InstanceFinder.ClientManager != null) {
                InstanceFinder.ClientManager.UnregisterBroadcast<WV_ProductionResult>(HandleProductionResult);
                InstanceFinder.ClientManager.UnregisterBroadcast<WV_ResearchResult>(HandleResearchResult);
                InstanceFinder.ClientManager.UnregisterBroadcast<WV_Notice>(HandleNotice);
            }
            broadcastsRegistered = false;
        }

        void RegisterBroadcasts() {
            if (broadcastsRegistered || InstanceFinder.ClientManager == null || !InstanceFinder.ClientManager.Started)
                return;
            broadcastsRegistered = true;
            InstanceFinder.ClientManager.RegisterBroadcast<WV_ProductionResult>(HandleProductionResult);
            InstanceFinder.ClientManager.RegisterBroadcast<WV_ResearchResult>(HandleResearchResult);
            InstanceFinder.ClientManager.RegisterBroadcast<WV_Notice>(HandleNotice);
        }

        /// <summary>
        /// Reports what the server did with a build request. Queueing can be refused for several
        /// ordinary reasons, and a button that silently does nothing is indistinguishable from a
        /// broken one, so every answer is put on the hint line.
        /// </summary>
        void HandleProductionResult(WV_ProductionResult result, Channel channel) {
            WV_ProductionItem item = WV_ProductionItem.Decode(result.item);
            hud.SetHint(result.queued
                ? $"{item.DisplayName} queued"
                : WV_Rules.GetProductionRefusalMessage((WV_TroopRefusal)result.refusal, item));
        }

        void HandleResearchResult(WV_ResearchResult result, Channel channel) {
            var tech = (WV_Tech)result.tech;
            var refusal = (WV_ResearchRefusal)result.refusal;
            string name = WV_TechTree.GetDisplayName(tech);
            if (refusal != WV_ResearchRefusal.None)
                hud.SetHint(WV_Rules.GetResearchRefusalMessage(refusal, tech));
            else
                hud.SetHint(result.cancel ? $"{name} research cancelled and refunded" : $"Researching {name}");
        }

        /// <summary>Server-side outcomes the player would otherwise never see: refusals, refunds, penalties.</summary>
        void HandleNotice(WV_Notice notice, Channel channel) => hud.SetHint(notice.message);

        void OnDestroy() {
            UnbindCommandMenu();
            inspector?.Dispose();
            if (hud != null)
                Destroy(hud.gameObject);
        }

        void MarkEconomyDirty() => economyDirty = true;

        void MarkResearchDirty() => researchDirty = true;

        // --- Build menu gates -------------------------------------------------

        StructureAvailability GetStructureAvailability(StructureComponent structure) {
            WV_Tech required = WV_TechTree.GetRequirement(structure.StructureID);
            if (required == WV_Tech.None)
                return StructureAvailability.Available;

            WV_Research research = WV_Research.Instance;
            return research != null && research.IsResearched(LocalClientId, required)
                ? StructureAvailability.Available
                : StructureAvailability.Locked($"Research {WV_TechTree.GetDisplayName(required)} at a research station");
        }

        bool CanAffordStructure(StructureComponent structure) {
            WV_Economy economy = WV_Economy.Instance;
            return economy == null || economy.CanAfford(LocalClientId, (long)structure.Cost);
        }

        // --- Research ---------------------------------------------------------

        /// <summary>
        /// Announces research the local commander has just finished and re-evaluates every lock that
        /// depends on it. The first reading of each round's ledger is taken silently, so joining a
        /// round - or the debug switch that starts it fully researched - does not flood the hint line.
        /// </summary>
        void RefreshResearch() {
            researchDirty = false;
            StructureMenu.NotifyAvailabilityChanged();
            inspector.MarkDirty();

            WV_Research research = WV_Research.Instance;
            bool baseline = research != trackedResearch;
            trackedResearch = research;
            if (baseline)
                knownResearched.Clear();
            if (research == null)
                return;

            int clientId = LocalClientId;
            foreach (WV_TechDefinition definition in WV_TechTree.All) {
                if (!research.IsResearched(clientId, definition.Tech) || !knownResearched.Add(definition.Tech) || baseline)
                    continue;
                hud.SetHint($"Research complete: {definition.DisplayName} - {definition.Description}");
            }
        }

        /// <summary>The economy card's one-line view of what the local commander is researching.</summary>
        void RefreshResearchSummary() {
            nextResearchSummaryTime = Time.unscaledTime + ResearchSummaryInterval;
            WV_Research research = WV_Research.Instance;
            if (research == null) {
                hud.SetResearchSummary(null);
                return;
            }

            int clientId = LocalClientId;
            researchSummary.Clear();
            foreach (WV_TechDefinition definition in WV_TechTree.All) {
                if (research.GetPhase(clientId, definition.Tech) != WV_ResearchPhase.Researching)
                    continue;
                researchSummary.Append(researchSummary.Length == 0 ? "Researching " : ", ")
                    .Append(definition.DisplayName).Append(' ')
                    .Append(Mathf.FloorToInt(research.GetProgress(clientId, definition.Tech) * 100f)).Append('%');
            }
            hud.SetResearchSummary(researchSummary.Length > 0 ? researchSummary.ToString() : null);
        }

        void HandleRosterChanged() {
            PruneSelection();
            RefreshSelectionUI();
        }

        void HandleQueueChanged() => inspector.MarkDirty();

        void HandleCharacterAdded(GameCharacter character) {
            if (character != null && !characters.Contains(character))
                characters.Add(character);
        }

        void HandleCharacterRemoved(GameCharacter character) {
            characters.Remove(character);
            RemoveFromSelection(character);
        }

        void Update() {
            RegisterBroadcasts();

            // A troop dies where it stands rather than despawning, so death - not removal - is what
            // has to drop it from the selection and take its ring away.
            if (SelectionCount > 0) {
                int before = SelectionCount;
                PruneSelection();
                if (SelectionCount != before)
                    RefreshSelectionUI();
            }

            if (economyDirty) {
                economyDirty = false;
                hud.RefreshEconomy(LocalClientId);
                inspector.MarkDirty();
                StructureMenu.NotifyAvailabilityChanged();
            }

            if (researchDirty)
                RefreshResearch();
            if (Time.unscaledTime >= nextResearchSummaryTime)
                RefreshResearchSummary();

            // A building that was destroyed, despawned, or handed out of reach drops its panel here.
            if (!inspector.Tick() && armedOrder.HasValue && SelectionCount == 0)
                DisarmOrder(null);

            HandlePointer();
            HandleCommandKeys();
        }

        // --- Pointer ----------------------------------------------------------

        void HandlePointer() {
            Mouse mouse = Mouse.current;
            Camera camera = Camera.main;
            if (mouse == null || camera == null || !Application.isFocused) {
                CancelDrag();
                return;
            }

            // While the structure menu is placing a building, left click belongs to placement. A
            // click that lands on the HUD belongs to the HUD: without this, buying a unit or a troop
            // also cleared the selection behind the panel and re-ran a world raycast through it.
            bool inputBlocked = TopbarControls.IsMenuOpen
                || !ToolControls.IsCursorFree()
                || IsPointerOverHud();
            Vector2 screenPosition = mouse.position.ReadValue();

            if (mouse.leftButton.wasPressedThisFrame && !inputBlocked) {
                // An armed order spends the click on itself rather than starting a selection, so a
                // drag cannot begin underneath an order the player has already committed to.
                if (armedOrder.HasValue) {
                    ExecuteArmedOrder(camera, screenPosition);
                    return;
                }
                pointerDown = true;
                dragStart = screenPosition;
            }

            if (pointerDown && !dragging
                && (screenPosition - dragStart).sqrMagnitude >= DragThresholdPixels * DragThresholdPixels) {
                dragging = true;
                hud.SetSelectionBoxVisible(true);
            }

            if (dragging)
                hud.UpdateSelectionBox(dragStart, screenPosition);

            if (!mouse.leftButton.wasReleasedThisFrame || !pointerDown)
                return;

            if (dragging)
                SelectInBox(camera, dragStart, screenPosition, AdditiveHeld());
            else
                SelectAtPoint(camera, screenPosition, AdditiveHeld());

            CancelDrag();
        }

        // --- Command keys -----------------------------------------------------

        /// <summary>
        /// Stop, Hold Position, and the control groups.
        /// <para>
        /// The number row belongs to the toolbar and WASD/Space/Shift to the player's character, so
        /// control groups sit on F1-F4 and the two standing orders on keys nothing else claims.
        /// </para>
        /// </summary>
        void HandleCommandKeys() {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null || !Application.isFocused || TopbarControls.IsMenuOpen || !ToolControls.IsCursorFree())
                return;

            // Arming keys. M/V/T rather than the conventional A, which is the character's strafe.
            if (keyboard.mKey.wasPressedThisFrame)
                ArmOrder(WV_OrderType.Move);
            if (keyboard.vKey.wasPressedThisFrame)
                ArmOrder(WV_OrderType.AttackMove);
            if (keyboard.tKey.wasPressedThisFrame)
                ArmOrder(WV_OrderType.Attack);

            // Standing orders need no destination, so they are issued rather than armed.
            if (keyboard.xKey.wasPressedThisFrame)
                IssueImmediateOrder(WV_OrderType.Stop);
            if (keyboard.hKey.wasPressedThisFrame)
                IssueImmediateOrder(WV_OrderType.HoldPosition);

            // Escape backs out one step at a time: an armed order, then an open menu, then the
            // selected building.
            if (keyboard.escapeKey.wasPressedThisFrame) {
                if (armedOrder.HasValue)
                    DisarmOrder("Order cancelled");
                else if (inspector.IsMenuOpen)
                    inspector.CloseMenu();
                else if (inspector.HasSelection)
                    SelectBuilding(null);
            }

            for (int i = 0; i < ControlGroupCount; i++) {
                if (!GroupKey(keyboard, i).wasPressedThisFrame)
                    continue;
                if (GroupBindHeld())
                    BindControlGroup(i);
                else
                    RecallControlGroup(i);
            }
        }

        static ButtonControl GroupKey(Keyboard keyboard, int index) => index switch {
            0 => keyboard.f1Key,
            1 => keyboard.f2Key,
            2 => keyboard.f3Key,
            _ => keyboard.f4Key
        };

        /// <summary>Parks the current selection on a group key, replacing whatever was there.</summary>
        void BindControlGroup(int index) {
            if (SelectionCount == 0) {
                // Binding nothing is how a group is cleared, which is worth saying out loud rather
                // than leaving the player unsure whether the key registered.
                controlGroups[index] = null;
                hud.SetHint($"Group F{index + 1} cleared");
                return;
            }

            var group = new ControlGroup();
            group.Units.AddRange(selection);
            group.Troops.AddRange(troopSelection);
            controlGroups[index] = group;
            hud.SetHint($"Group F{index + 1} set - {SelectionCount} selected");
        }

        /// <summary>
        /// Selects a parked group. Anything that died since it was bound is dropped here rather than
        /// being tracked as it happens, so a group never has to be maintained to stay correct.
        /// </summary>
        void RecallControlGroup(int index) {
            ControlGroup group = controlGroups[index];
            if (group == null) {
                hud.SetHint($"Group F{index + 1} is empty");
                return;
            }

            ClearSelection();
            foreach (WV_Unit unit in group.Units) {
                if (unit != null && unit.IsSpawned && !unit.IsDead)
                    AddToSelection(unit);
            }
            foreach (GameCharacter troop in group.Troops) {
                if (IsCommandableTroop(troop))
                    AddToSelection(troop);
            }

            if (SelectionCount == 0) {
                controlGroups[index] = null;
                hud.SetHint($"Group F{index + 1} was wiped out");
                RefreshSelectionUI();
                return;
            }

            SelectBuilding(null);
            RefreshSelectionUI();
            hud.SetHint($"Group F{index + 1} - {SelectionCount} selected");
        }

        static bool IsPointerOverHud() =>
            EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();

        static bool AdditiveHeld() =>
            Keyboard.current != null
            && (Keyboard.current.leftShiftKey.isPressed || Keyboard.current.rightShiftKey.isPressed);

        /// <summary>Ctrl held over a group key means "set this group" rather than "recall it".</summary>
        static bool GroupBindHeld() =>
            Keyboard.current != null
            && (Keyboard.current.leftCtrlKey.isPressed || Keyboard.current.rightCtrlKey.isPressed);

        void CancelDrag() {
            pointerDown = false;
            dragging = false;
            hud.SetSelectionBoxVisible(false);
        }

        // --- Selection --------------------------------------------------------

        void SelectInBox(Camera camera, Vector2 start, Vector2 end, bool additive) {
            if (!additive)
                ClearSelection();

            Vector2 min = Vector2.Min(start, end);
            Vector2 max = Vector2.Max(start, end);

            WV_Unit.CollectOwnedBy(LocalClientId, ownedScratch);
            foreach (WV_Unit unit in ownedScratch) {
                if (SelectionCount >= maxSelection)
                    break;
                if (IsInsideBox(camera, unit.transform.position, min, max))
                    AddToSelection(unit);
            }

            foreach (GameCharacter character in characters) {
                if (SelectionCount >= maxSelection)
                    break;
                if (IsCommandableTroop(character) && IsInsideBox(camera, character.transform.position, min, max))
                    AddToSelection(character);
            }

            if (SelectionCount > 0) {
                SelectBuilding(null);
                RefreshSelectionUI();
                return;
            }

            // A box around no units is a box around buildings: the way to take a row of barracks at
            // once. Units win when both are inside, since that is what a drag usually means.
            CollectUsableStructures(structureScratch, structure => IsInsideBox(camera, structure.transform.position, min, max));
            if (structureScratch.Count > 0) {
                if (additive)
                    inspector.Add(structureScratch);
                else
                    inspector.SetSelection(structureScratch);
                if (inspector.Count > 1)
                    hud.SetHint($"{inspector.Count} buildings selected");
            } else if (!additive) {
                SelectBuilding(null);
            }
            RefreshSelectionUI();
        }

        /// <summary>Every live building the local commander may use that passes <paramref name="filter"/>.</summary>
        void CollectUsableStructures(List<StructureComponent> results, System.Predicate<StructureComponent> filter) {
            results.Clear();
            foreach (WV_Owned owned in WV_Owned.All) {
                if (results.Count >= maxSelection)
                    break;
                if (owned == null
                    || !owned.TryGetComponent(out StructureComponent structure)
                    || !inspector.CanUse(structure)
                    || !filter(structure))
                    continue;
                results.Add(structure);
            }
        }

        static bool IsInsideBox(Camera camera, Vector3 worldPosition, Vector2 min, Vector2 max) {
            Vector3 viewport = camera.WorldToViewportPoint(worldPosition);
            // Anything behind the camera projects to a mirrored point in front of it, so the
            // depth test has to come before the rectangle test.
            if (viewport.z <= 0f)
                return false;

            Vector2 screen = camera.WorldToScreenPoint(worldPosition);
            return screen.x >= min.x && screen.x <= max.x && screen.y >= min.y && screen.y <= max.y;
        }

        void SelectAtPoint(Camera camera, Vector2 screenPosition, bool additive) {
            Ray ray = camera.ScreenPointToRay(screenPosition);
            bool hitSomething = Physics.Raycast(
                ray, out RaycastHit hit, float.MaxValue, WV_Combat.TargetMask, QueryTriggerInteraction.Ignore);

            if (!hitSomething) {
                if (!additive) {
                    ClearSelection();
                    SelectBuilding(null);
                    RefreshSelectionUI();
                }
                return;
            }

            WV_Unit unit = hit.collider.GetComponentInParent<WV_Unit>();
            if (unit != null && unit.Owned != null && unit.Owned.IsOwnedBy(LocalClientId)) {
                bool selectAll = ConsumeDoubleClick(unit);
                if (!additive)
                    ClearSelection();
                if (selectAll)
                    SelectAllOfKind(unit.Kind);
                else
                    AddToSelection(unit);
                SelectBuilding(null);
                RefreshSelectionUI();
                return;
            }

            GameCharacter troop = hit.collider.GetComponentInParent<GameCharacter>();
            if (IsCommandableTroop(troop)) {
                bool selectAll = ConsumeDoubleClick(troop);
                if (!additive)
                    ClearSelection();
                if (selectAll)
                    SelectAllTroopsNamed(troop.DisplayName);
                else
                    AddToSelection(troop);
                SelectBuilding(null);
                RefreshSelectionUI();
                return;
            }

            // Any building the commander may use - their own or an ally's - can be selected, which
            // also opens its menu. Shift adds or removes it; a double click takes every building of
            // that kind on screen.
            StructureComponent structure = hit.collider.GetComponentInParent<StructureComponent>();
            if (inspector.CanUse(structure)) {
                bool selectAll = ConsumeDoubleClick(structure);
                ClearSelection();
                if (selectAll)
                    SelectAllStructuresLike(camera, structure);
                else if (additive)
                    inspector.Toggle(structure);
                else
                    SelectBuilding(structure);
                RefreshSelectionUI();
                return;
            }

            if (structure != null) {
                hud.SetHint($"You cannot use that {structure.DisplayName}");
                return;
            }

            if (!additive) {
                ClearSelection();
                SelectBuilding(null);
                RefreshSelectionUI();
            }
        }

        /// <summary>
        /// True when this click is the second on the same thing in quick succession, which means
        /// "select every one of these". Consuming the state here stops a third click from reading as
        /// another double click.
        /// </summary>
        bool ConsumeDoubleClick(Object clicked) {
            bool isDouble = lastClicked == clicked && Time.unscaledTime - lastClickTime <= DoubleClickSeconds;
            lastClicked = isDouble ? null : clicked;
            lastClickTime = Time.unscaledTime;
            return isDouble;
        }

        /// <summary>
        /// Selects every vehicle of this kind the commander owns. Deliberately not limited to what
        /// is on screen: the point of the gesture is to grab the whole armoured push, and a tank
        /// that happened to be behind the camera is still part of it.
        /// </summary>
        void SelectAllOfKind(WV_UnitKind kind) {
            WV_Unit.CollectOwnedBy(LocalClientId, ownedScratch);
            foreach (WV_Unit candidate in ownedScratch) {
                if (SelectionCount >= maxSelection)
                    break;
                if (candidate != null && candidate.Kind == kind)
                    AddToSelection(candidate);
            }
        }

        /// <summary>
        /// Selects every usable building of the clicked one's kind that is on screen, keeping the
        /// clicked one first so the panel describes it. Unlike units, off-screen buildings stay out:
        /// they are not going anywhere, and the gesture means "these ones here".
        /// </summary>
        void SelectAllStructuresLike(Camera camera, StructureComponent clicked) {
            CollectUsableStructures(structureScratch, candidate =>
                candidate != clicked
                && candidate.StructureID == clicked.StructureID
                && IsInsideBox(camera, candidate.transform.position, Vector2.zero, new Vector2(Screen.width, Screen.height)));
            structureScratch.Insert(0, clicked);
            inspector.SetSelection(structureScratch);
            hud.SetHint($"{inspector.Count} x {clicked.DisplayName} selected");
        }

        /// <summary>
        /// Selects every troop of this loadout. A troop's kind is not replicated as a field, so its
        /// display name - which the server sets from that kind - is what identifies it, the same
        /// string the selection summary already groups by.
        /// </summary>
        void SelectAllTroopsNamed(string displayName) {
            foreach (GameCharacter candidate in characters) {
                if (SelectionCount >= maxSelection)
                    break;
                if (IsCommandableTroop(candidate) && candidate.DisplayName == displayName)
                    AddToSelection(candidate);
            }
        }

        int SelectionCount => selection.Count + troopSelection.Count;

        /// <summary>
        /// True for a foot troop this client commands.
        /// <para>
        /// A troop's owner is recorded only on the server, so it is recognised here by the commander
        /// colour carried in the display half of its team - which the server derives from the same
        /// client id. Player characters are excluded: a commander drives their own avatar directly
        /// and should never be able to box-select it as a unit.
        /// </para>
        /// </summary>
        bool IsCommandableTroop(GameCharacter character) {
            if (character == null || character is LocalCharacter || character.IsDead || !character.IsSpawned)
                return false;

            int clientId = LocalClientId;
            if (clientId == WV_Owned.NoOwner)
                return false;

            TeamConfig team = character.Team;
            return team != null && team.displayTeam == WV_Rules.GetCommanderColor(clientId);
        }

        void AddToSelection(WV_Unit unit) {
            if (unit == null || unit.IsDead || selection.Contains(unit))
                return;
            unit.SelectedLocally = true;
            selection.Add(unit);
        }

        void AddToSelection(GameCharacter troop) {
            if (troop == null || troopSelection.Contains(troop))
                return;

            troopSelection.Add(troop);
            if (troopSelectionIndicator == null || troopIndicators.ContainsKey(troop))
                return;

            // The ring is an authored prefab instanced under the troop, not geometry built here.
            GameObject indicator = Instantiate(troopSelectionIndicator, troop.transform);
            indicator.transform.localPosition = Vector3.zero;
            indicator.transform.localRotation = Quaternion.identity;
            troopIndicators[troop] = indicator;
        }

        void RemoveFromSelection(GameCharacter troop) {
            if (troop == null)
                return;
            troopSelection.Remove(troop);
            ClearTroopIndicator(troop);
        }

        void ClearTroopIndicator(GameCharacter troop) {
            if (!troopIndicators.TryGetValue(troop, out GameObject indicator))
                return;
            troopIndicators.Remove(troop);
            if (indicator != null)
                Destroy(indicator);
        }

        void ClearSelection() {
            foreach (WV_Unit unit in selection) {
                if (unit != null)
                    unit.SelectedLocally = false;
            }
            selection.Clear();

            foreach (GameCharacter troop in troopSelection) {
                if (troop != null)
                    ClearTroopIndicator(troop);
            }
            troopSelection.Clear();
        }

        void RefreshSelectionUI() {
            // An order in hand with nothing left to give it to would sit primed forever and then
            // swallow the click that was meant to start a new selection.
            if (armedOrder.HasValue && SelectionCount == 0 && inspector.RallyTargets.Count == 0)
                DisarmOrder("Selection lost - order cancelled");
            hud.RefreshSelection(selection, troopSelection);
        }

        /// <summary>Drops units and troops that despawned or died while selected.</summary>
        void PruneSelection() {
            for (int i = selection.Count - 1; i >= 0; i--) {
                WV_Unit unit = selection[i];
                if (unit == null || unit.IsDead || !unit.IsSpawned)
                    selection.RemoveAt(i);
            }

            for (int i = troopSelection.Count - 1; i >= 0; i--) {
                GameCharacter troop = troopSelection[i];
                if (troop == null || troop.IsDead || !troop.IsSpawned) {
                    if (troop != null)
                        ClearTroopIndicator(troop);
                    troopSelection.RemoveAt(i);
                }
            }
        }

        /// <summary>Selects one building and opens its menu, or clears the building selection when passed null.</summary>
        void SelectBuilding(StructureComponent structure) {
            if (structure == null)
                inspector.Clear();
            else
                inspector.Select(structure);
        }

        /// <summary>The panel's Set Rally button: the next ground click moves the selected buildings' gather point.</summary>
        void ArmRallyPoint() {
            if (inspector.RallyTargets.Count == 0)
                return;
            ArmOrder(WV_OrderType.Move);
        }

        // --- Orders -----------------------------------------------------------

        /// <summary>
        /// Puts an order in hand, to be placed by the next left click. Rearming simply replaces what
        /// was held, and arming with nothing selected is refused out loud rather than leaving a
        /// primed cursor that will turn out to do nothing.
        /// </summary>
        public void ArmOrder(WV_OrderType orderType) {
            if (orderType is WV_OrderType.Stop or WV_OrderType.HoldPosition) {
                IssueImmediateOrder(orderType);
                return;
            }

            if (SelectionCount == 0 && inspector.RallyTargets.Count == 0) {
                hud.SetHint("Select something first");
                return;
            }

            armedOrder = orderType;
            hud.SetArmedOrder(orderType);
            hud.SetHint(orderType switch {
                WV_OrderType.Attack => "Attack: click a target",
                WV_OrderType.AttackMove => "Attack-move: click a destination",
                _ => SelectionCount == 0 ? "Rally: click a gather point" : "Move: click a destination"
            });
        }

        /// <summary>Issues an order that needs no destination, so it never waits for a click.</summary>
        public void IssueImmediateOrder(WV_OrderType orderType) {
            if (armedOrder.HasValue)
                DisarmOrder(null);
            SendOrder(orderType, Vector3.zero, null);
        }

        void DisarmOrder(string hint) {
            armedOrder = null;
            hud.SetArmedOrder(null);
            if (!string.IsNullOrEmpty(hint))
                hud.SetHint(hint);
        }

        /// <summary>
        /// Lands the armed order wherever the player clicked. A click that resolves to nothing usable
        /// keeps the order in hand rather than silently dropping it, so a misclick on the skybox
        /// costs a second click instead of the whole order.
        /// </summary>
        void ExecuteArmedOrder(Camera camera, Vector2 screenPosition) {
            if (!armedOrder.HasValue)
                return;

            WV_OrderType orderType = armedOrder.Value;
            Ray ray = camera.ScreenPointToRay(screenPosition);

            if (orderType == WV_OrderType.Attack) {
                if (!Physics.Raycast(ray, out RaycastHit entityHit, float.MaxValue, WV_Combat.TargetMask,
                        QueryTriggerInteraction.Ignore)) {
                    hud.SetHint("No target there - click an enemy");
                    return;
                }

                IEntity target = entityHit.collider.GetComponentInParent<IEntity>();
                if (target == null || !IsHostileToLocalPlayer(target)) {
                    hud.SetHint("Not a valid target - click a living, hostile enemy");
                    return;
                }

                DisarmOrder(null);
                SendOrder(WV_OrderType.Attack, Vector3.zero, ((Component)target).GetComponent<NetworkObject>());
                return;
            }

            if (!Physics.Raycast(ray, out RaycastHit groundHit, float.MaxValue, WV_Rules.OrderGroundMask,
                    QueryTriggerInteraction.Ignore)) {
                hud.SetHint("No ground there - click the map");
                return;
            }

            // With buildings selected and nothing else, a placed Move sets their gather point. That
            // is the only thing a move could sensibly mean for structures that cannot walk.
            IReadOnlyList<WV_ProductionBuilding> rallyBuildings = inspector.RallyTargets;
            if (rallyBuildings.Count > 0 && SelectionCount == 0) {
                DisarmOrder(null);
                foreach (WV_ProductionBuilding building in rallyBuildings) {
                    InstanceFinder.ClientManager.Broadcast(new WV_RallyPointRequest {
                        buildingObjectId = building.NetworkObject.ObjectId,
                        position = groundHit.point
                    });
                }
                hud.SetHint(rallyBuildings.Count > 1 ? $"Rally point set for {rallyBuildings.Count} buildings" : "Rally point set");
                return;
            }

            DisarmOrder(null);
            SendOrder(orderType, groundHit.point, null);
        }

        /// <summary>
        /// Whether the selection may be ordered to attack <paramref name="target"/>: the same rule the
        /// server's brains apply, so a living, hurtable enemy is accepted and a dead or invulnerable
        /// one is refused here rather than silently dropped there.
        /// </summary>
        bool IsHostileToLocalPlayer(IEntity target) {
            // Compare against something the player actually commands rather than against the player's
            // own character, so the answer matches what the ordered units will do on the server.
            foreach (WV_Unit unit in selection) {
                if (unit != null)
                    return WV_Combat.IsValidTarget(target, unit.Team);
            }
            foreach (GameCharacter troop in troopSelection) {
                if (troop != null)
                    return WV_Combat.IsValidTarget(target, troop.Team);
            }
            return false;
        }

        void SendOrder(WV_OrderType orderType, Vector3 position, NetworkObject target) {
            if (SelectionCount == 0 || InstanceFinder.ClientManager == null)
                return;

            orderScratch.Clear();
            foreach (WV_Unit unit in selection) {
                if (unit != null && unit.IsSpawned && !unit.IsDead)
                    orderScratch.Add(unit.NetworkObject.ObjectId);
            }
            foreach (GameCharacter troop in troopSelection) {
                if (troop != null && troop.IsSpawned && !troop.IsDead)
                    orderScratch.Add(troop.NetworkObject.ObjectId);
            }
            if (orderScratch.Count == 0)
                return;

            InstanceFinder.ClientManager.Broadcast(new WV_UnitOrderRequest {
                unitObjectIds = orderScratch.ToArray(),
                orderType = (byte)orderType,
                position = position,
                targetObjectId = target != null ? target.ObjectId : 0
            });

            hud.SetHint(orderType switch {
                WV_OrderType.Attack => "Attacking",
                WV_OrderType.AttackMove => "Advancing under fire",
                WV_OrderType.Stop => "Holding here",
                WV_OrderType.HoldPosition => "Holding position",
                _ => "Moving out"
            });
        }
    }
}
