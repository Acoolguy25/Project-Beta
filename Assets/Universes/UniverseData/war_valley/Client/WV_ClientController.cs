using System.Collections.Generic;
using FishNet;
using FishNet.Object;
using RyanAssets.Characters.Shared;
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

        readonly List<WV_Unit> selection = new();
        readonly List<WV_Unit> ownedScratch = new();
        readonly List<int> orderScratch = new();

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
        WV_ProductionBuilding selectedBuilding;
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
        bool productionResultsRegistered;

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
            hud.SetHint(
                "Drag to select • Right click to move or attack • Ctrl+right click to attack-move\n"
                + "X stop • H hold • Ctrl+F1-F4 set group • F1-F4 recall • Double click selects all of a kind");
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
            WV_Unit.RosterChanged += HandleRosterChanged;
            WV_ProductionBuilding.QueueChanged += HandleQueueChanged;
            GameCharacter.GameCharacterAdded += HandleCharacterAdded;
            GameCharacter.GameCharacterRemoved += HandleCharacterRemoved;
        }

        void OnDisable() {
            WV_Economy.LedgerChanged -= MarkEconomyDirty;
            WV_Unit.RosterChanged -= HandleRosterChanged;
            WV_ProductionBuilding.QueueChanged -= HandleQueueChanged;
            GameCharacter.GameCharacterAdded -= HandleCharacterAdded;
            GameCharacter.GameCharacterRemoved -= HandleCharacterRemoved;
            if (productionResultsRegistered && InstanceFinder.ClientManager != null) {
                InstanceFinder.ClientManager.UnregisterBroadcast<WV_ProductionResult>(HandleProductionResult);
                productionResultsRegistered = false;
            }
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

        void OnDestroy() {
            UnbindCommandMenu();
            if (hud != null)
                Destroy(hud.gameObject);
        }

        void MarkEconomyDirty() => economyDirty = true;

        void HandleRosterChanged() {
            PruneSelection();
            RefreshSelectionUI();
        }

        void HandleQueueChanged() {
            if (selectedBuilding != null)
                hud.RefreshProductionQueue(selectedBuilding);
        }

        void HandleCharacterAdded(GameCharacter character) {
            if (character != null && !characters.Contains(character))
                characters.Add(character);
        }

        void HandleCharacterRemoved(GameCharacter character) {
            characters.Remove(character);
            RemoveFromSelection(character);
        }

        void Update() {
            if (!productionResultsRegistered
                && InstanceFinder.ClientManager != null
                && InstanceFinder.ClientManager.Started) {
                productionResultsRegistered = true;
                InstanceFinder.ClientManager.RegisterBroadcast<WV_ProductionResult>(HandleProductionResult);
            }

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
                hud.ProductionMenu?.RefreshAffordability();
            }

            if (selectedBuilding != null)
                hud.RefreshProductionQueue(selectedBuilding);

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

            // The right button is the camera's (LookPC), so nothing here may consume it.

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

            // Escape backs out of an armed order without issuing it.
            if (keyboard.escapeKey.wasPressedThisFrame && armedOrder.HasValue)
                DisarmOrder("Order cancelled");

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

            if (SelectionCount > 0)
                SelectBuilding(null);
            RefreshSelectionUI();
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

            WV_ProductionBuilding building = hit.collider.GetComponentInParent<WV_ProductionBuilding>();
            if (building != null && building.Owned != null && building.Owned.IsOwnedBy(LocalClientId)) {
                ClearSelection();
                RefreshSelectionUI();
                SelectBuilding(building);
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
            if (armedOrder.HasValue && SelectionCount == 0 && selectedBuilding == null)
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

            if (selectedBuilding != null
                && (!selectedBuilding.IsSpawned
                    || selectedBuilding.Owned == null
                    || !selectedBuilding.Owned.IsOwnedBy(LocalClientId)
                    || IsDeadBuilding(selectedBuilding)))
                SelectBuilding(null);
        }

        /// <summary>A destroyed building keeps its panel open until this notices it is rubble.</summary>
        static bool IsDeadBuilding(WV_ProductionBuilding building) =>
            building.TryGetComponent(out StructureComponent structure) && structure.IsDead;

        void SelectBuilding(WV_ProductionBuilding building) {
            selectedBuilding = building;
            hud.ShowProduction(building, LocalClientId);
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

            if (SelectionCount == 0 && selectedBuilding == null) {
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
                    hud.SetHint("Not an enemy - click a hostile target");
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

            // With a building selected and nothing else, a placed Move sets its gather point. That
            // is the only thing a move could sensibly mean for a structure that cannot walk.
            if (selectedBuilding != null && SelectionCount == 0) {
                DisarmOrder(null);
                InstanceFinder.ClientManager.Broadcast(new WV_RallyPointRequest {
                    buildingObjectId = selectedBuilding.NetworkObject.ObjectId,
                    position = groundHit.point
                });
                hud.SetHint("Rally point set");
                return;
            }

            DisarmOrder(null);
            SendOrder(orderType, groundHit.point, null);
        }

        bool IsHostileToLocalPlayer(IEntity target) {
            // Compare against something the player actually commands rather than against the player's
            // own character, so the answer matches what the ordered units will do on the server.
            foreach (WV_Unit unit in selection) {
                if (unit != null)
                    return WV_Combat.AreEnemies(unit.Team, target.Team);
            }
            foreach (GameCharacter troop in troopSelection) {
                if (troop != null)
                    return WV_Combat.AreEnemies(troop.Team, target.Team);
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
