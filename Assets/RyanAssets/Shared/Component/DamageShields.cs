using System.Collections.Generic;
using RyanAssets.Shared.Declarations;

namespace RyanAssets.Shared.Component {
    /// <summary>Something that can stop a hit from reaching an entity: a force field, a safe zone.</summary>
    public interface IDamageShield {
        /// <summary>
        /// True when this shield stops <paramref name="source"/> from harming <paramref name="target"/>.
        /// Called on the server for every hit, so implementations answer from cached state.
        /// </summary>
        bool Blocks(IEntity target, IEntity source, DamageType damageType);
    }

    /// <summary>
    /// The shields currently standing in the world, consulted by <see cref="HealthComponent.IsProtected"/>.
    /// <para>
    /// A game mode's shield registers itself here while it is up, and every source of damage in
    /// every universe - a gun, a knife, a vehicle, an explosion - is refused in the one place damage
    /// is already refused for invulnerability and team kills. Targeting that skips protected
    /// entities follows automatically, because it asks the same question.
    /// </para>
    /// </summary>
    public static class DamageShields {
        static readonly List<IDamageShield> shields = new();

        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => shields.Clear();

        public static void Register(IDamageShield shield) {
            if (shield != null && !shields.Contains(shield))
                shields.Add(shield);
        }

        public static void Unregister(IDamageShield shield) => shields.Remove(shield);

        /// <summary>True when any registered shield stops this hit.</summary>
        public static bool IsShielded(IEntity target, IEntity source, DamageType damageType) {
            if (target == null)
                return false;
            for (int i = 0; i < shields.Count; i++) {
                if (shields[i].Blocks(target, source, damageType))
                    return true;
            }
            return false;
        }
    }
}
