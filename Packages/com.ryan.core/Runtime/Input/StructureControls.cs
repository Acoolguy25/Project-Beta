using System;
using System.Collections;
using UnityEngine;

namespace RyanAssets.Input {
    public class StructureControls : MonoBehaviour {
        public static Action rotateEvent, onToggleStructureMenuEvent;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Init() {
            rotateEvent = null;
            onToggleStructureMenuEvent = null;
        }
        public void OnRotate() {
            rotateEvent?.Invoke();
        }
        public void OnToggleStructureMenu() {
            onToggleStructureMenuEvent?.Invoke();
        }
    }
}