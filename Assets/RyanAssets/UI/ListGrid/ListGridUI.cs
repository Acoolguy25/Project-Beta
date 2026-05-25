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
        ScrollRect scrollRect;
        Transform contentTarget;

        protected Action<GameObject, T> OnCreatePrefab;
        protected Action<GameObject> OnDeletePrefab;

        readonly List<AsyncInstantiateOperation<GameObject>> pending_ops = new();

        RectTransform contentRT;
        public void RefreshPrefabs(T[] objects) {
            // Cancel all pending operations
            foreach (AsyncInstantiateOperation op in pending_ops)
                op.Cancel();
            pending_ops.Clear();

            // Destroy all remaining created objects
            foreach (Transform obj in contentTarget){
                OnDeletePrefab?.Invoke(obj.gameObject);
                Destroy(obj.gameObject);
            }

            foreach (T data in objects) {
                AsyncInstantiateOperation<GameObject> op = InstantiateAsync(modelPrefab);

                op.completed += _ => {
                    GameObject prefabClone = op.Result[0];
                    prefabClone.transform.SetParent(contentTarget, false);
                    pending_ops.Remove(op);
                    UpdateLayout();

                    OnCreatePrefab?.Invoke(prefabClone.gameObject, data);
                };
                pending_ops.Add(op);
            }
        }
        private void UpdateLayout()
        {
            Canvas.ForceUpdateCanvases();

            LayoutRebuilder.ForceRebuildLayoutImmediate(contentRT);

            float height = 0f;

            foreach (RectTransform child in contentRT)
            {
                if (!child.gameObject.activeInHierarchy)
                    continue;

                height += child.rect.height;
            }

            var grid = contentRT.GetComponent<GridLayoutGroup>();
            if (grid != null)
            {
                int count = contentRT.childCount;
                int columns = Mathf.Max(1, Mathf.FloorToInt(
                    (contentRT.rect.width + grid.spacing.x) /
                    (grid.cellSize.x + grid.spacing.x)
                ));

                int rows = Mathf.CeilToInt(count / (float)columns);

                height =
                    grid.padding.top +
                    grid.padding.bottom +
                    rows * grid.cellSize.y +
                    Mathf.Max(0, rows - 1) * grid.spacing.y;
            }

            contentRT.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
        }
        virtual protected void Start() {
            contentTarget = scrollRect.content;
            contentRT = contentTarget.GetComponent<RectTransform>();
        }
    }
}