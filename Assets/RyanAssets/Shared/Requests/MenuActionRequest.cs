using FishNet.Broadcast;
using UnityEngine;

namespace RyanAssets.Shared.Requests
{
    public enum MenuActionType: byte {
        ResetCharacter
    };
    public struct MenuActionRequest : IBroadcast {
        // add optional arguments here
        public MenuActionType type;
    };
}
