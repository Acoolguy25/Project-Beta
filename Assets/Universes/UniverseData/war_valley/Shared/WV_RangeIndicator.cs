using RyanAssets.Shared.Declarations;
using RyanAssets.Shared.Component;
using UnityEngine;

namespace Universes.UniverseData.war_valley.Shared {
    /// <summary>Projects an authored range outline onto the ground without following aircraft height.</summary>
    public sealed class WV_RangeIndicator : MonoBehaviour {
        [SerializeField] LineRenderer outline;
        [SerializeField] WV_Unit unit;
        [SerializeField] WV_DefenseTurret turret;
        [SerializeField] StructureComponent structure;
        [SerializeField] WV_Constructable constructable;
        [SerializeField, Min(0f)] float radius;
        EntityBase entity;
        bool selectedLocally;

        const float GroundOffset = 0.15f;
        Vector3[] authoredPoints;
        Vector3[] projectedPoints;
        bool projectionDirty = true;
        int groundMask;
        Vector3 lastPosition = new(float.PositiveInfinity, 0f, 0f);
        float lastRadius;

        public float Radius => unit != null ? unit.AttackRange : turret != null ? turret.Range : radius;

        public void SetRadius(float value) {
            radius = Mathf.Max(0f, value);
            projectionDirty = true;
        }

        public void BindEntity(EntityBase value) => entity = value;

        public void SetSelected(bool value) {
            selectedLocally = value;
            projectionDirty = true;
            if (!value && outline != null)
                outline.enabled = false;
        }

        void Awake() {
            if (outline == null) {
                Debug.LogError($"{name} has no authored range outline.", this);
                enabled = false;
                return;
            }
            authoredPoints = new Vector3[outline.positionCount];
            projectedPoints = new Vector3[outline.positionCount];
            groundMask = LayerMask.GetMask("Default", "Ground");
            outline.GetPositions(authoredPoints);
            outline.useWorldSpace = true;
            outline.enabled = false;
        }

        void LateUpdate() {
#if UNITY_SERVER
            outline.enabled = false;
#else
            bool selected = unit != null ? unit.SelectedLocally : selectedLocally;
            float currentRadius = Radius;
            bool visible = selected && currentRadius > 0f && (unit == null || !unit.IsDead)
                && (entity == null || !entity.IsDead)
                && (structure == null || !structure.IsDead)
                && (constructable == null || constructable.IsOperational);
            bool wasVisible = outline.enabled;
            outline.enabled = visible;
            if (!visible)
                return;

            Vector3 origin = transform.position;
            outline.startColor = outline.endColor = unit != null && unit.SelectedLocally
                ? new Color(0.35f, 1f, 0.7f, 0.9f) : new Color(0.45f, 0.85f, 1f, 0.45f);
            // Follow the displayed unit every frame, after movement/interpolation. A time gate
            // or movement dead zone makes this world-space outline visibly trail its owner.
            // Only selected, changed outlines need the ground queries; the buffers are reused.
            if (!projectionDirty && wasVisible && origin == lastPosition && Mathf.Approximately(lastRadius, currentRadius))
                return;
            projectionDirty = false;
            lastPosition = origin;
            lastRadius = currentRadius;
            for (int i = 0; i < authoredPoints.Length; i++) {
                Vector3 point = origin + authoredPoints[i] * currentRadius;
                // Terrain alone defines the footprint. Buildings and airborne units must not
                // lift the outline onto their roofs, or lift an aircraft's flight band repeatedly.
                if (Physics.Raycast(point + Vector3.up * 400f, Vector3.down, out RaycastHit hit,
                        900f, groundMask, QueryTriggerInteraction.Ignore))
                    point.y = hit.point.y;
                else if (unit != null && unit.IsAircraft)
                    point.y -= WV_Rules.GetCruiseAltitude(unit.Kind);
                point.y += GroundOffset;
                projectedPoints[i] = point;
            }
            outline.SetPositions(projectedPoints);
#endif
        }
    }
}
