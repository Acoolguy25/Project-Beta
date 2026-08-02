using Cysharp.Threading.Tasks;
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
        //protected List<CancellationTokenSource> activeTasks = new();
        protected CancellationTokenSource activeTask = new();
        protected ToolBaseShared toolBaseShared;
        protected CharacterAnimator characterAnimator;
        protected Animator animator;
        protected Collider hitCollider;
        protected float StartCooldown, StopCooldown;
        protected bool IsAttacking;
        protected virtual void Awake() {
            toolBaseShared = GetComponent<ToolBaseShared>();
            hitCollider = toolBaseShared.weaponRoot.GetComponentInChildren<Collider>(true);
            animator = toolBaseShared.connectedCharacter.GetComponent<Animator>();
            characterAnimator = animator.GetComponent<CharacterAnimator>();
            toolBaseShared.equippedEvent += OnEquip;
            toolBaseShared.unequippedEvent += OnUnequip;
            SetLethalAttack(false);
        }
        protected virtual void OnEquip(ToolBaseShared _) {
#if !UNITY_SERVER
            ToolControls.activateToolPressed += TryActivate;
            ToolControls.reloadToolPressed += TryReload;
#endif
            characterAnimator.LethalAttackStarted += OnLethalAttackStart;
            characterAnimator.LethalAttackEnded += OnLethalAttackEnd;
        }
        protected virtual void OnUnequip(ToolBaseShared _) {
#if !UNITY_SERVER
            ToolControls.activateToolPressed -= TryActivate;
            ToolControls.reloadToolPressed -= TryReload;
#endif
            characterAnimator.LethalAttackStarted -= OnLethalAttackStart;
            characterAnimator.LethalAttackEnded -= OnLethalAttackEnd;
            if (characterAnimator.LethalAttackEnabled)
                SetLethalAttack(false);
            SetAttacking(false);
            //foreach (CancellationTokenSource token in activeTasks) {
            //    token.Cancel();
            //    token.Dispose();
            //}
            //activeTasks.Clear();
            activeTask.Cancel();
            activeTask.Dispose();
            activeTask = new();
        }
        public virtual void TryActivate(Vector3 targetLocation = default) { 
            if (CanAttack()) {
                OnActivate(targetLocation);
            } else if (MustReload()) {
                TryReload();
            }
        }
        protected virtual void OnActivate(Vector3 targetLocation) {

        }
        protected virtual void TryReload() {
            if (CanReload()) {
                Reload();
            }
        }
        protected virtual async void Reload() {
            SetCooldown(toolBaseShared.reloadDuration);
            bool isCancelled = await UniTask.WaitForSeconds(toolBaseShared.reloadDuration, cancellationToken: AddCancellationToken().Token).SuppressCancellationThrow();
            if (isCancelled)
                return;
            if (toolBaseShared.currentStoredAmmo >= 0) { // finite ammo enabled
                int ammoToReload = Mathf.Min(toolBaseShared.maxClipAmmo - toolBaseShared.currentAmmo, toolBaseShared.currentStoredAmmo);
                SetCurrentAmmo(toolBaseShared.currentAmmo + ammoToReload);
                SetMaxAmmo(toolBaseShared.currentStoredAmmo - ammoToReload);
            } else { // infinite ammo enabled
                SetCurrentAmmo(toolBaseShared.maxClipAmmo);
            }
        }
        protected virtual void SetCurrentAmmo(int ammo) {
            toolBaseShared.currentAmmo = ammo;
            toolBaseShared.currentAmmoEvent?.Invoke(toolBaseShared.currentAmmo);
        }
        protected virtual void SetMaxAmmo(int ammo) {
            toolBaseShared.currentStoredAmmo = ammo;
            toolBaseShared.maxAmmoEvent?.Invoke(toolBaseShared.currentStoredAmmo);
        }
        protected virtual bool ConsumeAmmo(int amount) {
            if (toolBaseShared.currentAmmo >= 0) {
                int newAmmo = toolBaseShared.currentAmmo - amount;
                if (newAmmo < 0) {
                    return false; // ran out of ammo scrub
                }
                SetCurrentAmmo(toolBaseShared.currentAmmo - amount);
            }
            return true;
        }
        protected virtual void SetAttacking(bool attack) {
            IsAttacking = attack;
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
        protected virtual bool CanReload() {
            return toolBaseShared.currentAmmo >= 0 && toolBaseShared.currentAmmo < toolBaseShared.maxClipAmmo && toolBaseShared.currentStoredAmmo != 0
                && !IsOnCooldown();
        }
        protected virtual bool MustReload() {
            return toolBaseShared.currentAmmo == 0;
        }
        protected virtual bool IsOnCooldown() {
            return StopCooldown > Time.time;
        }
        protected virtual bool CanAttack() {
            return !IsOnCooldown() && (CanActivateEvent == null || CanActivateEvent()) 
                && !IsAttacking // Attack Checks
                && toolBaseShared.currentAmmo != 0 // Ammo Checks
                && gameObject != null && toolBaseShared.equipped; // Sanity Checks
        }
        protected virtual CancellationTokenSource AddCancellationToken() {
            return activeTask;
        }
    }
}
