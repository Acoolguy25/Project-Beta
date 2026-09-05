using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace RyanAssets.UI.Hover
{
    /// <summary>
    /// Displays a self-sizing tooltip when the cursor rests over a HoverItem.
    /// Supports uGUI elements, 3D colliders, and 2D colliders.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HoverManager : MonoBehaviour
    {
        const string RuntimeObjectName = "HoverManager";
        static HoverManager instance;

        [Header("Timing")]
        [SerializeField, Min(0f)] float hoverDelay = 1.25f;
        [SerializeField, Min(0f)] float cursorMovementTolerance = 3f;

        [Header("Layout")]
        [SerializeField, Min(0f)] float itemGap = 12f;
        [SerializeField, Min(0f)] float screenMargin = 8f;
        [SerializeField, Min(0f)] float horizontalPadding = 14f;
        [SerializeField, Min(0f)] float verticalPadding = 9f;
        [SerializeField, Min(1f)] float maximumWidth = 420f;
        [SerializeField, Min(1f)] float fontSize = 22f;

        [Header("World Objects")]
        [SerializeField] LayerMask hoverLayers = ~0;

        readonly List<RaycastResult> uiRaycastResults = new();
        readonly Vector3[] rectCorners = new Vector3[4];

        Canvas hoverCanvas;
        RectTransform canvasRect;
        RectTransform boxRect;
        TextMeshProUGUI label;
        PointerEventData pointerEventData;
        EventSystem pointerEventSystem;
        HoverItem currentItem;
        Vector2 stationaryCursorPosition;
        Rect lastItemScreenRect;
        float hoverStartedAt;
        bool hasCursorPosition;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void EnsureManagerExists()
        {
            if (FindFirstObjectByType<HoverManager>(FindObjectsInactive.Include) != null)
                return;

            new GameObject(RuntimeObjectName, typeof(HoverManager));
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            instance = null;
        }

        void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }

            instance = this;
            DontDestroyOnLoad(gameObject);
            CreateHoverBox();
        }

        void OnDestroy()
        {
            if (instance == this)
                instance = null;
        }

        void OnDisable()
        {
            Hide();
            currentItem = null;
            hasCursorPosition = false;
        }

        void Update()
        {
            if (Mouse.current == null || Cursor.lockState == CursorLockMode.Locked)
            {
                ResetHover();
                return;
            }

            Vector2 cursorPosition = Mouse.current.position.ReadValue();
            if (!hasCursorPosition)
            {
                stationaryCursorPosition = cursorPosition;
                hasCursorPosition = true;
                hoverStartedAt = Time.unscaledTime;
            }

            float movementToleranceSquared = cursorMovementTolerance * cursorMovementTolerance;
            if ((cursorPosition - stationaryCursorPosition).sqrMagnitude > movementToleranceSquared)
            {
                stationaryCursorPosition = cursorPosition;
                hoverStartedAt = Time.unscaledTime;
                Hide();
            }

            HoverItem item = FindItemUnderCursor(cursorPosition);
            if (item != currentItem)
            {
                currentItem = item;
                hoverStartedAt = Time.unscaledTime;
                Hide();

                if (currentItem != null)
                    TryGetItemScreenRect(currentItem, out lastItemScreenRect);
            }

            if (currentItem == null || string.IsNullOrWhiteSpace(currentItem.HoverText))
            {
                Hide();
                return;
            }

            if (!TryGetItemScreenRect(currentItem, out Rect itemScreenRect))
            {
                Hide();
                return;
            }

            if (!boxRect.gameObject.activeSelf && ItemMoved(itemScreenRect))
            {
                lastItemScreenRect = itemScreenRect;
                hoverStartedAt = Time.unscaledTime;
            }

            if (Time.unscaledTime - hoverStartedAt < hoverDelay)
                return;

            if (!boxRect.gameObject.activeSelf || label.text != currentItem.HoverText)
                SetTextAndSize(currentItem.HoverText);

            if (!TryPositionBox(itemScreenRect))
                Hide();
        }

        HoverItem FindItemUnderCursor(Vector2 cursorPosition)
        {
            HoverItem uiItem = FindUiItem(cursorPosition);
            if (uiItem != null)
                return uiItem;

            Camera camera = Camera.main;
            if (camera == null)
                return null;

            Ray ray = camera.ScreenPointToRay(cursorPosition);
            if (Physics.Raycast(ray, out RaycastHit hit, float.MaxValue, hoverLayers, QueryTriggerInteraction.Collide))
            {
                HoverItem item = hit.transform.GetComponentInParent<HoverItem>();
                if (item != null && item.isActiveAndEnabled)
                    return item;
            }

            RaycastHit2D hit2D = Physics2D.GetRayIntersection(ray, float.MaxValue, hoverLayers);
            if (hit2D.collider != null)
            {
                HoverItem item = hit2D.transform.GetComponentInParent<HoverItem>();
                if (item != null && item.isActiveAndEnabled)
                    return item;
            }

            return null;
        }

        HoverItem FindUiItem(Vector2 cursorPosition)
        {
            EventSystem eventSystem = EventSystem.current;
            if (eventSystem == null)
                return null;

            if (pointerEventData == null || pointerEventSystem != eventSystem)
            {
                pointerEventSystem = eventSystem;
                pointerEventData = new PointerEventData(eventSystem);
            }

            pointerEventData.Reset();
            pointerEventData.position = cursorPosition;
            uiRaycastResults.Clear();
            eventSystem.RaycastAll(pointerEventData, uiRaycastResults);

            foreach (RaycastResult result in uiRaycastResults)
            {
                HoverItem item = result.gameObject.GetComponentInParent<HoverItem>();
                if (item != null && item.isActiveAndEnabled)
                    return item;
            }

            return null;
        }

        bool TryGetItemScreenRect(HoverItem item, out Rect screenRect)
        {
            if (item.transform is RectTransform itemRect)
            {
                Canvas itemCanvas = item.GetComponentInParent<Canvas>();
                Camera uiCamera = itemCanvas != null && itemCanvas.rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay
                    ? itemCanvas.rootCanvas.worldCamera
                    : null;

                itemRect.GetWorldCorners(rectCorners);
                Vector2 min = RectTransformUtility.WorldToScreenPoint(uiCamera, rectCorners[0]);
                Vector2 max = min;
                for (int i = 1; i < rectCorners.Length; i++)
                {
                    Vector2 point = RectTransformUtility.WorldToScreenPoint(uiCamera, rectCorners[i]);
                    min = Vector2.Min(min, point);
                    max = Vector2.Max(max, point);
                }

                screenRect = Rect.MinMaxRect(min.x, min.y, max.x, max.y);
                return true;
            }

            if (TryGetWorldBounds(item, out Bounds bounds))
            {
                Camera camera = Camera.main;
                if (camera == null)
                {
                    screenRect = default;
                    return false;
                }

                Vector3 center = bounds.center;
                Vector3 extents = bounds.extents;
                Vector2 min = new(float.PositiveInfinity, float.PositiveInfinity);
                Vector2 max = new(float.NegativeInfinity, float.NegativeInfinity);

                for (int x = -1; x <= 1; x += 2)
                for (int y = -1; y <= 1; y += 2)
                for (int z = -1; z <= 1; z += 2)
                {
                    Vector3 screenPoint = camera.WorldToScreenPoint(center + Vector3.Scale(extents, new Vector3(x, y, z)));
                    if (screenPoint.z < 0f)
                        continue;

                    min = Vector2.Min(min, screenPoint);
                    max = Vector2.Max(max, screenPoint);
                }

                if (!float.IsInfinity(min.x))
                {
                    screenRect = Rect.MinMaxRect(min.x, min.y, max.x, max.y);
                    return true;
                }
            }

            screenRect = default;
            return false;
        }

        static bool TryGetWorldBounds(HoverItem item, out Bounds bounds)
        {
            Renderer[] renderers = item.GetComponentsInChildren<Renderer>();
            if (renderers.Length > 0)
            {
                bounds = renderers[0].bounds;
                for (int i = 1; i < renderers.Length; i++)
                    bounds.Encapsulate(renderers[i].bounds);
                return true;
            }

            Collider[] colliders = item.GetComponentsInChildren<Collider>();
            if (colliders.Length > 0)
            {
                bounds = colliders[0].bounds;
                for (int i = 1; i < colliders.Length; i++)
                    bounds.Encapsulate(colliders[i].bounds);
                return true;
            }

            Collider2D[] colliders2D = item.GetComponentsInChildren<Collider2D>();
            if (colliders2D.Length > 0)
            {
                bounds = colliders2D[0].bounds;
                for (int i = 1; i < colliders2D.Length; i++)
                    bounds.Encapsulate(colliders2D[i].bounds);
                return true;
            }

            bounds = default;
            return false;
        }

        bool ItemMoved(Rect itemScreenRect)
        {
            return (itemScreenRect.center - lastItemScreenRect.center).sqrMagnitude > 1f ||
                   Mathf.Abs(itemScreenRect.width - lastItemScreenRect.width) > 1f ||
                   Mathf.Abs(itemScreenRect.height - lastItemScreenRect.height) > 1f;
        }

        void CreateHoverBox()
        {
            GameObject canvasObject = new("HoverCanvas", typeof(RectTransform), typeof(Canvas),
                typeof(UnityEngine.UI.CanvasScaler), typeof(UnityEngine.UI.GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);

            hoverCanvas = canvasObject.GetComponent<Canvas>();
            hoverCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            hoverCanvas.sortingOrder = short.MaxValue;

            UnityEngine.UI.CanvasScaler scaler = canvasObject.GetComponent<UnityEngine.UI.CanvasScaler>();
            scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            canvasRect = canvasObject.GetComponent<RectTransform>();

            GameObject boxObject = new("HoverBox", typeof(RectTransform), typeof(CanvasRenderer), typeof(UnityEngine.UI.Image));
            boxObject.transform.SetParent(canvasObject.transform, false);
            boxRect = boxObject.GetComponent<RectTransform>();
            boxRect.anchorMin = new Vector2(0.5f, 0.5f);
            boxRect.anchorMax = new Vector2(0.5f, 0.5f);
            boxRect.pivot = new Vector2(0.5f, 0.5f);

            UnityEngine.UI.Image background = boxObject.GetComponent<UnityEngine.UI.Image>();
            background.color = Color.black;
            background.raycastTarget = false;

            GameObject textObject = new("Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            textObject.transform.SetParent(boxObject.transform, false);
            RectTransform textRect = textObject.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(horizontalPadding, verticalPadding);
            textRect.offsetMax = new Vector2(-horizontalPadding, -verticalPadding);

            label = textObject.GetComponent<TextMeshProUGUI>();
            label.color = Color.white;
            label.fontSize = fontSize;
            label.alignment = TextAlignmentOptions.TopLeft;
            label.enableWordWrapping = true;
            label.overflowMode = TextOverflowModes.Ellipsis;
            label.raycastTarget = false;

            boxObject.SetActive(false);
        }

        void SetTextAndSize(string text)
        {
            label.text = text;
            label.fontSize = fontSize;

            float scaleFactor = Mathf.Max(hoverCanvas.scaleFactor, 0.01f);
            Rect safeArea = Screen.safeArea;
            float availableWidth = Mathf.Max(1f, (safeArea.width - 2f * screenMargin) / scaleFactor);
            float availableHeight = Mathf.Max(1f, (safeArea.height - 2f * screenMargin) / scaleFactor);
            float contentMaxWidth = Mathf.Max(1f, Mathf.Min(maximumWidth, availableWidth) - 2f * horizontalPadding);

            Vector2 preferredSize = label.GetPreferredValues(text, contentMaxWidth, 0f);
            float width = Mathf.Min(preferredSize.x + 2f * horizontalPadding, availableWidth);
            float height = Mathf.Min(preferredSize.y + 2f * verticalPadding, availableHeight);
            boxRect.sizeDelta = new Vector2(Mathf.Ceil(width), Mathf.Ceil(height));
            boxRect.gameObject.SetActive(true);
        }

        bool TryPositionBox(Rect itemScreenRect)
        {
            float scaleFactor = Mathf.Max(hoverCanvas.scaleFactor, 0.01f);
            Vector2 boxSizePixels = boxRect.rect.size * scaleFactor;
            Rect safeArea = Screen.safeArea;
            safeArea.xMin += screenMargin;
            safeArea.xMax -= screenMargin;
            safeArea.yMin += screenMargin;
            safeArea.yMax -= screenMargin;

            float halfWidth = boxSizePixels.x * 0.5f;
            float halfHeight = boxSizePixels.y * 0.5f;
            float gapPixels = itemGap * scaleFactor;
            Vector2[] candidates =
            {
                new(itemScreenRect.xMax + gapPixels + halfWidth, itemScreenRect.center.y),
                new(itemScreenRect.xMin - gapPixels - halfWidth, itemScreenRect.center.y),
                new(itemScreenRect.center.x, itemScreenRect.yMax + gapPixels + halfHeight),
                new(itemScreenRect.center.x, itemScreenRect.yMin - gapPixels - halfHeight)
            };

            Vector2 chosenPosition = ClampToSafeArea(candidates[0], halfWidth, halfHeight, safeArea);
            bool foundNonOverlappingPosition = false;
            foreach (Vector2 candidate in candidates)
            {
                Vector2 clamped = ClampToSafeArea(candidate, halfWidth, halfHeight, safeArea);
                Rect candidateRect = new(clamped - boxSizePixels * 0.5f, boxSizePixels);
                if (!candidateRect.Overlaps(itemScreenRect))
                {
                    chosenPosition = clamped;
                    foundNonOverlappingPosition = true;
                    break;
                }
            }

            if (!foundNonOverlappingPosition)
                return false;

            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, chosenPosition, null, out Vector2 localPosition))
            {
                boxRect.anchoredPosition = localPosition;
                return true;
            }

            return false;
        }

        static Vector2 ClampToSafeArea(Vector2 position, float halfWidth, float halfHeight, Rect safeArea)
        {
            return new Vector2(
                Mathf.Clamp(position.x, safeArea.xMin + halfWidth, safeArea.xMax - halfWidth),
                Mathf.Clamp(position.y, safeArea.yMin + halfHeight, safeArea.yMax - halfHeight));
        }

        void ResetHover()
        {
            currentItem = null;
            hasCursorPosition = false;
            Hide();
        }

        void Hide()
        {
            if (boxRect != null)
                boxRect.gameObject.SetActive(false);
        }
    }
}
