using FishNet.Broadcast;
using UnityEngine;
using RyanAssets.Shared.Declarations;

namespace RyanAssets.Shared.Requests
{
    public struct MenuActionRequest : IBroadcast {
        // add optional arguments here
        public MenuActionType type;
    };
}
