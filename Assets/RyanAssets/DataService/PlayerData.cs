namespace RyanAssets.DataService {
    [System.Serializable]
    public struct PlayerSettings {
        public int BidirectionalZoomSensitivity;
        public int VerticalZoomSensitivity;
        public int HorizontalZoomSensitivity;
        public bool InvertedControls;
    };
    [System.Serializable]
    public struct PlayerData {
        public string username;
        public ulong xp;
        public ulong gold;
    };
}