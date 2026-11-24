using System;
using RyanAssets.Progress;
using RyanAssets.Retention;

namespace Universes.UniverseData.capture_the_flag.Server {
    /// <summary>
    /// What Flag Wars remembers about a player between sessions. Written only by the server. New
    /// counters were added without a version bump: missing fields load as their initializers.
    /// </summary>
    public sealed class CTF_SaveData : ProgressData {
        public const string Universe = CTF_Retention.Universe;
        public override int LatestVersion => 1;

        public int captures;
        public int kills;
        /// <summary>Games the player's team won.</summary>
        public int wins;
        /// <summary>Own dropped flags sent home by touching them.</summary>
        public int returns;
        /// <summary>Deaths during play (not in the lobby or results).</summary>
        public int deaths;
        /// <summary>Matches the player was on a team for when the results came.</summary>
        public int matchesPlayed;
        /// <summary>Health and speed power-ups taken.</summary>
        public int powerUps;

        // ---- retention pass (2026-11)
        /// <summary>Level, daily challenges and play streak (the shared retention kit).</summary>
        public RetentionSave retention = new();
        /// <summary>The gun picked for the next life (<see cref="CTF_WeaponDefinition.id"/>); kept only while owned.</summary>
        public string weapon = CTF_Loadouts.Get(CTF_Weapon.Sidearm).id;
        /// <summary>The perk picked (<see cref="CTF_PerkDefinition.id"/>).</summary>
        public string perk = CTF_Loadouts.Get(CTF_Perk.None).id;
        /// <summary>Medals earned (lifetime).</summary>
        public int medals;
        /// <summary>Matches finished as MVP.</summary>
        public int mvps;
        /// <summary>Longest kill streak in one life.</summary>
        public int bestStreak;
        /// <summary>Enemies eliminated while they carried your flag.</summary>
        public int carrierKills;

        // ---- retention pass try 2 (bot tier by results)
        /// <summary>The bots' tier this player has earned (<see cref="CTF_BotSkill"/>): up after a clear win, down after a clear loss, capped by level.</summary>
        public int botTier;
        /// <summary>Matches lost in a row (a draw leaves it as it was).</summary>
        public int lossStreak;
        /// <summary>The last match's capture margin for the player's team (+ = won).</summary>
        public int lastMargin;
        /// <summary>Eliminations with each gun, in <see cref="CTF_Weapon"/> order (gun mastery, a standing goal).</summary>
        public int[] gunKills = new int[CTF_Loadouts.Weapons.Length];

        public override void Validate() {
            captures = Math.Max(0, captures);
            kills = Math.Max(0, kills);
            wins = Math.Max(0, wins);
            returns = Math.Max(0, returns);
            deaths = Math.Max(0, deaths);
            matchesPlayed = Math.Max(0, matchesPlayed);
            powerUps = Math.Max(0, powerUps);
            retention = RetentionSave.Repair(retention);
            // Unknown ids (a renamed or removed item) fall back to the defaults.
            weapon = CTF_Loadouts.Get(CTF_Loadouts.WeaponById(weapon)).id;
            perk = CTF_Loadouts.Get(CTF_Loadouts.PerkById(perk)).id;
            medals = Math.Max(0, medals);
            mvps = Math.Max(0, mvps);
            bestStreak = Math.Max(0, bestStreak);
            carrierKills = Math.Max(0, carrierKills);
            botTier = Math.Clamp(botTier, 0, CTF_BotRules.TierCount - 1);
            lossStreak = Math.Max(0, lossStreak);
            if (gunKills == null || gunKills.Length != CTF_Loadouts.Weapons.Length) {
                var resized = new int[CTF_Loadouts.Weapons.Length];
                if (gunKills != null) Array.Copy(gunKills, resized, Math.Min(gunKills.Length, resized.Length));
                gunKills = resized;
            }
            for (int i = 0; i < gunKills.Length; i++) gunKills[i] = Math.Max(0, gunKills[i]);
        }
    }
}
