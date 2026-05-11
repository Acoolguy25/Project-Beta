using UnityEngine;
using FishNet.Managing;
public class DebugNetwork: MonoBehaviour
{
    private NetworkManager _networkManager;
    void Start()
    {
        _networkManager = FindAnyObjectByType<NetworkManager>();
        _networkManager.ServerManager.StartConnection();
        _networkManager.ClientManager.StartConnection();
    }
}