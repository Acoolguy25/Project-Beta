using System;
using FishNet.Object;
using FishNet.Connection;
using UnityEngine;
using FishNet.Object.Synchronizing;

namespace RyanAssets.Characters.Shared {
    public class TrackedGameCharacter : GameCharacter {
        protected override void Died(DamageSource source, NetworkObject sourceObject) {
            base.Died(source, sourceObject);
            RpcDied(source, sourceObject);
        }
        [ObserversRpc]
        private void RpcDied(DamageSource source, NetworkObject sourceObject) {
            SharedDied(source, sourceObject);
        }
    }
}