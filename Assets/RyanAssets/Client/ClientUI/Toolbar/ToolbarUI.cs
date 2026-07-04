using UnityEngine;
using RyanAssets.Tools.Shared;
using RyanAssets.UI.ButtonGrid;
using System.Collections.Generic;
using RyanAssets.Input;
using RyanAssets.Characters.Client;
using TMPro;
using UnityEngine.UI;

namespace RyanAssets.Client.ClientUI.Toolbar
{
    public class ToolbarUI : ButtonGridUI<ToolBaseShared>
    {
        Dictionary<ToolBaseShared, Transform> toolBaseToItem = new();
        protected override void Start()
        {
            base.Start();
            ToolBaseShared.createEvent += OnToolCreated;
            ToolBaseShared.destroyEvent += OnToolRemoved;
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
            if (tool.IsOwner)
                toolBaseToItem.Remove(tool);
        }
        void OnAddPrefab(GameObject prefabClone, ToolBaseShared toolBase) {
            prefabClone.transform.GetChild(0).GetComponent<Image>().sprite = toolBase.toolImage;
            prefabClone.transform.GetChild(1).GetComponent<TextMeshProUGUI>().text = prefabClone.transform.GetSiblingIndex().ToString();
            toolBaseToItem.Add(toolBase, prefabClone.transform);
            toolBase.equippedEvent += (ToolBaseShared _) => {
                prefabClone.GetComponent<Image>().color = Color.green;
            };
            toolBase.unequippedEvent += (ToolBaseShared _) => {
                prefabClone.GetComponent<Image>().color = Color.gray;
            };
        }
        void OnPrefabClicked(GameObject prefabClone, ToolBaseShared toolBase) {
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
