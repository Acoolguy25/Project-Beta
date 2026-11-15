using UnityEngine;

namespace RyanAssets.Platformer {
    /// <summary>Client-side idle spin and bob for collectibles and markers. Purely visual.</summary>
    public sealed class Spinner : MonoBehaviour {
        public Vector3 degreesPerSecond = new(0f, 120f, 0f);
        [Min(0f)] public float bobHeight = 0.2f;
        [Min(0f)] public float bobSpeed = 2f;
        Vector3 basePosition;
        void Awake() => basePosition = transform.localPosition;
#if !UNITY_SERVER
        void Update() {
            transform.Rotate(degreesPerSecond * Time.deltaTime, Space.Self);
            transform.localPosition = basePosition + Vector3.up * (Mathf.Sin(Time.time * bobSpeed) * bobHeight);
        }
#endif
    }
}
