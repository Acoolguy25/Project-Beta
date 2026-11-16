using System;
using System.Collections.Generic;
using RyanAssets.Characters.Shared;
using RyanAssets.GameFeel;
using RyanAssets.Shared.Component;
using RyanAssets.Shared.Declarations;
using UnityEngine;
using Universes.UniverseData.war_valley.Shared;

namespace Universes.UniverseData.war_valley.Client {
    /// <summary>Tuning for <see cref="WV_BattleFeedback"/>, serialized on the client controller.</summary>
    [Serializable]
    public sealed class WV_BattleFeedbackTuning {
        [Tooltip("Effects further than this from the camera are skipped (m): nobody sees them, and a " +
                 "big battle across the valley should not cost frames here.")]
        [Min(10f)] public float effectsDistance = 240f;

        [Header("Hits")]
        [Tooltip("Flash colour on a soldier that is hit.")]
        public Color hitFlashColor = new(1f, 0.82f, 0.68f, 1f);
        [Tooltip("Damage numbers show over the commander's selected units and troops, and over enemies " +
                 "within this distance of one of them (m), so the fight being watched reads in numbers " +
                 "while the rest of the valley stays quiet.")]
        [Min(0f)] public float selectedFightRadius = 32f;
        [Min(0.1f)] public float damageNumberSize = 1f;

        [Header("Deaths")]
        [Tooltip("Size of the dust a soldier goes down in. The dust fills the soldier's whole height " +
                 "(the robots are about 5.6 m tall), so it reads from the commander's camera.")]
        [Min(0.1f)] public float soldierBurstScale = 1.2f;
        [Tooltip("Size of the sparks thrown from a falling soldier's chest.")]
        [Min(0.1f)] public float soldierSparkScale = 1.4f;
        [Tooltip("Spark colour when a wave enemy falls.")]
        public Color enemyDeathTint = new(1f, 0.45f, 0.35f, 1f);
        [Tooltip("Spark colour when one of the defenders' soldiers falls.")]
        public Color defenderDeathTint = new(0.6f, 0.9f, 1f, 1f);
        [Tooltip("Camera-shake radius of a destroyed vehicle (m).")]
        [Min(1f)] public float vehicleExplosionRadius = 50f;
        [Tooltip("Size of the smoke left over a destroyed building, per grid cell of its footprint.")]
        [Min(0.1f)] public float structureSmokeScale = 2.2f;

        [Header("Shells")]
        [Tooltip("Camera-shake radius of an artillery shell or a tank round (m).")]
        [Min(1f)] public float shellShakeRadius = 35f;
        [Tooltip("Size of a tank round's impact blast.")]
        [Min(0.1f)] public float tankImpactScale = 2.5f;
        [Range(0f, 1f)] public float tankShakeStrength = 0.35f;
        [Min(0.05f)] public float tankShakeSeconds = 0.3f;
        [Range(0f, 1f)] public float tankReportVolume = 0.7f;

        [Header("Construction")]
        [Tooltip("Size of each puff in the dust cloud a new site or finished building throws up.")]
        [Min(0.1f)] public float constructionDustScale = 1.6f;
        [Tooltip("Height of that dust cloud per grid cell of the footprint (m); a hangar raises a taller " +
                 "cloud than a watchtower.")]
        [Min(0.5f)] public float constructionDustHeight = 4f;
        [Tooltip("Pale, so the cloud reads against the valley's brown dirt rather than vanishing into it.")]
        public Color constructionDustTint = new(1f, 0.96f, 0.88f, 1f);
        [Tooltip("How hard a finished building settles: a squash on its model, as a fraction of its height.")]
        [Range(0f, 0.5f)] public float finishSquash = 0.12f;
        [Min(0.05f)] public float finishSquashSeconds = 0.4f;
    }

    /// <summary>
    /// What the battle looks and sounds like on this client, on top of the replicated shots: vehicle
    /// rounds that blow up where they land, soldiers that flash when hit and go down in a puff,
    /// vehicles that explode and shake the camera, smoke over fallen buildings, damage numbers over the fight the
    /// commander is watching, and buildings that throw up dust when placed and settle with a thud
    /// when finished (they already rise out of the ground as they are built, in
    /// <see cref="WV_Constructable"/>).
    /// <para>
    /// Everything is a reaction to state the server already replicates - health, deaths, the
    /// construction flag, and the unit shot RPC - so nothing here can disagree with the game.
    /// </para>
    /// </summary>
    public sealed class WV_BattleFeedback {
        /// <summary>Seconds between sweeps for finished buildings.</summary>
        const float ConstructionSweepSeconds = 0.25f;
        /// <summary>A finished building's build sound, pitched down so it reads as a settling thud.</summary>
        const float FinishedPitchSemitones = -3f;

        sealed class Tracked {
            public EntityBase Entity;
            public Action Unbind;
        }

        sealed class Site {
            public WV_Constructable Constructable;
            public bool WasOperational;
        }

        readonly WV_BattleFeedbackTuning tuning;
        readonly WV_CommanderSelection selection;
        readonly Dictionary<EntityBase, Tracked> tracked = new();
        readonly List<Site> sites = new();
        float nextSweep;
        bool enabled;

        public WV_BattleFeedback(WV_BattleFeedbackTuning tuning, WV_CommanderSelection selection) {
            this.tuning = tuning ?? new WV_BattleFeedbackTuning();
            this.selection = selection;
        }

        public void Enable() {
            if (enabled)
                return;
            enabled = true;
            EntityBase.EntityAdded += HandleEntityAdded;
            EntityBase.EntityRemoved += HandleEntityRemoved;
            WV_UnitAnimation.ShotShown += HandleShotShown;
            // Whatever already stands is adopted silently, so joining a round in progress does not
            // set off a dust storm over every building on the map.
            foreach (EntityBase entity in EntityBase.All)
                Track(entity, announce: false);
        }

        public void Disable() {
            if (!enabled)
                return;
            enabled = false;
            EntityBase.EntityAdded -= HandleEntityAdded;
            EntityBase.EntityRemoved -= HandleEntityRemoved;
            WV_UnitAnimation.ShotShown -= HandleShotShown;
            foreach (Tracked entry in tracked.Values)
                entry.Unbind?.Invoke();
            tracked.Clear();
            sites.Clear();
        }

        /// <summary>Notices finished buildings. Called every frame; sweeps a few times a second.</summary>
        public void Tick() {
            float now = Time.unscaledTime;
            if (now < nextSweep)
                return;
            nextSweep = now + ConstructionSweepSeconds;
            for (int i = sites.Count - 1; i >= 0; i--) {
                Site site = sites[i];
                if (site.Constructable == null) {
                    sites.RemoveAt(i);
                    continue;
                }
                bool operational = site.Constructable.IsOperational;
                if (operational && !site.WasOperational)
                    PlayFinished(site.Constructable);
                site.WasOperational = operational;
            }
        }

        // --- Tracking -----------------------------------------------------------

        void HandleEntityAdded(EntityBase entity) => Track(entity, announce: true);

        void HandleEntityRemoved(EntityBase entity) {
            if (entity != null && tracked.Remove(entity, out Tracked entry))
                entry.Unbind?.Invoke();
        }

        void Track(EntityBase entity, bool announce) {
            if (entity == null || entity.HealthComponent == null || tracked.ContainsKey(entity))
                return;

            void OnHealthChanged(long previous, long next, bool asServer) {
                if (!asServer && next < previous && previous > 0)
                    HandleDamaged(entity, previous - next);
            }
            void OnDied(DamageType source, IEntity attacker) {
                if (source is not (DamageType.Reset or DamageType.Despawn))
                    HandleDied(entity);
            }

            entity.Health.OnChange += OnHealthChanged;
            entity.OnDied += OnDied;
            tracked.Add(entity, new Tracked {
                Entity = entity,
                Unbind = () => {
                    if (entity == null || entity.HealthComponent == null)
                        return;
                    entity.Health.OnChange -= OnHealthChanged;
                    entity.OnDied -= OnDied;
                }
            });

            if (entity.TryGetComponent(out WV_Constructable constructable)) {
                sites.Add(new Site { Constructable = constructable, WasOperational = constructable.IsOperational });
                if (announce && !constructable.IsOperational)
                    PlayPlaced(constructable);
            }
        }

        // --- Hits and deaths -----------------------------------------------------

        void HandleDamaged(EntityBase entity, long amount) {
            if (!IsWorthShowing(entity.transform.position))
                return;
            // Vehicles and buildings flash through their own owner colour; the robot soldiers do not.
            if (entity is GameCharacter character)
                HitFlash.On(character, tuning.hitFlashColor);
            if (IsInWatchedFight(entity))
                FloatingText.Damage(GetTop(entity), amount, tuning.damageNumberSize);
        }

        void HandleDied(EntityBase entity) {
            Vector3 position = entity.transform.position;
            if (!IsWorthShowing(position))
                return;
            switch (entity) {
                case WV_Unit unit:
                    GameFx.Explosion(GetCentre(entity), large: unit.Kind is WV_UnitKind.Tank or WV_UnitKind.Bomber,
                        tuning.vehicleExplosionRadius);
                    break;
                case GameCharacter:
                    Color side = IsWave(entity) ? tuning.enemyDeathTint : tuning.defenderDeathTint;
                    GameFx.Poof(position, GetTop(entity).y - position.y, tuning.soldierBurstScale);
                    GameFx.Play(FxKind.Sparks, GetCentre(entity), side, tuning.soldierSparkScale);
                    GameSfx.PlayAt(SfxCue.Kill, position);
                    break;
                case StructureComponent:
                    // The structure's shared ExplosionEffect throws its debris, and the kit gives that
                    // blast its boom and camera shake; this only adds smoke that hangs over the ruin.
                    float cells = entity.TryGetComponent(out WV_Constructable site) ? Mathf.Sqrt(site.FootprintCells) : 1f;
                    GameFx.Play(FxKind.Smoke, position, tuning.structureSmokeScale * cells);
                    break;
            }
        }

        void HandleShotShown(WV_UnitAnimation shooter, Vector3 muzzle, Vector3 impact) {
            if (shooter == null || !IsWorthShowing(impact) || !shooter.TryGetComponent(out WV_Unit unit))
                return;
            switch (unit.Kind) {
                case WV_UnitKind.Artillery:
                    GameFx.Explosion(impact, large: false, tuning.shellShakeRadius);
                    break;
                case WV_UnitKind.Bomber:
                    GameFx.Explosion(impact, large: true, tuning.shellShakeRadius);
                    break;
                case WV_UnitKind.Tank:
                    GameFx.Play(FxKind.ExplosionSmall, impact, tuning.tankImpactScale);
                    GameSfx.PlayAt(SfxCue.Explosion, impact, tuning.tankReportVolume);
                    CameraShake.Explosion(impact, tuning.shellShakeRadius, tuning.tankShakeStrength, tuning.tankShakeSeconds);
                    break;
            }
        }

        /// <summary>
        /// True for something the commander has selected, or for an enemy close to it: the fight the
        /// commander is watching, which is where numbers help rather than clutter.
        /// </summary>
        bool IsInWatchedFight(EntityBase entity) {
            if (selection == null || selection.Count == 0)
                return false;
            if (entity is WV_Unit unit && selection.Contains(unit))
                return true;
            if (entity is GameCharacter character && selection.Contains(character))
                return true;
            if (!IsWave(entity))
                return false;

            Vector3 position = entity.transform.position;
            float radiusSquared = tuning.selectedFightRadius * tuning.selectedFightRadius;
            foreach (WV_Unit member in selection.Units) {
                if (member != null && (member.transform.position - position).sqrMagnitude <= radiusSquared)
                    return true;
            }
            foreach (GameCharacter member in selection.Troops) {
                if (member != null && (member.transform.position - position).sqrMagnitude <= radiusSquared)
                    return true;
            }
            return false;
        }

        static bool IsWave(EntityBase entity) =>
            entity.Team != null && entity.Team.realTeam == WV_Alliances.GetWaveSide();

        bool IsWorthShowing(Vector3 position) {
            Camera camera = Camera.main;
            return camera != null
                   && (camera.transform.position - position).sqrMagnitude <= tuning.effectsDistance * tuning.effectsDistance;
        }

        static Vector3 GetCentre(Component entity) =>
            entity.TryGetComponent(out Collider body) ? body.bounds.center : entity.transform.position + Vector3.up;

        static Vector3 GetTop(Component entity) {
            if (!entity.TryGetComponent(out Collider body))
                return entity.transform.position + Vector3.up * 2f;
            Bounds bounds = body.bounds;
            return new Vector3(bounds.center.x, bounds.max.y, bounds.center.z);
        }

        // --- Construction -------------------------------------------------------

        void PlayPlaced(WV_Constructable site) {
            Vector3 position = site.transform.position;
            if (!IsWorthShowing(position))
                return;
            PlayDust(site);
            GameSfx.PlayAt(SfxCue.Build, position);
        }

        /// <summary>A cloud of dust over the whole footprint, as tall as the building is wide.</summary>
        void PlayDust(WV_Constructable site) {
            float height = tuning.constructionDustHeight * Mathf.Max(1, site.FootprintCells);
            GameFx.Poof(site.transform.position, height, tuning.constructionDustTint, tuning.constructionDustScale);
        }

        /// <summary>A finished building settles into place in a cloud of dust.</summary>
        void PlayFinished(WV_Constructable site) {
            Vector3 position = site.transform.position;
            if (!IsWorthShowing(position))
                return;
            PlayDust(site);
            GameSfx.PlayAt(SfxCue.Build, position, 1f, GameSfx.Semitones(FinishedPitchSemitones));
            if (site.BuildingRoot != null)
                Punch.Squash(site.BuildingRoot, tuning.finishSquash, tuning.finishSquashSeconds);
        }
    }
}
