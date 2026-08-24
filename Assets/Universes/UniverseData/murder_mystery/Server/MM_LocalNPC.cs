using RyanAssets.DataService;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using RyanAssets.Characters.Shared;
using RyanAssets.Characters.Server;
using RyanAssets.Shared.Global;
using RyanAssets.Shared.Declarations;
using RyanAssets.Server.ServerCore;
using RyanAssets.Tools.Shared;
using RyanAssets.Tools.Client;
using FishNet.Object;

namespace Universes.murder_mystery.Server {
    public class MM_LocalNPC : NetworkBehaviour {
        //[Header("Attack")]
        //[SerializeField] public List<TeamColor> TargetTeams = new List<TeamColor>();
        //[SerializeField] private float AttackEnterRadius = 6f;
        float unequipAttackTime = 1f;

        LocalNPC localNPC;
        GameCharacter gameCharacter;

        ToolBaseShared sharedWeapon;
        ToolBaseClient clientWeapon;
        float lastAttack = float.MinValue;
        void Awake() {
            gameCharacter = GetComponent<GameCharacter>();
            localNPC = GetComponent<LocalNPC>();
            localNPC.WalkSpeed = 18f;
            localNPC.FleeSpeed = 21f;
            localNPC.AttackSpeed = 21f;
            localNPC.AttackFunction = AttackFunction;
        }
        void Start() {
            base.OnStartServer();
            if (gameCharacter.GetTeam().team == TeamColor.Red) {
                sharedWeapon = ServerTool.Instance.SpawnTool(gameCharacter.NetworkObject, ToolEnum.Dagger);
                clientWeapon = sharedWeapon.GetComponent<ToolBaseClient>();
                localNPC.AttackDamageType = sharedWeapon.defaultDamageType;
                localNPC.SetTargetingType(NPCTargetingType.Attack);
            }
        }
        void AttackFunction(GameCharacter targetCharacter) {
            lastAttack = Time.time;
            gameCharacter.SwitchTool(sharedWeapon);
            clientWeapon.TryActivate(gameCharacter.transform.position);
        }
        void Update() {
            if (lastAttack + unequipAttackTime <= Time.time) {
                gameCharacter.SwitchTool(null);
            }
        }
    }
}
