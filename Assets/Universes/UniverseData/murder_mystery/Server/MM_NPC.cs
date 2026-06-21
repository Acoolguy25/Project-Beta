using System.Collections;
using UnityEngine;
using UnityEngine.AI;
using RyanAssets.Server.ServerFeatures;
using System.Collections.Generic;

namespace Universes.murder_mystery.Server
{
    public class MM_NPC : MonoBehaviour
    {
        Dictionary<NavMeshAgent, GameObject> PrevTargets = new();
        void Update(){
            foreach (GameObject obj in GameObject.FindGameObjectsWithTag("NPC")){
                var agent = obj.GetComponent<NavMeshAgent>();
                agent.enabled = true;
                PrevTargets.TryGetValue(agent, out GameObject target);
                ServerPathfindingHelper.UpdateTarget(
                    agent,
                    ref target,
                    "Player",
                    0f
                );
                PrevTargets[agent] = target;
            }
        }
    }
}