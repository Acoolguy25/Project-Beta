using System;
using System.Collections;
using UnityEngine;

namespace RyanAssets.Input {
    public class TextboxControls : MonoBehaviour {
        public static Action upEvent, downEvent, tabEvent;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Init() {
            upEvent = null;
            downEvent = null;
            tabEvent = null;
        }
        public void OnTab() {
            tabEvent?.Invoke();
        }
        public void OnUp() {
            upEvent?.Invoke();
        }
        public void OnDown() {
            downEvent?.Invoke();
        }
    }
}