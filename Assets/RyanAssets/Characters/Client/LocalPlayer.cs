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
        public static LocalCharacter Character;
        [NonSerialized]
        public static InstantEvent<LocalCharacter> OnCharacterAdded;
        public static Action<LocalCharacter> OnCharacterRemoved;
        [SerializeField] private Transform CharacterControl;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Init() {
            Instance = null;
            Character = null;
            OnCharacterAdded = new();
            OnCharacterRemoved = null;
        }
        private void Awake() {
            Instance = this;
            Character = null;
            LocalCharacter.LocalCharacterAdded += OnAnyCharacterAdded;
            LocalCharacter.LocalCharacterRemoved += OnAnyCharacterRemoved;
            InstanceFinder.ClientManager.OnClientConnectionState += OnClientConnectionState;
        }
        public void OnAnyCharacterAdded(LocalCharacter character){
            if (character.IsOwner)
                SetCharacter(character);
        }
        public void OnAnyCharacterRemoved(LocalCharacter character){
            if (character == Character)
                SetCharacter(null);
        }
        public void SetCharacter(LocalCharacter NewCharacter) {
            // InputService.SetInputScreenActive(I3nputScreen.Character, NewCharacter != null);
            if (NewCharacter == null && Character != null) {
                OnCharacterRemoved?.Invoke(Character);
            }
            Character = NewCharacter;
            if (NewCharacter != null) {
                OnCharacterAdded.Invoke(Character);
            }
            CharacterControl.gameObject.SetActive(NewCharacter != null);
        }
        public void OnClientConnectionState(ClientConnectionStateArgs clientConnection) {
            if (clientConnection.ConnectionState == LocalConnectionState.Stopped)
                SetCharacter(null);
        }
        private void OnDestroy() {
            if (InstanceFinder.ClientManager != null)
                InstanceFinder.ClientManager.OnClientConnectionState -= OnClientConnectionState;
            LocalCharacter.LocalCharacterAdded -= OnAnyCharacterAdded;
            LocalCharacter.LocalCharacterRemoved -= OnAnyCharacterRemoved;
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
