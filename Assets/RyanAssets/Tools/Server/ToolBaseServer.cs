using RyanAssets.Tools.Shared;
using UnityEngine;

namespace RyanAssets.Tools.Server
{
    [RequireComponent(typeof(ToolBaseShared))]
    public class ToolBaseServer : MonoBehaviour {
        protected ToolBaseShared toolBaseShared;
        protected virtual void Start() {
            toolBaseShared = GetComponent<ToolBaseShared>();
            toolBaseShared.equippedEvent += OnEquip;
            toolBaseShared.unequippedEvent += OnUnequip;
            enabled = false;
        }
        protected virtual void OnEquip(ToolBaseShared _) {
            enabled = true;
        }
        protected virtual void OnUnequip(ToolBaseShared _) {
            enabled = false;
        }
    }
}
