using System.Collections.Generic;
using FishNet;
using FishNet.Object;
using FishNet.Transporting;
using RyanAssets.Characters.Shared;
using RyanAssets.Client.ClientUI.Build;
using RyanAssets.Core;
using RyanAssets.Input;
using RyanAssets.Shared.Declarations;
using RyanAssets.Shared.Component;
using RyanAssets.DataService;
using RyanAssets.Client.ClientUI.Command;
using RyanAssets.UI.Hover;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using Universes.UniverseData.war_valley.Shared;

namespace Universes.UniverseData.war_valley.Client {
    /// <summary>
    /// War Valley's commander input: box selection, movement and attack orders, rally points, and the
    /// production panel.
    /// <para>
    /// This component owns input and the HUD's lifetime, and hands each concern to a focused helper:
    /// <see cref="WV_CommanderSelection"/> (what is selected, and the troop roster it is picked from),
    /// <see cref="WV_ControlGroups"/> (F1-F4), <see cref="WV_StructureInspector"/> (everything about
    /// selected buildings), <see cref="WV_ResearchPresenter"/>, <see cref="WV_DonationsPresenter"/>,
    /// <see cref="WV_BaseAlerts"/>, and the game feel: <see cref="WV_BattleFeedback"/> (hits, deaths,
    /// explosions), <see cref="WV_OrderFeedback"/> (order pings), and the HUD's
    /// <see cref="WV_HealthBarLayer"/> and <see cref="WV_WaveDrama"/>. What stays here is turning keys
    /// and clicks into selections and into the order, rally, and sell requests the server validates
    /// again.
    /// </para>
    /// <para>
    /// The keys deliberately avoid the number row and WASD/Space/Shift, which belong to the toolbar
    /// and to the player's own character.
    /// </para>
    /// <para>
    /// Everything is selected with the left button alone; the right button belongs to the camera.
    /// Clicking a building selects it and opens its menu - the build menu of a barracks, airfield, or
    /// helipad, the research menu of a research station. Shift-click adds or removes buildings, a
    /// double click takes every building of that kind on screen, and a drag box picks up units and
    /// troops, or buildings when it holds no units.
    /// </para>
    /// </summary>
    public sealed class WV_ClientController : MonoBehaviour {
        /// <summary>Pixels of travel before a click becomes a box drag.</summary>
        const float DragThresholdPixels = 8f;

        /// <summary>The standing controls reminder on the hint line. It is game help, and the player can turn it off.</summary>
        const string ControlsHint =
            "Drag to select  ·  Click ground to advance, an enemy to attack  ·  Hover a button for its key";

        /// <summary>Seconds within which a second click on the same unit means "select all of these".</summary>
        const float DoubleClickSeconds = 0.3f;

        /// <summary>How long a first click on Sell waits for the confirming second one.</summary>
        const float SellConfirmSeconds = 3f;

        [Header("Authored References")]
        [Tooltip("The War Valley HUD prefab. Instantiated once and bound to live match state.")]
        [SerializeField] WV_HUD hudPrefab;
        [Tooltip("Ring shown under a troop this client has selected. Leave unset to select troops " +
                 "without a ring rather than to disable troop selection.")]
        [SerializeField] GameObject troopSelectionIndicator;

        [Header("Selection")]
        [Tooltip("Maximum units a single box selection can pick up.")]
        [SerializeField, Min(1)] int maxSelection = 120;

        [Header("Game Feel")]
        [Tooltip("Hit flashes, death bursts, explosions, damage numbers, and construction dust.")]
        [SerializeField] WV_BattleFeedbackTuning battleFeedback = new();
        [Tooltip("Ground pings, the acknowledgement blip, and selection ring pulses for orders.")]
        [SerializeField] WV_OrderFeedbackTuning orderFeedback = new();

        [Header("Camera")]
        [Tooltip("How far the commander's camera may pull back over the valley.")]
        [SerializeField] WV_CommanderCameraTuning commanderCamera = new();

        readonly List<int> orderScratch = new();
        readonly List<StructureComponent> structureScratch = new();
        readonly List<RaycastResult> uiHits = new();

        WV_HUD hud;
        WV_CommanderSelection selection;
        WV_ControlGroups controlGroups;
        WV_StructureInspector inspector;
        WV_ResearchPresenter research;
        WV_DonationsPresenter donations;
        WV_DoctrinePresenter doctrines;
        WV_BaseAlerts alerts;
        WV_BattleFeedback battle;
        WV_OrderFeedback orders;
        readonly WV_CameraPostProcessing postProcessing = new();
        WV_CommanderCamera cameraRange;
        PointerEventData uiPointer;
        EventSystem uiPointerSystem;
        float nextSummaryTime;
        /// <summary>Until when a Sell click is waiting for its confirming second click; 0 when none is.</summary>
        float sellConfirmUntil;
        /// <summary>The selection a pending sale was priced for, so a changed selection is never sold by mistake.</summary>
        int sellConfirmSelection;
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

        int LocalClientId =>
            InstanceFinder.ClientManager != null && InstanceFinder.ClientManager.Connection.IsValid
                ? InstanceFinder.ClientManager.Connection.ClientId
                : WV_Owned.NoOwner;

        int SelectionCount => selection.Count;

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
            selection = new WV_CommanderSelection(
                () => LocalClientId, () => hud != null ? hud.TroopRangeIndicator : null,
                troopSelectionIndicator, maxSelection);
            controlGroups = new WV_ControlGroups(selection);
            BindCommandMenu();
            inspector = new WV_StructureInspector(hud, () => LocalClientId, selection.CountOwnTroops, ArmRallyPoint);
            research = new WV_ResearchPresenter(hud, () => LocalClientId, inspector.MarkDirty);
            donations = new WV_DonationsPresenter(hud, () => LocalClientId);
            doctrines = new WV_DoctrinePresenter(hud);
            alerts = new WV_BaseAlerts(hud.SetHint);
            battle = new WV_BattleFeedback(battleFeedback, selection);
            orders = new WV_OrderFeedback(orderFeedback, selection);
            cameraRange = new WV_CommanderCamera(commanderCamera);
            BindBattlefieldHud();
            hud.SellClicked += RequestSell;
            hud.ResearchClicked += ToggleResearch;
            GameHelp.Changed += HandleGameHelpChanged;
            hud.SetHint(GameHelp.Enabled ? ControlsHint : string.Empty);
        }

        /// <summary>The controls reminder comes and goes with the player's Game Help setting.</summary>
        void HandleGameHelpChanged(bool enabled) => hud.SetHint(enabled ? ControlsHint : string.Empty);

        /// <summary>
        /// The HUD's Research button: research has a menu of its own, reachable without first finding
        /// and selecting a research station. The menu says so when the commander has none.
        /// </summary>
        void ToggleResearch() => inspector.ToggleResearchMenu();

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
            menu.ClearRequested += ClearArmySelection;
            menu.SelectRequested += SelectAllOwned;
            menu.GroupRecalled += RecallControlGroup;
            menu.GroupBound += BindControlGroup;
            menu.SetHasSelection(false);
        }

        /// <summary>
        /// Connects the HUD's health bars to the selection and its wave staging to the hint line. A HUD
        /// authored before they existed is reported rather than silently left without them.
        /// </summary>
        void BindBattlefieldHud() {
            if (hud.HealthBars != null)
                hud.HealthBars.Bind(IsSelected, IsOwnEntity, () => selection.Count, IsPointerOverHud);
            if (hud.WaveDrama != null)
                hud.WaveDrama.Bind(hud.SetHint);
            if (hud.HealthBars == null || hud.WaveDrama == null)
                Debug.LogWarning(
                    $"{nameof(WV_ClientController)}: the HUD prefab predates the health bars and wave " +
                    "banner. Re-run Ryan/War Valley/Rebuild HUD to author them.", this);
        }

        bool IsSelected(EntityBase entity) => entity switch {
            GameCharacter troop => selection.Contains(troop),
            WV_Unit unit => selection.Contains(unit),
            _ => false
        };

        /// <summary>True for a troop, vehicle or building the local commander owns.</summary>
        bool IsOwnEntity(EntityBase entity) => entity switch {
            GameCharacter troop => selection.IsCommandableTroop(troop),
            WV_Unit unit => unit.Owned != null && unit.Owned.IsOwnedBy(LocalClientId),
            _ => entity.TryGetComponent(out WV_Owned owned) && owned.IsOwnedBy(LocalClientId)
        };

        void UnbindCommandMenu() {
            WV_CommandMenu menu = hud != null ? hud.CommandMenu : null;
            if (menu == null)
                return;
            menu.OrderArmed -= ArmOrder;
            menu.ClearRequested -= ClearArmySelection;
            menu.SelectRequested -= SelectAllOwned;
            menu.GroupRecalled -= RecallControlGroup;
            menu.GroupBound -= BindControlGroup;
        }

        void OnEnable() {
            WV_Economy.LedgerChanged += MarkEconomyDirty;
            WV_Research.Changed += MarkResearchDirty;
            WV_Unit.RosterChanged += HandleRosterChanged;
            WV_ProductionBuilding.QueueChanged += HandleQueueChanged;
            selection.Enable();
            battle.Enable();

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
            selection?.Disable();
            battle?.Disable();
            postProcessing.Release();
            cameraRange?.Release();
            inspector?.Clear();

            if (StructureMenu.AvailabilityProvider == GetStructureAvailability)
                StructureMenu.AvailabilityProvider = null;
            if (StructureMenu.AffordabilityProvider == CanAffordStructure)
                StructureMenu.AffordabilityProvider = null;
            StructureMenu.NotifyAvailabilityChanged();

            if (broadcastsRegistered && InstanceFinder.ClientManager != null) {
                InstanceFinder.ClientManager.UnregisterBroadcast<WV_ProductionResult>(HandleProductionResult);
                InstanceFinder.ClientManager.UnregisterBroadcast<WV_ResearchResult>(HandleResearchResult);
                InstanceFinder.ClientManager.UnregisterBroadcast<WV_Notice>(HandleNotice);
                InstanceFinder.ClientManager.UnregisterBroadcast<WV_WaveStarted>(HandleWaveStarted);
                InstanceFinder.ClientManager.UnregisterBroadcast<WV_RoundEnded>(HandleRoundEnded);
                InstanceFinder.ClientManager.UnregisterBroadcast<WV_DoctrineOffer>(doctrines.HandleOffer);
                InstanceFinder.ClientManager.UnregisterBroadcast<WV_DoctrineState>(doctrines.HandleState);
                InstanceFinder.ClientManager.UnregisterBroadcast<WV_CampaignStatus>(doctrines.HandleCampaign);
                InstanceFinder.ClientManager.UnregisterBroadcast<WV_TierBallot>(doctrines.HandleBallot);
                InstanceFinder.ClientManager.UnregisterBroadcast<WV_DoctrineEffect>(doctrines.HandleEffect);
                InstanceFinder.ClientManager.UnregisterBroadcast<WV_RoundBriefing>(HandleBriefing);
            }
            broadcastsRegistered = false;
        }

        void OnDestroy() {
            selection?.ReleaseRoster();
            UnbindCommandMenu();
            donations?.Dispose();
            doctrines?.Dispose();
            GameHelp.Changed -= HandleGameHelpChanged;
            if (hud != null) {
                hud.SellClicked -= RequestSell;
                hud.ResearchClicked -= ToggleResearch;
            }
            inspector?.Dispose();
            if (hud != null)
                Destroy(hud.gameObject);
        }

        void RegisterBroadcasts() {
            if (broadcastsRegistered || InstanceFinder.ClientManager == null || !InstanceFinder.ClientManager.Started)
                return;
            broadcastsRegistered = true;
            InstanceFinder.ClientManager.RegisterBroadcast<WV_ProductionResult>(HandleProductionResult);
            InstanceFinder.ClientManager.RegisterBroadcast<WV_ResearchResult>(HandleResearchResult);
            InstanceFinder.ClientManager.RegisterBroadcast<WV_Notice>(HandleNotice);
            InstanceFinder.ClientManager.RegisterBroadcast<WV_WaveStarted>(HandleWaveStarted);
            InstanceFinder.ClientManager.RegisterBroadcast<WV_RoundEnded>(HandleRoundEnded);
            InstanceFinder.ClientManager.RegisterBroadcast<WV_DoctrineOffer>(doctrines.HandleOffer);
            InstanceFinder.ClientManager.RegisterBroadcast<WV_DoctrineState>(doctrines.HandleState);
            InstanceFinder.ClientManager.RegisterBroadcast<WV_CampaignStatus>(doctrines.HandleCampaign);
            InstanceFinder.ClientManager.RegisterBroadcast<WV_TierBallot>(doctrines.HandleBallot);
            InstanceFinder.ClientManager.RegisterBroadcast<WV_DoctrineEffect>(doctrines.HandleEffect);
            InstanceFinder.ClientManager.RegisterBroadcast<WV_RoundBriefing>(HandleBriefing);
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

        void HandleWaveStarted(WV_WaveStarted wave, Channel channel) {
            alerts.Muted = false;
            if (hud.WaveDrama != null)
                hud.WaveDrama.ShowWave(wave);
        }

        void HandleRoundEnded(WV_RoundEnded result, Channel channel) {
            // A base knocked about under the result banner is not news.
            alerts.Muted = true;
            if (hud.WaveDrama != null)
                hud.WaveDrama.ShowRoundEnd(result);
            if (hud.CampaignReveal != null)
                hud.CampaignReveal.SetResult(result);
        }

        void HandleBriefing(WV_RoundBriefing briefing, Channel channel) {
            alerts.Muted = false;
            doctrines.HandleBriefing(briefing, channel);
        }

        void MarkEconomyDirty() => economyDirty = true;

        void MarkResearchDirty() => researchDirty = true;

        void HandleRosterChanged() {
            selection.Prune();
            RefreshSelectionUI();
        }

        void HandleQueueChanged() => inspector.MarkDirty();

        void Update() {
            RegisterBroadcasts();

            // A troop dies where it stands rather than despawning, so death - not removal - is what
            // has to drop it from the selection and take its ring away.
            if (SelectionCount > 0 && selection.Prune())
                RefreshSelectionUI();

            if (economyDirty) {
                economyDirty = false;
                hud.RefreshEconomy(LocalClientId);
                inspector.MarkDirty();
                donations.RefreshIfOpen();
                StructureMenu.NotifyAvailabilityChanged();
            }

            if (researchDirty) {
                researchDirty = false;
                research.Refresh();
            }
            if (Time.unscaledTime >= nextSummaryTime) {
                nextSummaryTime = Time.unscaledTime + WV_ResearchPresenter.SummaryInterval;
                research.RefreshSummary();
                RefreshForces();
                donations.RefreshIfOpen();
                // The selection's health figure follows fights and doctrines, not only selection changes.
                if (SelectionCount > 0)
                    hud.RefreshSelection(selection.Units, selection.Troops);
            }

            if (sellConfirmUntil > 0f && Time.unscaledTime >= sellConfirmUntil)
                CancelSellConfirm();

            // A building that was destroyed, despawned, or handed out of reach drops its panel here.
            if (!inspector.Tick() && armedOrder.HasValue && SelectionCount == 0)
                DisarmOrder(null);

            // Keys first: an order armed by a key in the same frame as the click that places it must
            // already be in hand when the click is read, or the click starts a selection instead and
            // the order is lost.
            HandleCommandKeys();
            HandlePointer();
            alerts.Tick(LocalClientId);
            postProcessing.Tick();
            cameraRange.Tick();
        }

        // --- Build menu gates -------------------------------------------------

        StructureAvailability GetStructureAvailability(StructureComponent structure) {
            if (WV_Limits.GetCategory(structure) == WV_ForceCategory.Building
                && WV_Limits.CountBuildings(LocalClientId) >= WV_Limits.MaxBuildings)
                return StructureAvailability.Locked(WV_Limits.GetLimitMessage(WV_ForceCategory.Building));

            WV_Tech required = WV_TechTree.GetRequirement(structure.StructureID);
            if (required == WV_Tech.None)
                return StructureAvailability.Available;

            WV_Research ledger = WV_Research.Instance;
            return ledger != null && ledger.IsResearched(LocalClientId, required)
                ? StructureAvailability.Available
                : StructureAvailability.Locked($"Research {WV_TechTree.GetDisplayName(required)} at a research station");
        }

        bool CanAffordStructure(StructureComponent structure) {
            WV_Economy economy = WV_Economy.Instance;
            return economy == null || economy.CanAfford(LocalClientId, (long)structure.Cost);
        }

        // --- Limits -----------------------------------------------------------

        /// <summary>The funds card's line showing how much of each per-player limit is in use.</summary>
        void RefreshForces() {
            int clientId = LocalClientId;
            if (clientId == WV_Owned.NoOwner)
                return;

            int troops = selection.CountOwnTroops();
            RefreshForce(clientId, WV_ForceCategory.Soldier, troops);
            RefreshForce(clientId, WV_ForceCategory.Vehicle, troops);
            RefreshForce(clientId, WV_ForceCategory.Aircraft, troops);
            RefreshForce(clientId, WV_ForceCategory.Building, troops);
        }

        void RefreshForce(int clientId, WV_ForceCategory category, int troops) =>
            hud.SetForce(category, WV_Limits.CountUsed(clientId, category, troops), WV_Limits.GetLimit(category));

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
            Vector2 screenPosition = mouse.position.ReadValue();
            bool inputBlocked = TopbarControls.IsMenuOpen
                || !ToolControls.IsCursorFree()
                || (mouse.leftButton.wasPressedThisFrame && IsPointerOverHud(screenPosition));

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

        void CancelDrag() {
            pointerDown = false;
            dragging = false;
            hud.SetSelectionBoxVisible(false);
        }

        /// <summary>
        /// True when HUD - any UI that takes pointer input - is under <paramref name="screenPosition"/>
        /// this frame. Asked of the event system directly rather than through
        /// IsPointerOverGameObject, which answers for wherever the pointer was when the UI last
        /// processed input: an order placed just after the cursor left a HUD button - arm, then
        /// click the ground straight away - read as a click on the button and was dropped.
        /// </summary>
        bool IsPointerOverHud(Vector2 screenPosition) {
            EventSystem system = EventSystem.current;
            if (system == null)
                return false;
            if (uiPointer == null || uiPointerSystem != system) {
                uiPointerSystem = system;
                uiPointer = new PointerEventData(system);
            }
            uiPointer.Reset();
            uiPointer.position = screenPosition;
            uiHits.Clear();
            system.RaycastAll(uiPointer, uiHits);
            return uiHits.Count > 0;
        }

        // --- Command keys -----------------------------------------------------

        /// <summary>Stop, Hold Position, quick selection, selling, and the control groups.</summary>
        void HandleCommandKeys() {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null || !Application.isFocused || TopbarControls.IsMenuOpen || IsTyping())
                return;

            if (keyboard.cKey.wasPressedThisFrame) {
                ClearArmySelection();
                return;
            }

            if (!ToolControls.IsCursorFree())
                return;

            if (keyboard.deleteKey.wasPressedThisFrame)
                RequestSell();

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
            if (keyboard.fKey.wasPressedThisFrame)
                IssueImmediateOrder(WV_OrderType.Freeze);

            // Quick selection: grab the army, part of it, or every factory without hunting for them.
            if (keyboard.zKey.wasPressedThisFrame)
                SelectAllOwned(units: true, troops: true);
            if (keyboard.uKey.wasPressedThisFrame)
                SelectAllOwned(units: true, troops: false);
            if (keyboard.iKey.wasPressedThisFrame)
                SelectAllOwned(units: false, troops: true);
            if (keyboard.gKey.wasPressedThisFrame)
                SelectProductionBuildings();

            // Escape backs out one step at a time: an armed order, then an open menu, then the
            // selected building.
            if (keyboard.escapeKey.wasPressedThisFrame) {
                if (armedOrder.HasValue)
                    DisarmOrder("Order cancelled");
                else if (sellConfirmUntil > 0f)
                    CancelSellConfirm();
                else if (donations.IsOpen)
                    donations.Close();
                else if (inspector.IsMenuOpen)
                    inspector.CloseMenu();
                else if (inspector.HasSelection)
                    SelectBuilding(null);
                else if (SelectionCount > 0)
                    ClearArmySelection();
            }

            for (int i = 0; i < WV_ControlGroups.Count; i++) {
                if (!WV_ControlGroups.Key(keyboard, i).wasPressedThisFrame)
                    continue;
                if (GroupBindHeld())
                    BindControlGroup(i);
                else if (GroupAddHeld())
                    hud.SetHint(controlGroups.Add(i));
                else
                    RecallControlGroup(i);
            }
        }

        /// <summary>
        /// True while a text field - the donation amount, chat - has the keyboard, so typing a
        /// number never also fires a hotkey.
        /// </summary>
        static bool IsTyping() {
            GameObject focused = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            return focused != null
                && focused.TryGetComponent(out TMPro.TMP_InputField field)
                && field.isFocused;
        }

        static bool AdditiveHeld() =>
            Keyboard.current != null
            && (Keyboard.current.leftShiftKey.isPressed || Keyboard.current.rightShiftKey.isPressed);

        /// <summary>Ctrl held over a group key means "set this group" rather than "recall it".</summary>
        static bool GroupBindHeld() =>
            Keyboard.current != null
            && (Keyboard.current.leftCtrlKey.isPressed || Keyboard.current.rightCtrlKey.isPressed);

        /// <summary>Alt held over a group key means "add to this group".</summary>
        static bool GroupAddHeld() =>
            Keyboard.current != null
            && (Keyboard.current.leftAltKey.isPressed || Keyboard.current.rightAltKey.isPressed);

        // --- Control groups and quick selection -------------------------------

        void BindControlGroup(int index) => hud.SetHint(controlGroups.Bind(index));

        void RecallControlGroup(int index) {
            string hint = controlGroups.Recall(index, out bool recalled);
            if (recalled)
                SelectBuilding(null);
            if (recalled || SelectionCount == 0)
                RefreshSelectionUI();
            hud.SetHint(hint);
        }

        /// <summary>
        /// Selects everything of the requested sort this commander owns. This is the card's answer to
        /// box selection: a player who cannot find their army on screen can still take hold of it.
        /// </summary>
        void SelectAllOwned(bool units, bool troops) {
            selection.Clear();
            selection.AddAllOwned(units, troops);
            if (SelectionCount > 0)
                SelectBuilding(null);
            RefreshSelectionUI();
            hud.SetHint(SelectionCount > 0 ? $"{SelectionCount} selected" : "Nothing to select");
        }

        void ClearArmySelection() {
            CancelDrag();
            selection.Clear();
            DisarmOrder(null);
            RefreshSelectionUI();
            hud.SetHint("Selection cleared");
        }

        /// <summary>
        /// G: every finished building this commander owns that trains something, so a whole row of
        /// barracks and hangars can share one rally point or one queue click.
        /// </summary>
        void SelectProductionBuildings() {
            int clientId = LocalClientId;
            CollectUsableStructures(structureScratch, structure =>
                structure.TryGetComponent(out WV_ProductionBuilding _)
                && structure.TryGetComponent(out WV_Owned owned) && owned.IsOwnedBy(clientId)
                && (!structure.TryGetComponent(out WV_Constructable site) || site.IsOperational));
            if (structureScratch.Count == 0) {
                hud.SetHint("You have no finished production buildings");
                return;
            }
            selection.Clear();
            inspector.SetSelection(structureScratch);
            RefreshSelectionUI();
            hud.SetHint($"{structureScratch.Count} production buildings selected");
        }

        // --- Selection --------------------------------------------------------

        void SelectInBox(Camera camera, Vector2 start, Vector2 end, bool additive) {
            if (!additive)
                selection.Clear();

            Vector2 min = Vector2.Min(start, end);
            Vector2 max = Vector2.Max(start, end);
            Rect box = Rect.MinMaxRect(min.x, min.y, max.x, max.y);
            selection.AddInBox(camera, box);

            if (SelectionCount > 0) {
                SelectBuilding(null);
                RefreshSelectionUI();
                return;
            }

            // A box around no units is a box around buildings: the way to take a row of barracks at
            // once. Units win when both are inside, since that is what a drag usually means.
            CollectUsableStructures(structureScratch, structure => WV_ScreenPicking.TouchesBox(camera, structure, box));
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

        void SelectAtPoint(Camera camera, Vector2 screenPosition, bool additive) {
            Ray ray = camera.ScreenPointToRay(screenPosition);
            // Triggers count: a gate standing open turns its collider into one so players can walk
            // through, and its owner still has to be able to click it to close it again.
            bool hitSomething = Physics.Raycast(
                ray, out RaycastHit hit, float.MaxValue, WV_Combat.TargetMask, QueryTriggerInteraction.Collide);

            if (!hitSomething) {
                ClickEmptyGround(camera, screenPosition, additive);
                return;
            }

            WV_Unit unit = hit.collider.GetComponentInParent<WV_Unit>();
            if (unit != null && unit.Owned != null && unit.Owned.IsOwnedBy(LocalClientId)) {
                bool selectAll = ConsumeDoubleClick(unit);
                if (!additive)
                    selection.Clear();
                if (selectAll)
                    selection.AddAllOfKind(unit.Kind);
                else
                    selection.Add(unit);
                SelectBuilding(null);
                RefreshSelectionUI();
                return;
            }

            GameCharacter troop = hit.collider.GetComponentInParent<GameCharacter>();
            if (selection.IsCommandableTroop(troop)) {
                bool selectAll = ConsumeDoubleClick(troop);
                if (!additive)
                    selection.Clear();
                if (selectAll)
                    selection.AddAllTroopsNamed(troop.DisplayName);
                else
                    selection.Add(troop);
                SelectBuilding(null);
                RefreshSelectionUI();
                return;
            }

            IEntity enemy = hit.collider.GetComponentInParent<IEntity>();
            if (SelectionCount > 0 && !additive && IsHostileToSelection(enemy)) {
                SendOrder(WV_OrderType.Attack, Vector3.zero, ((Component)enemy).GetComponent<NetworkObject>());
                return;
            }

            // Any building the commander may use - their own or an ally's - can be selected, which
            // also opens its menu. Shift adds or removes it; a double click takes every building of
            // that kind on screen.
            StructureComponent structure = hit.collider.GetComponentInParent<StructureComponent>();
            if (inspector.CanUse(structure)) {
                bool selectAll = ConsumeDoubleClick(structure);
                selection.Clear();
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

            ClickEmptyGround(camera, screenPosition, additive);
        }

        /// <summary>A plain click on nothing selectable: advance the selection there, or drop it.</summary>
        void ClickEmptyGround(Camera camera, Vector2 screenPosition, bool additive) {
            if (additive)
                return;
            if (SelectionCount > 0 && AdvanceAtPoint(camera, screenPosition))
                return;
            selection.Clear();
            SelectBuilding(null);
            RefreshSelectionUI();
        }

        bool AdvanceAtPoint(Camera camera, Vector2 screenPosition) {
            Ray ray = camera.ScreenPointToRay(screenPosition);
            if (!Physics.Raycast(ray, out RaycastHit hit, float.MaxValue, WV_Rules.OrderGroundMask,
                    QueryTriggerInteraction.Ignore))
                return false;
            SendOrder(WV_OrderType.AttackMove, hit.point, null);
            return true;
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
        /// Selects every usable building of the clicked one's kind that is on screen, keeping the
        /// clicked one first so the panel describes it. Unlike units, off-screen buildings stay out:
        /// they are not going anywhere, and the gesture means "these ones here".
        /// </summary>
        void SelectAllStructuresLike(Camera camera, StructureComponent clicked) {
            CollectUsableStructures(structureScratch, candidate =>
                candidate != clicked
                && candidate.StructureID == clicked.StructureID
                && WV_ScreenPicking.IsOnScreen(camera, candidate.transform.position));
            structureScratch.Insert(0, clicked);
            inspector.SetSelection(structureScratch);
            hud.SetHint($"{inspector.Count} x {clicked.DisplayName} selected");
        }

        void RefreshSelectionUI() {
            // A pending sale named a price for the old selection; it must not sell a new one.
            if (sellConfirmUntil > 0f && selection.Signature() != sellConfirmSelection)
                CancelSellConfirm();
            // An order in hand with nothing left to give it to would sit primed forever and then
            // swallow the click that was meant to start a new selection.
            if (armedOrder.HasValue && SelectionCount == 0 && inspector.RallyTargets.Count == 0)
                DisarmOrder("Selection lost - order cancelled");
            hud.RefreshSelection(selection.Units, selection.Troops);
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

        // --- Selling ----------------------------------------------------------

        /// <summary>
        /// The Sell button and the Delete key. The first press names the refund and waits; a second
        /// press within <see cref="SellConfirmSeconds"/>, on the same selection, sells. A sale cannot
        /// be undone, so it is never one click.
        /// </summary>
        void RequestSell() {
            if (SelectionCount == 0) {
                hud.SetHint("Select units or troops to sell");
                return;
            }

            if (sellConfirmUntil <= 0f || selection.Signature() != sellConfirmSelection) {
                long refund = selection.EstimateSellRefund();
                sellConfirmUntil = Time.unscaledTime + SellConfirmSeconds;
                sellConfirmSelection = selection.Signature();
                hud.SetSellConfirming(true, refund);
                hud.SetHint($"Sell {SelectionCount} for {refund:N0}? Click Sell or press Delete again to confirm");
                return;
            }

            CancelSellConfirm();
            selection.CollectObjectIds(orderScratch);
            if (orderScratch.Count == 0 || InstanceFinder.ClientManager == null)
                return;
            InstanceFinder.ClientManager.Broadcast(new WV_SellRequest { objectIds = orderScratch.ToArray() });
        }

        void CancelSellConfirm() {
            if (sellConfirmUntil <= 0f)
                return;
            sellConfirmUntil = 0f;
            hud.SetSellConfirming(false, 0);
        }

        // --- Orders -----------------------------------------------------------

        /// <summary>
        /// Puts an order in hand, to be placed by the next left click. Rearming simply replaces what
        /// was held, and arming with nothing selected is refused out loud rather than leaving a
        /// primed cursor that will turn out to do nothing.
        /// </summary>
        public void ArmOrder(WV_OrderType orderType) {
            if (orderType is WV_OrderType.Stop or WV_OrderType.HoldPosition or WV_OrderType.Freeze) {
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
                if (target == null || !IsHostileToSelection(target)) {
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
                orders.RallySet(groundHit.point);
                return;
            }

            DisarmOrder(null);
            SendOrder(orderType, groundHit.point, null);
        }

        /// <summary>
        /// Whether the selection may be ordered to attack <paramref name="target"/>: the same rule the
        /// server's brains apply, so a living, hurtable enemy is accepted and a dead or invulnerable
        /// one is refused here rather than silently dropped there. It compares against something the
        /// player actually commands rather than against the player's own character, so the answer
        /// matches what the ordered units will do on the server.
        /// </summary>
        bool IsHostileToSelection(IEntity target) {
            TeamConfig team = selection.GetCommandingTeam();
            return team != null && WV_Combat.IsValidTarget(target, team);
        }

        void SendOrder(WV_OrderType orderType, Vector3 position, NetworkObject target) {
            if (SelectionCount == 0 || InstanceFinder.ClientManager == null)
                return;

            selection.CollectObjectIds(orderScratch);
            if (orderScratch.Count == 0)
                return;

            InstanceFinder.ClientManager.Broadcast(new WV_UnitOrderRequest {
                unitObjectIds = orderScratch.ToArray(),
                orderType = (byte)orderType,
                position = position,
                targetObjectId = target != null ? target.ObjectId : 0
            });

            Vector3? ping = orderType switch {
                WV_OrderType.Attack when target != null => target.transform.position,
                WV_OrderType.Move or WV_OrderType.AttackMove => position,
                _ => null
            };
            orders.Ordered(orderType, ping);

            hud.SetHint(orderType switch {
                WV_OrderType.Attack => "Attacking",
                WV_OrderType.AttackMove => "Advancing under fire",
                WV_OrderType.Stop => "Holding here",
                WV_OrderType.HoldPosition => "Holding position",
                WV_OrderType.Freeze => "Frozen - choose Advance or Hold & Fight to resume",
                _ => "Moving out"
            });
        }
    }
}
