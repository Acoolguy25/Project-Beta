using System.Collections;
using UnityEngine;

namespace RyanAssets.Characters.Shared {
    public class RagdollCameraHelper : IRagdoll {
        [SerializeField]
        private Transform hipController;

        private Vector3 savePositionOffset;
        void OnEnable() {
            savePositionOffset = transform.localPosition;
        }
        void OnDisable() {
            transform.localPosition = savePositionOffset;
        }
        void Update() {
            transform.position = hipController.position;
        }
    }
}