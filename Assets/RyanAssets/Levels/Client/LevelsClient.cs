using UnityEngine;
using RyanAssets.Levels.Shared;
using RyanAssets.DataService;
using System.Collections.Generic;
using UnityEngine.UI;
using TMPro;

namespace RyanAssets.Levels.Client {
    public class LevelsClient: MonoBehaviour {
        readonly static List<LevelsClient> Instances = new();
        static ulong xp;
        TextMeshProUGUI _level_text;
        void Awake(){
            Instances.Add(this);
        }
        void OnDestroy(){
            Instances.Remove(this);
        }
        void Start(){
            _level_text = GetComponent<TextMeshProUGUI>();
            UpdateLevel();
        }
        void UpdateLevel(){
            _level_text.text = LevelsCalc.GetRank(xp).ToString();
        }
        public static void UpdateLevelInstances(LocalPlayerData localPlayerData){
            xp = localPlayerData.xp;
            foreach (LevelsClient client in Instances){
                client.UpdateLevel();
            }
        }
    }
}