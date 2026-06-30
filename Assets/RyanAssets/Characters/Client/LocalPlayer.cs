using UnityEngine;
using RyanAssets.Core;
using FishNet;
using FishNet.Transporting;
using RyanAssets.Input;
using RyanAssets.Characters.Shared;
using System;

namespace RyanAssets.Characters.Client {
    public class LocalPlayer : MonoBehaviour {
        public static LocalPlayer Instance { get; private set; }
        public static Transform Character;
        [NonSerialized]
        public InstantEvent<Transform> OnCharacterAdded;
        [SerializeField] private Transform CharacterControl;
        private void Awake() {
            Instance = this;
            Character = null;
            OnCharacterAdded = new();
            LocalCharacter.AnyCharacterAdded += OnAnyCharacterAdded;
            LocalCharacter.AnyCharacterRemoved += OnAnyCharacterRemoved;
            InstanceFinder.ClientManager.OnClientConnectionState += OnClientConnectionState;
        }
        public void OnAnyCharacterAdded((Transform obj, bool IsOwner) NewCharacter){
            if (NewCharacter.IsOwner)
                SetCharacter(NewCharacter.obj);
        }
        public void OnAnyCharacterRemoved((Transform obj, bool IsOwner) NewCharacter){
            if (NewCharacter.IsOwner && NewCharacter.obj == Character)
                SetCharacter(null);
        }
        public void SetCharacter(Transform NewCharacter) {
            // InputService.SetInputScreenActive(I3nputScreen.Character, NewCharacter != null);
            Character = NewCharacter;
            OnCharacterAdded.Invoke(NewCharacter);
            CharacterControl.gameObject.SetActive(NewCharacter != null);
        }
        public void OnClientConnectionState(ClientConnectionStateArgs clientConnection) {
            if (clientConnection.ConnectionState == LocalConnectionState.Stopped)
                SetCharacter(null);
        }
        private void OnDestroy() {
            if (InstanceFinder.ClientManager != null)
                InstanceFinder.ClientManager.OnClientConnectionState -= OnClientConnectionState;
        }
        void OnEnable(){
            InputService.SetInputScreenActive(InputScreen.Client, true);
            InputService.SetInputScreenActive(InputScreen.GameMenu, false);
            InputService.SetInputScreenActive(InputScreen.GameSettings, false);
        }
        void OnDisable(){
            InputService.SetInputScreenActive(InputScreen.Client, false);
            InputService.SetInputScreenActive(InputScreen.GameMenu, true);
            InputService.SetInputScreenActive(InputScreen.GameSettings, false);
        }
    }
}
