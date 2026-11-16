using RyanAssets.GameFeel;
using UnityEngine;

namespace Universes.UniverseData.classic_horror.Client {
    /// <summary>
    /// Presents the server's ambient scares (<see cref="CH_AmbientScareBroadcast"/>): a door slamming
    /// somewhere close, a cry from far across the water, every lamp failing at once, the chapel bell.
    /// </summary>
    public sealed class CH_AmbientScares : MonoBehaviour {
        public SfxSound doorSlam = new();
        public SfxSound distantCry = new();
        public SfxSound lightsOut = new();
        public SfxSound chapelBell = new();
        [Tooltip("A door slam this close (metres) jolts the camera.")]
        [SerializeField, Min(0f)] float doorShakeRadius = 22f;
        [SerializeField, Range(0f, 1f)] float doorShake = 0.3f;
        [SerializeField, Min(0f)] float doorShakeSeconds = 0.35f;

        CH_Atmosphere atmosphere;

        void Awake() => atmosphere = GetComponent<CH_Atmosphere>();

        public void Play(CH_AmbientScareBroadcast scare) {
            switch (scare.kind) {
                case CH_AmbientScare.DoorSlam:
                    GameSfx.PlayAt(doorSlam, scare.position);
                    CameraShake.Explosion(scare.position, doorShakeRadius, doorShake, doorShakeSeconds);
                    break;
                case CH_AmbientScare.DistantCry:
                    GameSfx.PlayAt(distantCry, scare.position);
                    break;
                case CH_AmbientScare.LightsOut:
                    GameSfx.Play(lightsOut);
                    atmosphere?.LightsOut(scare.seconds);
                    break;
                case CH_AmbientScare.ChapelBell:
                    GameSfx.PlayAt(chapelBell, scare.position);
                    break;
            }
        }
    }
}
