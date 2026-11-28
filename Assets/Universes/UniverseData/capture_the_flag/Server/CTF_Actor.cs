using System.Collections.Generic;
using FishNet.Connection;
using RyanAssets.Characters.Shared;

namespace Universes.UniverseData.capture_the_flag.Server {
    /// <summary>
    /// One combatant in a match, a player or a bot: everything the flag, kill and medal rules need to
    /// treat both alike. Players keep theirs from join to leave (their bodies come and go); a bot slot
    /// keeps its id, name and gun across lives.
    /// </summary>
    public sealed class CTF_Actor {
        /// <summary>The actor id on the wire: the client id for a player, <see cref="CTF_Rules.BotId"/> for a bot.</summary>
        public readonly int Id;
        /// <summary>The player's connection; null for a bot.</summary>
        public readonly NetworkConnection Connection;
        /// <summary>The bot slot index; -1 for a player.</summary>
        public readonly int BotSlot;

        public CTF_Team Team;
        /// <summary>The current body (null between lives).</summary>
        public GameCharacter Character;
        /// <summary>Name for feeds and banners (a bot's call-sign; a player's is read live, since it can change).</summary>
        public string BotName;
        /// <summary>A bot's gun for the match.</summary>
        public CTF_Weapon BotWeapon;
        /// <summary>A bot's job on its team (re-dealt when the team changes).</summary>
        public CTF_BotRole Role;
        /// <summary>A bot slot no longer needed (a player took its place): it leaves when its body dies or is cleared.</summary>
        public bool Retired;
        /// <summary>Bot: the scheduled respawn time (Time.time), or 0 when none is pending.</summary>
        public float RespawnAt;

        public readonly CTF_MedalTracker Medals = new();
        public CTF_MatchStats Stats;
        /// <summary>Actor id of whoever last killed this one (-1 = nobody yet), for Payback.</summary>
        public int LastKillerId = -1;
        /// <summary>The gun and perk this life was spawned with.</summary>
        public CTF_Weapon LiveWeapon;
        public CTF_Perk LivePerk;
        /// <summary>Who hurt this life and when (Time.time), newest last, for assists.</summary>
        public readonly List<(int attackerId, float time)> RecentAttackers = new();
        /// <summary>A running speed power-up ends at this Time.time (0 = none).</summary>
        public float BoostedUntil;
        /// <summary>How much of the match had gone when this player was dealt in (0 = from the start), for <see cref="CTF_Rules.WinCounts"/>.</summary>
        public float JoinedAtShare;

        CTF_Actor(int id, NetworkConnection connection, int botSlot, CTF_Team team) {
            Id = id;
            Connection = connection;
            BotSlot = botSlot;
            Team = team;
        }

        public static CTF_Actor Player(NetworkConnection connection, CTF_Team team) => new(connection.ClientId, connection, -1, team);

        public static CTF_Actor Bot(int slot, CTF_Team team, string name, CTF_Weapon weapon) =>
            new(CTF_Rules.BotId(slot), null, slot, team) { BotName = name, BotWeapon = weapon, LiveWeapon = weapon };

        public bool IsBot => Connection == null;
        public bool Boosted(float now) => now < BoostedUntil;

        /// <summary>Remembers that <paramref name="attackerId"/> hurt this life at <paramref name="now"/> (one entry per attacker).</summary>
        public void NoteAttacker(int attackerId, float now) {
            for (int i = RecentAttackers.Count - 1; i >= 0; i--)
                if (RecentAttackers[i].attackerId == attackerId) RecentAttackers.RemoveAt(i);
            RecentAttackers.Add((attackerId, now));
        }

        /// <summary>A new life: nobody has hurt it yet.</summary>
        public void NewLife() => RecentAttackers.Clear();
        public bool IsAlive => Character != null && Character.IsSpawned && !Character.IsDead;

        /// <summary>Starts a match: stats, streaks and the payback target begin again.</summary>
        public void ResetMatch() {
            Stats = default;
            Medals.Reset();
            LastKillerId = -1;
            RecentAttackers.Clear();
            BoostedUntil = 0f;
        }

        public override string ToString() => IsBot ? $"bot {BotName} ({Team})" : $"player {Id} ({Team})";
    }
}
