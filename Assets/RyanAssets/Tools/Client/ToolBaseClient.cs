using RyanAssets.Input;
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
        public event Action<float, float> onCooldownChangeEvent;
        protected List<CancellationTokenSource> activeTasks = new();
        protected ToolBaseShared toolBaseShared;
        protected Animator animator;
        protected Collider hitCollider;
        protected float StartCooldown, StopCooldown;
        protected virtual void Start() {
            toolBaseShared = GetComponent<ToolBaseShared>();
            hitCollider = toolBaseShared.weaponRoot.GetComponentInChildren<Collider>(true);
            animator = toolBaseShared.connectedCharacter.GetComponent<Animator>();
            toolBaseShared.equippedEvent += OnEquip;
            toolBaseShared.unequippedEvent += OnUnequip;
        }
        protected virtual void OnEquip(ToolBaseShared _) {
            ToolControls.activateToolPressed += TryActivate;
        }
        protected virtual void OnUnequip(ToolBaseShared _) {
            ToolControls.activateToolPressed -= TryActivate;
            SetAttacking(false);
            foreach (CancellationTokenSource token in activeTasks) {
                token.Cancel();
                token.Dispose();
            }
            activeTasks.Clear();
        }
        protected virtual void TryActivate() { 
            if (CanAttack()) {
                OnActivate();
            }
        }
        protected virtual void OnActivate() {

        }
        protected virtual void SetAttacking(bool attack) {
        
        }
        protected virtual void OnHit(Collider collider) {

        }
        protected virtual void SetCooldown(float Duration) {
            StartCooldown = Time.time;
            StopCooldown = Time.time + Duration;
            onCooldownChangeEvent?.Invoke(StartCooldown, StopCooldown);
        }
        protected virtual bool CanAttack() {
            return StopCooldown <= Time.time;
        }
        protected virtual CancellationTokenSource AddCancellationToken() {
            CancellationTokenSource token = new();
            activeTasks.Add(token);
            return token;
        }
    }
}
