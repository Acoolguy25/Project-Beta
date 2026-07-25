using RyanAssets.Characters.Shared;
#if !UNITY_SERVER
using RyanAssets.Input;
#endif
using RyanAssets.Tools.Shared;
using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using UnityEngine.EventSystems;

namespace RyanAssets.Tools.Client
{
    [RequireComponent(typeof(ToolBaseShared))]
    public class ToolBaseClient : MonoBehaviour {
        public Func<bool> CanActivateEvent = null;
        public event Action<float, float> onCooldownChangeEvent;
        protected List<CancellationTokenSource> activeTasks = new();
        protected ToolBaseShared toolBaseShared;
        protected CharacterAnimator characterAnimator;
        protected Animator animator;
        protected Collider hitCollider;
        protected float StartCooldown, StopCooldown;
        protected virtual void Start() {
            toolBaseShared = GetComponent<ToolBaseShared>();
            hitCollider = toolBaseShared.weaponRoot.GetComponentInChildren<Collider>(true);
            animator = toolBaseShared.connectedCharacter.GetComponent<Animator>();
            characterAnimator = animator.GetComponent<CharacterAnimator>();
            toolBaseShared.equippedEvent += OnEquip;
            toolBaseShared.unequippedEvent += OnUnequip;
        }
        protected virtual void OnEquip(ToolBaseShared _) {
#if !UNITY_SERVER
            ToolControls.activateToolPressed += TryActivate;
#endif
            characterAnimator.LethalAttackStarted += OnLethalAttackStart;
            characterAnimator.LethalAttackEnded += OnLethalAttackEnd;
        }
        protected virtual void OnUnequip(ToolBaseShared _) {
#if !UNITY_SERVER
            ToolControls.activateToolPressed -= TryActivate;
#endif
            characterAnimator.LethalAttackStarted -= OnLethalAttackStart;
            characterAnimator.LethalAttackEnded -= OnLethalAttackEnd;
            if (characterAnimator.LethalAttackEnabled)
                SetLethalAttack(false);
            SetAttacking(false);
            foreach (CancellationTokenSource token in activeTasks) {
                token.Cancel();
                token.Dispose();
            }
            activeTasks.Clear();
        }
        public virtual void TryActivate() { 
            if (CanAttack()) {
                OnActivate();
            }
        }
        protected virtual void OnActivate() {

        }
        protected virtual void SetAttacking(bool attack) {
            
        }
        protected virtual void OnLethalAttackStart() {
            SetLethalAttack(true);
        }
        protected virtual void OnLethalAttackEnd() {
            SetLethalAttack(false);
        }
        protected virtual void SetLethalAttack(bool attack) {

        }
        protected virtual void OnHit(Collider collider) {

        }
        protected virtual void SetCooldown(float Duration) {
            StartCooldown = Time.time;
            StopCooldown = Time.time + Duration;
            onCooldownChangeEvent?.Invoke(StartCooldown, StopCooldown);
        }
        protected virtual bool CanAttack() {
            return StopCooldown <= Time.time && (CanActivateEvent == null || CanActivateEvent());
        }
        protected virtual CancellationTokenSource AddCancellationToken() {
            CancellationTokenSource token = new();
            activeTasks.Add(token);
            return token;
        }
    }
}
