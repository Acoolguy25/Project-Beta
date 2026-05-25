using System;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.UI;
using RyanAssets.UI.ListGrid;

namespace RyanAssets.UI.ButtonGrid {
    public class ButtonGridUI<T> : ListGridUI<T> {
        protected Action<GameObject, T> OnClickPrefab;
        virtual protected void Awake() {
            OnCreatePrefab += (GameObject prefabClone, T data) => {
                Button prefabButton = prefabClone.GetComponent<Button>();
                Assert.IsNotNull(prefabButton, $"ButtonGrid Prefab does not contain button: {prefabClone}");
                prefabButton.onClick.AddListener(() => OnClickPrefab?.Invoke(prefabClone, data));
            };
        }
    }
}
