using System;
using System.Collections;
using RyanAssets.Shared.Player;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

namespace RyanAssets.Client.ClientUI.Vote {
    public class ClientVoteOptionPrefab : MonoBehaviour {
        [SerializeField] RawImage image;
        [SerializeField] TextMeshProUGUI titleText;
        [SerializeField] TextMeshProUGUI descriptionText;
        [SerializeField] TextMeshProUGUI countText;
        [SerializeField] Button button;
        [SerializeField] Image background;

        Coroutine imageLoad;
        int optionId;

        public static ClientVoteOptionPrefab CreateTemplate(Transform parent) {
            GameObject go = new("VoteOptionPrefab", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement), typeof(ClientVoteOptionPrefab));
            go.transform.SetParent(parent, false);
            ClientVoteOptionPrefab row = go.GetComponent<ClientVoteOptionPrefab>();
            row.BuildLayout();
            go.SetActive(false);
            return row;
        }

        void Awake() {
            if (button == null || titleText == null)
                BuildLayout();
        }

        public void Bind(SharedVoteOption option, int totalVotes, bool selected, Action<int> onClick) {
            optionId = option.optionId;
            titleText.text = option.title;
            descriptionText.text = option.description;
            float percent = totalVotes > 0 ? option.count / (float)totalVotes : 0f;
            countText.text = $"{option.count}\n{Mathf.RoundToInt(percent * 100f)}%";
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() => onClick(optionId));
            SetSelected(selected);

            image.texture = null;
            image.color = new Color32(47, 57, 68, 255);
            if (!string.IsNullOrWhiteSpace(option.imageUrl))
                imageLoad = StartCoroutine(LoadImage(option.imageUrl));
        }

        public void SetSelected(bool selected) {
            background.color = selected ? new Color32(32, 118, 183, 210) : new Color32(20, 22, 26, 210);
        }

        public void Cleanup() {
            if (imageLoad != null)
                StopCoroutine(imageLoad);
            imageLoad = null;
        }

        IEnumerator LoadImage(string url) {
            using UnityWebRequest request = UnityWebRequestTexture.GetTexture(url);
            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
                yield break;

            image.texture = DownloadHandlerTexture.GetContent(request);
            image.color = Color.white;
            imageLoad = null;
        }

        void BuildLayout() {
            background = GetComponent<Image>() ?? gameObject.AddComponent<Image>();
            button = GetComponent<Button>() ?? gameObject.AddComponent<Button>();
            background.color = new Color32(20, 22, 26, 210);

            LayoutElement rootLayout = GetComponent<LayoutElement>() ?? gameObject.AddComponent<LayoutElement>();
            rootLayout.minHeight = 104f;

            HorizontalLayoutGroup rowLayout = GetComponent<HorizontalLayoutGroup>() ?? gameObject.AddComponent<HorizontalLayoutGroup>();
            rowLayout.padding = new RectOffset(8, 10, 8, 8);
            rowLayout.spacing = 10f;
            rowLayout.childControlWidth = true;
            rowLayout.childControlHeight = true;
            rowLayout.childForceExpandWidth = false;

            image = UiObject("Image", typeof(RawImage), typeof(LayoutElement)).GetComponent<RawImage>();
            image.color = new Color32(47, 57, 68, 255);
            LayoutElement imageLayout = image.GetComponent<LayoutElement>();
            imageLayout.preferredWidth = 86f;
            imageLayout.preferredHeight = 86f;
            image.transform.SetParent(transform, false);

            GameObject textColumn = UiObject("Text", typeof(VerticalLayoutGroup), typeof(LayoutElement));
            VerticalLayoutGroup textLayout = textColumn.GetComponent<VerticalLayoutGroup>();
            textLayout.spacing = 3f;
            textLayout.childForceExpandHeight = false;
            textColumn.GetComponent<LayoutElement>().flexibleWidth = 1f;
            textColumn.transform.SetParent(transform, false);

            titleText = CreateText("Title", 21, FontStyles.Bold);
            titleText.transform.SetParent(textColumn.transform, false);

            descriptionText = CreateText("Description", 15, FontStyles.Normal);
            descriptionText.color = new Color32(210, 218, 226, 255);
            descriptionText.transform.SetParent(textColumn.transform, false);

            countText = CreateText("Count", 18, FontStyles.Bold);
            countText.alignment = TextAlignmentOptions.MidlineRight;
            countText.GetComponent<LayoutElement>().preferredWidth = 64f;
            countText.transform.SetParent(transform, false);
        }

        TextMeshProUGUI CreateText(string name, int size, FontStyles style) {
            TextMeshProUGUI text = UiObject(name, typeof(TextMeshProUGUI), typeof(LayoutElement)).GetComponent<TextMeshProUGUI>();
            text.fontSize = size;
            text.fontStyle = style;
            text.color = Color.white;
            text.textWrappingMode = TextWrappingModes.Normal;
            text.overflowMode = TextOverflowModes.Ellipsis;
            return text;
        }

        GameObject UiObject(string name, params Type[] components) {
            GameObject go = new(name, typeof(RectTransform));
            int uiLayer = LayerMask.NameToLayer("UI");
            if (uiLayer >= 0)
                go.layer = uiLayer;
            foreach (Type component in components)
                if (component != typeof(RectTransform) && go.GetComponent(component) == null)
                    go.AddComponent(component);
            return go;
        }
    }
}
