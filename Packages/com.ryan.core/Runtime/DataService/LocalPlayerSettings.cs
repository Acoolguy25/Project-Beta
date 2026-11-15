using FishNet.Connection;
using FishNet.Object;
using System.Collections.Generic;

namespace RyanAssets.DataService {
    [System.Serializable]
    public struct LocalPlayerSettings {
        public int BidirectionalZoomSensitivity;
        public int VerticalZoomSensitivity;
        public int HorizontalZoomSensitivity;
        public bool InvertedControls;
    };
}