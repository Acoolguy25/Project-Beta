using System.Collections.Generic;
using RyanAssets.Characters.Client;
using RyanAssets.Characters.Shared;
using RyanAssets.GameFeel;
using RyanAssets.Platformer;
using UnityEngine;

namespace Universes.UniverseData.obby.Client {
    /// <summary>
    /// Sky Obby's race moments (coins, stars, checkpoints, the countdown, finishing, overtakes, launch
    /// pads, falls and respawns) and its mechanics (switches, gravity flips, crushers, hammers, lasers,
    /// cannons, mushrooms, rising lava) as sound, particles, popups, flashes and shakes from the shared
    /// game-feel kit. Each mechanic has its own cue, so a course can be followed by ear.
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
        [Tooltip("Size of each confetti burst over the pad that starts a new tier (an ordinary checkpoint only has the pad's own small burst). " +
                 "Keep it near 1 so pieces stay smaller than the character's head; the burst gets bigger from its count instead.")]
        [Min(0f)] public float tierConfettiScale = 1f;
        [Tooltip("Confetti bursts spread around the pad on a new tier.")]
        [Min(1)] public int tierConfettiBursts = 3;

        [Header("Countdown")]
        public OB_Cue countdownSound = new(SfxCue.CountdownBeep, 0.8f);
        public OB_Cue goSound = new(SfxCue.Go, 1f);

        [Header("Finish")]
        public OB_Cue finishSound = new(SfxCue.Win, 0.9f);
        [Tooltip("Extra sting for a new personal best.")]
        public OB_Cue personalBestSound = new(SfxCue.LevelUp, 0.8f, 1.25f);
        [Tooltip("Size of each finish confetti burst (see tierConfettiScale).")]
        [Min(0f)] public float finishConfettiScale = 1f;
        [Min(1)] public int finishConfettiBursts = 6;
        [Tooltip("Radius in metres the tier and finish bursts are spread over.")]
        [Min(0f)] public float confettiSpread = 3.5f;
        [Min(0f)] public float finishRingScale = 4f;
        [Range(0f, 1f)] public float finishFlashAlpha = 0.35f;

        [Header("Launch pads")]
        [Tooltip("A pad that launches mostly upward (a spring).")]
        public OB_Cue springSound = new(SfxCue.Jump, 0.9f, 0.8f);
        [Tooltip("A pad that launches mostly forward (a boost panel).")]
        public OB_Cue boostSound = new(SfxCue.Whoosh, 1f, 0.85f);

        [Tooltip("A cannon shot.")]
        public OB_Cue cannonSound = new(SfxCue.Explosion, 0.8f, 0.7f);
        [Range(0f, 1f)] public float cannonShake = 0.3f;
        [Tooltip("A bounce on a mushroom cap.")]
        public OB_Cue mushroomSound = new(SfxCue.Jump, 0.85f, 1.45f);

        [Header("Stars")]
        public OB_Cue starSound = new(SfxCue.LevelUp, 0.9f, 1.5f);
        [Min(0f)] public float starBurstScale = 1.4f;
        [Range(0f, 1f)] public float starFlashAlpha = 0.2f;
        [Tooltip("Size of the time bonus popup over a collected star.")]
        [Min(0f)] public float starPopupSize = 1f;

        [Header("Mechanics")]
        [Tooltip("An ON/OFF switch hit by the local player.")]
        public OB_Cue switchSound = new(SfxCue.UiClick, 1f, 0.6f);
        [Range(0f, 1f)] public float switchFlashAlpha = 0.12f;
        [Tooltip("A gravity flip arrow turning the local player's gravity round.")]
        public OB_Cue flipSound = new(SfxCue.Whoosh, 1f, 1.5f);
        [Range(0f, 1f)] public float flipFlashAlpha = 0.18f;
        [Min(0f)] public float flipRingScale = 2.4f;
        [Tooltip("A crusher hitting the floor near the local player (quieter and smaller further away).")]
        public OB_Cue crusherSound = new(SfxCue.Explosion, 0.6f, 0.55f);
        [Range(0f, 1f)] public float crusherShake = 0.35f;
        [Tooltip("Crushers, hammers and lasers further than this from the local player make no sound.")]
        [Min(1f)] public float hearingRadius = 22f;
        [Tooltip("A hammer passing the bottom of its swing.")]
        public OB_Cue hammerSound = new(SfxCue.Whoosh, 0.55f, 0.7f);
        [Tooltip("A laser switching on.")]
        public OB_Cue laserSound = new(SfxCue.Alarm, 0.25f, 2.2f);
        [Tooltip("The local player entering a wind field.")]
        public OB_Cue windSound = new(SfxCue.Whoosh, 0.7f, 0.6f);
        [Tooltip("The local player landing on ice.")]
        public OB_Cue iceSound = new(SfxCue.Land, 0.8f, 1.6f);
        [Tooltip("Size of the frost burst where the local player lands on ice.")]
        [Min(0f)] public float iceBurstScale = 0.8f;
        [Tooltip("The local player's rising-lava chase starting.")]
        public OB_Cue lavaSound = new(SfxCue.Alarm, 0.8f, 0.8f);
        [Range(0f, 1f)] public float lavaFlashAlpha = 0.25f;

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
        readonly Dictionary<LaunchPad, OB_PieceKind> padKinds = new();
        readonly List<Pendulum> pendulums = new();
        readonly List<float> pendulumAngles = new();
        readonly List<TimedToggleBlock> lasers = new();
        readonly List<bool> laserStates = new();
        bool onIce;

        /// <summary>Seconds a coin takes off the finish time on the current course (shown over coins).</summary>
        public float CoinBonus { get; set; }
        /// <summary>Seconds a star takes off the finish time on the current course.</summary>
        public float StarBonus { get; set; }

        /// <summary>Learns the course's pads, hammers and lasers, so each gets its own sound.</summary>
        public void RegisterCourse(OB_Course course) {
            padKinds.Clear();
            pendulums.Clear();
            pendulumAngles.Clear();
            lasers.Clear();
            laserStates.Clear();
            if (course == null || course.Layout == null) return;
            var pieces = course.Layout.pieces;
            var instances = course.Instances;
            for (int i = 0; i < pieces.Count && i < instances.Count; i++) {
                if (instances[i] == null) continue;
                switch (pieces[i].kind) {
                    case OB_PieceKind.Cannon:
                    case OB_PieceKind.Mushroom:
                        var pad = instances[i].GetComponentInChildren<LaunchPad>();
                        if (pad != null) padKinds[pad] = pieces[i].kind;
                        break;
                    case OB_PieceKind.Pendulum:
                        if (instances[i].TryGetComponent(out Pendulum pendulum)) {
                            pendulums.Add(pendulum);
                            pendulumAngles.Add(0f);
                        }
                        break;
                    case OB_PieceKind.Laser:
                        if (instances[i].TryGetComponent(out TimedToggleBlock laser) && !laser.AlwaysSolid) {
                            lasers.Add(laser);
                            laserStates.Add(true);
                        }
                        break;
                }
            }
        }

        void Awake() => coinStreak = new SfxLadder(coinCue, coinStreakSemitones, coinStreakMaxSteps, coinStreakSeconds);

        void OnEnable() {
            LocalCharacter.Teleported += OnTeleported;
            LaunchPad.Launched += OnLaunched;
            OnOffSwitch.Pressed += OnSwitch;
            GravityFlipPad.Flipped += OnFlip;
            Crusher.Slammed += OnSlam;
            RisingLava.ChaseStarted += OnLavaChase;
            WindVolume.Entered += OnWind;
        }

        void OnDisable() {
            LocalCharacter.Teleported -= OnTeleported;
            LaunchPad.Launched -= OnLaunched;
            OnOffSwitch.Pressed -= OnSwitch;
            GravityFlipPad.Flipped -= OnFlip;
            Crusher.Slammed -= OnSlam;
            RisingLava.ChaseStarted -= OnLavaChase;
            WindVolume.Entered -= OnWind;
        }

        void OnWind(WindVolume field) => windSound.Play();

        /// <summary>A crunch and a frost burst when the local player first touches ice.</summary>
        void UpdateIce() {
            var player = CharacterMovement.Local;
            bool now = player != null && player.Body != null && player.Grounded && player.Traction < 1f;
            if (now && !onIce) {
                iceSound.PlayAt(player.Body.position);
                GameFx.Play(FxKind.Sparks, player.Body.position, OB_Palette.Ice, iceBurstScale);
            }
            onIce = now;
        }

        void OnLaunched(LaunchPad pad, CharacterMovement movement) {
            Vector3 at = pad.transform.position;
            if (padKinds.TryGetValue(pad, out OB_PieceKind kind)) {
                if (kind == OB_PieceKind.Cannon) {
                    cannonSound.PlayAt(at);
                    CameraShake.Kick(cannonShake);
                    GameFx.Play(FxKind.Smoke, at + pad.WorldLaunchVelocity.normalized * 2f);
                } else {
                    mushroomSound.PlayAt(at);
                    GameFx.Play(FxKind.ShockwaveRing, at, OB_Palette.MushroomCap);
                }
                return;
            }
            Vector3 launch = pad.localLaunchVelocity;
            bool boost = new Vector2(launch.x, launch.z).sqrMagnitude > launch.y * launch.y;
            (boost ? boostSound : springSound).PlayAt(at);
        }

        void OnSwitch(OnOffSwitch button) {
            bool flipped = OnOffChannels.IsFlipped(button.channel);
            Color colour = flipped ? OB_Palette.OnOffBlue : OB_Palette.OnOffRed;
            switchSound.PlayAt(button.transform.position, flipped ? 1.2f : 1f);
            ScreenFx.Flash(colour.WithAlpha(switchFlashAlpha));
            GameFx.Play(FxKind.Sparks, button.transform.position, colour);
        }

        void OnFlip(GravityFlipPad pad) {
            flipSound.Play();
            ScreenFx.Flash(OB_Palette.Flip.WithAlpha(flipFlashAlpha));
            var player = CharacterMovement.Local;
            if (player != null && player.Body != null) GameFx.Play(FxKind.ShockwaveRing, player.Body.position, pad.WorldDown, OB_Palette.Flip, flipRingScale);
        }

        void OnSlam(Crusher crusher) {
            float nearness = Nearness(crusher.transform.position);
            if (nearness <= 0f) return;
            // Slammed is raised as the block lands, so its underside is on the floor.
            Vector3 floor = crusher.transform.position + Vector3.down * (crusher.transform.lossyScale.y * 0.5f);
            crusherSound.PlayAt(floor);
            CameraShake.Kick(crusherShake * nearness);
            GameFx.Play(FxKind.DustPuff, floor, 2f);
        }

        void OnLavaChase(RisingLava lava) {
            lavaSound.Play();
            ScreenFx.Flash(OB_Palette.Lava.WithAlpha(lavaFlashAlpha));
        }

        /// <summary>1 next to the local player, falling to 0 at <see cref="hearingRadius"/>.</summary>
        float Nearness(Vector3 position) {
            var player = CharacterMovement.Local;
            if (player == null || player.Body == null) return 0f;
            return 1f - Mathf.Clamp01(Vector3.Distance(position, player.Body.position) / hearingRadius);
        }

        void Update() {
            UpdateIce();
            double now = PlatformerClock.Now;
            for (int i = 0; i < pendulums.Count; i++) {
                if (pendulums[i] == null) continue;
                float angle = pendulums[i].AngleAt(now);
                // A swoosh as the head sweeps through the bottom of its swing.
                if (Mathf.Sign(angle) != Mathf.Sign(pendulumAngles[i]) && Nearness(pendulums[i].head != null ? pendulums[i].head.position : pendulums[i].transform.position) > 0f)
                    hammerSound.PlayAt(pendulums[i].head != null ? pendulums[i].head.position : pendulums[i].transform.position);
                pendulumAngles[i] = angle;
            }
            for (int i = 0; i < lasers.Count; i++) {
                if (lasers[i] == null) continue;
                bool on = lasers[i].IsSolidAt(now);
                if (on && !laserStates[i] && Nearness(lasers[i].transform.position) > 0f) laserSound.PlayAt(lasers[i].transform.position);
                laserStates[i] = on;
            }
        }

        // ---------------------------------------------------------------- moments
        public void Coin(Vector3 position) {
            GameFx.Play(FxKind.CoinBurst, position, coinBurstScale);
            coinStreak.PlayAt(position, coinVolume);
            if (CoinBonus > 0f) FloatingText.Number(position, "-{0:1}s", CoinBonus, OB_Palette.Gold, coinPopupSize);
        }

        public void Star(Vector3 position) {
            starSound.PlayAt(position);
            GameFx.Play(FxKind.CoinBurst, position, OB_Palette.Star, starBurstScale);
            GameFx.Play(FxKind.Sparks, position, OB_Palette.Star, starBurstScale);
            ScreenFx.Flash(OB_Palette.Star.WithAlpha(starFlashAlpha));
            if (StarBonus > 0f) FloatingText.Number(position, "-{0:1}s", StarBonus, OB_Palette.Star, starPopupSize);
        }

        public void Checkpoint(bool newTier, Color tierAccent, Vector3 pad) {
            if (!newTier) {
                checkpointSound.Play();
                return;
            }
            tierSound.Play();
            ScreenFx.Flash(tierAccent.WithAlpha(tierFlashAlpha), tierFlashSeconds);
            Confetti(pad, tierConfettiBursts, tierConfettiScale);
        }

        public void CountdownTick() => countdownSound.Play();

        public void Go() => goSound.Play();

        public void Finish(Vector3 position, bool personalBest) {
            finishSound.Play();
            if (personalBest) personalBestSound.Play();
            Confetti(position, finishConfettiBursts, finishConfettiScale);
            GameFx.Play(FxKind.ShockwaveRing, position, OB_Palette.Gold, finishRingScale);
            ScreenFx.Flash(OB_Palette.Gold.WithAlpha(finishFlashAlpha));
        }

        /// <summary>A bigger celebration from more small bursts, not bigger pieces: one in the middle, the rest on a ring.</summary>
        void Confetti(Vector3 centre, int bursts, float scale) {
            GameFx.Play(FxKind.Confetti, centre, scale);
            for (int i = 1; i < bursts; i++) {
                float angle = (i - 1) * Mathf.PI * 2f / (bursts - 1);
                GameFx.Play(FxKind.Confetti, centre + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * confettiSpread, scale);
            }
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
