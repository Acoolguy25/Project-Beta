using Cysharp.Threading.Tasks;
using FishNet;
using FishNet.Connection;
using FishNet.Object;
using Channel = FishNet.Transporting.Channel;
using RyanAssets.Characters.Server;
using RyanAssets.Characters.Shared;
using RyanAssets.DataService;
using RyanAssets.Item.FloatingTool;
using RyanAssets.Levels.Server;
using RyanAssets.Server.ServerCore;
using RyanAssets.Server.ServerFeatures;
using RyanAssets.Shared.Declarations;
using RyanAssets.Tools.Shared;
using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using UnityEngine.AI;
using Universes.UniverseData.murder_mystery;
using Universes.UniverseData.murder_mystery.Server;

namespace Universes.murder_mystery.Server {
    /// <summary>
    /// A Classic round in the style of Murder Mystery 2: role reveal, then a timed hunt in which the
    /// murderer stabs and throws a knife, the sheriff has a one-shot gun (shooting an innocent kills them),
    /// a dead sheriff's gun can be picked up by an innocent who becomes the hero, and everyone fills a coin
    /// bag; then the results. The runner owns spawning, tools and the mode vote; numbers come from
    /// <see cref="MM_ClassicTuning"/> on the runner.
    /// </summary>
    public sealed class MM_ClassicRound {
        // Lethal for any character in this game (100 health), used if a thrower's knife tool is gone.
        const long FallbackKnifeDamage = 1000;
        // How often the round looks for an NPC to send for a gun nobody has taken.
        const float GunSeekerSearchInterval = 1f;

        /// <summary>Someone who had a role this round, kept for the results.</summary>
        sealed class Participant {
            public GameCharacter character;
            public PlayerData player; // null for NPCs
            public string alias, username;
            public MM_Role role;
            public bool hero;
            public int kills, coins, xp;
            public bool Alive => character != null && !character.IsDead;
            public bool IsPlayer => player != null;
        }

        readonly MM_ServerRunner runner;
        readonly MM_ClassicTuning tuning;
        readonly List<Participant> participants = new();
        readonly Dictionary<GameCharacter, Participant> byCharacter = new();
        readonly Dictionary<GameCharacter, float> lastKillAt = new();
        readonly Dictionary<GameCharacter, float> lastThrowAt = new();
        readonly Dictionary<GameCharacter, float> nextThrowAt = new();
        // Weapon skins by tool object id: murderers' knives, and players' guns (MM_KnifeSkinsBroadcast).
        readonly Dictionary<int, byte> toolSkins = new();
        readonly List<NetworkObject> coins = new();
        readonly Dictionary<GameCharacter, int> manhuntRanks = new();
        Dictionary<PlayerData, (byte murderer, byte sheriff)> odds = new();
        int oddsPlayerCount = -1;
        MM_KillClock killClock;
        readonly MM_KnifeThrows throws;
        FloatingToolShared droppedGun;
        float gunDroppedAt, nextGunSeekerSearch;
        MM_NpcGunSeeker gunSeeker;
        readonly HashSet<GameCharacter> gunSeekersTried = new();
        float nextCoinAt;
        int huntLength;
        bool resolvingThrow, playersOut, lastSurvivorAnnounced, manhuntRanksStale = true;
        string stoppedBy;

        public int Round { get; private set; }
        public MM_Phase Phase { get; private set; } = MM_Phase.Lobby;
        public bool Active { get; private set; }
        public int SecondsLeft { get; private set; }
        public MM_Outcome Outcome { get; private set; }

        /// <summary>A murderer killed someone: (murderer, victim). NPC sheriffs listen to it as witnesses.</summary>
        public event Action<GameCharacter, GameCharacter> MurderCommitted;

        public MM_ClassicRound(MM_ServerRunner runner, MM_ClassicTuning tuning) {
            this.runner = runner;
            this.tuning = tuning ?? throw new ArgumentNullException(nameof(tuning));
            killClock = new MM_KillClock(tuning.murdererGraceSeconds);
            throws = new MM_KnifeThrows(tuning) { KnifeHit = OnKnifeHit };
        }

        // ---------- lifecycle ----------

        public void Begin(int round) {
            Round = round;
            Active = true;
            Outcome = MM_Outcome.None;
            ClearRoundState();
            // Rebuilt every round so Inspector changes to the tuning apply from the next round on.
            killClock = new MM_KillClock(tuning.murdererGraceSeconds);
            Phase = MM_Phase.RoleReveal;
        }

        public void End() {
            Active = false;
            Phase = MM_Phase.Lobby;
            ClearRoundState();
            killClock.Clear();
        }

        void ClearRoundState() {
            participants.Clear();
            byCharacter.Clear();
            lastKillAt.Clear();
            lastThrowAt.Clear();
            nextThrowAt.Clear();
            toolSkins.Clear();
            coins.Clear();
            throws.Clear();
            droppedGun = null;
            StopGunSeeker();
            gunSeekersTried.Clear();
            stoppedBy = null;
            playersOut = false;
            lastSurvivorAnnounced = false;
            manhuntRanksStale = true;
        }

        /// <summary>Adds a character dealt <paramref name="role"/> this round (<paramref name="player"/> is null for NPCs).</summary>
        public void AddParticipant(GameCharacter character, PlayerData player, MM_Role role) {
            if (character == null)
                return;
            Participant participant = new() {
                character = character,
                player = player,
                alias = character.DisplayName,
                username = player != null ? player.username.Value : null,
                role = role,
            };
            participants.Add(participant);
            byCharacter[character] = participant;
            manhuntRanksStale = true;
            if (player != null)
                PlayerProgress.Modify<MM_SaveData>(player, save => save.RecordDealt(role));
        }

        public static MM_Role RoleOf(TeamColor team) => team switch {
            TeamColor.Red => MM_Role.Murderer,
            TeamColor.Blue => MM_Role.Sheriff,
            TeamColor.Green => MM_Role.Innocent,
            _ => MM_Role.None,
        };

        public static TeamColor TeamOf(MM_Role role) => role switch {
            MM_Role.Murderer => TeamColor.Red,
            MM_Role.Sheriff => TeamColor.Blue,
            MM_Role.Innocent => TeamColor.Green,
            _ => TeamColor.None,
        };

        /// <summary>The role this character was dealt (or took over) this round.</summary>
        public MM_Role RoleIn(GameCharacter character) =>
            character != null && byCharacter.TryGetValue(character, out Participant p) ? p.role : MM_Role.None;

        // ---------- weapons ----------

        /// <summary>
        /// Gives a sheriff (dealt, NPC or hero) the gun with this round's clip and reload. A player's gun wears
        /// the best skin their coins have unlocked, so coins show in every role.
        /// </summary>
        public ToolBaseShared ArmSheriff(GameCharacter character, PlayerData player = null) {
            ToolBaseShared gun = ServerTool.Instance.SpawnTool(character.NetworkObject, ToolEnum.Pistol, tool => {
                tool.maxClipAmmo = tuning.gunClip;
                tool.reloadDurationSync.Value = tuning.gunReloadSeconds;
            });
            if (gun != null && player != null) {
                byte skin = MM_KnifeSkins.Unlocked(MM_SaveData.Of(player).coinsCollected);
                if (skin > 0) {
                    toolSkins[gun.NetworkObject.ObjectId] = skin;
                    if (Phase == MM_Phase.Hunt)
                        BroadcastToolSkins();
                }
            }
            return gun;
        }

        /// <summary>Gives a murderer the knife and records the skin everyone will see on it.</summary>
        public ToolBaseShared ArmMurderer(GameCharacter character, PlayerData player) {
            ToolBaseShared dagger = ServerTool.Instance.SpawnTool(character.NetworkObject, ToolEnum.Dagger);
            if (dagger == null)
                return null;
            byte skin = player != null
                ? MM_KnifeSkins.Unlocked(MM_SaveData.Of(player).coinsCollected)
                : (byte)UnityEngine.Random.Range(0, Mathf.Min(tuning.npcKnifeSkinMax, MM_KnifeSkins.All.Length - 1) + 1);
            toolSkins[dagger.NetworkObject.ObjectId] = skin;
            return dagger;
        }

        byte SkinOf(ToolBaseShared tool) =>
            tool != null && toolSkins.TryGetValue(tool.NetworkObject.ObjectId, out byte skin) ? skin : (byte)0;

        /// <summary>Tells every client (or one) which skin each knife and player's gun wears.</summary>
        public void BroadcastToolSkins(NetworkConnection only = null) {
            if (InstanceFinder.ServerManager == null || toolSkins.Count == 0)
                return;
            MM_KnifeSkinsBroadcast message = new() {
                toolObjectIds = new int[toolSkins.Count],
                skins = new byte[toolSkins.Count],
            };
            int i = 0;
            foreach (KeyValuePair<int, byte> entry in toolSkins) {
                message.toolObjectIds[i] = entry.Key;
                message.skins[i] = entry.Value;
                i++;
            }
            if (only != null)
                InstanceFinder.ServerManager.Broadcast(only, message);
            else
                InstanceFinder.ServerManager.Broadcast(message);
        }

        // ---------- murderer balance ----------

        /// <summary>
        /// Whether this murderer may kill right now: only during the hunt, after the grace period, and once their
        /// own kill cooldown has run out. NPC murderers ask before every attack; knife throws ask too.
        /// </summary>
        public bool CanKill(GameCharacter murderer) =>
            Active && Phase == MM_Phase.Hunt && murderer != null && killClock.CanKill(KillerId(murderer), Time.time);

        static int KillerId(GameCharacter character) => character.NetworkObject.ObjectId;

        /// <summary>
        /// Whether townsfolk should run from this murderer: the knife is out, or it just killed or threw.
        /// Otherwise a murderer walking past is just another face in the crowd.
        /// </summary>
        public bool IsVisibleThreat(GameCharacter murderer) {
            if (murderer == null || murderer.IsDead || RoleIn(murderer) != MM_Role.Murderer)
                return false;
            float now = Time.time;
            if (lastKillAt.TryGetValue(murderer, out float killedAt) && now - killedAt < tuning.npcPanicAfterKillSeconds)
                return true;
            if (lastThrowAt.TryGetValue(murderer, out float threwAt) && now - threwAt < tuning.throwPanicSeconds)
                return true;
            ToolBaseShared active = murderer.ActiveTool.Value;
            return active != null && active.toolEnum == ToolEnum.Dagger && active.equipped;
        }

        /// <summary>Whether this murderer was seen killing or throwing in the last <paramref name="seconds"/>.</summary>
        public bool RecentlyAttacked(GameCharacter murderer, float seconds) {
            float now = Time.time;
            return (lastKillAt.TryGetValue(murderer, out float k) && now - k < seconds)
                || (lastThrowAt.TryGetValue(murderer, out float t) && now - t < seconds);
        }

        /// <summary>How close the sheriff must be before this NPC murderer goes for them (<see cref="MM_ClassicTuning.SheriffNoticeDistance"/>).</summary>
        public float SheriffNoticeDistance(GameCharacter hunter) =>
            tuning.SheriffNoticeDistance(HuntSecondsElapsed, ManhuntRank(hunter));

        /// <summary>The murderer's place in the manhunt: living murderers dealt before it this round.</summary>
        int ManhuntRank(GameCharacter hunter) {
            // Asked every frame by every NPC murderer; the order only changes when someone dies or takes a role.
            if (manhuntRanksStale) {
                manhuntRanks.Clear();
                int rank = 0;
                foreach (Participant participant in participants)
                    if (participant.role == MM_Role.Murderer && participant.Alive)
                        manhuntRanks[participant.character] = rank++;
                manhuntRanksStale = false;
            }
            return manhuntRanks.TryGetValue(hunter, out int cached) ? cached : manhuntRanks.Count;
        }

        /// <summary>Seconds of the hunt played so far this round.</summary>
        public float HuntSecondsElapsed => Phase == MM_Phase.Hunt ? huntLength - SecondsLeft : 0f;

        float KillCooldownFor(GameCharacter murderer) =>
            murderer.Owner.IsValid ? tuning.playerKillCooldownSeconds : tuning.npcKillCooldownSeconds;

        /// <summary>Locks the murderer's knife for as long as the kill clock says, so their client shows and respects it.</summary>
        void SyncDagger(GameCharacter murderer) {
            if (murderer == null || murderer.IsDead)
                return;
            ToolBaseShared dagger = ServerTool.Instance.GetTool(murderer.NetworkObject, ToolEnum.Dagger);
            if (dagger != null)
                dagger.UpdateServerCooldown(killClock.SecondsUntilReady(KillerId(murderer), Time.time));
        }

        void SyncAllDaggers() {
            foreach (Participant participant in participants)
                if (participant.role == MM_Role.Murderer)
                    SyncDagger(participant.character);
        }

        // ---------- knife throws ----------

        public void OnThrowRequest(NetworkConnection conn, MM_ThrowKnifeRequest request, Channel channel) {
            if (TryGetLivingCharacter(conn, out GameCharacter thrower))
                TryThrow(thrower, request.target);
        }

        /// <summary>Seconds until this murderer may throw (the throw cooldown or the kill clock, whichever is longer).</summary>
        public float ThrowCooldown(GameCharacter murderer) {
            float throwWait = nextThrowAt.TryGetValue(murderer, out float at) ? at - Time.time : 0f;
            float killWait = killClock.Running ? killClock.SecondsUntilReady(KillerId(murderer), Time.time) : float.PositiveInfinity;
            return Mathf.Max(0f, Mathf.Max(throwWait, killWait));
        }

        /// <summary>Throws a murderer's knife at <paramref name="target"/> if they may. Used by players and NPCs alike.</summary>
        public bool TryThrow(GameCharacter thrower, Vector3 target) {
            if (thrower == null || thrower.IsDead || RoleIn(thrower) != MM_Role.Murderer || !CanKill(thrower))
                return false;
            if (!float.IsFinite(target.x) || !float.IsFinite(target.y) || !float.IsFinite(target.z))
                return false;
            if (nextThrowAt.TryGetValue(thrower, out float readyAt) && Time.time < readyAt)
                return false;
            ToolBaseShared dagger = ServerTool.Instance.GetTool(thrower.NetworkObject, ToolEnum.Dagger);
            if (dagger == null)
                return false;
            nextThrowAt[thrower] = Time.time + tuning.throwCooldownSeconds;
            lastThrowAt[thrower] = Time.time;
            throws.Launch(thrower, target, SkinOf(dagger), dagger.NetworkObject.ObjectId, tuning.throwCooldownSeconds);
            return true;
        }

        /// <summary>Where a knife leaves this character's hand (for NPC aim).</summary>
        public Vector3 ThrowOrigin(GameCharacter thrower) => throws.Origin(thrower);

        bool OnKnifeHit(GameCharacter thrower, GameCharacter victim) {
            if (!Active || Phase != MM_Phase.Hunt || thrower == null || victim == null)
                return false;
            ToolBaseShared dagger = ServerTool.Instance.GetTool(thrower.NetworkObject, ToolEnum.Dagger);
            long damage = dagger != null ? dagger.hitDamage : FallbackKnifeDamage;
            resolvingThrow = true;
            try {
                return victim.TakeDamage(damage, DamageType.Melee, thrower.NetworkObject);
            } finally {
                resolvingThrow = false;
            }
        }

        /// <summary>Per frame from the runner: flying knives and coin spawns.</summary>
        public void Tick(float deltaTime) {
            if (!Active)
                return;
            throws.Tick(deltaTime);
            if (Phase != MM_Phase.Hunt)
                return;
            if (Time.time >= nextCoinAt) {
                nextCoinAt = Time.time + tuning.coinSpawnInterval;
                SpawnCoins(1);
            }
            if (GunOnFloor && gunSeeker == null && Time.time >= nextGunSeekerSearch
                && MM_DropRules.NpcMayGoForGun(Time.time - gunDroppedAt, tuning.npcHeroDelaySeconds)) {
                nextGunSeekerSearch = Time.time + GunSeekerSearchInterval;
                SendNpcForGun();
            }
        }

        // ---------- coins ----------

        void SpawnCoins(int count) {
            coins.RemoveAll(coin => coin == null || !coin.IsSpawned);
            for (int i = 0; i < count && coins.Count < tuning.coinsOnMap; i++) {
                GameObject coin = ServerCoin.SpawnCoin(null);
                if (coin == null)
                    return;
                coins.Add(coin.GetComponent<NetworkObject>());
            }
        }

        /// <summary>Server's pickup gate (<c>CoinShared.CanCollect</c>): living participants with room in their bag, during the hunt.</summary>
        public bool CanCollectCoin(NetworkConnection conn) {
            if (!Active)
                return true; // other modes and the lobby keep the shared coin behaviour
            if (Phase != MM_Phase.Hunt || !TryGetLivingCharacter(conn, out GameCharacter character))
                return false;
            return byCharacter.TryGetValue(character, out Participant p) && MM_RoundRules.BagHasRoom(p.coins, tuning.coinBagSize);
        }

        public void OnCoinCollected(NetworkConnection conn, ulong gold, Vector3 position) {
            if (!Active || !TryGetLivingCharacter(conn, out GameCharacter character)
                || !byCharacter.TryGetValue(character, out Participant p))
                return;
            p.coins++;
            Award(p, tuning.xpPerCoin);
            if (p.player != null)
                PlayerProgress.Modify<MM_SaveData>(p.player, save => save.coinsCollected++);
            InstanceFinder.ServerManager.Broadcast(conn, new MM_CoinBroadcast {
                position = position, coins = p.coins, coinCap = tuning.coinBagSize,
            });
        }

        // ---------- deaths ----------

        public void OnCharacterDied(GameCharacter victim, DamageType source, GameCharacter killer) {
            if (!Active || victim == null || !byCharacter.TryGetValue(victim, out Participant dead))
                return;
            manhuntRanksStale = true;
            float now = Time.time;
            if (killer != null && killer != victim && byCharacter.TryGetValue(killer, out Participant attacker)) {
                attacker.kills++;
                if (attacker.role == MM_Role.Murderer) {
                    killClock.RecordKill(KillerId(killer), now, playersOut ? 0f : KillCooldownFor(killer));
                    lastKillAt[killer] = now;
                    SyncDagger(killer);
                    Award(attacker, tuning.xpPerKill);
                    bool thrown = resolvingThrow;
                    if (attacker.player != null)
                        PlayerProgress.Modify<MM_SaveData>(attacker.player, save => {
                            save.murders++;
                            if (thrown) save.throwKills++;
                        });
                    MurderCommitted?.Invoke(killer, victim);
                } else if (attacker.role == MM_Role.Sheriff) {
                    if (dead.role == MM_Role.Murderer) {
                        stoppedBy ??= attacker.alias;
                        Award(attacker, tuning.xpForStoppingMurderer);
                        if (attacker.player != null)
                            PlayerProgress.Modify<MM_SaveData>(attacker.player, save => save.murderersShot++);
                    } else if (MM_DropRules.IsWrongShot(attacker.role, dead.role)) {
                        PunishWrongShot(attacker);
                    }
                }
            }
            if (MM_DropRules.DropsGun(dead.role))
                DropGun(victim);
            AnnounceMilestones();
        }

        /// <summary>The sheriff shot an innocent: MM2 rules kill the sheriff too (their gun drops), or the gun jams.</summary>
        void PunishWrongShot(Participant sheriff) {
            if (tuning.wrongShotPenalty == MM_WrongShotPenalty.Jam) {
                ToolBaseShared gun = ServerTool.Instance.GetTool(sheriff.character.NetworkObject, ToolEnum.Pistol);
                if (gun != null)
                    gun.UpdateServerCooldown(tuning.wrongShotJamSeconds);
                return;
            }
            if (sheriff.player != null)
                ServerChat.SendSystemMessage(sheriff.player.Owner, new("You shot an innocent. That was your last mistake.", SystemMessageSource.CustomMessage));
            Announce(MM_Announcement.SheriffMistake, null);
            // The victim's death is still being processed; the sheriff falls on the next frame.
            KillNextFrame(sheriff.character, runner.destroyCancellationToken).Forget(e => Debug.LogException(e, runner));
        }

        static async UniTask KillNextFrame(GameCharacter character, CancellationToken token) {
            if (await UniTask.Yield(PlayerLoopTiming.Update, token).SuppressCancellationThrow())
                return;
            if (character != null && !character.IsDead)
                character.Kill(DamageType.Command);
        }

        void AnnounceMilestones() {
            if (Phase != MM_Phase.Hunt)
                return;
            int survivors = SurvivorsAlive();
            if (!lastSurvivorAnnounced && survivors == 1 && MurderersAlive() > 0) {
                lastSurvivorAnnounced = true;
                Announce(MM_Announcement.LastSurvivor, null);
            }
            if (!playersOut && MM_RoundRules.Decide(MurderersAlive(), survivors, false) == MM_Outcome.None && !AnyPlayerAlive())
                StartPlayersOutEndgame();
        }

        /// <summary>
        /// No player is left alive and the NPCs are still at it: cut the clock and let the NPC murderers stop
        /// waiting between kills, so spectators are not kept watching a long NPC chase.
        /// </summary>
        void StartPlayersOutEndgame() {
            playersOut = true;
            SecondsLeft = Mathf.Min(SecondsLeft, tuning.secondsLeftWhenPlayersOut);
            killClock.ReleaseAll(Time.time);
            SyncAllDaggers();
            Announce(MM_Announcement.PlayersOut, null);
            EndgameStarted?.Invoke();
        }

        /// <summary>The players-out endgame began: the runner lets NPC murderers search the whole house.</summary>
        public event Action EndgameStarted;

        /// <summary>Whether the round has entered the endgame where only NPCs are left alive.</summary>
        public bool PlayersOut => playersOut;

        // ---------- the dropped gun ----------

        /// <summary>Whether a dead sheriff's gun is lying on the floor right now.</summary>
        public bool GunOnFloor => droppedGun != null && droppedGun.IsSpawned;

        /// <summary>Where the dropped gun lies (only meaningful while <see cref="GunOnFloor"/>).</summary>
        public Vector3 GunPosition => droppedGun != null ? droppedGun.transform.position : Vector3.zero;

        void DropGun(GameCharacter sheriff) {
            // On the floor where the sheriff fell, never on a lamp or a table they died next to.
            Vector3 floor = sheriff.transform.position;
            if (NavMesh.SamplePosition(floor, out NavMeshHit hit, tuning.gunFloorSearchRadius, NavMesh.AllAreas))
                floor = hit.position;
            FloatingToolShared floating = ServerTool.Instance.SpawnFloatingTool(ToolEnum.Pistol, floor + Vector3.up * tuning.gunFloatHeight);
            floating.OnToolCollectedFunc = OnDroppedGunCollected;
            droppedGun = floating;
            gunDroppedAt = Time.time;
            nextGunSeekerSearch = Time.time;
            StopGunSeeker();
            gunSeekersTried.Clear();
            Announce(MM_Announcement.GunDropped, null);
        }

        /// <summary>Server side of a player walking over the gun: an innocent takes it and, secretly, becomes the hero.</summary>
        bool OnDroppedGunCollected(NetworkBehaviour collector, ToolEnum tool) {
            if (!Active || Phase != MM_Phase.Hunt || tool != ToolEnum.Pistol
                || !collector.TryGetComponent(out GameCharacter character)
                || !byCharacter.TryGetValue(character, out Participant p)
                || !p.IsPlayer)
                return false;
            // The pickup despawns itself when this returns true.
            return PromoteToHero(p);
        }

        /// <summary>
        /// Players had their head start: the nearest living, empty-handed NPC townsperson that has not tried
        /// yet heads for the gun (<see cref="MM_NpcGunSeeker"/>).
        /// </summary>
        void SendNpcForGun() {
            Vector3 gun = GunPosition;
            Participant best = null;
            float bestDistance = float.PositiveInfinity;
            foreach (Participant p in participants) {
                if (p.IsPlayer || gunSeekersTried.Contains(p.character)
                    || !MM_DropRules.CanPickUp(p.role, p.Alive, p.character.Tools.Length))
                    continue;
                float distance = (p.character.transform.position - gun).sqrMagnitude;
                if (distance < bestDistance) {
                    bestDistance = distance;
                    best = p;
                }
            }
            if (best == null)
                return;
            gunSeekersTried.Add(best.character);
            gunSeeker = best.character.gameObject.AddComponent<MM_NpcGunSeeker>();
            gunSeeker.Begin(this, tuning);
        }

        /// <summary>The NPC sent for the gun got there.</summary>
        public void OnNpcReachedGun(MM_NpcGunSeeker seeker, GameCharacter npc) {
            if (seeker != gunSeeker || !GunOnFloor || !byCharacter.TryGetValue(npc, out Participant p))
                return;
            gunSeeker = null;
            FloatingToolShared gun = droppedGun;
            if (PromoteToHero(p))
                gun.Despawn();
        }

        /// <summary>The NPC sent for the gun died, gave up or the gun went: the next one may try.</summary>
        public void OnGunSeekerStopped(MM_NpcGunSeeker seeker) {
            if (seeker == gunSeeker)
                gunSeeker = null;
        }

        void StopGunSeeker() {
            if (gunSeeker != null)
                gunSeeker.Stop();
            gunSeeker = null;
        }

        /// <summary>Hands the dropped gun to an innocent (player or NPC), who becomes the hero. False leaves it on the floor.</summary>
        bool PromoteToHero(Participant p) {
            if (!Active || Phase != MM_Phase.Hunt || !MM_DropRules.CanPickUp(p.role, p.Alive, p.character.Tools.Length))
                return false;
            GameCharacter character = p.character;
            // Gun first: if it cannot be given, the collector stays innocent and the gun stays on the floor.
            ToolBaseShared gun = ArmSheriff(character, p.player);
            if (gun == null) {
                Debug.LogError($"[MM] Could not give the gun to {character.name}; it stays on the floor.", character);
                return false;
            }
            // The display team stays hidden, exactly like a role dealt at the start of the round.
            character.SetTeam(new TeamConfig(TeamColor.Blue, TeamColor.None));
            p.role = MM_Role.Sheriff;
            p.hero = true;
            droppedGun = null;
            StopGunSeeker();
            manhuntRanksStale = true;
            if (p.player != null) {
                PlayerProgress.Modify<MM_SaveData>(p.player, save => save.gunsPickedUp++);
                ServerChat.SendSystemMessage(p.player.Owner, new("You picked up the gun. You are the hero now: stop the murderer.", SystemMessageSource.CustomMessage));
            } else {
                character.gameObject.AddComponent<MM_NpcSheriff>().Configure(this, tuning, gun);
            }
            Announce(MM_Announcement.GunTaken, null);
            return true;
        }

        void Announce(MM_Announcement kind, string name) {
            if (InstanceFinder.ServerManager != null)
                InstanceFinder.ServerManager.Broadcast(new MM_AnnounceBroadcast { kind = kind, name = name });
        }

        // ---------- phases ----------

        public async UniTask RunRoleReveal(CancellationToken token) {
            Phase = MM_Phase.RoleReveal;
            BroadcastToolSkins();
            for (int i = tuning.roleRevealSeconds; i > 0; i--) {
                SecondsLeft = i;
                ServerRunner.SetTopMessage($"Round {Round}: check your role! ({i})");
                if (await runner.AwaitTime(1000, token))
                    break;
            }
        }

        /// <summary>Counts the hunt down. Returns when time runs out, the round is decided, or the timer is skipped.</summary>
        public async UniTask RunHunt(int seconds, CancellationToken token) {
            Phase = MM_Phase.Hunt;
            huntLength = seconds;
            SecondsLeft = seconds;
            killClock.BeginHunt(Time.time);
            SyncAllDaggers();
            SpawnCoins(tuning.coinsAtStart);
            nextCoinAt = Time.time + tuning.coinSpawnInterval;
            bool skipped = false;
            while (SecondsLeft > 0 && Decide(false) == MM_Outcome.None) {
                ServerRunner.SetTopMessage(TopMessage());
                if (await runner.AwaitTime(1000, token)) {
                    skipped = true;
                    break;
                }
                SecondsLeft--;
            }
            Outcome = Decide(timeUp: true);
            if (skipped && Outcome == MM_Outcome.None)
                Outcome = MM_Outcome.InnocentsSurvived;
            throws.Clear();
            ServerCoin.ClearAllCoins();
            coins.Clear();
        }

        string TopMessage() {
            int survivors = SurvivorsAlive();
            string who = survivors == 1 ? "1 innocent left" : $"{survivors} innocents left";
            return playersOut ? $"Finishing up · {who} · {MM_Text.Clock(SecondsLeft)}" : $"{who} · {MM_Text.Clock(SecondsLeft)}";
        }

        MM_Outcome Decide(bool timeUp) => MM_RoundRules.Decide(MurderersAlive(), SurvivorsAlive(), timeUp);

        /// <summary>The end of the round: XP and saves for everyone, then the reveal for <paramref name="seconds"/>.</summary>
        public async UniTask RunResults(int seconds, CancellationToken token) {
            Phase = MM_Phase.Results;
            if (Outcome == MM_Outcome.None)
                Outcome = Decide(timeUp: true);
            foreach (Participant p in participants) {
                if (p.player == null)
                    continue;
                bool won = MM_RoundRules.IsWinner(p.role, Outcome);
                bool survived = p.Alive;
                if (won) Award(p, tuning.xpForWinning);
                if (survived) Award(p, tuning.xpForSurviving);
                MM_Role role = p.role;
                PlayerProgress.Modify<MM_SaveData>(p.player, save => {
                    save.RecordRound(role, won);
                    if (survived) save.survivals++;
                });
            }
            RefreshOdds(force: true);

            string headline = MM_Text.OutcomeHeadline(Outcome);
            string summary = Summary();
            ServerChat.SendSystemMessage(new($"{headline}! {summary}", SystemMessageSource.CustomMessage));
            MM_ResultEntry[] entries = BuildEntries();
            string murderers = NamesWith(p => p.role == MM_Role.Murderer);
            string sheriff = NamesWith(p => p.role == MM_Role.Sheriff && !p.hero);
            string hero = NamesWith(p => p.hero);
            // The card is sent once; its countdown follows the state broadcast's secondsLeft.
            SecondsLeft = seconds;
            foreach (PlayerData player in PlayerData.Players.Values) {
                if (player == null || !player.Owner.IsActive)
                    continue;
                Participant me = FindPlayer(player);
                MM_SaveData save = MM_SaveData.Of(player);
                (byte murdererChance, byte sheriffChance) = OddsFor(player);
                InstanceFinder.ServerManager.Broadcast(player.Owner, new MM_ResultsBroadcast {
                    round = Round,
                    outcome = Outcome,
                    headline = headline,
                    summary = summary,
                    entries = entries,
                    secondsLeft = seconds,
                    murdererName = murderers,
                    sheriffName = sheriff,
                    heroName = hero,
                    stoppedBy = stoppedBy,
                    youWon = me != null && MM_RoundRules.IsWinner(me.role, Outcome),
                    yourCoins = me?.coins ?? 0,
                    yourXp = me?.xp ?? 0,
                    lifetimeCoins = save.coinsCollected,
                    murdererChance = murdererChance,
                    sheriffChance = sheriffChance,
                    winStreak = save.winStreak,
                    bestWinStreak = save.bestWinStreak,
                });
            }
            for (int i = seconds; i > 0; i--) {
                SecondsLeft = i;
                ServerRunner.SetTopMessage($"{headline}! Next round in {i}");
                if (await runner.AwaitTime(1000, token))
                    break;
            }
        }

        string Summary() {
            int survivors = SurvivorsAlive();
            string reason = MM_RoundRules.Reason(Outcome, stoppedBy);
            return Outcome == MM_Outcome.MurdererWon ? $"{reason}." : $"{reason}. {survivors} survived.";
        }

        MM_ResultEntry[] BuildEntries() {
            List<MM_ResultEntry> entries = new(participants.Count);
            foreach (Participant p in participants)
                entries.Add(new MM_ResultEntry {
                    name = p.alias,
                    username = p.username,
                    role = p.role,
                    isPlayer = p.IsPlayer,
                    survived = p.Alive,
                    kills = p.kills,
                    hero = p.hero,
                    coins = p.coins,
                });
            return entries.ToArray();
        }

        string NamesWith(Predicate<Participant> match) {
            List<string> names = new();
            foreach (Participant p in participants)
                if (match(p))
                    names.Add(p.alias);
            return names.Count == 0 ? null : string.Join(", ", names);
        }

        void Award(Participant p, int xp) {
            if (xp <= 0)
                return;
            p.xp += xp;
            if (p.player != null)
                LevelsServer.AwardPlayerXP(p.player, (ulong)xp);
        }

        // ---------- per-player state ----------

        /// <summary>Recomputes everyone's role odds for the next round if the player count changed (or when forced).</summary>
        public void RefreshOdds(bool force = false) {
            int count = PlayerData.Players.Count;
            if (!force && count == oddsPlayerCount)
                return;
            oddsPlayerCount = count;
            List<PlayerData> players = new();
            foreach (PlayerData player in PlayerData.Players.Values)
                if (player != null && player.Owner.IsValid)
                    players.Add(player);
            odds = MM_Roles.Odds(players, tuning);
        }

        (byte, byte) OddsFor(PlayerData player) => odds.TryGetValue(player, out var o) ? o : ((byte)0, (byte)0);

        public MM_StateBroadcast BuildState(PlayerData player, bool classic) {
            bool gunDropped = Active && classic && GunOnFloor;
            (byte murdererChance, byte sheriffChance) = OddsFor(player);
            MM_StateBroadcast state = new() {
                round = Round,
                phase = Active ? Phase : MM_Phase.Lobby,
                classic = classic,
                secondsLeft = SecondsLeft,
                coinCap = tuning.coinBagSize,
                gunDropped = gunDropped,
                gunPosition = gunDropped ? droppedGun.transform.position : Vector3.zero,
                murdererChance = murdererChance,
                sheriffChance = sheriffChance,
                lifetimeCoins = MM_SaveData.Of(player).coinsCollected,
            };
            Participant me = Active ? FindPlayer(player) : null;
            state.role = me != null ? me.role : Active && classic ? MM_Role.Spectator : MM_Role.None;
            state.alive = me != null && me.Alive;
            if (!Active || !classic)
                return state;
            state.survivorsLeft = SurvivorsAlive();
            if (me == null)
                return state;
            state.hero = me.hero;
            state.coins = me.coins;
            if (me.role == MM_Role.Murderer && me.Alive && killClock.Running) {
                state.killCooldown = Mathf.CeilToInt(killClock.SecondsUntilReady(KillerId(me.character), Time.time));
                state.throwCooldown = ThrowCooldown(me.character);
            }
            return state;
        }

        // ---------- helpers ----------

        Participant FindPlayer(PlayerData player) {
            foreach (Participant p in participants)
                if (p.player == player)
                    return p;
            return null;
        }

        int MurderersAlive() {
            int count = 0;
            foreach (Participant p in participants)
                if (p.role == MM_Role.Murderer && p.Alive) count++;
            return count;
        }

        int SurvivorsAlive() {
            int count = 0;
            foreach (Participant p in participants)
                if (p.role != MM_Role.Murderer && p.Alive) count++;
            return count;
        }

        bool AnyPlayerAlive() {
            foreach (Participant p in participants)
                if (p.IsPlayer && p.Alive) return true;
            return false;
        }

        /// <summary>The living murderers and sheriffs of the round, for NPC policies.</summary>
        public IEnumerable<GameCharacter> LivingWithRole(MM_Role role) {
            foreach (Participant p in participants)
                if (p.role == role && p.Alive)
                    yield return p.character;
        }

        static bool TryGetLivingCharacter(NetworkConnection conn, out GameCharacter character) {
            character = null;
            if (conn == null || !LocalCharacter.Characters.TryGetValue(conn, out LocalCharacter local) || local == null || local.IsDead)
                return false;
            character = local;
            return true;
        }
    }
}
