using System;
using System.Collections;
using UnityEngine;

namespace RyanAssets.Tools.Shared {
    [RequireComponent(typeof(Collider))]
    public class ToolHitDetection : MonoBehaviour {
        public event Action<Collider> CollisionEntered;

        void OnTriggerEnter(UnityEngine.Collider other) {
            CollisionEntered?.Invoke(other);
        }

        void OnCollisionEnter(UnityEngine.Collision collision) {
            CollisionEntered?.Invoke(collision.collider);
        }
    }
}
