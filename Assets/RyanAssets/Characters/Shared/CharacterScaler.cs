using System.Collections.Generic;
using UnityEngine;

namespace RyanAssets.Characters.Shared {
    public class CharacterScaler : MonoBehaviour {

        private struct JointData {
            public Joint joint;
            public Vector3 originalAnchor;
            public Vector3 originalConnectedAnchor;
        }

        private List<JointData> _jointData = new List<JointData>();
        private Vector3 _originalScale;
        private bool _initialized = false;

        private void Init() {
            _originalScale = transform.localScale;
            _jointData.Clear();

            foreach (Joint j in transform.GetComponentsInChildren<Joint>(true)) {
                _jointData.Add(new JointData {
                    joint = j,
                    originalAnchor = j.anchor,
                    originalConnectedAnchor = j.connectedAnchor
                });
            }

            _initialized = true;
        }

        public void SetScale(Vector3 newScale) {
            if (!_initialized) Init();

            transform.localScale = newScale;

            // Per-axis ratio between new scale and the scale anchors were recorded at
            Vector3 ratio = new Vector3(
                newScale.x / _originalScale.x,
                newScale.y / _originalScale.y,
                newScale.z / _originalScale.z
            );

            foreach (JointData data in _jointData) {
                if (data.joint == null) continue;

                // anchor is in the joint body's local space — scale it directly
                data.joint.anchor = Vector3.Scale(data.originalAnchor, ratio);

                // connectedAnchor is in the connected body's local space.
                // If the connected body is also a child of this transform, it
                // needs the same ratio applied. If it's world-space (no connected
                // body), you likely want to leave it alone or handle separately.
                data.joint.connectedAnchor = Vector3.Scale(data.originalConnectedAnchor, ratio);
            }
        }

        // Call this if you dynamically add ragdoll bodies at runtime
        public void Reinitialize() {
            _initialized = false;
            Init();
        }
    }
}