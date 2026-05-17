using UnityEngine;
using FishNet.Object;
using UnityEngine.AI;

public class BasicZombie : MonoBehaviour
{
    private NavMeshAgent agent;
    public Transform player;
    void Start(){
        agent = GetComponent<NavMeshAgent>();
    }
    // public void ServerTick(float deltaTime){
        
    // }
    void Update(){
        if (player)
            agent.SetDestination(player.position);
    }
}
