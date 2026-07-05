using System;
using System.Collections;
using UnityEngine;

namespace RyanAssets.Tools.Shared {
    public class ToolHitDetection : MonoBehaviour {
        public event Action<Collider> CollisionEntered;

        void OnTriggerEnter(UnityEngine.Collider other) {
            CollisionEntered?.Invoke(other);
        }
    }
}