using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace RyanAssets.Input {
    public class ToolControls : MonoBehaviour {
        public static Action<int> toolBarHotkeyPressed;
        public static Action activateToolPressed;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Init() {
            toolBarHotkeyPressed = null;
            activateToolPressed = null;
        }
        public void On_0() {
            toolBarHotkeyPressed?.Invoke(0);
        }
        public void On_1() {
            toolBarHotkeyPressed?.Invoke(1);
        }
        public void On_2() {
            toolBarHotkeyPressed?.Invoke(2);
        }
        public void On_3() {
            toolBarHotkeyPressed?.Invoke(3);
        }
        public void On_4() {
            toolBarHotkeyPressed?.Invoke(4);
        }
        public void On_5() {
            toolBarHotkeyPressed?.Invoke(5);
        }
        public void On_6() {
            toolBarHotkeyPressed?.Invoke(6);
        }
        public void On_7() {
            toolBarHotkeyPressed?.Invoke(7);
        }
        public void On_8() {
            toolBarHotkeyPressed?.Invoke(8);
        }
        public void On_9() {
            toolBarHotkeyPressed?.Invoke(9);
        }
        bool IsCursorFree() { 
            PointerEventData pointerData = new(EventSystem.current) {
                position = Mouse.current.position.ReadValue()
            };

            List<RaycastResult> results = new();
            EventSystem.current.RaycastAll(pointerData, results);

            bool overUI = results.Count == 0;
            return overUI;
        }
        public void OnActivateTool() {
            if (IsCursorFree())
                activateToolPressed?.Invoke();
        }
    }
}