using System;
using System.Collections.Generic;
using System.Linq;
using RyanAssets.UI.Textbox;
using UnityEngine;
using UnityEngine.UI;
using System.Collections;


namespace RyanAssets.UI.ListGrid {
    public class ListGridUI<T> : MonoBehaviour {
        [SerializeField]
        protected GameObject modelPrefab;
        [SerializeField]
        protected ScrollRect scrollRect;
        [SerializeField]
        protected bool AutoScroll;
        [SerializeField]
        protected CustomInputField searchInputField;
        protected Transform contentTarget;
        GridLayoutGroup gridLayoutGroup;
        VerticalLayoutGroup verticalLayoutGroup;

        protected bool IsDestroyed;
        protected Action<GameObject, T> OnCreatePrefab;
        protected Action<GameObject> OnDeletePrefab;
        protected Dictionary<Transform, int> prefabOrder = new();
        protected int globalOrder;

        readonly List<AsyncInstantiateOperation<GameObject>> pending_ops = new();
        public bool IsBuilding => pending_ops.Count > 0;

        RectTransform contentRT;
        protected void ClearPendingPrefabs() {
            // Cancel all pending operations
            foreach (var op in pending_ops)
                op.Cancel();
            pending_ops.Clear();
        }
        protected void ClearActivePrefabs() {
            // Destroy all remaining created objects
            if (contentTarget == null)
                return;
            foreach (Transform obj in contentTarget) {
                RemovePrefab(obj);
            }
            globalOrder = 0;
        }
        public void SetPrefabActive(GameObject prefab) {
            if (searchInputField == null)
                return; // do nothing if search textbox does not exist

            string prefabName = prefab.name;

            bool active = string.IsNullOrEmpty(searchInputField.text) ||
                          prefabName.Contains(searchInputField.text, StringComparison.OrdinalIgnoreCase);

            prefab.SetActive(active);
        }
        public void ClearPrefabs() {
            ClearPendingPrefabs();
            ClearActivePrefabs();
        }
        public void RemovePrefab(Transform obj) {
            OnDeletePrefab?.Invoke(obj.gameObject);
            Destroy(obj.gameObject);
            prefabOrder.Remove(obj);
        }
        // Models may contain network connections whose ToString queries a transport.
        // Let each list provide its display key without triggering those side effects.
        protected virtual string GetItemName(T data) => data?.ToString() ?? typeof(T).Name;
        protected void AddPrefab(T data, int order) {
            AsyncInstantiateOperation<GameObject> op = InstantiateAsync(modelPrefab);

            op.completed += _ => {
                GameObject prefabClone = op.Result != null && op.Result.Length > 0 ? op.Result[0] : null;
                bool wasPending = pending_ops.Remove(op);
                // Networked tools and other Unity objects can despawn while their
                // asynchronous UI row is being created. Never dereference stale data.
                bool dataDestroyed = data is UnityEngine.Object source && source == null;
                if (this == null || IsDestroyed || gameObject.IsDestroying() || !wasPending || prefabClone == null || dataDestroyed) {
                    if (prefabClone != null)
                        DestroyImmediate(prefabClone);
                    return; // cancelled
                }
                prefabOrder.Add(prefabClone.transform, order);
                prefabClone.transform.SetParent(contentTarget, false);
                prefabClone.name = GetItemName(data);
                SetPrefabActive(prefabClone);

                OnCreatePrefab?.Invoke(prefabClone.gameObject, data);
                UpdateLayout();
            };
            pending_ops.Add(op);
        }
        public virtual void AddPrefab(T data) {
            AddPrefab(data, globalOrder++);
        }
        public void AddPrefabs(T[] objects) {
            //foreach (T data in objects) {
            for (int i = 0; i < objects.Length; i++) {
                T data = objects[i];
                AddPrefab(data, globalOrder + i);
            }
            globalOrder += ((int)objects.Length);
        }
        public void RefreshPrefabs(T[] objects) {
            ClearPrefabs();
            AddPrefabs(objects);
        }
        public void UpdateSearchText(string searchText) {
            UpdateLayout();
        }
        protected void UpdateLayout() {
            if (contentRT == null || !isActiveAndEnabled)
                return;

            if (layoutRoutine != null)
                StopCoroutine(layoutRoutine);

            layoutRoutine = StartCoroutine(UpdateLayoutRoutine());
        }
        private Coroutine layoutRoutine;
        private void UpdateLayoutOrder() {
            var children = contentTarget.Cast<Transform>()
                .OrderBy(t => prefabOrder[t])
                .ToList();

            for (int i = 0; i < children.Count; i++) {
                children[i].SetSiblingIndex(i);
                SetPrefabActive(children[i].gameObject);
            }
        }
        private IEnumerator UpdateLayoutRoutine() {
            bool wasAtBottom = AutoScroll &&
                            scrollRect != null &&
                            scrollRect.verticalNormalizedPosition <= 0.01f;

            yield return new WaitForEndOfFrame();

            UpdateLayoutOrder();
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(contentRT);

            float height;

            if (gridLayoutGroup != null) {
                int count = 0;

                foreach (RectTransform child in contentRT) {
                    if (child.gameObject.activeInHierarchy)
                        count++;
                }

                int columns;
                int rows;

                if (gridLayoutGroup.constraint == GridLayoutGroup.Constraint.FixedColumnCount) {
                    columns = Mathf.Max(1, gridLayoutGroup.constraintCount);
                    rows = Mathf.CeilToInt(count / (float)columns);
                } else if (gridLayoutGroup.constraint == GridLayoutGroup.Constraint.FixedRowCount) {
                    rows = count == 0 ? 0 : Mathf.Min(count, Mathf.Max(1, gridLayoutGroup.constraintCount));
                } else {
                    float availableWidth = contentRT.rect.width - gridLayoutGroup.padding.horizontal;

                    columns = Mathf.Max(1, Mathf.FloorToInt(
                        (availableWidth + gridLayoutGroup.spacing.x + 0.001f) /
                        (gridLayoutGroup.cellSize.x + gridLayoutGroup.spacing.x)));

                    rows = Mathf.CeilToInt(count / (float)columns);
                }

                height =
                    gridLayoutGroup.padding.vertical +
                    rows * gridLayoutGroup.cellSize.y +
                    Mathf.Max(0, rows - 1) * gridLayoutGroup.spacing.y;
            } else if (verticalLayoutGroup != null) {
                int count = 0;
                height = verticalLayoutGroup.padding.vertical;

                foreach (Transform child in contentRT) {
                    if (child.TryGetComponent(out RectTransform childRT) &&
                        child.gameObject.activeInHierarchy) {
                        float preferredHeight = LayoutUtility.GetPreferredHeight(childRT);
                        height += preferredHeight >= 0f ? preferredHeight : childRT.rect.height;
                        count++;
                    }
                }

                height += Mathf.Max(0, count - 1) * verticalLayoutGroup.spacing;
            } else {
                height = 0f;

                foreach (Transform child in contentRT) {
                    if (child.TryGetComponent(out RectTransform childRT) &&
                        child.gameObject.activeInHierarchy) {
                        height += childRT.rect.height;
                    }
                }
            }

            contentRT.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);

            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(contentRT);

            if (AutoScroll && wasAtBottom && scrollRect != null) {
                scrollRect.StopMovement();
                scrollRect.verticalNormalizedPosition = 0f;
            }

            layoutRoutine = null;
        }
        public void ScrollIntoView(GameObject obj, float time = 0f) {
            Canvas.ForceUpdateCanvases();

            RectTransform item = (RectTransform)obj.transform;
            RectTransform view = scrollRect.viewport;
            RectTransform content = contentRT;

            Bounds itemBounds = RectTransformUtility.CalculateRelativeRectTransformBounds(view, item);

            float offset = 0f;

            if (itemBounds.max.y > view.rect.yMax)
                offset = itemBounds.max.y - view.rect.yMax;
            else if (itemBounds.min.y < view.rect.yMin)
                offset = itemBounds.min.y - view.rect.yMin;
            else
                return;

            float hidden = content.rect.height - view.rect.height;
            if (hidden <= 0f)
                return;

            float target = Mathf.Clamp01(
                scrollRect.verticalNormalizedPosition + offset / hidden);

            if (time <= 0f)
                scrollRect.verticalNormalizedPosition = target;
            else
                StartCoroutine(ScrollRoutine(target, time));
        }

        private IEnumerator ScrollRoutine(float target, float time) {
            float start = scrollRect.verticalNormalizedPosition;

            for (float t = 0; t < time; t += Time.unscaledDeltaTime) {
                scrollRect.verticalNormalizedPosition = Mathf.Lerp(start, target, t / time);
                yield return null;
            }

            scrollRect.verticalNormalizedPosition = target;
        }
        virtual protected void Start() {
            contentTarget = scrollRect.content;
            contentRT = contentTarget.GetComponent<RectTransform>();
            gridLayoutGroup = contentRT.GetComponent<GridLayoutGroup>();
            verticalLayoutGroup = contentRT.GetComponent<VerticalLayoutGroup>();
            if (searchInputField)
                searchInputField.onValueChanged.AddListener(UpdateSearchText);
            globalOrder = 0;
        }
        virtual protected void OnDestroy() {
            IsDestroyed = true;
            if (searchInputField)
                searchInputField.onValueChanged.RemoveListener(UpdateSearchText);
            ClearPendingPrefabs();
        }
    }
}
