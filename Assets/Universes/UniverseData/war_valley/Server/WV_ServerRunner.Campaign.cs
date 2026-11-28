using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using FishNet;
using FishNet.Connection;
using RyanAssets.DataService;
using RyanAssets.Retention;
using RyanAssets.Retention.Server;
using RyanAssets.Server.ServerCore;
using RyanAssets.Server.ServerFeatures;
using RyanAssets.Shared.Declarations;
using RyanAssets.Shared.Global;
using UnityEngine;
using Universes.UniverseData.war_valley.Shared;

namespace Universes.UniverseData.war_valley.Server {
    /// <summary>
    /// The runner's campaign half: the difficulty vote, doctrine hands, stars and records, and every
    /// reward routed through the retention kit (levels, unlocks, daily challenges, the summary card).
    /// </summary>
    public partial class WV_ServerRunner {
        [Header("Campaign")]
        [SerializeField]
        [Tooltip("The tiers a round can be played on, the Endless scaling and the star thresholds.")]
        private WV_DifficultyTuning DifficultyTuning = new();

        [SerializeField]
        [Tooltip("When doctrine hands are dealt and how big they are.")]
        private WV_DoctrineTuning DoctrineTuning = new();

        [SerializeField]
        [Tooltip("What a round pays on the summary card.")]
        private WV_CampaignRewards Rewards = new();

        [SerializeField]
        [Tooltip("The day's front: one rule change for every round of a UTC day.")]
        private WV_FrontTuning FrontTuning = new();

        [Header("Retention (levels, unlocks, daily challenges, streak)")]
        [SerializeField]
        [Tooltip("The level track ends at the last unlock (level 20), so no level is empty at the top.")]
        private RetentionTuning RetentionTuning = WV_Retention.DefaultTuning();

        private RetentionService<WV_SaveData> retention;
        private WV_ServerDoctrines serverDoctrines;
        private WV_DifficultyTier difficulty = WV_DifficultyTier.Recruit;
        /// <summary>True from the vote's result until the first wave: opening doctrine hands may be dealt.</summary>
        private bool preparing;
        /// <summary>True from the start of a round until its summary is sent.</summary>
        private bool roundLive;
        private bool difficultyChosen;
        /// <summary>When the last round's summary cards were sent, so the next vote waits until they are read.</summary>
        private float lastSummaryAt = float.NegativeInfinity;
        /// <summary>The lowest share of its health the flag was left with this round, for the stars.</summary>
        private float lowestFlagFraction = 1f;
        /// <summary>Kills and start bonuses this round, by player id like attendance, so a rejoin neither repeats nor loses them.</summary>
        private readonly Dictionary<string, int> killsThisRound = new();
        private readonly HashSet<string> startBonusGiven = new();
        /// <summary>Commanders who closed (or finished reading) the last round's summary, by player id.</summary>
        private readonly HashSet<string> summariesRead = new();
        /// <summary>Commanders the last round's summary was sent to, by player id.</summary>
        private readonly HashSet<string> summaryRecipients = new();
        private int nextBallotId = 1;
        /// <summary>Waves each commander was present for this round, by player id, so a rejoin keeps them.</summary>
        private readonly Dictionary<string, int> wavesPresent = new();
        private WV_Front front;
        private WV_FrontEffect frontEffect;

        private bool IsEndless => difficulty == WV_DifficultyTier.Endless;
        /// <summary>" - Veteran" on the top bar from Veteran up (Endless says so in its wave line), so the tier is always on screen.</summary>
        private string TierBadge => difficulty is WV_DifficultyTier.Recruit or WV_DifficultyTier.Endless ? string.Empty : $" - {Profile.DisplayName}";
        private WV_DifficultyProfile Profile => DifficultyTuning.Get(difficulty);
        /// <summary>The wave count clients are told: 0 for an Endless round, which has no last wave.</summary>
        private int AnnouncedWaveCount => IsEndless ? 0 : WaveTuning.WaveCount;

        // --- Setup -----------------------------------------------------------

        private void InitializeCampaign() {
            // The kit's daily clock follows the front's QA day offset, so a previewed day also has its challenges.
            retention = new RetentionService<WV_SaveData>(WV_Retention.Rules(RetentionTuning), save => save.retention,
                () => FrontTuning.Now(DateTime.UtcNow));
            retention.Ready += HandleRetentionReady;

            serverDoctrines = gameObject.AddComponent<WV_ServerDoctrines>();
            serverDoctrines.Initialize(DoctrineTuning, serverEconomy, IsUnlocked, PlayerKeyOf);
            serverDoctrines.Picked += HandleDoctrinePicked;
            serverTroops.TroopDelivered += HandleTroopDelivered;
            WV_ProductionBuilding.UnitProduced += HandleVehicleProduced;
            ServerStructure.StructurePlaced += HandleStructurePlacedForStats;
            if (InstanceFinder.ServerManager != null)
                InstanceFinder.ServerManager.RegisterBroadcast<WV_SummaryRead>(OnSummaryRead, true);
        }

        /// <summary>A commander pressed Continue on (or closed) their round summary.</summary>
        private void OnSummaryRead(NetworkConnection sender, WV_SummaryRead message, FishNet.Transporting.Channel channel) {
            string key = PlayerKey(sender);
            if (key != null)
                summariesRead.Add(key);
        }

        /// <summary>Whether every commander still here who was sent the last summary has finished with it.</summary>
        private bool AllSummariesRead() {
            foreach (NetworkConnection connection in Commanders()) {
                string key = PlayerKey(connection);
                if (key != null && summaryRecipients.Contains(key) && !summariesRead.Contains(key))
                    return false;
            }
            return true;
        }

        private void DisposeCampaign() {
            if (InstanceFinder.ServerManager != null)
                InstanceFinder.ServerManager.UnregisterBroadcast<WV_SummaryRead>(OnSummaryRead);
            WV_ProductionBuilding.UnitProduced -= HandleVehicleProduced;
            ServerStructure.StructurePlaced -= HandleStructurePlacedForStats;
            if (serverTroops != null)
                serverTroops.TroopDelivered -= HandleTroopDelivered;
            if (serverDoctrines != null)
                serverDoctrines.Picked -= HandleDoctrinePicked;
            if (retention != null) {
                retention.Ready -= HandleRetentionReady;
                retention.Dispose();
                retention = null;
            }
        }

        private void CampaignPlayerJoined(PlayerData player) {
            retention.PlayerJoined(player);
            // Joining a round under way: the summary card still covers the rest of it.
            if (roundLive)
                retention.BeginMatch(player.Owner);
            serverDoctrines.SendState(player.Owner);
        }

        /// <summary>The player's level is known: unlocks can be honoured and their campaign shown.</summary>
        private void HandleRetentionReady(NetworkConnection connection, int level) {
            SendCampaignStatus(connection);
            if (!roundLive)
                return;
            // A commander joining (or coming back to) a round under way: their doctrines back, and
            // the hands they missed, so a late joiner is not left a round's worth of picks behind.
            serverDoctrines.Join(connection.ClientId);
            if (difficultyChosen) {
                connection.Broadcast(Briefing());
                GiveStartBonus(connection);
            }
        }

        /// <summary>The player's saved id (the same across a rejoin), or the client id when there is none.</summary>
        private static string PlayerKey(NetworkConnection connection) {
            if (connection == null)
                return null;
            return PlayerData.Players != null && PlayerData.Players.TryGetValue(connection, out PlayerData player)
                   && player != null && !string.IsNullOrEmpty(player.player_id.Value)
                ? player.player_id.Value
                : "client:" + connection.ClientId;
        }

        private static string PlayerKeyOf(int clientId) =>
            TryGetConnection(clientId, out NetworkConnection connection) ? PlayerKey(connection) : null;

        private int WavesPresent(NetworkConnection connection) {
            string key = PlayerKey(connection);
            return key != null && wavesPresent.TryGetValue(key, out int waves) ? waves : 0;
        }

        private bool IsUnlocked(int clientId, string unlockId) =>
            TryGetConnection(clientId, out NetworkConnection connection) && retention.IsUnlocked(connection, unlockId);

        private static bool TryGetConnection(int clientId, out NetworkConnection connection) {
            connection = null;
            return clientId != WV_Owned.NoOwner
                   && InstanceFinder.ServerManager != null
                   && InstanceFinder.ServerManager.Clients.TryGetValue(clientId, out connection)
                   && connection != null && connection.IsValid;
        }

        private static IEnumerable<NetworkConnection> Commanders() {
            foreach (KeyValuePair<NetworkConnection, PlayerData> entry in PlayerData.Players) {
                if (entry.Key != null && entry.Key.IsValid)
                    yield return entry.Key;
            }
        }

        // --- Round start ------------------------------------------------------

        /// <summary>A new round: no doctrines, no stars lost yet, and every summary card opened.</summary>
        private void BeginCampaignRound() {
            difficulty = WV_DifficultyTier.Recruit;
            difficultyChosen = false;
            preparing = true;
            roundLive = true;
            lowestFlagFraction = 1f;
            killsThisRound.Clear();
            startBonusGiven.Clear();
            wavesPresent.Clear();
            serverDoctrines.BeginRound(UnityEngine.Random.Range(int.MinValue, int.MaxValue));
            // The day's front, read once a round so a round never changes front at midnight.
            front = WV_DailyFront.Today(FrontTuning.Now(DateTime.UtcNow));
            frontEffect = WV_DailyFront.Effect(front, FrontTuning);
            WV_CommanderBonuses.Baseline = frontEffect.Modifiers;
            serverDoctrines.Featured = frontEffect.Featured;
            foreach (NetworkConnection connection in Commanders())
                retention.BeginMatch(connection);
        }

        private void ResetCampaignRound() {
            roundLive = false;
            preparing = false;
            difficultyChosen = false;
        }

        /// <summary>
        /// Puts the round's tier to a vote among the tiers any present commander has unlocked. With
        /// only Recruit open there is nothing to vote on. No votes picks the easiest tier somebody
        /// present has not won yet; a tie picks the hardest of the tied tiers.
        /// </summary>
        private async UniTask ChooseDifficulty(CancellationToken token) {
            List<WV_DifficultyTier> offered = GetOfferedTiers(out int[] lowestStars);
            if (offered.Count <= 1) {
                difficulty = offered.Count == 1 ? offered[0] : WV_DifficultyTier.Recruit;
                return;
            }

            // The vote hides the HUD, the summary card with it: let the last round's card count up and
            // be read first, until every commander has pressed Continue on it. Commanders can already
            // build meanwhile.
            int wait = Mathf.CeilToInt(DifficultyTuning.VoteAfterSummarySeconds - (Time.time - lastSummaryAt));
            for (int left = wait; left > 0 && !AllSummariesRead(); left--) {
                SharedGlobalEvents.Instance.TopMessage = $"Choose the war in {left}";
                for (int quarter = 0; quarter < 4 && !AllSummariesRead(); quarter++) {
                    if (await AwaitTime(250, token))
                        break;
                    if (token.IsCancellationRequested)
                        return;
                }
            }
            if (token.IsCancellationRequested)
                return;

            var names = new string[offered.Count];
            var descriptions = new string[offered.Count];
            var xp = new short[offered.Count];
            var tiers = new byte[offered.Count];
            for (int i = 0; i < offered.Count; i++) {
                WV_DifficultyProfile profile = DifficultyTuning.Get(offered[i]);
                names[i] = profile.DisplayName;
                descriptions[i] = profile.Description;
                xp[i] = (short)Mathf.Round((profile.XpMultiplier - 1f) * 100f);
                tiers[i] = (byte)offered[i];
            }
            float seconds = Mathf.Max(3, DifficultyTuning.VoteSeconds);
            // War Valley's own tier panel draws the options from this; the shared vote counts them.
            BroadcastToAll(new WV_TierBallot {
                ballotId = nextBallotId++,
                tiers = tiers,
                names = names,
                descriptions = descriptions,
                xpBonusPercent = xp,
                seconds = seconds
            });

            VoteResult result;
            try {
                result = await ServerVote.StartDynamicVote("Choose the war",
                    "Harder tiers pay more XP. Win a tier to unlock the next.", names, descriptions,
                    seconds, token,
                    player => player != null && player.Owner != null && player.Owner.IsValid);
            } catch (OperationCanceledException) {
                return;
            }

            if (result.winner >= 0 && result.winner < offered.Count) {
                difficulty = offered[result.winner];
            } else if (result.tied && result.totals != null) {
                int top = result.TopVotes;
                for (int i = Mathf.Min(result.totals.Length, offered.Count) - 1; i >= 0; i--) {
                    if (result.totals[i] == top) {
                        difficulty = offered[i];
                        break;
                    }
                }
            } else {
                difficulty = WV_Campaign.DefaultChoice(offered, lowestStars);
            }
        }

        /// <summary>
        /// The tiers to vote on (<see cref="WV_Campaign.VoteOptions"/>, from every tier at least one present
        /// commander has unlocked), and per tier the fewest stars any of them has.
        /// </summary>
        private List<WV_DifficultyTier> GetOfferedTiers(out int[] lowestStars) {
            int mask = 1 << (int)WV_DifficultyTier.Recruit;
            lowestStars = new int[WV_Campaign.Tiers.Length];
            for (int i = 0; i < lowestStars.Length; i++)
                lowestStars[i] = WV_Campaign.MaxStars;
            bool anyone = false;
            foreach (NetworkConnection connection in Commanders()) {
                if (!PlayerProgress.TryGet(connection, out WV_SaveData save))
                    continue;
                anyone = true;
                mask |= WV_Campaign.UnlockedMask(save.tierStars);
                foreach (WV_DifficultyTier tier in WV_Campaign.Tiers)
                    lowestStars[(int)tier] = Mathf.Min(lowestStars[(int)tier], save.StarsOn(tier));
            }
            if (!anyone) {
                for (int i = 0; i < lowestStars.Length; i++)
                    lowestStars[i] = 0;
            }

            var unlocked = new List<WV_DifficultyTier>();
            foreach (WV_DifficultyTier tier in WV_Campaign.Tiers) {
                if (WV_Campaign.IsInMask((byte)mask, tier))
                    unlocked.Add(tier);
            }
            return WV_Campaign.VoteOptions(unlocked, lowestStars);
        }

        /// <summary>The tier is settled: arm the flag for it, announce it, and hand out each commander's start bonus.</summary>
        private void ApplyDifficulty() {
            difficultyChosen = true;
            if (spawnedFlag != null)
                spawnedFlag.DamageMultiplier = Profile.FlagDamageMultiplier;
            // The round's briefing banner on every client, in place of two lines on the hint pill.
            BroadcastToAll(Briefing());
            foreach (NetworkConnection connection in Commanders()) {
                SendCampaignStatus(connection);
                if (retention.IsReady(connection))
                    GiveStartBonus(connection);
            }
        }

        /// <summary>What this round is: its tier and the day's front, for every client's start banner.</summary>
        private WV_RoundBriefing Briefing() => new() {
            difficulty = (byte)difficulty,
            difficultyName = Profile.DisplayName,
            front = (byte)front,
            frontRules = WV_DailyFront.EffectText(front, FrontTuning),
            tierDescription = Profile.Description,
            xpBonusPercent = (short)Mathf.Round((Profile.XpMultiplier - 1f) * 100f)
        };

        /// <summary>
        /// What a commander's tier and levels give them at the start of a round (or when they join
        /// one): extra funds for a harder tier, the Supply Cache, the Veteran Squad and the opening
        /// doctrine hand. Once per commander per round.
        /// </summary>
        private void GiveStartBonus(NetworkConnection connection) {
            int clientId = connection.ClientId;
            string key = PlayerKey(connection);
            if (key == null || !startBonusGiven.Add(key))
                return;

            long funds = (long)Mathf.Round(WV_Rules.StartingFunds * Mathf.Max(0f, Profile.StartingFundsMultiplier - 1f));
            var reasons = new List<string>();
            if (funds > 0)
                reasons.Add(Profile.DisplayName);
            if (frontEffect.StartingFunds > 0) {
                funds += frontEffect.StartingFunds;
                reasons.Add(WV_DailyFront.Title(front));
            }
            if (retention.IsUnlocked(connection, WV_Retention.SupplyCache)) {
                funds += WV_Retention.SupplyCacheFunds;
                reasons.Add("Supply Cache");
            }
            if (funds > 0)
                serverEconomy.Grant(clientId, funds, $"{string.Join(" + ", reasons)}: +{funds:N0} funds");

            Vector3 muster = spawnedFlag != null ? spawnedFlag.MusterPoint : FlagSpawnPosition;
            if (frontEffect.FreeGunners > 0) {
                int reinforced = serverTroops.DeployFreeTroops(clientId, WV_TroopKind.Gunner, frontEffect.FreeGunners, muster);
                if (reinforced > 0)
                    WV_ServerCommand.Notify(connection,
                        $"{WV_DailyFront.Title(front)}: {reinforced} {WV_Rules.GetTroopDisplayName(WV_TroopKind.Gunner)}{(reinforced == 1 ? "" : "s")} report for duty");
            }
            if (retention.IsUnlocked(connection, WV_Retention.VeteranSquad)) {
                int deployed = serverTroops.DeployFreeTroops(clientId, WV_Retention.VeteranSquadKind,
                    WV_Retention.VeteranSquadSize, muster);
                if (deployed > 0)
                    WV_ServerCommand.Notify(connection,
                        $"Veteran Squad reports for duty: {deployed} {WV_Rules.GetTroopDisplayName(WV_Retention.VeteranSquadKind)}s");
            }

            if (preparing && retention.IsUnlocked(connection, WV_Retention.OpeningOrders) && !serverDoctrines.HasBeenDealt(clientId))
                serverDoctrines.Offer(clientId, 0);
        }

        private WV_WaveScaling GetWaveScaling(int waveNumber) {
            float health = Profile.HealthMultiplier * Mathf.Max(0.1f, frontEffect.EnemyHealthMultiplier);
            if (IsEndless)
                health *= WV_Campaign.EndlessHealthMultiplier(waveNumber, WaveTuning.WaveCount, DifficultyTuning);
            return new WV_WaveScaling(Profile.StrengthMultiplier, health);
        }

        // --- During the round -------------------------------------------------

        private void RecordWaveReached(int waveNumber) {
            foreach (NetworkConnection connection in Commanders()) {
                // Records and wave challenges need the commander to have been there for the round,
                // not to have joined for its last wave (the same rule as a win).
                if (!WV_Attendance.WinCounts(WavesPresent(connection), waveNumber, Rewards.WinAttendanceShare))
                    continue;
                retention.Reach(connection, WV_Retention.WaveStat, waveNumber);
                if (!IsEndless)
                    continue;
                retention.Reach(connection, WV_Retention.EndlessStat, waveNumber);
                PlayerProgress.Modify<WV_SaveData>(connection, save => save.RecordEndlessWave(waveNumber));
            }
        }

        /// <summary>A wave was held: its XP and the Flag Engineers' repair.</summary>
        private void HoldWave(int waveNumber) {
            bool engineers = false;
            foreach (NetworkConnection connection in Commanders()) {
                string key = PlayerKey(connection);
                if (key != null) {
                    wavesPresent.TryGetValue(key, out int present);
                    wavesPresent[key] = present + 1;
                }
                retention.AddXp(connection, Rewards.XpPerWaveHeld, "Waves held");
                retention.Count(connection, WV_Retention.WavesStat);
                engineers |= retention.IsUnlocked(connection, WV_Retention.FlagEngineers);
            }

            if (engineers && spawnedFlag != null) {
                long repaired = spawnedFlag.Repair(WV_Retention.FlagEngineersRepair);
                if (repaired > 0)
                    WV_ServerCommand.NotifyAll($"Flag Engineers repair the flag: +{repaired:N0} health");
            }

        }

        /// <summary>
        /// Deals every commander a doctrine hand once a due wave has fully arrived - every
        /// <see cref="WV_DoctrineTuning.EveryWaves"/> waves, and never after the last one.
        /// </summary>
        private void OfferDoctrines(int waveNumber) {
            if (flagDown || !serverDoctrines.IsDue(waveNumber) || (!IsEndless && waveNumber >= WaveTuning.WaveCount))
                return;
            serverDoctrines.DealRound(waveNumber, serverEconomy.ActiveClientIds);
        }

        private void CreditKill(IEntity attacker, bool isBoss) {
            if (isBoss) {
                foreach (NetworkConnection connection in Commanders())
                    retention.Count(connection, WV_Retention.BossStat);
            }
            int clientId = WV_KillCredit.CommanderOf(attacker);
            if (clientId == WV_Owned.NoOwner)
                return;
            if (TryGetConnection(clientId, out NetworkConnection killer)) {
                string key = PlayerKey(killer);
                killsThisRound.TryGetValue(key, out int kills);
                killsThisRound[key] = kills + 1;
                retention.Count(killer, WV_Retention.KillsStat);
            }
            int bounty = WV_CommanderBonuses.For(clientId).FundsPerKill;
            if (bounty > 0)
                serverEconomy.Grant(clientId, bounty);
        }

        private void HandleDoctrinePicked(int clientId, WV_DoctrineId doctrine) {
            if (!TryGetConnection(clientId, out NetworkConnection connection))
                return;
            retention.Count(connection, WV_Retention.DoctrinesStat);
            PlayerProgress.Modify<WV_SaveData>(connection, save => save.doctrinesPicked++);
            SendCampaignStatus(connection);
        }

        private void HandleTroopDelivered(int clientId, WV_TroopKind kind) {
            serverDoctrines.HandleTroopDelivered(clientId, kind);
            if (TryGetConnection(clientId, out NetworkConnection connection))
                retention.Count(connection, WV_Retention.TroopsStat);
        }

        private void HandleVehicleProduced(WV_ProductionBuilding building, WV_Unit unit) {
            if (unit != null && unit.TryGetComponent(out WV_Owned owned)
                && TryGetConnection(owned.OwnerClientId, out NetworkConnection connection))
                retention.Count(connection, WV_Retention.VehiclesStat);
        }

        private void HandleStructurePlacedForStats(NetworkConnection sender, StructureComponent structure) {
            // The economy runs first and records the builder only when the placement was paid for.
            if (sender != null && structure != null && structure.TryGetComponent(out WV_Owned owned)
                && owned.IsOwnedBy(sender.ClientId))
                retention.Count(sender, WV_Retention.StructuresStat);
        }

        private void HandleFlagHealthChanged(long previous, long next, bool asServer) {
            if (asServer && spawnedFlag != null && spawnedFlag.MaxHealth.Value > 0)
                lowestFlagFraction = Mathf.Min(lowestFlagFraction, Mathf.Clamp01(next / (float)spawnedFlag.MaxHealth.Value));
        }

        // --- Round end ---------------------------------------------------------

        /// <summary>
        /// Saves each commander's stars and records, counts the round toward their challenges, and
        /// sends their summary card: every line of XP the round paid, through the retention kit.
        /// </summary>
        private void FinishCampaignRound(WV_RoundEnded result) {
            bool victory = result.victory;
            int waveReached = result.waveReached;
            int held = result.wavesHeld;
            int stars = result.stars;
            roundLive = false;
            preparing = false;
            lastSummaryAt = Time.time;
            summariesRead.Clear();
            summaryRecipients.Clear();
            foreach (NetworkConnection connection in Commanders()) {
                killsThisRound.TryGetValue(PlayerKey(connection) ?? string.Empty, out int kills);
                var outcome = new WV_RoundOutcome {
                    Tier = difficulty,
                    Victory = victory,
                    WaveReached = waveReached,
                    WaveCount = WaveTuning.WaveCount,
                    Held = held,
                    WavesPresent = WavesPresent(connection),
                    Kills = kills,
                    Stars = stars,
                    LowestFlagFraction = lowestFlagFraction,
                    Doctrines = serverDoctrines.CountPicks(connection.ClientId),
                    Front = front
                };
                bool counts = WV_RoundSummary.Counts(outcome, Rewards);

                bool firstWin = false;
                int bestEndless = 0;
                byte unlockedBefore = 0, unlockedAfter = 0;
                PlayerProgress.Modify<WV_SaveData>(connection, save => {
                    unlockedBefore = WV_Campaign.UnlockedMask(save.tierStars);
                    if (counts) {
                        if (victory)
                            save.victories++;
                        firstWin = save.RecordStars(difficulty, stars);
                        if (IsEndless)
                            save.RecordEndlessWave(waveReached);
                    }
                    bestEndless = save.bestEndlessWave;
                    unlockedAfter = WV_Campaign.UnlockedMask(save.tierStars);
                });
                outcome.FirstWin = firstWin;
                outcome.BestEndless = bestEndless;
                if (PlayerProgress.TryGet(connection, out WV_SaveData saved))
                    outcome.NextGoal = WV_Campaign.DescribeNextGoal(saved.tierStars, saved.bestEndlessWave, DifficultyTuning);

                if (victory && counts) {
                    retention.Count(connection, WV_Retention.WinsStat);
                    if (difficulty >= WV_DifficultyTier.Veteran && !IsEndless)
                        retention.Count(connection, WV_Retention.VeteranWinStat);
                    retention.Reach(connection, WV_Retention.StarsStat, stars);
                }

                // This commander's own banner: their stars, whether the win counts, and the tiers it opened.
                WV_RoundEnded own = result;
                own.lateJoin = victory && !counts;
                own.stars = (byte)(counts ? stars : 0);
                own.firstWin = firstWin;
                own.unlockedTiers = (byte)(unlockedAfter & ~unlockedBefore);
                var names = new List<string>();
                var descriptions = new List<string>();
                foreach (WV_DifficultyTier tier in WV_Campaign.Tiers) {
                    if (!WV_Campaign.IsInMask(own.unlockedTiers, tier))
                        continue;
                    WV_DifficultyProfile profile = DifficultyTuning.Get(tier);
                    names.Add(profile.DisplayName);
                    descriptions.Add(profile.Description);
                }
                own.unlockedNames = names.ToArray();
                own.unlockedDescriptions = descriptions.ToArray();
                connection.Broadcast(own);

                retention.FinishMatch(connection, WV_RoundSummary.Title(outcome, DifficultyTuning, counts),
                    WV_RoundSummary.Build(outcome, Rewards, DifficultyTuning, FrontTuning));
                string key = PlayerKey(connection);
                if (key != null)
                    summaryRecipients.Add(key);
                SendCampaignStatus(connection);
            }
        }

        /// <summary>Tells one commander their campaign: the tier, their stars per tier and their next goal.</summary>
        private void SendCampaignStatus(NetworkConnection connection) {
            if (connection == null || !connection.IsValid || !PlayerProgress.TryGet(connection, out WV_SaveData save))
                return;
            var stars = new byte[WV_Campaign.Tiers.Length];
            foreach (WV_DifficultyTier tier in WV_Campaign.Tiers)
                stars[(int)tier] = (byte)save.StarsOn(tier);
            connection.Broadcast(new WV_CampaignStatus {
                difficulty = (byte)difficulty,
                tierStars = stars,
                unlockedTiers = WV_Campaign.UnlockedMask(save.tierStars),
                bestWave = save.bestWave,
                bestEndlessWave = save.bestEndlessWave,
                victories = save.victories,
                nextGoal = WV_Campaign.DescribeNextGoal(save.tierStars, save.bestEndlessWave, DifficultyTuning),
                difficultyName = Profile.DisplayName,
                front = (byte)front,
                frontText = WV_DailyFront.Describe(front, FrontTuning)
            });
        }
    }
}
