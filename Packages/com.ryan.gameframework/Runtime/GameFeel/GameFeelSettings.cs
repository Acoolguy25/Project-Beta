using System;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.Rendering;

namespace RyanAssets.GameFeel {
    /// <summary>
    /// Every tuning value and asset reference of the game-feel kit, in one asset loaded from
    /// <c>Resources/GameFeelSettings</c>. Generated (and kept wired) by Ryan &gt; Game Feel &gt; Rebuild;
    /// numbers set by hand survive a rebuild.
    /// </summary>
    [CreateAssetMenu(menuName = "Ryan/Game Feel/Settings", fileName = "GameFeelSettings")]
    public sealed class GameFeelSettings : ScriptableObject {
        public const string ResourcePath = "GameFeelSettings";

        [Serializable]
        public sealed class AudioTuning {
            [Tooltip("Mixer group every kit sound plays through (the SFX group, so the SFX Volume setting applies).")]
            public AudioMixerGroup mixerGroup;
            [Tooltip("Sounds that can play at once; the oldest is cut when all are busy.")]
            [Range(4, 64)] public int voices = 24;
            [Tooltip("3D sounds: full volume within this distance (m), unless the sound sets its own.")]
            [Min(0.1f)] public float minDistance = 6f;
            [Tooltip("3D sounds: inaudible beyond this distance (m), unless the sound sets its own.")]
            [Min(1f)] public float maxDistance = 90f;
            [Tooltip("Doppler on moving 3D sounds (0 = none; one-shots rarely want it).")]
            [Range(0f, 1f)] public float doppler;
        }

        [Serializable]
        public sealed class CameraShakeTuning {
            [Tooltip("Rotation (degrees) at full strength.")]
            [Range(0f, 15f)] public float maxAngle = 2.5f;
            [Tooltip("Camera-space offset (m) at full strength.")]
            [Range(0f, 2f)] public float maxOffset = 0.12f;
            [Tooltip("Noise frequency (Hz): higher feels sharper.")]
            [Range(1f, 60f)] public float frequency = 22f;
            [Tooltip("Roll share of the rotation (0 = pitch and yaw only).")]
            [Range(0f, 1f)] public float roll = 0.5f;
            [Tooltip("How quickly a kick dies away (1 = linear, 2 = quadratic).")]
            [Range(0.5f, 4f)] public float decayExponent = 2f;
            [Tooltip("Strength curve over distance for Explosion: 1 = linear falloff to the radius, higher = faster.")]
            [Range(0.5f, 4f)] public float falloffExponent = 1.5f;
            [Tooltip("Kicks that can overlap; the weakest is replaced when full.")]
            [Range(1, 16)] public int maxKicks = 8;
        }

        [Serializable]
        public sealed class CameraFovTuning {
            [Tooltip("Most extra degrees all kicks and the boost may add together.")]
            [Range(0f, 40f)] public float maxExtraDegrees = 20f;
            [Tooltip("Extra degrees at CameraFov.SetBoost(1).")]
            [Range(0f, 30f)] public float boostDegrees = 8f;
            [Tooltip("Seconds for the boost to follow a new value.")]
            [Range(0.01f, 2f)] public float boostFollowSeconds = 0.3f;
            [Tooltip("Share of a kick spent widening before it eases back.")]
            [Range(0.01f, 0.9f)] public float kickRiseShare = 0.15f;
        }

        [Serializable]
        public sealed class FloatingTextTuning {
            [Tooltip("Pooled popup prefab (a world-space TextMeshPro).")]
            public FloatingTextLabel prefab;
            [Range(4, 64)] public int poolSize = 24;
            [Tooltip("Distance (m) at which a popup has exactly its style's size.")]
            [Min(0.1f)] public float referenceDistance = 14f;
            [Tooltip("Popups closer than the reference shrink, but never below this factor.")]
            [Range(0.1f, 1f)] public float minDistanceScale = 0.5f;
            [Tooltip("Popups further than the reference grow to stay readable, up to this factor.")]
            [Range(1f, 10f)] public float maxDistanceScale = 4f;
            [Tooltip("Largest share of the screen height one popup may cover; closer popups shrink to it, so a callout at the player's feet never fills the view.")]
            [Range(0.02f, 0.5f)] public float maxScreenHeight = 0.09f;
            public FloatingTextStyle normal = new(Color.white, 0.9f, 2.2f, 1.1f, 1.5f, 0.6f);
            public FloatingTextStyle damage = new(new Color(1f, 0.32f, 0.28f), 0.8f, 1.8f, 0.8f, 1.8f, 0.8f);
            public FloatingTextStyle money = new(new Color(1f, 0.79f, 0.24f), 0.9f, 2.4f, 1.2f, 1.6f, 0.5f);
            public FloatingTextStyle heal = new(new Color(0.3f, 0.92f, 0.5f), 0.8f, 2f, 1f, 1.5f, 0.6f);
            public FloatingTextStyle callout = new(Color.white, 1.6f, 1.6f, 1.8f, 2f, 0f);
        }

        [Serializable]
        public sealed class CharacterFeelTuning {
            [Tooltip("Give every character landing squash, run dust and jump sounds.")]
            public bool enabled = true;
            [Tooltip("Characters further than this from the camera (m) get no feel effects.")]
            [Min(1f)] public float maxDistance = 70f;
            [Tooltip("Smallest take-off speed (m/s, against gravity) that plays the jump sound and puff.")]
            [Min(0f)] public float jumpSpeed = 6f;
            [Tooltip("Smallest landing speed (m/s, along gravity) that squashes and puffs.")]
            [Min(0f)] public float landSpeed = 9f;
            [Tooltip("Landing speed (m/s) that gives the full squash.")]
            [Min(0.1f)] public float hardLandSpeed = 35f;
            [Tooltip("Squash at the softest landing that squashes (0.1 = a tenth shorter).")]
            [Range(0f, 0.6f)] public float minSquash = 0.1f;
            [Tooltip("Squash at a full-speed landing (0.25 = a quarter shorter).")]
            [Range(0f, 0.6f)] public float maxSquash = 0.28f;
            [Tooltip("Seconds the landing squash takes.")]
            [Range(0.05f, 1f)] public float squashSeconds = 0.32f;
            [Tooltip("Ground speed (m/s) above which footsteps kick up dust.")]
            [Min(0f)] public float runDustSpeed = 11f;
            [Tooltip("Dust puff size on footsteps and jumps, per unit of character scale (the robot is scale 3).")]
            [Range(0.01f, 2f)] public float footDustScale = 0.22f;
            [Tooltip("Landing puff size as a multiple of the footstep puff, for the softest and the hardest landing.")]
            public Vector2 landDustScale = new(1.4f, 2.6f);
            [Tooltip("Dust colour (multiplies the puff).")]
            public Color dustColor = new(0.86f, 0.82f, 0.74f, 0.8f);
            [Tooltip("Play the Jump cue on take-off.")]
            public bool jumpSound = true;
            [Tooltip("Play the Land cue on landing. Off for the robot, whose animation already plays Player_Land.")]
            public bool landSound;
            [Tooltip("Land cue volume for the softest and the hardest landing.")]
            public Vector2 landVolume = new(0.5f, 1f);
            [Tooltip("Kick the camera when the local player lands hard (scaled by the player's Camera Shake setting).")]
            public bool landShake = true;
            [Tooltip("Landing speed (m/s, along gravity) from which the local player's landing shakes the camera.")]
            [Min(0f)] public float landShakeSpeed = 18f;
            [Tooltip("Shake strength (0..1) at that speed and at the hard-landing speed.")]
            public Vector2 landShakeStrength = new(0.08f, 0.35f);
            [Tooltip("Seconds the landing shake lasts.")]
            [Range(0.05f, 1f)] public float landShakeSeconds = 0.25f;
            [Tooltip("Seconds after a teleport or respawn during which landings never shake the camera.")]
            [Min(0f)] public float teleportGraceSeconds = 0.75f;
            [Tooltip("Effects for the special stances (dash, wall slide, wall jump).")]
            public StanceFeelTuning stances = new();
        }

        [Serializable]
        public sealed class StanceFeelTuning {
            [Header("Dash")]
            [Tooltip("Whoosh and a burst of dust at the feet when a dash starts, and a trail while it lasts (every character this client sees).")]
            public bool dashEffects = true;
            [Tooltip("Whoosh cue volume and pitch when a dash starts.")]
            public Vector2 dashWhoosh = new(0.9f, 1.25f);
            [Tooltip("Colour of the dash trail.")]
            public Color dashTrailColor = new(0.45f, 0.85f, 1f, 0.85f);
            [Tooltip("Trail size per unit of character scale (the robot is scale 3).")]
            [Range(0.01f, 2f)] public float dashTrailScale = 0.3f;
            [Tooltip("Where the trail is attached, as a fraction of the character's height up and a fraction of its depth behind the centre.")]
            public Vector2 dashTrailAnchor = new(0.45f, 0.35f);
            [Tooltip("Extra field of view (degrees) that kicks in when the local player's dash starts.")]
            [Range(0f, 30f)] public float dashFovKick = 9f;
            [Tooltip("Field of view (degrees) held while the local player dashes.")]
            [Range(0f, 30f)] public float dashFovBoost = 8f;
            [Tooltip("Speed-line intensity (0..1) while the local player dashes.")]
            [Range(0f, 1f)] public float dashSpeedLines = 0.85f;
            [Tooltip("Dust puff size at the feet when a dash starts, per unit of character scale.")]
            [Range(0.01f, 2f)] public float dashBurstDust = 0.5f;

            [Header("Wall slide and wall jump")]
            [Tooltip("Dust and sparks from the wall while a character slides down it, and a push-off burst on a wall jump.")]
            public bool wallEffects = true;
            [Tooltip("Seconds between dust puffs while sliding.")]
            [Min(0.02f)] public float wallPuffSeconds = 0.09f;
            [Tooltip("Chance (0..1) that a puff also throws sparks.")]
            [Range(0f, 1f)] public float wallSparkChance = 0.35f;
            [Tooltip("Dust puff size at the wall, per unit of character scale.")]
            [Range(0.01f, 2f)] public float wallDustScale = 0.22f;
            [Tooltip("Spark size at the wall, per unit of character scale.")]
            [Range(0.01f, 2f)] public float wallSparkScale = 0.14f;
            [Tooltip("Sound volume of the cling and of the push-off.")]
            public Vector2 wallVolume = new(0.4f, 0.8f);
        }

        [Serializable]
        public sealed class ExplosionTuning {
            [Tooltip("Shake the camera (and play a sound for blasts that author none) whenever a shared ExplosionEffect goes off on this client.")]
            public bool enabled = true;
            [Tooltip("Shake strength (0..1) at the centre of a blast drawn at scale 1.")]
            [Range(0f, 1f)] public float shakeStrength = 0.7f;
            [Tooltip("Metres from the blast at which the shake has died out, for a blast drawn at scale 1 (a damaging blast uses at least twice its damage radius).")]
            [Min(1f)] public float shakeRadius = 40f;
            [Tooltip("Seconds the shake lasts.")]
            [Range(0.1f, 2f)] public float shakeSeconds = 0.6f;
            [Tooltip("Blasts drawn at this scale or larger use the ExplosionLarge cue and full strength.")]
            [Min(1f)] public float largeScale = 2.5f;
        }

        [Serializable]
        public sealed class MoodTuning {
            public VolumeProfile arcadeBright;
            public VolumeProfile warmIndoor;
            public VolumeProfile night;
            public VolumeProfile horror;
            [Tooltip("Priority of the kit's global mood volume (above a scene's own global volume).")]
            public float priority = 20f;
            [Tooltip("Turn Post Processing on for the main camera while a mood is applied (and back off after Clear) when the camera has it off.")]
            public bool enablePostProcessing = true;
        }

        [Header("Sound")]
        public SfxCueLibrary cues;
        public AudioTuning audio = new();

        [Header("Particles")]
        public GameFxLibrary effects;

        [Header("Floating text")]
        public FloatingTextTuning floatingText = new();

        [Header("Screen")]
        [Tooltip("Overlay canvas prefab (flash, vignettes, speed lines, countdown), created on first use.")]
        public ScreenFxOverlay screenOverlay;

        [Header("Camera shake")]
        public CameraShakeTuning cameraShake = new();

        [Header("Camera FOV")]
        public CameraFovTuning cameraFov = new();

        [Header("Shared explosions")]
        public ExplosionTuning explosions = new();

        [Header("Moods")]
        public MoodTuning moods = new();

        [Header("Characters")]
        public CharacterFeelTuning characters = new();

        public VolumeProfile Mood(MoodPreset preset) => preset switch {
            MoodPreset.ArcadeBright => moods.arcadeBright,
            MoodPreset.WarmIndoor => moods.warmIndoor,
            MoodPreset.Night => moods.night,
            MoodPreset.Horror => moods.horror,
            _ => null,
        };

        static GameFeelSettings active;
        static bool missingLogged;

        /// <summary>The settings asset, or null (logged once) when it has not been generated.</summary>
        public static GameFeelSettings Active {
            get {
                if (active == null) {
                    active = Resources.Load<GameFeelSettings>(ResourcePath);
                    if (active == null && !missingLogged) {
                        missingLogged = true;
                        Debug.LogError("GameFeel: Resources/GameFeelSettings is missing. Run Ryan > Game Feel > Rebuild.");
                    }
                }
                return active;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() {
            active = null;
            missingLogged = false;
        }
    }
}
