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
        [Min(0f)] public float sprintSpeed = 15f;
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

        [Header("Presentation")]
        [Tooltip("Platform colours, cycled per stage.")]
        public Material[] platformPalette;
        [Tooltip("Beat block materials per group: solid then ghost.")]
        public Material beatSolidA, beatGhostA, beatSolidB, beatGhostB;
        [Min(0.1f)] public float tileMetres = 2f;
        [Tooltip("Client-only HUD and course presentation.")]
        public GameObject clientPrefab;

        readonly List<GameObject> instances = new();
        Transform root;
        MaterialPropertyBlock tiling;
        static readonly int BaseMapST = Shader.PropertyToID("_BaseMap_ST");

        public OB_CourseLayout Layout { get; private set; }
        /// <summary>Spawned objects, index-aligned with <see cref="OB_CourseLayout.pieces"/>.</summary>
        public IReadOnlyList<GameObject> Instances => instances;
        public Vector3 LobbySpawn => lobbySpawn != null ? lobbySpawn.position : transform.position;

        void Start() {
#if !UNITY_SERVER
            if (clientPrefab != null) Instantiate(clientPrefab, transform);
#endif
        }

        public bool IsConfigured => lobbySpawn != null && courseStart != null && platform != null && checkpointPad != null
            && finishPad != null && lava != null && spring != null && boost != null && gravityZone != null && conveyor != null
            && crumble != null && beat != null && wallJumpWall != null && mover != null && spinnerBar != null
            && spinnerCross != null && coin != null;

        public OB_CourseLayout Generate(int seed, int stageCount) => OB_CourseGenerator.Generate(seed, stageCount, movement,
            courseStart.position, courseStart.eulerAngles.y, LobbySpawn);

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
            _ => null
        };

        static bool IsScaledBox(OB_PieceKind kind) => kind is OB_PieceKind.Platform or OB_PieceKind.Lava or OB_PieceKind.GravityZone
            or OB_PieceKind.Conveyor or OB_PieceKind.Crumble or OB_PieceKind.Beat or OB_PieceKind.WallJumpWall or OB_PieceKind.Mover;

        GameObject Spawn(OB_Piece piece) {
            float yaw = piece.yaw;
            Vector3 size = piece.size;
            if (piece.kind == OB_PieceKind.Conveyor && piece.vector.sqrMagnitude > 0f) {
                // Turn belts so their arrows (local +Z) point the way they push.
                float turn = Mathf.Atan2(piece.vector.x, piece.vector.z) * Mathf.Rad2Deg;
                yaw += turn;
                if (Mathf.Abs(piece.vector.x) > Mathf.Abs(piece.vector.z)) size = new Vector3(size.z, size.y, size.x);
            }
            var go = Instantiate(PrefabFor(piece.kind), piece.position, Quaternion.Euler(0f, yaw, 0f), root);
            go.name = $"{piece.stage:00} {piece.kind}";
            if (IsScaledBox(piece.kind)) go.transform.localScale = size;
            switch (piece.kind) {
                case OB_PieceKind.Platform:
                    if (platformPalette != null && platformPalette.Length > 0)
                        foreach (var r in go.GetComponentsInChildren<Renderer>())
                            r.sharedMaterial = platformPalette[Mathf.Abs(piece.stage) % platformPalette.Length];
                    Tile(go, size);
                    break;
                case OB_PieceKind.Lava:
                case OB_PieceKind.Crumble:
                    Tile(go, piece.size);
                    break;
                case OB_PieceKind.Spring:
                case OB_PieceKind.Boost:
                    go.GetComponentInChildren<LaunchPad>().Configure(piece.vector);
                    break;
                case OB_PieceKind.GravityZone:
                    go.GetComponentInChildren<GravityZone>().Configure(piece.speed);
                    break;
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
                    go.GetComponent<NetworkPlatform>().Configure(Vector3.zero, 0f, new Vector3(0f, 360f * Mathf.Sign(piece.speed), 0f),
                        Mathf.Abs(piece.speed), false, false, piece.phase);
                    break;
            }
            if (piece.kind == OB_PieceKind.Crumble)
                go.GetComponentInChildren<CrumbleBlock>().Configure(piece.speed, piece.period);
            return go;
        }

        /// <summary>Keeps the tile texture at a constant world size on scaled boxes.</summary>
        void Tile(GameObject go, Vector3 size) {
#if !UNITY_SERVER
            tiling ??= new MaterialPropertyBlock();
            foreach (var r in go.GetComponentsInChildren<Renderer>()) {
                r.GetPropertyBlock(tiling);
                tiling.SetVector(BaseMapST, new Vector4(size.x / tileMetres, size.z / tileMetres, 0f, 0f));
                r.SetPropertyBlock(tiling);
            }
#endif
        }
    }
}
