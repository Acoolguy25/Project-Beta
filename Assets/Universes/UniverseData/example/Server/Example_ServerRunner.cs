using RyanAssets.Server.ServerFeatures;
using RyanAssets.DataService;
using RyanAssets.Shared.Declarations;

namespace Universes.UniverseData.example.Server {
    /// <summary>
    /// Minimal server entry point for the example universe.
    /// </summary>
    public sealed class Example_ServerRunner : ServerRunner {
        protected override void OnPlayerAdded(PlayerData playerData) {
            base.OnPlayerAdded(playerData);
            playerData.cameraTypes.Add(GameCameraType.ThirdPersonCamera);
        }
    }
}
