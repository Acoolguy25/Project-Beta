using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace RyanAssets.Input {
    public class ToolControls : MonoBehaviour {
        public static event Action<int> toolBarHotkeyPressed;
        public static event Action<Vector3> activateToolPressed;
        public static event Action reloadToolPressed;
        public static event Action interactPressed, primaryPressed, journalPressed;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Init() {
            toolBarHotkeyPressed = null;
            activateToolPressed = null;
            reloadToolPressed = null;
            interactPressed = primaryPressed = journalPressed = null;
        }
        // Utility actions live in the existing Tool action map and inherit the
        // normal prompt/menu/chat input policy.
        public void OnInteract() => interactPressed?.Invoke();
        public void OnJournal() => journalPressed?.Invoke();
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
        public void OnReloadTool() {
            reloadToolPressed?.Invoke();
        }
        bool IsCursorFree() {
            if (Cursor.lockState == CursorLockMode.Locked || EventSystem.current == null)
                return true;
            if (Mouse.current == null) return false;
            PointerEventData pointerData = new(EventSystem.current) {
                position = Mouse.current.position.ReadValue()
            };
            List<RaycastResult> results = new();
            EventSystem.current.RaycastAll(pointerData, results);

            foreach (var result in results) {
                // Skip fully transparent graphics — invisible overlay panels, faded-out UI, etc.
                if (result.gameObject.TryGetComponent<Graphic>(out var graphic) && graphic.color.a <= 0.3f)
                    continue;

                return false; // hit something visible — cursor is blocked
            }

            return true; // nothing visible under the cursor
        }
        public static bool TryGetCursorWorldPosition(out Vector3 worldPosition, int layerMask = Physics.DefaultRaycastLayers) {
            Mouse mouse = Mouse.current;
            Camera camera = Camera.main;
            if (camera != null && (mouse != null || Cursor.lockState == CursorLockMode.Locked)) {
                Ray ray = GetAimRay(camera);
                int cursorMask = layerMask & ~LayerMask.GetMask("LocalCharacter", "Ignore Raycast");
                if (Physics.Raycast(ray, out RaycastHit hit, float.MaxValue, cursorMask, QueryTriggerInteraction.Ignore)) {
                    worldPosition = hit.point;
                    return true;
                }
            }

            worldPosition = default;
            return false;
        }
        public static Vector3 GetCursorWorldPosition() {
            TryGetCursorWorldPosition(out Vector3 worldPosition);
            return worldPosition;
        }
        public static Ray GetAimRay(Camera camera) {
            return Cursor.lockState == CursorLockMode.Locked || Mouse.current == null
                ? camera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0))
                : camera.ScreenPointToRay(Mouse.current.position.ReadValue());
        }
        public void OnActivateTool() {
            if (!IsCursorFree()) return;
            primaryPressed?.Invoke();
            if (TryGetCursorWorldPosition(out Vector3 worldPosition))
                activateToolPressed?.Invoke(worldPosition);
        }
    }
}
