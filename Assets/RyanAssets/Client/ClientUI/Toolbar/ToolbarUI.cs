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
using RyanAssets.UI;
using RyanAssets.TweenService;
using RyanAssets.Characters.Shared;
using RyanAssets.Shared.Declarations;
using FishNet.Object;

namespace RyanAssets.Client.ClientUI.Toolbar
{
    public class ToolbarUI : ButtonGridUI<ToolBaseShared>
    {
        [SerializeField]
        public float SwitchToolDelay = 0.01f;
        float LastSwitchToolTime = float.MinValue;
        Dictionary<ToolBaseShared, Transform> toolBaseToItem = new();
        List<ToolBaseShared> orderedTools = new();
        ToolBaseShared equippedToolShared;

        [SerializeField]
        CanvasGroupController toolUI, weaponAmmoUI;
        TextMeshProUGUI weaponTitleText;
        TextMeshProUGUI currentAmmoText, maxAmmoText;
        GameObject maxAmmoFinite, maxAmmoInfinte;
        protected override void Start()
        {
            base.Start();
            weaponTitleText = weaponAmmoUI.transform.GetChild(0).GetComponent<TextMeshProUGUI>();
            currentAmmoText = weaponAmmoUI.transform.GetChild(1).GetComponent<TextMeshProUGUI>();
            maxAmmoText = weaponAmmoUI.transform.GetChild(3).GetComponent<TextMeshProUGUI>();
            maxAmmoFinite = weaponAmmoUI.transform.GetChild(3).gameObject;
            maxAmmoInfinte = weaponAmmoUI.transform.GetChild(4).gameObject;

            ToolBaseShared.createStaticEvent += OnToolCreated;
            ToolBaseShared.destroyStaticEvent += OnToolRemoved;
            OnCreatePrefab += OnAddPrefab;
            OnClickPrefab += OnPrefabClicked;
            ToolControls.toolBarHotkeyPressed += OnActivateToolPressed;
            LocalPlayer.OnCharacterAdded.Subscribe(OnCharacterAdded);
            if (LocalPlayer.Character != null) {
                foreach (ToolBaseShared tool in LocalPlayer.Character.GetComponentsInChildren<ToolBaseShared>(true)) {
                    OnToolCreated(tool);
                }
            }
        }
        protected override void OnDestroy() {
            base.OnDestroy();
            LocalPlayer.OnCharacterRemoved -= OnCharacterAdded;
        }
        void OnCharacterAdded(LocalCharacter character) {
            toolUI.SetVisible(true);
            character.OnDied += OnCharacterDied;
        }
        void OnCharacterDied(DamageSource damageSource, NetworkObject networkObject) {
            toolUI.SetVisible(false);
        }
        void OnToolCreated(ToolBaseShared tool) {
            if (tool.IsOwner) {
                orderedTools.Add(tool);
                AddPrefab(tool, orderedTools.Count - 1);
            }
        }
        void OnToolRemoved(ToolBaseShared tool) {
            if (toolBaseToItem.TryGetValue(tool, out Transform prefabClone) && prefabClone != null) {
                RemovePrefab(prefabClone);
                toolBaseToItem.Remove(tool);
            }
            orderedTools.Remove(tool);
        }
        void OnAddPrefab(GameObject prefabClone, ToolBaseShared toolBase) {
            // ListGrid creates entries asynchronously. The tool can be despawned before
            // its creation callback runs, leaving this managed reference pointing to a
            // destroyed Unity object.
            if (toolBase == null || !orderedTools.Contains(toolBase)) {
                RemovePrefab(prefabClone.transform);
                return;
            }

            ToolBaseClient baseClient = toolBase.GetComponent<ToolBaseClient>();
            UnityEngine.Assertions.Assert.IsNotNull(baseClient, $"ToolBaseShared {toolBase.name} does not have a ToolBaseClient component.");
            Image backingImage = prefabClone.GetComponent<Image>();
            prefabClone.transform.GetChild(0).GetComponent<Image>().sprite = toolBase.toolImage;
            Image sliderImage = prefabClone.transform.GetChild(1).GetComponent<Image>();
            prefabClone.transform.GetChild(2).GetComponent<TextMeshProUGUI>().text = (orderedTools.FindIndex(t => t == toolBase) + 1).ToString();
            toolBaseToItem.Add(toolBase, prefabClone.transform);
            toolBase.equippedEvent += (ToolBaseShared _) => {
                //prefabClone.GetComponent<Image>().color = new Color32(60, 81, 161, 219);
                TweenImage.ColorImage(backingImage, 0.15f, new Color32(60, 81, 161, 219));
                equippedToolShared = toolBase;

                weaponTitleText.text = toolBase.toolName;
                RefreshCurrentAmmoUI();
                RefreshMaxAmmoUI();
            };
            toolBase.unequippedEvent += (ToolBaseShared _) => {
                //prefabClone.GetComponent<Image>().color = new Color32(13, 14, 15, 219);
                TweenImage.ColorImage(backingImage, 0.15f, new Color32(13, 14, 15, 219));
                equippedToolShared = null;
                RefreshCurrentAmmoUI();
                RefreshMaxAmmoUI();
            };
            toolBase.currentAmmoEvent += (int ammo) => RefreshCurrentAmmoUI();
            toolBase.maxAmmoEvent += (int ammo) => RefreshMaxAmmoUI();
            baseClient.onCooldownChangeEvent += (float start, float stop) => {
                if (sliderImage == null)
                    return;
                TweenManager.Instance.ClearTweens(sliderImage.rectTransform);
                sliderImage.rectTransform.anchorMax = new Vector2(1, 1);
                TweenRectTransform.AnchorTween(sliderImage.rectTransform, stop - start, new Vector2(0f, 0f), new Vector2(1f, 0f));
            };
            sliderImage.rectTransform.anchorMax = new Vector2(1, 0);
        }
        private const string LeadingZeroColor = "#808080"; // Grey

        private static string FormatAmmo(int ammo) {
            ammo = Mathf.Clamp(ammo, 0, 999);

            string text = (ammo == 0)? "": ammo.ToString();
            int leadingZeros = 3 - text.Length;

            if (leadingZeros <= 0)
                return text;

            return $"<color={LeadingZeroColor}>{new string('0', leadingZeros)}</color>{text}";
        }
        void RefreshCurrentAmmoUI() {
            if (weaponAmmoUI)
                weaponAmmoUI.SetVisible(equippedToolShared != null && equippedToolShared.currentAmmo >= 0);
            if (equippedToolShared)
                currentAmmoText.text = FormatAmmo(equippedToolShared.currentAmmo);

        }
        void RefreshMaxAmmoUI() {
            if (equippedToolShared) {
                maxAmmoText.text = FormatAmmo(equippedToolShared.currentStoredAmmo);
                maxAmmoFinite.SetActive(equippedToolShared.currentStoredAmmo >= 0);
                maxAmmoInfinte.SetActive(equippedToolShared.currentStoredAmmo < 0);
            }
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
