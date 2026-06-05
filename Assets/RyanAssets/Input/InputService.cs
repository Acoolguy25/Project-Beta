using FishNet;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.InputSystem;

namespace RyanAssets.Input {
    public enum InputControl {
        Character,
        Client,
        GameSettings,
        Prompt,
        None,
    }
    public class InputService : MonoBehaviour {
        public static InputService Instance { get; private set; }

        private PlayerInput _inputAction;
        private static Dictionary<InputControl, int> mapLocks = new();
        public static MenuControls menuControls;
        public static CharacterControls characterControls;
        public static PromptControls promptControls;
        private int globalLocks;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Init() {
            Instance = null;
        }
        private void Awake() {
            if (Instance != null)
                return;
            Instance = this;
            _inputAction = GetComponent<PlayerInput>();
            characterControls = GetComponent<CharacterControls>();
            menuControls = GetComponent<MenuControls>();
            promptControls = GetComponent<PromptControls>();
            mapLocks.Clear();
            Reset();
        }
        private void OnDestroy() {
            if (Instance == this)
                Instance = null;
        }
        public static void ResetAction(InputControl action){
            mapLocks[action] = 0;
            Instance.SetControlsEnabled(action, false);
        }
        private void Reset() {
            foreach (InputActionMap map in _inputAction.actions.actionMaps) {
                bool valid = Enum.TryParse<InputControl>(map.name, out InputControl action);
                Debug.Assert(valid, $"Action {map.name} does not have a valid enum!");
                ResetAction(action);
            }
        }
        public void SetControlsEnabled(InputControl actionName, bool enabled, int amount = 1) {
            if (actionName == InputControl.None)
                return;
            mapLocks[actionName] += (enabled ? -amount : amount);
            var map = _inputAction.actions.FindActionMap(actionName.ToString());
            if (mapLocks[actionName] == 0)
                map.Enable();
            else
                map.Disable();
            Assert.IsTrue(mapLocks[actionName] >= 0, $"Maplock {actionName} is negative!");
        }
        public void SetFocusControls(InputControl exclude, bool focused){
            foreach (InputControl action in Enum.GetValues(typeof(InputControl))){
                if (action != exclude && (int) action < (int) exclude)
                    SetControlsEnabled(action, !focused);
            }
        }
        public static void LockControls(InputControl action) {
            Instance.SetControlsEnabled(action, false);
        }
        public static void UnlockControls(InputControl action) {
            Instance.SetControlsEnabled(action, true);
        }
        public static void FocusControls(InputControl action){
            Instance.SetFocusControls(action, true);
        }
        public static void UnfocusControls(InputControl action){
            Instance.SetFocusControls(action, false);
        }
        // public static void ResetControls(InputControl action){
        //     mapLocks.Clear();
        //     Instance.Reset();
        // }
        // private void OnApplicationFocus(bool hasFocus) {
        //     SetCursorState(cursorLocked);
        // }
        // private void SetCursorState(bool newState) {
        //     Cursor.lockState = newState ? CursorLockMode.Locked : CursorLockMode.None;
        // }
        
    }

}

