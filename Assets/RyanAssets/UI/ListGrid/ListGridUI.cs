using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
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
        protected Transform contentTarget;
        GridLayoutGroup gridLayoutGroup;
        // VerticalLayoutGroup verticalLayoutGroup;

        protected Action<GameObject, T> OnCreatePrefab;
        protected Action<GameObject> OnDeletePrefab;
        protected Dictionary<Transform, uint> prefabOrder = new();
        protected uint globalOrder;

        readonly List<AsyncInstantiateOperation<GameObject>> pending_ops = new();

        RectTransform contentRT;
        protected void ClearPendingPrefabs(){
            // Cancel all pending operations
            foreach (var op in pending_ops)
                op.Cancel();
            pending_ops.Clear();
        }
        protected void ClearActivePrefabs(){
            // Destroy all remaining created objects
            if (contentTarget == null)
                return;
            foreach (Transform obj in contentTarget) {
                RemovePrefab(obj);
            }
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
        protected void AddPrefab(T data, uint order) {
            AsyncInstantiateOperation<GameObject> op = InstantiateAsync(modelPrefab);

            op.completed += _ => {
                if (!pending_ops.Remove(op))
                    return; // cancelled
                GameObject prefabClone = op.Result[0];
                prefabOrder.Add(prefabClone.transform, order);
                prefabClone.transform.SetParent(contentTarget, false);
                

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
            for (uint i = 0; i < objects.Length; i++) {
                T data = objects[i];
                AddPrefab(data, globalOrder + i);
            }
            globalOrder += ((uint)objects.Length);
        }
        public void RefreshPrefabs(T[] objects) {
            ClearPrefabs();
            AddPrefabs(objects);
        }
        protected void UpdateLayout(){
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
            }
        }
        private IEnumerator UpdateLayoutRoutine()
        {
            bool wasAtBottom = AutoScroll &&
                            scrollRect != null &&
                            scrollRect.verticalNormalizedPosition <= 0.01f;

            yield return new WaitForEndOfFrame();

            UpdateLayoutOrder();
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(contentRT);

            float height;

            if (gridLayoutGroup != null)
            {
                int count = 0;

                foreach (RectTransform child in contentRT)
                {
                    if (child.gameObject.activeInHierarchy)
                        count++;
                }

                float availableWidth = contentRT.rect.width - gridLayoutGroup.padding.horizontal;

                int columns = Mathf.Max(1, Mathf.FloorToInt(
                    (availableWidth + gridLayoutGroup.spacing.x + 0.001f) /
                    (gridLayoutGroup.cellSize.x + gridLayoutGroup.spacing.x)));

                int rows = Mathf.CeilToInt(count / (float)columns);

                height =
                    gridLayoutGroup.padding.vertical +
                    rows * gridLayoutGroup.cellSize.y +
                    Mathf.Max(0, rows - 1) * gridLayoutGroup.spacing.y;
            }
            else
            {
                height = 0f;

                foreach (Transform child in contentRT)
                {
                    if (child.TryGetComponent(out RectTransform childRT) &&
                        child.gameObject.activeInHierarchy)
                    {
                        height += childRT.rect.height;
                    }
                }
            }

            contentRT.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);

            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(contentRT);

            if (AutoScroll && wasAtBottom && scrollRect != null)
            {
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
            globalOrder = 0;
            // verticalLayoutGroup = contentRT.GetComponent<VerticalLayoutGroup>();
        }
        virtual protected void OnDestroy(){
            ClearPendingPrefabs();
        }
    }
}
