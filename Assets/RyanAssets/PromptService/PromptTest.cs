using System.Threading.Tasks;
using UnityEngine;

namespace RyanAssets.PromptService {
    public class PromptTest: MonoBehaviour{
        async void Start(){
            while (true){
                PromptButton resp = await PromptManager.Instance.PromptLocalUser("Hello", "This is a test", PromptId.Protected, PromptManager.ButtonPreset_OkOnly);
                Debug.Log("Resp: " + resp);
                await Task.Delay(2 * 1000);
            }
        }
    }
}