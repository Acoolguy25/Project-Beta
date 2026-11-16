using System;
using RyanAssets.GameFeel;
using UnityEngine;

namespace Universes.UniverseData.capture_the_flag.Client {
    /// <summary>
    /// Makes both flags findable from across the map: a team-coloured light beam on a flag that sits
    /// at home or on the ground, a glowing trail behind whoever carries it. Owned and driven by
    /// <see cref="CTF_ClientController"/>; the handles come from the shared game-feel kit.
    /// </summary>
    [Serializable]
    public sealed class CTF_FlagBeacons {
        [Tooltip("Size of the light pillar on a flag that is home or dropped.")]
        [Min(0.1f)] public float beamScale = 2.5f;
        [Tooltip("Size of the trail behind a carrier.")]
        [Min(0.1f)] public float trailScale = 1.6f;
        [Tooltip("Where the trail starts, in the carrier's local space (the robot root is scaled).")]
        public Vector3 trailOffset = new(0f, 0.8f, -0.15f);

        readonly FxHandle[] beams = new FxHandle[2];
        readonly FxHandle[] trails = new FxHandle[2];
        readonly Transform[] trailTargets = new Transform[2];

        /// <summary>Call every frame per flag (0 red, 1 blue) with where it is shown right now.</summary>
        public void Refresh(int index, CTF_FlagState state, Vector3 position, Transform carrier, Color color) {
            if (state == CTF_FlagState.Carried && carrier != null) {
                beams[index].Stop();
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
            if (beams[index].IsPlaying) beams[index].SetPosition(position);
            else beams[index] = GameFx.Start(FxKind.BeaconBeam, position, color, beamScale);
        }

        /// <summary>Stops every effect (the kit's pool outlives this game's scene).</summary>
        public void StopAll() {
            for (int i = 0; i < beams.Length; i++) {
                beams[i].Stop();
                trails[i].Stop();
                trailTargets[i] = null;
            }
        }
    }
}
