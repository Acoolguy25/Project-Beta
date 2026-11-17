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

        readonly Beam[] beams = new Beam[2];
        readonly FxHandle[] trails = new FxHandle[2];
        readonly Transform[] trailTargets = new Transform[2];
        MaterialPropertyBlock block;
        Transform parent;

        sealed class Beam {
            public ParticleSystem system;
            public ParticleSystemRenderer[] renderers;
            public Color tint = new(-1f, -1f, -1f, -1f);
        }

        /// <summary>Where the beams live; call once before the first <see cref="Refresh"/>.</summary>
        public void Init(Transform owner) => parent = owner;

        /// <summary>Call every frame per flag (0 red, 1 blue) with where it is shown right now.</summary>
        public void Refresh(int index, CTF_FlagState state, Vector3 position, Transform carrier, Color color) {
            if (state == CTF_FlagState.Carried && carrier != null) {
                StopBeam(index);
                if (trailTargets[index] == carrier && trails[index].IsPlaying) return;
                trails[index].Stop();
                trails[index] = GameFx.Attach(FxKind.Trail, carrier, trailOffset, color, trailScale);
                trailTargets[index] = carrier;
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
