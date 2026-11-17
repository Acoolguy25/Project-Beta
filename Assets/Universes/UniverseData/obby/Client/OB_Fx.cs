using RyanAssets.Characters.Client;
using RyanAssets.Characters.Shared;
using RyanAssets.GameFeel;
using RyanAssets.Platformer;
using UnityEngine;

namespace Universes.UniverseData.obby.Client {
    /// <summary>
    /// Sky Obby's race moments (coins, checkpoints, the countdown, finishing, overtakes, launch pads,
    /// falls and respawns) as sound, particles, popups, flashes and shakes from the shared game-feel kit.
    /// Called by <see cref="OB_ClientController"/> when the server reports progress; checkpoint pads
    /// carry their own confetti (<c>CheckpointMarker.reachedEffect</c>), and jumps and landings come
    /// from the kit's character feel.
    /// </summary>
    public sealed class OB_Fx : MonoBehaviour {
        [Header("Coins")]
        public SfxCue coinCue = SfxCue.Coin;
        [Range(0f, 1f)] public float coinVolume = 0.9f;
        [Tooltip("Each coin collected without falling plays this many semitones higher than the last.")]
        [Min(0f)] public float coinStreakSemitones = 1f;
        [Tooltip("Highest step of the coin streak.")]
        [Min(0)] public int coinStreakMaxSteps = 12;
        [Tooltip("Seconds without a coin that end the streak (a fall or hazard ends it too).")]
        [Min(0.1f)] public float coinStreakSeconds = 90f;
        [Min(0f)] public float coinBurstScale = 1.6f;
        [Tooltip("Size of the time bonus popup over a collected coin.")]
        [Min(0f)] public float coinPopupSize = 0.8f;

        [Header("Checkpoints")]
        public OB_Cue checkpointSound = new(SfxCue.Pickup, 0.9f, 1.15f);
        [Tooltip("Played instead of the checkpoint sound when the checkpoint starts a new tier.")]
        public OB_Cue tierSound = new(SfxCue.LevelUp, 0.85f);
        [Tooltip("Screen flash on a new tier, in the tier's accent colour at this peak opacity.")]
        [Range(0f, 1f)] public float tierFlashAlpha = 0.25f;
        [Min(0.01f)] public float tierFlashSeconds = 0.5f;
        [Tooltip("Big confetti burst over the pad that starts a new tier (an ordinary checkpoint only has the pad's own small burst).")]
        [Min(0f)] public float tierConfettiScale = 2.5f;

        [Header("Countdown")]
        public OB_Cue countdownSound = new(SfxCue.CountdownBeep, 0.8f);
        public OB_Cue goSound = new(SfxCue.Go, 1f);

        [Header("Finish")]
        public OB_Cue finishSound = new(SfxCue.Win, 0.9f);
        [Tooltip("Extra sting for a new personal best.")]
        public OB_Cue personalBestSound = new(SfxCue.LevelUp, 0.8f, 1.25f);
        [Min(0f)] public float finishConfettiScale = 3.5f;
        [Min(0f)] public float finishRingScale = 4f;
        [Range(0f, 1f)] public float finishFlashAlpha = 0.35f;

        [Header("Launch pads")]
        [Tooltip("A pad that launches mostly upward (a spring).")]
        public OB_Cue springSound = new(SfxCue.Jump, 0.9f, 0.8f);
        [Tooltip("A pad that launches mostly forward (a boost panel).")]
        public OB_Cue boostSound = new(SfxCue.Whoosh, 1f, 0.85f);

        [Header("Overtakes")]
        public OB_Cue gainedPlaceSound = new(SfxCue.Pickup, 0.7f, 1.5f);
        public OB_Cue lostPlaceSound = new(SfxCue.Whoosh, 0.7f, 1.2f);

        [Header("Hazards, falls and respawns")]
        public OB_Cue hazardSound = new(SfxCue.Hit, 1f, 0.9f);
        [Range(0f, 1f)] public float hazardShake = 0.45f;
        [Min(0f)] public float hazardShakeSeconds = 0.3f;
        [Range(0f, 1f)] public float hazardFlashAlpha = 0.3f;
        public OB_Cue respawnSound = new(SfxCue.Pickup, 0.6f, 0.75f);
        [Tooltip("Two rings spread out around the player where they reappear: a white inner one and a gravity-blue outer one.")]
        [Min(0f)] public float respawnInnerRingScale = 1.6f;
        [Min(0f)] public float respawnRingScale = 3.2f;
        [Tooltip("A respawn effect waits this long for the server's teleport after a fall or hazard.")]
        [Min(0.1f)] public float respawnWaitSeconds = 3f;
        [Tooltip("A teleport this recent when a fall is reported is taken as its respawn (hazard respawns always come later).")]
        [Min(0f)] public float teleportMatchSeconds = 0.75f;

        SfxLadder coinStreak;
        float respawnExpected = float.NegativeInfinity, lastTeleport = float.NegativeInfinity;

        /// <summary>Seconds a coin takes off the finish time on the current course (shown over coins).</summary>
        public float CoinBonus { get; set; }

        void Awake() => coinStreak = new SfxLadder(coinCue, coinStreakSemitones, coinStreakMaxSteps, coinStreakSeconds);

        void OnEnable() {
            LocalCharacter.Teleported += OnTeleported;
            LaunchPad.Launched += OnLaunched;
        }

        void OnDisable() {
            LocalCharacter.Teleported -= OnTeleported;
            LaunchPad.Launched -= OnLaunched;
        }

        void OnLaunched(LaunchPad pad, CharacterMovement movement) {
            Vector3 launch = pad.localLaunchVelocity;
            bool boost = new Vector2(launch.x, launch.z).sqrMagnitude > launch.y * launch.y;
            (boost ? boostSound : springSound).PlayAt(pad.transform.position);
        }

        // ---------------------------------------------------------------- moments
        public void Coin(Vector3 position) {
            GameFx.Play(FxKind.CoinBurst, position, coinBurstScale);
            coinStreak.PlayAt(position, coinVolume);
            if (CoinBonus > 0f) FloatingText.Number(position, "-{0:1}s", CoinBonus, OB_Palette.Gold, coinPopupSize);
        }

        public void Checkpoint(bool newTier, Color tierAccent, Vector3 pad) {
            if (!newTier) {
                checkpointSound.Play();
                return;
            }
            tierSound.Play();
            ScreenFx.Flash(tierAccent.WithAlpha(tierFlashAlpha), tierFlashSeconds);
            GameFx.Play(FxKind.Confetti, pad, tierConfettiScale);
        }

        public void CountdownTick() => countdownSound.Play();

        public void Go() => goSound.Play();

        public void Finish(Vector3 position, bool personalBest) {
            finishSound.Play();
            if (personalBest) personalBestSound.Play();
            GameFx.Play(FxKind.Confetti, position, finishConfettiScale);
            GameFx.Play(FxKind.ShockwaveRing, position, OB_Palette.Gold, finishRingScale);
            ScreenFx.Flash(OB_Palette.Gold.WithAlpha(finishFlashAlpha));
        }

        public void GainedPlace() => gainedPlaceSound.Play();

        public void LostPlace() => lostPlaceSound.Play();

        /// <summary>The local player touched a hazard: they are knocked limp, then respawned by the server.</summary>
        public void Hazard() {
            hazardSound.Play();
            CameraShake.Kick(hazardShake, hazardShakeSeconds);
            ScreenFx.Flash(OB_Palette.Lava.WithAlpha(hazardFlashAlpha));
            // The server keeps a hit player limp for a moment before the teleport, so only a later one counts.
            ExpectRespawn(false);
        }

        /// <summary>The local player fell off the course and is being returned to their checkpoint.</summary>
        public void Fell() => ExpectRespawn(true);

        // ---------------------------------------------------------------- respawns
        /// <summary>
        /// The server's teleport and its progress message travel separately, so whichever arrives second
        /// plays the effect: a teleport just before this call counts too when <paramref name="matchEarlier"/>.
        /// </summary>
        void ExpectRespawn(bool matchEarlier) {
            coinStreak.Reset();
            if (matchEarlier && Time.unscaledTime - lastTeleport < teleportMatchSeconds) {
                PlayRespawn();
                return;
            }
            respawnExpected = Time.unscaledTime + respawnWaitSeconds;
        }

        void OnTeleported(LocalCharacter character) {
            if (character == null || LocalPlayer.Character != character) return;
            lastTeleport = Time.unscaledTime;
            if (Time.unscaledTime > respawnExpected) return;
            respawnExpected = float.NegativeInfinity;
            PlayRespawn();
        }

        void PlayRespawn() {
            lastTeleport = float.NegativeInfinity;
            var player = CharacterMovement.Local;
            if (player == null || player.Body == null) return;
            Vector3 position = player.Body.position, up = player.Up;
            respawnSound.PlayAt(position);
            // A ring, not a dust puff: tinted dust reads as a dirty smudge on the pale course.
            GameFx.Play(FxKind.ShockwaveRing, position, up, Color.white, respawnInnerRingScale);
            GameFx.Play(FxKind.ShockwaveRing, position, up, OB_Palette.Gravity, respawnRingScale);
        }
    }
}
