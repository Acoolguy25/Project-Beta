using System;
using UnityEngine;

namespace RyanAssets.Core {
    /// <summary>Places a body without sweeping it, notifying riders before its old support pose disappears.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody))]
    public sealed class RigidbodyReposition : MonoBehaviour {
        public event Action<Vector3, Quaternion> Repositioning;

        public void Reposition(Vector3 position, Quaternion rotation) {
            Repositioning?.Invoke(position, rotation);
            var body = GetComponent<Rigidbody>();
            var interpolation = body.interpolation;
            body.interpolation = RigidbodyInterpolation.None;
            body.position = position;
            body.rotation = rotation;
            body.interpolation = interpolation;
        }
    }
}
