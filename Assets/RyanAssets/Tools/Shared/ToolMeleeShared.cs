using FishNet.Object;
using System;
using System.Collections;
using UnityEngine;

namespace RyanAssets.Tools.Shared {
    public class ToolMeleeShared : ToolBaseShared {
        public event Action<NetworkBehaviour> hitEvent;
        public void OnHit(NetworkBehaviour gameCharacter) {
#if UNITY_SERVER
            hitEvent.Invoke(gameCharacter);
#else
            _OnHitRpc(gameCharacter);
#endif
        }
        [ServerRpc(RequireOwnership = true)]
        public void _OnHitRpc(NetworkBehaviour gameCharacter) {
            OnHit(gameCharacter);
        }
    }
}