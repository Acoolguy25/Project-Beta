using FishNet;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using RyanAssets.Core;
using RyanAssets.Input;
using RyanAssets.Shared.Declarations;
using RyanAssets.Shared.Global;
using RyanAssets.Shared.Globals;
using RyanAssets.Shared.Requests;
using RyanAssets.UI;
using RyanAssets.UI.ButtonGrid;
using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RyanAssets.Client.ClientUI.Build {
    public class StructureMenu : ButtonGridUI<StructureComponent> {
        [SerializeField] private CanvasGroupController canvasGroupController;

        [Header("Categories")]
        [Tooltip("Parent the category tabs are cloned under. Laid out by the prefab's layout group.")]
        [SerializeField] private RectTransform categoryButtonRoot;
        [Tooltip("Authored tab prefab, cloned once per category the build list contains.")]
        [SerializeField] private StructureCategoryButton categoryButtonPrefab;
        [Tooltip("Shows how many structures the current filter leaves visible.")]
        [SerializeField] private TextMeshProUGUI itemCountText;
        [SerializeField] private string allCategoriesLabel = "ALL ITEMS";
        [SerializeField] private string uncategorizedLabel = "OTHER";

        [Header("Placement")]
        [SerializeField] private GameObject placementPanel;
        [SerializeField] private TextMeshProUGUI placementTitle;
        [SerializeField] private TextMeshProUGUI placementStatus;

        [Header("Availability")]
        [Tooltip("Description colour on a card the game mode has locked.")]
        [SerializeField] private Color lockedTextColor = new(1f, 0.72f, 0.35f, 1f);
        [Tooltip("Cost colour on a card the local player cannot currently pay for.")]
        [SerializeField] private Color unaffordableCostColor = new(1f, 0.45f, 0.45f, 1f);

        private static readonly Color ValidPreviewColor = new(0.2f, 1f, 0.55f, 0.72f);
        private static readonly Color InvalidPreviewColor = new(1f, 0.2f, 0.2f, 0.72f);

        // Card children, in the StructureItemCard prefab's authored order.
        private const int IconChild = 1, CategoryChild = 2, NameChild = 3, DescriptionChild = 4, CostChild = 5;

        /// <summary>
        /// Optional game-mode gate consulted for every card. A locked structure stays listed - a
        /// player should be able to see what research or progress will unlock - but cannot be
        /// picked for placement. Leave null and every structure is available.
        /// </summary>
        public static Func<StructureComponent, StructureAvailability> AvailabilityProvider;

        /// <summary>
        /// Optional funds check, so a structure the player cannot pay for reads that way on its card
        /// rather than only through a refused placement. Leave null to never mark costs.
        /// </summary>
        public static Func<StructureComponent, bool> AffordabilityProvider;

        private static event Action AvailabilityChanged;

        /// <summary>
        /// Re-evaluates every open card against the providers. A game mode calls this when the state
        /// its providers read changes - research finishing, funds moving - since the menu cannot
        /// observe that state itself.
        /// </summary>
        public static void NotifyAvailabilityChanged() => AvailabilityChanged?.Invoke();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetProviders() {
            // A previous session's game mode must not keep locking cards after a domain reload.
            AvailabilityProvider = null;
            AffordabilityProvider = null;
            AvailabilityChanged = null;
        }

        // Row -> its structure's category, so the sidebar can filter without re-deriving the
        // category from a row's display name.
        private readonly Dictionary<GameObject, string> rowCategories = new();
        // Row -> the structure it offers and the colours its labels were authored with, so a card
        // can be locked, unlocked, and restored in place without being rebuilt.
        private readonly Dictionary<GameObject, RowBinding> rowBindings = new();

        private sealed class RowBinding {
            public StructureComponent Structure;
            public Color DescriptionColor;
            public Color CostColor;
        }
        private readonly List<StructureCategoryButton> categoryTabs = new();
        /// <summary>The category the sidebar is filtered to, or null for "all items".</summary>
        private string selectedCategory;

        private MaterialPropertyBlock previewProperties;
        private StructureComponent selectedStructure;
        private GameObject previewInstance;
        private Renderer[] previewRenderers;
        private Vector3 placementPosition;
        private float placementRotation;
        private bool placementValid;
        private bool menuCloseSubscribed;

        protected override void Awake() {
            base.Awake();
            previewProperties = new MaterialPropertyBlock();
        }

        protected override void Start() {
            base.Start();
            if (placementPanel != null)
                placementPanel.SetActive(false);
            SetVisible(false, true);
        }

        public void SetVisible(bool visible, bool instant = false) {
            if (canvasGroupController.isVisible != visible)
                canvasGroupController.SetVisible(visible, instant ? 0f : 0.15f);
            RefreshMenuActive();
        }

        private void RefreshMenuActive() {
            bool menuActive = canvasGroupController.isVisible || selectedStructure != null;
            TopbarControls.IsMenuOpen = menuActive;
            if (menuActive && !menuCloseSubscribed) {
                TopbarControls.closeToggledEvent += Close_ButtonPressed;
                menuCloseSubscribed = true;
            } else if (!menuActive && menuCloseSubscribed) {
                TopbarControls.closeToggledEvent -= Close_ButtonPressed;
                menuCloseSubscribed = false;
            }
        }

        public void Open_ButtonPressed() {
            CancelPlacement(false);
            SetVisible(true);
        }

        public void Close_ButtonPressed() {
            if (selectedStructure != null)
                CancelPlacement(false);
            else
                SetVisible(false);
        }

        public void OnToggleMenu() {
            if (selectedStructure != null) {
                CancelPlacement(true);
                return;
            }

            SetVisible(!canvasGroupController.isVisible);
        }

        private void OnBuildsChanged(
            SyncListOperation op,
            int index,
            ushort oldItem,
            ushort newItem,
            bool asServer) {
            if (op != SyncListOperation.Complete)
                return;
            StructureComponent[] structures = SharedGlobalEvents.Instance.Builds
                .Select(FindStructurePrefab)
                .Where(structure => structure != null)
                .ToArray();
            rowCategories.Clear();
            RefreshPrefabs(structures);
            RebuildCategoryTabs(structures);
        }

        // --- Categories -------------------------------------------------------

        /// <summary>
        /// Rebuilds the sidebar from the categories the live build list actually contains. The tabs
        /// were previously five fixed buttons authored into the canvas with no handlers and labels
        /// that matched no real category, so nothing they named could ever be selected.
        /// </summary>
        private void RebuildCategoryTabs(StructureComponent[] structures) {
            if (categoryButtonRoot == null || categoryButtonPrefab == null)
                return;

            foreach (StructureCategoryButton tab in categoryTabs) {
                if (tab != null)
                    Destroy(tab.gameObject);
            }
            categoryTabs.Clear();

            string[] categories = structures
                .Select(structure => NormalizeCategory(structure.Category))
                .Distinct()
                .OrderBy(category => category)
                .ToArray();

            CreateCategoryTab(null, allCategoriesLabel);
            foreach (string category in categories)
                CreateCategoryTab(category, category);

            // A category can disappear between rebuilds when the server changes the build list.
            if (selectedCategory != null && !categories.Contains(selectedCategory))
                selectedCategory = null;
            SelectCategory(selectedCategory);
        }

        private void CreateCategoryTab(string category, string label) {
            StructureCategoryButton tab = Instantiate(categoryButtonPrefab, categoryButtonRoot);
            tab.gameObject.name = $"{label}Button";
            tab.Bind(category, label.ToUpperInvariant(), () => SelectCategory(category));
            categoryTabs.Add(tab);
        }

        private void SelectCategory(string category) {
            selectedCategory = category;
            foreach (StructureCategoryButton tab in categoryTabs) {
                if (tab != null)
                    tab.SetSelected(tab.Category == category);
            }
            RefreshFilter();
            RefreshItemCount();
        }

        /// <summary>Rows are filtered by category on top of the shared search box.</summary>
        public override void SetPrefabActive(GameObject prefab) {
            base.SetPrefabActive(prefab);
            // The base pass applies the search box. Only narrow further - never re-show a row the
            // search already hid.
            if (!prefab.activeSelf)
                return;
            if (selectedCategory == null)
                return;
            prefab.SetActive(
                rowCategories.TryGetValue(prefab, out string category) && category == selectedCategory);
        }

        public override void UpdateSearchText(string searchText) {
            base.UpdateSearchText(searchText);
            if (contentTarget == null)
                return;
            // UpdateLayout re-applies the filter next frame; the rows themselves are already
            // re-evaluated synchronously, so the count can be refreshed now.
            foreach (Transform row in contentTarget)
                SetPrefabActive(row.gameObject);
            RefreshItemCount();
        }

        private void RefreshItemCount() {
            if (itemCountText == null || contentTarget == null)
                return;
            int visible = 0;
            foreach (Transform row in contentTarget) {
                if (row.gameObject.activeSelf)
                    visible++;
            }
            itemCountText.text = visible == 1 ? "1 ITEM" : $"{visible} ITEMS";
        }

        private string NormalizeCategory(string category) =>
            string.IsNullOrWhiteSpace(category) ? uncategorizedLabel : category.Trim();

        private void OnInstanceRemoved() {
            if (SharedGlobalEvents.Instance != null)
                SharedGlobalEvents.Instance.Builds.OnChange -= OnBuildsChanged;
            CancelPlacement(false);
            ClearPrefabs();
        }

        private void OnInstanceReady() {
            SharedGlobalEvents.Instance.Builds.OnChange += OnBuildsChanged;
            OnBuildsChanged(SyncListOperation.Complete, 0, default, default, false);
        }

        private void OnAddPrefab(GameObject prefab, StructureComponent structure) {
            var icon = prefab.transform.GetChild(IconChild).GetComponent<Image>();
            icon.sprite = structure.Sprite;
            // An Image with no sprite draws an opaque white box over the card's icon frame, which is
            // what made every shop icon read as "not rendering". Hide it instead.
            icon.enabled = structure.Sprite != null;

            prefab.transform.GetChild(CategoryChild).GetComponent<TextMeshProUGUI>().text = structure.Category;
            prefab.transform.GetChild(NameChild).GetComponent<TextMeshProUGUI>().text = structure.DisplayName;
            TextMeshProUGUI description = prefab.transform.GetChild(DescriptionChild).GetComponent<TextMeshProUGUI>();
            TextMeshProUGUI cost = prefab.transform.GetChild(CostChild).GetComponent<TextMeshProUGUI>();
            cost.text = MathHelper.AddCommas(structure.Cost);

            rowCategories[prefab] = NormalizeCategory(structure.Category);
            rowBindings[prefab] = new RowBinding {
                Structure = structure,
                DescriptionColor = description.color,
                CostColor = cost.color
            };
            ApplyAvailability(prefab);
            SetPrefabActive(prefab);
            RefreshItemCount();
        }

        private void OnRemovePrefab(GameObject prefab) {
            rowCategories.Remove(prefab);
            rowBindings.Remove(prefab);
        }

        private static StructureAvailability GetAvailability(StructureComponent structure) =>
            AvailabilityProvider != null && structure != null
                ? AvailabilityProvider(structure)
                : StructureAvailability.Available;

        private void RefreshAllAvailability() {
            foreach (GameObject row in rowBindings.Keys)
                ApplyAvailability(row);
        }

        /// <summary>
        /// Shows a card's lock and affordability. A locked card keeps its place in the list with its
        /// requirement in place of the description; the button is disabled so the click that would
        /// start a doomed placement never happens.
        /// </summary>
        private void ApplyAvailability(GameObject row) {
            if (row == null || !rowBindings.TryGetValue(row, out RowBinding binding))
                return;

            StructureAvailability availability = GetAvailability(binding.Structure);
            bool affordable = AffordabilityProvider == null || AffordabilityProvider(binding.Structure);

            TextMeshProUGUI description = row.transform.GetChild(DescriptionChild).GetComponent<TextMeshProUGUI>();
            description.text = availability.IsAvailable
                ? binding.Structure.Description
                : $"LOCKED - {availability.LockedReason}";
            description.color = availability.IsAvailable ? binding.DescriptionColor : lockedTextColor;

            row.transform.GetChild(CostChild).GetComponent<TextMeshProUGUI>().color =
                affordable ? binding.CostColor : unaffordableCostColor;

            if (row.TryGetComponent(out Button button))
                button.interactable = availability.IsAvailable;
        }

        private static StructureComponent FindStructurePrefab(ushort prefabId) {
            if (InstanceFinder.NetworkManager == null || InstanceFinder.NetworkManager.SpawnablePrefabs == null)
                return null;

            NetworkObject prefab = InstanceFinder.NetworkManager.SpawnablePrefabs.GetObject(asServer: false, prefabId);
            return prefab != null && prefab.TryGetComponent(out StructureComponent structure) ? structure : null;
        }

        private void OnSelectStructure(GameObject _, StructureComponent structure) {
            if (structure == null || SharedGlobalEvents.Instance == null || !SharedGlobalEvents.Instance.CanBuild.Value)
                return;
            // The card is disabled while locked, but the lock can land between the frame the card
            // was drawn and the click, so it is re-checked here rather than trusted.
            if (!GetAvailability(structure).IsAvailable)
                return;

            CancelPlacement(false);
            selectedStructure = structure;
            placementRotation = 0f;
            previewInstance = Instantiate(structure.gameObject);
            DontDestroyOnLoad(previewInstance);
            previewInstance.name = $"{structure.DisplayName} Placement Preview";

            foreach (NetworkBehaviour behaviour in previewInstance.GetComponentsInChildren<NetworkBehaviour>(true))
                behaviour.enabled = false;
            if (previewInstance.TryGetComponent(out NetworkObject networkObject))
                networkObject.enabled = false;
            foreach (Collider collider in previewInstance.GetComponentsInChildren<Collider>(true))
                collider.enabled = false;
            foreach (Transform child in previewInstance.GetComponentsInChildren<Transform>(true))
                child.gameObject.layer = LayerMask.NameToLayer("Ignore Raycast");

            previewRenderers = previewInstance.GetComponentsInChildren<Renderer>(true);
            foreach (Renderer renderer in previewRenderers)
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            if (placementTitle != null)
                placementTitle.text = $"PLACE {structure.DisplayName.ToUpperInvariant()}";
            if (placementStatus != null)
                placementStatus.text = "Move the cursor over the ground • Left click to place • Esc to cancel";
            if (placementPanel != null)
                placementPanel.SetActive(true);
            SetVisible(false);
        }

        private void Update() {
            if (selectedStructure == null)
                return;

            if (!ToolControls.TryGetCursorWorldPosition(out Vector3 worldPosition, StructurePlacement.GroundMask)) {
                SetPreviewValid(false, "Point at the ground to place");
                return;
            }

            UpdatePlacementPreview(worldPosition);
        }

        private void UpdatePlacementPreview(Vector3 worldPosition) {
            Vector3 snappedPosition = StructurePlacement.SnapToGrid(worldPosition);
            previewInstance.SetActive(true);
            if (!StructurePlacement.TryFindGround(snappedPosition, out Vector3 groundPoint) ||
                !StructurePlacement.TryPositionOnGround(previewInstance, groundPoint, placementRotation, out Bounds bounds)) {
                SetPreviewValid(false, "No ground at this grid position");
                return;
            }

            placementPosition = groundPoint;
            StructurePlacement.GetOverlapVolume(
                bounds, groundPoint, out Vector3 overlapCenter, out Vector3 overlapHalfExtents);
            placementValid = !Physics.CheckBox(
                overlapCenter,
                overlapHalfExtents,
                Quaternion.identity,
                LayerMask.GetMask("Structure"),
                QueryTriggerInteraction.Ignore);
            SetPreviewValid(
                placementValid,
                placementValid
                    ? $"Grid {StructurePlacement.GridSize:0}×{StructurePlacement.GridSize:0} • Left click place • R rotate • Esc cancel"
                    : "That grid space is occupied");
        }

        private void OnPlaceStructure(Vector3 worldPosition) {
            if (selectedStructure == null)
                return;

            UpdatePlacementPreview(worldPosition);
            if (!placementValid)
                return;

            InstanceFinder.ClientManager.Broadcast(new StructurePlacementRequest {
                prefabId = selectedStructure.NetworkObject.PrefabId,
                position = placementPosition,
                yRotation = placementRotation
            });
            if (placementStatus != null)
                placementStatus.text = "Placement requested • Move the cursor to place another";
        }

        private void RotatePlacement() {
            if (selectedStructure != null)
                placementRotation = StructurePlacement.SnapRotation(placementRotation + 90f);
        }

        private void SetPreviewValid(bool valid, string status) {
            placementValid = valid;
            if (previewInstance != null)
                previewInstance.SetActive(valid || status != "Point at the ground to place");
            if (placementStatus != null)
                placementStatus.text = status;

            Color color = valid ? ValidPreviewColor : InvalidPreviewColor;
            previewProperties.SetColor("_BaseColor", color);
            previewProperties.SetColor("_Color", color);
            if (previewRenderers == null)
                return;
            foreach (Renderer renderer in previewRenderers)
                renderer.SetPropertyBlock(previewProperties);
        }

        private void CancelPlacement(bool reopenMenu) {
            selectedStructure = null;
            placementValid = false;
            previewRenderers = null;
            if (previewInstance != null)
                Destroy(previewInstance);
            previewInstance = null;
            if (placementPanel != null)
                placementPanel.SetActive(false);
            if (reopenMenu)
                SetVisible(true);
            else
                RefreshMenuActive();
        }

        private void OnEnable() {
            StructureControls.onToggleStructureMenuEvent += OnToggleMenu;
            ToolControls.activateToolPressed += OnPlaceStructure;
            ToolControls.reloadToolPressed += RotatePlacement;
            SharedGlobalEvents.BindInstanceReady(OnInstanceReady);
            OnCreatePrefab += OnAddPrefab;
            OnDeletePrefab += OnRemovePrefab;
            OnClickPrefab += OnSelectStructure;
            AvailabilityChanged += RefreshAllAvailability;
            RefreshAllAvailability();
        }

        private void OnDisable() {
            StructureControls.onToggleStructureMenuEvent -= OnToggleMenu;
            ToolControls.activateToolPressed -= OnPlaceStructure;
            ToolControls.reloadToolPressed -= RotatePlacement;
            SharedGlobalEvents.UnbindInstanceReady(OnInstanceReady);
            OnCreatePrefab -= OnAddPrefab;
            OnDeletePrefab -= OnRemovePrefab;
            OnClickPrefab -= OnSelectStructure;
            AvailabilityChanged -= RefreshAllAvailability;
            OnInstanceRemoved();
            SetVisible(false, true);
        }

        protected override void OnDestroy() {
            base.OnDestroy();
        }
    }
}
