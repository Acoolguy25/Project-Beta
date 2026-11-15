using System.Collections.Generic;
using UnityEngine;

namespace RyanAssets.Client.ClientUI.Command {
    /// <summary>
    /// Keeps exactly as many copies of an authored row prefab active under a parent as a list needs.
    /// <para>
    /// The command panels redraw on every change to a queue, a balance, or a research timer. Tearing
    /// the rows down and instantiating them again each time would allocate constantly and reset
    /// hover state under the cursor, so rows are kept and rebound, and surplus ones are only hidden.
    /// </para>
    /// </summary>
    public sealed class UIPool<T> where T : Component {
        readonly T prefab;
        readonly Transform parent;
        readonly List<T> items = new();

        public UIPool(T prefab, Transform parent) {
            this.prefab = prefab;
            this.parent = parent;
        }

        public int ActiveCount { get; private set; }

        public T this[int index] => items[index];

        /// <summary>
        /// Makes <paramref name="count"/> rows active, in order, creating any that are missing.
        /// Returns false when the pool has no prefab or parent to build from.
        /// </summary>
        public bool Resize(int count) {
            if (prefab == null || parent == null)
                return false;

            while (items.Count < count) {
                T item = Object.Instantiate(prefab, parent);
                item.name = $"{prefab.name} {items.Count}";
                items.Add(item);
            }

            for (int i = 0; i < items.Count; i++) {
                bool active = i < count;
                if (items[i].gameObject.activeSelf != active)
                    items[i].gameObject.SetActive(active);
            }

            ActiveCount = count;
            return true;
        }
    }
}
