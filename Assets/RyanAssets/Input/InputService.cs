using FishNet;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.InputSystem;

namespace RyanAssets.Input {
    public enum InputScreen { // lower in the list list is assiocated with higher "priority"
        Textbox,
        Prompt,
        GameMenu,
        // Character,
        GameSettings,
        Client,
        // None,
    }
    public enum RyanAssetsActionMap {
        Prompt,
        GameSettings,
        Character,
        Client
    }
    public class InputService : MonoBehaviour {
        public static InputService Instance { get; private set; }
        private static Dictionary<InputScreen, List<RyanAssetsActionMap>> InputScreenToActiveMaps = new(){
            {InputScreen.Textbox, new(){}},
            {InputScreen.Client, new(){RyanAssetsActionMap.Client, RyanAssetsActionMap.Character}},
            {InputScreen.GameSettings, new(){RyanAssetsActionMap.GameSettings}},
            {InputScreen.GameMenu, new(){}},
            {InputScreen.Prompt, new(){RyanAssetsActionMap.Prompt}}
        };
        private static bool[] activeInputScreen;
        private PlayerInput _inputAction;
        public static CharacterControls characterControls;

        private void Awake() {
            Instance = this;
            _inputAction = GetComponent<PlayerInput>();
            activeInputScreen = new bool[Enum.GetValues(typeof(InputScreen)).Length];
            characterControls = GetComponent<CharacterControls>();
            SetInputScreenActive(InputScreen.GameMenu, true);
        }
        private void ApplyActiveScreen(InputScreen screen){
            // foreach (string map_string in InputScreenToActiveMaps[screen]){
                foreach (var map in _inputAction.actions.actionMaps) {
                    RyanAssetsActionMap ryanAssetsActionMap = Enum.Parse<RyanAssetsActionMap>(map.name);
                    if (InputScreenToActiveMaps[screen].Contains(ryanAssetsActionMap)){
                        map.Enable();
                    } else {
                        map.Disable();
                    }
                }
            // }
        }
        private void RefreshActiveScreen(){
            for (int i = activeInputScreen.Count() - 1; i >= 0; i--){
                if (activeInputScreen[i]){
                    ApplyActiveScreen((InputScreen) i);
                }
            }
        }
        public static void SetInputScreenActive(InputScreen screen, bool active){
            Debug.Assert(activeInputScreen != null, "ActiveInputScreen is not initalized!");
            Debug.Assert((int) screen < activeInputScreen.Count(), $"Input screen {screen} does not exist");
            activeInputScreen[((int)screen)] = active;
            Instance.RefreshActiveScreen();
        }

        // private void OnApplicationFocus(bool hasFocus) {
        //     SetCursorState(cursorLocked);
        // }
        // private void SetCursorState(bool newState) {
        //     Cursor.lockState = newState ? CursorLockMode.Locked : CursorLockMode.None;
        // }
        
    }

}

