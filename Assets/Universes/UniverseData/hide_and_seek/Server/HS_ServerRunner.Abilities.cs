using System.Collections.Generic;
using FishNet;
using FishNet.Connection;
using RyanAssets.Characters.Shared;
using RyanAssets.Server.ServerCore;
using UnityEngine;
using Channel = FishNet.Transporting.Channel;

namespace Universes.UniverseData.hide_and_seek.Server {
    /// <summary>
    /// Role abilities, decided here: hiders Dash, throw a Decoy and Blend In; seekers (and found hiders)
    /// Rush, sweep a Radar and fire a Flashlight. Each is unlocked by the player's level in this game
    /// (<see cref="HS_Retention"/>), has a cooldown and, when it has an effect over time, ends by itself.
    /// Clients only draw what <see cref="HS_AbilityBroadcast"/> and <see cref="HS_AbilityStateBroadcast"/> say.
    /// A decoy reaches everyone but its owner as a real <see cref="HS_TauntBroadcast"/>, so no client can
    /// tell it from a taunt; pressing Blend In again ends it early.
    /// </summary>
    public sealed partial class HS_ServerRunner {
        [Header("Abilities (unlock levels: HS_Retention)")]
        [SerializeField] HS_AbilityTuning abilityTuning = new();
        [Tooltip("Height above the feet (world units) of a player's eyes: decoys are thrown, the flashlight shines and tags reach from here (the robot's head).")]
        [SerializeField, Min(0f)] float abilityEyeHeight = 4.4f;
        [Tooltip("Height above a hider's feet (world units) that the flashlight and a tag aim at: about the chest.")]
        [SerializeField, Min(0f)] float abilityTargetHeight = 3f;
        [Tooltip("Decoy Pair: the two decoys fly this many degrees either side of the aim.")]
        [SerializeField, Range(5f, 60f)] float decoyPairSpread = 22f;
        [Tooltip("A decoy lands this far short of a wall it hits (world units).")]
        [SerializeField, Min(0f)] float decoyWallMargin = 1f;
        [Tooltip("Shortest time between two ability requests from one player that are looked at (seconds).")]
        [SerializeField, Min(0f)] float abilityRequestInterval = 0.2f;

        sealed class AbilityState {
            public readonly float[] readyAt = new float[HS_Abilities.Count];
            public readonly float[] activeUntil = new float[HS_Abilities.Count];
            public float dazzledUntil = float.NegativeInfinity;
            public NetworkConnection dazzledBy;
            public float lastRequest = float.NegativeInfinity;
            /// <summary>Whether movement was last applied with an effect running, so its end re-applies it once.</summary>
            public bool movementEffect;
            public bool blendShown;
        }

        struct Decoy {
            public NetworkConnection owner;
            public Vector3 position;
            public float until;
            public bool fooledSomeone;
        }

        readonly Dictionary<NetworkConnection, AbilityState> abilityStates = new();
        readonly List<Decoy> decoys = new();
        readonly List<NetworkConnection> abilityScratch = new();
        readonly List<int> targetScratch = new();
        readonly RaycastHit[] rayHits = new RaycastHit[16];

        AbilityState AbilitiesOf(NetworkConnection c) {
            if (!abilityStates.TryGetValue(c, out AbilityState state)) abilityStates[c] = state = new AbilityState();
            return state;
        }

        bool IsActive(NetworkConnection c, HS_Ability ability) =>
            abilityStates.TryGetValue(c, out AbilityState state) && Time.time < state.activeUntil[(int)ability];

        bool IsBlended(NetworkConnection c) => IsActive(c, HS_Ability.Blend);
        bool IsDazzled(NetworkConnection c) => abilityStates.TryGetValue(c, out AbilityState state) && Time.time < state.dazzledUntil;
        bool IsRooted(NetworkConnection c) => IsBlended(c);

        bool Upgraded(NetworkConnection c, HS_Ability ability) {
            string upgrade = HS_Abilities.Info(ability).UpgradeId;
            return upgrade != null && retention.IsUnlocked(c, upgrade);
        }

        HS_AbilityStats StatsOf(NetworkConnection c, HS_Ability ability) => abilityTuning.Stats(ability, Upgraded(c, ability));

        /// <summary>Speed multiplier from abilities and events: a dash or rush, a flashlight's dazzle, a seekers' frenzy.</summary>
        float SpeedScaleOf(NetworkConnection c, HS_Role role) {
            float scale = 1f;
            if (IsActive(c, HS_Ability.Dash)) scale *= StatsOf(c, HS_Ability.Dash).power;
            if (IsActive(c, HS_Ability.Rush)) scale *= StatsOf(c, HS_Ability.Rush).power;
            if (IsDazzled(c)) scale *= abilityTuning.dazzledSpeedScale;
            if (activeEvent == HS_RoundEvent.Frenzy && Seeks(role)) scale *= events.frenzySpeedScale;
            return scale;
        }

        bool HasMovementEffect(NetworkConnection c) =>
            IsActive(c, HS_Ability.Dash) || IsActive(c, HS_Ability.Rush) || IsBlended(c) || IsDazzled(c);

        // ---------------------------------------------------------------- requests
        void OnAbilityRequest(NetworkConnection connection, HS_AbilityRequest request, Channel channel) {
            if (!connection.IsAuthenticated || request.round != round || !HS_Abilities.IsKnown(request.ability)) return;
            AbilityState state = AbilitiesOf(connection);
            if (Time.unscaledTime - state.lastRequest < abilityRequestInterval) return;
            state.lastRequest = Time.unscaledTime;

            HS_Ability ability = request.ability;
            HS_AbilityInfo info = HS_Abilities.Info(ability);
            // A second press of a running Blend In steps out of it (its cooldown keeps running from the first press).
            if (HS_Abilities.Cancels(RoleOf(connection), ability, state.activeUntil[(int)ability] - Time.time)) {
                state.activeUntil[(int)ability] = 0f;
                ApplyMovement(connection);
                SendAbilityState(connection);
                return;
            }
            float readyIn = state.readyAt[(int)ability] - Time.time;
            string refusal = HS_Abilities.Refusal(phase, RoleOf(connection), ability, retention.IsUnlocked(connection, info.UnlockId), readyIn);
            if (refusal != null) {
                InstanceFinder.ServerManager.Broadcast(connection, new HS_ResultBroadcast { accepted = false, message = refusal, ability = ability });
                // Resend the bar so a HUD that drifted (a missed update, a stale cooldown) corrects itself.
                SendAbilityState(connection);
                return;
            }
            if (!TryGetLiveCharacter(connection, out LocalCharacter character)) return;

            bool upgraded = Upgraded(connection, ability);
            HS_AbilityStats stats = abilityTuning.Stats(ability, upgraded);
            Vector3 feet = character.transform.position;
            Vector3 aim = HS_Abilities.SafeDirection(request.direction, character.transform.forward);
            state.readyAt[(int)ability] = Time.time + abilityTuning.Cooldown(ability, upgraded, retention.IsUnlocked(connection, HS_Retention.UnlockIds.QuickRecovery));
            switch (ability) {
                case HS_Ability.Dash:
                case HS_Ability.Rush:
                case HS_Ability.Blend:
                    state.activeUntil[(int)ability] = Time.time + stats.duration;
                    ApplyMovement(connection);
                    InstanceFinder.ServerManager.Broadcast(new HS_AbilityBroadcast {
                        clientId = connection.ClientId, ability = ability, position = feet, direction = aim, seconds = stats.duration, range = stats.power,
                        flair = ability != HS_Ability.Blend && retention.IsUnlocked(connection, HS_Retention.UnlockIds.GhostTrail),
                    });
                    break;
                case HS_Ability.Decoy:
                    if (retention.IsUnlocked(connection, HS_Retention.UnlockIds.DecoyPair)) {
                        ThrowDecoy(connection, feet, Quaternion.Euler(0f, -decoyPairSpread, 0f) * aim, stats);
                        ThrowDecoy(connection, feet, Quaternion.Euler(0f, decoyPairSpread, 0f) * aim, stats);
                    } else ThrowDecoy(connection, feet, aim, stats);
                    break;
                case HS_Ability.Radar:
                    SweepRadar(connection, feet, stats);
                    break;
                case HS_Ability.Flashlight:
                    FireFlashlight(connection, feet, aim, stats);
                    break;
            }
            retention.Count(connection, HS_Retention.Stats.Abilities);
            PlayerProgress.Modify<HS_SaveData>(connection, save => save.abilitiesUsed++);
            SendAbilityState(connection);
        }

        /// <summary>A blend starting (<paramref name="seconds"/> left) or ending (0), for everyone or one client.</summary>
        void BroadcastBlend(NetworkConnection user, float seconds, NetworkConnection to = null) {
            var message = new HS_AbilityBroadcast { clientId = user.ClientId, ability = HS_Ability.Blend, seconds = seconds };
            if (to != null) InstanceFinder.ServerManager.Broadcast(to, message);
            else InstanceFinder.ServerManager.Broadcast(message);
        }

        /// <summary>A player who joins mid-round learns which hiders are blended right now, so they never see one in the open.</summary>
        void SendRunningBlends(NetworkConnection to) {
            if (to == null || !to.IsActive) return;
            foreach (var pair in abilityStates) {
                float left = pair.Value.activeUntil[(int)HS_Ability.Blend] - Time.time;
                if (left > 0f && pair.Key != to && pair.Key.IsActive) BroadcastBlend(pair.Key, left, to);
            }
        }

        /// <summary>
        /// The decoy flies flat along the aim, stops short of a wall, and drops to the floor below. Its owner
        /// gets the ability message (their marker); everyone else gets a real taunt at the spot.
        /// </summary>
        void ThrowDecoy(NetworkConnection owner, Vector3 feet, Vector3 aim, HS_AbilityStats stats) {
            Vector3 flat = new Vector3(aim.x, 0f, aim.z);
            flat = flat.sqrMagnitude < 1e-4f ? Vector3.forward : flat.normalized;
            Vector3 from = feet + Vector3.up * abilityEyeHeight;
            float distance = stats.power;
            if (TryHitWorld(from, flat, distance, out float hitDistance)) distance = Mathf.Max(0f, hitDistance - decoyWallMargin);
            Vector3 over = from + flat * distance;
            Vector3 spot = TryHitWorld(over, Vector3.down, abilityEyeHeight * 3f, out float drop) ? over + Vector3.down * drop : new Vector3(over.x, feet.y, over.z);
            decoys.Add(new Decoy { owner = owner, position = spot, until = Time.time + stats.duration });
            InstanceFinder.ServerManager.Broadcast(owner, new HS_AbilityBroadcast {
                clientId = owner.ClientId, ability = HS_Ability.Decoy, position = spot, direction = flat, seconds = stats.duration, range = distance,
            });
            BroadcastTauntToOthers(owner, spot);
        }

        /// <summary>The user gets blurred fixes on hiders (not blended ones) and decoys in range; those hiders are warned.</summary>
        void SweepRadar(NetworkConnection seeker, Vector3 feet, HS_AbilityStats stats) {
            var positions = new List<Vector3>();
            foreach (var pair in roles) {
                if (pair.Value != HS_Role.Hider || IsBlended(pair.Key) || !TryGetLiveCharacter(pair.Key, out LocalCharacter hider)) continue;
                if (Vector3.Distance(feet, hider.transform.position) > stats.power) continue;
                positions.Add(Blur(hider.transform.position, abilityTuning.radarScatter));
                InstanceFinder.ServerManager.Broadcast(pair.Key, new HS_ResultBroadcast { accepted = true, message = "A seeker's radar picked you up. Move!", ability = HS_Ability.Radar });
            }
            AddDecoyPositions(positions, feet, stats.power, abilityTuning.radarScatter);
            InstanceFinder.ServerManager.Broadcast(seeker, new HS_AbilityBroadcast {
                clientId = seeker.ClientId, ability = HS_Ability.Radar, position = feet, seconds = stats.duration, range = stats.power, positions = positions.ToArray(),
            });
            // Everyone else sees the sweep go out (not what it found).
            var others = new HashSet<NetworkConnection>();
            foreach (var pair in roles) if (pair.Key != seeker && pair.Key.IsActive) others.Add(pair.Key);
            InstanceFinder.ServerManager.Broadcast(others, new HS_AbilityBroadcast {
                clientId = seeker.ClientId, ability = HS_Ability.Radar, position = feet, seconds = stats.duration, range = stats.power,
            });
        }

        /// <summary>Hiders inside the beam with nothing in between are dazzled: slowed, revealed to seekers, and un-blended.</summary>
        void FireFlashlight(NetworkConnection seeker, Vector3 feet, Vector3 aim, HS_AbilityStats stats) {
            Vector3 eye = feet + Vector3.up * abilityEyeHeight;
            targetScratch.Clear();
            foreach (var pair in roles) {
                if (pair.Value != HS_Role.Hider || !TryGetLiveCharacter(pair.Key, out LocalCharacter hider)) continue;
                Vector3 chest = hider.transform.position + Vector3.up * abilityTargetHeight;
                if (!HS_Abilities.InCone(eye, aim, chest, stats.power, abilityTuning.flashlightConeDegrees)) continue;
                Vector3 to = chest - eye;
                if (TryHitWorld(eye, to.normalized, to.magnitude, out _)) continue;
                AbilityState state = AbilitiesOf(pair.Key);
                state.dazzledUntil = Time.time + stats.duration;
                state.dazzledBy = seeker;
                if (IsBlended(pair.Key)) EndBlend(pair.Key);
                ApplyMovement(pair.Key);
                targetScratch.Add(pair.Key.ClientId);
            }
            InstanceFinder.ServerManager.Broadcast(new HS_AbilityBroadcast {
                clientId = seeker.ClientId, ability = HS_Ability.Flashlight, position = eye, direction = aim, seconds = stats.duration, range = stats.power,
                angle = abilityTuning.flashlightConeDegrees, targets = targetScratch.ToArray(),
            });
        }

        /// <summary>The nearest hit along a ray that is not a character (characters never block a throw or a beam).</summary>
        bool TryHitWorld(Vector3 from, Vector3 direction, float distance, out float hitDistance) {
            hitDistance = float.MaxValue;
            int count = Physics.RaycastNonAlloc(from, direction, rayHits, distance, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++) {
                if (rayHits[i].collider.GetComponentInParent<LocalCharacter>() != null) continue;
                hitDistance = Mathf.Min(hitDistance, rayHits[i].distance);
            }
            return hitDistance < float.MaxValue;
        }

        Vector3 Blur(Vector3 position, float scatter) {
            double a = random.NextDouble() * Mathf.PI * 2, r = random.NextDouble() * scatter;
            return position + new Vector3((float)(System.Math.Cos(a) * r), 0f, (float)(System.Math.Sin(a) * r));
        }

        /// <summary>Adds the live decoys (within <paramref name="radius"/> of <paramref name="center"/>, when given) to a ping or radar list.</summary>
        void AddDecoyPositions(List<Vector3> positions, Vector3? center, float radius, float scatter) {
            foreach (Decoy decoy in decoys) {
                if (Time.time >= decoy.until) continue;
                if (center.HasValue && Vector3.Distance(center.Value, decoy.position) > radius) continue;
                positions.Add(Blur(decoy.position, scatter));
            }
        }

        // ---------------------------------------------------------------- effects over time
        /// <summary>Re-applies movement when an effect ends, and pays a hider whose decoy a seeker walked up to.</summary>
        void TickAbilities() {
            abilityScratch.Clear();
            foreach (var pair in abilityStates) {
                AbilityState state = pair.Value;
                bool moving = HasMovementEffect(pair.Key);
                if (moving != state.movementEffect) {
                    state.movementEffect = moving;
                    abilityScratch.Add(pair.Key);
                }
                bool blended = IsBlended(pair.Key);
                if (state.blendShown && !blended) BroadcastBlend(pair.Key, 0f);
                state.blendShown = blended;
            }
            foreach (NetworkConnection c in abilityScratch) ApplyMovement(c);
            if (phase == HS_Phase.Seeking) CheckDecoys();
        }

        void CheckDecoys() {
            for (int i = decoys.Count - 1; i >= 0; i--) {
                Decoy decoy = decoys[i];
                if (Time.time >= decoy.until) {
                    decoys.RemoveAt(i);
                    continue;
                }
                if (decoy.fooledSomeone || NearestSeekerDistance(decoy.position) > abilityTuning.decoyFoolRange) continue;
                decoy.fooledSomeone = true;
                decoys[i] = decoy;
                if (decoy.owner.IsActive) AddPoints(decoy.owner, scoring.decoyFooledPoints, "Decoy fooled a seeker", true);
            }
        }

        /// <summary>Taunts this player may make in a round.</summary>
        int TauntAllowance(NetworkConnection c) =>
            HS_Rules.TauntsPerRound + (retention.IsUnlocked(c, HS_Retention.UnlockIds.TauntMaster) ? HS_Retention.TauntMasterExtra : 0);

        void EndBlend(NetworkConnection c) {
            if (!abilityStates.TryGetValue(c, out AbilityState state)) return;
            state.activeUntil[(int)HS_Ability.Blend] = 0f;
        }

        /// <summary>Stops every running effect on a player (they were found, or the round ended).</summary>
        void EndEffects(NetworkConnection c) {
            if (!abilityStates.TryGetValue(c, out AbilityState state)) return;
            System.Array.Clear(state.activeUntil, 0, state.activeUntil.Length);
            state.dazzledUntil = float.NegativeInfinity;
        }

        /// <summary>A fresh round: no cooldowns, no effects, no decoys. Blends end where everyone sees it.</summary>
        void ResetAbilities() {
            foreach (var pair in abilityStates)
                if (pair.Value.blendShown) BroadcastBlend(pair.Key, 0f);
            abilityStates.Clear();
            decoys.Clear();
        }

        void ForgetAbilities(NetworkConnection c) {
            abilityStates.Remove(c);
            decoys.RemoveAll(d => d.owner == c);
        }

        /// <summary>Unlocking an ability fills its slot at once (the kit shows the toast).</summary>
        void OnUnlocked(NetworkConnection connection, RyanAssets.Retention.UnlockDefinition unlock) => SendAbilityState(connection);

        /// <summary>The player's level is known (their progress loaded): their bar can show what they own.</summary>
        void OnRetentionReady(NetworkConnection connection, int level) => SendAbilityState(connection);

        // ---------------------------------------------------------------- the bar
        void SendAbilityState(NetworkConnection c) {
            if (c == null || !c.IsActive || InstanceFinder.ServerManager == null) return;
            HS_Role role = RoleOf(c);
            bool hasBar = HS_Abilities.HasBar(role) && (phase is HS_Phase.Hiding or HS_Phase.Seeking);
            var slots = new HS_AbilitySlot[hasBar ? HS_Abilities.SlotsPerSide : 0];
            abilityStates.TryGetValue(c, out AbilityState state);
            bool quick = hasBar && retention.IsUnlocked(c, HS_Retention.UnlockIds.QuickRecovery);
            for (int i = 0; i < slots.Length; i++) {
                HS_Ability ability = HS_Abilities.InSlot(HS_Abilities.SeekerSide(role), i);
                HS_AbilityInfo info = HS_Abilities.Info(ability);
                bool upgraded = Upgraded(c, ability);
                HS_AbilityStats stats = abilityTuning.Stats(ability, upgraded);
                slots[i] = new HS_AbilitySlot {
                    ability = ability,
                    unlocked = retention.IsUnlocked(c, info.UnlockId),
                    unlockLevel = HS_Retention.UnlockLevel(info.UnlockId),
                    upgraded = upgraded,
                    cooldown = abilityTuning.Cooldown(ability, upgraded, quick),
                    readyIn = state != null ? Mathf.Max(0f, state.readyAt[(int)ability] - Time.time) : 0f,
                    duration = stats.duration,
                    activeLeft = state != null ? Mathf.Max(0f, state.activeUntil[(int)ability] - Time.time) : 0f,
                };
            }
            InstanceFinder.ServerManager.Broadcast(c, new HS_AbilityStateBroadcast { round = round, slots = slots, tauntAllowance = TauntAllowance(c) });
        }
    }
}
