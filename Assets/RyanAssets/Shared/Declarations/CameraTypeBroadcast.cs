using FishNet.Broadcast;
using FishNet.Connection;

namespace RyanAssets.Shared.Declarations {
    public enum GameCameraType: ushort {
        SpectateCamera = 0,
        OrbitCamera = 1,
        ThirdPersonCamera = 2,
        DeathCamera = 3,
        CutsceneCamera = 4
    };
    public struct CameraTypeBroadcast : IBroadcast {
        public GameCameraType cameraType;
        public bool enabled;
    }
}