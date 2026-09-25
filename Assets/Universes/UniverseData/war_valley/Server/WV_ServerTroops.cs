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
            // Named for its commander - "Player0's Skinny Legend" - so on a field shared with allies
            // everyone can tell whose soldier it is. The kind stays readable from the name's tail.
            character.DisplayName = WV_Rules.GetOwnedName(clientId, WV_Rules.GetTroopDisplayName(kind));
            // The kind's body - thin, big, short, or quick - and the health, speed, and damage that
            // come with it, applied through the character's own build setting.
            character.ApplyBuild(WV_TroopCatalog.Get(kind).Build, WV_TroopCatalog.BaseHealth);

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
