using FishNet.Object;
using FishNet.Object.Synchronizing;
using RyanAssets.Core;
using RyanAssets.Shared.Declarations;
using RyanAssets.Shared.WorldUI;
using UnityEngine;

namespace Universes.UniverseData.war_valley.Shared {
    /// <summary>
    /// Runs a placed structure through a timed construction phase before it becomes operational.
    /// <para>
    /// Build time comes from <see cref="StructureComponent.Duration"/>, which already exists as the
    /// authored per-structure setting, so a hangar takes longer than a fence without introducing a
    /// second source of truth. While the site is building it stands inside its construction hoarding,
    /// rises out of the ground at its authored proportions, and carries only a fraction of its
    /// health; every War Valley behaviour that makes a structure do something gates itself on
    /// <see cref="IsOperational"/>.
    /// </para>
    /// <para>
    /// The countdown floating over the site is the shared <see cref="WorldTimerBar"/>, handed the
    /// same replicated deadline this component works from. Nothing about the bar's progress is sent
    /// separately: every client derives it from <see cref="completionTime"/> and
    /// <see cref="buildDuration"/>, which is why the bar and the building rise in step.
    /// </para>
    /// </summary>
    [RequireComponent(typeof(StructureComponent))]
    public sealed class WV_Constructable : NetworkBehaviour, IGridFootprint {
        [Header("Structure")]
        [Tooltip("Grid cells this structure spans on its longer side. Placement snaps by this rather " +
                 "than by the model's measured size, so a fence post can sit on the grid point two " +
                 "fence runs meet at.")]
        [SerializeField, Min(1)] int footprintCells = 1;
        [Tooltip("Health of the finished structure. The site starts at a fraction of this.")]
        [SerializeField, Min(1)] long maxHealth = 500;

        [Header("Authored References")]
        [Tooltip("The finished building's visuals. Scaled up out of the ground as construction progresses.")]
        [SerializeField] Transform buildingRoot;
        [Tooltip("Construction hoarding placed around the slot. Hidden once the structure completes.")]
        [SerializeField] GameObject constructionScaffold;
        [Tooltip("Countdown and progress bar drawn above the site while it is being built.")]
        [SerializeField] WorldTimerBar buildTimer;

        [Header("Presentation")]
        [Tooltip("How far below the ground the finished building starts, as a fraction of its height. " +
                 "1 means it begins completely buried and rises into view as the build runs.")]
        [SerializeField, Range(0.1f, 1f)] float startingSubmergedFraction = 0.95f;

        /// <summary>Server clock reading at which construction finishes. Zero before the site is initialized.</summary>
        readonly SyncVar<float> completionTime = new();
        readonly SyncVar<float> buildDuration = new();
        readonly SyncVar<bool> complete = new();

        StructureComponent structure;
        WV_Owned owned;
        Vector3 finishedLocalPosition;
        float buildingHeight;

        public int FootprintCells => footprintCells;
        public long MaxHealth => maxHealth;

        /// <summary>True once the build timer has elapsed. Every gameplay behaviour checks this first.</summary>
        public bool IsOperational => complete.Value;

        /// <summary>The replicated deadline, as a <see cref="NetworkHelper.ServerTime"/> reading.</summary>
        public float CompletionServerTime => completionTime.Value;

        public float BuildDuration => buildDuration.Value;

        public float SecondsRemaining =>
            complete.Value ? 0f : Mathf.Max(0f, completionTime.Value - NetworkHelper.ServerTime);

        /// <summary>0 while the site is fresh, 1 the moment it completes. Drives the visuals and HUD.</summary>
        public float Progress {
            get {
                if (complete.Value)
                    return 1f;
                float duration = buildDuration.Value;
                return duration <= 0f ? 0f : Mathf.Clamp01(1f - SecondsRemaining / duration);
            }
        }

        /// <summary>
        /// 0 to 1: how intact the structure is against the health it should have right now. A
        /// finished building is measured against its full health; a site is measured against the
        /// health its construction has reached, because a site that is only a quarter built is not
        /// a quarter destroyed.
        /// </summary>
        public float Integrity {
            get {
                if (structure.IsDead)
                    return 0f;
                float expected = complete.Value
                    ? maxHealth
                    : maxHealth * Mathf.Lerp(WV_Rules.UnderConstructionHealthFraction, 1f, Progress);
                return expected <= 0f ? 1f : Mathf.Clamp01(structure.Health.Value / expected);
            }
        }

        void Awake() {
            structure = GetComponent<StructureComponent>();
            owned = GetComponent<WV_Owned>();
            if (buildingRoot == null)
                return;

            finishedLocalPosition = buildingRoot.localPosition;
            buildingHeight = MeasureBuildingHeight();
        }

        /// <summary>
        /// Height of the finished building in the parent's local space, which is how far it has to
        /// travel to clear the ground. Measured once from the authored renderers rather than per
        /// frame, and falling back to one grid cell if the model has no renderers to measure.
        /// </summary>
        float MeasureBuildingHeight() {
            Renderer[] renderers = buildingRoot.GetComponentsInChildren<Renderer>(true);
            bool measured = false;
            Bounds bounds = default;
            foreach (Renderer renderer in renderers) {
                if (!measured) {
                    bounds = renderer.bounds;
                    measured = true;
                } else {
                    bounds.Encapsulate(renderer.bounds);
                }
            }

            if (!measured)
                return WV_Rules.GridSize;

            float parentScale = transform.lossyScale.y;
            float localHeight = parentScale > 0.0001f ? bounds.size.y / parentScale : bounds.size.y;
            return Mathf.Max(localHeight, 0.1f);
        }

        public override void OnStartNetwork() {
            base.OnStartNetwork();
            complete.OnChange += HandleCompleteChanged;
            // A client receives the deadline as part of the object's first state, after Awake. The
            // bar is started from that callback rather than from OnStartNetwork alone, so a site
            // spawned mid-build shows the right countdown instead of an empty bar.
            completionTime.OnChange += HandleDeadlineChanged;
            if (owned != null)
                owned.OwnerChanged += HandleOwnerChanged;
            ApplyPresentation();
        }

        public override void OnStopNetwork() {
            complete.OnChange -= HandleCompleteChanged;
            completionTime.OnChange -= HandleDeadlineChanged;
            if (owned != null)
                owned.OwnerChanged -= HandleOwnerChanged;
            base.OnStopNetwork();
        }

        void HandleCompleteChanged(bool previous, bool next, bool asServer) => ApplyPresentation();

        void HandleDeadlineChanged(float previous, float next, bool asServer) => ApplyPresentation();

        void HandleOwnerChanged(int clientId) => ApplyTimerOwnerColor();

        void Update() {
            if (complete.Value)
                return;

#if UNITY_SERVER
            TickConstruction();
            if (complete.Value)
                return;
#endif
            // Clients hold no authoritative timer; they interpolate the same replicated deadline so
            // the building visibly rises in step with the server's countdown.
            ApplyProgressVisuals(Progress);
        }

        void ApplyPresentation() {
            bool operational = complete.Value;
            if (constructionScaffold != null)
                constructionScaffold.SetActive(!operational);
            ApplyProgressVisuals(operational ? 1f : Progress);
            ApplyTimerState();
        }

        void ApplyTimerState() {
            if (buildTimer == null)
                return;

            if (complete.Value || completionTime.Value <= 0f) {
                buildTimer.Stop();
                return;
            }

            ApplyTimerOwnerColor();
            buildTimer.StartCountdown(completionTime.Value, buildDuration.Value);
        }

        void ApplyTimerOwnerColor() {
            if (buildTimer == null || owned == null)
                return;
            buildTimer.SetAccentColor(WV_Rules.GetCommanderUIColor(owned.OwnerClientId));
        }

        /// <summary>
        /// Slides the finished building up out of the ground as the build runs, at its authored
        /// scale. Scaling it vertically instead would squash the model flat and distort every
        /// proportion of the art while the site was under construction.
        /// </summary>
        void ApplyProgressVisuals(float progress) {
            if (buildingRoot == null)
                return;

            float submerged = buildingHeight * startingSubmergedFraction * (1f - Mathf.Clamp01(progress));
            Vector3 position = finishedLocalPosition;
            position.y = finishedLocalPosition.y - submerged;
            buildingRoot.localPosition = position;
        }

#if UNITY_SERVER
        /// <summary>
        /// Debug multiplier on every construction timer, where 1 is the authored build time and 0
        /// completes a site almost immediately. Static and set once by the universe's runner, the
        /// same way <see cref="WV_ProductionBuilding.BuildDurationMultiplier"/> is, so one runner
        /// setting speeds up every structure without editing thirteen prefabs.
        /// </summary>
        public static float ConstructionDurationMultiplier { get; set; } = 1f;

        /// <summary>
        /// Starts the build timer. The placement path calls this immediately after the site spawns so
        /// the deadline replicates with the object's first state.
        /// </summary>
        [Server]
        public void BeginConstruction() {
            // Floored rather than zeroed even at an instant multiplier: a site that completes in the
            // frame it spawns never replicates its construction state, which skips the owner's colour
            // and the scaffold teardown clients key off.
            float duration = Mathf.Max(
                0.1f,
                Mathf.Max(WV_Rules.MinConstructionSeconds, structure.Duration)
                    * Mathf.Max(0f, ConstructionDurationMultiplier));
            buildDuration.Value = duration;
            completionTime.Value = NetworkHelper.ServerTime + duration;
            complete.Value = false;

            // A site under construction is deliberately fragile: it is worth attacking before it
            // finishes. Health scales back up to the authored maximum as the build completes.
            long startingHealth = System.Math.Max(1L, (long)(maxHealth * WV_Rules.UnderConstructionHealthFraction));
            structure.Init(startingHealth, maxHealth);
            ApplyPresentation();
        }

        void TickConstruction() {
            if (!IsServerStarted || completionTime.Value <= 0f || structure.IsDead)
                return;

            // Grow toward full health alongside the build so a nearly finished structure is not still
            // one-shot, without ever healing battle damage past the current progress ceiling.
            long floor = (long)(maxHealth * Mathf.Lerp(WV_Rules.UnderConstructionHealthFraction, 1f, Progress));
            if (structure.Health.Value < floor)
                structure.HealHealth(floor - structure.Health.Value);

            if (NetworkHelper.ServerTime < completionTime.Value)
                return;

            complete.Value = true;
            if (structure.Health.Value < maxHealth)
                structure.HealHealth(maxHealth - structure.Health.Value);
            ApplyPresentation();
        }
#endif
    }
}
