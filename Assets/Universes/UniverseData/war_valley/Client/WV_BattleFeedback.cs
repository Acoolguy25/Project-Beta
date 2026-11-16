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
        [Tooltip("Size of each dust puff kicked up along the footprint's edge.")]
        [Min(0.1f)] public float constructionDustScale = 3f;
        [Tooltip("Puffs along each side of the footprint per grid cell it spans; a hangar kicks up a " +
                 "longer skirt of dust than a watchtower.")]
        [Min(1)] public int constructionDustPerCell = 2;
        [Tooltip("How far outside the footprint's edge the dust rises (m), so it frames the building " +
                 "rather than hiding it.")]
        [Min(0f)] public float constructionDustSpread = 1f;
        [Tooltip("Dirt-toned, a step lighter than the valley floor, so it reads as dust thrown up from " +
                 "the ground rather than cloud.")]
        public Color constructionDustTint = new(0.86f, 0.76f, 0.6f, 1f);
        [Tooltip("How hard a finished building settles: a squash on its model, as a fraction of its height.")]
        [Range(0f, 0.5f)] public float finishSquash = 0.12f;
        [Min(0.05f)] public float finishSquashSeconds = 0.4f;

        [Header("Fallbacks")]
        [Tooltip("Height used for the top of something with no collider to measure (m).")]
        [Min(0f)] public float fallbackHeight = 2f;
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
        /// <summary>A finished building's build sound, pitched down so it reads as a settling thud.</summary>
        const float FinishedPitchSemitones = -3f;

        sealed class Tracked {
            public EntityBase Entity;
            public Action Unbind;
        }

        readonly WV_BattleFeedbackTuning tuning;
        readonly WV_CommanderSelection selection;
        readonly Dictionary<EntityBase, Tracked> tracked = new();
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
            // Soldiers are not in the entity registry: characters publish their own roster.
            GameCharacter.GameCharacterAdded += HandleCharacterAdded;
            GameCharacter.GameCharacterRemoved += HandleEntityRemoved;
            WV_UnitAnimation.ShotShown += HandleShotShown;
            // Whatever already stands is adopted silently, so joining a round in progress does not
            // set off a dust storm over every building on the map.
            foreach (EntityBase entity in EntityBase.All)
                Track(entity, announce: false);
            foreach (List<GameCharacter> team in GameCharacter.TeamToCharacter.Values) {
                foreach (GameCharacter character in team)
                    Track(character, announce: false);
            }
        }

        public void Disable() {
            if (!enabled)
                return;
            enabled = false;
            EntityBase.EntityAdded -= HandleEntityAdded;
            EntityBase.EntityRemoved -= HandleEntityRemoved;
            GameCharacter.GameCharacterAdded -= HandleCharacterAdded;
            GameCharacter.GameCharacterRemoved -= HandleEntityRemoved;
            WV_UnitAnimation.ShotShown -= HandleShotShown;
            foreach (Tracked entry in tracked.Values)
                entry.Unbind?.Invoke();
            tracked.Clear();
        }

        // --- Tracking -----------------------------------------------------------

        void HandleEntityAdded(EntityBase entity) => Track(entity, announce: true);

        void HandleCharacterAdded(GameCharacter character) => Track(character, announce: false);

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
            entity.TryGetComponent(out WV_Constructable constructable);
            if (constructable != null)
                constructable.Completed += PlayFinished;
            tracked.Add(entity, new Tracked {
                Entity = entity,
                Unbind = () => {
                    if (constructable != null)
                        constructable.Completed -= PlayFinished;
                    if (entity == null || entity.HealthComponent == null)
                        return;
                    entity.Health.OnChange -= OnHealthChanged;
                    entity.OnDied -= OnDied;
                }
            });

            if (announce && constructable != null && !constructable.IsOperational)
                PlayPlaced(constructable);
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

        Vector3 GetCentre(Component entity) =>
            entity.TryGetComponent(out Collider body)
                ? body.bounds.center
                : entity.transform.position + Vector3.up * (tuning.fallbackHeight * 0.5f);

        Vector3 GetTop(Component entity) {
            if (!entity.TryGetComponent(out Collider body))
                return entity.transform.position + Vector3.up * tuning.fallbackHeight;
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

        /// <summary>
        /// A skirt of dust kicked up at ground level all around the footprint's edge: more puffs the
        /// more cells the building covers, and the building itself left in view.
        /// </summary>
        void PlayDust(WV_Constructable site) {
            Transform root = site.transform;
            Bounds local = site.LocalPlacementBounds;
            Vector3 lossy = root.lossyScale;
            float halfX = Mathf.Abs(local.extents.x * lossy.x) + tuning.constructionDustSpread;
            float halfZ = Mathf.Abs(local.extents.z * lossy.z) + tuning.constructionDustSpread;
            Vector3 centre = root.TransformPoint(new Vector3(local.center.x, 0f, local.center.z));
            centre.y = root.position.y;
            int perSide = Mathf.Max(1, site.FootprintCells) * tuning.constructionDustPerCell;
            Vector3 right = root.right;
            Vector3 forward = root.forward;
            // Walk the four sides corner to corner; each side starts at its own corner, so the
            // corners are covered once.
            for (int side = 0; side < 4; side++) {
                for (int i = 0; i < perSide; i++) {
                    float t = Mathf.Lerp(-1f, 1f, i / (float)perSide);
                    Vector2 edge = side switch {
                        0 => new Vector2(t, -1f),
                        1 => new Vector2(1f, t),
                        2 => new Vector2(-t, 1f),
                        _ => new Vector2(-1f, -t)
                    };
                    Vector3 point = centre + right * (edge.x * halfX) + forward * (edge.y * halfZ);
                    GameFx.Play(FxKind.DustPuff, point, tuning.constructionDustTint, tuning.constructionDustScale);
                }
            }
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
