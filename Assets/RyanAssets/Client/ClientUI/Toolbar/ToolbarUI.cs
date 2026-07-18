using UnityEngine;
using RyanAssets.Tools.Shared;
using RyanAssets.UI.ButtonGrid;
using System.Collections.Generic;
using RyanAssets.Input;
using RyanAssets.Characters.Client;
using TMPro;
using UnityEngine.UI;
using RyanAssets.TweenService.TweenComponents;
using RyanAssets.Tools.Client;

namespace RyanAssets.Client.ClientUI.Toolbar
{
    public class ToolbarUI : ButtonGridUI<ToolBaseShared>
    {
        [SerializeField]
        public float SwitchToolDelay = 0.1f;
        float LastSwitchToolTime = float.MinValue;
        Dictionary<ToolBaseShared, Transform> toolBaseToItem = new();
        protected override void Start()
        {
            base.Start();
            ToolBaseShared.createStaticEvent += OnToolCreated;
            ToolBaseShared.destroyStaticEvent += OnToolRemoved;
            OnCreatePrefab += OnAddPrefab;
            OnClickPrefab += OnPrefabClicked;
            ToolControls.toolBarHotkeyPressed += OnActivateToolPressed;
            if (LocalPlayer.Character != null) {
                foreach (ToolBaseShared tool in LocalPlayer.Character.GetComponentsInChildren<ToolBaseShared>(true)) {
                    OnToolCreated(tool);
                }
            }
        }
        void OnToolCreated(ToolBaseShared tool) {
            if (tool.IsOwner) {
                AddPrefab(tool);
            }
        }
        void OnToolRemoved(ToolBaseShared tool) {
            if (toolBaseToItem.TryGetValue(tool, out Transform prefabClone) && prefabClone != null) {
                RemovePrefab(prefabClone);
                toolBaseToItem.Remove(tool);
            }
        }
        void OnAddPrefab(GameObject prefabClone, ToolBaseShared toolBase) {
            ToolBaseClient baseClient = toolBase.GetComponent<ToolBaseClient>();
            UnityEngine.Assertions.Assert.IsNotNull(baseClient, $"ToolBaseShared {toolBase.name} does not have a ToolBaseClient component.");
            Image backingImage = prefabClone.GetComponent<Image>();
            prefabClone.transform.GetChild(0).GetComponent<Image>().sprite = toolBase.toolImage;
            Image sliderImage = prefabClone.transform.GetChild(1).GetComponent<Image>();
            prefabClone.transform.GetChild(2).GetComponent<TextMeshProUGUI>().text = (prefabOrder[prefabClone.transform] + 1).ToString();
            toolBaseToItem.Add(toolBase, prefabClone.transform);
            toolBase.equippedEvent += (ToolBaseShared _) => {
                //prefabClone.GetComponent<Image>().color = new Color32(60, 81, 161, 219);
                TweenImage.ColorImage(backingImage, 0.15f, new Color32(60, 81, 161, 219));
            };
            toolBase.unequippedEvent += (ToolBaseShared _) => {
                //prefabClone.GetComponent<Image>().color = new Color32(13, 14, 15, 219);
                TweenImage.ColorImage(backingImage, 0.15f, new Color32(13, 14, 15, 219));
            };
            baseClient.onCooldownChangeEvent += (float start, float stop) => {
                sliderImage.rectTransform.anchorMax = new Vector2(1, 1);
                TweenRectTransform.AnchorTween(sliderImage.rectTransform, stop - start, new Vector2(0f, 0f), new Vector2(1f, 0f));
            };
            sliderImage.rectTransform.anchorMax = new Vector2(1, 0);
        }
        void OnPrefabClicked(GameObject prefabClone, ToolBaseShared toolBase) {
            if (LastSwitchToolTime + SwitchToolDelay > Time.time)
                return;
            LastSwitchToolTime = Time.time;
            if (LocalPlayer.Character.Equipped(toolBase))
                LocalPlayer.Character.SwitchTool(null);
            else
                LocalPlayer.Character.SwitchTool(toolBase);
        }
        void OnActivateToolPressed(int toolNumber) {
            if (toolNumber < 0 || toolNumber > toolBaseToItem.Count)
                return;
            Transform target = contentTarget.transform.GetChild(toolNumber - 1);
            target.GetComponent<Button>().onClick.Invoke(); // fake a click
        }
    }
}
