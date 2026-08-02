//using FishNet.Connection;
//using RyanAssets.Characters.Server;
//using RyanAssets.Characters.Shared;
//using RyanAssets.Server.ServerCore;
//using RyanAssets.Server.ServerFeatures;
//using System.Collections;
//using System.Collections.Generic;
//using UnityEngine;
//using UnityEngine.AI;
//using UnityEngine.TextCore.Text;

//namespace Universes.murder_mystery.Server
//{
//    public class MM_NPC : MonoBehaviour
//    {
//        //Dictionary<NavMeshAgent, GameObject> PrevTargets = new();
//        //void Update(){
//        //    foreach (GameObject obj in GameObject.FindGameObjectsWithTag("NPC")){
//        //        var agent = obj.GetComponent<NavMeshAgent>();
//        //        agent.enabled = true;
//        //        PrevTargets.TryGetValue(agent, out GameObject target);
//        //        if (!ServerPathfinding.UpdateTarget(
//        //            agent,
//        //            ref target,
//        //            "Player",
//        //            0f
//        //        )) {
//        //            agent.SetDestination(ServerPathfinding.GetRandomPosition());
//        //        }
//        //        PrevTargets[agent] = target;
//        //    }
//        //}
//        public static List<Transform> characters;
//        private void Awake()
//        {
//            characters = new();
//            ServerPlayerCharacter.OnPlayerCharacterAdded += OnCharacterAdded;
//            ServerPlayerCharacter.OnPlayerCharacterDied += OnCharacterDied;
//        }
//        private void OnCharacterAdded(NetworkConnection conn, LocalCharacter character)
//        {
//            characters.Add(character.transform);
//            ApplyTargets();
//        }
//        private void OnCharacterDied(NetworkConnection conn, LocalCharacter character)
//        {
//            if (characters.Remove(character.transform)) // if it existed
//                ApplyTargets();
//        }
//        private void ApplyTargets(){
//            var char_arr = characters.ToArray();
//            foreach (GameObject obj in GameObject.FindGameObjectsWithTag("NPC")){
//                if (obj.TryGetComponent(out LocalNPC npc)){
//                    npc.FleeTargets = char_arr;
//                }
//            }
//        }

//    }
//}