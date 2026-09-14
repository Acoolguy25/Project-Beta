using RyanAssets.Server.ServerFeatures;
using RyanAssets.DataService;
using RyanAssets.Shared.Declarations;

namespace Universes.UniverseData.escape.Server {
    /// <summary>
    /// Minimal server entry point for the example universe.
    /// </summary>
    public class ESC_ServerRunner : ServerRunner {
        protected override void OnPlayerAdded(PlayerData playerData) {
            base.OnPlayerAdded(playerData);
            playerData.cameraTypes.Add(GameCameraType.ThirdPersonCamera);
            playerData.gravity.Value = -12f;
            playerData.jumpHeight.Value = 5.5f;
        }
    }
}
