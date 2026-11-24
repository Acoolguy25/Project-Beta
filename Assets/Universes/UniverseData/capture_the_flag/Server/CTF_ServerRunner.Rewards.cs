using System;
using System.Collections.Generic;
using FishNet.Connection;
using RyanAssets.Retention;
using RyanAssets.Rounds;
using RyanAssets.DataService;
using RyanAssets.Server.ServerCore;
using FishNet;
using UnityEngine;

namespace Universes.UniverseData.capture_the_flag.Server {
    // XP, challenge stats, saved counters and the match-end summary. Every reward goes through the
    // retention kit (it pays account XP and gold itself), so nothing here calls LevelsServer.
    public sealed partial class CTF_ServerRunner {
        [Header("Rewards (Flag Wars XP; account XP and gold follow the retention tuning)")]
        [SerializeField, Min(0)] int killXp = CTF_RewardDefaults.KillXp;
        [SerializeField, Min(0)] int flagTakeXp = CTF_RewardDefaults.FlagTakeXp;
        [SerializeField, Min(0)] int captureXp = CTF_RewardDefaults.CaptureXp;
        [SerializeField, Min(0)] int returnXp = CTF_RewardDefaults.ReturnXp;
        [SerializeField, Min(0)] int winXp = CTF_RewardDefaults.WinXp, winGold = CTF_RewardDefaults.WinGold;
        [SerializeField, Min(0)] int lossXp = CTF_RewardDefaults.LossXp, lossGold = CTF_RewardDefaults.LossGold;
        [SerializeField, Min(0)] int drawXp = CTF_RewardDefaults.DrawXp, drawGold = CTF_RewardDefaults.DrawGold;
        [Tooltip("Gold per flag the player captured, paid on the summary.")]
        [SerializeField, Min(0)] int captureGold = CTF_RewardDefaults.CaptureGold;

        const string KillsReason = "Eliminations";
        const string FlagTakesReason = "Flag grabs";
        const string CapturesReason = "Flag captures";
        const string ReturnsReason = "Flag returns";

        readonly List<RewardLine> summaryLines = new();

        void KillRewards(CTF_Actor killer, bool carrierKill, bool defend, bool multi) {
            if (killer.IsBot) return;
            var c = killer.Connection;
            if (PlayerData.TryGetPlayerData(c, out var p)) RoundLeaderboard.Add(p, KillsColumn, 1);
            // A loaned gun's day: its eliminations pay more.
            retention.AddXp(c, CTF_Daily.KillXp(killXp, killer.LiveWeapon, twist, daily.xpMultiplier), KillsReason);
            retention.Count(c, CTF_Retention.StatKills);
            retention.Reach(c, CTF_Retention.StatKillStreak, killer.Medals.Streak);
            if (multi) retention.Count(c, CTF_Retention.StatMultiKills);
            if (carrierKill) retention.Count(c, CTF_Retention.StatCarrierKills);
            if (defend) retention.Count(c, CTF_Retention.StatDefends);
            int streak = killer.Medals.Streak;
            int gun = (int)killer.LiveWeapon;
            PlayerProgress.Modify<CTF_SaveData>(c, save => {
                save.kills++;
                if (carrierKill) save.carrierKills++;
                save.bestStreak = Math.Max(save.bestStreak, streak);
                if (save.gunKills != null && gun >= 0 && gun < save.gunKills.Length) save.gunKills[gun]++;
            });
        }

        /// <summary>Whether <paramref name="actor"/> (a player) has mastered the gun in hand.</summary>
        bool Mastered(CTF_Actor actor) {
            if (actor == null || actor.IsBot || !PlayerProgress.TryGet<CTF_SaveData>(actor.Connection, out var save) || save.gunKills == null) return false;
            int gun = (int)actor.LiveWeapon;
            return gun >= 0 && gun < save.gunKills.Length && CTF_Goals.Mastered(save.gunKills[gun]);
        }

        /// <summary>The player's career for the standing goals (the card beside J, and the summary's next goal at the cap).</summary>
        CTF_CareerBroadcast Career(NetworkConnection c) {
            var career = new CTF_CareerBroadcast { level = retention.LevelOf(c), maxLevel = CTF_Retention.MaxLevel };
            if (!PlayerProgress.TryGet<CTF_SaveData>(c, out var save)) return career;
            career.wins = save.wins;
            career.mvps = save.mvps;
            career.bestStreak = save.bestStreak;
            career.captures = save.captures;
            career.matches = save.matchesPlayed;
            career.kills = save.kills;
            career.gunKills = save.gunKills != null ? (int[])save.gunKills.Clone() : new int[CTF_Loadouts.Weapons.Length];
            return career;
        }

        void SendCareer(NetworkConnection c) {
            if (c != null && c.IsActive) InstanceFinder.ServerManager.Broadcast(c, Career(c));
        }

        void MedalRewards(CTF_Actor actor, CTF_Medal medal, int xp) {
            var c = actor.Connection;
            // The medal popup shows the XP; the chip's "+XP" would say it twice.
            retention.AddXp(c, xp, CTF_MedalTracker.Get(medal).title, announce: false);
            retention.Count(c, CTF_Retention.StatMedals);
            PlayerProgress.Modify<CTF_SaveData>(c, save => save.medals++);
        }

        /// <summary>
        /// A flag was picked up. <paramref name="pays"/> is false for a pickup of a dropped flag that was
        /// already paid for since it left its stand (try 1: 56 paid grabs in one match).
        /// </summary>
        void FlagTaken(CTF_Actor actor, bool pays) {
            if (phase != CTF_Phase.Playing) return;
            FieldMedic(actor);
            if (!pays) return;
            actor.Stats.flagTakes++;
            if (actor.IsBot) return;
            retention.AddXp(actor.Connection, CTF_Daily.FlagXp(flagTakeXp, twist, daily.xpMultiplier), FlagTakesReason);
            retention.Count(actor.Connection, CTF_Retention.StatFlagTakes);
        }

        void FlagReturned(CTF_Actor actor) {
            actor.Stats.returns++;
            RosterDirty();
            FieldMedic(actor);
            AwardMedal(actor, CTF_Medal.FlagSaved);
            if (actor.IsBot) return;
            retention.AddXp(actor.Connection, returnXp, ReturnsReason);
            retention.Count(actor.Connection, CTF_Retention.StatReturns);
            PlayerProgress.Modify<CTF_SaveData>(actor.Connection, save => save.returns++);
        }

        void FlagCaptured(CTF_Actor carrier) {
            if (carrier == null) return;
            carrier.Stats.captures++;
            RosterDirty();
            FieldMedic(carrier);
            if (carrier.Stats.captures == medalTuning.hatTrickCaptures) AwardMedal(carrier, CTF_Medal.HatTrick);
            if (carrier.IsBot) return;
            var c = carrier.Connection;
            if (PlayerData.TryGetPlayerData(c, out var p)) RoundLeaderboard.Add(p, CapturesColumn, 1);
            retention.AddXp(c, CTF_Daily.FlagXp(captureXp, twist, daily.xpMultiplier), CapturesReason);
            retention.Count(c, CTF_Retention.StatCaptures);
            PlayerProgress.Modify<CTF_SaveData>(c, save => save.captures++);
        }

        void PowerUpTaken(CTF_Actor actor) {
            if (actor.IsBot) return;
            retention.Count(actor.Connection, CTF_Retention.StatPowerUps);
            PlayerProgress.Modify<CTF_SaveData>(actor.Connection, save => save.powerUps++);
        }

        /// <summary>
        /// The match is over: picks the MVP (players and bots alike), announces the result, and closes
        /// every player's match on the retention summary with their result, captures and line stats.
        /// </summary>
        void FinishMatch(CTF_Team? winner) {
            var stats = new List<CTF_MatchStats>(actors.Count);
            var winning = new List<bool>(actors.Count);
            foreach (var actor in actors) {
                stats.Add(actor.Stats);
                winning.Add(winner is CTF_Team team && actor.Team == team);
            }
            int mvpIndex = CTF_Scoring.Mvp(stats, winning, mvpWeights);
            CTF_Actor mvp = mvpIndex >= 0 ? actors[mvpIndex] : null;
            if (mvp != null) {
                AwardMedal(mvp, CTF_Medal.Mvp);
                if (!mvp.IsBot) PlayerProgress.Modify<CTF_SaveData>(mvp.Connection, save => save.mvps++);
            }

            // Each player's own result first, so the banner can say a late joiner's win does not count.
            foreach (var actor in players.Values) {
                if (!actor.Connection.IsActive) continue;
                bool counts = WinCountsFor(actor);
                InstanceFinder.ServerManager.Broadcast(actor.Connection, new CTF_ResultNoteBroadcast {
                    lateJoin = actor.JoinedAtShare > lateJoinWinShare, winCounts = counts,
                });
            }

            var result = new CTF_EventBroadcast {
                kind = winner.HasValue ? CTF_EventKind.Won : CTF_EventKind.Draw,
                flag = winner ?? CTF_Team.Red,
                playerName = "",
                playerId = -1,
                redScore = redScore, blueScore = blueScore,
                hasMvp = mvp != null,
                mvpId = mvp?.Id ?? -1,
                mvpName = NameOf(mvp),
                mvpTeam = mvp?.Team ?? CTF_Team.Red,
                mvpKills = mvp?.Stats.kills ?? 0,
                mvpCaptures = mvp?.Stats.captures ?? 0,
                mvpReturns = mvp?.Stats.returns ?? 0,
            };
            // The final board first, so the result card's roster is complete.
            BroadcastRoster(null);
            InstanceFinder.ServerManager.Broadcast(result);
            BroadcastState();

            Debug.Log($"[FlagWars] match over: Red {redScore} - {blueScore} Blue; bots Red {squad.SkillOf(CTF_Team.Red)} x{BotCount(CTF_Team.Red)}, " +
                      $"Blue {squad.SkillOf(CTF_Team.Blue)} x{BotCount(CTF_Team.Blue)}; players Red {Humans(CTF_Team.Red)}, Blue {Humans(CTF_Team.Blue)}" +
                      $"{(BotsOnly ? " (QA bots only)" : "")}.", this);
            // The QA baseline's players sat in the lobby: nothing to pay or record.
            if (BotsOnly) return;

            foreach (var actor in players.Values) {
                var c = actor.Connection;
                if (!c.IsActive) continue;
                int margin = actor.Team == CTF_Team.Red ? redScore - blueScore : blueScore - redScore;
                float level = retention.LevelOf(c);
                bool teamWon = winner is CTF_Team team && actor.Team == team;
                bool won = teamWon && WinCountsFor(actor);
                bool draw = !winner.HasValue;
                if (won) retention.Count(c, CTF_Retention.StatWins);
                retention.Count(c, CTF_Retention.StatMatches);
                int bestStreak = actor.Medals.BestStreak;
                PlayerProgress.Modify<CTF_SaveData>(c, save => {
                    save.matchesPlayed++;
                    if (won) save.wins++;
                    save.bestStreak = Math.Max(save.bestStreak, bestStreak);
                    // Results move the bots' tier for next time (level only caps it).
                    save.botTier = squad.NextTier(save.botTier, margin, level);
                    save.lossStreak = CTF_BotRules.NextLossStreak(save.lossStreak, margin);
                    save.lastMargin = margin;
                });
                var outcome = CTF_Summary.Outcome(teamWon, won, draw);
                Debug.Log($"[FlagWars] result for {actor}: {outcome} (joined at {actor.JoinedAtShare:P0}, flag plays {FlagPlays(actor)}), score {redScore}-{blueScore}.", this);
                retention.FinishMatch(c, CTF_Summary.Title(outcome), SummaryLines(actor, outcome, actor == mvp));
                SendCareer(c);
            }
        }

        int BotCount(CTF_Team team) {
            int n = 0;
            foreach (var bot in squad.Bots) if (bot.Team == team) n++;
            return n;
        }

        static int FlagPlays(CTF_Actor actor) => actor.Stats.flagTakes + actor.Stats.captures + actor.Stats.returns;

        bool WinCountsFor(CTF_Actor actor) => CTF_Rules.WinCounts(actor.JoinedAtShare, FlagPlays(actor) > 0, lateJoinWinShare);

        readonly List<CTF_Goal> goalScratch = new();

        List<RewardLine> SummaryLines(CTF_Actor actor, CTF_Outcome outcome, bool mvp) {
            var s = actor.Stats;
            var pay = new CTF_SummaryPay {
                winXp = winXp, winGold = winGold, lossXp = lossXp, lossGold = lossGold, drawXp = drawXp, drawGold = drawGold,
                captureGold = captureGold, mvpTitle = CTF_MedalTracker.Get(CTF_Medal.Mvp).title,
            };
            var c = actor.Connection;
            // At the cap the card has no next level to fill: it names the standing goal nearest done instead.
            string nextGoal = retention.LevelOf(c) >= CTF_Retention.MaxLevel ? CTF_Goals.Nearest(Career(c), goalScratch).ToString() : null;
            var input = new CTF_SummaryInput {
                outcome = outcome, redScore = redScore, blueScore = blueScore, mvp = mvp,
                captures = s.captures, kills = s.kills, deaths = s.deaths, joinedAtShare = actor.JoinedAtShare,
                twistBonusXp = CTF_Daily.ResultBonusXp(CTF_Summary.ResultXp(outcome, pay), twist, daily.toughBotsResultBonus),
                twistTitle = CTF_Daily.Title(twist), nextGoal = nextGoal,
            };
            CTF_Summary.Lines(input, pay, summaryLines);
            return summaryLines;
        }
    }
}
