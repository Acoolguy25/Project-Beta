namespace RyanAssets.NetworkService {
    public static class NetworkSettings {
        public static readonly string DEPLOY_SERVER_IP = "5.78.211.52";
        public static readonly ushort BackendAPIPort = 8212;
        #if (LOCAL_BACKEND && UNITY_EDITOR) || SERVER_BUILD
            public static readonly string YOUR_SERVER_IP = "127.0.0.1";
        #else
            public static readonly string YOUR_SERVER_IP = DEPLOY_SERVER_IP;
        #endif
        public static readonly string BackendAPIURL = "http://" + YOUR_SERVER_IP + ":" + BackendAPIPort;

        public static ushort IdleShutdownSeconds = 60;
    }
}