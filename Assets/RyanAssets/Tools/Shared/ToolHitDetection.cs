using System;
using System.Collections.Generic;
using UnityEngine;

namespace RyanAssets.Tools.Shared {
    [RequireComponent(typeof(Collider))]
    public class ToolHitDetection : MonoBehaviour {
        public event Action<Collider> CollisionEntered;

        private readonly HashSet<Collider> _reportedColliders = new();
        private Collider _hitCollider;
        private Collider[] _overlaps = new Collider[32];
        private Transform _ignoredRoot;
        private bool _wasColliderEnabled;

        public void Init(Transform ignoredRoot) {
            _ignoredRoot = ignoredRoot;
        }

        void Start() {
            _hitCollider = GetComponent<Collider>();
        }

        void FixedUpdate() {
            if (_hitCollider == null || !_hitCollider.enabled) {
                _wasColliderEnabled = false;
                return;
            }

            if (!_wasColliderEnabled) {
                _reportedColliders.Clear();
                _wasColliderEnabled = true;
            }

            Bounds bounds = _hitCollider.bounds;
            int overlapCount;
            do {
                overlapCount = Physics.OverlapBoxNonAlloc(
                    bounds.center,
                    bounds.extents,
                    _overlaps,
                    Quaternion.identity,
                    Physics.AllLayers,
                    QueryTriggerInteraction.Collide);

                if (overlapCount < _overlaps.Length)
                    break;

                _overlaps = new Collider[_overlaps.Length * 2];
            } while (true);

            for (int i = 0; i < overlapCount; i++) {
                Collider other = _overlaps[i];
                if (ShouldIgnore(other))
                    continue;

                ReportCollision(other);
            }
        }

        private bool ShouldIgnore(Collider other) {
            return _ignoredRoot != null && other.transform.IsChildOf(_ignoredRoot);
        }

        private void ReportCollision(Collider other) {
            if (ShouldIgnore(other))
                return;

            CollisionEntered?.Invoke(other);
        }
    }
}
