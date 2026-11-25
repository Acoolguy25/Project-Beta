using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace RyanAssets.Input {
    public class CharacterControls: MonoBehaviour {
        [Header("Character Input Values")]
        public Vector2 move;
        public Vector2 look;
        public bool jump;
        public bool sprint;
        /// <summary>Held to crouch: slides while running, rolls on landing, lets go of ledges.</summary>
        public bool crouch;
        PlayerInput playerInput;
        InputActionAsset zoomActionsAsset;
        InputAction zoomInAction;
        InputAction zoomOutAction;
        InputAction routeModifierAction;
        readonly List<RaycastResult> raycastResults = new();
        public bool LookIsRate => playerInput != null && playerInput.actions["Character/Look"].activeControl?.device is Gamepad;
        public bool GameplayInputActive => playerInput != null && playerInput.inputIsActive &&
            playerInput.actions.FindActionMap("Character", false)?.enabled == true;
        public bool RouteModifier {
            get {
                RefreshActions();
                return GameplayInputActive && routeModifierAction?.IsPressed() == true;
            }
        }

        void RefreshActions() {
            if (playerInput == null || zoomActionsAsset == playerInput.actions)
                return;
            zoomActionsAsset = playerInput.actions;
            zoomInAction = zoomActionsAsset?.FindAction("Character/ZoomIn", false);
            zoomOutAction = zoomActionsAsset?.FindAction("Character/ZoomOut", false);
            routeModifierAction = zoomActionsAsset?.FindAction("Character/RouteModifier", false);
        }
        static int keyboardZoomLocks;
        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetZoomLocks() => keyboardZoomLocks = 0;
        /// <summary>
        /// Turns the Q / E camera zoom keys off while a game uses E for its own action (interact, throw, tag).
        /// Every call needs a matching <see cref="UnlockKeyboardZoom"/>; the scroll wheel still zooms.
        /// </summary>
        public static void LockKeyboardZoom() => keyboardZoomLocks++;
        public static void UnlockKeyboardZoom() => keyboardZoomLocks = Mathf.Max(0, keyboardZoomLocks - 1);
        public float KeyboardZoom {
            get {
                if (playerInput == null || !playerInput.inputIsActive || keyboardZoomLocks > 0)
                    return 0f;

                // PlayerInput may use a private copy of the asset. Cache its live actions.
                RefreshActions();

                return (zoomOutAction?.IsPressed() == true ? 1f : 0f)
                    - (zoomInAction?.IsPressed() == true ? 1f : 0f);
            }
        }
        void Awake() {
            playerInput = GetComponent<PlayerInput>();
        }

        [Header("Movement Settings")]
        public bool analogMovement;

        [Header("Mouse Cursor Settings")]
        public bool cursorLocked = false;
        public bool cursorInputForLook = true;

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
        public void OnCrouch(InputValue value) {
            CrouchInput(value.isPressed);
        }
        public bool IsPointerOverScrollRect() {
            if (EventSystem.current == null || Mouse.current == null)
                return false;

            PointerEventData eventData = new(EventSystem.current) {
                position = Mouse.current.position.ReadValue()
            };
            raycastResults.Clear();
            EventSystem.current.RaycastAll(eventData, raycastResults);

            foreach (RaycastResult result in raycastResults) {
                if (result.gameObject.GetComponentInParent<ScrollRect>() != null)
                    return true;
            }

            return false;
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
        public void CrouchInput(bool newCrouchState) {
            crouch = newCrouchState;
        }
    }
}
