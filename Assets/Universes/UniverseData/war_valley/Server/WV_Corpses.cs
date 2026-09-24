using System.Collections;
using RyanAssets.Characters.Shared;
using UnityEngine;

namespace Universes.UniverseData.war_valley.Server {
    /// <summary>
    /// Clears the fallen off the valley floor.
    /// <para>
    /// A killed character ragdolls and is tagged as dead, but nothing ever despawned it, so bodies
    /// piled up for the whole round. That is not only untidy: a dead character is still registered
    /// on its team, and the end-of-round "finish off remaining enemies" count is taken from that
    /// register, so corpses kept the round from ever ending.
    /// </para>
    /// </summary>
    public static class WV_Corpses {
        /// <summary>How long a body stays where it fell. Long enough to see the kill, short enough to clear.</summary>
        public const float LingerSeconds = 8f;

        /// <summary>
        /// Despawns this character once it dies. Safe to call for any spawned character; the
        /// subscription releases itself whether the character dies or leaves first.
        /// </summary>
        public static void DespawnWhenDead(MonoBehaviour host, GameCharacter character) {
            if (host == null || character == null)
                return;

            void HandleDied(RyanAssets.Shared.Declarations.DamageType source,
                RyanAssets.Shared.Declarations.IEntity attacker) {
                character.OnDied -= HandleDied;
                if (host != null && host.isActiveAndEnabled)
                    host.StartCoroutine(DespawnAfterLinger(character));
            }

            character.OnDied += HandleDied;
        }

        static IEnumerator DespawnAfterLinger(GameCharacter character) {
            yield return new WaitForSeconds(LingerSeconds);

            if (character != null && character.IsSpawned)
                character.Despawn();
        }
    }
}
