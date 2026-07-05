using RyanAssets.Input;
using RyanAssets.Tools.Shared;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.EventSystems;

namespace RyanAssets.Tools.Client
{
    [RequireComponent(typeof(ToolBaseShared))]
    public class ToolBaseClient : MonoBehaviour {
        protected List<CancellationTokenSource> activeTasks = new();
        protected ToolBaseShared toolBaseShared;
        protected Animator animator;
        protected Collider hitCollider;
        protected virtual void Start() {
            toolBaseShared = GetComponent<ToolBaseShared>();
            hitCollider = toolBaseShared.weaponRoot.GetComponent<Collider>();
            animator = toolBaseShared.connectedCharacter.GetComponent<Animator>();
            toolBaseShared.equippedEvent += OnEquip;
            toolBaseShared.unequippedEvent += OnUnequip;
            ToolControls.activateToolPressed += OnActivate;
        }
        protected virtual void OnEquip(ToolBaseShared _) {

        }
        protected virtual void OnUnequip(ToolBaseShared _) {
            SetAttacking(false);
            foreach (CancellationTokenSource token in activeTasks) {
                token.Cancel();
                token.Dispose();
            }
            activeTasks.Clear();
        }
        protected virtual void OnActivate() {

        }
        protected virtual void SetAttacking(bool attack) {
        
        }
        protected virtual void OnHit(Collision collision) {

        }
        protected virtual CancellationTokenSource AddCancellationToken() {
            CancellationTokenSource token = new();
            activeTasks.Add(token);
            return token;
        }
    }
}
