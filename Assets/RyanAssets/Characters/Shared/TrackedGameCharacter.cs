using System;
using FishNet.Object;
using FishNet.Connection;
using UnityEngine;
using FishNet.Object.Synchronizing;
using RyanAssets.Shared.Declarations;

namespace RyanAssets.Characters.Shared {
    public class TrackedGameCharacter : GameCharacter {
#if UNITY_SERVER
        protected override void Died(DamageType source, NetworkObject sourceObject) {
            base.Died(source, sourceObject);
            RpcDied(source, sourceObject);
        }
#endif
        [ObserversRpc]
        private void RpcDied(DamageType source, NetworkObject sourceObject) {
            SharedDied(source, sourceObject);
        }
    }
}