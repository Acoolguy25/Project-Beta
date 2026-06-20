using System;
using FishNet.Object;
using FishNet.Connection;
using UnityEngine;
using FishNet.Object.Synchronizing;

namespace RyanAssets.Characters.Shared {
    public class TrackedGameCharacter : GameCharacter {
#if !UNITY_SERVER
        protected virtual void Awake() {
            Health.OnChange += OnHealthChange;
        }
        protected virtual void OnHealthChange(long oldHealth, long newHealth, bool asServer) {
            if (newHealth == 0 && MaxHealth.Value > 0 && !asServer) {
                base.OnDied?.Invoke();
            }
        }
#endif
    }
}