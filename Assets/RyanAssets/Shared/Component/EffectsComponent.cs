using FishNet.Object;
using FishNet.Object.Synchronizing;
using RyanAssets.Core;
using RyanAssets.Shared.Declarations;

namespace RyanAssets.Shared.Component {
    /// <summary>Owns the networked status effects for an entity.</summary>
    public class EffectsComponent : NetworkBehaviour {
        public readonly SyncDictionary<CharacterEffect, float> ActiveEffects = new();

        public bool IsEffectActive(CharacterEffect effect) {
            return ActiveEffects.TryGetValue(effect, out float expiresAt)
                && expiresAt >= NetworkHelper.GetServerTime();
        }

#if UNITY_SERVER
        [Server]
        public virtual void AddEffect(CharacterEffect effect, float duration) {
            if (IsEffectActive(effect))
                ActiveEffects[effect] += duration;
            else
                ActiveEffects[effect] = NetworkHelper.GetServerTime() + duration;
        }

        [Server]
        public virtual void RemoveEffect(CharacterEffect effect) {
            ActiveEffects.Remove(effect);
        }

        [Server]
        public virtual void ClearEffects() {
            ActiveEffects.Clear();
        }
#endif
    }
}
