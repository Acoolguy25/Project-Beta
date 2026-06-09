using UnityEngine;
using UnityEngine.Assertions;
using RyanAssets.Characters.Shared;
using RyanAssets.Characters.Client;
using System.Linq;
using System.Collections.Generic;
using System.Collections.Specialized;
using Unity.Cinemachine;
using RyanAssets.Client.ClientCore;

namespace RyanAssets.Cameras
{
    public enum CameraType {
        OrbitCamera = 0,
        ThirdPersonCamera = 1,
        DeathCamera = 2,
        CutsceneCamera = 3
    };
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
            newCamera.transform.position = activeCamera.transform.position;
            activeCamera.gameObject.SetActive(false);
            newCamera.gameObject.SetActive(true);
            activeCamera = newCamera;
            activeIndex = index;
        }
        public void SetCameraAvailable(CameraType camType, bool active) {
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
            if (character)
                Assert.IsNotNull(CharacterCamera);
            SetCameraTarget(CharacterCamera);
            SetCameraAvailable(CameraType.ThirdPersonCamera, character != null);
        }
        private void OnConnected(){
            LocalPlayer.Instance.OnCharacterAdded.Subscribe(OnCharacterAdded);
        }
        private void OnDisconnected(){
            LocalPlayer.Instance.OnCharacterAdded.Unsubscribe(OnCharacterAdded);
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