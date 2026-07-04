using System;
using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;

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
        public void On_ActivateTool() {
            if (!EventSystem.current.IsPointerOverGameObject())
                activateToolPressed?.Invoke();
        }
    }
}