using FishNet;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.InputSystem;

namespace RyanAssets.Characters {
    public class SharedInputController : MonoBehaviour {
        public static SharedInputController Instance { get; private set; }

        [Header("Character Input Values")]
        public Vector2 move;
        public Vector2 look;
        public bool jump;
        public bool sprint;

        [Header("Movement Settings")]
        public bool analogMovement;

        [Header("Mouse Cursor Settings")]
        public bool cursorLocked = false;
        public bool cursorInputForLook = true;

        public static Action menuToggledEvent, playerListEvent, chatActivateEvent;
        private PlayerInput _inputAction;
        private Dictionary<string, int> mapLocks = new();
        private int globalLocks;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Init() {
            Instance = null;
        }
        private void Awake() {
            Assert.IsTrue(Instance == null || Instance != this, "StarterAssetInputs is valid in Awake()");
            Instance = this;
            _inputAction = GetComponent<PlayerInput>();
        }
        private void Start() {
            foreach (InputActionMap map in _inputAction.actions.actionMaps) {
                mapLocks.Add(map.name, 0);
                map.Enable();
            }
        }
        public void SetControlsEnabled(string actionName, bool enabled) {
            mapLocks[actionName] += (enabled ? -1 : 1);
            var map = _inputAction.actions.FindActionMap(actionName);
            if (mapLocks[actionName] == 0)
                map.Enable();
            else
                map.Disable();
            Assert.IsTrue(mapLocks[actionName] >= 0, $"Maplock {actionName} is negative!");
        }
        public void LockControls() {
            globalLocks += 1;
            if (globalLocks == 1)
                SetControlsEnabled("Player", false);
        }
        public void UnlockControls() {
            globalLocks -= 1;
            Assert.IsTrue(globalLocks >= 0, $"Globallock is negative {globalLocks}!");
            if (globalLocks == 0)
                SetControlsEnabled("Player", true);
        }
        public bool GetControlsEnabled(string actionName) {
            var map = _inputAction.actions.FindActionMap(actionName);
            return map.enabled;
        }
        public void OnMove(InputValue value) {
            MoveInput(value.Get<Vector2>());
        }
        public void OnLook(InputValue value) {
            if (cursorInputForLook) {
                LookInput(value.Get<Vector2>());
            }
        }
        public void OnJump(InputValue value) {
            JumpInput(value.isPressed);
        }
        public void OnSprint(InputValue value) {
            SprintInput(value.isPressed);
        }
        public void MoveInput(Vector2 newMoveDirection) {
            move = newMoveDirection;
        }
        public void LookInput(Vector2 newLookDirection) {
            look = newLookDirection;
        }
        public void JumpInput(bool newJumpState) {
            jump = newJumpState;
        }
        public void SprintInput(bool newSprintState) {
            sprint = newSprintState;
        }
        private void OnApplicationFocus(bool hasFocus) {
            SetCursorState(cursorLocked);
        }
        private void SetCursorState(bool newState) {
            Cursor.lockState = newState ? CursorLockMode.Locked : CursorLockMode.None;
        }
        public void OnToggleMenu() {
            menuToggledEvent.Invoke();
        }
        public void OnActivateChat() {
            chatActivateEvent.Invoke();
        }
        public void OnTogglePlayerList() {
            playerListEvent.Invoke();
        }
    }

}
