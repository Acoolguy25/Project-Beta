using UnityEngine;
using FishNet.Object;
using RyanAssets.Core;
using FishNet;
using FishNet.Transporting;
using RyanAssets.Input;

namespace RyanAssets.Characters {
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
            if (Character == null && NewCharacter != null)
                InputService.UnlockControls(InputControl.Character);
            else if (Character != null && NewCharacter == null)
                InputService.LockControls(InputControl.Character);
            Character = NewCharacter;
            OnCharacterAdded.Invoke(NewCharacter);
            CharacterControl.gameObject.SetActive(NewCharacter != null);
        }
        public void OnClientConnectionState(ClientConnectionStateArgs clientConnection) {
            if (clientConnection.ConnectionState == LocalConnectionState.Stopped)
                SetCharacter(null);
        }
        void OnEnable(){
            InputService.UnlockControls(InputControl.Client);
            InputService.UnlockControls(InputControl.GameSettings);
        }
        void OnDisable(){
            InputService.LockControls(InputControl.Client);
            InputService.LockControls(InputControl.GameSettings);
        }
    }
}