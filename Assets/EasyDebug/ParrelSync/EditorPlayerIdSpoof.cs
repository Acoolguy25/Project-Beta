#if UNITY_EDITOR && !UNITY_SERVER
    using System.Collections;
    using UnityEditor;
    using ParrelSync;
    using UnityEngine;
    using RyanAssets.NetworkService;

    namespace Assets.EasyDebug.ParrelSync {
        [InitializeOnLoad]
        public static class EditorPlayerIdSpoof {
            static EditorPlayerIdSpoof() {
                if (ClonesManager.IsClone()) 
                    BackendNetwork.FakePlayerId = ClonesManager.GetArgument();
                else
                    BackendNetwork.FakePlayerId = "client0";
            
            }
        }
}
#endif