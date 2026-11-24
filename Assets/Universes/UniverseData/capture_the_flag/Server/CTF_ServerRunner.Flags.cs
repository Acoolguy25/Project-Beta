using System;
using FishNet;
using RyanAssets.DataService;
using RyanAssets.Server.ServerCore;
using UnityEngine;

namespace Universes.UniverseData.capture_the_flag.Server {
    // Flags, power-ups and speed: the same rules for players and bots.
    public sealed partial class CTF_ServerRunner {
        sealed class Flag {
            public CTF_Team team;
            public CTF_FlagState state;
            public Vector3 position;
            public CTF_Actor carrier;
            public float droppedAt;
            /// <summary>Pays a grab once per trip off the stand.</summary>
            public CTF_GrabPay pay;
        }

        readonly Flag[] flags = { new() { team = CTF_Team.Red }, new() { team = CTF_Team.Blue } };
        uint powerUpsReady;
        float[] powerUpReadyAt = Array.Empty<float>();

        void UpdateFlag(Flag flag) {
            if (flag.state == CTF_FlagState.Carried) {
                var carrier = flag.carrier;
                if (carrier == null || !carrier.IsAlive) {
                    Drop(flag, flag.position);
                    return;
                }
                flag.position = carrier.Character.transform.position;
                // Score: carrier reaches their own stand while their own flag is home.
                var carrierTeam = CTF_Rules.Other(flag.team);
                if (flags[(int)carrierTeam].state == CTF_FlagState.Home && CTF_Rules.InReach(flag.position, map.Stand(carrierTeam), CTF_Rules.CaptureRange))
                    Capture(flag, carrierTeam);
                return;
            }
            if (flag.state == CTF_FlagState.Dropped && Time.time - flag.droppedAt > CTF_Rules.DropReturnSeconds) {
                Return(flag, true);
                return;
            }
            foreach (var actor in actors) {
                if (!actor.IsAlive || actor.Retired) continue;
                // Guardian reaches its own dropped flag from further away.
                float reach = actor.Team == flag.team ? loadoutTuning.ReturnReach(actor.LivePerk) : CTF_Rules.PickupRange;
                if (!CTF_Rules.InReach(actor.Character.transform.position, flag.position, reach)) continue;
                if (actor.Team != flag.team) {
                    // A grab off the stand gets a head start: the defenders respawn beside the stand.
                    if (flag.state == CTF_FlagState.Home && grabHeadStartSeconds > 0f)
                        actor.BoostedUntil = Math.Max(actor.BoostedUntil, Time.time + grabHeadStartSeconds);
                    flag.state = CTF_FlagState.Carried;
                    flag.carrier = actor;
                    RefreshSpeed(actor);
                    FlagTaken(actor, flag.pay.Grab());
                    Event(CTF_EventKind.Taken, flag.team, actor);
                    return;
                }
                if (flag.state == CTF_FlagState.Dropped) {
                    // Guardian: show it when the longer reach made the return.
                    if (actor.LivePerk == CTF_Perk.Guardian && !CTF_Rules.InReach(actor.Character.transform.position, flag.position, CTF_Rules.PickupRange))
                        PerkFired(actor, CTF_Perk.Guardian, 0f);
                    FlagReturned(actor);
                    Event(CTF_EventKind.Returned, flag.team, actor);
                    Return(flag, false);
                    return;
                }
            }
        }

        void Capture(Flag flag, CTF_Team scorer) {
            var carrier = flag.carrier;
            if (scorer == CTF_Team.Red) redScore++; else blueScore++;
            FlagCaptured(carrier);
            Event(CTF_EventKind.Captured, flag.team, carrier);
            Return(flag, false);
        }

        void Drop(Flag flag, Vector3 at) {
            if (flag.state != CTF_FlagState.Carried) return;
            var carrier = flag.carrier;
            flag.carrier = null;
            if (carrier != null) RefreshSpeed(carrier);
            if (map != null && at.y < map.killY + 2f) { Return(flag, true); return; }
            flag.state = CTF_FlagState.Dropped;
            flag.position = at;
            flag.droppedAt = Time.time;
            Event(CTF_EventKind.Dropped, flag.team, carrier);
        }

        /// <summary>Drops whatever <paramref name="actor"/> carries, where it stands (or where the flag was last seen).</summary>
        void DropFlagsOf(CTF_Actor actor, Vector3? at) {
            foreach (var flag in flags)
                if (flag.state == CTF_FlagState.Carried && flag.carrier == actor) Drop(flag, at ?? flag.position);
        }

        void Return(Flag flag, bool announce) {
            var carrier = flag.carrier;
            flag.state = CTF_FlagState.Home;
            flag.carrier = null;
            flag.pay.Home();
            if (carrier != null) RefreshSpeed(carrier);
            if (map != null) flag.position = map.Stand(flag.team);
            if (announce) Event(CTF_EventKind.Returned, flag.team, null);
        }

        bool IsCarrying(CTF_Actor actor) {
            foreach (var flag in flags)
                if (flag.state == CTF_FlagState.Carried && flag.carrier == actor) return true;
            return false;
        }

        /// <summary>
        /// Applies the carrier slow-down, a running speed boost and the perk to a player's walk/sprint
        /// speed. Bots read <see cref="SpeedMultiplier"/> themselves every step.
        /// </summary>
        void RefreshSpeed(CTF_Actor actor) {
            if (actor?.Connection == null || !PlayerData.TryGetPlayerData(actor.Connection, out var player)) return;
            float m = SpeedMultiplier(actor);
            player.walkSpeed.Value = walkSpeed * m;
            player.sprintSpeed.Value = sprintSpeed * m;
        }

        // ---------------------------------------------------------------- power-ups
        void ResetPowerUps() {
            int count = map.powerUps.Length;
            powerUpReadyAt = new float[count];
            powerUpsReady = 0;
            for (int i = 0; i < count; i++) powerUpsReady = CTF_Rules.WithReady(powerUpsReady, i, true);
            ClearBoosts();
        }

        void UpdatePowerUps() {
            var spots = map.powerUps;
            for (int i = 0; i < spots.Length; i++) {
                if (!CTF_Rules.IsReady(powerUpsReady, i)) {
                    if (Time.time >= powerUpReadyAt[i]) powerUpsReady = CTF_Rules.WithReady(powerUpsReady, i, true);
                    continue;
                }
                if (spots[i] == null) continue;
                foreach (var actor in actors) {
                    if (!actor.IsAlive) continue;
                    if (!CTF_Rules.InReach(actor.Character.transform.position, spots[i].Position, CTF_Rules.PowerUpRange)) continue;
                    if (!TryApply(spots[i].kind, actor)) continue;
                    powerUpsReady = CTF_Rules.WithReady(powerUpsReady, i, false);
                    powerUpReadyAt[i] = Time.time + CTF_Daily.PowerUpRespawn(powerUpRespawnSeconds, twist, daily.powerSurgeRespawnScale);
                    PowerUpTaken(actor);
                    InstanceFinder.ServerManager.Broadcast(new CTF_PowerUpBroadcast {
                        index = i, kind = spots[i].kind, playerId = actor.Id,
                        seconds = spots[i].kind == CTF_PowerUpKind.Speed ? speedBoostSeconds : 0f
                    });
                    BroadcastState();
                    break;
                }
            }
        }

        bool TryApply(CTF_PowerUpKind kind, CTF_Actor actor) {
            var body = actor.Character;
            switch (kind) {
                case CTF_PowerUpKind.Health: {
                    long heal = CTF_Rules.HealAmount(body.Health.Value, body.MaxHealth.Value, healAmount);
                    if (heal <= 0) return false; // full health: leave it for someone who needs it
                    body.HealHealth(heal);
                    return true;
                }
                case CTF_PowerUpKind.Speed:
                    actor.BoostedUntil = Time.time + speedBoostSeconds;
                    RefreshSpeed(actor);
                    return true;
                default:
                    Debug.LogWarning($"Flag Wars has no rule for power-up kind {kind}.", this);
                    return false;
            }
        }

        void ExpireBoosts() {
            float now = Time.time;
            foreach (var actor in actors) {
                if (actor.BoostedUntil <= 0f || now < actor.BoostedUntil) continue;
                actor.BoostedUntil = 0f;
                RefreshSpeed(actor);
            }
        }

        void ClearBoosts() {
            foreach (var actor in actors) {
                if (actor.BoostedUntil <= 0f) continue;
                actor.BoostedUntil = 0f;
                RefreshSpeed(actor);
            }
        }

        void Event(CTF_EventKind kind, CTF_Team flag, CTF_Actor actor) {
            InstanceFinder.ServerManager.Broadcast(new CTF_EventBroadcast {
                kind = kind, flag = flag,
                playerName = NameOf(actor),
                playerId = actor != null ? actor.Id : -1,
                redScore = redScore, blueScore = blueScore
            });
            BroadcastState();
        }
    }
}
