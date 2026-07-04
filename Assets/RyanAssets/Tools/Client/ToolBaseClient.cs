using RyanAssets.Input;
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
            ToolControls.activateToolPressed += OnActivate;
        }
        protected virtual void OnEquip(ToolBaseShared _) {

        }
        protected virtual void OnUnequip(ToolBaseShared _) {

        }
        protected virtual void OnActivate() {

        }
    }
}
