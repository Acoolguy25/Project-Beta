using FishNet.Object;
using System;
using System.Collections;
using UnityEngine;

namespace RyanAssets.Tools.Shared {
    public class ToolMeleeShared : ToolBaseShared {
        public event Action<NetworkBehaviour> hitEvent;
        [ServerRpc(RequireOwnership = true)]
        public void OnHit(NetworkBehaviour gameCharacter) {
            hitEvent.Invoke(gameCharacter);
        }
    }
}