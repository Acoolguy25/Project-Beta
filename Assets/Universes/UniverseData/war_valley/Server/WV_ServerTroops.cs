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
    /// The server half of a commander's personal squad: how many troops they may field, and how a
    /// finished one is assembled and enrolled.
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
        /// True when this commander is already fielding as many troops as the rules allow. Checked
        /// before the order is charged and queued, so a commander at their cap is told no at the
        /// button rather than paying for a soldier who cannot be delivered.
        /// </summary>
        public bool IsSquadFull(int clientId) => CountAlive(clientId) >= WV_Rules.MaxTroopsPerCommander;

        /// <summary>
        /// Delivers a troop a barracks has finished training. The order was paid for when it was
        /// queued, so the only question left is whether the squad still has room: a commander whose
        /// troops all survived while this one was in the oven has legitimately hit the cap, and the
        /// funds go back rather than the cap being quietly exceeded.
        /// </summary>
        public void TrainFromBuilding(WV_ProductionBuilding building, WV_TroopKind kind) {
            if (building == null || building.Owned == null)
                return;

            int clientId = building.Owned.OwnerClientId;
            if (troopPrefab == null || IsSquadFull(clientId)) {
                Refund(clientId, kind);
                return;
            }

            StructureComponent structure = building.GetComponent<StructureComponent>();
            TeamConfig team = structure != null && structure.Team != null
                ? structure.Team
                : new TeamConfig(TeamColor.Blue, WV_Rules.GetCommanderColor(clientId));

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
            character.SetTeam(team ?? new TeamConfig(TeamColor.Blue, WV_Rules.GetCommanderColor(clientId)));
            character.DisplayName = WV_Rules.GetTroopDisplayName(kind);

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
