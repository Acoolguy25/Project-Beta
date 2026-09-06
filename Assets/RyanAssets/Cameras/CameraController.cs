using FishNet;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using FishNet.Transporting;
using RyanAssets.Characters.Client;
using RyanAssets.Characters.Shared;
using RyanAssets.Client.ClientCore;
using RyanAssets.DataService;
using RyanAssets.Shared.Declarations;
using RyanAssets.Shared.Requests;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.Assertions;

namespace RyanAssets.Cameras
{
    public class CameraController : MonoBehaviour
    {
        public static CameraController Instance;
        private List<ICamera> CameraComponents = new();
        private List<bool> CameraActive = new();

        private int activeIndex;
        private int cameraLock = -1;
        public ICamera activeCamera { get; private set; }
        public static GameCharacter targetCharacter { get; private set; }
        //private AudioListener audioListener;

        public static event Action<GameCharacter> OnCameraTargetAdded, OnCameraTargetRemoved;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Init() {
            OnCameraTargetAdded = OnCameraTargetRemoved = null;
            targetCharacter = null;
        }
        public void SwitchCamera(int index) {
            if (!this || index < 0 || index >= CameraComponents.Count)
                return;
            int lockedIndex = cameraLock;
            if (lockedIndex >= 0 && lockedIndex < CameraComponents.Count && index != lockedIndex)
                index = lockedIndex;
            ICamera newCamera = CameraComponents[index];
            Assert.IsNotNull(newCamera, "Active Camera cannot be null");
            Assert.IsTrue(newCamera.transform.parent == transform, "Cameras must be parented to GameObject.Cameras");
            //newCamera.transform.position = activeCamera.transform.position;
            ICamera oldController = activeCamera;
            ICamera newController = newCamera.GetComponent<ICamera>();
            oldController?.DisableCamera(newCamera.transform, (GameCameraType) index);
            newController?.EnableCamera(activeCamera?.transform, (GameCameraType) activeIndex);
            activeCamera?.gameObject.SetActive(false);
            newCamera.gameObject.SetActive(true);
            activeCamera = newCamera;
            activeIndex = index;
            //audioListener.enabled = false;
        }
        public void SetCameraAvailable(GameCameraType camType, bool active) {
            int index = (int) camType;
            if (index < 0 || index >= CameraActive.Count)
                return;
            CameraActive[index] = active;
            int lockedIndex = cameraLock;
            if (lockedIndex >= 0 && lockedIndex < CameraComponents.Count) {
                if (activeIndex != lockedIndex || !CameraComponents[lockedIndex].gameObject.activeSelf)
                    SwitchCamera(lockedIndex);
                return;
            }
            if (active && index > activeIndex){
                SwitchCamera(index);
            }
            else if (!active && index == activeIndex){
                for (int i = index - 1; i >= 0; i--){
                    if (CameraActive[i]){
                        SwitchCamera(i);
                        return;
                    }
                }
                activeCamera?.DisableCamera(null, default);
                activeCamera?.gameObject.SetActive(false);
                //audioListener.enabled = true;
                activeIndex = -1; // if not set one, then remind it to set it active later
            }
        }
        public void SetCameraTarget(GameCharacter target) {
            if (gameObject == null)
                return; // we are being destroyed
            if (target)
                Assert.IsNotNull(target.CharacterCamera, $"Target {target.name} does not have a valid CharacterCamera");
            foreach (var cinemachine in GetComponentsInChildren<CinemachineCamera>(true)) {
                cinemachine.LookAt = cinemachine.Follow = target?.CharacterCamera;
            }
            if (targetCharacter)
                OnCameraTargetRemoved?.Invoke(targetCharacter);
            targetCharacter = target;
            if (target)
                OnCameraTargetAdded?.Invoke(target);
        }
        private void Awake() {
            Instance = this;
        }
        private void OnCharacterAdded(LocalCharacter localCharacter) {
            localCharacter.OnDied += OnCharacterDied;
            localCharacter.OnRevive += OnCharacterRevived;
            SetCameraAvailable(GameCameraType.SpectateCamera, false);
            SetCameraAvailable(GameCameraType.DeathCamera, false);
            SetCameraAvailable(GameCameraType.ThirdPersonCamera, true);
            SetCameraTarget(localCharacter);
        }
        private void OnCharacterDied(RyanAssets.Shared.Declarations.DamageType source, IEntity sourceEntity) {
            SetCameraAvailable(GameCameraType.DeathCamera, true);
            SetCameraAvailable(GameCameraType.ThirdPersonCamera, false);
        }
        private void OnCharacterRevived() {
            SetCameraAvailable(GameCameraType.SpectateCamera, false);
            SetCameraAvailable(GameCameraType.DeathCamera, false);
            SetCameraAvailable(GameCameraType.ThirdPersonCamera, true);
            if (LocalPlayer.Character)
                SetCameraTarget(LocalPlayer.Character);
        }
        private void OnCharacterRemoved(LocalCharacter localCharacter) {
            if (localCharacter != null) {
                localCharacter.OnDied -= OnCharacterDied;
                localCharacter.OnRevive -= OnCharacterRevived;
            }
            SetCameraAvailable(GameCameraType.ThirdPersonCamera, false);
            SetCameraAvailable(GameCameraType.DeathCamera, false);
            SetCameraAvailable(GameCameraType.SpectateCamera, true);
            if (targetCharacter == localCharacter)
                SetCameraTarget(null);
        }
        //private void SetCameraAvailable_RPC(CameraTypeBroadcast broadcast, Channel channel = Channel.Reliable) {
        //    SetCameraAvailable(broadcast.cameraType, broadcast.enabled);
        //}
        private void OnCameraTypeSyncChanged(SyncHashSetOperation op, GameCameraType item, bool asServer) {
            if (op == SyncHashSetOperation.Clear) {
                foreach (GameCameraType camType in Enum.GetValues(typeof(GameCameraType))) {
                    SetCameraAvailable(camType, false);
                }
            } else if (op == SyncHashSetOperation.Remove) {
                SetCameraAvailable(item, false);
            } else if (op == SyncHashSetOperation.Add) {
                SetCameraAvailable(item, true);
            } else if (op == SyncHashSetOperation.Set) {
                OnCameraTypeSyncChanged(SyncHashSetOperation.Clear, default, false);
                foreach (GameCameraType camType in PlayerData.localData.cameraTypes) {
                    SetCameraAvailable(camType, true);
                }
            }
        }
        void OnMyPlayerAdded(PlayerData playerData) {
            cameraLock = playerData.lockedCameraType.Value;
            OnCharacterRemoved(null);
            LocalPlayer.OnCharacterAdded.Subscribe(OnCharacterAdded);
            LocalPlayer.OnCharacterRemoved += OnCharacterRemoved;
            playerData.cameraTypes.OnChange += OnCameraTypeSyncChanged;
            playerData.lockedCameraType.OnChange += OnCameraLockChanged;

            OnCameraTypeSyncChanged(SyncHashSetOperation.Set, GameCameraType.SpectateCamera, false);
        }
        void OnMyPlayerRemoved(PlayerData playerData) {
            cameraLock = -1;
            LocalPlayer.OnCharacterAdded.Unsubscribe(OnCharacterAdded);
            LocalPlayer.OnCharacterRemoved -= OnCharacterRemoved;
            playerData.cameraTypes.OnChange -= OnCameraTypeSyncChanged;
            playerData.lockedCameraType.OnChange -= OnCameraLockChanged;

            OnCameraTypeSyncChanged(SyncHashSetOperation.Clear, default, false);
            SetCameraTarget(null);
        }
        void OnCameraLockChanged(int previous, int next, bool asServer) {
            cameraLock = next;
            if (next >= 0 && next < CameraComponents.Count) {
                SwitchCamera(next);
                return;
            }
            for (int i = CameraActive.Count - 1; i >= 0; i--) {
                if (CameraActive[i]) {
                    SwitchCamera(i);
                    return;
                }
            }
        }
        private void Start() {
            //audioListener = GetComponent<AudioListener>();
            activeCamera = null;
            activeIndex = -1;
            // Existing camera rigs acquire the reusable mode without requiring every
            // universe's client scene to duplicate a first-person camera prefab.
            if (transform.Find(nameof(GameCameraType.FirstPersonCamera)) == null) {
                var firstPerson = new GameObject(nameof(GameCameraType.FirstPersonCamera));
                firstPerson.SetActive(false);
                firstPerson.transform.SetParent(transform, false);
                firstPerson.AddComponent<FirstPersonController>();
            }
            for (int i = 0; i < transform.childCount; i++){
                ICamera cam = transform.GetChild(i).GetComponent<ICamera>();
                bool cam_active = cam.gameObject.activeSelf;
                if (cam_active) {
                    activeIndex = i;
                    cam.gameObject.SetActive(false);
                    activeCamera = cam;
                }
                Assert.IsNotNull(cam);
                CameraComponents.Add(cam);
                CameraActive.Add(cam_active);
                Assert.AreEqual(((int)Enum.Parse<GameCameraType>(cam.transform.name)), i, $"{cam.transform.name} does not match its enum!");
            }
            SwitchCamera(activeIndex);
            PlayerData.OnMyPlayerAdded.Subscribe(OnMyPlayerAdded);
            PlayerData.OnMyPlayerRemoved += OnMyPlayerRemoved;
            //ClientConnector.OnConnected += OnConnected;
            //ClientConnector.OnDisconnected += OnDisconnected;
        }
        private void OnDestroy() {
            Instance = null;
            PlayerData.OnMyPlayerAdded.Unsubscribe(OnMyPlayerAdded);
            PlayerData.OnMyPlayerRemoved -= OnMyPlayerRemoved;
            //ClientConnector.OnConnected -= OnConnected;
            //ClientConnector.OnDisconnected -= OnDisconnected;
            if (LocalPlayer.Character) {
                LocalPlayer.Character.OnDied -= OnCharacterDied;
                LocalPlayer.Character.OnRevive -= OnCharacterRevived;
            }
            if (PlayerData.localData)
                OnMyPlayerRemoved(PlayerData.localData);
        }
    }
}
