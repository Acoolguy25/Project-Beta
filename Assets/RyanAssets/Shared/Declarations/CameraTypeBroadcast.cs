using FishNet.Broadcast;
using FishNet.Connection;

namespace RyanAssets.Shared.Declarations {
    public enum GameCameraType: ushort {
        OrbitCamera = 0,
        ThirdPersonCamera = 1,
        DeathCamera = 2,
        CutsceneCamera = 3,
        LockedSpectateCamera = 4
    };
    public struct CameraTypeBroadcast : IBroadcast {
        public GameCameraType cameraType;
        public bool enabled;
    }
}