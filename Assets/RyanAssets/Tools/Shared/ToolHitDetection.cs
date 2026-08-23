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
        private Bounds _previousBounds;
        private bool _hasPreviousBounds;

        public void Init(Transform ignoredRoot) {
            _ignoredRoot = ignoredRoot;
        }

        void Start() {
            _hitCollider = GetComponent<Collider>();
        }

        void FixedUpdate() {
            if (_hitCollider == null || !_hitCollider.enabled) {
                _wasColliderEnabled = false;
                _hasPreviousBounds = false;
                return;
            }

            if (!_wasColliderEnabled) {
                _reportedColliders.Clear();
                _wasColliderEnabled = true;
            }

            // A knife can move much farther than the small fixed-step padding while its
            // wielder or target is running. Include the previous collider bounds so an
            // animated swing is tested across its whole physics-step path, not only at its
            // final pose.
            Bounds currentBounds = _hitCollider.bounds;
            Bounds bounds = currentBounds;
            if (_hasPreviousBounds) {
                bounds.Encapsulate(_previousBounds.min);
                bounds.Encapsulate(_previousBounds.max);
            }
            _previousBounds = currentBounds;
            _hasPreviousBounds = true;
            bounds.Expand(3f);

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
            if (ShouldIgnore(other) || !_reportedColliders.Add(other))
                return;

            CollisionEntered?.Invoke(other);
        }
    }
}
