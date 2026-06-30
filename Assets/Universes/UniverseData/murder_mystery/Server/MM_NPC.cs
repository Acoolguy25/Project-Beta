using System.Collections;
using UnityEngine;
using UnityEngine.AI;
using RyanAssets.Server.ServerFeatures;
using System.Collections.Generic;
using RyanAssets.Server.ServerCore;
using FishNet.Connection;
using RyanAssets.Characters.Shared;
using RyanAssets.Characters.Server;

namespace Universes.murder_mystery.Server
{
    public class MM_NPC : MonoBehaviour
    {
        //Dictionary<NavMeshAgent, GameObject> PrevTargets = new();
        //void Update(){
        //    foreach (GameObject obj in GameObject.FindGameObjectsWithTag("NPC")){
        //        var agent = obj.GetComponent<NavMeshAgent>();
        //        agent.enabled = true;
        //        PrevTargets.TryGetValue(agent, out GameObject target);
        //        if (!ServerPathfinding.UpdateTarget(
        //            agent,
        //            ref target,
        //            "Player",
        //            0f
        //        )) {
        //            agent.SetDestination(ServerPathfinding.GetRandomPosition());
        //        }
        //        PrevTargets[agent] = target;
        //    }
        //}
        List<Transform> characters = new();
        private void Awake()
        {
            ServerPlayerCharacter.OnPlayerCharacterAdded += OnCharacterAdded;
            ServerPlayerCharacter.OnPlayerCharacterDied += OnCharacterDied;
        }
        private void OnCharacterAdded(NetworkConnection conn, LocalCharacter character)
        {
            characters.Add(character.transform);
            ApplyTargets();
        }
        private void OnCharacterDied(NetworkConnection conn, LocalCharacter character)
        {
            if (characters.Remove(character.transform)) // if it existed
                ApplyTargets();
        }
        private void ApplyTargets(){
            var char_arr = characters.ToArray();
            foreach (GameObject obj in GameObject.FindGameObjectsWithTag("NPC")){
                if (obj.TryGetComponent(out LocalNPC npc)){
                    npc.FleeTargets = char_arr;
                }
            }
        }
        public static void RefreshNPCSpeeds(){
            foreach (GameObject obj in GameObject.FindGameObjectsWithTag("NPC"))
            {
                if (obj.TryGetComponent(out LocalNPC npc))
                {
                    npc.UpdateSpeed();
                }
            }
        }
    }
}