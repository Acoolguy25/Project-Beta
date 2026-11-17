using RyanAssets.Characters.Client;
using RyanAssets.GameFeel;
using RyanAssets.Shared.Declarations;
using RyanAssets.Shared.Global;
using RyanAssets.Tools.Shared;
using UnityEngine;

namespace Universes.UniverseData.classic_horror.Client {
    /// <summary>
    /// The investigator's body reacting to the Presence: a heartbeat that quickens and breathing that
    /// grows ragged as it gets closer, a low drone, a red edge on the screen, and the flashlight
    /// stuttering when it is near. The chase music the server switches on is met with a sting and a
    /// jolt, and the fear settles slowly once it ends. Presentation only.
    /// </summary>
    public sealed class CH_FearLoop : MonoBehaviour {
        [Header("Distance")]
        [Tooltip("Fear starts at this distance from the Presence (metres).")]
        [SerializeField, Min(1f)] float fearStartDistance = 42f;
        [Tooltip("Fear is at its peak at this distance.")]
        [SerializeField, Min(0.5f)] float fearPeakDistance = 7f;
        [Tooltip("Extra fear while the Presence is chasing someone, even far away.")]
        [SerializeField, Range(0f, 1f)] float chaseFearFloor = 0.45f;
        [Tooltip("How fast fear rises and how slowly it settles (per second).")]
        [SerializeField, Min(0.1f)] float fearRiseRate = 2.5f;
        [SerializeField, Min(0.05f)] float fearFallRate = 0.35f;

        [Header("Heartbeat")]
        public AudioSource heartbeatSource;
        public AudioClip heartbeatClip;
        [SerializeField, Min(20f)] float calmBeatsPerMinute = 62f;
        [SerializeField, Min(20f)] float panicBeatsPerMinute = 142f;
        [Tooltip("Fear below this is silent; the heartbeat fades in above it.")]
        [SerializeField, Range(0f, 1f)] float heartbeatThreshold = 0.12f;
        [SerializeField, Range(0f, 1f)] float heartbeatVolume = 0.85f;
        [Tooltip("Fraction of heartbeatVolume when the heartbeat first becomes audible.")]
        [SerializeField, Range(0f, 1f)] float heartbeatQuietFraction = 0.35f;

        [Header("Breathing")]
        [Tooltip("Looping breath; its volume and pace follow fear.")]
        public AudioSource breathingSource;
        [SerializeField, Range(0f, 1f)] float breathingVolume = 0.5f;
        [SerializeField] Vector2 breathingPitch = new(0.92f, 1.32f);
        [SerializeField, Range(0f, 1f)] float breathingThreshold = 0.3f;

        [Header("Presence drone and screen")]
        public AudioSource droneSource;
        [SerializeField, Range(0f, 1f)] float droneVolume = 0.38f;
        [SerializeField] Vector2 dronePitch = new(0.7f, 1.05f);
        [Tooltip("Edge vignette (the shared danger vignette, drawn under the HUD); it beats with the heart.")]
        [SerializeField] Color dangerColor = new(0.42f, 0.02f, 0.02f);
        [SerializeField, Range(0f, 1f)] float dangerMaxAlpha = 0.22f;
        [Tooltip("Fear below this leaves the screen edge clear.")]
        [SerializeField, Range(0f, 1f)] float dangerThreshold = 0.2f;
        [Tooltip("The vignette is only re-sent when its strength moves by more than this.")]
        [SerializeField, Range(0.005f, 0.2f)] float dangerStep = 0.03f;

        [Header("Flashlight")]
        [Tooltip("Above this fear the local flashlight starts to stutter.")]
        [SerializeField, Range(0f, 1f)] float flashlightFlickerThreshold = 0.7f;
        [SerializeField, Min(0.1f)] float flashlightFlickerSpeed = 18f;
        [Tooltip("Lowest beam strength during a stutter (fraction of normal).")]
        [SerializeField, Range(0f, 1f)] float flashlightFlickerFloor = 0.08f;
        [Tooltip("Share of the time the beam is out at peak fear.")]
        [SerializeField, Range(0f, 1f)] float flashlightDropout = 0.6f;

        [Tooltip("A surface closer to the beam than this (metres) dims it, so a wall at arm's length is not blown out to white.")]
        [SerializeField, Min(0.1f)] float closeRangeDistance = 2.5f;
        [Tooltip("Beam strength (fraction of normal) against a surface right at the lens. Between the lens and the close-range "
            + "distance the strength follows the square of the distance, as the light on the surface grows with it.")]
        [SerializeField, Range(0.01f, 1f)] float closeRangeFloor = 0.12f;
        [Tooltip("How fast the beam follows the close-range dimming (per second).")]
        [SerializeField, Min(0.5f)] float closeRangeRate = 6f;

        [Header("Chase hand-off")]
        [Tooltip("Played once when the chase music starts; the music itself crossfades in.")]
        public SfxSound chaseSting = new();
        [SerializeField, Range(0f, 1f)] float chaseShake = 0.25f;
        [SerializeField, Min(0f)] float chaseShakeSeconds = 0.4f;

        public float Fear { get; private set; }
        /// <summary>The local investigator's flashlight once it has spawned (null before).</summary>
        public ToolFlashlightShared LocalFlashlight => localFlashlight;
        public bool Chasing { get; private set; }

        CH_Atmosphere atmosphere;
        Transform monster;
        bool active;
        float nextBeat;
        Light localBeam;
        float localBeamIntensity, nextBeamSearch;
        ToolFlashlightShared localFlashlight;
        // The flashlight spawns a moment after the character; look for it at most this often.
        const float BeamSearchInterval = 1f;
        float sentDanger, sentDangerPulse;
        const float BeamProbeRadius = 0.15f;
        float closeRange = 1f;
        int beamMask;

        void Awake() {
            atmosphere = GetComponent<CH_Atmosphere>();
            beamMask = ~LayerMask.GetMask("Character", "LocalCharacter", "UI", "Ignore Raycast", "Water");
            if (breathingSource != null) { breathingSource.loop = true; breathingSource.volume = 0f; }
            if (droneSource != null) { droneSource.loop = true; droneSource.volume = 0f; }
        }

        void OnDisable() {
            RestoreFlashlight();
            ScreenFx.ClearDanger();
            sentDanger = sentDangerPulse = 0f;
        }

        /// <summary>Who to be afraid of. Null or inactive (dead, case over) lets the fear settle.</summary>
        public void SetThreat(Transform presence, bool caseRunning) {
            monster = presence;
            active = caseRunning;
        }

        /// <summary>The server switches the shared music track when a chase starts; meet it with a sting and a jolt.</summary>
        void UpdateChase() {
            var events = SharedGlobalEvents.Instance;
            bool chasing = active && events != null && events.MusicTrack.Value == MusicSelection.HorrorChaseMusic;
            if (chasing && !Chasing) {
                GameSfx.Play(chaseSting);
                CameraShake.Kick(chaseShake, chaseShakeSeconds);
            }
            Chasing = chasing;
        }

        void Update() {
            UpdateChase();
            float target = TargetFear();
            float rate = target > Fear ? fearRiseRate : fearFallRate;
            Fear = Mathf.MoveTowards(Fear, target, rate * Time.deltaTime);
            atmosphere?.SetThreat(Fear);
            UpdateHeartbeat();
            UpdateBreathing();
            UpdateDrone();
            UpdateDanger();
            UpdateFlashlight();
        }

        float TargetFear() {
            if (!active || LocalPlayer.Character == null || LocalPlayer.Character.IsDead) return 0f;
            float distanceFear = 0f;
            Camera main = Camera.main;
            if (monster != null && main != null)
                distanceFear = Mathf.InverseLerp(fearStartDistance, fearPeakDistance, Vector3.Distance(monster.position, main.transform.position));
            return Mathf.Max(distanceFear, Chasing ? chaseFearFloor : 0f);
        }

        void UpdateHeartbeat() {
            if (heartbeatSource == null || heartbeatClip == null || Fear < heartbeatThreshold) return;
            if (Time.time < nextBeat) return;
            nextBeat = Time.time + 60f / BeatsPerMinute;
            float loudness = Mathf.InverseLerp(heartbeatThreshold, 1f, Fear);
            heartbeatSource.PlayOneShot(heartbeatClip, heartbeatVolume * Mathf.Lerp(heartbeatQuietFraction, 1f, loudness));
        }

        void UpdateBreathing() {
            if (breathingSource == null) return;
            float amount = Mathf.InverseLerp(breathingThreshold, 1f, Fear);
            breathingSource.volume = amount * breathingVolume;
            breathingSource.pitch = Mathf.Lerp(breathingPitch.x, breathingPitch.y, amount);
            if (amount > 0f && !breathingSource.isPlaying) breathingSource.Play();
            else if (amount <= 0f && breathingSource.isPlaying) breathingSource.Pause();
        }

        float BeatsPerMinute => Mathf.Lerp(calmBeatsPerMinute, panicBeatsPerMinute, Fear);

        void UpdateDrone() {
            if (droneSource == null) return;
            droneSource.volume = Fear * droneVolume;
            droneSource.pitch = Mathf.Lerp(dronePitch.x, dronePitch.y, Fear);
        }

        /// <summary>The shared edge vignette, beating at the heart rate; only re-sent when it visibly changes.</summary>
        void UpdateDanger() {
            float strength = Mathf.InverseLerp(dangerThreshold, 1f, Fear);
            float pulse = strength > 0f ? BeatsPerMinute / 60f : 0f;
            bool cleared = strength <= 0f && sentDanger > 0f;
            if (!cleared && Mathf.Abs(strength - sentDanger) < dangerStep && Mathf.Abs(pulse - sentDangerPulse) < dangerStep * 4f) return;
            sentDanger = strength;
            sentDangerPulse = pulse;
            ScreenFx.SetDanger(strength, dangerColor, dangerMaxAlpha, pulse);
        }

        void UpdateFlashlight() {
            if (!FindLocalBeam()) return;
            float stutter = 1f;
            float flicker = Mathf.InverseLerp(flashlightFlickerThreshold, 1f, Fear);
            if (flicker > 0f) {
                float noise = Mathf.PerlinNoise(Time.time * flashlightFlickerSpeed, 0.37f);
                // The closer it is, the more often the beam drops out.
                if (noise < flicker * flashlightDropout) stutter = flashlightFlickerFloor;
            }
            float lightsOut = atmosphere != null ? atmosphere.LightsOutAmount : 0f;
            closeRange = Mathf.MoveTowards(closeRange, CloseRangeScale(), closeRangeRate * Time.deltaTime);
            localBeam.intensity = localBeamIntensity * closeRange * Mathf.Min(stutter, 1f - lightsOut);
        }

        /// <summary>Less light the closer the surface the beam is pointed at: one short ray along the beam.</summary>
        float CloseRangeScale() {
            Transform beam = localBeam.transform;
            // A thin sphere rather than a ray, so a post or a door frame right in front of the lens counts too.
            if (!Physics.SphereCast(beam.position, BeamProbeRadius, beam.forward, out RaycastHit hit, closeRangeDistance, beamMask, QueryTriggerInteraction.Ignore))
                return 1f;
            float t = hit.distance / closeRangeDistance;
            return Mathf.Max(closeRangeFloor, t * t);
        }

        /// <summary>Caches the beam of the local investigator's flashlight; it changes when they respawn.</summary>
        bool FindLocalBeam() {
            var character = LocalPlayer.Character;
            if (localFlashlight != null && localFlashlight.connectedCharacter == character) return localBeam != null;
            RestoreFlashlight();
            if (character == null || Time.unscaledTime < nextBeamSearch) return false;
            nextBeamSearch = Time.unscaledTime + BeamSearchInterval;
            foreach (var flashlight in FindObjectsByType<ToolFlashlightShared>()) {
                if (flashlight.connectedCharacter != character) continue;
                localFlashlight = flashlight;
                localBeam = flashlight.GetComponentInChildren<Light>(true);
                localBeamIntensity = localBeam != null ? localBeam.intensity : 0f;
                return localBeam != null;
            }
            return false;
        }

        void RestoreFlashlight() {
            if (localBeam != null) localBeam.intensity = localBeamIntensity;
            localBeam = null;
            localFlashlight = null;
        }
    }
}
