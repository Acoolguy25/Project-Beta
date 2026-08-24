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
        public ICamera activeCamera { get; private set; }
        public static GameCharacter targetCharacter { get; private set; }

        public static event Action<GameCharacter> OnCameraTargetAdded, OnCameraTargetRemoved;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Init() {
            OnCameraTargetAdded = OnCameraTargetRemoved = null;
            targetCharacter = null;
        }
        public void SwitchCamera(int index) {
            if (!this)
                return;
            ICamera newCamera = CameraComponents[index];
            Assert.IsNotNull(newCamera, "Active Camera cannot be null");
            Assert.IsTrue(newCamera.transform.parent == transform, "Cameras must be parented to GameObject.Cameras");
            //newCamera.transform.position = activeCamera.transform.position;
            ICamera oldController = activeCamera;
            ICamera newController = newCamera.GetComponent<ICamera>();
            oldController?.DisableCamera(newCamera.transform, (GameCameraType) index);
            newController?.EnableCamera(activeCamera?.transform, (GameCameraType) activeIndex);
            activeCamera.gameObject.SetActive(false);
            newCamera.gameObject.SetActive(true);
            activeCamera = newCamera;
            activeIndex = index;
        }
        public void SetCameraAvailable(GameCameraType camType, bool active) {
            int index = (int) camType;
            CameraActive[index] = active;
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
        private void OnCharacterAdded(LocalCharacter localCharacter)
        {
            localCharacter.OnDied += OnCharacterDied;
            SetCameraAvailable(GameCameraType.SpectateCamera, false);
            SetCameraAvailable(GameCameraType.DeathCamera, false);
            SetCameraAvailable(GameCameraType.ThirdPersonCamera, true);
            SetCameraTarget(localCharacter);
        }
        private void OnCharacterDied(RyanAssets.Shared.Declarations.DamageType source, IEntity sourceEntity) {
            SetCameraAvailable(GameCameraType.DeathCamera, true);
            SetCameraAvailable(GameCameraType.ThirdPersonCamera, false);
        }
        private void OnCharacterRemoved(LocalCharacter localCharacter) {
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
            OnCharacterRemoved(null);
            LocalPlayer.OnCharacterAdded.Subscribe(OnCharacterAdded);
            LocalPlayer.OnCharacterRemoved += OnCharacterRemoved;
            playerData.cameraTypes.OnChange += OnCameraTypeSyncChanged;

            OnCameraTypeSyncChanged(SyncHashSetOperation.Set, GameCameraType.SpectateCamera, false);
        }
        void OnMyPlayerRemoved(PlayerData playerData) {
            LocalPlayer.OnCharacterAdded.Unsubscribe(OnCharacterAdded);
            LocalPlayer.OnCharacterRemoved -= OnCharacterRemoved;
            playerData.cameraTypes.OnChange -= OnCameraTypeSyncChanged;

            SetCameraAvailable(GameCameraType.SpectateCamera, false);
            SetCameraAvailable(GameCameraType.ThirdPersonCamera, false);
            SetCameraTarget(null);
        }
        private void Start() {
            activeCamera = null;
            activeIndex = -1;
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
            if (LocalPlayer.Character)
                LocalPlayer.Character.OnDied -= OnCharacterDied;
            if (PlayerData.localData)
                OnMyPlayerRemoved(PlayerData.localData);
        }
    }
}
