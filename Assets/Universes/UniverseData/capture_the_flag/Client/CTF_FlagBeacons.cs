using System;
using RyanAssets.GameFeel;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Universes.UniverseData.capture_the_flag.Client {
    /// <summary>
    /// Makes both flags findable from across the map: a team-coloured light beam on a flag that sits
    /// at home or on the ground, a glowing trail behind whoever carries it. Owned and driven by
    /// <see cref="CTF_ClientController"/>. The beam is this game's own prefab (the kit's beam with its
    /// motes rising inside the column); the trail comes from the shared game-feel kit.
    /// </summary>
    [Serializable]
    public sealed class CTF_FlagBeacons {
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        [Tooltip("The light pillar on a flag that is home or dropped (built by CTF_HudAuthoring).")]
        public ParticleSystem beamPrefab;
        [Tooltip("Size of the light pillar.")]
        [Min(0.1f)] public float beamScale = 2.5f;
        [Tooltip("Size of the trail behind a carrier.")]
        [Min(0.1f)] public float trailScale = 1.6f;
        [Tooltip("Where the trail starts, in the carrier's local space (the robot root is scaled).")]
        public Vector3 trailOffset = new(0f, 0.8f, -0.15f);
        [Header("Flag Trail (unlock at level 24)")]
        [Tooltip("The unlocked trail's size: bigger and hotter than everyone's.")]
        [Min(0.1f)] public float blazeScale = 2.8f;
        [Tooltip("How far the unlocked trail's colour is pulled from the team colour toward the blaze colour.")]
        [Range(0f, 1f)] public float blazeBlend = 0.45f;
        public Color blazeColor = new(1f, 0.62f, 0.15f);
        [Tooltip("Seconds between the sparks thrown off an unlocked trail.")]
        [Min(0.03f)] public float blazeSparkInterval = 0.14f;
        [Min(0.05f)] public float blazeSparkScale = 0.7f;

        readonly Beam[] beams = new Beam[2];
        readonly FxHandle[] trails = new FxHandle[2];
        readonly Transform[] trailTargets = new Transform[2];
        readonly bool[] trailBlazing = new bool[2];
        readonly float[] nextSpark = new float[2];
        MaterialPropertyBlock block;
        Transform parent;

        sealed class Beam {
            public ParticleSystem system;
            public ParticleSystemRenderer[] renderers;
            public Color tint = new(-1f, -1f, -1f, -1f);
        }

        /// <summary>Where the beams live; call once before the first <see cref="Refresh"/>.</summary>
        public void Init(Transform owner) => parent = owner;

        /// <summary>
        /// Call every frame per flag (0 red, 1 blue) with where it is shown right now. <paramref name="blazing"/>:
        /// the carrier unlocked Flag Trail, so the trail is bigger, hotter and throws sparks.
        /// </summary>
        public void Refresh(int index, CTF_FlagState state, Vector3 position, Transform carrier, Color color, bool blazing = false) {
            if (state == CTF_FlagState.Carried && carrier != null) {
                StopBeam(index);
                if (blazing && Time.time >= nextSpark[index]) {
                    nextSpark[index] = Time.time + blazeSparkInterval;
                    GameFx.Play(FxKind.Sparks, carrier.TransformPoint(trailOffset), -carrier.forward, Blaze(color), blazeSparkScale);
                }
                if (trailTargets[index] == carrier && trailBlazing[index] == blazing && trails[index].IsPlaying) return;
                trails[index].Stop();
                trails[index] = blazing
                    ? GameFx.Attach(FxKind.Trail, carrier, trailOffset, Blaze(color), blazeScale)
                    : GameFx.Attach(FxKind.Trail, carrier, trailOffset, color, trailScale);
                trailTargets[index] = carrier;
                trailBlazing[index] = blazing;
                return;
            }
            if (trailTargets[index] != null) {
                trails[index].Stop();
                trailTargets[index] = null;
            }
            var beam = beams[index] ??= CreateBeam();
            Tint(beam, color);
            beam.system.transform.position = position;
            if (!beam.system.isEmitting) beam.system.Play(true);
        }

        /// <summary>Stops flag <paramref name="index"/>'s beam and trail.</summary>
        public void Stop(int index) {
            StopBeam(index);
            trails[index].Stop();
            trailTargets[index] = null;
        }

        /// <summary>Stops every effect and removes the beams (the kit's trail pool outlives this game's scene).</summary>
        public void StopAll() {
            for (int i = 0; i < beams.Length; i++) {
                if (beams[i] != null && beams[i].system != null) Object.Destroy(beams[i].system.gameObject);
                beams[i] = null;
                trails[i].Stop();
                trailTargets[i] = null;
            }
        }

        Color Blaze(Color team) => Color.Lerp(team, blazeColor, blazeBlend);

        void StopBeam(int index) {
            var beam = beams[index];
            if (beam != null && beam.system.isEmitting) beam.system.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }

        Beam CreateBeam() {
            var system = Object.Instantiate(beamPrefab, parent);
            system.transform.localScale = Vector3.one * beamScale;
            return new Beam { system = system, renderers = system.GetComponentsInChildren<ParticleSystemRenderer>(true) };
        }

        /// <summary>Same tinting as the kit's pooled effects: the base colour through a property block.</summary>
        void Tint(Beam beam, Color color) {
            if (beam.tint == color) return;
            beam.tint = color;
            block ??= new MaterialPropertyBlock();
            block.Clear();
            block.SetColor(BaseColorId, color);
            foreach (var renderer in beam.renderers) renderer.SetPropertyBlock(block);
        }
    }
}
