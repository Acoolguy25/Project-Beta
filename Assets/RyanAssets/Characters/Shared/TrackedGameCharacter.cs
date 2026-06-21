using System;
using FishNet.Object;
using FishNet.Connection;
using UnityEngine;
using FishNet.Object.Synchronizing;

namespace RyanAssets.Characters.Shared {
    public class TrackedGameCharacter : GameCharacter {
        protected override void Died(DamageSource source) {
            base.Died(source);
            RpcDied(source);
        }
        [ObserversRpc]
        private void RpcDied(DamageSource source) {
            OnDied?.Invoke(source);
        }
    }
}