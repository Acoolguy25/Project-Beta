using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace RyanAssets.UI.ListGrid {
    public class ListGridUI<T> : MonoBehaviour {
        [SerializeField]
        GameObject modelPrefab;
        [SerializeField]
        protected ScrollRect scrollRect;
        protected Transform contentTarget;
        GridLayoutGroup gridLayoutGroup;
        // VerticalLayoutGroup verticalLayoutGroup;

        protected Action<GameObject, T> OnCreatePrefab;
        protected Action<GameObject> OnDeletePrefab;

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
        }
        public void AddPrefab(T data) {
            AsyncInstantiateOperation<GameObject> op = InstantiateAsync(modelPrefab);

            op.completed += _ => {
                if (!pending_ops.Remove(op))
                    return; // cancelled
                GameObject prefabClone = op.Result[0];
                prefabClone.transform.SetParent(contentTarget, false);
                

                OnCreatePrefab?.Invoke(prefabClone.gameObject, data);
                UpdateLayout();
            };
            pending_ops.Add(op);
        }
        public void AddPrefabs(T[] objects) {
            foreach (T data in objects) {
                AddPrefab(data);
            }
        }
        public void RefreshPrefabs(T[] objects) {
            ClearPrefabs();
            AddPrefabs(objects);
        }
        protected void UpdateLayout() {
            if (contentRT == null) // ignore if destroyed or deleted
                return;
            Canvas.ForceUpdateCanvases();

            LayoutRebuilder.ForceRebuildLayoutImmediate(contentRT);

            float height = 0f;

            foreach (Transform transform_child in contentRT) {
                if (!transform_child.gameObject.activeInHierarchy || !transform_child.TryGetComponent<RectTransform>(out RectTransform child))
                    continue;

                height += child.rect.height;
            }

            if (gridLayoutGroup != null) {
                int count = contentRT.childCount;
                int columns = Mathf.Max(1, Mathf.FloorToInt(
                    (contentRT.rect.width + gridLayoutGroup.spacing.x) /
                    (gridLayoutGroup.cellSize.x + gridLayoutGroup.spacing.x)
                ));

                int rows = Mathf.CeilToInt(count / (float)columns);

                height =
                    gridLayoutGroup.padding.top +
                    gridLayoutGroup.padding.bottom +
                    rows * gridLayoutGroup.cellSize.y +
                    Mathf.Max(0, rows - 1) * gridLayoutGroup.spacing.y;
            }

            contentRT.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
        }
        virtual protected void Start() {
            contentTarget = scrollRect.content;
            contentRT = contentTarget.GetComponent<RectTransform>();
            gridLayoutGroup = contentRT.GetComponent<GridLayoutGroup>();
            // verticalLayoutGroup = contentRT.GetComponent<VerticalLayoutGroup>();
        }
        void OnDestroy(){
            ClearPendingPrefabs();
        }
    }
}