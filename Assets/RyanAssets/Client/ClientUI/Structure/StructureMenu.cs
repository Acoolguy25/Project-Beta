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
using System.Linq;
using TMPro;
using UnityEngine;

namespace RyanAssets.Client.ClientUI.Build {
    public class StructureMenu : ButtonGridUI<StructureComponent> {
        [SerializeField] private CanvasGroupController canvasGroupController;

        [Header("Placement")]
        [SerializeField] private GameObject placementPanel;
        [SerializeField] private TextMeshProUGUI placementTitle;
        [SerializeField] private TextMeshProUGUI placementStatus;

        private static readonly Color ValidPreviewColor = new(0.2f, 1f, 0.55f, 0.72f);
        private static readonly Color InvalidPreviewColor = new(1f, 0.2f, 0.2f, 0.72f);

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
            RefreshPrefabs(structures);
        }

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
            prefab.transform.GetChild(1).GetComponent<UnityEngine.UI.Image>().sprite = structure.Sprite;
            prefab.transform.GetChild(2).GetComponent<TextMeshProUGUI>().text = structure.Category;
            prefab.transform.GetChild(3).GetComponent<TextMeshProUGUI>().text = structure.DisplayName;
            prefab.transform.GetChild(4).GetComponent<TextMeshProUGUI>().text = structure.Description;
            prefab.transform.GetChild(5).GetComponent<TextMeshProUGUI>().text = MathHelper.AddCommas(structure.Cost);
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
            placementValid = !Physics.CheckBox(
                bounds.center,
                StructurePlacement.GetOverlapHalfExtents(bounds),
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
            OnClickPrefab += OnSelectStructure;
        }

        private void OnDisable() {
            StructureControls.onToggleStructureMenuEvent -= OnToggleMenu;
            ToolControls.activateToolPressed -= OnPlaceStructure;
            ToolControls.reloadToolPressed -= RotatePlacement;
            SharedGlobalEvents.UnbindInstanceReady(OnInstanceReady);
            OnCreatePrefab -= OnAddPrefab;
            OnClickPrefab -= OnSelectStructure;
            OnInstanceRemoved();
            SetVisible(false, true);
        }

        protected override void OnDestroy() {
            base.OnDestroy();
        }
    }
}
