using UnityEngine;

namespace RyanAssets.Core {
    // Runs after FishNet reads connection state and before ordinary gameplay Updates.
    [DefaultExecutionOrder(-200)]
    internal sealed class NetworkMotionClockDriver : MonoBehaviour {
        private void Update() => NetworkHelper.UpdateMotionClocks();
    }
}
