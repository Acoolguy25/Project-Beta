using UnityEngine;

namespace RyanAssets.ControlLocking {
    public interface IControlLockProvider {
        void LockControls();
        void UnlockControls();
    }

    public static class ControlLockService {
        private static IControlLockProvider provider;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Init() {
            provider = null;
        }

        public static void Register(IControlLockProvider controlLockProvider) {
            provider = controlLockProvider;
        }

        public static void Unregister(IControlLockProvider controlLockProvider) {
            if (provider == controlLockProvider)
                provider = null;
        }

        public static void LockControls() {
            provider?.LockControls();
        }

        public static void UnlockControls() {
            provider?.UnlockControls();
        }
    }
}
