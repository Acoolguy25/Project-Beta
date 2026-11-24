using System;
using System.Collections.Generic;
using FishNet;
using FishNet.Object;
using RyanAssets.Characters.Server;
using RyanAssets.Characters.Shared;
using RyanAssets.Server.ServerFeatures;
using RyanAssets.Shared.Declarations;
using UnityEngine;
using Universes.UniverseData.war_valley.Shared;

namespace Universes.UniverseData.war_valley.Server {
    /// <summary>
    /// The server half of a commander's personal squad: which troops they field, and how a
    /// finished one is assembled and enrolled. How many they may field is <see cref="WV_Limits"/>.
    /// <para>
    /// Troops are trained at a barracks, through the same queue a hangar builds tanks with, so this
    /// no longer takes requests from clients at all - <see cref="WV_ServerCommand"/> charges and
    /// queues the order, and <see cref="WV_ProductionBuilding"/> calls back here when the timer
    /// runs out. What is left is the part only the server assembly can do.
    /// </para>
    /// <para>
    /// Troops are the same character the waves are built from, so they are spawned through
    /// <see cref="ServerNPC"/> and then given the two server-only behaviours that make them a
    /// player's: <see cref="WV_NpcCombat"/> for the weapon and <see cref="WV_TroopBrain"/> for the
    /// orders. Nothing about that is serialized on the prefab, because the prefab also has to load
    /// in a client build.
    /// </para>
    /// </summary>
    public sealed class WV_ServerTroops : MonoBehaviour {
        public static WV_ServerTroops Instance { get; private set; }

        GameObject troopPrefab;

        readonly Dictionary<int, List<WV_TroopBrain>> squads = new();

        /// <summary>Raised when a troop a commander trained (not a free one) takes the field: the commander's client id and its kind.</summary>
        public event Action<int, WV_TroopKind> TroopDelivered;

        void Awake() {
            Instance = this;
        }

        void OnDestroy() {
            if (Instance == this)
                Instance = null;
        }

        /// <summary>Points the troop factory at the character prefab the wave script already uses.</summary>
        public void Initialize(GameObject prefab) {
            troopPrefab = prefab;
            if (prefab == null)
                Debug.LogError(
                    $"{nameof(WV_ServerTroops)} was given no troop prefab. Assign the runner's " +
                    "RobotNPC_Prefab list or no commander will be able to train troops.", this);
        }

        /// <summary>Living troops this commander currently fields.</summary>
        public int CountAlive(int clientId) {
            if (!squads.TryGetValue(clientId, out List<WV_TroopBrain> squad))
                return 0;

            for (int i = squad.Count - 1; i >= 0; i--) {
                WV_TroopBrain brain = squad[i];
                if (brain == null || brain.Character == null || brain.Character.IsDead)
                    squad.RemoveAt(i);
            }
            return squad.Count;
        }

        /// <summary>
        /// True while the commander's soldier limit has room for one more. The order was counted
        /// against the limit while it waited in the queue; by delivery it has left the queue, so the
        /// same check asks whether it may now take its place in the field.
        /// </summary>
        public bool HasRoomForTroop(int clientId) =>
            WV_Limits.HasRoom(clientId, WV_ForceCategory.Soldier, CountAlive(clientId));

        /// <summary>
        /// Delivers a troop a barracks has finished training to the commander who paid for it -
        /// the barracks' owner, or an ally who queued there. The order was paid for when it was
        /// queued, so the only question left is whether that commander still has room under their
        /// soldier limit; if not, the funds go back rather than the limit being quietly exceeded.
        /// </summary>
        public void TrainFromBuilding(WV_ProductionBuilding building, WV_TroopKind kind, int clientId) {
            if (building == null)
                return;

            if (troopPrefab == null || !HasRoomForTroop(clientId)) {
                Refund(clientId, kind);
                return;
            }

            // The troop fights under its commander's own team, in their colour - which is also how
            // that commander's client recognises it as one of theirs to select.
            TeamConfig team = WV_Permissions.GetCommanderTeam(clientId);

            WV_TroopBrain brain = SpawnTroop(building.SpawnPoint.position, team, kind, clientId);
            if (brain == null) {
                Refund(clientId, kind);
                return;
            }

            // A trained troop walks to the same gather point a produced vehicle does, so one rally
            // flag controls everything a building turns out.
            brain.OrderMove(building.RallyPoint);
            TroopDelivered?.Invoke(clientId, kind);
        }

        /// <summary>
        /// Gives a commander up to <paramref name="count"/> free troops of <paramref name="kind"/>
        /// beside <paramref name="center"/> - the Veteran Squad unlock - within their soldier limit.
        /// Returns how many took the field.
        /// </summary>
        public int DeployFreeTroops(int clientId, WV_TroopKind kind, int count, Vector3 center) {
            if (troopPrefab == null || clientId == WV_Owned.NoOwner)
                return 0;
            int deployed = 0;
            for (int i = 0; i < count && HasRoomForTroop(clientId); i++) {
                if (SpawnTroop(center, WV_Permissions.GetCommanderTeam(clientId), kind, clientId) != null)
                    deployed++;
            }
            return deployed;
        }

        /// <summary>
        /// Heals each living troop of a commander by <paramref name="fractionOfMax"/> of its maximum
        /// health, rounded up (Field Medics).
        /// </summary>
        public void HealSquad(int clientId, float fractionOfMax) {
            if (fractionOfMax <= 0f || !squads.TryGetValue(clientId, out List<WV_TroopBrain> squad))
                return;
            foreach (WV_TroopBrain brain in squad) {
                GameCharacter character = brain != null ? brain.Character : null;
                if (character == null || character.IsDead || character.IsFullHealth)
                    continue;
                long max = character.MaxHealth.Value;
                long heal = Math.Min(max - character.Health.Value, (long)Mathf.Ceil(max * fractionOfMax));
                if (heal > 0)
                    character.HealHealth(heal);
            }
        }

        /// <summary>
        /// A troop doctrine picked mid-round reaches the troops already on the field: their health grows by
        /// <paramref name="healthRatio"/> (the added share is healed, as a reinforced flag is) and their
        /// damage by <paramref name="damageRatio"/>. Every living troop of the commander is added to
        /// <paramref name="changed"/>, so the pick can be shown on them.
        /// </summary>
        public void Reinforce(int clientId, float healthRatio, float damageRatio, List<GameCharacter> changed) {
            if (!squads.TryGetValue(clientId, out List<WV_TroopBrain> squad))
                return;
            foreach (WV_TroopBrain brain in squad) {
                GameCharacter character = brain != null ? brain.Character : null;
                if (character == null || character.IsDead)
                    continue;
                changed?.Add(character);
                if (healthRatio <= 1.0001f && damageRatio <= 1.0001f)
                    continue;
                CharacterBuild build = character.Build;
                if (healthRatio > 1.0001f) {
                    long max = character.MaxHealth.Value;
                    long added = WV_CommanderModifiers.Scale(max, healthRatio) - max;
                    build.HealthMultiplier *= healthRatio;
                    if (added > 0)
                        character.HealMaxHealth(added);
                }
                if (damageRatio > 1.0001f)
                    build.DamageMultiplier *= damageRatio;
                // Written back without ApplyBuild, which would reset the health just raised.
                character.BuildSync.Value = build;
                if (damageRatio > 1.0001f && character.TryGetComponent(out WV_NpcCombat combat))
                    combat.RefreshDamage();
            }
        }

        /// <summary>Hands back what a troop cost when it was queued but could not be delivered.</summary>
        static void Refund(int clientId, WV_TroopKind kind) {
            WV_Economy economy = WV_Economy.Instance;
            if (economy != null)
                economy.Credit(clientId, WV_Rules.GetTroopCost(kind));
        }

        WV_TroopBrain SpawnTroop(Vector3 deployCenter, TeamConfig team, WV_TroopKind kind, int clientId) {
            // Scattered around the muster point so a queued pair does not arrive inside each other.
            Vector3 spawnPosition = ServerPathfinding.GetRandomPositionOnCircle(
                deployCenter, WV_Rules.TroopSpawnRadius);

            LocalNPC npc = ServerNPC.SpawnNPC(troopPrefab, location: spawnPosition);
            if (npc == null)
                return null;

            GameCharacter character = npc.GetComponent<GameCharacter>();
            if (character == null) {
                Debug.LogError($"{troopPrefab.name} has no {nameof(GameCharacter)} and cannot be a troop.", this);
                return null;
            }

            // The troop fights on its commander's team and, through the display half of that team,
            // carries their colour - the same colour their buildings and their own name already use.
            character.SetTeam(team ?? WV_Permissions.GetCommanderTeam(clientId));
            // A commander's troops walk through their side's gates, never through its walls.
            WV_NavAreas.Apply(npc.agent, character.GetTeam());
            WV_NavAreas.PlaceOnNavMesh(npc.agent, spawnPosition);
            // Named for its commander - "Player0's Skinny Legend" - so on a field shared with allies
            // everyone can tell whose soldier it is. The kind stays readable from the name's tail.
            character.DisplayName = WV_Rules.GetOwnedName(clientId, WV_Rules.GetTroopDisplayName(kind));
            // The kind's body - thin, big, short, or quick - and the health, speed, and damage that
            // come with it, applied through the character's own build setting.
            // The commander's doctrines (Hardened Infantry, Sharpshooters, Total War) toughen and
            // sharpen every troop they field from the moment it is picked.
            WV_CommanderModifiers bonuses = WV_CommanderBonuses.For(clientId);
            CharacterBuild build = WV_TroopCatalog.Get(kind).Build.OrStandard;
            build.HealthMultiplier *= bonuses.TroopHealthMultiplier;
            build.DamageMultiplier *= bonuses.TroopDamageMultiplier;
            character.ApplyBuild(build, WV_TroopCatalog.BaseHealth);

            // The robot body ships with a material variant per team colour, replicated by the
            // character itself, so a commander's squad is literally painted in their colour rather
            // than being identifiable only by the name over its head.
            RobotColor robotColor = character.GetComponent<RobotColor>();
            if (robotColor != null)
                robotColor.ApplyColor(WV_Rules.GetCommanderColor(clientId));

            WV_TroopBrain brain = WV_TroopBrain.Attach(character, kind, clientId);
            Register(clientId, brain);
            WV_Corpses.DespawnWhenDead(this, character);
            return brain;
        }

        void Register(int clientId, WV_TroopBrain brain) {
            if (!squads.TryGetValue(clientId, out List<WV_TroopBrain> squad)) {
                squad = new List<WV_TroopBrain>();
                squads[clientId] = squad;
            }
            squad.Add(brain);

            GameCharacter character = brain.Character;

            void HandleDied(DamageType source, IEntity attacker) {
                character.OnDied -= HandleDied;
                squad.Remove(brain);
                // A dead troop must not keep taking orders from a selection that outlived it.
                if (brain != null)
                    brain.enabled = false;
            }

            character.OnDied += HandleDied;
        }

        /// <summary>
        /// Resolves a troop the sender actually commands. Order requests name NetworkObject ids, so
        /// this is the point where a modified client is stopped from driving someone else's squad.
        /// </summary>
        public bool TryGetOwnedTroop(int clientId, int objectId, out WV_TroopBrain brain) {
            brain = null;
            if (objectId == 0
                || InstanceFinder.ServerManager == null
                || !InstanceFinder.ServerManager.Objects.Spawned.TryGetValue(objectId, out NetworkObject networkObject)
                || networkObject == null)
                return false;

            WV_TroopBrain candidate = networkObject.GetComponent<WV_TroopBrain>();
            if (candidate == null || !candidate.IsOwnedBy(clientId))
                return false;

            brain = candidate;
            return true;
        }
    }
}
