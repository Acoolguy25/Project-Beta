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
        }
        protected virtual void OnEquip(ToolBaseShared _) {

        }
        protected virtual void OnUnequip(ToolBaseShared _) {

        }
    }
}
