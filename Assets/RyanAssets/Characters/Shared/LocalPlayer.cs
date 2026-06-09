using UnityEngine;
using FishNet.Object;
using RyanAssets.Core;
using FishNet;
using FishNet.Transporting;
using RyanAssets.Input;

namespace RyanAssets.Characters.Shared {
    public class LocalPlayer : MonoBehaviour {
        public static LocalPlayer Instance { get; private set; }
        public static Transform Character;
        static bool _init;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Init() {
            _init = false;
        }
        public InstantEvent<Transform> OnCharacterAdded;
        [SerializeField] private Transform CharacterControl;
        private void Awake() {
            if (_init) {
                Destroy(gameObject);
                return;
            }
            _init = true;
            Instance = this;
            Character = null;
            OnCharacterAdded = new();
            InstanceFinder.ClientManager.OnClientConnectionState += OnClientConnectionState;
        }
        public void SetCharacter(Transform NewCharacter) {
            // InputService.SetInputScreenActive(InputScreen.Character, NewCharacter != null);
            Character = NewCharacter;
            OnCharacterAdded.Invoke(NewCharacter);
            CharacterControl.gameObject.SetActive(NewCharacter != null);
        }
        public void OnClientConnectionState(ClientConnectionStateArgs clientConnection) {
            if (clientConnection.ConnectionState == LocalConnectionState.Stopped)
                SetCharacter(null);
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