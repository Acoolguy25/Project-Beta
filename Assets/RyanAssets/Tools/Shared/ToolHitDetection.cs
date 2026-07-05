using System;
using System.Collections;
using UnityEngine;

namespace RyanAssets.Tools.Shared {
    public class ToolHitDetection : MonoBehaviour {
        public event Action<Collision> CollisionEntered;

        void OnCollisionEnter(Collision collision) {
            CollisionEntered?.Invoke(collision);
        }
    }
}