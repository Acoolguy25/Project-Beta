using UnityEngine;

namespace RyanAssets.Client.ClientCore {
    public class ClientInit: MonoBehaviour {
        [SerializeField]
        GameObject ClientPrefab;
        void Start(){
            if (ClientConnector.Instance == null)
                Instantiate(ClientPrefab);
            Destroy(gameObject);
        }
    }
}