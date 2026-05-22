namespace RyanAssets.DataService {
    [System.Serializable]
    public struct PlayerSettings {
        public float ZoomSensitivity;
        public bool InvertedControls;
    };
    [System.Serializable]
    public struct PlayerData {
        public string username;
        public ulong xp;
        public ulong gold;
    };
}