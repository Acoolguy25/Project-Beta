using UnityEngine;
using RyanAssets.Levels.Shared;
using RyanAssets.DataService;
using System.Collections.Generic;
using UnityEngine.UI;
using TMPro;
using RyanAssets.TweenService.TweenComponents;

namespace RyanAssets.Levels.Client {
    public class LevelsClient: MonoBehaviour {
        //readonly static List<LevelsClient> Instances = new();
        //static ulong xp;
        [SerializeField]
        TextMeshProUGUI _level_text;
        [SerializeField]
        RectTransform _level_fill_graphic;
        //protected virtual void Start() {
        //    _level_text = GetComponent<TextMeshProUGUI>();
        //}
        //void Awake(){
        //    Instances.Add(this);
        //}
        //void OnDestroy(){
        //    Instances.Remove(this);
        //}


        public void UpdateLevel(ulong xp, bool instant = false){
            _level_text.text = LevelsCalc.GetRank(xp).ToString();
            TweenRectTransform.AnchorTween(_level_fill_graphic, instant? 0f: 1f, new Vector2(0, 0), new Vector2(1, LevelsCalc.GetRankProgress(xp)));
        }
        //public static void UpdateLevelInstances(LocalPlayerData localPlayerData){
        //    xp = localPlayerData.xp;
        //    foreach (LevelsClient client in Instances){
        //        client.UpdateLevel();
        //    }
        //}
    }
}