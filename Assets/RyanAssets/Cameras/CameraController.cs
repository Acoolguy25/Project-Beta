using UnityEngine;
using UnityEngine.Assertions;
using RyanAssets.Characters.Shared;
using RyanAssets.Characters.Client;
using System.Linq;
using System.Collections.Generic;
using System.Collections.Specialized;
using Unity.Cinemachine;
using RyanAssets.Client.ClientCore;
using RyanAssets.Shared.Declarations;
using FishNet;
using FishNet.Transporting;
using FishNet.Object;

namespace RyanAssets.Cameras
{
    public class CameraController : MonoBehaviour
    {
        public static CameraController Instance;
        private List<Camera> CameraComponents = new();
        private List<bool> CameraActive = new();
        private int activeIndex;
        public Camera activeCamera { get; private set; }
        public void SwitchCamera(int index) {
            Camera newCamera = CameraComponents[index];
            Assert.IsNotNull(newCamera, "Active Camera cannot be null");
            Assert.IsTrue(newCamera.transform.parent == transform, "Cameras must be parented to GameObject.Cameras");
            Assert.IsTrue(!activeCamera || newCamera.tag == activeCamera.tag, "Tags between cameras must be the same");
            //newCamera.transform.position = activeCamera.transform.position;
            ICamera oldController = activeCamera.GetComponent<ICamera>();
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
                        break;
                    }
                }
            }
        }
        public void SetCameraTarget(Transform target) {
            foreach (var cinemachine in GetComponentsInChildren<CinemachineCamera>(true)) {
                cinemachine.LookAt = cinemachine.Follow = target;
            }
        }
        private void Awake() {
            Instance = this;

        }
        private void OnCharacterAdded(Transform character)
        {
            LocalCharacter localCharacter = character?.GetComponent<LocalCharacter>();
            Transform CharacterCamera = localCharacter?.CharacterCamera;
            if (character) {
                Assert.IsNotNull(CharacterCamera);
                localCharacter.OnDied += OnCharacterDied;
            }
            SetCameraTarget(CharacterCamera);
            SetCameraAvailable(GameCameraType.DeathCamera, false);
            SetCameraAvailable(GameCameraType.ThirdPersonCamera, character != null);
        }
        private void OnCharacterDied(DamageSource source, NetworkObject sourceObject) {
            SetCameraAvailable(GameCameraType.DeathCamera, true);
            SetCameraAvailable(GameCameraType.ThirdPersonCamera, false);
        }
        private void OnConnected(){
            LocalPlayer.Instance.OnCharacterAdded.Subscribe(OnCharacterAdded);
            InstanceFinder.ClientManager.RegisterBroadcast<CameraTypeBroadcast>(SetCameraAvailable_RPC);
        }
        private void OnDisconnected(){
            LocalPlayer.Instance?.OnCharacterAdded.Unsubscribe(OnCharacterAdded);
            InstanceFinder.ClientManager.UnregisterBroadcast<CameraTypeBroadcast>(SetCameraAvailable_RPC);
        }
        private void SetCameraAvailable_RPC(CameraTypeBroadcast broadcast, Channel channel = Channel.Reliable) {
            SetCameraAvailable(broadcast.cameraType, broadcast.enabled);
        }
        private void Start() {
            ClientConnector.OnConnected += OnConnected;
            ClientConnector.OnDisconnected += OnDisconnected;
            activeCamera = null;
            activeIndex = -1;
            for (int i = 0; i < transform.childCount; i++){
                Camera cam = transform.GetChild(i).GetComponent<Camera>();
                bool cam_active = cam.gameObject.activeSelf;
                if (cam_active) {
                    activeIndex = i;
                    cam.gameObject.SetActive(false);
                    activeCamera = cam;
                }
                Assert.IsNotNull(cam);
                CameraComponents.Add(cam);
                CameraActive.Add(cam_active);
            }
            SwitchCamera(activeIndex);
        }
        private void OnDestroy() {
            Instance = null;
        }
    }
}