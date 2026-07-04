using RyanAssets.Tools.Shared;
using UnityEngine;
using UnityEngine.EventSystems;

namespace RyanAssets.Tools.Client
{
    [RequireComponent(typeof(ToolBaseShared))]
    public class ToolBaseClient : MonoBehaviour {
        private ToolBaseShared toolBaseShared;
        void Start() {
            toolBaseShared = GetComponent<ToolBaseShared>();
            toolBaseShared.equippedEvent += OnEquip;
            toolBaseShared.unequippedEvent += OnUnequip;
        }
        protected virtual void Update() {
            if (Input.GetMouseButtonDown(0))
                if (!EventSystem.current.IsPointerOverGameObject())
                    OnClick(Input.mousePosition);
        }
        protected virtual void OnEquip(ToolBaseShared _) {

        }
        protected virtual void OnUnequip(ToolBaseShared _) {

        }
        protected virtual void OnClick(Vector2 position) {

        }
    }
}
