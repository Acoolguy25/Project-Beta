using UnityEngine;
using FishNet.Object;
using RyanAssets.Core;
using FishNet;
using FishNet.Transporting;

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
            Character = NewCharacter;
            OnCharacterAdded.Invoke(NewCharacter);
            // if (CharacterControl)
            CharacterControl.gameObject.SetActive(NewCharacter != null);
        }
        public void OnClientConnectionState(ClientConnectionStateArgs clientConnection) {
            if (clientConnection.ConnectionState == LocalConnectionState.Stopped)
                SetCharacter(null);
        }
    }
}