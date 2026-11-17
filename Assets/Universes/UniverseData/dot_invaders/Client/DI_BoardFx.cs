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
    /// lands on, elimination shockwaves, confetti on a win and a dimmed DEFEAT on a loss. Client-side reactions
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
        [Tooltip("A clash your own troops took part in.")]
        [SerializeField, Range(0f, 1f)] float clashVolume = 0.45f;
        [Tooltip("A clash between other teams: a brawl across the board stays in the background.")]
        [SerializeField, Range(0f, 1f)] float otherClashVolume = 0.12f;
        [Tooltip("Board units from a clash within which one of your troops counts as taking part.")]
        [SerializeField, Min(0f)] float clashInvolvementRadius = 3f;
        [SerializeField, Range(0f, 1f)] float otherEliminationVolume = 0.6f;

        // Effect scales are in board units and sized for the board camera, which frames about 180 units top to bottom.
        [Header("Captures")]
        [SerializeField, Min(0f)] float captureRingScale = 5f;
        [Tooltip("How far the capture ring is lightened toward white, so a capture reads as a crisp flash, not a dull band.")]
        [SerializeField, Range(0f, 1f)] float captureRingWhiten = 0.55f;
        [SerializeField, Min(0f)] float capturePunch = 0.25f;
        [SerializeField, Min(0.01f)] float capturePunchSeconds = 0.35f;
        [Tooltip("Camera kick when your base falls.")]
        [SerializeField, Range(0f, 1f)] float baseLostShake = 0.15f;
        [SerializeField, Min(0.01f)] float baseLostShakeSeconds = 0.2f;

        [Header("Clashes")]
        [SerializeField, Min(0f)] float clashSparkScale = 6f;
        [Tooltip("A small flat ring under the sparks: sparks fly toward the top-down camera and read as a dot on their own.")]
        [SerializeField, Min(0f)] float clashRingScale = 3f;
        [SerializeField] Color clashSparkColor = new(1f, 0.92f, 0.7f);
        [SerializeField, Min(0)] int maxClashSparksPerSnapshot = 12;
        [Tooltip("Seconds during which a new clash ring is skipped if one already went off within Clash Ring Scale of it, so a brawl on one road does not stack rings.")]
        [SerializeField, Min(0f)] float clashRingCooldown = 0.3f;

        [Header("Arrivals")]
        [Tooltip("A troop that vanishes within this many board units of a base landed on it.")]
        [SerializeField, Min(0f)] float arrivalRadius = 5f;
        [Tooltip("A vanished troop this close to a clash or a turret hit died there; it did not land.")]
        [SerializeField, Min(0f)] float combatLossRadius = 2.5f;
        [Tooltip("Extra scale a base swells by when troops land on it.")]
        [SerializeField, Min(0f)] float arrivalPunch = 0.45f;
        [SerializeField, Min(0.01f)] float arrivalPunchSeconds = 0.25f;
        [Tooltip("Seconds before the same base punches again, so a steady stream reads as beats, not jitter.")]
        [SerializeField, Min(0f)] float arrivalPunchCooldown = 0.3f;

        [Header("Eliminations")]
        [SerializeField, Min(0f)] float eliminationRingScale = 13f;
        [Tooltip("Brightness of the outer ring: below 1 so a saturated team colour stays a ring instead of blooming into a haze.")]
        [SerializeField, Range(0f, 1f)] float eliminationRingGlow = 0.5f;
        [Tooltip("A thinner second ring inside the first, in the eliminated team's colour.")]
        [SerializeField, Min(0f)] float eliminationInnerRingScale = 7f;
        [Tooltip("How far the inner ring is lightened toward white, so the two rings read apart.")]
        [SerializeField, Range(0f, 1f)] float eliminationInnerWhiten = 0.5f;
        [SerializeField, Min(0f)] float eliminationSparkScale = 6f;
        [SerializeField, Min(0f)] float eliminationShakeRadius = 120f;
        [SerializeField, Range(0f, 1f)] float eliminationShake = 0.35f;
        [SerializeField] Color localEliminationFlash = new(1f, 0.2f, 0.25f, 0.35f);
        [SerializeField, Min(0.01f)] float localEliminationFlashSeconds = 0.5f;
        [SerializeField] string localEliminationText = "ELIMINATED";
        [Tooltip("Caption size as a share of the countdown font: small enough to leave the board readable.")]
        [SerializeField, Range(0.1f, 1f)] float localEliminationTextSize = 0.33f;
        [Tooltip("How far the caption is lightened from the team colour toward white, so it reads over the red flash.")]
        [SerializeField, Range(0f, 1f)] float localEliminationTextWhiten = 0.65f;
        [Tooltip("How long the ELIMINATED caption stays on screen.")]
        [SerializeField, Min(0.1f)] float localEliminationTextSeconds = 2.2f;

        [Header("Match end")]
        [SerializeField, Min(0f)] float confettiScale = 8f;
        [SerializeField, Min(0)] int maxConfettiBursts = 8;
        [Tooltip("Viewport area the match-over panel covers; confetti bursts go off outside it.")]
        [SerializeField] Rect confettiKeepOut = new(0.3f, 0.3f, 0.4f, 0.45f);
        [Tooltip("Viewport area bursts may start in: clear of the HUD strip at the top and the chat on the left, since bursts rise.")]
        [SerializeField] Rect confettiArea = new(0.25f, 0.05f, 0.55f, 0.62f);
        [SerializeField] Color victoryFlash = new(1f, 1f, 1f, 0.25f);
        [SerializeField, Min(0.01f)] float victoryFlashSeconds = 0.4f;
        [SerializeField] Color defeatFlash = new(0.05f, 0.02f, 0.08f, 0.45f);
        [Tooltip("A loss gets a dark flash and the lose cue only; the match-over panel is the caption.")]
        [SerializeField, Min(0.01f)] float defeatFlashSeconds = 0.8f;

        [Header("Placement")]
        [Tooltip("Height above the board effects play at, so they sit over roads and bases.")]
        [SerializeField, Min(0f)] float effectHeight = 0.6f;

        readonly List<DI_Capture> captures = new();
        readonly List<int> eliminated = new();
        readonly List<int> vanished = new();
        readonly HashSet<int> scratch = new();
        float[] nextPunchTime = Array.Empty<float>();
        // Recent clash rings (position, unscaled time) in a small ring buffer; see clashRingCooldown.
        const int RecentClashRings = 16;
        readonly Vector2[] recentClashPositions = new Vector2[RecentClashRings];
        readonly float[] recentClashTimes = new float[RecentClashRings];
        int nextClashSlot;

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
            bool involved = false;
            for (int i = 0; i < sparks; i++) {
                Vector2 point = next.clashPositions[i];
                Vector3 position = World(point);
                GameFx.Play(FxKind.Sparks, position, Vector3.up, clashSparkColor, clashSparkScale);
                if (TryClaimClashRing(point))
                    GameFx.Play(FxKind.ShockwaveRing, position, clashSparkColor, clashRingScale);
                involved |= HasTeamDotNear(next, next.yourTeamId, point, clashInvolvementRadius);
            }
            // The sound's own minimum interval (DI_Authoring) keeps a brawl from machine-gunning.
            GameSfx.Play(clashSound, involved ? clashVolume : otherClashVolume);
        }

        /// <summary>False when a ring already went off near <paramref name="point"/> within the cooldown.</summary>
        bool TryClaimClashRing(Vector2 point) {
            float now = Time.unscaledTime, squared = clashRingScale * clashRingScale;
            for (int i = 0; i < RecentClashRings; i++)
                if (now - recentClashTimes[i] < clashRingCooldown && (recentClashPositions[i] - point).sqrMagnitude <= squared)
                    return false;
            recentClashPositions[nextClashSlot] = point;
            recentClashTimes[nextClashSlot] = now;
            nextClashSlot = (nextClashSlot + 1) % RecentClashRings;
            return true;
        }

        static bool HasTeamDotNear(in DI_StateBroadcast state, int team, Vector2 point, float radius) {
            if (team < 0 || state.dotTeams == null || state.dotPositions == null)
                return false;
            float squared = radius * radius;
            int count = Mathf.Min(state.dotTeams.Length, state.dotPositions.Length);
            for (int i = 0; i < count; i++)
                if (state.dotTeams[i] == team && (state.dotPositions[i] - point).sqrMagnitude <= squared)
                    return true;
            return false;
        }

        void PresentArrivals(in DI_StateBroadcast previous, in DI_StateBroadcast next, IReadOnlyList<DI_BaseView> views) {
            DI_SnapshotEvents.FindVanishedDots(previous, next, scratch, vanished);
            if (nextPunchTime.Length < views.Count)
                Array.Resize(ref nextPunchTime, views.Count);
            float now = Time.unscaledTime;
            foreach (int index in vanished) {
                if (previous.dotPositions == null || index >= previous.dotPositions.Length)
                    continue;
                Vector2 last = previous.dotPositions[index];
                if (DiedInCombat(previous, next, last))
                    continue;
                int baseId = DI_SnapshotEvents.NearestBase(next, last, arrivalRadius);
                if (baseId < 0 || baseId >= views.Count || now < nextPunchTime[baseId])
                    continue;
                nextPunchTime[baseId] = now + arrivalPunchCooldown;
                views[baseId].PlayPunch(arrivalPunch, arrivalPunchSeconds);
            }
        }

        /// <summary>A troop last seen next to a clash or a fresh turret hit was destroyed there, not landed.</summary>
        bool DiedInCombat(in DI_StateBroadcast previous, in DI_StateBroadcast next, Vector2 position) {
            float squared = combatLossRadius * combatLossRadius;
            if (next.clashPositions != null)
                foreach (Vector2 clash in next.clashPositions)
                    if ((clash - position).sqrMagnitude <= squared)
                        return true;
            if (next.turretShotSequences == null || next.turretShotPositions == null || previous.turretShotSequences == null)
                return false;
            int turrets = Mathf.Min(next.turretShotSequences.Length,
                Mathf.Min(next.turretShotPositions.Length, previous.turretShotSequences.Length));
            for (int i = 0; i < turrets; i++)
                if (next.turretShotSequences[i] != previous.turretShotSequences[i] &&
                    (next.turretShotPositions[i] - position).sqrMagnitude <= squared)
                    return true;
            return false;
        }

        void PresentCaptures(in DI_StateBroadcast previous, in DI_StateBroadcast next,
            IReadOnlyList<DI_BaseView> views, Func<int, Color> teamColor) {
            DI_SnapshotEvents.FindCaptures(previous, next, captures);
            int you = next.yourTeamId;
            foreach (DI_Capture capture in captures) {
                Vector3 position = World(next.basePositions[capture.baseId]);
                GameFx.Play(FxKind.ShockwaveRing, position, Color.Lerp(teamColor(capture.teamId), Color.white, captureRingWhiten), captureRingScale);
                if (capture.baseId < views.Count)
                    views[capture.baseId].PlayPunch(capturePunch, capturePunchSeconds);
                if (you >= 0 && capture.teamId == you) {
                    GameSfx.Play(captureSound);
                } else if (you >= 0 && capture.previousTeamId == you) {
                    GameSfx.Play(baseLostSound);
                    CameraShake.Kick(baseLostShake, baseLostShakeSeconds);
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
                    GameFx.Play(FxKind.ShockwaveRing, position, Dim(color, eliminationRingGlow), eliminationRingScale);
                    GameFx.Play(FxKind.ShockwaveRing, position, Color.Lerp(color, Color.white, eliminationInnerWhiten), eliminationInnerRingScale);
                    GameFx.Play(FxKind.Sparks, position, Vector3.up, color, eliminationSparkScale);
                    CameraShake.Explosion(position, eliminationShakeRadius, eliminationShake);
                }
                if (team == next.yourTeamId) {
                    GameSfx.Play(eliminationSound);
                    ScreenFx.Flash(localEliminationFlash, localEliminationFlashSeconds);
                    ScreenFx.ShowText(localEliminationText, Color.Lerp(color, Color.white, localEliminationTextWhiten),
                        localEliminationTextSeconds, localEliminationTextSize);
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
                ScreenFx.Flash(defeatFlash, defeatFlashSeconds);
                return;
            }
            GameSfx.Play(SfxCue.Win);
            ScreenFx.Flash(victoryFlash, victoryFlashSeconds);
            PlayConfetti(next, you);
        }

        /// <summary>Bursts spread across your bases, skipping those behind the match-over panel.</summary>
        void PlayConfetti(in DI_StateBroadcast next, int you) {
            Camera view = Camera.main;
            int held = 0;
            for (int i = 0; i < next.baseTeams.Length; i++)
                if (next.baseTeams[i] == you && InConfettiArea(view, next.basePositions[i]))
                    held++;
            if (held == 0 || maxConfettiBursts == 0)
                return;
            // Every stride-th visible base, so bursts spread over the board rather than bunching at low ids.
            int stride = Mathf.Max(1, held / maxConfettiBursts);
            for (int i = 0, seen = 0, bursts = 0; i < next.baseTeams.Length && bursts < maxConfettiBursts; i++) {
                if (next.baseTeams[i] != you || !InConfettiArea(view, next.basePositions[i]))
                    continue;
                if (seen++ % stride != 0)
                    continue;
                GameFx.Play(FxKind.Confetti, World(next.basePositions[i]), confettiScale);
                bursts++;
            }
        }

        bool InConfettiArea(Camera view, Vector2 point) {
            if (view == null)
                return true;
            Vector3 viewport = view.WorldToViewportPoint(World(point));
            return viewport.z > 0f && confettiArea.Contains(viewport) && !confettiKeepOut.Contains(viewport);
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

        static Color Dim(Color color, float brightness) => new(color.r * brightness, color.g * brightness, color.b * brightness, color.a);
#endif
    }
}
