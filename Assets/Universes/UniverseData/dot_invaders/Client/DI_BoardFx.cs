using UnityEngine;
#if !UNITY_SERVER
using System;
using System.Collections.Generic;
using RyanAssets.GameFeel;
#endif

namespace Universes.UniverseData.dot_invaders.Client {
    /// <summary>
    /// Sounds and effects for what happens on the board: the order blip (lower for bigger waves),
    /// capture ring bursts, lost-base and clash sounds, clash sparks, a punch on the base a troop
    /// lands on, elimination shockwaves and the win/lose sting with confetti. Client-side reactions
    /// to consecutive server snapshots (<see cref="DI_SnapshotEvents"/>); nothing here changes the
    /// game. Sounds are game-specific clips wired by DI_Authoring, plus the kit's Win/Lose cues.
    /// </summary>
    public sealed class DI_BoardFx : MonoBehaviour {
#if !UNITY_SERVER
        [Header("Sounds")]
        [Tooltip("Played when you order troops out; pitched by the size of the wave.")]
        [SerializeField] SfxSound sendSound = new();
        [Tooltip("Your team took a base.")]
        [SerializeField] SfxSound captureSound = new();
        [Tooltip("Your team lost a base.")]
        [SerializeField] SfxSound baseLostSound = new();
        [Tooltip("Troops destroyed each other on a road.")]
        [SerializeField] SfxSound clashSound = new();
        [Tooltip("A team was wiped off the board.")]
        [SerializeField] SfxSound eliminationSound = new();

        [Header("Order blip")]
        [Tooltip("Pitch for a one-troop order.")]
        [SerializeField, Range(0.25f, 3f)] float smallWavePitch = 1.4f;
        [Tooltip("Pitch for an order of Large Wave troops or more.")]
        [SerializeField, Range(0.25f, 3f)] float largeWavePitch = 0.7f;
        [SerializeField, Min(2)] int largeWave = 120;

        [Header("Volumes")]
        [Tooltip("Captures between other teams, so the board sounds alive without drowning your own.")]
        [SerializeField, Range(0f, 1f)] float otherCaptureVolume = 0.25f;
        [SerializeField, Range(0f, 1f)] float clashVolume = 0.3f;
        [SerializeField, Range(0f, 1f)] float otherEliminationVolume = 0.6f;

        // Effect scales are in board units and sized for the board camera, which frames about 180 units top to bottom.
        [Header("Captures")]
        [SerializeField, Min(0f)] float captureRingScale = 10f;
        [SerializeField, Min(0f)] float capturePunch = 0.25f;
        [SerializeField, Min(0.01f)] float capturePunchSeconds = 0.35f;
        [Tooltip("Camera kick when your base falls.")]
        [SerializeField, Range(0f, 1f)] float baseLostShake = 0.15f;

        [Header("Clashes and arrivals")]
        [SerializeField, Min(0f)] float clashSparkScale = 6f;
        [Tooltip("A small flat ring under the sparks: sparks fly toward the top-down camera and read as a dot on their own.")]
        [SerializeField, Min(0f)] float clashRingScale = 3f;
        [SerializeField] Color clashSparkColor = new(1f, 0.92f, 0.7f);
        [SerializeField, Min(0)] int maxClashSparksPerSnapshot = 12;
        [Tooltip("A troop that vanishes within this many board units of a base landed on it.")]
        [SerializeField, Min(0f)] float arrivalRadius = 5f;
        [SerializeField, Min(0f)] float arrivalPunch = 0.22f;
        [SerializeField, Min(0.01f)] float arrivalPunchSeconds = 0.22f;

        [Header("Eliminations")]
        [SerializeField, Min(0f)] float eliminationRingScale = 24f;
        [SerializeField, Min(0f)] float eliminationShakeRadius = 120f;
        [SerializeField, Range(0f, 1f)] float eliminationShake = 0.35f;
        [SerializeField] Color localEliminationFlash = new(1f, 0.2f, 0.25f, 0.35f);
        [SerializeField] string localEliminationText = "ELIMINATED";

        [Header("Match end")]
        [SerializeField, Min(0f)] float confettiScale = 8f;
        [SerializeField, Min(0)] int maxConfettiBursts = 8;
        [SerializeField] Color victoryFlash = new(1f, 1f, 1f, 0.25f);

        [Header("Placement")]
        [Tooltip("Height above the board effects play at, so they sit over roads and bases.")]
        [SerializeField, Min(0f)] float effectHeight = 0.6f;

        readonly List<DI_Capture> captures = new();
        readonly List<int> eliminated = new();
        readonly List<int> vanished = new();
        readonly HashSet<int> scratch = new();
        readonly HashSet<int> punchedBases = new();

        /// <summary>The order blip for a wave of <paramref name="troops"/>: bigger waves sound lower.</summary>
        public void PlayOrder(int troops) {
            if (troops <= 0)
                return;
            float size = Mathf.Clamp01(Mathf.Log(troops) / Mathf.Log(largeWave));
            GameSfx.Play(sendSound, 1f, Mathf.Lerp(smallWavePitch, largeWavePitch, size));
        }

        /// <summary>Reacts to everything that changed from <paramref name="previous"/> to <paramref name="next"/>.</summary>
        public void Present(in DI_StateBroadcast previous, in DI_StateBroadcast next,
            IReadOnlyList<DI_BaseView> views, Func<int, Color> teamColor) {
            if (DI_SnapshotEvents.SameBoard(previous, next)) {
                PresentClashes(next);
                PresentArrivals(previous, next, views);
                PresentCaptures(previous, next, views, teamColor);
                PresentEliminations(previous, next, teamColor);
            }
            if (previous.basePositions != null && !previous.matchEnded && next.matchEnded)
                PresentMatchEnd(next);
        }

        void PresentClashes(in DI_StateBroadcast next) {
            if (next.clashPositions == null || next.clashPositions.Length == 0)
                return;
            int sparks = Mathf.Min(maxClashSparksPerSnapshot, next.clashPositions.Length);
            for (int i = 0; i < sparks; i++) {
                Vector3 position = World(next.clashPositions[i]);
                GameFx.Play(FxKind.Sparks, position, Vector3.up, clashSparkColor, clashSparkScale);
                GameFx.Play(FxKind.ShockwaveRing, position, clashSparkColor, clashRingScale);
            }
            // The sound's own minimum interval keeps a brawl from machine-gunning.
            GameSfx.Play(clashSound, clashVolume);
        }

        void PresentArrivals(in DI_StateBroadcast previous, in DI_StateBroadcast next, IReadOnlyList<DI_BaseView> views) {
            DI_SnapshotEvents.FindVanishedDots(previous, next, scratch, vanished);
            punchedBases.Clear();
            foreach (int index in vanished) {
                if (previous.dotPositions == null || index >= previous.dotPositions.Length)
                    continue;
                int baseId = DI_SnapshotEvents.NearestBase(next, previous.dotPositions[index], arrivalRadius);
                if (baseId >= 0 && baseId < views.Count && punchedBases.Add(baseId))
                    views[baseId].PlayPunch(arrivalPunch, arrivalPunchSeconds);
            }
        }

        void PresentCaptures(in DI_StateBroadcast previous, in DI_StateBroadcast next,
            IReadOnlyList<DI_BaseView> views, Func<int, Color> teamColor) {
            DI_SnapshotEvents.FindCaptures(previous, next, captures);
            int you = next.yourTeamId;
            foreach (DI_Capture capture in captures) {
                Vector3 position = World(next.basePositions[capture.baseId]);
                GameFx.Play(FxKind.ShockwaveRing, position, teamColor(capture.teamId), captureRingScale);
                if (capture.baseId < views.Count)
                    views[capture.baseId].PlayPunch(capturePunch, capturePunchSeconds);
                if (you >= 0 && capture.teamId == you) {
                    GameSfx.Play(captureSound);
                } else if (you >= 0 && capture.previousTeamId == you) {
                    GameSfx.Play(baseLostSound);
                    CameraShake.Kick(baseLostShake, 0.2f);
                } else {
                    GameSfx.Play(captureSound, otherCaptureVolume);
                }
            }
        }

        void PresentEliminations(in DI_StateBroadcast previous, in DI_StateBroadcast next, Func<int, Color> teamColor) {
            DI_SnapshotEvents.FindEliminatedTeams(previous, next, scratch, eliminated);
            foreach (int team in eliminated) {
                Color color = teamColor(team);
                // The shockwave goes off where the team made its last stand.
                if (TryLastPosition(previous, team, out Vector3 position)) {
                    GameFx.Play(FxKind.ShockwaveRing, position, color, eliminationRingScale);
                    GameFx.Play(FxKind.ExplosionSmall, position, color, captureRingScale);
                    CameraShake.Explosion(position, eliminationShakeRadius, eliminationShake);
                }
                if (team == next.yourTeamId) {
                    GameSfx.Play(eliminationSound);
                    ScreenFx.Flash(localEliminationFlash, 0.5f);
                    ScreenFx.ShowText(localEliminationText, color);
                } else {
                    GameSfx.Play(eliminationSound, otherEliminationVolume);
                }
            }
        }

        void PresentMatchEnd(in DI_StateBroadcast next) {
            int you = next.yourTeamId;
            if (you < 0)
                return;
            if (next.winningTeamId != you) {
                GameSfx.Play(SfxCue.Lose);
                return;
            }
            GameSfx.Play(SfxCue.Win);
            ScreenFx.Flash(victoryFlash, 0.4f);
            int bursts = 0;
            for (int i = 0; i < next.baseTeams.Length && bursts < maxConfettiBursts; i++) {
                if (next.baseTeams[i] != you)
                    continue;
                GameFx.Play(FxKind.Confetti, World(next.basePositions[i]), confettiScale);
                bursts++;
            }
        }

        bool TryLastPosition(in DI_StateBroadcast state, int team, out Vector3 position) {
            if (state.baseTeams != null)
                for (int i = 0; i < state.baseTeams.Length; i++)
                    if (state.baseTeams[i] == team) {
                        position = World(state.basePositions[i]);
                        return true;
                    }
            if (state.dotTeams != null && state.dotPositions != null)
                for (int i = 0; i < Mathf.Min(state.dotTeams.Length, state.dotPositions.Length); i++)
                    if (state.dotTeams[i] == team) {
                        position = World(state.dotPositions[i]);
                        return true;
                    }
            position = default;
            return false;
        }

        Vector3 World(Vector2 point) => new(point.x, effectHeight, point.y);
#endif
    }
}
