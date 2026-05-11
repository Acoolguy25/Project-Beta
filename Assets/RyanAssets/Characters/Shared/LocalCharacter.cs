using System;
using FishNet.Object;
using FishNet.Connection;
using UnityEngine;

namespace RyanAssets.Characters
{
    public class LocalCharacter : NetworkBehaviour
    {
        public Transform CharacterCamera;
        public override void OnOwnershipClient(NetworkConnection prevOwner){
            if (!IsOwner)
                return;
            LocalPlayer.Instance.SetCharacter(transform);
            gameObject.name = "LocalCharacter (" + gameObject.name + ")";
        }
        public override void OnStopClient()
        {
            if (!IsOwner)
                return;
            LocalPlayer.Instance?.SetCharacter(null);
        }
    }
}