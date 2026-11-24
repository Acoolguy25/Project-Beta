using System;
using System.Collections.Generic;
using FishNet;
using FishNet.Connection;
using RyanAssets.Characters.Shared;
using RyanAssets.Retention.Server;
using RyanAssets.Server.ServerCore;
using RyanAssets.Shared.Declarations;
using RyanAssets.Tools.Shared;
using UnityEngine;
using Channel = FishNet.Transporting.Channel;

namespace Universes.UniverseData.capture_the_flag.Server {
    /// <summary>
    /// Players' guns and perks. A pick is made with G (gun) and H (perk) through
    /// <see cref="CTF_LoadoutRequest"/>, only from what the player's Flag Wars level has unlocked
    /// (<see cref="RetentionService{TSave}.IsUnlocked"/>), and saved. It is worn from the next life;
    /// before the match starts it is swapped at once, so a player can try each gun in the lobby.
    /// </summary>
    public sealed class CTF_LoadoutService {
        struct Pick {
            public CTF_Weapon weapon;
            public CTF_Perk perk;
            public float lastRequest;
        }

        readonly CTF_LoadoutTuning tuning;
        readonly RetentionService<CTF_SaveData> retention;
        readonly Dictionary<NetworkConnection, Pick> picks = new();

        public CTF_LoadoutService(CTF_LoadoutTuning tuning, RetentionService<CTF_SaveData> retention) {
            this.tuning = tuning ?? new CTF_LoadoutTuning();
            this.retention = retention ?? throw new ArgumentNullException(nameof(retention));
        }

        public CTF_LoadoutTuning Tuning => tuning;

        /// <summary>Today's twist: a gun it lends counts as owned by everyone (set by the runner each match).</summary>
        public CTF_DailyTwist Twist { get; set; }

        byte OwnedWeapons(NetworkConnection c) => CTF_Daily.WithLoan(CTF_Loadouts.OwnedWeapons(id => retention.IsUnlocked(c, id)), Twist);
        byte OwnedPerks(NetworkConnection c) => CTF_Loadouts.OwnedPerks(id => retention.IsUnlocked(c, id));

        /// <summary>The player's level is known: read their saved pick, keep only what they own, tell them.</summary>
        public void Ready(CTF_Actor actor) {
            if (actor?.Connection == null) return;
            var connection = actor.Connection;
            var pick = new Pick();
            if (PlayerProgress.TryGet<CTF_SaveData>(connection, out var save)) {
                pick.weapon = CTF_Loadouts.WeaponById(save.weapon);
                pick.perk = CTF_Loadouts.PerkById(save.perk);
            }
            picks[connection] = Owned(connection, pick);
            Send(actor, false, CTF_LoadoutSlot.Weapon);
        }

        /// <summary>Sends the pick and what is owned again (a level-up may have unlocked something; a client asked).</summary>
        public void Refresh(CTF_Actor actor) {
            if (actor?.Connection == null) return;
            Send(actor, false, CTF_LoadoutSlot.Weapon);
        }

        public void Left(NetworkConnection connection) => picks.Remove(connection);

        Pick Owned(NetworkConnection connection, Pick pick) {
            if (!CTF_Loadouts.Has(OwnedWeapons(connection), (int)pick.weapon)) pick.weapon = CTF_Weapon.Sidearm;
            if (!CTF_Loadouts.Has(OwnedPerks(connection), (int)pick.perk)) pick.perk = CTF_Perk.None;
            return pick;
        }

        Pick PickOf(NetworkConnection connection) => picks.TryGetValue(connection, out var pick) ? Owned(connection, pick) : default;

        /// <summary>
        /// Moves the player's pick one owned step in <paramref name="request"/>'s slot and saves it.
        /// Returns true when it changed; the caller re-arms at once when the match has not started.
        /// </summary>
        public bool Request(CTF_Actor actor, CTF_LoadoutRequest request) {
            if (actor?.Connection == null) return false;
            var connection = actor.Connection;
            if (!picks.TryGetValue(connection, out var pick)) return false; // level still loading
            float now = Time.unscaledTime;
            if (now - pick.lastRequest < tuning.requestCooldown) return false;
            pick.lastRequest = now;
            pick = Owned(connection, pick);
            int step = request.step < 0 ? -1 : 1;
            bool changed;
            if (request.slot == CTF_LoadoutSlot.Perk) {
                var next = (CTF_Perk)CTF_Loadouts.Cycle((int)pick.perk, step, OwnedPerks(connection), CTF_Loadouts.Perks.Length);
                changed = next != pick.perk;
                pick.perk = next;
            } else {
                var next = (CTF_Weapon)CTF_Loadouts.Cycle((int)pick.weapon, step, OwnedWeapons(connection), CTF_Loadouts.Weapons.Length);
                changed = next != pick.weapon;
                pick.weapon = next;
            }
            picks[connection] = pick;
            if (changed) {
                string weaponId = CTF_Loadouts.Get(pick.weapon).id, perkId = CTF_Loadouts.Get(pick.perk).id;
                PlayerProgress.Modify<CTF_SaveData>(connection, save => {
                    save.weapon = weaponId;
                    save.perk = perkId;
                });
            }
            Send(actor, changed, request.slot);
            return changed;
        }

        /// <summary>
        /// Arms a player's body with their pick: tunes its pistol tool, notes what this life wears and
        /// sends the live stats to the player's client (which fires the gun).
        /// </summary>
        public void Arm(CTF_Actor actor) {
            if (actor?.Connection == null || actor.Character == null) return;
            var pick = PickOf(actor.Connection);
            actor.LiveWeapon = pick.weapon;
            actor.LivePerk = pick.perk;
            ArmBody(actor.Character, tuning.StatsFor(pick.weapon, pick.perk));
            Send(actor, false, CTF_LoadoutSlot.Weapon);
        }

        /// <summary>Tunes the pistol tool on <paramref name="body"/> (players and bots); returns it, or null if it has none yet.</summary>
        public static ToolBaseShared ArmBody(GameCharacter body, in CTF_GunStats stats) {
            if (body == null || ServerTool.Instance == null) return null;
            var pistol = ServerTool.Instance.GetTool(body.NetworkObject, ToolEnum.Pistol);
            if (pistol == null) return null;
            CTF_GunSetup.ApplyReplicated(pistol, stats);
            // The server keeps the pattern too, so its own range checks agree with the shooter's.
            CTF_GunSetup.ApplyFiringPattern(pistol as ToolGunShared, stats, refill: true);
            return pistol;
        }

        void Send(CTF_Actor actor, bool changed, CTF_LoadoutSlot slot) {
            var connection = actor.Connection;
            if (connection == null || !connection.IsActive) return;
            var pick = PickOf(connection);
            InstanceFinder.ServerManager.Broadcast(connection, new CTF_LoadoutBroadcast {
                weapon = pick.weapon,
                perk = pick.perk,
                liveWeapon = actor.LiveWeapon,
                livePerk = actor.LivePerk,
                liveStats = tuning.StatsFor(actor.LiveWeapon, actor.LivePerk),
                ownedWeapons = OwnedWeapons(connection),
                ownedPerks = OwnedPerks(connection),
                changed = changed,
                changedSlot = slot,
                twist = Twist,
                weaponLoaned = !CTF_Loadouts.Has(CTF_Loadouts.OwnedWeapons(id => retention.IsUnlocked(connection, id)), (int)pick.weapon),
            }, true, Channel.Reliable);
        }
    }
}
