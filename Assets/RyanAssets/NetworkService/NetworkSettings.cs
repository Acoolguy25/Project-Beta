namespace RyanAssets.NetworkService{
    public static class NetworkSettings{
        #if UNITY_EDITOR
            public const string BackendURL = "http://127.0.0.1:8212";
        #else
            public const string BackendURL = "http://127.0.0.1:8212";
        #endif
    }
}