using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using RyanAssets.NetworkService;
using RyanAssets.Prompt;
using PlasticPipe.PlasticProtocol.Messages;
using System.Threading.Tasks;

namespace RyanAssets.DataService {
    public static class LocalPlayerData {        
        public static PlayerData localData;
        public static PlayerSettings localSettings;
        public static void PlayerInit(JObject json){
            localData.username = (string) json["username"];
            localData.xp       = (ulong)  json["xp"];
            localData.gold     = (ulong)  json["gold"];
            localSettings      = (json.TryGetValue("preferences", out JToken preferences) && (string) preferences != null)
                ? preferences.ToObject<PlayerSettings>()
                : default;
        }
        static JObject pending_data;
        static async Task<(string, JObject)> ModifyUsernameNetworkRequest(){
            return await ServerNetwork.PostRequest("/api/players/v1/me/username", pending_data);
        }
        public static async void ModifyUsername(string username){
            pending_data = new JObject(
                new JProperty("username", username)
            );
            (string res, JObject json) = await ServerNetwork.RequestAsync(ModifyUsernameNetworkRequest, "Modify Username", promptWaiting: PromptId.UsernameChangeAwait, promptResult: PromptId.UsernameResponse);
            if (res == null)
                localData.username = username;
        }
    }
}