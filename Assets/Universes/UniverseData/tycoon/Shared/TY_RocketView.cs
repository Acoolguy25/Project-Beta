using UnityEngine;

namespace Universes.UniverseData.tycoon {
    /// <summary>
    /// The Brick Rocket landmark (authored by TY_Authoring): the launch pad stays, the rocket on it can
    /// take off. Clients launch it when it is built (the last landmark), then it lands back on its pad.
    /// </summary>
    public sealed class TY_RocketView : MonoBehaviour {
        [Tooltip("The part that flies: the rocket, its origin on the pad.")]
        public Transform rocket;
        [Tooltip("Where the exhaust comes out (the rocket's tail), a child of the rocket.")]
        public Transform nozzle;
    }
}
