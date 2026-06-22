using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.AI;
using RyanAssets.Server.ServerFeatures;

namespace RyanAssets.Characters.Server {
    public enum NPCTargetingType {
        None,
        Random,
        Character
    }
    [RequireComponent(typeof(NavMeshAgent))]
    public class LocalNPC: MonoBehaviour {
        [SerializeField]
        private float RunSpeed = 3.5f;
        [SerializeField]
        public bool Running = false;
        
        public GameObject PreviousTarget;
        public Vector3? PreviousTargetVec;
        public NPCTargetingType TargetingType = NPCTargetingType.Random;
        
        public NavMeshAgent agent;
        void Start(){
            agent = GetComponent<NavMeshAgent>();
            agent.updateRotation = false;
            agent.updateUpAxis = false;
            agent.enabled = true;
        }
        public void Update() {
            if (!agent.pathPending)
            {
                if (agent.velocity.sqrMagnitude > 0.001f)
                {
                    Vector3 dir = new Vector3(agent.velocity.x, 0f, agent.velocity.z);
                    transform.rotation = Quaternion.RotateTowards(
                        transform.rotation,
                        Quaternion.LookRotation(dir),
                        agent.angularSpeed * Time.deltaTime
                    );

                }
                if (agent.remainingDistance <= agent.stoppingDistance && TargetingType == NPCTargetingType.Random)
                {
                    PreviousTarget = null;
                    PreviousTargetVec = null;
                    //TargetingType = NPCTargetingType.None;
                }
            }
            switch (TargetingType)
            {
                case NPCTargetingType.Random:
                    if (PreviousTargetVec == null)
                    {
                        PreviousTargetVec = ServerPathfinding.GetRandomPosition();
                        agent.SetDestination(PreviousTargetVec.Value);
                    }
                    break;
                case NPCTargetingType.Character:
                    ServerPathfinding.UpdateTarget(agent, ref PreviousTarget, "Player", 0f);
                    break;
                case NPCTargetingType.None:
                    agent.ResetPath();
                    break;
            }
            //agent.isStopped = agent.pathStatus == NavMeshPathStatus.PathInvalid;
        }
    }
}
