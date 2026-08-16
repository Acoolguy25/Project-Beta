using RyanAssets.Characters.Shared;
using RyanAssets.Tools.Shared;
using UnityEngine;

namespace RyanAssets.Tools.Server
{
    [RequireComponent(typeof(ToolBaseShared))]
    public class ToolBaseServer : MonoBehaviour {
        protected ToolBaseShared toolBaseShared;
        protected virtual void Start() {
            toolBaseShared = GetComponent<ToolBaseShared>();
            toolBaseShared.equippedEvent += OnEquip;
            toolBaseShared.unequippedEvent += OnUnequip;
            toolBaseShared.hitEvent += (gameCharacter) => {
                OnHit(gameCharacter.gameObject.GetComponent<GameCharacter>());
            };
        }
        protected virtual void OnEquip(ToolBaseShared _) {

        }
        protected virtual void OnUnequip(ToolBaseShared _) {

        }
        protected virtual void OnHit(GameCharacter character) {
            if (character.TakeDamage(toolBaseShared.hitDamage, toolBaseShared.defaultDamageType, toolBaseShared.connectedCharacter)) {
                toolBaseShared.PlayAudio(0); // Play audio only if the hit was successful
            }
        }
    }
}
