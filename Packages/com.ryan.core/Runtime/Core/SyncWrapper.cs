using FishNet.Object.Synchronizing;
using System;
using System.Collections;
using UnityEngine;

namespace RyanAssets.Core.Assets.RyanAssets.Core {
    public class SyncWrapper<T> : SyncVar<T> {
        public event Action OnSynced;
        protected override void Initialized() {
            base.Initialized();
            OnChange += OnWrapperHandleChanged;
        }
        public void Subscribe(Action func, bool immediatelyRun = true) {
            OnSynced += func;
            if (immediatelyRun)
                func.Invoke();
        }
        public void Unsubscribe(Action func) {
            OnSynced -= func;
        }
        void OnWrapperHandleChanged(T oldValue, T newValue, bool asServer) {
            OnSynced?.Invoke();
        }

        // Constructors
        public SyncWrapper(SyncTypeSettings settings = new()) : this(default, settings) { }
        public SyncWrapper(T initialValue, SyncTypeSettings settings = new()) : base(settings) => SetInitialValues(initialValue);
    }
}