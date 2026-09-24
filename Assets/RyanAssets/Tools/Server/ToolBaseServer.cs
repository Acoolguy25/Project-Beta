using FishNet.Object;
using RyanAssets.Characters.Shared;
using RyanAssets.Shared.Declarations;
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
            toolBaseShared.hitEvent += HandleHit;
        }
        /// <summary>
        /// Resolves what a shot or swing actually struck.
        /// <para>
        /// A bullet stops on whatever is in front of the muzzle, and that is not always a character:
        /// an NPC firing at a structure or an objective hits a <see cref="IEntity"/> with no
        /// <see cref="GameCharacter"/> on it at all. Resolving the entity rather than assuming a
        /// character keeps those hits damaging instead of throwing on a null cast.
        /// </para>
        /// </summary>
        void HandleHit(NetworkObject hitObject) {
            if (hitObject == null)
                return;

            GameCharacter character = hitObject.GetComponent<GameCharacter>();
            if (character != null) {
                OnHit(character);
                return;
            }

            OnHitEntity(hitObject.GetComponent<IEntity>());
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
        /// <summary>Damage applied to a non-character entity: a structure, wall, or objective.</summary>
        protected virtual void OnHitEntity(IEntity entity) {
            if (entity == null)
                return;
            // The wielder is tracked as a NetworkBehaviour, but a kill credit and the friendly-fire
            // check both need it as an entity.
            IEntity wielder = toolBaseShared.connectedCharacter as IEntity;
            if (entity.TakeDamage(toolBaseShared.hitDamage, toolBaseShared.defaultDamageType, wielder)) {
                toolBaseShared.PlayAudio(0);
            }
        }
    }
}
