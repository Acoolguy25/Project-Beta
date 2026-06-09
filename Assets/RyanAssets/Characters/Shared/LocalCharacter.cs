using System;
using FishNet.Object;
using FishNet.Connection;
using UnityEngine;

namespace RyanAssets.Characters.Shared
{
    public class LocalCharacter : NetworkBehaviour
    {
#if !UNITY_SERVER
        [SerializeField]
        public Transform CharacterCamera;
        public static event Action<(Transform, bool)> AnyCharacterAdded;
        public static event Action<(Transform, bool)> AnyCharacterRemoved;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public void Init(){
            AnyCharacterAdded = null;
            AnyCharacterRemoved = null;
        }
        public override void OnOwnershipClient(NetworkConnection prevOwner){
            AnyCharacterAdded.Invoke((transform, IsOwner));
            if (!IsOwner)
                gameObject.name = $"{base.Owner}";
            else
                gameObject.name = "LocalCharacter (" + gameObject.name + ")";
        }
        void OnDestroy()
        {
            AnyCharacterRemoved.Invoke((transform, IsOwner));
        }
#endif
    }
}