using RyanAssets.Tools.Shared;
using UnityEngine;

namespace RyanAssets.Tools.Server
{
    [RequireComponent(typeof(ToolBaseShared))]
    public class ToolBaseServer : MonoBehaviour {
        private ToolBaseShared toolBaseShared;
        void Start() {
            toolBaseShared = GetComponent<ToolBaseShared>();
        }
    }
}
