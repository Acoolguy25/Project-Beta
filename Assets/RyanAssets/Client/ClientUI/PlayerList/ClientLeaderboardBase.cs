using NUnit.Framework;
using RyanAssets.DataService;
using RyanAssets.Shared.Declarations;
using RyanAssets.UI.ListGrid;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace RyanAssets.Client.ClientUI.PlayerList {
    public class ClientLeaderboardBase : ListGridUI<int> {
        [SerializeField]
        protected Text playerNameText;
        [SerializeField]
        HorizontalLayoutGroup leaderboardParent;
        //[SerializeField]
        //GameObject leaderboardItemPrefab;
        protected override void Start() {
            base.Start();
            OnCreatePrefab += (prefab, idx) => {
                playerNameText.rectTransform.offsetMax = new Vector2(-leaderboardParent.preferredWidth - 8f, 0);
                prefab.name = $"LeaderboardItem_{idx}";
            };
        }
        public void BuildLeaderboard(int items) {
            globalOrder = 0;
            //RectTransform itemRT = leaderboardItemPrefab.GetComponent<RectTransform>();

            //foreach (Transform item in leaderboardParent.transform) {
            //    Destroy(item.gameObject);
            //}
            //for (int i = 0; i < items; i++) {
            //    GameObject leaderboardItem = Instantiate(leaderboardItemPrefab);
            //    leaderboardItem.transform.SetParent(leaderboardParent.transform, true);
            //    leaderboardItem.name = $"LeaderboardItem_{i}";
            //}
            int[] ints = new int[items];
            for (int i = 0; i < items; i++) {
                ints[i] = i;
            }
            RefreshPrefabs(ints);
        }
        Text GetTextElement(GameObject prefab) {
            return prefab.GetComponent<Text>();
        }
        public void SetLeaderboardItem(GameObject index, int value) {
            GetTextElement(index).text = value.ToString("N0");
        }
        public void SetLeaderboardItem(GameObject index, string value) {
            GetTextElement(index).text = value;
        }
    }
}