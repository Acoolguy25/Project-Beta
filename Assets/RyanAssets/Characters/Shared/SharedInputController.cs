using FishNet;
using System;
using System.Runtime.CompilerServices;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.InputSystem;

namespace RyanAssets.Characters
{
	public class SharedInputController : MonoBehaviour
	{
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

		public Action menuToggledEvent;
		private PlayerInput _inputAction;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Init() {
            Instance = null;
        }
        private void Awake() {
            Assert.IsNull(Instance, "StarterAssetInputs is not null in Awake()");
            Instance = this;
            _inputAction = GetComponent<PlayerInput>();
        }
        private void Start()
        {
			SetControlsEnabled("Menu", true);
			SetControlsEnabled("Player", true);
        }
		public void OnDiedRpc() {
			SetControlsEnabled("Menu", false);
		}
		public void SetControlsEnabled(string actionName, bool enabled) {
			var map = _inputAction.actions.FindActionMap(actionName);
			if (enabled)
				map.Enable();
			else
				map.Disable();
        }
		public bool GetControlsEnabled(string actionName) {
			var map = _inputAction.actions.FindActionMap(actionName);
			return map.enabled;
		}
        public void OnMove(InputValue value)
		{
			MoveInput(value.Get<Vector2>());
		}

		public void OnLook(InputValue value)
		{
			if(cursorInputForLook)
			{
				LookInput(value.Get<Vector2>());
			}
		}

		public void OnJump(InputValue value)
		{
            JumpInput(value.isPressed);
		}

		public void OnSprint(InputValue value)
		{
			SprintInput(value.isPressed);
		}


		public void MoveInput(Vector2 newMoveDirection)
		{
			move = newMoveDirection;
		} 

		public void LookInput(Vector2 newLookDirection)
		{
			look = newLookDirection;
		}

		public void JumpInput(bool newJumpState)
		{
            jump = newJumpState;
		}

		public void SprintInput(bool newSprintState)
		{
			sprint = newSprintState;
		}

		private void OnApplicationFocus(bool hasFocus)
		{
			SetCursorState(cursorLocked);
		}

		private void SetCursorState(bool newState)
		{
			Cursor.lockState = newState ? CursorLockMode.Locked : CursorLockMode.None;
		}

		public void OnToggleMenu()
		{
			menuToggledEvent.Invoke();
		}

    }
	
}