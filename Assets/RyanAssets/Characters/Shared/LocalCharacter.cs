using System;
using FishNet.Object;
using FishNet.Connection;
using UnityEngine;

namespace RyanAssets.Characters.Shared
{
    public class LocalCharacter : NetworkBehaviour
    {
        // [SerializeField]
        public Transform CharacterCamera;
#if !UNITY_SERVER
        public static event Action<(Transform, bool)> AnyCharacterAdded;
        public static event Action<(Transform, bool)> AnyCharacterRemoved;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Init(){
            AnyCharacterAdded = null;
            AnyCharacterRemoved = null;
        }
        public override void OnOwnershipClient(NetworkConnection prevOwner){
            AnyCharacterAdded?.Invoke((transform, IsOwner));
            if (!IsOwner)
                gameObject.name = $"{base.Owner}";
            else
                gameObject.name = "LocalCharacter (" + gameObject.name + ")";
        }
        void OnDestroy()
        {
            AnyCharacterRemoved?.Invoke((transform, IsOwner));
        }
        void Awake(){
            CharacterCamera = transform.Find("CharacterCamera");
        }
#endif
    }
}
