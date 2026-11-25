using System;
using System.Collections.Generic;
using RyanAssets.Items.Platform;
using RyanAssets.Platformer;
using UnityEngine;

namespace Universes.UniverseData.obby {
    /// <summary>
    /// Builds the generated course from authored piece prefabs. Lives in the start scene, so the
    /// server and every client build identical geometry from the seed the server broadcasts.
    /// </summary>
    public sealed class OB_Course : MonoBehaviour {
        [Header("Design")]
        [Tooltip("Movement the generator designs gaps and heights around. The server applies the same values to players.")]
        public OB_Movement movement = OB_Movement.Default;
        [Tooltip("Dash top speed (CharacterDash: walk x 1.9). The generator sizes pro routes and landing room for it; required jumps are walk-speed.")]
        [Min(0f)] public float sprintSpeed = 19f;
        [Tooltip("The difficulty curve: per-tier segments, jump reach, platform sizes and which mechanics each tier introduces.")]
        public OB_CourseRules rules = new();
        [Tooltip("Lobby floor point players spawn on before reaching a checkpoint.")]
        public Transform lobbySpawn;
        [Tooltip("Course starts at this point (the lobby exit), heading along its forward axis.")]
        public Transform courseStart;
        [Tooltip("Blocks the lobby exit between races.")]
        public GameObject startGate;

        [Header("Piece prefabs")]
        public GameObject platform;
        public GameObject checkpointPad, finishPad, lava, spring, boost, gravityZone, conveyor;
        public GameObject crumble, beat, wallJumpWall, mover, spinnerBar, spinnerCross, coin;
        [Tooltip("Overhaul mechanics (OB_Authoring assigns them; rebuild the scene after adding a slot).")]
        public GameObject beam, onOffBlock, onOffSwitchPad, onOffSwitchBlock, pendulum, crusher, laser, rollingLog, turntable;
        public GameObject cannon, mushroom, gravityFlipPad, gravityFlipOrb, risingLava, star;
        public GameObject ice, wind;
        [Tooltip("Round-2 mechanics (OB_Authoring assigns them).")]
        public GameObject ghost, honey, ring, swing, plank;

        [Header("Presentation")]
        [Tooltip("Platform materials per difficulty tier (indexed by OB_Tier), cycled per stage inside the tier.")]
        public TierMaterials[] tierPlatforms;
        [Tooltip("Beat block materials per group: solid then ghost.")]
        public Material beatSolidA, beatGhostA, beatSolidB, beatGhostB;
        [Tooltip("ON/OFF block materials: colour A (solid from the start) and colour B (solid once switched), each solid then ghost.")]
        public Material onOffSolidA, onOffGhostA, onOffSolidB, onOffGhostB;
        [Min(0.1f)] public float tileMetres = 2f;
        [Tooltip("Tint for gravity fields that turn gravity (strength-only fields keep their material colour).")]
        public Color directionalFieldTint = new(0.3f, 0.85f, 1f, 0.09f);
        [Tooltip("Client-only HUD and course presentation.")]
        public GameObject clientPrefab;

        [Serializable]
        public struct TierMaterials {
            public Material[] materials;
        }

        readonly List<GameObject> instances = new();
        Transform root;
        MaterialPropertyBlock tiling;
        static readonly int BaseMapST = Shader.PropertyToID("_BaseMap_ST");
        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        /// <summary>Name of the mushroom prefab's stalk child, stretched down to the piece's stalk length.</summary>
        public const string MushroomStalk = "Stalk";

        public OB_CourseLayout Layout { get; private set; }
        /// <summary>Spawned objects, index-aligned with <see cref="OB_CourseLayout.pieces"/>.</summary>
        public IReadOnlyList<GameObject> Instances => instances;
        /// <summary>Parent of the built course's pieces (null before a build).</summary>
        public Transform Root => root;
        public Vector3 LobbySpawn => lobbySpawn != null ? lobbySpawn.position : transform.position;

        void Start() {
#if !UNITY_SERVER
            if (clientPrefab != null) Instantiate(clientPrefab, transform);
#endif
        }

        public bool IsConfigured => lobbySpawn != null && courseStart != null && platform != null && checkpointPad != null
            && finishPad != null && lava != null && spring != null && boost != null && gravityZone != null && conveyor != null
            && crumble != null && beat != null && wallJumpWall != null && mover != null && spinnerBar != null
            && spinnerCross != null && coin != null && beam != null && onOffBlock != null && onOffSwitchPad != null
            && onOffSwitchBlock != null && pendulum != null && crusher != null && laser != null && rollingLog != null
            && turntable != null && cannon != null && mushroom != null && gravityFlipPad != null && gravityFlipOrb != null
            && risingLava != null && star != null && ice != null && wind != null && ghost != null && honey != null && ring != null
            && swing != null && plank != null;

        /// <summary>The movement the course is designed around, with the sprint speed the server applies.</summary>
        public OB_Movement DesignMovement {
            get {
                var design = movement;
                design.sprintSpeed = sprintSpeed;
                return design;
            }
        }

        public OB_CourseLayout Generate(int seed, int stageCount) => OB_CourseGenerator.Generate(seed, stageCount, DesignMovement,
            courseStart.position, courseStart.eulerAngles.y, LobbySpawn, rules ?? OB_CourseRules.Default);

        public OB_CourseLayout Build(int seed, int stageCount) {
            if (!IsConfigured) throw new InvalidOperationException("OB_Course is missing piece prefabs or anchors.");
            Clear();
            Layout = Generate(seed, stageCount);
            root = new GameObject($"Course {seed}").transform;
            root.SetParent(transform, false);
            foreach (OB_Piece piece in Layout.pieces) instances.Add(Spawn(piece));
            Physics.SyncTransforms();
            return Layout;
        }

        public void Clear() {
            instances.Clear();
            if (root != null) {
                if (Application.isPlaying) Destroy(root.gameObject);
                else DestroyImmediate(root.gameObject);
            }
            root = null;
            Layout = null;
        }

        public void SetGateClosed(bool closed) {
            if (startGate != null && startGate.activeSelf != closed) startGate.SetActive(closed);
        }

        GameObject PrefabFor(OB_PieceKind kind) => kind switch {
            OB_PieceKind.Platform => platform,
            OB_PieceKind.CheckpointPad => checkpointPad,
            OB_PieceKind.Finish => finishPad,
            OB_PieceKind.Lava => lava,
            OB_PieceKind.Spring => spring,
            OB_PieceKind.Boost => boost,
            OB_PieceKind.GravityZone => gravityZone,
            OB_PieceKind.Conveyor => conveyor,
            OB_PieceKind.Crumble => crumble,
            OB_PieceKind.Beat => beat,
            OB_PieceKind.WallJumpWall => wallJumpWall,
            OB_PieceKind.Mover => mover,
            OB_PieceKind.SpinnerBar => spinnerBar,
            OB_PieceKind.SpinnerCross => spinnerCross,
            OB_PieceKind.Coin => coin,
            OB_PieceKind.Beam => beam,
            OB_PieceKind.OnOffBlock => onOffBlock,
            OB_PieceKind.OnOffSwitch => null, // two prefabs: see Spawn
            OB_PieceKind.Pendulum => pendulum,
            OB_PieceKind.Crusher => crusher,
            OB_PieceKind.Laser => laser,
            OB_PieceKind.RollingLog => rollingLog,
            OB_PieceKind.Turntable => turntable,
            OB_PieceKind.Cannon => cannon,
            OB_PieceKind.Mushroom => mushroom,
            OB_PieceKind.GravityFlip => null, // two prefabs: see Spawn
            OB_PieceKind.RisingLava => risingLava,
            OB_PieceKind.Star => star,
            OB_PieceKind.Ice => ice,
            OB_PieceKind.Wind => wind,
            OB_PieceKind.Ghost => ghost,
            OB_PieceKind.Honey => honey,
            OB_PieceKind.Ring => ring,
            OB_PieceKind.Swing => swing,
            OB_PieceKind.Plank => plank,
            _ => null
        };

        GameObject PrefabFor(in OB_Piece piece) => piece.kind switch {
            OB_PieceKind.OnOffSwitch => piece.group == 0 ? onOffSwitchPad : onOffSwitchBlock,
            OB_PieceKind.GravityFlip => piece.group == 0 ? gravityFlipPad : gravityFlipOrb,
            _ => PrefabFor(piece.kind)
        };

        static bool IsScaledBox(OB_PieceKind kind) => kind is OB_PieceKind.Platform or OB_PieceKind.Lava or OB_PieceKind.GravityZone
            or OB_PieceKind.Conveyor or OB_PieceKind.Crumble or OB_PieceKind.Beat or OB_PieceKind.WallJumpWall or OB_PieceKind.Mover
            or OB_PieceKind.Beam or OB_PieceKind.OnOffBlock or OB_PieceKind.Laser or OB_PieceKind.Crusher or OB_PieceKind.RisingLava
            or OB_PieceKind.Ice or OB_PieceKind.Ghost or OB_PieceKind.Honey or OB_PieceKind.Plank;

        /// <summary>Limits walking input in the flight after a launcher (the client's FlightControl does the work).</summary>
        static void AddFlightControl(GameObject launcher, float seconds, float share) {
            if (!launcher.TryGetComponent(out FlightControl control)) control = launcher.AddComponent<FlightControl>();
            control.Configure(seconds, share);
        }

        GameObject Spawn(OB_Piece piece) {
            float yaw = piece.yaw;
            Vector3 size = piece.size;
            if (piece.kind == OB_PieceKind.Conveyor && piece.vector.sqrMagnitude > 0f) {
                // Turn belts so their arrows (local +Z) point the way they push.
                float turn = Mathf.Atan2(piece.vector.x, piece.vector.z) * Mathf.Rad2Deg;
                yaw += turn;
                if (Mathf.Abs(piece.vector.x) > Mathf.Abs(piece.vector.z)) size = new Vector3(size.z, size.y, size.x);
            }
            GameObject prefab = PrefabFor(piece);
            // Out-of-bounds guards are data for the server's envelope: an empty marker keeps the instance list aligned with the pieces.
            if (piece.kind == OB_PieceKind.OutOfBounds) {
                var marker = new GameObject($"{piece.stage:00} {piece.kind}");
                marker.transform.SetPositionAndRotation(piece.position, Quaternion.Euler(0f, yaw, 0f));
                marker.transform.SetParent(root, true);
                return marker;
            }
            Quaternion rotation = piece.kind == OB_PieceKind.Swing ? Quaternion.Euler(piece.vector.x, yaw, 0f) : Quaternion.Euler(0f, yaw, 0f);
            var go = Instantiate(prefab, piece.position, rotation, root);
            go.name = $"{piece.stage:00} {piece.kind}";
            if (IsScaledBox(piece.kind)) go.transform.localScale = size;
            switch (piece.kind) {
                case OB_PieceKind.Platform: {
                    Material material = PlatformMaterial(piece.stage);
                    if (material != null)
                        foreach (var r in go.GetComponentsInChildren<Renderer>())
                            if (r is not ParticleSystemRenderer) r.sharedMaterial = material;
                    Tile(go, size);
                    break;
                }
                case OB_PieceKind.Lava:
                    Tile(go, piece.size);
                    Ambient(go, Vector3.up * 2.2f, piece.size.x * piece.size.z * 0.12f);
                    break;
                case OB_PieceKind.Crumble:
                    Tile(go, piece.size);
                    break;
                case OB_PieceKind.Spring:
                case OB_PieceKind.Boost:
                    go.GetComponentInChildren<LaunchPad>().Configure(piece.vector);
                    break;
                case OB_PieceKind.GravityZone: {
                    var zone = go.GetComponentInChildren<GravityZone>();
                    float volume = piece.size.x * piece.size.y * piece.size.z;
                    if (piece.vector == Vector3.zero) {
                        zone.Configure(piece.speed);
                        Ambient(go, Vector3.up * 1.2f, volume * 0.004f);
                    } else {
                        zone.Configure(piece.vector, piece.speed);
                        Tint(go, directionalFieldTint);
                        Ambient(go, piece.vector.normalized * 6f, volume * 0.006f, directionalFieldTint);
                    }
                    break;
                }
                case OB_PieceKind.Conveyor:
                    go.GetComponentInChildren<ConveyorSurface>().Configure(Vector3.forward, piece.speed);
                    break;
                case OB_PieceKind.Beat: {
                    var block = go.GetComponentInChildren<TimedToggleBlock>();
                    block.Configure(piece.period, 0.6f, piece.phase);
                    block.solidMaterial = piece.group == 0 ? beatSolidA : beatSolidB;
                    block.ghostMaterial = piece.group == 0 ? beatGhostA : beatGhostB;
                    Tile(go, piece.size);
                    break;
                }
                case OB_PieceKind.WallJumpWall:
                    go.GetComponentInChildren<WallJumpSurface>().Configure(piece.vector.y, piece.vector.x);
                    break;
                case OB_PieceKind.Mover:
                    go.GetComponent<NetworkPlatform>().Configure(piece.vector, piece.speed, Vector3.zero, 0f, true, false, piece.phase);
                    break;
                case OB_PieceKind.SpinnerBar:
                case OB_PieceKind.SpinnerCross:
                case OB_PieceKind.Turntable:
                    go.GetComponent<NetworkPlatform>().Configure(Vector3.zero, 0f, new Vector3(0f, 360f * Mathf.Sign(piece.speed), 0f),
                        Mathf.Abs(piece.speed), false, false, piece.phase);
                    if (piece.kind == OB_PieceKind.Turntable && go.TryGetComponent(out Turntable table))
                        table.Build(piece.size.x, piece.size.y, piece.group);
                    break;
                case OB_PieceKind.Beam:
                    Tile(go, piece.size);
                    break;
                case OB_PieceKind.OnOffBlock:
                    if (go.TryGetComponent(out OnOffBlock onOff)) {
                        bool second = piece.group == 1;
                        onOff.Configure(piece.channel, second, second ? onOffSolidB : onOffSolidA, second ? onOffGhostB : onOffGhostA);
                    }
                    break;
                case OB_PieceKind.OnOffSwitch:
                    if (go.TryGetComponent(out OnOffSwitch button)) button.Configure(piece.channel);
                    break;
                case OB_PieceKind.Pendulum:
                    if (go.TryGetComponent(out Pendulum hammer)) {
                        hammer.Configure(piece.vector.x, piece.period, piece.phase, piece.size.y);
                        if (hammer.head != null) hammer.head.localScale = new Vector3(piece.size.x, piece.size.x, piece.size.z);
                    }
                    break;
                case OB_PieceKind.Crusher:
                    if (go.TryGetComponent(out Crusher thwomp)) thwomp.Configure(piece.vector.y, piece.period, piece.phase, piece.speed);
                    break;
                case OB_PieceKind.Laser:
                    if (go.TryGetComponent(out TimedToggleBlock beamCycle)) {
                        // Period 0 is a beam that is always on.
                        if (piece.period > 0f) beamCycle.Configure(piece.period, piece.speed, piece.phase);
                        else beamCycle.Configure(1f, 1f, 0f);
                    }
                    break;
                case OB_PieceKind.RollingLog:
                    if (go.TryGetComponent(out RollingLog log)) {
                        float radius = piece.size.x * 0.5f;
                        log.Configure(piece.speed, radius);
                        if (go.TryGetComponent(out BoxCollider top))
                            top.size = new Vector3(radius * OB_CourseGenerator.LogStandFraction * 2f, piece.size.y, piece.size.z);
                        if (log.spinVisual != null) log.spinVisual.localScale = piece.size;
                    }
                    break;
                case OB_PieceKind.Cannon:
                    go.GetComponentInChildren<LaunchPad>().Configure(piece.vector);
                    AddFlightControl(go.GetComponentInChildren<LaunchPad>().gameObject, piece.speed + rules.mechanics.cannons.flightMargin, rules.mechanics.cannons.airControl);
                    break;
                case OB_PieceKind.Mushroom: {
                    go.GetComponentInChildren<LaunchPad>().Configure(piece.vector);
                    go.transform.localScale = new Vector3(piece.size.x, 1f, piece.size.x);
                    Transform stalk = go.transform.Find(MushroomStalk);
                    if (stalk != null) {
                        // The stalk hangs from the cap down to the stalk length, whatever the cap's width.
                        stalk.localScale = new Vector3(stalk.localScale.x, piece.size.y, stalk.localScale.z);
                        stalk.localPosition = Vector3.down * (piece.size.y * 0.5f);
                    }
                    break;
                }
                case OB_PieceKind.GravityFlip:
                    // A pad's local up is the gravity it gives, so its arrow plate lies on the surface the player stands on now.
                    if (piece.group == 0 && piece.vector != Vector3.zero)
                        go.transform.rotation = Quaternion.LookRotation(Quaternion.Euler(0f, yaw, 0f) * Vector3.forward, piece.vector);
                    if (go.TryGetComponent(out BoxCollider trigger)) trigger.size = piece.size;
                    go.GetComponentInChildren<GravityFlipPad>().Configure(piece.vector, rules.mechanics.gravity.flipHoldSeconds);
                    break;
                case OB_PieceKind.Ice:
                    if (go.TryGetComponent(out IceSurface iceSurface)) iceSurface.Configure(piece.speed);
                    Tile(go, piece.size);
                    break;
                case OB_PieceKind.Wind:
                    if (go.TryGetComponent(out WindVolume gusts)) {
                        float calm = piece.vector.magnitude;
                        Vector3 local = Quaternion.Inverse(go.transform.rotation) * (calm > 0f ? piece.vector / calm : Vector3.right);
                        gusts.Resize(piece.size);
                        gusts.Configure(local, calm, calm > 0f ? piece.speed / calm : 0f, piece.period, piece.phase);
                    }
                    break;
                case OB_PieceKind.Ghost:
                    if (go.TryGetComponent(out TimedToggleBlock ghostCycle)) ghostCycle.Configure(piece.period, piece.speed, piece.phase);
                    Tile(go, piece.size);
                    break;
                case OB_PieceKind.Honey:
                    if (go.TryGetComponent(out StickyZone honeyZone)) honeyZone.Configure(piece.speed);
                    Tile(go, piece.size);
                    break;
                case OB_PieceKind.Ring:
                    if (go.TryGetComponent(out BoostRing hoop)) hoop.Configure(Quaternion.Inverse(go.transform.rotation) * piece.vector, piece.size, rules.mechanics.rings.catchAbove);
                    AddFlightControl(go, piece.speed + rules.mechanics.rings.flightMargin, rules.mechanics.rings.airControl);
                    break;
                case OB_PieceKind.Swing:
                    if (go.TryGetComponent(out SwingPlatform swing)) {
                        swing.Configure(piece.size, piece.vector.x, piece.vector.y);
                        if (go.TryGetComponent(out NetworkPlatform motion))
                            motion.Configure(Vector3.zero, 0f, SwingPlatform.MotionDelta(piece.vector.x), SwingPlatform.MotionSpeed(piece.vector.x, piece.period), false, true, piece.phase);
                    }
                    break;
                case OB_PieceKind.Plank:
                    Tile(go, piece.size);
                    break;
                case OB_PieceKind.RisingLava:
                    if (go.TryGetComponent(out RisingLava rising))
                        rising.Configure(piece.speed, piece.period, piece.vector.y, new Vector2(piece.size.x, piece.size.z));
                    Tile(go, piece.size);
                    break;
            }
            if (piece.kind is OB_PieceKind.Crumble or OB_PieceKind.Plank)
                go.GetComponentInChildren<CrumbleBlock>().Configure(piece.speed, piece.period);
            return go;
        }

        /// <summary>The themed material for a platform on <paramref name="stage"/>, or null to keep the prefab's.</summary>
        Material PlatformMaterial(int stage) {
            if (tierPlatforms == null || tierPlatforms.Length == 0 || Layout == null) return null;
            int clamped = Mathf.Clamp(stage, 0, Layout.StageCount - 1);
            OB_Tier tier = Layout.stages[clamped].tier;
            var set = tierPlatforms[Mathf.Min((int)tier, tierPlatforms.Length - 1)].materials;
            if (set == null || set.Length == 0) return null;
            // Alternate styles stage by stage inside a tier.
            return set[(clamped - OB_Tiers.FirstStage((int)tier, Layout.StageCount)) % set.Length];
        }

        /// <summary>
        /// Sets up a piece's ambient particles (lava embers, gravity-field drift): they drift with
        /// <paramref name="velocity"/> (world space) at a rate that suits the piece's size.
        /// </summary>
        void Ambient(GameObject go, Vector3 velocity, float rate, Color? color = null) {
#if !UNITY_SERVER
            var particles = go.GetComponentInChildren<ParticleSystem>();
            if (particles == null) return;
            var motion = particles.velocityOverLifetime;
            motion.enabled = true;
            motion.space = ParticleSystemSimulationSpace.World;
            motion.x = velocity.x;
            motion.y = velocity.y;
            motion.z = velocity.z;
            var emission = particles.emission;
            emission.rateOverTime = Mathf.Clamp(rate, 3f, 60f);
            if (color.HasValue) {
                var main = particles.main;
                main.startColor = new Color(color.Value.r, color.Value.g, color.Value.b, 0.9f);
            }
            particles.Play();
#endif
        }

        void Tint(GameObject go, Color color) {
#if !UNITY_SERVER
            tiling ??= new MaterialPropertyBlock();
            foreach (var r in go.GetComponentsInChildren<Renderer>()) {
                if (r is ParticleSystemRenderer) continue;
                r.GetPropertyBlock(tiling);
                tiling.SetColor(BaseColor, color);
                r.SetPropertyBlock(tiling);
            }
#endif
        }

        /// <summary>Keeps the tile texture at a constant world size on scaled boxes.</summary>
        void Tile(GameObject go, Vector3 size) {
#if !UNITY_SERVER
            tiling ??= new MaterialPropertyBlock();
            foreach (var r in go.GetComponentsInChildren<Renderer>()) {
                if (r is ParticleSystemRenderer) continue;
                r.GetPropertyBlock(tiling);
                tiling.SetVector(BaseMapST, new Vector4(size.x / tileMetres, size.z / tileMetres, 0f, 0f));
                r.SetPropertyBlock(tiling);
            }
#endif
        }
    }
}
